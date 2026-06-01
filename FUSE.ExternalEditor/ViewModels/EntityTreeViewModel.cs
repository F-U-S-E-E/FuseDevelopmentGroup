using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Fuse.Core.Model;

namespace Fuse.ExternalEditor.ViewModels;

/// <summary>One node in the entity tree: a section header, group, or a concrete entity.</summary>
public sealed class EntityNode
{
    public EntityNode(string label, string kind, string? entityId = null)
    {
        Label = label;
        Kind = kind;
        EntityId = entityId;
    }

    public string Label { get; }

    /// <summary>"tracks" | "node" | "segment" | "world" | "scenery" | "spliney" | "operations" | "industry" | "load" | group headers.</summary>
    public string Kind { get; }

    /// <summary>The entity's id when this node maps to a concrete entity (else null).</summary>
    public string? EntityId { get; }

    public ObservableCollection<EntityNode> Children { get; } = new();
}

/// <summary>
/// Builds a navigable tree of a <see cref="FuseModDefinition"/>'s entities
/// (tracks → nodes/segments, world → scenery/splineys, operations →
/// industries/loads). UI-free + testable; the view binds to <see cref="Roots"/>.
/// </summary>
public partial class EntityTreeViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<EntityNode> _roots = new();

    [ObservableProperty]
    private EntityNode? _selected;

    public void Build(FuseModDefinition definition)
    {
        System.ArgumentNullException.ThrowIfNull(definition);

        var roots = new ObservableCollection<EntityNode>();

        var tracks = definition.Tracks;
        var tracksNode = new EntityNode($"Tracks — {tracks.Nodes.Count} nodes, {tracks.Segments.Count} segments", "tracks");
        var nodeGroup = new EntityNode("Nodes", "node-group");
        foreach (var id in tracks.Nodes.Keys)
        {
            nodeGroup.Children.Add(new EntityNode(id, "node", id));
        }

        var segmentGroup = new EntityNode("Segments", "segment-group");
        foreach (var id in tracks.Segments.Keys)
        {
            segmentGroup.Children.Add(new EntityNode(id, "segment", id));
        }

        tracksNode.Children.Add(nodeGroup);
        tracksNode.Children.Add(segmentGroup);
        roots.Add(tracksNode);

        var world = definition.World;
        var worldNode = new EntityNode($"World — {world.Scenery.Count} scenery, {world.Splineys.Count} splineys", "world");
        var sceneryGroup = new EntityNode("Scenery", "scenery-group");
        foreach (var id in world.Scenery.Keys)
        {
            sceneryGroup.Children.Add(new EntityNode(id, "scenery", id));
        }

        var splineyGroup = new EntityNode("Splineys", "spliney-group");
        foreach (var id in world.Splineys.Keys)
        {
            splineyGroup.Children.Add(new EntityNode(id, "spliney", id));
        }

        worldNode.Children.Add(sceneryGroup);
        worldNode.Children.Add(splineyGroup);
        roots.Add(worldNode);

        var ops = definition.Operations;
        var opsNode = new EntityNode($"Operations — {ops.Industries.Count} industries, {ops.Loads.Count} loads", "operations");
        var industryGroup = new EntityNode("Industries", "industry-group");
        foreach (var id in ops.Industries.Keys)
        {
            industryGroup.Children.Add(new EntityNode(id, "industry", id));
        }

        var loadGroup = new EntityNode("Loads", "load-group");
        foreach (var id in ops.Loads.Keys)
        {
            loadGroup.Children.Add(new EntityNode(id, "load", id));
        }

        opsNode.Children.Add(industryGroup);
        opsNode.Children.Add(loadGroup);
        roots.Add(opsNode);

        Roots = roots;
    }
}
