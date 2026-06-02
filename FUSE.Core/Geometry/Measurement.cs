using System;
using Fuse.Core.Model;

namespace Fuse.Core.Geometry
{
    /// <summary>Bearing/distance/grade measurement helpers (editor rotY convention: +Z = 0°, +X = 90°).</summary>
    public static class Measurement
    {
        /// <summary>Heading bearing in degrees [0, 360) from one XZ point toward another.</summary>
        public static double BearingDeg((double X, double Z) from, (double X, double Z) to)
        {
            var deg = Math.Atan2(to.X - from.X, to.Z - from.Z) * 180.0 / Math.PI;
            var m = deg % 360.0;
            return m < 0 ? m + 360.0 : m;
        }

        public static double DistanceXz((double X, double Z) a, (double X, double Z) b)
        {
            double dx = b.X - a.X, dz = b.Z - a.Z;
            return Math.Sqrt((dx * dx) + (dz * dz));
        }

        public static double Distance3d(FuseVector3 a, FuseVector3 b) => FuseVector3.Distance(a, b);

        /// <summary>Grade as a percentage (rise / horizontal run × 100); 0 for a zero run.</summary>
        public static double GradePercent(double rise, double run) =>
            Math.Abs(run) < 1e-9 ? 0.0 : rise / run * 100.0;
    }
}
