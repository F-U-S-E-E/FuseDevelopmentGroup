using System;
using System.IO;
using Fuse.Core.Serialization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Fuse.Core.Tests
{
    /// <summary>
    /// Phase 0 gate: the Unity-free <see cref="FuseCoreSerializer"/> must load the
    /// canonical FUSE example and re-emit it losslessly. A stable fixed point
    /// (json2 == json3) proves the model + converters + migration normalization
    /// round-trip without drift. The stronger cross-serializer test (vs the
    /// shipping FuseSerializer) lives in FUSE.Tests, which can reference both.
    /// </summary>
    public class RoundTripTests
    {
        private static string ExamplePath =>
            Path.Combine(AppContext.BaseDirectory, "fuse-mod.example.json");

        private static string ReadExample() => File.ReadAllText(ExamplePath);

        [Fact]
        public void Example_Loads_WithExpectedHeader()
        {
            var definition = FuseCoreSerializer.FromJson(ReadExample());

            Assert.NotNull(definition);
            Assert.Equal("FUSE.Example.MurphyBranch", definition.Id);
            Assert.Equal("1.0", definition.SchemaVersion);
            Assert.NotNull(definition.Tracks);
            Assert.NotNull(definition.World);
            Assert.NotNull(definition.Operations);
        }

        [Fact]
        public void RoundTrip_IsStableFixedPoint()
        {
            var definition1 = FuseCoreSerializer.FromJson(ReadExample());
            var json2 = FuseCoreSerializer.ToJson(definition1);

            var definition2 = FuseCoreSerializer.FromJson(json2);
            var json3 = FuseCoreSerializer.ToJson(definition2);

            Assert.True(
                JToken.DeepEquals(JObject.Parse(json2), JObject.Parse(json3)),
                "FUSE.Core serializer round-trip must be a stable fixed point (json2 == json3).");
        }
    }
}
