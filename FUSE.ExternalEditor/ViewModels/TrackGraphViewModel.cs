using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fuse.Core.Authoring;
using Fuse.Core.Geometry;
using Fuse.Core.Model;
using Fuse.ExternalEditor.Services;
using Fuse.ExternalEditor.Tools;

namespace Fuse.ExternalEditor.ViewModels;

/// <summary>
/// View model for authoring a FUSE mod's track graph: loads/saves <c>*.fuse.json</c>,
/// exposes the live <see cref="FuseTrackDefinition"/> the viewport renders, the current
/// selection, and add/delete/generate commands routed through <see cref="TrackOps"/> /
/// <see cref="TrackGenerators"/>. Every mutation is recorded on an <see cref="UndoService"/>.
/// </summary>
public partial class TrackGraphViewModel : ViewModelBase, IToolContext
{
    private readonly IProjectService _projects;
    private readonly ILiveBridgeService _bridge;
    private readonly UndoService _undo;
    private readonly ToolHost _toolHost;
    private FuseModDefinition _project = new() { Id = "untitled", Name = "Untitled" };

    [ObservableProperty]
    private FuseTrackDefinition _tracks = new();

    [ObservableProperty]
    private FuseWorldDefinition _world = new();

    [ObservableProperty]
    private string _activeToolId = "select";

    [ObservableProperty]
    private string? _selectedNodeId;

    [ObservableProperty]
    private string? _toolStatus;

    [ObservableProperty]
    private string _status = "No mod loaded.";

    /// <summary>Path to the running game's <c>Mods</c> folder (live-bridge target).</summary>
    [ObservableProperty]
    private string _gameModsPath = string.Empty;

    [ObservableProperty]
    private string _bridgeStatus = "Bridge: idle.";

