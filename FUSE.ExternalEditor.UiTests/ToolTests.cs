using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Fuse.Core.Authoring;
using Fuse.Core.Geometry;
using Fuse.Core.Model;
using Fuse.ExternalEditor.Controls;
using Fuse.ExternalEditor.Tools;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

/// <summary>
/// The interactive tools are pure state machines over an <see cref="IToolContext"/>,
/// so they're driven directly with abstracted pointer samples — no display needed.
/// </summary>
public class ToolTests
{
    private sealed class FakeContext : IToolContext
    {
        public FuseModDefinition Project { get; } = new() { Id = "t", Name = "t" };

        public FuseTrackDefinition Tracks => Project.Tracks;
        public FuseWorldDefinition World => Project.World;
        public string? SelectedNodeId { get; set; }
        public UndoService Undo { get; } = new();
        public int Changes { get; private set; }

        public string? ToolStatus { get; set; }

        public void Changed() => Changes++;

        public void CommitGenerated(string label, GeneratedTrack generated)
        {
            List<string>? nodes = null;
            List<string>? segments = null;
            Undo.Execute(new UndoAction(
                label,
                () => { var (n, s) = TrackGenerators.Commit(Tracks, generated); nodes = n; segments = s; Changed(); },
                () =>
                {
                    if (segments != null)
                    {
                        foreach (var sid in segments)
                        {
                            TrackOps.DeleteSegment(Tracks, sid);
                        }
                    }

                    if (nodes != null)
                    {
                        foreach (var nid in nodes)
                        {
                            Tracks.Nodes.Remove(nid);
                        }
                    }

                    Changed();
                }));
        }
    }

    private static ToolPointer At(double worldX, double worldZ, string? under = null) =>
        new(worldX, worldZ, worldX, worldZ, under);

    [Fact]
    public void SelectTool_Selects_Node_On_Click_Not_Drag()
    {
        var ctx = new FakeContext();
        TrackOps.AddNode(ctx.Tracks, "n1", new FuseVector3(0, 0, 0), default);
        var tool = new SelectTool();

        Assert.True(tool.PointerReleased(ctx, At(0, 0, "n1"), wasDrag: false));
        Assert.Equal("n1", ctx.SelectedNodeId);

        ctx.SelectedNodeId = null;
        Assert.False(tool.PointerReleased(ctx, At(0, 0, "n1"), wasDrag: true)); // drag = pan, not select
        Assert.Null(ctx.SelectedNodeId);
    }

    [Fact]
    public void MoveNodeTool_Drags_Node_And_Undo_Restores()
    {
        var ctx = new FakeContext();
        TrackOps.AddNode(ctx.Tracks, "n1", new FuseVector3(10, 5, 20), default);
        var tool = new MoveNodeTool();

        Assert.True(tool.PointerPressed(ctx, At(10, 20, "n1")));
        tool.PointerMoved(ctx, At(110, 220), pressed: true);
        Assert.True(tool.PointerReleased(ctx, At(110, 220), wasDrag: true));

        var moved = ctx.Tracks.Nodes["n1"].Position;
        Assert.Equal(110f, moved.x);
        Assert.Equal(5f, moved.y); // y preserved
        Assert.Equal(220f, moved.z);

        ctx.Undo.Undo();
        var restored = ctx.Tracks.Nodes["n1"].Position;
        Assert.Equal(10f, restored.x);
        Assert.Equal(20f, restored.z);
    }

    [Fact]
    public void MoveNodeTool_On_Empty_Does_Not_Capture()
    {
        var ctx = new FakeContext();
        var tool = new MoveNodeTool();
        Assert.False(tool.PointerPressed(ctx, At(0, 0, under: null))); // lets the viewport pan
    }

