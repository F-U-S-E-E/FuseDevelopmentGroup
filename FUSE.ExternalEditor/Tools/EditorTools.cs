using System;
using Fuse.Core.Authoring;
using Fuse.Core.Geometry;
using Fuse.Core.Model;

namespace Fuse.ExternalEditor.Tools;

/// <summary>Click an empty area to clear selection, or a node to select it. Drag pans (viewport default).</summary>
public sealed class SelectTool : ToolBase
{
    public override string Id => "select";
    public override string Title => "Select";

    public override bool PointerReleased(IToolContext ctx, ToolPointer p, bool wasDrag)
    {
        if (wasDrag)
        {
            return false;
        }

        ctx.SelectedNodeId = p.NodeUnderCursor;
        ctx.Changed();
        return true;
    }
}

/// <summary>Drag a node to reposition it (XZ); commits one undoable move on release.</summary>
public sealed class MoveNodeTool : ToolBase
{
    private string? _moving;
    private FuseVector3 _original;
    private bool _dragged;

    public override string Id => "move";
    public override string Title => "Move";

    public override bool PointerPressed(IToolContext ctx, ToolPointer p)
    {
        if (p.NodeUnderCursor is { } id && ctx.Tracks.Nodes.TryGetValue(id, out var node) && node is not null)
        {
            _moving = id;
            _original = node.Position;
            _dragged = false;
            ctx.SelectedNodeId = id;
            ctx.Changed();
            return true; // own the gesture; don't pan
        }

        _moving = null;
        return false;
    }

    public override void PointerMoved(IToolContext ctx, ToolPointer p, bool pressed)
    {
        if (pressed && _moving is { } id)
        {
            TrackOps.MoveNode(ctx.Tracks, id, new FuseVector3((float)p.WorldX, _original.y, (float)p.WorldZ));
            _dragged = true;
            ctx.Changed();
        }
    }

    public override bool PointerReleased(IToolContext ctx, ToolPointer p, bool wasDrag)
    {
        if (_moving is not { } id)
        {
            return false;
        }

        var final = ctx.Tracks.Nodes.TryGetValue(id, out var node) && node is not null ? node.Position : _original;
        var original = _original;
        _moving = null;

        var moved = final.x != original.x || final.y != original.y || final.z != original.z;
        if (_dragged && moved)
        {
            // The live drag already applied 'final'; record it as a reversible step.
            ctx.Undo.Execute(new UndoAction(
                $"Move {id}",
                () => { TrackOps.MoveNode(ctx.Tracks, id, final); ctx.Changed(); },
                () => { TrackOps.MoveNode(ctx.Tracks, id, original); ctx.Changed(); }));
        }

        return true;
    }
}

/// <summary>Press one node, release on another to connect them with a segment (undoable).</summary>
public sealed class ConnectTool : ToolBase
{
    private string? _start;
    private bool _haveCursor;
    private double _cursorX;
    private double _cursorZ;
    private readonly ToolPreview _preview = new();

    public override string Id => "connect";
    public override string Title => "Connect";

    public override ToolPreview? Preview => _start is not null && _haveCursor ? _preview : null;

    public override void Deactivated(IToolContext ctx) => Reset();

    public override bool PointerPressed(IToolContext ctx, ToolPointer p)
    {
        if (p.NodeUnderCursor is { } id)
        {
            _start = id;
            return true;
        }

        _start = null;
        return false;
    }

    public override void PointerMoved(IToolContext ctx, ToolPointer p, bool pressed)
    {
        _cursorX = p.WorldX;
        _cursorZ = p.WorldZ;
        _haveCursor = true;
        if (_start is { } id && ctx.Tracks.Nodes.TryGetValue(id, out var node) && node is not null)
        {
            _preview.Lines.Clear();
            _preview.Lines.Add(((node.Position.x, node.Position.z), (p.WorldX, p.WorldZ)));
        }
    }

    public override bool PointerReleased(IToolContext ctx, ToolPointer p, bool wasDrag)
    {
        if (_start is not { } start)
        {
            return false;
        }

        Reset();
        if (p.NodeUnderCursor is { } end && end != start)
        {
            var id = TrackOps.NewSegmentId(ctx.Tracks);
            ctx.Undo.Execute(new UndoAction(
                $"Connect {start}->{end}",
                () => { TrackOps.ConnectSegment(ctx.Tracks, id, start, end); ctx.Changed(); },
                () => { TrackOps.DeleteSegment(ctx.Tracks, id); ctx.Changed(); }));
        }

        return true;
    }

    private void Reset()
    {
        _start = null;
        _haveCursor = false;
        _preview.Lines.Clear();
    }
}

