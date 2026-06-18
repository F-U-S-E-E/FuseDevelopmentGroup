using System;
using System.Collections.Generic;

namespace Fuse.Core.Geometry
{
    /// <summary>Closest-point projection of a point onto a polyline.</summary>
    public sealed class PolylineProjection
    {
        public PolylineProjection((double X, double Z) point, double distance, int segmentIndex, double t)
        {
            Point = point;
            Distance = distance;
            SegmentIndex = segmentIndex;
            T = t;
        }

        public (double X, double Z) Point { get; }
        public double Distance { get; }
        public int SegmentIndex { get; }
        public double T { get; }
    }

    /// <summary>Algebraic least-squares circle fit result.</summary>
    public sealed class CircleFit
    {
        public CircleFit((double X, double Z) center, double radius, double rmsError, int turnSign)
        {
            Center = center;
            Radius = radius;
            RmsError = rmsError;
            TurnSign = turnSign;
        }

        public (double X, double Z) Center { get; }
        public double Radius { get; }
        public double RmsError { get; }
        public int TurnSign { get; }
    }

    /// <summary>Constant-radius arc fitted to a chain of X/Z points.</summary>
    public sealed class ArcFit
    {
        public ArcFit(
            (double X, double Z) center, double radius, double rmsError, int turnSign,
            double startAngle, double endAngle, double deltaAngleRad, double deltaAngleDeg,
            double arcLength, double chordLength, IReadOnlyList<(double X, double Z, double RotY)> points)
        {
            Center = center;
            Radius = radius;
            RmsError = rmsError;
            TurnSign = turnSign;
            StartAngle = startAngle;
            EndAngle = endAngle;
            DeltaAngleRad = deltaAngleRad;
            DeltaAngleDeg = deltaAngleDeg;
            ArcLength = arcLength;
            ChordLength = chordLength;
            Points = points;
        }

        public (double X, double Z) Center { get; }
        public double Radius { get; }
        public double RmsError { get; }
        public int TurnSign { get; }
        public double StartAngle { get; }
        public double EndAngle { get; }
        public double DeltaAngleRad { get; }
        public double DeltaAngleDeg { get; }
        public double ArcLength { get; }
        public double ChordLength { get; }
        public IReadOnlyList<(double X, double Z, double RotY)> Points { get; }
    }

    public sealed class RadiusSample
    {
        public RadiusSample(int index, (double X, double Z) point, double radius)
        {
            Index = index;
            Point = point;
            Radius = radius;
        }

        public int Index { get; }
        public (double X, double Z) Point { get; }
        public double Radius { get; }
    }

    public sealed class DeviationResult
    {
        public DeviationResult(IReadOnlyList<(( double X, double Z) From, (double X, double Z) To, double Distance, int SegmentIndex)> samples, double? maxDistance, double? rmsDistance)
        {
            Samples = samples;
            MaxDistance = maxDistance;
            RmsDistance = rmsDistance;
        }

        public IReadOnlyList<((double X, double Z) From, (double X, double Z) To, double Distance, int SegmentIndex)> Samples { get; }
        public double? MaxDistance { get; }
        public double? RmsDistance { get; }
    }

    /// <summary>
    /// 2D (X/Z) alignment helpers ported from <c>edit_tiles/alignment.py</c>: polyline
    /// length/stationing, point→polyline projection + deviation, algebraic circle fit,
    /// constant-radius arc fitting, and local (circumcircle) radius sampling. Pure and
    /// Unity-free; verified against the Python reference by AlignmentGoldenTests.
    /// </summary>
    public static class Alignment
    {
        private const double Tau = 2.0 * Math.PI;

        public static double PolylineLength(IReadOnlyList<(double X, double Z)> points)
        {
            if (points == null || points.Count < 2)
            {
                return 0.0;
            }

            var total = 0.0;
            var (px, pz) = points[0];
            for (var i = 1; i < points.Count; i++)
            {
                var (x, z) = points[i];
                total += Math.Sqrt(((x - px) * (x - px)) + ((z - pz) * (z - pz)));
                px = x;
                pz = z;
            }

            return total;
        }

        public static double[] CumulativeLengths(IReadOnlyList<(double X, double Z)> points)
        {
            if (points == null || points.Count == 0)
            {
                return Array.Empty<double>();
            }

            var lengths = new double[points.Count];
            var total = 0.0;
            var (px, pz) = points[0];
            lengths[0] = 0.0;
            for (var i = 1; i < points.Count; i++)
            {
                var (x, z) = points[i];
                total += Math.Sqrt(((x - px) * (x - px)) + ((z - pz) * (z - pz)));
                lengths[i] = total;
                px = x;
                pz = z;
            }

            return lengths;
        }

