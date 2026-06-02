using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fuse.ExternalEditor.Logic.Generation;
using Fuse.ExternalEditor.Models.Terrain;
using Fuse.ExternalEditor.Rendering;
using Fuse.ExternalEditor.Services;

namespace Fuse.ExternalEditor.ViewModels;

/// <summary>
/// Fetches an OSM raster overlay for the loaded terrain region and exposes it
/// (geo-aligned to world metres) for the viewport to draw as a guide trace.
/// </summary>
public partial class OsmOverlayViewModel : ViewModelBase
{
    private readonly IOsmTileService _osm;
    private readonly ViewportViewModel _viewport;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private int _zoom = 14;

    [ObservableProperty]
    private bool _isFetching;

    [ObservableProperty]
    private OsmOverlay? _overlay;

    [ObservableProperty]
    private string _status = "OSM overlay off.";

    public OsmOverlayViewModel(IOsmTileService osm, ViewportViewModel viewport)
    {
        _osm = osm;
        _viewport = viewport;
    }

    [RelayCommand]
    private async Task FetchAsync(CancellationToken ct)
    {
        var grid = _viewport.Grid;
        if (grid.Count == 0)
        {
            Status = "Load terrain first (the overlay aligns to the loaded region).";
            return;
        }

        // Terrain tile index → world metres (tile gx spans worldX = gx * UnityTileMeters).
        var minWorldX = grid.MinX * TerrainConstants.UnityTileMeters;
        var maxWorldX = (grid.MaxX + 1) * TerrainConstants.UnityTileMeters;
        var minWorldZ = grid.MinY * TerrainConstants.UnityTileMeters;
        var maxWorldZ = (grid.MaxY + 1) * TerrainConstants.UnityTileMeters;
        var sw = MapboxTerrain.WorldToGeo(minWorldX, minWorldZ);
        var ne = MapboxTerrain.WorldToGeo(maxWorldX, maxWorldZ);

        IsFetching = true;
        try
        {
            var mosaic = await _osm.FetchAsync(sw.Lat, sw.Lon, ne.Lat, ne.Lon, Zoom, ct).ConfigureAwait(true);
            var nw = MapboxTerrain.GeoToWorld(mosaic.NorthLat, mosaic.WestLon);
            var se = MapboxTerrain.GeoToWorld(mosaic.SouthLat, mosaic.EastLon);
            Overlay = new OsmOverlay(
                mosaic.Rgba, mosaic.Width, mosaic.Height,
                Math.Min(nw.WorldX, se.WorldX), Math.Max(nw.WorldX, se.WorldX),
                Math.Min(nw.WorldZ, se.WorldZ), Math.Max(nw.WorldZ, se.WorldZ));
            Enabled = true;
            Status = $"OSM: {mosaic.TileCount} tile(s) @ z{Zoom}.";
        }
        catch (Exception e)
        {
            Status = "OSM fetch failed: " + e.Message;
        }
        finally
        {
            IsFetching = false;
        }
    }

    partial void OnEnabledChanged(bool value) => OnPropertyChanged(nameof(EffectiveOverlay));

    partial void OnOverlayChanged(OsmOverlay? value) => OnPropertyChanged(nameof(EffectiveOverlay));

    /// <summary>The overlay to draw, or null when disabled — what the viewport binds to.</summary>
    public OsmOverlay? EffectiveOverlay => Enabled ? Overlay : null;
}
