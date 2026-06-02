using System;
using System.IO;
using Fuse.Core.Geometry;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Fuse.Core.Tests;

/// <summary>
/// Golden test: <see cref="TrackGenerators"/> must reproduce the Python reference
/// generators (positions/rotations to ~1e-9 and identical segment connectivity).
/// IDs are random in Python, so connectivity is compared by node index.
/// Fixture: <c>Fixtures/generators-golden.json</c> (regenerate with tmp_gen_golden.py).
/// </summary>
public class GeneratorsGoldenTests
{
    private static JObject Cases() =>
        (JObject)JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "generators-golden.json")))["cases"]!;

    [Fact]
    public void Straight_Matches_Python() =>
        AssertCase("straight", TrackGenerators.Straight(0, 0, 0, 0.0, 100.0, 5.0, 4));

    [Fact]
    public void CurveLeft_Matches_Python() =>
        AssertCase("curveLeft", TrackGenerators.Curve(0, 0, 0, 0.0, 200.0, 90.0, 0.0, right: false, nSegments: 6));

    [Fact]
    public void CurveRightGrade_Matches_Python() =>
        AssertCase("curveRightGrade", TrackGenerators.Curve(10, 5, 20, 45.0, 150.0, 60.0, 12.0, right: true, nSegments: 5));

    [Fact]
    public void Turnout_Matches_Python() =>
        AssertCase("turnout", TrackGenerators.Turnout(0, 0, 0, 30.0, divergeAngle: 10.0, legLength: 30.0, right: false));

    [Fact]
    public void Wye_Matches_Python() =>
        AssertCase("wye", TrackGenerators.Wye(0, 0, 0, 90.0, leftAngle: 12.0, rightAngle: 8.0, legLength: 25.0));

    [Fact]
    public void ParallelBoth_Matches_Python()
    {
        var source = TrackGenerators.Curve(0, 0, 0, 0.0, 100.0, 30.0, 0.0, right: false, nSegments: 3);
        var result = TrackGenerators.Parallel(source, 20.0, nTracks: 1, side: ParallelSide.Both);

        var tracks = (JArray)((JObject)Cases()["parallelBoth"]!)["tracks"]!;
        Assert.Equal(tracks.Count, result.Count);
        for (var t = 0; t < tracks.Count; t++)
        {
            AssertTrack((JObject)tracks[t]!, result[t], $"parallelBoth[{t}]");
        }
    }

    private static void AssertCase(string name, GeneratedTrack actual) =>
        AssertTrack((JObject)Cases()[name]!, actual, name);

    private static void AssertTrack(JObject expected, GeneratedTrack actual, string name)
    {
        var nodes = (JArray)expected["nodes"]!;
        var segments = (JArray)expected["segments"]!;

        Assert.Equal(nodes.Count, actual.Nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            var e = (JObject)nodes[i]!;
            var a = actual.Nodes[i];
            Close(e["x"]!.Value<double>(), a.X, $"{name}[{i}].x");
            Close(e["y"]!.Value<double>(), a.Y, $"{name}[{i}].y");
            Close(e["z"]!.Value<double>(), a.Z, $"{name}[{i}].z");
            Close(e["rotX"]!.Value<double>(), a.RotX, $"{name}[{i}].rotX");
            Close(e["rotY"]!.Value<double>(), a.RotY, $"{name}[{i}].rotY");
        }

        Assert.Equal(segments.Count, actual.Segments.Count);
        for (var i = 0; i < segments.Count; i++)
        {
            var e = (JObject)segments[i]!;
            Assert.Equal(e["startIndex"]!.Value<int>(), actual.Segments[i].StartIndex);
            Assert.Equal(e["endIndex"]!.Value<int>(), actual.Segments[i].EndIndex);
        }
    }

    private static void Close(double expected, double actual, string what) =>
        Assert.True(Math.Abs(expected - actual) <= 1e-9, $"{what}: expected {expected:R}, got {actual:R}");
}