/// <summary>Hover shows a ghost of a generated chain; click places it (undoable). Powers turnout/wye/curve.</summary>
public sealed class PlaceGeneratorTool : ToolBase
{
    private readonly Func<double, double, double, GeneratedTrack> _factory;
    private ToolPreview? _preview;

    public PlaceGeneratorTool(string id, string title, Func<double, double, double, GeneratedTrack> factory)
    {
        Id = id;
        Title = title;
        _factory = factory;
    }

    public override string Id { get; }
    public override string Title { get; }
    public override ToolPreview? Preview => _preview;

    public override void Deactivated(IToolContext ctx) => _preview = null;

    public override void PointerMoved(IToolContext ctx, ToolPointer p, bool pressed)
        => _preview = ToPreview(_factory(p.WorldX, 0, p.WorldZ));

    public override bool PointerReleased(IToolContext ctx, ToolPointer p, bool wasDrag)
    {
        if (wasDrag)
        {
            return false; // that was a pan, not a placement
        }

        ctx.CommitGenerated(Title, _factory(p.WorldX, 0, p.WorldZ));
        return true;
    }

    private static ToolPreview ToPreview(GeneratedTrack g)
    {
        var pv = new ToolPreview();
        foreach (var n in g.Nodes)
        {
            pv.Markers.Add((n.X, n.Z));
        }

        foreach (var s in g.Segments)
        {
            pv.Lines.Add(((g.Nodes[s.StartIndex].X, g.Nodes[s.StartIndex].Z), (g.Nodes[s.EndIndex].X, g.Nodes[s.EndIndex].Z)));
        }

        return pv;
    }
}

/// <summary>Two clicks → bearing + XZ distance between the points (reported to the HUD).</summary>
public sealed class MeasureTool : ToolBase
{
    private (double X, double Z)? _a;
    private readonly ToolPreview _preview = new();

    public override string Id => "measure";
    public override string Title => "Measure";
    public override ToolPreview? Preview => _a is null ? null : _preview;

    public override void Deactivated(IToolContext ctx)
    {
        _a = null;
        _preview.Lines.Clear();
        _preview.Markers.Clear();
    }

    public override void PointerMoved(IToolContext ctx, ToolPointer p, bool pressed)
    {
        if (_a is { } a)
        {
            _preview.Lines.Clear();
            _preview.Lines.Add(((a.X, a.Z), (p.WorldX, p.WorldZ)));
        }
    }

    public override bool PointerReleased(IToolContext ctx, ToolPointer p, bool wasDrag)
    {
        if (wasDrag)
        {
            return false;
        }

        if (_a is null)
        {
            _a = (p.WorldX, p.WorldZ);
            _preview.Markers.Clear();
            _preview.Markers.Add((p.WorldX, p.WorldZ));
            ctx.ToolStatus = "Measure: click the second point.";
            return true;
        }

        var a = _a.Value;
        var b = (p.WorldX, p.WorldZ);
        var distance = Measurement.DistanceXz(a, b);
        var bearing = Measurement.BearingDeg(a, b);
        ctx.ToolStatus = $"Distance {distance:0.0} m · bearing {bearing:0.0}°";
        _a = null;
        _preview.Lines.Clear();
        _preview.Markers.Clear();
        return true;
    }
}

/// <summary>Click to drop a scenery instance at the cursor's world position (undoable).</summary>
public sealed class PlaceSceneryTool : ToolBase
{
    private bool _haveCursor;
    private double _cursorX;
    private double _cursorZ;

    public override string Id => "scenery";
    public override string Title => "Scenery";

    public override ToolPreview? Preview
    {
        get
        {
            if (!_haveCursor)
            {
                return null;
            }

            var pv = new ToolPreview();
            pv.Markers.Add((_cursorX, _cursorZ));
            return pv;
        }
    }

    public override void Deactivated(IToolContext ctx) => _haveCursor = false;

    public override void PointerMoved(IToolContext ctx, ToolPointer p, bool pressed)
    {
        _cursorX = p.WorldX;
        _cursorZ = p.WorldZ;
        _haveCursor = true;
    }

    public override bool PointerReleased(IToolContext ctx, ToolPointer p, bool wasDrag)
    {
        if (wasDrag)
        {
            return false;
        }

        var id = WorldOps.NewSceneryId(ctx.World);
        var pos = new FuseVector3((float)p.WorldX, 0, (float)p.WorldZ);
        ctx.Undo.Execute(new UndoAction(
            "Place scenery",
            () => { WorldOps.AddScenery(ctx.World, id, "scenery", pos, new FuseVector3(0, 0, 0)); ctx.Changed(); },
            () => { WorldOps.DeleteScenery(ctx.World, id); ctx.Changed(); }));
        return true;
    }
}