        public static ((double X, double Z) Point, double Distance, double T) ProjectPointToSegment(
            (double X, double Z) point, (double X, double Z) start, (double X, double Z) end)
        {
            double dx = end.X - start.X, dz = end.Z - start.Z;
            var segLen2 = (dx * dx) + (dz * dz);
            if (segLen2 <= 1e-9)
            {
                return (start, Math.Sqrt(((point.X - start.X) * (point.X - start.X)) + ((point.Z - start.Z) * (point.Z - start.Z))), 0.0);
            }

            var t = (((point.X - start.X) * dx) + ((point.Z - start.Z) * dz)) / segLen2;
            t = Math.Max(0.0, Math.Min(1.0, t));
            double qx = start.X + (dx * t), qz = start.Z + (dz * t);
            return ((qx, qz), Math.Sqrt(((point.X - qx) * (point.X - qx)) + ((point.Z - qz) * (point.Z - qz))), t);
        }

        public static PolylineProjection ProjectPointToPolyline((double X, double Z) point, IReadOnlyList<(double X, double Z)> points)
        {
            if (points == null || points.Count == 0)
            {
                return null;
            }

            if (points.Count == 1)
            {
                var d = Math.Sqrt(((point.X - points[0].X) * (point.X - points[0].X)) + ((point.Z - points[0].Z) * (point.Z - points[0].Z)));
                return new PolylineProjection(points[0], d, 0, 0.0);
            }

            PolylineProjection best = null;
            for (var idx = 0; idx < points.Count - 1; idx++)
            {
                var s = ProjectPointToSegment(point, points[idx], points[idx + 1]);
                if (best == null || s.Distance < best.Distance)
                {
                    best = new PolylineProjection(s.Point, s.Distance, idx, s.T);
                }
            }

            return best;
        }

        public static DeviationResult DeviationSamples(IReadOnlyList<(double X, double Z)> samplePoints, IReadOnlyList<(double X, double Z)> targetPolyline)
        {
            var samples = new List<((double X, double Z) From, (double X, double Z) To, double Distance, int SegmentIndex)>();
            if (samplePoints == null || targetPolyline == null || samplePoints.Count == 0 || targetPolyline.Count == 0)
            {
                return new DeviationResult(samples, null, null);
            }

            var sqTotal = 0.0;
            var maxDist = 0.0;
            foreach (var p in samplePoints)
            {
                var hit = ProjectPointToPolyline(p, targetPolyline);
                if (hit == null)
                {
                    continue;
                }

                sqTotal += hit.Distance * hit.Distance;
                maxDist = Math.Max(maxDist, hit.Distance);
                samples.Add((p, hit.Point, hit.Distance, hit.SegmentIndex));
            }

            if (samples.Count == 0)
            {
                return new DeviationResult(samples, null, null);
            }

            return new DeviationResult(samples, maxDist, Math.Sqrt(sqTotal / samples.Count));
        }

        public static int SignedTurn(IReadOnlyList<(double X, double Z)> points)
        {
            if (points == null || points.Count < 3)
            {
                return 0;
            }

            var total = 0.0;
            for (var idx = 1; idx < points.Count - 1; idx++)
            {
                var (ax, az) = points[idx - 1];
                var (bx, bz) = points[idx];
                var (cx, cz) = points[idx + 1];
                double abx = bx - ax, abz = bz - az, bcx = cx - bx, bcz = cz - bz;
                total += (abx * bcz) - (abz * bcx);
            }

            if (Math.Abs(total) < 1e-9)
            {
                return 0;
            }

            return total > 0.0 ? 1 : -1;
        }

        public static CircleFit FitCircle(IReadOnlyList<(double X, double Z)> points)
        {
            if (points == null || points.Count < 3)
            {
                return null;
            }

            var n = (double)points.Count;
            var meanX = 0.0;
            var meanZ = 0.0;
            foreach (var (x, z) in points)
            {
                meanX += x;
                meanZ += z;
            }

            meanX /= n;
            meanZ /= n;

            double suu = 0, svv = 0, suv = 0, suuu = 0, svvv = 0, suvv = 0, svuu = 0;
            foreach (var (x, z) in points)
            {
                double u = x - meanX, v = z - meanZ;
                suu += u * u;
                svv += v * v;
                suv += u * v;
                suuu += u * u * u;
                svvv += v * v * v;
                suvv += u * v * v;
                svuu += v * u * u;
            }

            var det = (suu * svv) - (suv * suv);
            if (Math.Abs(det) < 1e-9)
            {
                return null;
            }

            var rhsU = 0.5 * (suuu + suvv);
            var rhsV = 0.5 * (svvv + svuu);
            var uc = ((rhsU * svv) - (rhsV * suv)) / det;
            var vc = ((rhsV * suu) - (rhsU * suv)) / det;

            var center = (meanX + uc, meanZ + vc);
            var radiusSum = 0.0;
            var radii = new double[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                radii[i] = Math.Sqrt(((points[i].X - center.Item1) * (points[i].X - center.Item1)) + ((points[i].Z - center.Item2) * (points[i].Z - center.Item2)));
                radiusSum += radii[i];
            }

            var radius = radiusSum / n;
            var rmsAcc = 0.0;
            foreach (var r in radii)
            {
                rmsAcc += (r - radius) * (r - radius);
            }

            return new CircleFit(center, radius, Math.Sqrt(rmsAcc / n), SignedTurn(points));
        }

