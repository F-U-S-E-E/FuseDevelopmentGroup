using System;
using System.Linq;
using Fuse.Core.Authoring;
using Fuse.Core.Geometry;
using Fuse.Core.Model;
using Fuse.ExternalEditor.Logic;
using Fuse.ExternalEditor.Models.Terrain;
using Fuse.ExternalEditor.Services;
using Fuse.ExternalEditor.ViewModels;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

/// <summary>Phase 7b: profile dock VM, arc-fit apply/undo, terrain sampler.</summary>
public class Profile7bTests
{
    private static (TrackGraphViewModel Tg, ProfileViewModel Pf, UndoService Undo) Build()
    {
        var undo = new UndoService();
        var viewport = new ViewportViewModel(new TerrainTileService());
        var trackGraph = new TrackGraphViewModel(new ProjectService(), new LiveBridgeService(), undo);
        var profile = new ProfileViewModel(trackGraph, viewport, undo);
        return (trackGraph, profile, undo);
    }

    [Fact]
    public void Profile_Builds_Stations_For_A_Chain()
    {
        var (tg, pf, _) = Build();
        var t = tg.Tracks;
        TrackOps.AddNode(t, "a", new FuseVector3(0, 100, 0), default);
        TrackOps.AddNode(t, "b", new FuseVector3(0, 110, 100), default);
        TrackOps.AddNode(t, "c", new FuseVector3(0, 110, 200), default);
        TrackOps.ConnectSegment(t, "s1", "a", "b");
        TrackOps.ConnectSegment(t, "s2", "b", "c");

        pf.Refresh();

        Assert.Equal(3, pf.Points.Count);
        Assert.Equal(0.0, pf.Points[0].Station);
        Assert.True(pf.Points[^1].Station > 150); // ~200 m of connected run
        Assert.Contains("length", pf.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void FitArc_Recovers_Radius_And_Is_Undoable()
    {
        var (tg, pf, undo) = Build();
        var t = tg.Tracks;
        TrackGenerators.Commit(t, TrackGenerators.Curve(0, 0, 0, 0.0, 200.0, 60.0, 0.0, right: false, nSegments: 6));

        pf.Refresh();
        Assert.True(Math.Abs(pf.ArcRadius - 200.0) < 5.0); // fit recovers the generating radius

        var before = t.Nodes.Keys.OrderBy(k => k).Select(k => (t.Nodes[k].Position.x, t.Nodes[k].Position.z)).ToList();
        pf.FitArcCommand.Execute(null);
        Assert.True(undo.CanUndo);

        undo.Undo();
        var after = t.Nodes.Keys.OrderBy(k => k).Select(k => (t.Nodes[k].Position.x, t.Nodes[k].Position.z)).ToList();
        Assert.Equal(before, after); // undo restored node positions exactly
    }

    [Fact]
    public void TerrainHeightSampler_Reads_Tile_Height_And_Misses_Outside()
    {
        var grid = new TileGrid();
        grid.Add(TerrainBrushTests.Flat(64, 16383)); // tile (0,0)

        var h = TerrainHeightSampler.Sample(grid, 250.0, 250.0); // inside tile (0,0)
        Assert.NotNull(h);
        Assert.True(Math.Abs(h!.Value - TerrainConstants.ToMeters(16383)) < 0.01);

        Assert.Null(TerrainHeightSampler.Sample(grid, 99999.0, 0.0)); // no tile there
    }
}
