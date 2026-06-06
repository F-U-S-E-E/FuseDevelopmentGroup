using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Fuse.Core.Authoring;
using Fuse.Core.Model;
using Fuse.ExternalEditor.Controls;
using Fuse.ExternalEditor.Logic;
using Fuse.ExternalEditor.Rendering;
using Fuse.ExternalEditor.Services;
using Fuse.ExternalEditor.ViewModels;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

public class TrackGraphTests
{
    [Fact]
    public void NearestNode_Picks_Closest_Within_Radius()
    {
        var view = new ViewTransform { Zoom = 1.0, PanX = 0, PanY = 0, MinX = 0, MaxY = 0 };
        var tracks = new FuseTrackDefinition();
        TrackOps.AddNode(tracks, "a", new FuseVector3(0, 0, 0), default);
        TrackOps.AddNode(tracks, "b", new FuseVector3(500, 0, 0), default); // one tile east

        // Node "a" world (0,0) -> screen (0,64); node "b" world (500,0) -> screen (64,64).
        Assert.Equal("a", TrackHitTest.NearestNode(view, tracks, 2, 66, 9));
        Assert.Equal("b", TrackHitTest.NearestNode(view, tracks, 62, 66, 9));
        Assert.Null(TrackHitTest.NearestNode(view, tracks, 300, 300, 9)); // nothing near
    }

    [Fact]
    public void ViewModel_Add_Select_Delete()
    {
        var vm = new TrackGraphViewModel(new ProjectService(), new LiveBridgeService(), new UndoService());
        Assert.Equal(0, vm.NodeCount);

        vm.AddNodeCommand.Execute(null);
        Assert.Equal(1, vm.NodeCount);
        Assert.NotNull(vm.SelectedNodeId);
        Assert.Contains("Node ", vm.SelectedNodeSummary, StringComparison.Ordinal);

        vm.DeleteSelectedCommand.Execute(null);
        Assert.Equal(0, vm.NodeCount);
        Assert.Null(vm.SelectedNodeId);
    }

    [Fact]
    public void ViewModel_Opens_Fuse_Mod()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fuse-mod.example.json");
        var vm = new TrackGraphViewModel(new ProjectService(), new LiveBridgeService(), new UndoService());

        vm.OpenProject(path);

        Assert.Contains("Loaded", vm.Status, StringComparison.Ordinal);
        Assert.NotNull(vm.Tracks);
    }

    [Fact]
    public void ViewModel_Generate_Turnout_With_Undo_Redo()
    {
        var vm = new TrackGraphViewModel(new ProjectService(), new LiveBridgeService(), new UndoService());

        vm.GenerateTurnoutCommand.Execute(null);
        Assert.Equal(4, vm.NodeCount); // switch + entry + through + diverge
        Assert.Equal(3, vm.SegmentCount);
        Assert.True(vm.CanUndo);
        Assert.False(vm.CanRedo);

        vm.UndoCommand.Execute(null);
        Assert.Equal(0, vm.NodeCount);
        Assert.Equal(0, vm.SegmentCount);
        Assert.True(vm.CanRedo);

        vm.RedoCommand.Execute(null);
        Assert.Equal(4, vm.NodeCount);
        Assert.Equal(3, vm.SegmentCount);
    }

    [AvaloniaFact]
    public void MapViewport_Renders_Tracks_And_Selection()
    {
        var tracks = new FuseTrackDefinition();
        TrackOps.AddNode(tracks, "n1", new FuseVector3(0, 0, 0), new FuseVector3(0, 0, 0));
        TrackOps.AddNode(tracks, "n2", new FuseVector3(100, 0, 50), new FuseVector3(0, 90, 0));
        TrackOps.ConnectSegment(tracks, "s1", "n1", "n2");

        var viewport = new MapViewport { Tracks = tracks };
        var window = new Window { Width = 500, Height = 400, Content = viewport };
        window.Show();

        Assert.True(viewport.Bounds.Width > 0);

        viewport.SelectedNodeId = "n1"; // must not throw, triggers re-render
        Assert.Equal("n1", viewport.SelectedNodeId);
    }
}
