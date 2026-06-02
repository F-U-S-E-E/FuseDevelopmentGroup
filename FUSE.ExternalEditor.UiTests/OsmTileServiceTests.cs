using System;
using System.IO;
using System.Net;
using System.Net.Http;
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

            using var img = new Image<Rgba32>(256, 256, new Rgba32(120, 140, 160, 255));
            using var ms = new MemoryStream();
            img.SaveAsPng(ms);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ms.ToArray()) });
        }
    }

    [Fact]
    public async Task Fetch_Stitches_Tiles_And_Sends_UserAgent()
    {
        var handler = new FakeHandler();
        var svc = new OsmTileService(new HttpClient(handler));

        var mosaic = await svc.FetchAsync(35.38, -83.50, 35.40, -83.48, zoom: 14);

        Assert.True(mosaic.TileCount >= 1);
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
}
