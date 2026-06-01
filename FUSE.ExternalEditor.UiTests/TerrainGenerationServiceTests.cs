using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Fuse.ExternalEditor.Logic.Generation;
using Fuse.ExternalEditor.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

/// <summary>
/// Exercises the async terrain generator with a fake HTTP handler (no network):
/// auth-error surfacing, generated tile bytes from canned Mapbox/NLCD imagery, and
/// bounded-parallel region progress/concurrency.
/// </summary>
public class TerrainGenerationServiceTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        private int _concurrent;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public int MaxConcurrent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var c = Interlocked.Increment(ref _concurrent);
            MaxConcurrent = Math.Max(MaxConcurrent, c);
            try
            {
                await Task.Delay(5, cancellationToken);
                return _responder(request);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }
    }

    private static byte[] Png(int w, int h, byte r, byte g, byte b)
    {
        using var img = new Image<Rgb24>(w, h, new Rgb24(r, g, b));
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static HttpResponseMessage Ok(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    [Fact]
    public async Task GenerateTile_Decodes_Mapbox_And_Nlcd_To_Expected_Bytes()
    {
        // (1,163,236) terrain-RGB → -10000 + 0.1*(65536+41728+236) = 750 m.
        // gx=0 → no east-west ramp → pack16(750) = 16383 → R=63, G=255.
        // NLCD (181,201,142) → preset 1 → A = 1<<4 = 16.
        var handler = new FakeHandler(req => Ok(req.RequestUri!.Host.Contains("mapbox")
            ? Png(256, 256, 1, 163, 236)
            : Png(609, 609, 181, 201, 142)));
        var svc = new TerrainGenerationService(new HttpClient(handler), new TerrainTileService());

        var tile = await svc.GenerateTileAsync(0, 0, "token", new TerrainGenOptions { UseNlcd = true, NlcdBlur = 0 });

        Assert.Equal(513, tile.Res);
        Assert.Equal(63, tile.R[0]);
        Assert.Equal(255, tile.G[0]);
        Assert.Equal(16, tile.A[0]);
        var mid = ((513 * 513) / 2);
        Assert.Equal(63, tile.R[mid]); // uniform across the (gx=0) tile
        Assert.Equal(255, tile.G[mid]);
    }

    [Fact]
    public async Task Unauthorized_Surfaces_As_MapboxAuthException()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var svc = new TerrainGenerationService(new HttpClient(handler), new TerrainTileService());

        await Assert.ThrowsAsync<MapboxAuthException>(() =>
            svc.GenerateTileAsync(0, 0, "token", new TerrainGenOptions { UseNlcd = false }));
    }

    [Fact]
    public async Task Empty_Token_Throws_Before_Any_Request()
    {
        var svc = new TerrainGenerationService(new HttpClient(new FakeHandler(_ => Ok(Png(1, 1, 0, 0, 0)))), new TerrainTileService());
        await Assert.ThrowsAsync<MapboxAuthException>(() =>
            svc.GenerateTileAsync(0, 0, "   ", new TerrainGenOptions()));
    }

    [Fact]
    public async Task GenerateRegion_Reports_Progress_And_Runs_Bounded_Parallel()
    {
        var handler = new FakeHandler(req => Ok(req.RequestUri!.Host.Contains("mapbox")
            ? Png(256, 256, 1, 163, 236)
            : Png(609, 609, 104, 170, 99)));
        var svc = new TerrainGenerationService(new HttpClient(handler), new TerrainTileService());
        var tiles = Enumerable.Range(0, 6).Select(i => (i, 0)).ToList();
        var reports = new List<int>();
        var progress = new Progress<TerrainGenProgress>(p =>
        {
            lock (reports)
            {
                reports.Add(p.Completed);
            }
        });

        var done = await svc.GenerateRegionAsync(tiles, "token", new TerrainGenOptions { UseNlcd = false, MaxConcurrency = 3 }, progress);

        Assert.Equal(6, done);
        Assert.True(handler.MaxConcurrent >= 2, $"expected parallel fetches, got {handler.MaxConcurrent}");
        Assert.True(handler.MaxConcurrent <= 3, $"expected bounded by MaxConcurrency, got {handler.MaxConcurrent}");
        await Task.Delay(50); // let the last Progress callbacks post
        lock (reports)
        {
            Assert.Equal(6, reports.Count);
        }
    }
}
