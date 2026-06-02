using System;
using System.Globalization;
using System.IO;
using Fuse.Core.Geometry;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Fuse.Core.Tests;

/// <summary>
/// Golden test: <see cref="BezierMath"/> must reproduce the Python reference
/// (<c>mod_project/geometry.py</c>) to ~1e-9. The fixture
/// <c>Fixtures/geometry-golden.json</c> is dumped by <c>tmp_geom_golden.py</c>
/// from the actual Python functions — regenerate it if the geometry changes.
/// </summary>
public class GeometryGoldenTests
{
    private static JObject LoadGolden()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "geometry-golden.json");
        return JObject.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void ControlPoints_Length_Tangent_Grade_Curve_Match_Python()
    {
        foreach (var c in LoadGolden()["cases"]!)
        {
            var n0 = Node(c["n0"]!);
            var n1 = Node(c["n1"]!);

            var (p0, p1, p2, p3) = BezierMath.ControlPoints(n0, n1);
            var ctrl = (JArray)c["control"]!;
            AssertVec(ctrl[0], p0, "P0");
            AssertVec(ctrl[1], p1, "P1");
            AssertVec(ctrl[2], p2, "P2");
            AssertVec(ctrl[3], p3, "P3");

            Close(c["length"]!.Value<double>(), BezierMath.SegmentLength(n0, n1), 1e-9, "length");
            Close(c["tangentFactor"]!.Value<double>(), BezierMath.TangentFactor(n0.RotX, n0.RotY, n1.RotX, n1.RotY), 1e-12, "tangentFactor");
            Close(c["grade"]!.Value<double>(), BezierMath.SegmentGrade(n0, n1), 1e-9, "grade");
            Close(c["curveDegrees"]!.Value<double>(), BezierMath.SegmentCurveDegrees(n0, n1), 1e-7, "curveDegrees");
        }
    }

    [Fact]
    public void CubicPoint_And_Deriv_Match_Python()
    {
        foreach (var c in LoadGolden()["cases"]!)
        {
            var ctrl = (JArray)c["control"]!;
            var p0 = Vec(ctrl[0]);
            var p1 = Vec(ctrl[1]);
            var p2 = Vec(ctrl[2]);
            var p3 = Vec(ctrl[3]);

            foreach (var prop in ((JObject)c["pointsAtT"]!).Properties())
            {
                var t = double.Parse(prop.Name, CultureInfo.InvariantCulture);
                AssertVec(prop.Value, BezierMath.CubicPoint(p0, p1, p2, p3, t), $"point@{t}");
            }

            foreach (var prop in ((JObject)c["derivAtT"]!).Properties())
            {
                var t = double.Parse(prop.Name, CultureInfo.InvariantCulture);
                AssertVec(prop.Value, BezierMath.CubicDeriv(p0, p1, p2, p3, t), $"deriv@{t}");
            }
        }
    }

    [Fact]
    public void NodeForward_Matches_Python()
    {
        foreach (var f in LoadGolden()["forward"]!)
        {
            var v = BezierMath.NodeForward(f["rotX"]!.Value<double>(), f["rotY"]!.Value<double>());
            AssertVec(f["vec"]!, v, "forward");
        }
    }

    private static TrackNodeGeometry Node(JToken t) => new(
        t["x"]!.Value<double>(), t["y"]!.Value<double>(), t["z"]!.Value<double>(),
        t["rotX"]!.Value<double>(), t["rotY"]!.Value<double>());

    private static Vec3d Vec(JToken arr)
    {
        var a = (JArray)arr;
        return new Vec3d(a[0]!.Value<double>(), a[1]!.Value<double>(), a[2]!.Value<double>());
    }

    private static void AssertVec(JToken arr, Vec3d v, string what)
    {
        var a = (JArray)arr;
        Close(a[0]!.Value<double>(), v.X, 1e-9, what + ".x");
        Close(a[1]!.Value<double>(), v.Y, 1e-9, what + ".y");
        Close(a[2]!.Value<double>(), v.Z, 1e-9, what + ".z");
    }

    private static void Close(double expected, double actual, double tol, string what) =>
        Assert.True(Math.Abs(expected - actual) <= tol,
            $"{what}: expected {expected:R}, got {actual:R} (|Δ|={Math.Abs(expected - actual):R} > {tol:R})");
}
