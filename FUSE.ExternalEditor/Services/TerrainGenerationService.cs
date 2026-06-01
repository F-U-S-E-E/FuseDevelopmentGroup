using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Fuse.ExternalEditor.Logic.Generation;
using Fuse.ExternalEditor.Models.Terrain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fuse.ExternalEditor.Services;

/// <summary>
/// Async terrain generator ported from <c>edit_tiles/generate.py</c>: fetches Mapbox
/// terrain-RGB tiles, mosaics + decodes + bilinearly resamples them to the tile height
/// grid (with the east-west elevation ramp), optionally fetches NLCD land cover for
/// veg/water, and packs to a <see cref="TerrainTile"/>. Region generation uses a bounded
/// pool of workers over a <see cref="Channel{T}"/>. HTTP is injectable for testing.
/// </summary>
public sealed class TerrainGenerationService : ITerrainGenerationService
{
    private const int NlcdGutter = 48;
    private readonly HttpClient _http;
    private readonly ITerrainTileService _tiles;

    public TerrainGenerationService(HttpClient http, ITerrainTileService tiles)
    {
        _http = http;
        _tiles = tiles;
    }

    public async Task<TerrainTile> GenerateTileAsync(int gx, int gy, string token, TerrainGenOptions options, CancellationToken ct = default)
    {
        token = MapboxTerrain.CleanToken(token);
        if (string.IsNullOrEmpty(token))
        {
            throw new MapboxAuthException("Mapbox token missing");
        }

        var res = GenerationConstants.HeightRes;
        var heights = await SampleHeightsAsync(gx, gy, token, ct).ConfigureAwait(false);

        byte[]? veg = null;
        bool[]? water = null;
        if (options.VegOverride is { } ov)
        {
            veg = new byte[res * res];
            Array.Fill(veg, (byte)(ov & 0x7));
            water = new bool[res * res];
        }
        else if (options.UseNlcd)
        {
            try
            {
                (veg, water) = await VegWaterAsync(gx, gy, options.NlcdBlur, ct).ConfigureAwait(false);
            }
            catch (MapboxAuthException)
            {
                throw;
            }
            catch
            {
                veg = null; // NLCD failure → veg 0 (matches the reference's fallback)
                water = null;
            }
        }

        var r = new byte[res * res];
        var g = new byte[res * res];
        var a = new byte[res * res];
        for (var i = 0; i < res * res; i++)
        {
            var (pr, pg, pa) = MapboxTerrain.PackPixel(heights[i], veg?[i] ?? 0, water?[i] ?? false);
            r[i] = pr;
            g[i] = pg;
            a[i] = pa;
        }

        var path = options.OutputDir is null ? null : Path.Combine(options.OutputDir, MapboxTerrain.TileDataFilename(gx, gy));
        var tile = new TerrainTile(gx, gy, res, r, g, a, path);
        if (path is not null)
        {
            _tiles.SaveTile(tile, path);
        }

        return tile;
    }

