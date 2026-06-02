using System.Collections.Generic;
using Fuse.Core.Authoring;
using Fuse.Core.Geometry;
using Fuse.Core.Model;

namespace Fuse.ExternalEditor.Tools;

/// <summary>A pointer sample handed to tools: world (metres) + screen (px) + the node under the cursor (pre-picked by the viewport).</summary>
public readonly record struct ToolPointer(double WorldX, double WorldZ, double ScreenX, double ScreenY, string? NodeUnderCursor);

/// <summary>World-space ghost geometry a tool wants drawn (markers + line segments).</summary>
public sealed class ToolPreview
{
    public List<(double X, double Z)> Markers { get; } = new();
    public List<((double X, double Z) A, (double X, double Z) B)> Lines { get; } = new();
}

/// <summary>
/// The editing surface a tool operates on. Implemented by the view model so tools
/// stay Avalonia-free (and unit-testable with a fake context).
/// </summary>
public interface IToolContext
{
    FuseTrackDefinition Tracks { get; }
    FuseWorldDefinition World { get; }
    string? SelectedNodeId { get; set; }
    UndoService Undo { get; }

    /// <summary>Commit a generated chain with fresh ids as one undoable step.</summary>
    void CommitGenerated(string label, GeneratedTrack generated);

    /// <summary>Notify the host that the model/selection changed (refresh + re-render).</summary>
    void Changed();

    /// <summary>Transient status text a tool can surface to a HUD (e.g. measure readout).</summary>
    string? ToolStatus { get; set; }
}

/// <summary>
/// An interactive editor tool. Pointer handlers return <c>true</c> to consume the
/// event (so the viewport won't also pan/select). <see cref="PointerMoved"/> fires on
/// every move (for hover previews), with <paramref name="pressed"/> indicating a drag.
/// </summary>
public interface ITool
{
    string Id { get; }
    string Title { get; }
    ToolPreview? Preview { get; }
    void Activated(IToolContext ctx);
    void Deactivated(IToolContext ctx);
    bool PointerPressed(IToolContext ctx, ToolPointer p);
    void PointerMoved(IToolContext ctx, ToolPointer p, bool pressed);
    bool PointerReleased(IToolContext ctx, ToolPointer p, bool wasDrag);
}

/// <summary>No-op defaults so each tool only overrides what it needs.</summary>
public abstract class ToolBase : ITool
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    public virtual ToolPreview? Preview => null;
    public virtual void Activated(IToolContext ctx) { }
    public virtual void Deactivated(IToolContext ctx) { }
    public virtual bool PointerPressed(IToolContext ctx, ToolPointer p) => false;
    public virtual void PointerMoved(IToolContext ctx, ToolPointer p, bool pressed) { }
    public virtual bool PointerReleased(IToolContext ctx, ToolPointer p, bool wasDrag) => false;
}

/// <summary>Holds the registered tools and the active one; first tool is the default.</summary>
public sealed class ToolHost
{
    private readonly Dictionary<string, ITool> _byId = new();

    public ToolHost(IReadOnlyList<ITool> tools)
    {
        System.ArgumentNullException.ThrowIfNull(tools);
        if (tools.Count == 0)
        {
            throw new System.ArgumentException("At least one tool must be registered.", nameof(tools));
        }

        Tools = tools;
        foreach (var t in tools)
        {
            if (!_byId.TryAdd(t.Id, t))
            {
                throw new System.ArgumentException($"Duplicate tool id '{t.Id}'.", nameof(tools));
            }
        }

        Active = tools[0];
    }

    public IReadOnlyList<ITool> Tools { get; }

    public ITool Active { get; private set; }

    public bool Activate(string id, IToolContext ctx)
    {
        if (!_byId.TryGetValue(id, out var tool) || ReferenceEquals(tool, Active))
        {
            return false;
        }

        Active.Deactivated(ctx);
        Active = tool;
        Active.Activated(ctx);
        return true;
    }
}
