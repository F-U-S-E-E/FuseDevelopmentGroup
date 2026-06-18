using System;
using System.Collections.Generic;
using Fuse.Core.Authoring;
using Fuse.Core.Model;

namespace Fuse.Core.Geometry
{
    /// <summary>A generated node (world position + euler rotation), id assigned at commit time.</summary>
    public sealed class GeneratedNode
    {
        public double X;
        public double Y;
        public double Z;
        public double RotX;
        public double RotY;
        public double RotZ;
        public bool FlipSwitchStand;
    }

    /// <summary>A generated segment referencing node indices within the same <see cref="GeneratedTrack"/>.</summary>
    public sealed class GeneratedSegment
    {
        public int StartIndex;
        public int EndIndex;
        public string TrackClass;
        public string Style;
        public int SpeedLimit;
        public int Priority;
    }

    /// <summary>Index-based generator output; <see cref="TrackGenerators.Commit"/> assigns real ids.</summary>
    public sealed class GeneratedTrack
    {
        public List<GeneratedNode> Nodes { get; } = new List<GeneratedNode>();
        public List<GeneratedSegment> Segments { get; } = new List<GeneratedSegment>();
    }

    /// <summary>Which side(s) to place parallel tracks on, relative to source heading.</summary>
    public enum ParallelSide
    {
        Right,
        Left,
        Both,
    }

    /// <summary>
    /// Track chain generators ported from <c>mod_project/geometry.py</c>
    /// (generate_straight / generate_curve / generate_turnout / generate_wye /
    /// generate_parallel_tracks). Geometry is deterministic and id-free here —
    /// <see cref="Commit"/> bridges a result into a <see cref="FuseTrackDefinition"/>
    /// with fresh ids. Verified against the Python reference by GeneratorsGoldenTests.
    /// </summary>
    public static class TrackGenerators
    {
        private const double DegToRad = Math.PI / 180.0;
        private const double RadToDeg = 180.0 / Math.PI;

        // Python's % always returns a non-negative result for a positive modulus.
        private static double Mod360(double v)
        {
            var m = v % 360.0;
            return m < 0 ? m + 360.0 : m;
        }

        public static GeneratedTrack Straight(
            double startX, double startY, double startZ, double startRotY,
            double length, double heightChange = 0, int nSegments = 1,
            string trackClass = "main", string style = "standard", int speedLimit = 0)
        {
            nSegments = Math.Max(1, nSegments);
            var step = length / nSegments;
            var dHeight = heightChange / nSegments;
            var r = startRotY * DegToRad;
            double fx = Math.Sin(r), fz = Math.Cos(r);

            var track = new GeneratedTrack();
            double cx = startX, cy = startY, cz = startZ;
            var prev = -1;
            for (var i = 0; i <= nSegments; i++)
            {
                track.Nodes.Add(new GeneratedNode { X = cx, Y = cy, Z = cz, RotX = 0, RotY = startRotY, RotZ = 0 });
                var idx = track.Nodes.Count - 1;
                if (prev >= 0)
                {
                    track.Segments.Add(new GeneratedSegment { StartIndex = prev, EndIndex = idx, TrackClass = trackClass, Style = style, SpeedLimit = speedLimit });
                }

                prev = idx;
                if (i < nSegments)
                {
                    cx += fx * step;
                    cz += fz * step;
                    cy += dHeight;
                }
            }

            return track;
        }

        public static GeneratedTrack Curve(
            double startX, double startY, double startZ, double startRotY,
            double radius, double degrees, double heightChange = 0, bool right = false, int nSegments = 0,
            string trackClass = "main", string style = "standard", int speedLimit = 0, double startRotX = 0)
        {
            if (nSegments <= 0)
            {
                nSegments = Math.Max(2, (int)(Math.Abs(degrees) / 5));
            }

            var arcLen = Math.Abs(degrees) * DegToRad * Math.Abs(radius);
            double sign = right ? -1 : 1;
            var dAngle = degrees / nSegments;

            if (heightChange == 0.0 && Math.Abs(startRotX) > 0.001 && arcLen > 0.0)
            {
                heightChange = arcLen * Math.Tan(-startRotX * DegToRad);
            }

            var dHeight = nSegments > 0 ? heightChange / nSegments : 0.0;
            var stepDist = nSegments > 0 ? arcLen / nSegments : arcLen;
            var nodeRotX = stepDist > 0.001 ? -Math.Atan2(dHeight, stepDist) * RadToDeg : startRotX;

            var track = new GeneratedTrack();
            double cx = startX, cy = startY, cz = startZ, curRotY = startRotY;
            var prev = -1;
            for (var i = 0; i <= nSegments; i++)
            {
                track.Nodes.Add(new GeneratedNode { X = cx, Y = cy, Z = cz, RotX = nodeRotX, RotY = curRotY, RotZ = 0 });
                var idx = track.Nodes.Count - 1;
                if (prev >= 0)
                {
                    track.Segments.Add(new GeneratedSegment { StartIndex = prev, EndIndex = idx, TrackClass = trackClass, Style = style, SpeedLimit = speedLimit });
                }

                prev = idx;
                if (i < nSegments)
                {
                    curRotY = Mod360(curRotY + (sign * dAngle));
                    double fx = Math.Sin(curRotY * DegToRad), fz = Math.Cos(curRotY * DegToRad);
                    cx += fx * stepDist;
                    cz += fz * stepDist;
                    cy += dHeight;
                }
            }

            return track;
        }

