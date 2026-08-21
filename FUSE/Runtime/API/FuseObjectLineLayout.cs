using System;
using System.Collections.Generic;
using FUSE.Authoring.Data;
using UnityEngine;

namespace FUSE.Runtime.API
{
    internal static class FuseObjectLineLayout
    {
        internal sealed class Placement
        {
            internal Vector3 Position;
            internal Vector3 Forward;
        }

        internal static IReadOnlyList<Placement> Build(
            IReadOnlyList<FuseSplineyPoint> points,
            float spacing,
            bool placeAtEnd,
            int maximumInstances)
        {
            if (points == null || points.Count < 2)
                throw new InvalidOperationException("An object line requires at least two points.");
            if (spacing <= 0f)
                throw new InvalidOperationException("Object-line spacing must be greater than zero.");
            if (maximumInstances < 1 || maximumInstances > 4096)
                throw new InvalidOperationException("Object-line maximumInstances must be between 1 and 4096.");

            var segments = new List<Segment>();
            var totalLength = 0f;
            for (var index = 1; index < points.Count; index++)
            {
                if (points[index - 1] == null || points[index] == null)
                    continue;
                var start = points[index - 1].Position;
                var end = points[index].Position;
                var delta = end - start;
                var length = delta.magnitude;
                if (length <= 0.001f)
                    continue;
                segments.Add(new Segment(start, end, delta / length, totalLength, length));
                totalLength += length;
            }
            if (segments.Count == 0)
                throw new InvalidOperationException("Object-line points must describe a non-zero path.");

            var distances = new List<float>();
            for (var distance = 0f; distance <= totalLength + 0.0001f; distance += spacing)
            {
                distances.Add(Mathf.Min(distance, totalLength));
                if (distances.Count > maximumInstances)
                    throw TooManyInstances(maximumInstances, spacing, totalLength);
            }
            if (placeAtEnd && totalLength - distances[distances.Count - 1] > 0.001f)
            {
                distances.Add(totalLength);
                if (distances.Count > maximumInstances)
                    throw TooManyInstances(maximumInstances, spacing, totalLength);
            }

            var placements = new List<Placement>(distances.Count);
            var segmentIndex = 0;
            foreach (var distance in distances)
            {
                while (segmentIndex + 1 < segments.Count
                       && distance > segments[segmentIndex].EndDistance + 0.0001f)
                {
                    segmentIndex++;
                }
                var segment = segments[segmentIndex];
                var local = Mathf.Clamp(distance - segment.StartDistance, 0f, segment.Length);
                placements.Add(new Placement
                {
                    Position = segment.Start + segment.Forward * local,
                    Forward = segment.Forward,
                });
            }
            return placements;
        }

        private static InvalidOperationException TooManyInstances(
            int maximumInstances,
            float spacing,
            float length)
        {
            return new InvalidOperationException(
                $"Object line would exceed its {maximumInstances} instance safety limit "
                + $"(path {length:0.##} m, spacing {spacing:0.##} m). Increase spacing or maximumInstances.");
        }

        private sealed class Segment
        {
            internal Segment(
                Vector3 start,
                Vector3 end,
                Vector3 forward,
                float startDistance,
                float length)
            {
                Start = start;
                End = end;
                Forward = forward;
                StartDistance = startDistance;
                Length = length;
            }

            internal Vector3 Start { get; }
            internal Vector3 End { get; }
            internal Vector3 Forward { get; }
            internal float StartDistance { get; }
            internal float Length { get; }
            internal float EndDistance => StartDistance + Length;
        }
    }
}
