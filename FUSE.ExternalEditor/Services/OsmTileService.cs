using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Fuse.ExternalEditor.Logic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fuse.ExternalEditor.Services;

/// <summary>
/// Fetches OSM raster tiles for a lat/lon box (slippy math from <see cref="OsmTileMath"/>),
/// stitches them into one RGBA mosaic, and reports the mosaic's geo bounds so the viewport
/// can align it to the world tiles. Sends a User-Agent per OSM tile-usage policy. HTTP is
/// injectable for testing.
/// </summary>
public sealed class OsmTileService : IOsmTileService
{
    private const int TileSize = 256;
    private const int MaxTiles = 256; // guard against runaway requests
    private readonly HttpClient _http;

    public OsmTileService(HttpClient http) => _http = http;

    public async Task<OsmMosaic> FetchAsync(double minLat, double minLon, double maxLat, double maxLon, int zoom, CancellationToken ct = default)
    {
        // NW corner = (maxLat, minLon); SE corner = (minLat, maxLon).
        var (x0, y0) = OsmTileMath.Deg2Tile(maxLat, minLon, zoom);
        var (x1, y1) = OsmTileMath.Deg2Tile(minLat, maxLon, zoom);
        if (x1 < x0)
        {
            (x0, x1) = (x1, x0);
        }

        if (y1 < y0)
        {
            (y0, y1) = (y1, y0);
        }

        int cols = x1 - x0 + 1, rows = y1 - y0 + 1;
        if ((long)cols * rows > MaxTiles)
        {
            throw new InvalidOperationException($"OSM region too large: {cols}x{rows} tiles (max {MaxTiles}). Zoom out or shrink the box.");
        }

        int w = cols * TileSize, h = rows * TileSize;
        var rgba = new byte[w * h * 4];
        var count = 0;
        for (var ty = y0; ty <= y1; ty++)
        {
            for (var tx = x0; tx <= x1; tx++)
            {
                var url = $"https://tile.openstreetmap.org/{zoom}/{tx}/{ty}.png";
                var tile = await FetchTileAsync(url, ct).ConfigureAwait(false);
                Blit(tile, rgba, w, (tx - x0) * TileSize, (ty - y0) * TileSize);
                count++;
            }
        }

        var nw = OsmTileMath.Tile2Deg(x0, y0, zoom);
        var se = OsmTileMath.Tile2Deg(x1 + 1, y1 + 1, zoom);
        return new OsmMosaic(rgba, w, h, count, nw.Lat, nw.Lon, se.Lat, se.Lon);
    }

    private async Task<byte[]> FetchTileAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "FUSE.ExternalEditor (https://github.com/)");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

        using var ms = new MemoryStream(bytes);
        using var image = Image.Load<Rgba32>(ms);
        var tile = new byte[TileSize * TileSize * 4];
        image.ProcessPixelRows(accessor =>
        {
            var rows = Math.Min(TileSize, accessor.Height);
            for (var y = 0; y < rows; y++)
            {
                var row = accessor.GetRowSpan(y);
                var cols = Math.Min(TileSize, row.Length);
                for (var x = 0; x < cols; x++)
                {
                    var p = row[x];
                    var i = ((y * TileSize) + x) * 4;
                    tile[i] = p.R;
                    tile[i + 1] = p.G;
                    tile[i + 2] = p.B;
                    tile[i + 3] = p.A;
                }
            }
        });
        return tile;
    }

    private static void Blit(byte[] tile, byte[] dst, int dstW, int dx, int dy)
    {
        for (var y = 0; y < TileSize; y++)
        {
            for (var x = 0; x < TileSize; x++)
            {
                var s = ((y * TileSize) + x) * 4;
                var d = (((dy + y) * dstW) + dx + x) * 4;
                dst[d] = tile[s];
                dst[d + 1] = tile[s + 1];
                dst[d + 2] = tile[s + 2];
                dst[d + 3] = tile[s + 3];
            }
        }
    }
}
