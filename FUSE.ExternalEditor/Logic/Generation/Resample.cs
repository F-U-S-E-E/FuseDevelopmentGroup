using System;

namespace Fuse.ExternalEditor.Logic.Generation;

/// <summary>Hand-rolled bilinear resample (replaces scipy <c>map_coordinates</c> order=1, mode='nearest').</summary>
public static class Resample
{
    public static double Bilinear(float[] src, int h, int w, double y, double x)
    {
        var i0 = (int)Math.Floor(y);
        var j0 = (int)Math.Floor(x);
        var dy = y - i0;
        var dx = x - j0;
        i0 = Math.Clamp(i0, 0, h - 1);
        j0 = Math.Clamp(j0, 0, w - 1);
        var i1 = Math.Min(i0 + 1, h - 1);
        var j1 = Math.Min(j0 + 1, w - 1);

        double v00 = src[(i0 * w) + j0];
        double v10 = src[(i1 * w) + j0];
        double v01 = src[(i0 * w) + j1];
        double v11 = src[(i1 * w) + j1];
        return (v00 * (1 - dy) * (1 - dx)) + (v10 * dy * (1 - dx)) + (v01 * (1 - dy) * dx) + (v11 * dy * dx);
    }
}

/// <summary>Separable Gaussian blur (replaces scipy <c>gaussian_filter</c>: radius = int(4σ+0.5), 'reflect' edges).</summary>
public static class SeparableGaussian
{
    public static float[] Blur(float[] src, int h, int w, double sigma)
    {
        if (sigma <= 0)
        {
            return (float[])src.Clone();
        }

        var radius = (int)((4.0 * sigma) + 0.5);
        var kernel = new double[(2 * radius) + 1];
        var sum = 0.0;
        for (var i = -radius; i <= radius; i++)
        {
            var v = Math.Exp(-0.5 * (i / sigma) * (i / sigma));
            kernel[i + radius] = v;
            sum += v;
        }

        for (var i = 0; i < kernel.Length; i++)
        {
            kernel[i] /= sum;
        }

        var tmp = new float[h * w];
        for (var r = 0; r < h; r++)
        {
            for (var c = 0; c < w; c++)
            {
                var acc = 0.0;
                for (var k = -radius; k <= radius; k++)
                {
                    acc += src[(r * w) + Reflect(c + k, w)] * kernel[k + radius];
                }

                tmp[(r * w) + c] = (float)acc;
            }
        }

        var output = new float[h * w];
        for (var r = 0; r < h; r++)
        {
            for (var c = 0; c < w; c++)
            {
                var acc = 0.0;
                for (var k = -radius; k <= radius; k++)
                {
                    acc += tmp[(Reflect(r + k, h) * w) + c] * kernel[k + radius];
                }

                output[(r * w) + c] = (float)acc;
            }
        }

        return output;
    }

    // scipy 'reflect': (d c b a | a b c d | d c b a) — mirror about the edge, edge not repeated.
    private static int Reflect(int p, int n)
    {
        if (n == 1)
        {
            return 0;
        }

        while (p < 0 || p >= n)
        {
            p = p < 0 ? -p - 1 : (2 * n) - p - 1;
        }

        return p;
    }
}
