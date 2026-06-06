using System;
using System.Collections.Generic;
using Fuse.Core.Model;

namespace Fuse.Core.Geometry
{
    /// <summary>One station along a track profile: track elevation + grade, optional terrain + cut/fill.</summary>
    public sealed class ProfilePoint
    {
        public ProfilePoint(string nodeId, double station, double trackElevation, double gradePercent, double? terrainElevation, double? cutFill)
        {
            NodeId = nodeId;
            Station = station;
            TrackElevation = trackElevation;
            GradePercent = gradePercent;
            TerrainElevation = terrainElevation;
            CutFill = cutFill;
        }

        public string NodeId { get; }
        public double Station { get; }
        public double TrackElevation { get; }
        public double GradePercent { get; }
        public double? TerrainElevation { get; }

        /// <summary>Track elevation − terrain elevation: positive = fill (track above ground), negative = cut.</summary>
        public double? CutFill { get; }
    }

    /// <summary>
    /// Builds an elevation profile along a connected node path: station (via
    /// <see cref="Stationing"/>), track elevation (node Y), grade between stations,
    /// and—if a terrain sampler is supplied—terrain elevation + cut/fill. Pure;
    /// the terrain sampler is injected so FUSE.Core stays Unity/render-free.
    /// </summary>
    public static class TrackProfile
    {
        public static IReadOnlyList<ProfilePoint> Build(
            FuseTrackDefinition tracks, IReadOnlyList<string> nodeIds, Func<double, double, double?> terrainSampler = null)
        {
            if (tracks == null)
            {
                throw new ArgumentNullException(nameof(tracks));
            }

            var result = new List<ProfilePoint>();
            if (nodeIds == null || nodeIds.Count == 0)
            {
                return result;
            }

            var stations = Stationing.PathStations(tracks, nodeIds);
            double prevElev = 0.0, prevStation = 0.0;
            var first = true;
            for (var i = 0; i < nodeIds.Count; i++)
            {
                if (!tracks.Nodes.TryGetValue(nodeIds[i], out var node) || node == null)
                {
                    continue;
                }

                var elev = node.Position.y;
                var grade = first ? 0.0 : Measurement.GradePercent(elev - prevElev, stations[i] - prevStation);
                double? terrain = terrainSampler?.Invoke(node.Position.x, node.Position.z);
                double? cutFill = terrain.HasValue ? elev - terrain.Value : (double?)null;
                result.Add(new ProfilePoint(nodeIds[i], stations[i], elev, grade, terrain, cutFill));
                prevElev = elev;
                prevStation = stations[i];
                first = false;
            }

            return result;
        }
    }
}