        public static double[] UnwrapArcAngles(IReadOnlyList<(double X, double Z)> points, (double X, double Z) center, int turnSign)
        {
            if (points == null || points.Count == 0)
            {
                return Array.Empty<double>();
            }

            var angles = new double[points.Count];
            angles[0] = Math.Atan2(points[0].Z - center.Z, points[0].X - center.X);
            for (var i = 1; i < points.Count; i++)
            {
                var angle = Math.Atan2(points[i].Z - center.Z, points[i].X - center.X);
                var prev = angles[i - 1];
                while (angle - prev > Math.PI)
                {
                    angle -= Tau;
                }

                while (angle - prev < -Math.PI)
                {
                    angle += Tau;
                }

                if (turnSign >= 0 && angle < prev)
                {
                    angle += Tau;
                }
                else if (turnSign < 0 && angle > prev)
                {
                    angle -= Tau;
                }

                angles[i] = angle;
            }

            return angles;
        }

        public static ArcFit FitArcToChain(IReadOnlyList<(double X, double Z)> points)
        {
            if (points == null || points.Count < 3)
            {
                return null;
            }

            var circle = FitCircle(points);
            if (circle == null || circle.Radius <= 0.01)
            {
                return null;
            }

            var center = circle.Center;
            var turnSign = circle.TurnSign != 0 ? circle.TurnSign : 1;
            var angles = UnwrapArcAngles(points, center, turnSign);
            var cum = CumulativeLengths(points);
            var totalLength = cum.Length > 0 ? cum[cum.Length - 1] : 0.0;
            if (totalLength <= 0.01)
            {
                return null;
            }

            double startAngle = angles[0], endAngle = angles[angles.Length - 1];
            var deltaAngle = endAngle - startAngle;
            if (Math.Abs(deltaAngle) <= 1e-6)
            {
                return null;
            }

            var tangentSign = deltaAngle >= 0.0 ? 1.0 : -1.0;
            var radius = circle.Radius;
            var fitted = new List<(double X, double Z, double RotY)>(points.Count);
            foreach (var distance in cum)
            {
                var t = distance / totalLength;
                var angle = startAngle + (deltaAngle * t);
                var x = center.X + (radius * Math.Cos(angle));
                var z = center.Z + (radius * Math.Sin(angle));
                var dx = -Math.Sin(angle) * tangentSign;
                var dz = Math.Cos(angle) * tangentSign;
                var rotY = Mod360(Math.Atan2(dx, dz) * 180.0 / Math.PI);
                fitted.Add((x, z, rotY));
            }

            var chord = Math.Sqrt(
                ((points[points.Count - 1].X - points[0].X) * (points[points.Count - 1].X - points[0].X)) +
                ((points[points.Count - 1].Z - points[0].Z) * (points[points.Count - 1].Z - points[0].Z)));
            return new ArcFit(
                center, radius, circle.RmsError, turnSign,
                startAngle, endAngle, deltaAngle, deltaAngle * 180.0 / Math.PI,
                Math.Abs(deltaAngle) * radius, chord, fitted);
        }

        public static double? Circumradius((double X, double Z) a, (double X, double Z) b, (double X, double Z) c)
        {
            var ab = Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Z - a.Z) * (b.Z - a.Z)));
            var bc = Math.Sqrt(((c.X - b.X) * (c.X - b.X)) + ((c.Z - b.Z) * (c.Z - b.Z)));
            var ca = Math.Sqrt(((a.X - c.X) * (a.X - c.X)) + ((a.Z - c.Z) * (a.Z - c.Z)));
            var twiceArea = Math.Abs(((b.X - a.X) * (c.Z - a.Z)) - ((b.Z - a.Z) * (c.X - a.X)));
            if (ab <= 1e-6 || bc <= 1e-6 || ca <= 1e-6 || twiceArea <= 1e-6)
            {
                return null;
            }

            return (ab * bc * ca) / (2.0 * twiceArea);
        }

        public static IReadOnlyList<RadiusSample> LocalRadiusSamples(IReadOnlyList<(double X, double Z)> points)
        {
            var samples = new List<RadiusSample>();
            if (points == null || points.Count < 3)
            {
                return samples;
            }

            for (var idx = 1; idx < points.Count - 1; idx++)
            {
                var radius = Circumradius(points[idx - 1], points[idx], points[idx + 1]);
                if (radius == null)
                {
                    continue;
                }

                samples.Add(new RadiusSample(idx, points[idx], radius.Value));
            }

            return samples;
        }

        private static double Mod360(double v)
        {
            var m = v % 360.0;
            return m < 0 ? m + 360.0 : m;
        }
    }
}