        /// <summary>3-node switch. Node order: 0 = switch, 1 = entry, 2 = through, 3 = diverge.</summary>
        public static GeneratedTrack Turnout(
            double swX, double swY, double swZ, double approachRotY,
            double divergeAngle = 10, double legLength = 30, bool right = false, bool flipSwitchStand = false,
            string trackClass = "main", string divergeClass = "branch", string style = "standard",
            int speedLimit = 0, int divergeSpeed = 0, double throughCurveAngle = 0)
        {
            var sign = right ? 1.0 : -1.0;
            var divRotY = Mod360(approachRotY + (sign * divergeAngle));
            var thruRotY = Mod360(approachRotY + (sign * throughCurveAngle));
            var entryRotY = Mod360(approachRotY + 180);

            var (ex, ez) = Fwd(swX, swZ, entryRotY, legLength);
            var (tx, tz) = Fwd(swX, swZ, thruRotY, legLength);
            var (dx, dz) = Fwd(swX, swZ, divRotY, legLength);

            var track = new GeneratedTrack();
            track.Nodes.Add(new GeneratedNode { X = swX, Y = swY, Z = swZ, RotY = approachRotY, FlipSwitchStand = flipSwitchStand });
            track.Nodes.Add(new GeneratedNode { X = ex, Y = swY, Z = ez, RotY = entryRotY });
            track.Nodes.Add(new GeneratedNode { X = tx, Y = swY, Z = tz, RotY = thruRotY });
            track.Nodes.Add(new GeneratedNode { X = dx, Y = swY, Z = dz, RotY = divRotY });
            track.Segments.Add(new GeneratedSegment { StartIndex = 1, EndIndex = 0, TrackClass = trackClass, Style = style, SpeedLimit = speedLimit });
            track.Segments.Add(new GeneratedSegment { StartIndex = 0, EndIndex = 2, TrackClass = trackClass, Style = style, SpeedLimit = speedLimit });
            track.Segments.Add(new GeneratedSegment { StartIndex = 0, EndIndex = 3, TrackClass = divergeClass, Style = style, SpeedLimit = divergeSpeed });
            return track;
        }

        /// <summary>Wye switch. Node order: 0 = switch, 1 = entry, 2 = left, 3 = right.</summary>
        public static GeneratedTrack Wye(
            double swX, double swY, double swZ, double approachRotY,
            double leftAngle = 10, double rightAngle = 10, double legLength = 30, bool flipSwitchStand = false,
            string trackClass = "main", string style = "standard", int speedLimit = 0)
        {
            var leftRotY = Mod360(approachRotY - leftAngle);
            var rightRotY = Mod360(approachRotY + rightAngle);
            var entryRotY = Mod360(approachRotY + 180);
            var bisectRotY = Mod360(approachRotY + ((-leftAngle + rightAngle) / 2.0));

            var (ex, ez) = Fwd(swX, swZ, entryRotY, legLength);
            var (lx, lz) = Fwd(swX, swZ, leftRotY, legLength);
            var (rx, rz) = Fwd(swX, swZ, rightRotY, legLength);

            var track = new GeneratedTrack();
            track.Nodes.Add(new GeneratedNode { X = swX, Y = swY, Z = swZ, RotY = bisectRotY, FlipSwitchStand = flipSwitchStand });
            track.Nodes.Add(new GeneratedNode { X = ex, Y = swY, Z = ez, RotY = entryRotY });
            track.Nodes.Add(new GeneratedNode { X = lx, Y = swY, Z = lz, RotY = leftRotY });
            track.Nodes.Add(new GeneratedNode { X = rx, Y = swY, Z = rz, RotY = rightRotY });
            track.Segments.Add(new GeneratedSegment { StartIndex = 1, EndIndex = 0, TrackClass = trackClass, Style = style, SpeedLimit = speedLimit });
            track.Segments.Add(new GeneratedSegment { StartIndex = 0, EndIndex = 2, TrackClass = trackClass, Style = style, SpeedLimit = speedLimit });
            track.Segments.Add(new GeneratedSegment { StartIndex = 0, EndIndex = 3, TrackClass = trackClass, Style = style, SpeedLimit = speedLimit });
            return track;
        }

