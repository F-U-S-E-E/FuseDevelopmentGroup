using System;

namespace Fuse.ExternalEditor.Models.Terrain;

/// <summary>
/// One terrain tile, ported from <c>edit_tiles/terrain.py</c> <c>Tile</c>.
/// Channels are flat, row-major, length <c>Res*Res</c>: R/G encode 16-bit
/// height (<c>R*256+G</c>); A encodes water in bit 7 and vegetation preset in
/// bits 4-6. Pure data + derived stats; rendering lives in <c>TerrainRenderer</c>.
/// </summary>
public sealed class TerrainTile
{
    public int X { get; }
    public int Y { get; }
    public int Res { get; }
    public byte[] R { get; }
    public byte[] G { get; }
    public byte[] A { get; }
    public string? Path { get; }

    public float MinM { get; private set; }
    public float MaxM { get; private set; }
    public float AvgM { get; private set; }

    /// <summary>Per-preset (0..7) pixel counts.</summary>
    public int[] Presets { get; } = new int[8];

    public int DomPreset { get; private set; }
    public float WaterPct { get; private set; }

    /// <summary>Set when the tile's pixels have been edited and need saving.</summary>
    public bool Dirty { get; set; }

    public TerrainTile(int x, int y, int res, byte[] r, byte[] g, byte[] a, string? path = null)
    {
        if (r is null || g is null || a is null)
        {
            throw new ArgumentNullException(nameof(r), "Tile channels are required.");
        }

        var n = res * res;
        if (r.Length < n || g.Length < n || a.Length < n)
        {
            throw new ArgumentException($"Tile channels must each have at least {n} entries for res {res}.");
        }

        X = x;
        Y = y;
        Res = res;
        R = r;
        G = g;
        A = a;
        Path = path;
        RecalcStats();
    }

    /// <summary>16-bit height sample (0..65535) at flat index.</summary>
    public int Height16(int index) => (R[index] << 8) | G[index];

    /// <summary>Recompute min/max/avg/preset/water stats after the channels are edited.</summary>
    public void RecalcStats()
    {
        var n = Res * Res;
        long sum = 0;
        var min = int.MaxValue;
        var max = int.MinValue;
        Array.Clear(Presets, 0, Presets.Length);
        var water = 0;

        for (var i = 0; i < n; i++)
        {
            var h = (R[i] << 8) | G[i];
            if (h < min) min = h;
            if (h > max) max = h;
            sum += h;

            Presets[(A[i] >> 4) & 0x7]++;
            water += (A[i] >> 7) & 1;
        }

        MinM = TerrainConstants.ToMeters(min);
        MaxM = TerrainConstants.ToMeters(max);
        AvgM = TerrainConstants.ToMeters((float)sum / n);

        var dom = 0;
        for (var i = 1; i < 8; i++)
        {
            if (Presets[i] > Presets[dom])
            {
                dom = i;
            }
        }

        DomPreset = dom;
        WaterPct = (float)water / n * 100f;
    }
}
