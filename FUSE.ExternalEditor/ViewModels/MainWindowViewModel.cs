using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Fuse.Core.Model;
using Fuse.ExternalEditor.Services;

namespace Fuse.ExternalEditor.ViewModels;

/// <summary>
/// Root view model for the main window. Deliberately UI-free so it can be
/// unit-tested without a display: it depends only on <see cref="IProjectService"/>
/// and exposes plain observable state that the view binds to.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IProjectService _projectService;
    private readonly ILegacyImportService _legacyImport;

    [ObservableProperty]
    private string _title = "FUSE External Editor";

    [ObservableProperty]
    private string _status = "No project loaded.";

    [ObservableProperty]
    private string? _modId;

    [ObservableProperty]
    private string? _modName;

    [ObservableProperty]
    private int _nodeCount;

    [ObservableProperty]
    private int _segmentCount;

    [ObservableProperty]
    private int _sceneryCount;

    public MainWindowViewModel(
        IProjectService projectService, ViewportViewModel viewport, TrackGraphViewModel trackGraph,
        TerrainEditViewModel terrainEdit, EntityTreeViewModel entityTree, ILegacyImportService legacyImport,
        GenerationViewModel generation, OsmOverlayViewModel osmOverlay, ProfileViewModel profile)
    {
        _projectService = projectService;
        _legacyImport = legacyImport;
        Viewport = viewport;
        TrackGraph = trackGraph;
        TerrainEdit = terrainEdit;
        EntityTree = entityTree;
        Generation = generation;
        OsmOverlay = osmOverlay;
        Profile = profile;
        TrackGraph.ProjectLoaded += () => EntityTree.Build(TrackGraph.Project);
    }

    /// <summary>The terrain viewport view model (Phase 1 main content).</summary>
    public ViewportViewModel Viewport { get; }

    /// <summary>The track-graph authoring view model (Phase 2).</summary>
    public TrackGraphViewModel TrackGraph { get; }

    /// <summary>The terrain-painting view model (Phase 5).</summary>
    public TerrainEditViewModel TerrainEdit { get; }

    /// <summary>The loaded mod's entity tree (Phase 8).</summary>
    public EntityTreeViewModel EntityTree { get; }

    /// <summary>Terrain generation (Mapbox/NLCD) controls (Phase 6).</summary>
    public GenerationViewModel Generation { get; }

    /// <summary>OSM guide-overlay controls (Phase 6).</summary>
    public OsmOverlayViewModel OsmOverlay { get; }

    /// <summary>Elevation/alignment profile + arc/min-radius diagnostics (Phase 7).</summary>
    public ProfileViewModel Profile { get; }

    /// <summary>The calculator panel (Phase 8).</summary>
    public CalculatorViewModel Calculator { get; } = new();

    /// <summary>Prompt for id/name via the dialog service and start a fresh mod.</summary>
    public async Task NewModAsync(IDialogService dialog)
    {
        var id = await dialog.PromptInputAsync("New mod", "Mod id (e.g. my.cool.route):", "untitled");
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var name = await dialog.PromptInputAsync("New mod", "Display name:", id) ?? id;
        TrackGraph.NewProject(id.Trim(), name.Trim());
        Status = $"Started new mod '{id.Trim()}'.";
    }

    /// <summary>
    /// Convert a legacy mod folder to a sibling <c>&lt;folder&gt;.FUSE</c> package and open
    /// the first converted fragment (which rebuilds the entity tree via ProjectLoaded).
    /// </summary>
    public LegacyImportResult ImportLegacyMod(string sourceFolder)
    {
        var outputFolder = sourceFolder.TrimEnd('/', '\\') + ".FUSE";
        var result = _legacyImport.Convert(sourceFolder, outputFolder);
        if (result.Success && result.FirstFragmentPath is { } fragment)
        {
            TrackGraph.OpenProject(fragment);
            Status = $"Imported '{result.ModId}' → {result.WrittenFragments.Count} fragment(s) into {System.IO.Path.GetFileName(outputFolder)}.";
        }
        else
        {
            Status = "Legacy import failed: " + (result.Messages.Count > 0 ? result.Messages[^1] : "unknown error");
        }

        return result;
    }

    /// <summary>
    /// Loads a <c>*.fuse.json</c> (or <c>.bson</c>) package via FUSE.Core and
    /// refreshes the summary fields. Returns the loaded definition so callers
    /// (and tests) can assert on it.
    /// </summary>
    public FuseModDefinition LoadProject(string path)
    {
        var definition = _projectService.Load(path);

        ModId = definition.Id;
        ModName = definition.Name;
        NodeCount = definition.Tracks?.Nodes?.Count ?? 0;
        SegmentCount = definition.Tracks?.Segments?.Count ?? 0;
        SceneryCount = definition.World?.Scenery?.Count ?? 0;
        Status = $"Loaded '{definition.Id}' — {NodeCount} nodes, {SegmentCount} segments, {SceneryCount} scenery.";

        return definition;
    }
}
