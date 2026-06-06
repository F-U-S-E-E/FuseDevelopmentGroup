using System;
using System.Collections.Generic;
using Fuse.Core.Model;

namespace Fuse.Core.Geometry
{
    /// <summary>Double-precision 3D point used by the track geometry math.</summary>
    public readonly struct Vec3d
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Vec3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>Node geometry inputs (position + euler pitch/yaw) for the bezier math.</summary>
    public readonly struct TrackNodeGeometry
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;
        public readonly double RotX;
        public readonly double RotY;

        public TrackNodeGeometry(double x, double y, double z, double rotX, double rotY)
        {
            X = x;
            Y = y;
            Z = z;
            RotX = rotX;
            RotY = rotY;
        }

        /// <summary>Promote a <see cref="FuseNode"/> (float) to double-precision geometry inputs.</summary>
        public static TrackNodeGeometry FromNode(FuseNode node) =>
            new TrackNodeGeometry(
                node.Position.x, node.Position.y, node.Position.z,
                node.Rotation.x, node.Rotation.y);
    }

    /// <summary>
    /// Faithful port of <c>mod_project/geometry.py</c> bezier + track geometry math
    /// (which itself mirrors the game's BezierCurve/BezierMath/TrackMath). Computed
    /// in <c>double</c> to match the Python float64 reference to ~1e-9. Verified by
    /// GeometryGoldenTests against values dumped from the Python functions.
    /// </summary>
    public static class BezierMath
    {
        // 24-point Gauss-Legendre nodes/weights (mod_project/constants.py _GAUSS_T/_GAUSS_C).
        private static readonly double[] GaussT =
        {
            -0.06405689286260563, 0.06405689286260563,
            -0.1911188674736163, 0.1911188674736163,
            -0.3150426796961634, 0.3150426796961634,
            -0.4337935076260451, 0.4337935076260451,
            -0.5454214713888396, 0.5454214713888396,
            -0.6480936519369755, 0.6480936519369755,
            -0.7401241915785544, 0.7401241915785544,
            -0.820001985973903, 0.820001985973903,
            -0.8864155270044011, 0.8864155270044011,
            -0.9382745520027328, 0.9382745520027328,
            -0.9747285559713095, 0.9747285559713095,
            -0.9951872199970213, 0.9951872199970213,
        };

        private static readonly double[] GaussC =
        {
            0.12793819534675216, 0.12793819534675216,
            0.1258374563468283, 0.1258374563468283,
            0.12167047292780339, 0.12167047292780339,
            0.1155056680537256, 0.1155056680537256,
            0.10744427011596563, 0.10744427011596563,
            0.09761865210411388, 0.09761865210411388,
            0.08619016153195327, 0.08619016153195327,
            0.0733464814110803, 0.0733464814110803,
            0.05929858491543678, 0.05929858491543678,
            0.04427743881741981, 0.04427743881741981,
            0.028531388628933663, 0.028531388628933663,
            0.0123412297999872, 0.0123412297999872,
        };

        private const double DegToRad = Math.PI / 180.0;
        private const double RadToDeg = 180.0 / Math.PI;

        public static Vec3d CubicPoint(Vec3d p0, Vec3d p1, Vec3d p2, Vec3d p3, double t)
        {
            var u = 1.0 - t;
            return new Vec3d(
                (u * u * u * p0.X) + (3 * u * u * t * p1.X) + (3 * u * t * t * p2.X) + (t * t * t * p3.X),
                (u * u * u * p0.Y) + (3 * u * u * t * p1.Y) + (3 * u * t * t * p2.Y) + (t * t * t * p3.Y),
                (u * u * u * p0.Z) + (3 * u * u * t * p1.Z) + (3 * u * t * t * p2.Z) + (t * t * t * p3.Z));
        }

        public static Vec3d CubicDeriv(Vec3d p0, Vec3d p1, Vec3d p2, Vec3d p3, double t)
        {
            var u = 1.0 - t;
            return new Vec3d(
                3 * ((u * u * (p1.X - p0.X)) + (2 * u * t * (p2.X - p1.X)) + (t * t * (p3.X - p2.X))),
                3 * ((u * u * (p1.Y - p0.Y)) + (2 * u * t * (p2.Y - p1.Y)) + (t * t * (p3.Y - p2.Y))),
                3 * ((u * u * (p1.Z - p0.Z)) + (2 * u * t * (p2.Z - p1.Z)) + (t * t * (p3.Z - p2.Z))));
        }

        /// <summary>De Casteljau split at t — returns the left and right control quads.</summary>
        public static (Vec3d[] Left, Vec3d[] Right) CubicSplit(Vec3d p0, Vec3d p1, Vec3d p2, Vec3d p3, double t)
        {
            var q0 = Lerp(p0, p1, t);
            var q1 = Lerp(p1, p2, t);
            var q2 = Lerp(p2, p3, t);
            var r0 = Lerp(q0, q1, t);
            var r1 = Lerp(q1, q2, t);
            var s = Lerp(r0, r1, t);
            return (new[] { p0, q0, r0, s }, new[] { s, r1, q2, p3 });
        }

        /// <summary>24-point Gaussian-quadrature arc length (offset to origin for stability).</summary>
        public static double LengthGauss(Vec3d p0, Vec3d p1, Vec3d p2, Vec3d p3)
        {
            var q0 = new Vec3d(0, 0, 0);
            var q1 = new Vec3d(p1.X - p0.X, p1.Y - p0.Y, p1.Z - p0.Z);
            var q2 = new Vec3d(p2.X - p0.X, p2.Y - p0.Y, p2.Z - p0.Z);
            var q3 = new Vec3d(p3.X - p0.X, p3.Y - p0.Y, p3.Z - p0.Z);

            var total = 0.0;
            for (var i = 0; i < GaussT.Length; i++)
            {
                var t = (0.5 * GaussT[i]) + 0.5;
                var d = CubicDeriv(q0, q1, q2, q3, t);
                total += GaussC[i] * Math.Sqrt((d.X * d.X) + (d.Y * d.Y) + (d.Z * d.Z));
            }

            return 0.5 * total;
        }

        public static Vec3d NodeForward(double rotXDeg, double rotYDeg)
        {
            var cosRx = Math.Cos(rotXDeg * DegToRad);
            return new Vec3d(
                Math.Sin(rotYDeg * DegToRad) * cosRx,
                -Math.Sin(rotXDeg * DegToRad),
                Math.Cos(rotYDeg * DegToRad) * cosRx);
        }

        /// <summary>Tangent factor from node rotations: Lerp(0.35, 0.41, InverseLerp(45, 90, angle)).</summary>
        public static double TangentFactor(double rx0, double ry0, double rx1, double ry1)
        {
            var f0 = NodeForward(rx0, ry0);
            var f1 = NodeForward(rx1, ry1);
            var dot = Math.Max(-1.0, Math.Min(1.0, (f0.X * f1.X) + (f0.Y * f1.Y) + (f0.Z * f1.Z)));
            var angle = Math.Acos(dot) * RadToDeg;
            if (angle > 90.0)
            {
                angle = 180.0 - angle;
            }

            var t = Math.Max(0.0, Math.Min(1.0, (angle - 45.0) / 45.0));
            return 0.35 + (t * (0.41 - 0.35));
        }

        /// <summary>Cubic control points for the segment n0→n1 (tangent side chosen toward the other node).</summary>
        public static (Vec3d P0, Vec3d P1, Vec3d P2, Vec3d P3) ControlPoints(TrackNodeGeometry n0, TrackNodeGeometry n1)
        {
            var ep0 = new Vec3d(n0.X, n0.Y, n0.Z);
            var ep1 = new Vec3d(n1.X, n1.Y, n1.Z);
            var dx = n1.X - n0.X;
            var dy = n1.Y - n0.Y;
            var dz = n1.Z - n0.Z;
            var dist3d = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            if (dist3d < 0.1)
            {
                return (ep0, ep0, ep1, ep1);
            }

            var factor = TangentFactor(n0.RotX, n0.RotY, n1.RotX, n1.RotY);
            var d = dist3d * factor;
            var p1 = TangentPointTowardOther(n0, ep1, d);
            var p2 = TangentPointTowardOther(n1, ep0, d);
            return (ep0, p1, p2, ep1);
        }

        public static double SegmentLength(TrackNodeGeometry n0, TrackNodeGeometry n1)
        {
            var (p0, p1, p2, p3) = ControlPoints(n0, n1);
            return LengthGauss(p0, p1, p2, p3);
        }

        public static double SegmentGrade(TrackNodeGeometry n0, TrackNodeGeometry n1)
        {
            var length = SegmentLength(n0, n1);
            if (length < 0.001)
            {
                return 0.0;
            }

            return (n1.Y - n0.Y) / length * 100.0;
        }

        /// <summary>Curvature in degrees per 100 ft (TrackMath.CalculateCurveDegrees). 0 for straight track.</summary>
        public static double SegmentCurveDegrees(TrackNodeGeometry n0, TrackNodeGeometry n1)
        {
            double ax = n0.X, az = n0.Z;
            double bx = n1.X, bz = n1.Z;
            var ayDeg = n0.RotY;
            var byDeg = n1.RotY;

            var delta = ayDeg - byDeg;
            while (delta > 180) delta -= 360;
            while (delta < -180) delta += 360;

            var ry0 = ayDeg * DegToRad;
            var ry1 = byDeg * DegToRad;
            if (delta < 0)
            {
                ry0 += Math.PI;
                ry1 += Math.PI;
            }

            var rx0 = Math.Cos(ry0);
            var rz0 = -Math.Sin(ry0);
            var rx1 = Math.Cos(ry1);
            var rz1 = -Math.Sin(ry1);

            var denom = (rx0 * rz1) - (rz0 * rx1);
            if (Math.Abs(denom) < 1e-6)
            {
                return 0.0;
            }

            var dx = bx - ax;
            var dz = bz - az;
            var t = ((dx * rz1) - (dz * rx1)) / denom;
            var cx = ax + (t * rx0);
            var cz = az + (t * rz0);

            var ra = Math.Sqrt(((ax - cx) * (ax - cx)) + ((az - cz) * (az - cz)));
            var rb = Math.Sqrt(((bx - cx) * (bx - cx)) + ((bz - cz) * (bz - cz)));
            var radiusM = (ra + rb) * 0.5;
            var radiusFt = radiusM * 3.28084;
            if (radiusFt < 1.0)
            {
                return 0.0;
            }

            var arg = Math.Max(-1.0, Math.Min(1.0, 100.0 / (2.0 * radiusFt)));
            return 2.0 * Math.Asin(arg) * RadToDeg;
        }

        /// <summary>Adaptive XZ polyline approximation of the segment (BezierCurve.Approximate defaults).</summary>
        public static List<(double X, double Z)> ApproximateXz(
            Vec3d p0, Vec3d p1, Vec3d p2, Vec3d p3,
            double flatness = 1.000005, double minLen = 0.5, double maxLen = 40.0, int depth = 16)
        {
            var pts = new List<(double X, double Z)> { (p0.X, p0.Z) };
            ApproxRecurse(p0, p1, p2, p3, pts, flatness, minLen, maxLen, depth);
            pts.Add((p3.X, p3.Z));
            return pts;
        }

        /// <summary>Convenience: the XZ polyline for the segment between two FUSE nodes.</summary>
        public static List<(double X, double Z)> SegmentPolylineXz(FuseNode a, FuseNode b)
        {
            var (p0, p1, p2, p3) = ControlPoints(TrackNodeGeometry.FromNode(a), TrackNodeGeometry.FromNode(b));
            return ApproximateXz(p0, p1, p2, p3);
        }

        /// <summary>Effective speed limit: 0 means use the class default (Mainline 35, Branch 25, Industrial 15).</summary>
        public static int EffectiveSpeedLimit(int speedLimit, string trackClass)
        {
            if (speedLimit != 0)
            {
                return speedLimit;
            }

            switch (trackClass)
            {
                case "Branch": return 25;
                case "Industrial": return 15;
                default: return 35;
            }
        }

        private static void ApproxRecurse(
            Vec3d p0, Vec3d p1, Vec3d p2, Vec3d p3,
            List<(double X, double Z)> pts, double flatness, double minLen, double maxLen, int depth)
        {
            var chord = Math.Sqrt(((p3.X - p0.X) * (p3.X - p0.X)) + ((p3.Z - p0.Z) * (p3.Z - p0.Z)));
            if (depth <= 0)
            {
                return;
            }

            var perim =
                Math.Sqrt(((p1.X - p0.X) * (p1.X - p0.X)) + ((p1.Z - p0.Z) * (p1.Z - p0.Z))) +
                Math.Sqrt(((p2.X - p1.X) * (p2.X - p1.X)) + ((p2.Z - p1.Z) * (p2.Z - p1.Z))) +
                Math.Sqrt(((p3.X - p2.X) * (p3.X - p2.X)) + ((p3.Z - p2.Z) * (p3.Z - p2.Z)));
            var flat = perim < flatness * chord;
            var shortSeg = chord / 2.0 < minLen;
            var longSeg = chord > maxLen;
            if (!longSeg && (flat || shortSeg))
            {
                return;
            }

            var (left, right) = CubicSplit(p0, p1, p2, p3, 0.5);
            var mid = left[3];
            ApproxRecurse(left[0], left[1], left[2], left[3], pts, flatness, minLen, maxLen, depth - 1);
            pts.Add((mid.X, mid.Z));
            ApproxRecurse(right[0], right[1], right[2], right[3], pts, flatness, minLen, maxLen, depth - 1);
        }

        private static Vec3d TangentPointTowardOther(TrackNodeGeometry node, Vec3d other, double distance)
        {
            var fwd = NodeForward(node.RotX, node.RotY);
            var forward = new Vec3d(node.X + (fwd.X * distance), node.Y + (fwd.Y * distance), node.Z + (fwd.Z * distance));
            var backward = new Vec3d(node.X - (fwd.X * distance), node.Y - (fwd.Y * distance), node.Z - (fwd.Z * distance));
            return Dist(backward, other) < Dist(forward, other) ? backward : forward;
        }

        private static double Dist(Vec3d a, Vec3d b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            var dz = a.Z - b.Z;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        private static Vec3d Lerp(Vec3d a, Vec3d b, double t) =>
            new Vec3d(a.X + (t * (b.X - a.X)), a.Y + (t * (b.Y - a.Y)), a.Z + (t * (b.Z - a.Z)));
    }
}
