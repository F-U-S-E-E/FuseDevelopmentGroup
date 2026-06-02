using System;

namespace Fuse.ExternalEditor.Logic;

/// <summary>A per-pixel noise field sampled by the noise brush.</summary>
public interface INoiseSource
{
    float Sample(int row, int col);
}

/// <summary>
/// Fractional Brownian motion (4-octave value/gradient noise), ported from
/// <c>edit_tiles/terrain.py</c> <c>noise_brush</c> + <c>_perlin_grid</c>. The
/// Python reference uses an <em>unseeded</em> RNG (so its output is not
/// reproducible); this port threads one seeded <see cref="Random"/> through all
/// octaves, so a given seed always yields the same field — better for the editor
/// (repeatable strokes) and testable. The octave structure, smoothstep
/// interpolation and 1/1.875 normalisation match the reference exactly.
/// </summary>
public sealed class FbmNoise : INoiseSource
{
    private readonly float[] _field;
    private readonly int _res;

    private FbmNoise(float[] field, int res)
    {
        _field = field;
        _res = res;
    }

    public float Sample(int row, int col) => _field[(row * _res) + col];

    public static FbmNoise Build(int res, double noiseScale, int seed)
    {
        var rng = new Random(seed);
        var field = new float[res * res];
        double amp = 1.0, freq = 1.0;
        for (var octave = 0; octave < 4; octave++)
        {
            var scale = Math.Max(4, (int)(noiseScale / freq));
            var layer = PerlinGrid(res, res, scale, rng);
            for (var i = 0; i < field.Length; i++)
            {
                field[i] += (float)(amp * layer[i]);
            }

            amp *= 0.5;
            freq *= 2.0;
        }

        const float norm = 1.0f + 0.5f + 0.25f + 0.125f; // 1.875
        for (var i = 0; i < field.Length; i++)
        {
            field[i] /= norm;
        }

        return new FbmNoise(field, res);
    }

    // Gradient noise: random unit gradients on a coarse grid, bilinearly
    // interpolated with a smoothstep weight (matches _perlin_grid).
    private static float[] PerlinGrid(int h, int w, int scale, Random rng)
    {
        var gh = Math.Max(2, (int)Math.Ceiling((double)h / scale) + 2);
        var gw = Math.Max(2, (int)Math.Ceiling((double)w / scale) + 2);
        var gx = new float[gh * gw];
        var gy = new float[gh * gw];
        for (var i = 0; i < gx.Length; i++)
        {
            var angle = rng.NextDouble() * 2.0 * Math.PI;
            gx[i] = (float)Math.Cos(angle);
            gy[i] = (float)Math.Sin(angle);
        }

        var r0c = new int[h];
        var r1c = new int[h];
        var rf = new float[h];
        var u = new float[h];
        for (var r = 0; r < h; r++)
        {
            var rows = (double)r / scale;
            var floor = (int)Math.Floor(rows);
            var f = (float)(rows - floor);
            rf[r] = f;
            u[r] = f * f * (3 - (2 * f));
            r0c[r] = Math.Clamp(floor, 0, gh - 1);
            r1c[r] = Math.Clamp(floor + 1, 0, gh - 1);
        }

        var c0c = new int[w];
        var c1c = new int[w];
        var cf = new float[w];
        var v = new float[w];
        for (var c = 0; c < w; c++)
        {
            var cols = (double)c / scale;
            var floor = (int)Math.Floor(cols);
            var f = (float)(cols - floor);
            cf[c] = f;
            v[c] = f * f * (3 - (2 * f));
            c0c[c] = Math.Clamp(floor, 0, gw - 1);
            c1c[c] = Math.Clamp(floor + 1, 0, gw - 1);
        }

        var output = new float[h * w];
        for (var r = 0; r < h; r++)
        {
            int rr0 = r0c[r], rr1 = r1c[r];
            float rfr = rf[r], ur = u[r];
            for (var c = 0; c < w; c++)
            {
                int cc0 = c0c[c], cc1 = c1c[c];
                float cfc = cf[c], vc = v[c];

                var n00 = (gx[(rr0 * gw) + cc0] * cfc) + (gy[(rr0 * gw) + cc0] * rfr);
                var n10 = (gx[(rr1 * gw) + cc0] * cfc) + (gy[(rr1 * gw) + cc0] * (rfr - 1));
                var n01 = (gx[(rr0 * gw) + cc1] * (cfc - 1)) + (gy[(rr0 * gw) + cc1] * rfr);
                var n11 = (gx[(rr1 * gw) + cc1] * (cfc - 1)) + (gy[(rr1 * gw) + cc1] * (rfr - 1));

                var a = n00 + (ur * (n10 - n00));
                var b = n01 + (ur * (n11 - n01));
                output[(r * w) + c] = a + (vc * (b - a));
            }
        }

        return output;
    }
}
