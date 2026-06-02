using System.Linq;
using Fuse.LiveHarness.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Fuse.LiveHarness.Tests;

public class JsonNormalizerTests
{
    [Fact]
    public void Strips_Volatile_Keys_Recursively()
    {
        var normalizer = new JsonNormalizer();
        var result = (JObject)normalizer.Normalize(JToken.Parse(
            """{ "createdLocal": "2026-01-01", "counts": { "pid": 99, "nodes": 3 } }"""));

        Assert.False(result.ContainsKey("createdLocal"));
        Assert.False(((JObject)result["counts"]!).ContainsKey("pid"));
        Assert.Equal(3, result["counts"]!["nodes"]!.Value<int>());
    }

    [Fact]
    public void Rounds_Floats_To_Configured_Precision()
    {
        var normalizer = new JsonNormalizer(decimals: 2);
        var result = normalizer.Normalize(JToken.Parse("""{ "x": 1.234567, "n": 10 }"""));

        Assert.Equal(1.23, result["x"]!.Value<double>(), 5);
        Assert.Equal(10, result["n"]!.Value<int>()); // integers untouched
    }

    [Fact]
    public void Sorts_Object_Keys_Recursively()
    {
        var normalizer = new JsonNormalizer();
        var result = (JObject)normalizer.Normalize(JToken.Parse("""{ "b": 1, "a": { "z": 1, "y": 2 } }"""));

        Assert.Equal("a,b", string.Join(",", result.Properties().Select(p => p.Name)));
        Assert.Equal("y,z", string.Join(",", ((JObject)result["a"]!).Properties().Select(p => p.Name)));
    }

    [Fact]
    public void Sorts_Arrays_So_Reordering_Is_Not_Drift()
    {
        var normalizer = new JsonNormalizer();
        var one = normalizer.Canonical("""{ "items": ["b", "a", "c"] }""");
        var two = normalizer.Canonical("""{ "items": ["c", "b", "a"] }""");

        Assert.Equal(one, two);
    }

    [Fact]
    public void Array_Sorting_Can_Be_Disabled()
    {
        var normalizer = new JsonNormalizer(sortArrays: false);
        var one = normalizer.Canonical("""{ "items": ["b", "a"] }""");
        var two = normalizer.Canonical("""{ "items": ["a", "b"] }""");

        Assert.NotEqual(one, two);
    }
}
