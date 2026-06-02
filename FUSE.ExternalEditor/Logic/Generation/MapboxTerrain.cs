using System;
using System.Linq;

namespace Fuse.ExternalEditor.Logic.Generation;

/// <summary>Raised when Mapbox rejects the configured token (HTTP 401/403).</summary>
public sealed class MapboxAuthException : Exception
{
    public MapboxAuthException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Pure Mapbox terrain-RGB + geo math ported from <c>edit_tiles/generate.py</c>
/// (token cleaning, elevation decode, the float32-faithful geo offset, web-mercator
/// pixel projection, 16-bit height pack, tile filename). No I/O.
/// </summary>
public static class MapboxTerrain
{
    /// <summary>Mapbox terrain-RGB → metres: <c>-10000 + 0.1·(R·65536 + G·256 + B)</c>.</summary>
    public static double DecodeElevation(byte r, byte g, byte b) =>
        -10000.0 + (0.1 * ((r * 65536.0) + (g * 256.0) + b));

    /// <summary>Strip clipboard/control junk that can invalidate an otherwise valid token.</summary>
    public static string CleanToken(string? token)
    {
        if (token is null)
        {
            return string.Empty;
        }

        var cleaned = new string(token.Where(ch => ch != '\0' && !char.IsControl(ch)).ToArray());
        return cleaned.Trim();
    }

    /// <summary>
    /// Offset a lat/lon by north/east metres, reproducing the reference's float32
    /// intermediate rounding exactly (each <c>_gen_f32</c> = a cast to <see cref="float"/>).
    /// </summary>
    public static (double Lat, double Lon) AddMeters(double lat, double lon, double north, double east)
    {
        double la = (float)lat, lo = (float)lon, n = (float)north, e = (float)east;
        double latOut = (float)(la + (float)(n / 111111.0));
        double cosArg = (float)((double)(float)0.017453292 * la);
        double denom = (float)(111111.0 * (double)(float)Math.Cos(cosArg));
        double lonOut = (float)(lo + (float)(e / denom));
        return (latOut, lonOut);
    }

    /// <summary>Lat/lon bounds (min, max) of world tile (gx, gy) relative to the geo origin.</summary>
    public static ((double Lat, double Lon) Min, (double Lat, double Lon) Max) TileBounds(int gx, int gy)
    {
        var min = AddMeters(
            GenerationConstants.OriginLat, GenerationConstants.OriginLon,
            (GenerationConstants.TileDimMeters * gy) + GenerationConstants.OriginNorthBias,
            (GenerationConstants.TileDimMeters * gx) + GenerationConstants.OriginEastBias);
        var max = AddMeters(
            GenerationConstants.OriginLat, GenerationConstants.OriginLon,
            (GenerationConstants.TileDimMeters * (gy + 1)) + GenerationConstants.OriginNorthBias,
            (GenerationConstants.TileDimMeters * (gx + 1)) + GenerationConstants.OriginEastBias);
        return (min, max);
    }

    /// <summary>World metres (x east, z north) → lat/lon, consistent with <see cref="TileBounds"/>.</summary>
    public static (double Lat, double Lon) WorldToGeo(double worldX, double worldZ) =>
        AddMeters(
            GenerationConstants.OriginLat, GenerationConstants.OriginLon,
            worldZ + GenerationConstants.OriginNorthBias, worldX + GenerationConstants.OriginEastBias);

    /// <summary>
    /// lat/lon → world metres — the approximate inverse of <see cref="WorldToGeo"/> (uses the
    /// origin latitude's cosine, exact at the origin and within ~1 m across a map-sized extent).
    /// </summary>
    public static (double WorldX, double WorldZ) GeoToWorld(double lat, double lon)
    {
        var north = (lat - GenerationConstants.OriginLat) * 111111.0;
        var cosArg = 0.017453292 * GenerationConstants.OriginLat;
        var denom = 111111.0 * Math.Cos(cosArg);
        var east = (lon - GenerationConstants.OriginLon) * denom;
        return (east - GenerationConstants.OriginEastBias, north - GenerationConstants.OriginNorthBias);
    }

    public static double LonToPixelX(double lon, int zoom) =>
        (lon + 180.0) / 360.0 * GenerationConstants.MapboxTileSize * Math.Pow(2, zoom);

    public static double LatToPixelY(double lat, int zoom)
    {
        var lr = lat * Math.PI / 180.0;
        return (1.0 - (Math.Log(Math.Tan((Math.PI / 4) + (lr / 2))) / Math.PI)) / 2.0
               * GenerationConstants.MapboxTileSize * Math.Pow(2, zoom);
    }

    /// <summary>Metres → packed 16-bit height over [GEN_HEIGHT_MIN_G, GEN_HEIGHT_MAX_G] (floor + clamp).</summary>
    public static ushort PackHeight16(double meters)
    {
        var u = Math.Floor((meters - GenerationConstants.HeightMinG)
                           / (GenerationConstants.HeightMaxG - GenerationConstants.HeightMinG) * 65535.0);
        return (ushort)Math.Clamp(u, 0.0, 65535.0);
    }

    /// <summary>Pack height (R/G) + vegetation (bits 4-6) + water (bit 7) into one (R,G,A) triple.</summary>
    public static (byte R, byte G, byte A) PackPixel(double meters, int vegPreset, bool water)
    {
        var u = PackHeight16(meters);
        var a = (byte)(((water ? 1 : 0) << 7) | ((vegPreset & 0x7) << 4));
        return ((byte)((u >> 8) & 0xFF), (byte)(u & 0xFF), a);
    }

    public static string TileDataFilename(int gx, int gy) => $"tile_{FormatCoord(gx)}_{FormatCoord(gy)}.data";

    private static string FormatCoord(int value) => value < 0 ? $"-{Math.Abs(value):D3}" : $"{value:D3}";
}
