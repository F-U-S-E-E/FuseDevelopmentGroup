using System.Collections.Generic;
using System.Linq;
using Fuse.Core.Authoring;
using Fuse.ExternalEditor.Models.Terrain;

namespace Fuse.ExternalEditor.Logic;

/// <summary>
/// Accumulates a terrain brush stroke for undo. Records each touched pixel's
/// original R/G/A once (first touch, per <c>edit_tiles/app.py</c> <c>_record_pixels</c>),
/// then <see cref="Commit"/> snapshots the final values and returns one reversible
/// <see cref="UndoAction"/> spanning every tile the stroke edited — so terrain edits
/// share the editor's single undo stack with track/world edits.
/// </summary>
public sealed class TerrainStroke
{
    private readonly Dictionary<TerrainTile, Dictionary<int, (byte R, byte G, byte A)>> _before = new();

    /// <summary>Capture the pre-edit values of these pixels (ignored if already captured this stroke).</summary>
    public void RecordBefore(TerrainTile tile, IReadOnlyList<int> indices)
    {
        if (!_before.TryGetValue(tile, out var map))
        {
            map = new Dictionary<int, (byte, byte, byte)>();
            _before[tile] = map;
        }

        foreach (var i in indices)
        {
            if (!map.ContainsKey(i))
            {
                map[i] = (tile.R[i], tile.G[i], tile.A[i]);
            }
        }
    }

    /// <summary>Snapshot the post-edit values and produce a reversible action, or null if nothing changed.</summary>
    public UndoAction? Commit()
    {
        var edits = new List<TileEdit>();
        foreach (var (tile, map) in _before)
        {
            var indices = map.Keys.ToArray();
            var old = new (byte R, byte G, byte A)[indices.Length];
            var now = new (byte R, byte G, byte A)[indices.Length];
            var changed = false;
            for (var k = 0; k < indices.Length; k++)
            {
                var i = indices[k];
                old[k] = map[i];
                now[k] = (tile.R[i], tile.G[i], tile.A[i]);
                if (now[k] != old[k])
                {
                    changed = true;
                }
            }

            if (changed)
            {
                edits.Add(new TileEdit(tile, indices, old, now));
            }
        }

        if (edits.Count == 0)
        {
            return null;
        }

        return new UndoAction(
            "Terrain edit",
            () => { foreach (var e in edits) { e.Write(e.New); } },
            () => { foreach (var e in edits) { e.Write(e.Old); } });
    }

    private sealed class TileEdit
    {
        private readonly TerrainTile _tile;
        private readonly int[] _indices;

        public TileEdit(TerrainTile tile, int[] indices, (byte R, byte G, byte A)[] old, (byte R, byte G, byte A)[] now)
        {
            _tile = tile;
            _indices = indices;
            Old = old;
            New = now;
        }

        public (byte R, byte G, byte A)[] Old { get; }
        public (byte R, byte G, byte A)[] New { get; }

        public void Write((byte R, byte G, byte A)[] values)
        {
            for (var k = 0; k < _indices.Length; k++)
            {
                var i = _indices[k];
                _tile.R[i] = values[k].R;
                _tile.G[i] = values[k].G;
                _tile.A[i] = values[k].A;
            }

            _tile.Dirty = true;
            _tile.RecalcStats();
        }
    }
}
