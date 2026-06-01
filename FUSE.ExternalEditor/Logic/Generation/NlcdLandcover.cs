namespace Fuse.ExternalEditor.Logic.Generation;

/// <summary>
/// NLCD land-cover → vegetation/water mapping ported from <c>edit_tiles/generate.py</c>
/// (<c>_gen_color_to_preset</c> + <c>_gen_veg_water</c>): exact colour match, else nearest
/// by squared RGB distance; optional blur + per-preset argmax; gutter crop.
/// </summary>
public static class NlcdLandcover
{
    public static (int Preset, bool Water) ColorToPreset(byte r, byte g, byte b)
    {
        var colors = GenerationConstants.NlcdColors;
        foreach (var c in colors)
        {
            if (c.R == r && c.G == g && c.B == b)
            {
                return (c.Preset, c.Water);
            }
        }

        var best = 0;
        var bestWater = false;
        var bestDist = long.MaxValue;
        foreach (var c in colors)
        {
            long dr = r - c.R, dg = g - c.G, db = b - c.B;
            var d = (dr * dr) + (dg * dg) + (db * db);
            if (d < bestDist)
            {
                bestDist = d;
                best = c.Preset;
                bestWater = c.Water;
            }
        }

        return (best, bestWater);
    }

    /// <summary>
    /// Build veg + water grids at <paramref name="outRes"/> from a fetched land-cover RGB
    /// grid at <c>outRes + 2·gutter</c> (row-major RGB triples). With blur, one-hot each
    /// preset, Gaussian-smooth, argmax; water is the blurred mask ≥ 0.5. Then crop the gutter.
    /// </summary>
    public static (byte[] Veg, bool[] Water) BuildVegWater(byte[] rgb, int fetchRes, int outRes, int gutter, double blurSigma)
    {
        if (rgb is null)
        {
            throw new System.ArgumentNullException(nameof(rgb));
        }

        if (fetchRes <= 0 || outRes <= 0 || gutter < 0 || fetchRes != outRes + (2 * gutter))
        {
            throw new System.ArgumentException("fetchRes must be positive and equal outRes + 2*gutter.");
        }

        var n = fetchRes * fetchRes;
        if (rgb.Length < n * 3)
        {
            throw new System.ArgumentException("RGB buffer must have at least fetchRes*fetchRes*3 entries.", nameof(rgb));
        }

        var vegRaw = new byte[n];
        var waterRaw = new float[n];
        for (var i = 0; i < n; i++)
        {
            var (preset, water) = ColorToPreset(rgb[i * 3], rgb[(i * 3) + 1], rgb[(i * 3) + 2]);
            vegRaw[i] = (byte)preset;
            waterRaw[i] = water ? 1f : 0f;
        }

        byte[] vegFull;
        bool[] waterFull;
        if (blurSigma > 0)
        {
            var allVeg = GenerationConstants.AllVeg;
            var blurred = new float[allVeg.Length][];
            for (var ci = 0; ci < allVeg.Length; ci++)
            {
                var mask = new float[n];
                for (var i = 0; i < n; i++)
                {
                    mask[i] = vegRaw[i] == allVeg[ci] ? 1f : 0f;
                }

                blurred[ci] = SeparableGaussian.Blur(mask, fetchRes, fetchRes, blurSigma);
            }

            vegFull = new byte[n];
            for (var i = 0; i < n; i++)
            {
                var bestClass = 0;
                var bestVal = blurred[0][i];
                for (var ci = 1; ci < allVeg.Length; ci++)
                {
                    if (blurred[ci][i] > bestVal)
                    {
                        bestVal = blurred[ci][i];
                        bestClass = ci;
                    }
                }

                vegFull[i] = (byte)allVeg[bestClass];
            }

            var waterBlur = SeparableGaussian.Blur(waterRaw, fetchRes, fetchRes, blurSigma);
            waterFull = new bool[n];
            for (var i = 0; i < n; i++)
            {
                waterFull[i] = waterBlur[i] >= 0.5f;
            }
        }
        else
        {
            vegFull = vegRaw;
            waterFull = new bool[n];
            for (var i = 0; i < n; i++)
            {
                waterFull[i] = waterRaw[i] >= 0.5f;
            }
        }

        var vegOut = new byte[outRes * outRes];
        var waterOut = new bool[outRes * outRes];
        for (var r = 0; r < outRes; r++)
        {
            for (var c = 0; c < outRes; c++)
            {
                var src = ((r + gutter) * fetchRes) + (c + gutter);
                vegOut[(r * outRes) + c] = vegFull[src];
                waterOut[(r * outRes) + c] = waterFull[src];
            }
        }

        return (vegOut, waterOut);
    }
}
