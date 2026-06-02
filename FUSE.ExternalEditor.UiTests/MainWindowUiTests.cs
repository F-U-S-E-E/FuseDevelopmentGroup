using System;
using System.IO;
using Avalonia.Headless.XUnit;
using Fuse.ExternalEditor.Services;
using Fuse.ExternalEditor.ViewModels;
using Fuse.ExternalEditor.Views;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

/// <summary>
/// Headless UI smoke tests: they construct the real <see cref="MainWindow"/> on
/// the Avalonia headless platform (no display) and verify the view binds to the
/// view model and the FUSE.Core load path works end-to-end. This is the harness
/// the rest of the editor's automated UI tests will build on.
/// </summary>
public class MainWindowUiTests
{
    private static string ExamplePath =>
        Path.Combine(AppContext.BaseDirectory, "fuse-mod.example.json");

    // Builds the full MainWindowViewModel dependency graph for these headless smoke tests.
    private static MainWindowViewModel BuildViewModel()
    {
        var viewport = new ViewportViewModel(new TerrainTileService());
        var undo = new Fuse.Core.Authoring.UndoService();
        var trackGraph = new TrackGraphViewModel(new ProjectService(), new LiveBridgeService(), undo);
        var gen = new GenerationViewModel(new TerrainGenerationService(new System.Net.Http.HttpClient(), new TerrainTileService()), viewport);
        var osm = new OsmOverlayViewModel(new OsmTileService(new System.Net.Http.HttpClient()), viewport);
        var profile = new ProfileViewModel(trackGraph, viewport, undo);
        return new MainWindowViewModel(new ProjectService(), viewport, trackGraph, new TerrainEditViewModel(new TerrainTileService(), viewport, undo), new EntityTreeViewModel(), new LegacyImportService(), gen, osm, profile);
    }

    [AvaloniaFact]
    public void MainWindow_Binds_Title_From_ViewModel()
    {
        var viewModel = BuildViewModel();
        var window = new MainWindow { DataContext = viewModel };

        window.Show();

        // The window's Title is a compiled binding to MainWindowViewModel.Title;
        // if binding resolution works headlessly, these match.
        Assert.Equal(viewModel.Title, window.Title);
    }

    [AvaloniaFact]
    public void Loading_Example_Updates_ViewModel_Summary()
    {
        var viewModel = BuildViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        viewModel.LoadProject(ExamplePath);

        Assert.Equal("FUSE.Example.MurphyBranch", viewModel.ModId);
        Assert.True(viewModel.NodeCount >= 0);
        Assert.Contains("Loaded", viewModel.Status, StringComparison.Ordinal);
    }
}
