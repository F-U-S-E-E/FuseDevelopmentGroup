using System;
using System.Collections.Generic;
using Fuse.ExternalEditor.Models.Terrain;

namespace Fuse.ExternalEditor.Logic;

public enum TerrainEditMode
{
    Height,
    Veg,
    Water,
}

public enum TerrainBrushKind
{
    Raise,
    Flatten,
    Paint,
    Smooth,
    Noise,
    ErodeThermal,
    ErodeHydraulic,
}

/// <summary>Mutable brush parameters (Python defaults: strength 0.008, noiseScale 64).</summary>
public sealed class BrushSettings
{
    public TerrainEditMode Mode { get; set; } = TerrainEditMode.Height;
    public TerrainBrushKind Kind { get; set; } = TerrainBrushKind.Raise;
    public float Strength { get; set; } = 0.008f;
    public bool Erase { get; set; }
    public int HeightTarget { get; set; } = 32768;
    public int VegPreset { get; set; }
    public double NoiseScale { get; set; } = 64;
    public int ClampLo { get; set; }
    public int ClampHi { get; set; } = 65535;
}

/// <summary>
/// Terrain brush engine ported from <c>edit_tiles/app.py</c> <c>_paint_at</c> +
/// <c>_apply_erosion</c>. <see cref="Apply"/> stamps one dab onto a tile at a
/// tile-pixel centre and returns the flat indices it touched (falloff &gt; 0.01),
/// for undo snapshotting. Pure and Unity-free → unit-tested headlessly.
/// </summary>
public static class TerrainBrush
{
    /// <summary>Quintic falloff: 1 at centre → 0 at edge (no hard ring). Matches <c>brush_falloff</c>.</summary>
    public static float Falloff(double dist, int radius)
    {
        var t = Math.Clamp(dist / Math.Max(radius, 1), 0.0, 1.0);
        return (float)(Math.Pow(1.0 - t, 3) * (1.0 + (3.0 * t) + (6.0 * t * t)));
    }

    public static IReadOnlyList<int> Apply(TerrainTile tile, double centreRow, double centreCol, int radius, BrushSettings s, INoiseSource? noise = null, TerrainStroke? stroke = null)
    {
        var res = tile.Res;
        var r0 = Math.Max(0, (int)Math.Floor(centreRow - radius));
        var r1 = Math.Min(res - 1, (int)Math.Ceiling(centreRow + radius));
        var c0 = Math.Max(0, (int)Math.Floor(centreCol - radius));
        var c1 = Math.Min(res - 1, (int)Math.Ceiling(centreCol + radius));

        var changed = new List<int>();
        if (r0 > r1 || c0 > c1)
        {
            return changed;
        }

        var rows = new List<int>();
        var cols = new List<int>();
        var falloff = new List<float>();
        for (var rr = r0; rr <= r1; rr++)
        {
            for (var cc = c0; cc <= c1; cc++)
            {
                var dist = Math.Sqrt(Math.Pow(rr - centreRow, 2) + Math.Pow(cc - centreCol, 2));
                var f = Falloff(dist, radius);
                if (f > 0.01f)
                {
                    rows.Add(rr);
                    cols.Add(cc);
                    falloff.Add(f);
                    changed.Add((rr * res) + cc);
                }
            }
        }

        if (changed.Count == 0)
        {
            return changed;
        }

        // Snapshot originals before any write (matches _record_pixels first-touch).
        stroke?.RecordBefore(tile, changed);

        switch (s.Mode)
        {
            case TerrainEditMode.Height:
                ApplyHeight(tile, res, rows, cols, falloff, changed, s, noise);
                break;
            case TerrainEditMode.Veg:
                var preset = s.Erase ? 0 : s.VegPreset;
                foreach (var idx in changed)
                {
                    tile.A[idx] = (byte)((tile.A[idx] & 0x8F) | ((preset & 0x7) << 4));
                }

                break;
            case TerrainEditMode.Water:
                foreach (var idx in changed)
                {
                    tile.A[idx] = (byte)(s.Erase ? tile.A[idx] & 0x7F : tile.A[idx] | 0x80);
                }

                break;
        }

        tile.Dirty = true;
        tile.RecalcStats();
        return changed;
    }