    public async Task<int> GenerateRegionAsync(
        IReadOnlyList<(int Gx, int Gy)> tiles, string token, TerrainGenOptions options,
        IProgress<TerrainGenProgress>? progress = null, CancellationToken ct = default)
    {
        var total = tiles.Count;
        var completed = 0;
        var channel = Channel.CreateUnbounded<(int Gx, int Gy)>();
        foreach (var t in tiles)
        {
            channel.Writer.TryWrite(t);
        }

        channel.Writer.Complete();

        var workers = Math.Max(1, options.MaxConcurrency);
        var tasks = new List<Task>(workers);
        for (var w = 0; w < workers; w++)
        {
            tasks.Add(Task.Run(
                async () =>
                {
                    await foreach (var (gx, gy) in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                    {
                        await GenerateTileAsync(gx, gy, token, options, ct).ConfigureAwait(false);
                        var done = Interlocked.Increment(ref completed);
                        progress?.Report(new TerrainGenProgress(done, total, MapboxTerrain.TileDataFilename(gx, gy)));
                    }
                },
                ct));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return completed;
    }

    private async Task<double[]> SampleHeightsAsync(int gx, int gy, string token, CancellationToken ct)
    {
        var (mn, mx) = MapboxTerrain.TileBounds(gx, gy);
        var z = GenerationConstants.MapboxZoom;
        double lx = MapboxTerrain.LonToPixelX(mn.Lon, z), rx = MapboxTerrain.LonToPixelX(mx.Lon, z);
        double tpy = MapboxTerrain.LatToPixelY(mx.Lat, z), bpy = MapboxTerrain.LatToPixelY(mn.Lat, z);

        var (src, h, w, ox, oy) = await MosaicAsync(lx, tpy, rx, bpy, token, ct).ConfigureAwait(false);

        var res = GenerationConstants.HeightRes;
        var srcX = new double[res];
        var srcY = new double[res];
        for (var i = 0; i < res; i++)
        {
            srcX[i] = Math.Clamp((lx - ox) + (i * (rx - lx) / (res - 1)), 0, w - 1.000001);
            srcY[i] = Math.Clamp((tpy - oy) + (i * (bpy - tpy) / (res - 1)), 0, h - 1.000001);
        }

        var output = new double[res * res];
        for (var c = 0; c < res; c++)
        {
            var t = Math.Clamp(
                (gx + ((double)c / (res - 1)) - GenerationConstants.OffsetEastX)
                / (GenerationConstants.OffsetWestX - GenerationConstants.OffsetEastX),
                0.0, 1.0);
            var ramp = t * GenerationConstants.OffsetMaxM;
            for (var r = 0; r < res; r++)
            {
                output[(r * res) + c] = Resample.Bilinear(src, h, w, srcY[r], srcX[c]) + ramp;
            }
        }

        return output;
    }

    private async Task<(float[] Src, int H, int W, int Ox, int Oy)> MosaicAsync(double lx, double tpy, double rx, double bpy, string token, CancellationToken ct)
    {
        var sz = GenerationConstants.MapboxTileSize;
        int tx0 = (int)Math.Floor(lx) / sz, ty0 = (int)Math.Floor(tpy) / sz;
        int tx1 = (int)Math.Ceiling(rx) / sz, ty1 = (int)Math.Ceiling(bpy) / sz;
        int w = (tx1 - tx0 + 1) * sz, h = (ty1 - ty0 + 1) * sz;
        var src = new float[h * w];

        for (var ty = ty0; ty <= ty1; ty++)
        {
            for (var tx = tx0; tx <= tx1; tx++)
            {
                var url = $"https://api.mapbox.com/v4/mapbox.terrain-rgb/{GenerationConstants.MapboxZoom}/{tx}/{ty}.pngraw?access_token={token}";
                var (rgb, iw, ih) = await FetchRgbAsync(url, ct).ConfigureAwait(false);
                int px = (tx - tx0) * sz, py = (ty - ty0) * sz;
                for (var yy = 0; yy < Math.Min(sz, ih); yy++)
                {
                    for (var xx = 0; xx < Math.Min(sz, iw); xx++)
                    {
                        var si = ((yy * iw) + xx) * 3;
                        src[((py + yy) * w) + px + xx] = (float)MapboxTerrain.DecodeElevation(rgb[si], rgb[si + 1], rgb[si + 2]);
                    }
                }
            }
        }

        return (src, h, w, tx0 * sz, ty0 * sz);
    }

    private async Task<(byte[] Veg, bool[] Water)> VegWaterAsync(int gx, int gy, double blur, CancellationToken ct)
    {
        var (mn, mx) = MapboxTerrain.TileBounds(gx, gy);
        var outRes = GenerationConstants.HeightRes;
        var fetchRes = outRes + (2 * NlcdGutter);
        var url = $"{GenerationConstants.NlcdUrl}?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=mrlc_display:NLCD_2021_Land_Cover_L48"
                  + $"&BBOX={mn.Lon},{mn.Lat},{mx.Lon},{mx.Lat}&WIDTH={fetchRes}&HEIGHT={fetchRes}&SRS=EPSG:4326&FORMAT=image/png&STYLES=";
        var (rgb, iw, ih) = await FetchRgbAsync(url, ct).ConfigureAwait(false);
        if (iw != fetchRes || ih != fetchRes)
        {
            rgb = ResizeNearest(rgb, iw, ih, fetchRes, fetchRes);
        }

        return NlcdLandcover.BuildVegWater(rgb, fetchRes, outRes, NlcdGutter, blur);
    }

    private async Task<(byte[] Rgb, int W, int H)> FetchRgbAsync(string url, CancellationToken ct)
    {
        var bytes = await FetchBytesAsync(url, ct).ConfigureAwait(false);
        using var ms = new MemoryStream(bytes);
        using var image = Image.Load<Rgb24>(ms);
        int w = image.Width, h = image.Height;
        var rgb = new byte[w * h * 3];
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < w; x++)
                {
                    var p = row[x];
                    var i = ((y * w) + x) * 3;
                    rgb[i] = p.R;
                    rgb[i + 1] = p.G;
                    rgb[i + 2] = p.B;
                }
            }
        });

        return (rgb, w, h);
    }

    private async Task<byte[]> FetchBytesAsync(string url, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    throw new MapboxAuthException("Mapbox token unauthorized; re-paste it to remove hidden characters");
                }

                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            }
            catch (MapboxAuthException)
            {
                throw;
            }
            catch (Exception e) when (attempt < 2)
            {
                last = e;
                await Task.Delay(200 * (attempt + 1), ct).ConfigureAwait(false);
            }
        }

        throw last ?? new IOException("fetch failed");
    }

    private static byte[] ResizeNearest(byte[] rgb, int srcW, int srcH, int dstW, int dstH)
    {
        var output = new byte[dstW * dstH * 3];
        for (var y = 0; y < dstH; y++)
        {
            var sy = Math.Min(srcH - 1, y * srcH / dstH);
            for (var x = 0; x < dstW; x++)
            {
                var sx = Math.Min(srcW - 1, x * srcW / dstW);
                var si = ((sy * srcW) + sx) * 3;
                var di = ((y * dstW) + x) * 3;
                output[di] = rgb[si];
                output[di + 1] = rgb[si + 1];
                output[di + 2] = rgb[si + 2];
            }
        }

        return output;
    }
}
