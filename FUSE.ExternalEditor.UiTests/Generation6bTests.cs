using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuse.ExternalEditor.Logic.Generation;
using Fuse.ExternalEditor.Models.Terrain;
using Fuse.ExternalEditor.Services;
using Fuse.ExternalEditor.ViewModels;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

/// <summary>Phase 6b: generation panel VM + OSM overlay VM + geo↔world alignment.</summary>
public class Generation6bTests
{
    private sealed class FakeGen : ITerrainGenerationService
    {
        public bool Throw401 { get; set; }

        public Task<TerrainTile> GenerateTileAsync(int gx, int gy, string token, TerrainGenOptions options, CancellationToken ct = default)
            => throw new NotImplementedException();

        public async Task<int> GenerateRegionAsync(IReadOnlyList<(int Gx, int Gy)> tiles, string token, TerrainGenOptions options, IProgress<TerrainGenProgress>? progress = null, CancellationToken ct = default)
        {
            if (Throw401)
            {
                throw new MapboxAuthException("nope");
            }

            for (var i = 0; i < tiles.Count; i++)
            {
                await Task.Yield();
                progress?.Report(new TerrainGenProgress(i + 1, tiles.Count, "tile"));
            }

            return tiles.Count;
        }
    }

    private sealed class FakeOsm : IOsmTileService
    {
        public Task<OsmMosaic> FetchAsync(double minLat, double minLon, double maxLat, double maxLon, int zoom, CancellationToken ct = default)
            => Task.FromResult(new OsmMosaic(new byte[4 * 4 * 4], 4, 4, 1, 0, maxLat, minLon, minLat, maxLon));
    }

    private static ViewportViewModel Viewport() => new(new TerrainTileService());

    [Fact]
    public async Task Generate_Reports_Progress_And_Completes()
    {
        var vm = new GenerationViewModel(new FakeGen(), Viewport())
        {
            MapboxToken = "tok",
            Width = 2,
            Height = 3,
        };

        Assert.True(vm.GenerateCommand.CanExecute(null));
        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(6, vm.ProgressTotal);
        Assert.Contains("Generated 6", vm.Status, StringComparison.Ordinal);
        Assert.False(vm.IsGenerating);
    }

    [Fact]
    public async Task Generate_Surfaces_Auth_Error()
    {
        var vm = new GenerationViewModel(new FakeGen { Throw401 = true }, Viewport())
        {
            MapboxToken = "tok",
            Width = 1,
            Height = 1,
        };

        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.Contains("rejected", vm.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_Disabled_Without_Token()
    {
        var vm = new GenerationViewModel(new FakeGen(), Viewport());
        Assert.False(vm.GenerateCommand.CanExecute(null));
        vm.MapboxToken = "tok";
        Assert.True(vm.GenerateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Osm_Fetch_Builds_Aligned_Overlay()
    {
        var grid = new TileGrid();
        grid.Add(TerrainBrushTests.Flat(64, 20000)); // tile (0,0)
        var vm = new OsmOverlayViewModel(new FakeOsm(), new ViewportViewModel(new TerrainTileService()) { Grid = grid }) { Zoom = 14 };

        await vm.FetchCommand.ExecuteAsync(null);

        Assert.NotNull(vm.Overlay);
        Assert.True(vm.Enabled);
        Assert.NotNull(vm.EffectiveOverlay);
        Assert.True(vm.Overlay!.WorldMaxX > vm.Overlay.WorldMinX);
        Assert.True(vm.Overlay.WorldMaxZ > vm.Overlay.WorldMinZ);
        Assert.Contains("OSM", vm.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Osm_Fetch_Without_Terrain_Reports()
    {
        var vm = new OsmOverlayViewModel(new FakeOsm(), Viewport());
        await vm.FetchCommand.ExecuteAsync(null);
        Assert.Null(vm.Overlay);
        Assert.Contains("Load terrain", vm.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldGeo_RoundTrips_Within_A_Few_Metres()
    {
        foreach (var (wx, wz) in new[] { (0.0, 0.0), (500.0, 500.0), (2500.0, -1500.0), (-1000.0, 3000.0) })
        {
            var (lat, lon) = MapboxTerrain.WorldToGeo(wx, wz);
            var (rx, rz) = MapboxTerrain.GeoToWorld(lat, lon);
            Assert.True(Math.Abs(rx - wx) < 5.0, $"x {wx} -> {rx}");
            Assert.True(Math.Abs(rz - wz) < 5.0, $"z {wz} -> {rz}");
        }
    }
}
