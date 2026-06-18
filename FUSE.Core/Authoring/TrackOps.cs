using System.Collections.Generic;
using System.Linq;
using Fuse.Core.Model;

namespace Fuse.Core.Authoring
{
    /// <summary>
    /// Track-graph CRUD on a <see cref="FuseTrackDefinition"/>, retargeting the
    /// Python editor's node/segment helpers (<c>mod_project/helpers.py</c>) onto
    /// the typed FUSE model. Pure and Unity-free so both editors and tests can use it.
    /// </summary>
    public static class TrackOps
    {
        public static FuseNode AddNode(FuseTrackDefinition tracks, string id, FuseVector3 position, FuseVector3 rotation, bool flipSwitchStand = false)
        {
            var node = new FuseNode
            {
                Position = position,
                Rotation = rotation,
                FlipSwitchStand = flipSwitchStand,
            };
            tracks.Nodes[id] = node;
            return node;
        }

        public static bool MoveNode(FuseTrackDefinition tracks, string id, FuseVector3 position)
        {
            if (tracks.Nodes.TryGetValue(id, out var node) && node != null)
            {
                node.Position = position;
                return true;
            }

            return false;
        }

        public static bool SetNodeRotation(FuseTrackDefinition tracks, string id, FuseVector3 rotation)
        {
            if (tracks.Nodes.TryGetValue(id, out var node) && node != null)
            {
                node.Rotation = rotation;
                return true;
            }

            return false;
        }

        /// <summary>Translate every listed node by (dx, dy, dz). Returns how many moved.</summary>
        public static int MoveGroup(FuseTrackDefinition tracks, IEnumerable<string> ids, float dx, float dy, float dz)
        {
            var moved = 0;
            foreach (var id in ids)
            {
                if (tracks.Nodes.TryGetValue(id, out var node) && node != null)
                {
                    var p = node.Position;
                    node.Position = new FuseVector3(p.x + dx, p.y + dy, p.z + dz);
                    moved++;
                }
            }

            return moved;
        }

        /// <summary>
        /// Remove a node and cascade-delete any segments touching it. Returns the
        /// number of segments removed, or -1 if the node didn't exist.
        /// </summary>
        public static int DeleteNode(FuseTrackDefinition tracks, string id)
        {
            if (!tracks.Nodes.Remove(id))
            {
                return -1;
            }

            var connected = tracks.Segments
                .Where(kv => kv.Value != null && (kv.Value.StartNodeId == id || kv.Value.EndNodeId == id))
                .Select(kv => kv.Key)
                .ToList();
            foreach (var segId in connected)
            {
                tracks.Segments.Remove(segId);
            }

            return connected.Count;
        }

        public static FuseSegment ConnectSegment(
            FuseTrackDefinition tracks, string id, string startNodeId, string endNodeId,
            string trackClass = "main", string style = "standard", int speedLimit = 45)
        {
            var segment = new FuseSegment
            {
                StartNodeId = startNodeId,
                EndNodeId = endNodeId,
                TrackClass = trackClass,
                Style = style,
                SpeedLimit = speedLimit,
            };
            tracks.Segments[id] = segment;
            return segment;
        }

        public static bool DeleteSegment(FuseTrackDefinition tracks, string id) => tracks.Segments.Remove(id);

        public static bool SetSegmentProps(
            FuseTrackDefinition tracks, string id,
            string trackClass = null, string style = null,
            int? speedLimit = null, int? priority = null, string groupId = null)
        {
            if (!tracks.Segments.TryGetValue(id, out var seg) || seg == null)
            {
                return false;
            }

            if (trackClass != null) seg.TrackClass = trackClass;
            if (style != null) seg.Style = style;
            if (speedLimit.HasValue) seg.SpeedLimit = speedLimit.Value;
            if (priority.HasValue) seg.Priority = priority.Value;
            if (groupId != null) seg.GroupId = groupId;
            return true;
        }

        /// <summary>Number of segments connected to a node (1 = dead end, 2 = through, 3+ = switch).</summary>
        public static int NodeValency(FuseTrackDefinition tracks, string nodeId) =>
            tracks.Segments.Values.Count(s => s != null && (s.StartNodeId == nodeId || s.EndNodeId == nodeId));

        public static string NewNodeId(FuseTrackDefinition tracks) => AuthoringIds.UniqueId(tracks.Nodes.Keys, "n");

        public static string NewSegmentId(FuseTrackDefinition tracks) => AuthoringIds.UniqueId(tracks.Segments.Keys, "s");

        /// <summary>
        /// Batch variant of <see cref="NewNodeId(FuseTrackDefinition)"/> for callers minting many
        /// ids in one operation: build <paramref name="takenIds"/> from <c>tracks.Nodes.Keys</c>
        /// once, start <paramref name="nextIndex"/> at 1, and reuse both across calls. Each
        /// returned id is added to <paramref name="takenIds"/>, so as long as no ids are removed
        /// mid-batch the sequence matches repeated single-shot calls (first free slot, gaps
        /// filled) without rescanning every key per id.
        /// </summary>
        public static string NewNodeId(ISet<string> takenIds, ref int nextIndex) => AuthoringIds.UniqueId(takenIds, "n", ref nextIndex);

        /// <summary>Batch variant of <see cref="NewSegmentId(FuseTrackDefinition)"/>; see <see cref="NewNodeId(ISet{string}, ref int)"/>.</summary>
        public static string NewSegmentId(ISet<string> takenIds, ref int nextIndex) => AuthoringIds.UniqueId(takenIds, "s", ref nextIndex);
    }
}
