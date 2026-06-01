using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fuse.Core.Authoring;
using Fuse.ExternalEditor.Logic;
using Fuse.ExternalEditor.Models.Terrain;
using Fuse.ExternalEditor.Services;

namespace Fuse.ExternalEditor.ViewModels;

/// <summary>
/// Terrain-painting view model: brush parameters + stroke lifecycle. The viewport
/// drives <see cref="BeginStroke"/>/<see cref="Dab"/>/<see cref="EndStroke"/>; a
/// completed stroke is pushed to the shared <see cref="UndoService"/> as one step,
/// so terrain edits share the editor's undo stack with track/world edits.
/// </summary>
public partial class TerrainEditViewModel : ViewModelBase, ITerrainEditor
{
    private readonly ITerrainTileService _tiles;
    private readonly ViewportViewModel _viewport;
    private readonly UndoService _undo;
    private TerrainStroke? _stroke;
    private FbmNoise? _noise;
    private int _noiseSeed;

    [ObservableProperty]
    private bool _editingEnabled;

    [ObservableProperty]
    private TerrainEditMode _editMode = TerrainEditMode.Height;

    [ObservableProperty]
    private TerrainBrushKind _brushKind = TerrainBrushKind.Raise;

    [ObservableProperty]
    private int _brushRadius = 20;

    [ObservableProperty]
    private float _brushStrength = 0.02f;

    [ObservableProperty]
    private int _vegPreset = 1;

    [ObservableProperty]
    private int _heightTarget = 32768;

    [ObservableProperty]
    private double _noiseScale = 64;

    [ObservableProperty]
    private string _status = "Terrain edit: off.";

    public TerrainEditViewModel(ITerrainTileService tiles, ViewportViewModel viewport, UndoService undo)
    {
        _tiles = tiles;
        _viewport = viewport;
        _undo = undo;
    }

    public bool Active => EditingEnabled;

    public int BrushScreenRadius => BrushRadius;

    public void BeginStroke()
    {
        _stroke = new TerrainStroke();
        _noise = BrushKind == TerrainBrushKind.Noise
            ? FbmNoise.Build(TerrainConstants.OverviewRes, NoiseScale, _noiseSeed++)
            : null;
    }

    public void Dab(TerrainTile tile, double centreRow, double centreCol, int radiusTilePx)
    {
        if (_stroke is null)
        {
            return;
        }

        var settings = new BrushSettings
        {
            Mode = EditMode,
            Kind = BrushKind,
            Strength = BrushStrength,
            HeightTarget = HeightTarget,
            VegPreset = VegPreset,
            NoiseScale = NoiseScale,
        };
        TerrainBrush.Apply(tile, centreRow, centreCol, radiusTilePx, settings, _noise, _stroke);
    }

    public void EndStroke()
    {
        var action = _stroke?.Commit();
        _stroke = null;
        if (action is not null)
        {
            _undo.Execute(action);
            Status = $"Edited terrain ({BrushKind}).";
        }
    }

    [RelayCommand]
    private void ToggleEditing()
    {
        EditingEnabled = !EditingEnabled;
        Status = EditingEnabled ? $"Terrain edit: {BrushKind} ({EditMode})." : "Terrain edit: off.";
    }

    [RelayCommand]
    private void SetEditMode(string mode)
    {
        EditMode = Enum.Parse<TerrainEditMode>(mode);
        Status = $"Terrain edit: {BrushKind} ({EditMode}).";
    }

    [RelayCommand]
    private void SetBrush(string kind)
    {
        BrushKind = Enum.Parse<TerrainBrushKind>(kind);
        Status = $"Terrain edit: {BrushKind} ({EditMode}).";
    }

    [RelayCommand]
    private void SaveTerrain()
    {
        var saved = 0;
        foreach (var tile in _viewport.Grid.Tiles.Values)
        {
            if (tile.Dirty && tile.Path is not null)
            {
                _tiles.SaveTile(tile);
                saved++;
            }
        }

        Status = saved == 0 ? "No edited tiles to save." : $"Saved {saved} edited tile(s).";
    }
}
