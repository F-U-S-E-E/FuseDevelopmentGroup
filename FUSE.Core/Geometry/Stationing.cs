using System;
using System.Collections.Generic;
using Fuse.Core.Model;

namespace Fuse.Core.Geometry
{
    /// <summary>
    /// Track-graph stationing: builds node adjacency weighted by bezier segment
    /// length (<see cref="BezierMath.SegmentLength"/>), finds the shortest connected
    /// path (Dijkstra), and returns cumulative station distance along a path. Pure
    /// and Unity-free. Retargets the Python editor's connected-path measurement.
    /// </summary>
    public static class Stationing
    {
        public static Dictionary<string, List<(string Neighbor, string SegmentId, double Length)>> BuildAdjacency(FuseTrackDefinition tracks)
        {
            var adjacency = new Dictionary<string, List<(string, string, double)>>();

            void Add(string from, string to, string segmentId, double length)
            {
                if (!adjacency.TryGetValue(from, out var list))
                {
                    list = new List<(string, string, double)>();
                    adjacency[from] = list;
                }

                list.Add((to, segmentId, length));
            }

            foreach (var kv in tracks.Segments)
            {
                var seg = kv.Value;
                if (seg?.StartNodeId == null || seg.EndNodeId == null)
                {
                    continue;
                }

                if (!tracks.Nodes.TryGetValue(seg.StartNodeId, out var a) || a == null
                    || !tracks.Nodes.TryGetValue(seg.EndNodeId, out var b) || b == null)
                {
                    continue;
                }

                var length = BezierMath.SegmentLength(TrackNodeGeometry.FromNode(a), TrackNodeGeometry.FromNode(b));
                Add(seg.StartNodeId, seg.EndNodeId, kv.Key, length);
                Add(seg.EndNodeId, seg.StartNodeId, kv.Key, length);
            }

            return adjacency;
        }

        /// <summary>Shortest connected node path from start to end (by segment length), or null if unreachable.</summary>
        public static List<string> ShortestPath(FuseTrackDefinition tracks, string startId, string endId)
        {
            if (!tracks.Nodes.ContainsKey(startId) || !tracks.Nodes.ContainsKey(endId))
            {
                return null;
            }

            if (startId == endId)
            {
                return new List<string> { startId };
            }

            var adjacency = BuildAdjacency(tracks);
            var dist = new Dictionary<string, double> { [startId] = 0.0 };
            var prev = new Dictionary<string, string>();
            var visited = new HashSet<string>();

            while (true)
            {
                string u = null;
                var best = double.PositiveInfinity;
                foreach (var kv in dist)
                {
                    if (!visited.Contains(kv.Key) && kv.Value < best)
                    {
                        best = kv.Value;
                        u = kv.Key;
                    }
                }

                if (u == null || u == endId)
                {
                    break;
                }

                visited.Add(u);
                if (!adjacency.TryGetValue(u, out var neighbours))
                {
                    continue;
                }

                foreach (var (v, _, length) in neighbours)
                {
                    if (visited.Contains(v))
                    {
                        continue;
                    }

                    var nd = dist[u] + length;
                    if (!dist.TryGetValue(v, out var dv) || nd < dv)
                    {
                        dist[v] = nd;
                        prev[v] = u;
                    }
                }
            }

            if (!dist.ContainsKey(endId))
            {
                return null;
            }

            var path = new List<string> { endId };
            var cur = endId;
            while (cur != startId)
            {
                if (!prev.TryGetValue(cur, out var p))
                {
                    return null;
                }

                cur = p;
                path.Add(cur);
            }

            path.Reverse();
            return path;
        }

        /// <summary>Cumulative station distance at each node along an explicit path.</summary>
        public static double[] PathStations(FuseTrackDefinition tracks, IReadOnlyList<string> nodeIds)
        {
            if (nodeIds == null || nodeIds.Count == 0)
            {
                return Array.Empty<double>();
            }

            var adjacency = BuildAdjacency(tracks);
            var stations = new double[nodeIds.Count];
            stations[0] = 0.0;
            for (var i = 1; i < nodeIds.Count; i++)
            {
                stations[i] = stations[i - 1] + EdgeLength(adjacency, tracks, nodeIds[i - 1], nodeIds[i]);
            }

            return stations;
        }

        public static double PathLength(FuseTrackDefinition tracks, IReadOnlyList<string> nodeIds)
        {
            var stations = PathStations(tracks, nodeIds);
            return stations.Length == 0 ? 0.0 : stations[stations.Length - 1];
        }

        private static double EdgeLength(
            Dictionary<string, List<(string Neighbor, string SegmentId, double Length)>> adjacency,
            FuseTrackDefinition tracks, string a, string b)
        {
            if (adjacency.TryGetValue(a, out var neighbours))
            {
                foreach (var (v, _, length) in neighbours)
                {
                    if (v == b)
                    {
                        return length;
                    }
                }
            }

            // Not directly connected — fall back to straight XZ distance.
            if (tracks.Nodes.TryGetValue(a, out var na) && na != null && tracks.Nodes.TryGetValue(b, out var nb) && nb != null)
            {
                double dx = nb.Position.x - na.Position.x, dz = nb.Position.z - na.Position.z;
                return Math.Sqrt((dx * dx) + (dz * dz));
            }

            return 0.0;
        }
    }
}
