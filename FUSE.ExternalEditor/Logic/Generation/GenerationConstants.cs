namespace Fuse.ExternalEditor.Logic.Generation;

/// <summary>
/// Terrain-generation constants ported verbatim from <c>edit_tiles/constants.py</c>
/// (the <c>GEN_*</c> block). The world tile grid is anchored to a real-world geo
/// origin; elevation comes from Mapbox terrain-RGB at zoom 15, land cover from NLCD.
/// </summary>
public static class GenerationConstants
{
    public const int MapboxZoom = 15;
    public const int MapboxTileSize = 256;
    public const int HeightRes = 513;
    public const double TileDimMeters = 500.0;
    public const double OriginLat = 35.382614;
    public const double OriginLon = -83.49541;
    public const double OriginEastBias = 8.0;
    public const double OriginNorthBias = -8.0;
    public const double HeightMinG = 500.0;
    public const double HeightMaxG = 1500.0;
    public const double OffsetEastX = -66;
    public const double OffsetWestX = -98;
    public const double OffsetMaxM = 40.0;
    public const double NlcdBlur = 16.0;
    public const string NlcdUrl = "https://www.mrlc.gov/geoserver/mrlc_display/NLCD_2021_Land_Cover_L48/wms";

    /// <summary>The eight vegetation presets (0..7).</summary>
    public static readonly int[] AllVeg = { 0, 1, 2, 3, 4, 5, 6, 7 };

    /// <summary>NLCD land-cover colour → (veg preset, is-water), from GEN_NLCD_COLORS.</summary>
    public static readonly (byte R, byte G, byte B, int Preset, bool Water)[] NlcdColors =
    {
        (71, 107, 160, 0, true), (186, 216, 234, 2, true),
        (112, 163, 186, 1, true), (221, 201, 201, 5, false),
        (216, 147, 130, 6, false), (237, 0, 0, 7, false),
        (170, 0, 0, 7, false), (178, 173, 163, 6, false),
        (104, 170, 99, 0, false), (28, 99, 48, 0, false),
        (181, 201, 142, 1, false), (204, 186, 124, 3, false),
        (226, 226, 193, 5, false), (219, 216, 61, 5, false),
        (170, 112, 40, 7, false),
    };
}