    public TrackGraphViewModel(IProjectService projects, ILiveBridgeService bridge, UndoService undo)
    {
        _projects = projects;
        _bridge = bridge;
        _undo = undo;
        // Any undo-stack change (incl. terrain strokes pushed by the terrain editor) refreshes the buttons.
        _undo.Changed += () =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        };
        Tracks = _project.Tracks;
        World = _project.World;
        _toolHost = new ToolHost(new ITool[]
        {
            new SelectTool(),
            new MoveNodeTool(),
            new ConnectTool(),
            new PlaceGeneratorTool("turnout", "Turnout", (x, y, z) => TrackGenerators.Turnout(x, y, z, 0, divergeAngle: 10, legLength: 30)),
            new PlaceGeneratorTool("wye", "Wye", (x, y, z) => TrackGenerators.Wye(x, y, z, 0, leftAngle: 10, rightAngle: 10, legLength: 30)),
            new PlaceGeneratorTool("curve", "Curve", (x, y, z) => TrackGenerators.Curve(x, y, z, 0, radius: 200, degrees: 45)),
            new PlaceSceneryTool(),
            new MeasureTool(),
        });
    }

    public IReadOnlyList<ITool> Tools => _toolHost.Tools;

    public ITool ActiveTool => _toolHost.Active;

    // Explicit impl: avoids colliding with the [RelayCommand] Undo() method.
    UndoService IToolContext.Undo => _undo;

    /// <summary>The full loaded mod definition (for the entity tree / packaging).</summary>
    public FuseModDefinition Project => _project;

    /// <summary>Raised after a project loads, so dependents (e.g. the entity tree) can rebuild.</summary>
    public event System.Action? ProjectLoaded;

    public int NodeCount => Tracks.Nodes.Count;

    public int SegmentCount => Tracks.Segments.Count;

    public bool CanUndo => _undo.CanUndo;

    public bool CanRedo => _undo.CanRedo;

    public string SelectedNodeSummary
    {
        get
        {
            if (SelectedNodeId is { } id && Tracks.Nodes.TryGetValue(id, out var node) && node is not null)
            {
                var p = node.Position;
                var r = node.Rotation;
                return $"Node {id}\nPos  {p.x:0.##}, {p.y:0.##}, {p.z:0.##}\nRot  {r.x:0.##}, {r.y:0.##}, {r.z:0.##}\nValency {TrackOps.NodeValency(Tracks, id)}";
            }

            return "No node selected.";
        }
    }

    public void OpenProject(string path)
    {
        _project = _projects.Load(path);
        Tracks = _project.Tracks; // new instance → viewport rebinds + re-centers
        World = _project.World;
        SelectedNodeId = null;
        _undo.Clear();
        Status = $"Loaded '{_project.Id}' — {NodeCount} nodes, {SegmentCount} segments.";
        AfterMutation();
        ProjectLoaded?.Invoke();
    }

    public void Save(string path)
    {
        _projects.Save(_project, path);
        Status = $"Saved {NodeCount} nodes, {SegmentCount} segments → {Path.GetFileName(path)}.";
    }

    /// <summary>Reset to a fresh, empty mod with the given id/name (the "New Mod" flow).</summary>
    public void NewProject(string id, string name)
    {
        _project = new FuseModDefinition { Id = id, Name = name };
        Tracks = _project.Tracks;
        World = _project.World;
        SelectedNodeId = null;
        _undo.Clear();
        Status = $"New mod '{id}' — 0 nodes.";
        AfterMutation();
        ProjectLoaded?.Invoke();
    }

    [RelayCommand]
    private void AddNode()
    {
        var id = TrackOps.NewNodeId(Tracks);
        _undo.Execute(new UndoAction(
            "Add node",
            () =>
            {
                TrackOps.AddNode(Tracks, id, new FuseVector3(0, 0, 0), new FuseVector3(0, 0, 0));
                SelectedNodeId = id;
                AfterMutation();
            },
            () =>
            {
                TrackOps.DeleteNode(Tracks, id);
                SelectedNodeId = null;
                AfterMutation();
            }));
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedNodeId is not { } id || !Tracks.Nodes.TryGetValue(id, out var node) || node is null)
        {
            return;
        }

        // Capture the node and the segments that the cascade delete will remove so undo can restore them.
        var removedSegments = Tracks.Segments
            .Where(kv => kv.Value != null && (kv.Value.StartNodeId == id || kv.Value.EndNodeId == id))
            .ToList();

        _undo.Execute(new UndoAction(
            "Delete node",
            () =>
            {
                TrackOps.DeleteNode(Tracks, id);
                SelectedNodeId = null;
                AfterMutation();
            },
            () =>
            {
                Tracks.Nodes[id] = node;
                foreach (var kv in removedSegments)
                {
                    Tracks.Segments[kv.Key] = kv.Value;
                }

                SelectedNodeId = id;
                AfterMutation();
            }));
    }

    [RelayCommand]
    private void GenerateTurnout()
    {
        var at = SelectionAnchor(out var rotation);
        CommitGenerated("Generate turnout", TrackGenerators.Turnout(at.x, at.y, at.z, rotation.y, divergeAngle: 10, legLength: 30));
    }

    [RelayCommand]
    private void GenerateWye()
    {
        var at = SelectionAnchor(out var rotation);
        CommitGenerated("Generate wye", TrackGenerators.Wye(at.x, at.y, at.z, rotation.y, leftAngle: 10, rightAngle: 10, legLength: 30));
    }

    [RelayCommand]
    private void GenerateCurve()
    {
        var at = SelectionAnchor(out var rotation);
        CommitGenerated("Generate curve", TrackGenerators.Curve(at.x, at.y, at.z, rotation.y, radius: 200, degrees: 45));
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        _undo.Undo();
        AfterMutation();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        _undo.Redo();
        AfterMutation();
    }

    [RelayCommand]
    private void ActivateTool(string? id)
    {
        if (id != null && _toolHost.Activate(id, this))
        {
            ActiveToolId = id;
            OnPropertyChanged(nameof(ActiveTool));
        }
    }

    /// <summary>IToolContext: tools call this after mutating the model or selection.</summary>
    public void Changed() => AfterMutation();

    /// <summary>
    /// Write the mod to <c>&lt;gameMods&gt;/&lt;id&gt;/&lt;id&gt;.fuse.json</c> and drop a reload
    /// command for the in-game FUSE.LiveBridge to pick up. (Editing an already-installed
    /// package; creating a brand-new package's Info.json is Phase 8 packaging.)
    /// </summary>
    [RelayCommand]
    private void PushToGame()
    {
        if (string.IsNullOrWhiteSpace(GameModsPath))
        {
            Status = "Set the game Mods folder first (Set Mods…).";
            return;
        }

        var packageId = string.IsNullOrWhiteSpace(_project.Id) ? "untitled" : _project.Id;
        var packageDir = Path.Combine(GameModsPath, packageId);
        Directory.CreateDirectory(packageDir);
        _projects.Save(_project, Path.Combine(packageDir, packageId + ".fuse.json"));
        _bridge.WriteReloadCommand(packageDir, packageId, "editor push");
        Status = $"Pushed '{packageId}' to game; reload requested.";
        RefreshBridgeStatus();
    }

    [RelayCommand]
    private void RefreshBridgeStatus()
    {
        if (string.IsNullOrWhiteSpace(GameModsPath))
        {
            BridgeStatus = "Bridge: no Mods folder set.";
            return;
        }

        var state = _bridge.ReadHeartbeat(GameModsPath);
        BridgeStatus = _bridge.Classify(state, DateTime.UtcNow) switch
        {
            BridgeConnection.Connected =>
                $"Bridge: connected (applied {state?.AppliedCount ?? 0}{(state?.CanApply == false ? ", MP client — reloads skipped" : string.Empty)}).",
            BridgeConnection.Stale => "Bridge: stale heartbeat (game not running?).",
            _ => "Bridge: not connected.",
        };
    }

    partial void OnSelectedNodeIdChanged(string? value) => OnPropertyChanged(nameof(SelectedNodeSummary));

    /// <summary>Place generated track at the selected node (using its rotation), else at the origin.</summary>
    private FuseVector3 SelectionAnchor(out FuseVector3 rotation)
    {
        if (SelectedNodeId is { } id && Tracks.Nodes.TryGetValue(id, out var node) && node is not null)
        {
            rotation = node.Rotation;
            return node.Position;
        }

        rotation = new FuseVector3(0, 0, 0);
        return new FuseVector3(0, 0, 0);
    }

    public void CommitGenerated(string label, GeneratedTrack generated)
    {
        List<string>? nodeIds = null;
        List<string>? segmentIds = null;
        _undo.Execute(new UndoAction(
            label,
            () =>
            {
                var (n, s) = TrackGenerators.Commit(Tracks, generated);
                nodeIds = n;
                segmentIds = s;
                if (n.Count > 0)
                {
                    SelectedNodeId = n[0];
                }

                AfterMutation();
            },
            () =>
            {
                if (segmentIds != null)
                {
                    foreach (var sid in segmentIds)
                    {
                        TrackOps.DeleteSegment(Tracks, sid);
                    }
                }

                if (nodeIds != null)
                {
                    foreach (var nid in nodeIds)
                    {
                        Tracks.Nodes.Remove(nid);
                    }
                }

                SelectedNodeId = null;
                AfterMutation();
            }));
    }

    private void AfterMutation()
    {
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(SegmentCount));
        OnPropertyChanged(nameof(SelectedNodeSummary));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }
}
