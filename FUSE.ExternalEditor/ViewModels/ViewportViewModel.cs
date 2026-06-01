using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fuse.ExternalEditor.Models.Terrain;
using Fuse.ExternalEditor.Services;

namespace Fuse.ExternalEditor.ViewModels;

/// <summary>
/// View model for the terrain viewport: owns the loaded <see cref="TileGrid"/>,
/// the render <see cref="Mode"/>, and the <see cref="Hillshade"/> toggle. UI-free
/// and testable; the <see cref="Controls.MapViewport"/> binds to these.
/// </summary>
public partial class ViewportViewModel : ViewModelBase
{
    private readonly ITerrainTileService _tileService;

    [ObservableProperty]
    private TileGrid _grid = new();

    [ObservableProperty]
    private TerrainMode _mode = TerrainMode.Height;

    [ObservableProperty]
    private bool _hillshade = true;

    [ObservableProperty]
    private string _status = "No terrain loaded.";

    public ViewportViewModel(ITerrainTileService tileService)
    {
        _tileService = tileService;
    }

    public int TileCount => Grid.Count;

    /// <summary>Load every <c>tile_*.data</c> in a folder and rebind the grid.</summary>
    public void LoadFolder(string directory)
    {
        var tiles = _tileService.LoadFolder(directory);
        var grid = new TileGrid();
        grid.AddRange(tiles);
        Grid = grid; // replacing the instance rebinds + re-centers the viewport

        Status = grid.Count == 0
            ? $"No tiles found in {directory}."
            : $"Loaded {grid.Count} tiles  (x {grid.MinX}..{grid.MaxX}, y {grid.MinY}..{grid.MaxY}).";
    }

    [RelayCommand]
    private void SetHeightMode() => Mode = TerrainMode.Height;

    [RelayCommand]
    private void SetVegMode() => Mode = TerrainMode.Veg;

    [RelayCommand]
    private void SetWaterMode() => Mode = TerrainMode.Water;
}