    private static void ApplyHeight(TerrainTile tile, int res, List<int> rows, List<int> cols, List<float> falloff, List<int> idxs, BrushSettings s, INoiseSource? noise)
    {
        // Erosion builds a full-grid working copy so neighbour transfers accumulate.
        float[]? eroded = null;
        if (s.Kind == TerrainBrushKind.ErodeThermal || s.Kind == TerrainBrushKind.ErodeHydraulic)
        {
            eroded = Erode(tile, res, rows, cols, s);
        }

        // Pass 1: compute new heights reading only the pre-dab tile (so smooth/erode
        // neighbour reads see original values, matching the reference's batch read).
        var newVals = new double[idxs.Count];
        for (var k = 0; k < idxs.Count; k++)
        {
            var idx = idxs[k];
            var f = falloff[k];
            var h16 = (double)tile.Height16(idx);

            switch (s.Kind)
            {
                case TerrainBrushKind.Flatten:
                    var fb = Math.Clamp(f * s.Strength * 10.0, 0.0, 1.0);
                    h16 = (h16 * (1.0 - fb)) + (s.HeightTarget * fb);
                    break;
                case TerrainBrushKind.Paint:
                    var pb = Math.Clamp((double)f, 0.0, 1.0);
                    h16 = (h16 * (1.0 - pb)) + (s.HeightTarget * pb);
                    break;
                case TerrainBrushKind.Noise:
                    var nv = noise?.Sample(rows[k], cols[k]) ?? 0f;
                    var sign = s.Erase ? -1 : 1;
                    h16 += sign * nv * f * s.Strength * 65535;
                    break;
                case TerrainBrushKind.Smooth:
                    var avg = NeighbourAverage(tile, res, rows[k], cols[k]);
                    var sb = Math.Clamp(f * s.Strength * 8.0, 0.0, 1.0);
                    h16 = (h16 * (1.0 - sb)) + (avg * sb);
                    break;
                case TerrainBrushKind.ErodeThermal:
                case TerrainBrushKind.ErodeHydraulic:
                    h16 = (h16 * (1.0 - f)) + (eroded![idx] * f);
                    break;
                default: // Raise / Lower (Erase)
                    var delta = s.Strength * (s.Erase ? -1 : 1);
                    h16 += delta * f * 65535;
                    break;
            }

            newVals[k] = h16;
        }

        // Pass 2: clamp + write.
        for (var k = 0; k < idxs.Count; k++)
        {
            var clamped = Math.Clamp((int)newVals[k], s.ClampLo, s.ClampHi);
            tile.R[idxs[k]] = (byte)((clamped >> 8) & 0xFF);
            tile.G[idxs[k]] = (byte)(clamped & 0xFF);
        }
    }

    private static double NeighbourAverage(TerrainTile tile, int res, int row, int col)
    {
        double sum = 0;
        var count = 0;
        for (var dr = -1; dr <= 1; dr++)
        {
            for (var dc = -1; dc <= 1; dc++)
            {
                var nr = Math.Clamp(row + dr, 0, res - 1);
                var nc = Math.Clamp(col + dc, 0, res - 1);
                sum += tile.Height16((nr * res) + nc);
                count++;
            }
        }

        return sum / count;
    }

    // Thermal (talus) / hydraulic erosion over the kept pixels; returns a full-grid
    // working copy of h16 with transfers applied (only kept pixels are later blended in).
    private static float[] Erode(TerrainTile tile, int res, List<int> rows, List<int> cols, BrushSettings s)
    {
        var grid = new float[res * res];
        for (var i = 0; i < grid.Length; i++)
        {
            grid[i] = tile.Height16(i);
        }

        var working = (float[])grid.Clone();

        if (s.Kind == TerrainBrushKind.ErodeThermal)
        {
            var talus = (int)(s.Strength * 65535 * 2);
            for (var k = 0; k < rows.Count; k++)
            {
                int ri = rows[k], ci = cols[k];
                var hc = grid[(ri * res) + ci];
                ReadOnlySpan<(int dr, int dc)> n4 = stackalloc (int, int)[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
                foreach (var (dr, dc) in n4)
                {
                    int nr = ri + dr, nc = ci + dc;
                    if (nr < 0 || nr >= res || nc < 0 || nc >= res)
                    {
                        continue;
                    }

                    var diff = hc - grid[(nr * res) + nc];
                    if (diff > talus)
                    {
                        var transfer = (int)(diff * 0.25);
                        working[(ri * res) + ci] -= transfer;
                        working[(nr * res) + nc] += transfer;
                    }
                }
            }
        }
        else
        {
            for (var k = 0; k < rows.Count; k++)
            {
                int ri = rows[k], ci = cols[k];
                var hc = grid[(ri * res) + ci];
                double sum = 0;
                var count = 0;
                ReadOnlySpan<(int dr, int dc)> n8 = stackalloc (int, int)[] { (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1) };
                foreach (var (dr, dc) in n8)
                {
                    int nr = ri + dr, nc = ci + dc;
                    if (nr < 0 || nr >= res || nc < 0 || nc >= res)
                    {
                        continue;
                    }

                    sum += grid[(nr * res) + nc];
                    count++;
                }

                if (count == 0)
                {
                    continue;
                }

                var avg = sum / count;
                if (hc > avg)
                {
                    var drop = (hc - avg) * s.Strength * 0.3;
                    working[(ri * res) + ci] = (float)(hc - drop);
                }
            }
        }

        return working;
    }
}