    [Fact]
    public void ConnectTool_Connects_Two_Nodes_With_Preview_And_Undo()
    {
        var ctx = new FakeContext();
        TrackOps.AddNode(ctx.Tracks, "a", new FuseVector3(0, 0, 0), default);
        TrackOps.AddNode(ctx.Tracks, "b", new FuseVector3(100, 0, 0), default);
        var tool = new ConnectTool();

        Assert.True(tool.PointerPressed(ctx, At(0, 0, "a")));
        tool.PointerMoved(ctx, At(50, 0), pressed: true);
        Assert.NotNull(tool.Preview); // ghost line from 'a' to cursor
        Assert.Single(tool.Preview!.Lines);

        Assert.True(tool.PointerReleased(ctx, At(100, 0, "b"), wasDrag: true));
        Assert.Single(ctx.Tracks.Segments);
        Assert.Null(tool.Preview); // cleared after commit

        ctx.Undo.Undo();
        Assert.Empty(ctx.Tracks.Segments);
    }

    [Fact]
    public void PlaceGeneratorTool_Previews_Then_Commits_Undoable()
    {
        var ctx = new FakeContext();
        var tool = new PlaceGeneratorTool("turnout", "Turnout", (x, y, z) => TrackGenerators.Turnout(x, y, z, 0, divergeAngle: 10, legLength: 30));

        tool.PointerMoved(ctx, At(50, 50), pressed: false);
        Assert.NotNull(tool.Preview);
        Assert.Equal(4, tool.Preview!.Markers.Count);
        Assert.Equal(3, tool.Preview.Lines.Count);

        Assert.True(tool.PointerReleased(ctx, At(50, 50), wasDrag: false));
        Assert.Equal(4, ctx.Tracks.Nodes.Count);
        Assert.Equal(3, ctx.Tracks.Segments.Count);

        ctx.Undo.Undo();
        Assert.Empty(ctx.Tracks.Nodes);
        Assert.Empty(ctx.Tracks.Segments);

        Assert.False(tool.PointerReleased(ctx, At(0, 0), wasDrag: true)); // drag = pan, no placement
        Assert.Empty(ctx.Tracks.Nodes);
    }

    [Fact]
    public void PlaceSceneryTool_Adds_Scenery_Undoable()
    {
        var ctx = new FakeContext();
        var tool = new PlaceSceneryTool();

        tool.PointerMoved(ctx, At(5, 5), pressed: false);
        Assert.NotNull(tool.Preview);

        Assert.True(tool.PointerReleased(ctx, At(5, 5), wasDrag: false));
        Assert.Single(ctx.World.Scenery);

        ctx.Undo.Undo();
        Assert.Empty(ctx.World.Scenery);
    }

    [Fact]
    public void ToolHost_Activate_Switches_Active_Tool()
    {
        var ctx = new FakeContext();
        var host = new ToolHost(new ITool[] { new SelectTool(), new MoveNodeTool(), new PlaceSceneryTool() });

        Assert.Equal("select", host.Active.Id);
        Assert.True(host.Activate("scenery", ctx));
        Assert.Equal("scenery", host.Active.Id);
        Assert.False(host.Activate("scenery", ctx)); // already active
        Assert.False(host.Activate("does-not-exist", ctx));
    }

    [Fact]
    public void MeasureTool_Reports_Distance_And_Bearing()
    {
        var ctx = new FakeContext();
        var tool = new MeasureTool();

        Assert.True(tool.PointerReleased(ctx, At(0, 0), wasDrag: false));
        Assert.Contains("second point", ctx.ToolStatus!, StringComparison.Ordinal);

        Assert.True(tool.PointerReleased(ctx, At(30, 40), wasDrag: false)); // 3-4-5 → 50 m
        Assert.Contains("50", ctx.ToolStatus!, StringComparison.Ordinal);
        Assert.Contains("bearing", ctx.ToolStatus!, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void MapViewport_Renders_With_Active_PlaceTool_Preview()
    {
        var ctx = new FakeContext();
        var tool = new PlaceGeneratorTool("turnout", "Turnout", (x, y, z) => TrackGenerators.Turnout(x, y, z, 0));
        tool.PointerMoved(ctx, At(0, 0), pressed: false); // populate the ghost preview

        var viewport = new MapViewport
        {
            Tracks = ctx.Tracks,
            World = ctx.World,
            ToolContext = ctx,
            ActiveTool = tool,
        };
        var window = new Window { Width = 400, Height = 300, Content = viewport };
        window.Show();

        Assert.True(viewport.Bounds.Width > 0); // rendered the preview without throwing
    }
}
