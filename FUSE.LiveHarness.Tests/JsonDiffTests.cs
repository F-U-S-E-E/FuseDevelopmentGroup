using Fuse.LiveHarness.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Fuse.LiveHarness.Tests;

public class JsonDiffTests
{
    [Fact]
    public void Identical_Trees_Have_No_Deltas()
    {
        var deltas = JsonDiff.Compare(
            JToken.Parse("""{ "a": 1, "b": [1, 2] }"""),
            JToken.Parse("""{ "a": 1, "b": [1, 2] }"""));

        Assert.Empty(deltas);
    }

    [Fact]
    public void Detects_Changed_Value_With_Json_Path()
    {
        var deltas = JsonDiff.Compare(
            JToken.Parse("""{ "counts": { "nodes": 10 } }"""),
            JToken.Parse("""{ "counts": { "nodes": 11 } }"""));

        var delta = Assert.Single(deltas);
        Assert.Equal(JsonDeltaKind.Changed, delta.Kind);
        Assert.Equal("$.counts.nodes", delta.Path);
        Assert.Equal("10", delta.Left);
        Assert.Equal("11", delta.Right);
    }

    [Fact]
    public void Detects_Added_And_Removed_Members()
    {
        var deltas = JsonDiff.Compare(
            JToken.Parse("""{ "a": 1 }"""),
            JToken.Parse("""{ "b": 2 }"""));

        Assert.Contains(deltas, d => d.Kind == JsonDeltaKind.Removed && d.Path == "$.a");
        Assert.Contains(deltas, d => d.Kind == JsonDeltaKind.Added && d.Path == "$.b");
    }

    [Fact]
    public void Normalized_Then_Diffed_Ignores_Volatile_Fields_And_Array_Order()
    {
        var normalizer = new JsonNormalizer();
        var baseline = normalizer.Normalize(JToken.Parse("""{ "createdLocal": "x", "items": ["a", "b"] }"""));
        var current = normalizer.Normalize(JToken.Parse("""{ "createdLocal": "y", "items": ["b", "a"] }"""));

        Assert.Empty(JsonDiff.Compare(baseline, current));
    }

    [Fact]
    public void Catches_A_Coordinate_Regression_After_Normalization()
    {
        // The Bryson-style case: a scene-clone localPosition must not silently change.
        var normalizer = new JsonNormalizer();
        var baseline = normalizer.Normalize(JToken.Parse("""{ "localPosition": { "x": 12.3456, "y": 0.0, "z": -4.5 } }"""));
        var current = normalizer.Normalize(JToken.Parse("""{ "localPosition": { "x": 0.0, "y": 0.0, "z": -4.5 } }"""));

        var deltas = JsonDiff.Compare(baseline, current);
        Assert.Contains(deltas, d => d.Path == "$.localPosition.x" && d.Kind == JsonDeltaKind.Changed);
    }
}
