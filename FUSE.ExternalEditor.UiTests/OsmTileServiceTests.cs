using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Fuse.ExternalEditor.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

public class OsmTileServiceTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public bool SawUserAgent { get; private set; }
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (request.Headers.UserAgent.Count > 0)
            {
                SawUserAgent = true;
            }

            return Task.FromResult(OkTile());
        }
    }

    /// <summary>Responds per call index so tests can script 429/503 sequences.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> _respond;

        public ScriptedHandler(Func<int, HttpResponseMessage> respond) => _respond = respond;

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond(Calls++));
    }

    private static HttpResponseMessage OkTile()
    {
        using var img = new Image<Rgba32>(256, 256, new Rgba32(120, 140, 160, 255));
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ms.ToArray()) };
    }

    private static HttpResponseMessage RateLimited(RetryConditionHeaderValue? retryAfter = null, HttpStatusCode status = HttpStatusCode.TooManyRequests)
    {
        var resp = new HttpResponseMessage(status);
        if (retryAfter is not null)
        {
            resp.Headers.RetryAfter = retryAfter;
        }

        return resp;
    }

    /// <summary>Service with the backoff wait stubbed out; waits are recorded into <paramref name="delays"/>.</summary>
    private static OsmTileService Service(HttpMessageHandler handler, List<TimeSpan> delays, int maxAttempts = 4)
        => new(new HttpClient(handler), maxAttempts, (d, _) =>
        {
            delays.Add(d);
            return Task.CompletedTask;
        });

    // Small box well inside one z14 tile; mosaic dimensions give the actual tile count.
    private static Task<OsmMosaic> FetchSmallAsync(OsmTileService svc, CancellationToken ct = default)
        => svc.FetchAsync(35.390, -83.490, 35.391, -83.489, zoom: 14, ct);

    [Fact]
    public async Task Fetch_Stitches_Tiles_And_Sends_UserAgent()
    {
        var handler = new FakeHandler();
        var svc = new OsmTileService(new HttpClient(handler));

        var mosaic = await svc.FetchAsync(35.38, -83.50, 35.40, -83.48, zoom: 14);

        Assert.True(mosaic.TileCount >= 1);
        Assert.Equal(0, mosaic.FailedTileCount);
        Assert.Equal(0, mosaic.Width % 256);
        Assert.Equal(0, mosaic.Height % 256);
        Assert.Equal((mosaic.Width / 256) * (mosaic.Height / 256), mosaic.TileCount);
        Assert.Equal(mosaic.TileCount, handler.Calls);
        Assert.True(handler.SawUserAgent);
        Assert.Equal(mosaic.Width * mosaic.Height * 4, mosaic.Rgba.Length);

        // Geo bounds are well-ordered (north above south, west left of east).
        Assert.True(mosaic.NorthLat > mosaic.SouthLat);
        Assert.True(mosaic.WestLon < mosaic.EastLon);
    }

    [Fact]
    public async Task Fetch_Rejects_Oversized_Region()
    {
        var svc = new OsmTileService(new HttpClient(new FakeHandler()));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.FetchAsync(-80, -179, 80, 179, zoom: 10)); // whole-world at z10 ≫ tile cap
    }

    [Fact]
    public async Task Fetch_Retries_RateLimited_Tile_Then_Succeeds()
    {
        var handler = new ScriptedHandler(call => call < 2 ? RateLimited() : OkTile());
        var delays = new List<TimeSpan>();
        var svc = Service(handler, delays);

        var mosaic = await FetchSmallAsync(svc);

        var tiles = (mosaic.Width / 256) * (mosaic.Height / 256);
        Assert.Equal(tiles, mosaic.TileCount);
        Assert.Equal(0, mosaic.FailedTileCount);
        Assert.Equal(tiles + 2, handler.Calls); // two 429s cost one extra request each
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task Fetch_Retries_ServiceUnavailable_Like_TooManyRequests()
    {
        // 503 takes the same retry branch as 429 (OsmTileService treats them identically).
        var handler = new ScriptedHandler(call => call < 2 ? RateLimited(status: HttpStatusCode.ServiceUnavailable) : OkTile());
        var delays = new List<TimeSpan>();
        var svc = Service(handler, delays);

        var mosaic = await FetchSmallAsync(svc);

        var tiles = (mosaic.Width / 256) * (mosaic.Height / 256);
        Assert.Equal(tiles, mosaic.TileCount);
        Assert.Equal(0, mosaic.FailedTileCount);
        Assert.Equal(tiles + 2, handler.Calls); // two 503s cost one extra request each
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task Fetch_Backoff_Is_Exponential_With_Jitter_When_No_RetryAfter()
    {
        var handler = new ScriptedHandler(call => call < 3 ? RateLimited() : OkTile());
        var delays = new List<TimeSpan>();
        var svc = Service(handler, delays);

        await FetchSmallAsync(svc);

        Assert.Equal(3, delays.Count);
        // base 500ms doubling per attempt, jittered into [50%, 100%] of nominal
        Assert.InRange(delays[0].TotalMilliseconds, 250, 500);
        Assert.InRange(delays[1].TotalMilliseconds, 500, 1000);
        Assert.InRange(delays[2].TotalMilliseconds, 1000, 2000);
    }

    [Fact]
    public async Task Fetch_Honors_RetryAfter_Header_And_Clamps_Excessive_Values()
    {
        var handler = new ScriptedHandler(call => call switch
        {
            0 => RateLimited(new RetryConditionHeaderValue(TimeSpan.FromSeconds(7))),
            1 => RateLimited(new RetryConditionHeaderValue(TimeSpan.FromMinutes(10))),
            _ => OkTile(),
        });
        var delays = new List<TimeSpan>();
        var svc = Service(handler, delays);

        await FetchSmallAsync(svc);

        Assert.Equal(TimeSpan.FromSeconds(7), delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(30), delays[1]); // 10 min clamped to the 30 s cap
    }

    [Fact]
    public async Task Fetch_Honors_RetryAfter_HttpDate_Format_And_Clamps()
    {
        // Retry-After as an absolute HTTP-date (the .Date branch) rather than delta-seconds.
        // Delay is computed as (date - UtcNow), so a sub-second gap elapses before the service
        // reads it; the generous lower bound tolerates that while still excluding the backoff
        // fallback (<=1 s) and the delta path, proving the date was actually parsed.
        var handler = new ScriptedHandler(call => call switch
        {
            0 => RateLimited(new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(10))),
            1 => RateLimited(new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(10))),
            _ => OkTile(),
        });
        var delays = new List<TimeSpan>();
        var svc = Service(handler, delays);

        await FetchSmallAsync(svc);

        Assert.InRange(delays[0].TotalSeconds, 8.0, 10.0); // ~10 s honored from the HTTP-date
        Assert.Equal(TimeSpan.FromSeconds(30), delays[1]); // 10 min out clamped to the 30 s cap
    }

    [Fact]
    public async Task Fetch_Leaves_Exhausted_Tile_Transparent_And_Stops_Retrying_Later_Tiles()
    {
        var handler = new ScriptedHandler(_ => RateLimited(new RetryConditionHeaderValue(TimeSpan.Zero)));
        var delays = new List<TimeSpan>();
        var svc = Service(handler, delays, maxAttempts: 3);

        var mosaic = await FetchSmallAsync(svc);

        var tiles = (mosaic.Width / 256) * (mosaic.Height / 256);
        Assert.Equal(0, mosaic.TileCount);
        Assert.Equal(tiles, mosaic.FailedTileCount);
        Assert.All(mosaic.Rgba, b => Assert.Equal(0, b)); // failed tiles stay transparent
        Assert.Equal(3 + (tiles - 1), handler.Calls); // full retries once, then one attempt per tile
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task Fetch_Throws_On_NonRetryable_Status()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var svc = Service(handler, new List<TimeSpan>());

        await Assert.ThrowsAsync<HttpRequestException>(() => FetchSmallAsync(svc));
        Assert.Equal(1, handler.Calls); // no retry for non-429/503
    }

    [Fact]
    public async Task Fetch_Propagates_Cancellation_During_Backoff()
    {
        var handler = new ScriptedHandler(_ => RateLimited());
        var svc = new OsmTileService(new HttpClient(handler), maxAttempts: 4, (_, ct) => throw new OperationCanceledException(ct));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FetchSmallAsync(svc));
        Assert.Equal(1, handler.Calls); // cancelled in the first backoff wait
    }

    [Fact]
    public void Constructor_Rejects_NonPositive_MaxAttempts()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new OsmTileService(new HttpClient(new FakeHandler()), maxAttempts: 0));
}
