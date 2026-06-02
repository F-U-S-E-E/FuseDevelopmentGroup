using System;
using Fuse.ExternalEditor.Models.Terrain;

namespace Fuse.ExternalEditor.Logic;

/// <summary>
/// Pure terrain tile renderer, ported from <c>edit_tiles/generate.py</c>
/// (<c>render_tile</c> + <c>compute_hillshade</c>). Produces a row-major RGBA
/// pixel buffer (4 bytes/pixel, A=255) sized <c>Res*Res</c>. Kept Avalonia-free
/// for unit testing; the viewport wraps the buffer in a bitmap.
/// </summary>
public static class TerrainRenderer
{
    /// <summary>Vegetation preset colours (from <c>constants.VEG_COLORS</c>).</summary>
    public static readonly (byte R, byte G, byte B)[] VegColors =
    {
        (20, 70, 25),
        (30, 90, 40),
        (55, 105, 35),
        (100, 130, 50),
        (140, 160, 65),
        (170, 185, 80),
        (200, 185, 110),
        (220, 200, 140),
    };

    private static readonly float LightX;
    private static readonly float LightY;
    private static readonly float LightZ;

    static TerrainRenderer()
    {
        float lx = -0.6f, ly = -0.6f, lz = 1.0f;
        var ll = MathF.Sqrt((lx * lx) + (ly * ly) + (lz * lz));
        LightX = lx / ll;
        LightY = ly / ll;
        LightZ = lz / ll;
    }

    /// <summary>Render a tile to a row-major RGBA buffer (length Res*Res*4).</summary>
    public static byte[] Render(TerrainTile tile, TerrainMode mode, bool hillshade)
    {
        if (tile is null)
        {
            throw new ArgumentNullException(nameof(tile));
        }

        var res = tile.Res;
        var n = res * res;
        var rgba = new byte[n * 4];
        var shade = hillshade ? ComputeShade(tile) : null;

        for (var i = 0; i < n; i++)
        {
            var s = shade is null ? 1f : shade[i];
            byte r, g, b;

            switch (mode)
            {
                case TerrainMode.Height:
                    r = RoundByte(tile.R[i] * s);
                    g = RoundByte(tile.G[i] * s);
                    b = 0;
                    break;

                case TerrainMode.Veg:
                    var color = VegColors[(tile.A[i] >> 4) & 0x7];
                    r = TruncByte(color.R * s);
                    g = TruncByte(color.G * s);
                    b = TruncByte(color.B * s);
                    break;

                default: // Water
                    if (((tile.A[i] >> 7) & 1) == 1)
                    {
                        r = 0; g = 100; b = 220;
                    }
                    else
                    {
                        r = 18; g = 22; b = 30;
                    }

                    break;
            }

            var o = i * 4;
            rgba[o] = r;
            rgba[o + 1] = g;
            rgba[o + 2] = b;
            rgba[o + 3] = 255;
        }

        return rgba;
    }

    /// <summary>
    /// Per-pixel shade factor <c>0.25 + 0.75*hillshade</c> over normalized
    /// heights, matching <c>render_tile</c>. Central-difference gradient × 20,
    /// dotted with the fixed light direction.
    /// </summary>
    private static float[] ComputeShade(TerrainTile tile)
    {
        var res = tile.Res;
        var heights = new float[res * res];
        for (var i = 0; i < heights.Length; i++)
        {
            heights[i] = tile.Height16(i) / 65535f;
        }

        var shade = new float[res * res];
        for (var y = 0; y < res; y++)
        {
            for (var x = 0; x < res; x++)
            {
                var i = (y * res) + x;
                var dx = 0f;
                var dy = 0f;
                if (x > 0 && x < res - 1)
                {
                    dx = (heights[i + 1] - heights[i - 1]) * 20f;
                }

                if (y > 0 && y < res - 1)
                {
                    dy = (heights[i + res] - heights[i - res]) * 20f;
                }

                float nx = -dx, ny = -dy, nz = 1f;
                var nlen = MathF.Max(MathF.Sqrt((nx * nx) + (ny * ny) + (nz * nz)), 1e-6f);
                var dot = ((nx / nlen) * LightX) + ((ny / nlen) * LightY) + ((nz / nlen) * LightZ);
                dot = Math.Clamp(dot, 0f, 1f);
                shade[i] = 0.25f + (0.75f * dot);
            }
        }

        return shade;
    }

    // height mode uses round-then-cast (np.round → uint8); veg mode uses
    // clip-then-cast (np.clip → astype truncates). Preserve both exactly.
    private static byte RoundByte(float v) =>
        (byte)Math.Clamp((int)Math.Round(v, MidpointRounding.ToEven), 0, 255);

    private static byte TruncByte(float v) =>
        (byte)Math.Clamp((int)v, 0, 255);
}