        /// <summary>
        /// Offset a source chain to one or more parallel tracks (one <see cref="GeneratedTrack"/>
        /// per offset; node order and connectivity preserved). Each node is shifted along the
        /// right-perpendicular of its own heading, so curves stay parallel.
        /// </summary>
        public static List<GeneratedTrack> Parallel(
            GeneratedTrack source, double separation, int nTracks = 1, ParallelSide side = ParallelSide.Right,
            string trackClass = null, string style = null, int speedLimit = 0)
        {
            var offsets = new List<double>();
            switch (side)
            {
                case ParallelSide.Both:
                    for (var i = -nTracks; i <= nTracks; i++)
                    {
                        if (i != 0)
                        {
                            offsets.Add(i * separation);
                        }
                    }

                    break;
                case ParallelSide.Left:
                    for (var i = 1; i <= nTracks; i++)
                    {
                        offsets.Add(-i * separation);
                    }

                    break;
                default: // Right
                    for (var i = 1; i <= nTracks; i++)
                    {
                        offsets.Add(i * separation);
                    }

                    break;
            }

            var results = new List<GeneratedTrack>(offsets.Count);
            foreach (var offset in offsets)
            {
                var track = new GeneratedTrack();
                foreach (var n in source.Nodes)
                {
                    var r = n.RotY * DegToRad;
                    double px = Math.Cos(r), pz = -Math.Sin(r);
                    track.Nodes.Add(new GeneratedNode
                    {
                        X = n.X + (px * offset),
                        Y = n.Y,
                        Z = n.Z + (pz * offset),
                        RotX = n.RotX,
                        RotY = n.RotY,
                        RotZ = n.RotZ,
                        FlipSwitchStand = n.FlipSwitchStand,
                    });
                }

                foreach (var s in source.Segments)
                {
                    track.Segments.Add(new GeneratedSegment
                    {
                        StartIndex = s.StartIndex,
                        EndIndex = s.EndIndex,
                        TrackClass = trackClass ?? s.TrackClass,
                        Style = style ?? s.Style,
                        SpeedLimit = speedLimit != 0 ? speedLimit : s.SpeedLimit,
                        Priority = s.Priority,
                    });
                }

                results.Add(track);
            }

            return results;
        }

        /// <summary>Adds a generated chain to a track definition with fresh ids; returns the new ids.</summary>
        public static (List<string> NodeIds, List<string> SegmentIds) Commit(FuseTrackDefinition tracks, GeneratedTrack generated)
        {
            var nodeIds = new List<string>(generated.Nodes.Count);
            var takenNodeIds = new HashSet<string>(tracks.Nodes.Keys);
            var nextNodeIndex = 1;
            foreach (var n in generated.Nodes)
            {
                var id = TrackOps.NewNodeId(takenNodeIds, ref nextNodeIndex);
                TrackOps.AddNode(
                    tracks, id,
                    new FuseVector3((float)n.X, (float)n.Y, (float)n.Z),
                    new FuseVector3((float)n.RotX, (float)n.RotY, (float)n.RotZ),
                    n.FlipSwitchStand);
                nodeIds.Add(id);
            }

            var segmentIds = new List<string>(generated.Segments.Count);
            var takenSegmentIds = new HashSet<string>(tracks.Segments.Keys);
            var nextSegmentIndex = 1;
            foreach (var s in generated.Segments)
            {
                var id = TrackOps.NewSegmentId(takenSegmentIds, ref nextSegmentIndex);
                TrackOps.ConnectSegment(
                    tracks, id, nodeIds[s.StartIndex], nodeIds[s.EndIndex],
                    s.TrackClass ?? "main", s.Style ?? "standard", s.SpeedLimit);
                segmentIds.Add(id);
            }

            return (nodeIds, segmentIds);
        }

        private static (double X, double Z) Fwd(double originX, double originZ, double rotY, double dist)
        {
            var r = rotY * DegToRad;
            return (originX + (dist * Math.Sin(r)), originZ + (dist * Math.Cos(r)));
        }
    }
}
