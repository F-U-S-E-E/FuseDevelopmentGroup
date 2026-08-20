using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Serialization
{
    public sealed class FuseJsonSchemaContractTests
    {
        [Fact]
        public void Gauge_IsSegmentMetadata_WithAllEditorAuthoredValues()
        {
            var schemaPath = Path.Combine(
                AppContext.BaseDirectory,
                "fuse-mod.schema.json");
            var schema = JObject.Parse(File.ReadAllText(schemaPath));
            var definitions = Assert.IsType<JObject>(schema["$defs"]);
            var node = Assert.IsType<JObject>(definitions["trackNode"]);
            var segment = Assert.IsType<JObject>(definitions["trackSegment"]);
            var nodeProperties = Assert.IsType<JObject>(node["properties"]);
            var segmentProperties = Assert.IsType<JObject>(
                segment["properties"]);

            Assert.Null(nodeProperties["gauge"]);
            var gauge = Assert.IsType<JObject>(segmentProperties["gauge"]);
            var values = Assert.IsType<JArray>(gauge["enum"])
                .Values<string>()
                .ToArray();

            Assert.Equal(new[]
            {
                "Standard",
                "Narrow",
                "3ft",
                "3 ft",
                "ThreeFoot",
                "Three Foot",
                "DualGauge",
                "DualGauge_L",
                "DualGauge_R",
                "DualGauge_T",
                "Dual",
                "Mixed",
                "MixedGauge"
            }, values);
        }

        [Fact]
        public void TrackStructureContract_IncludesDiamondAndFlagsEraMetadata()
        {
            var schemaPath = Path.Combine(AppContext.BaseDirectory, "fuse-mod.schema.json");
            var schema = JObject.Parse(File.ReadAllText(schemaPath));
            var definitions = Assert.IsType<JObject>(schema["$defs"]);
            var nodeProperties = Assert.IsType<JObject>(definitions["trackNode"]?["properties"]);
            var segmentProperties = Assert.IsType<JObject>(definitions["trackSegment"]?["properties"]);

            Assert.Equal("boolean", nodeProperties["isDiamond"]?["type"]?.Value<string>());
            Assert.Equal("boolean", segmentProperties["bridgeSupportsSteel"]?["type"]?.Value<string>());
            Assert.Equal("boolean", segmentProperties["yard"]?["type"]?.Value<string>());
            Assert.NotNull(segmentProperties["preserveBridgeSupportsSteel"]);
            Assert.NotNull(segmentProperties["preserveYard"]);
        }

        [Fact]
        public void UriContract_IncludesDocumentedFuseScheme()
        {
            var schemaPath = Path.Combine(
                AppContext.BaseDirectory,
                "fuse-mod.schema.json");
            var schema = JObject.Parse(File.ReadAllText(schemaPath));
            var definitions = Assert.IsType<JObject>(schema["$defs"]);
            var uri = Assert.IsType<JObject>(definitions["uri"]);
            var pattern = Assert.IsType<JValue>(uri["pattern"])
                .Value<string>();

            var regex = new Regex(pattern, RegexOptions.CultureInvariant);
            Assert.True(regex.IsMatch("fuse://example-package/object"));
            Assert.True(regex.IsMatch("path://scene/World/Example"));
            Assert.False(regex.IsMatch("https://example.invalid/fuse"));
            Assert.False(regex.IsMatch("not-fuse://example-package/object"));
        }

        [Fact]
        public void SplineyContract_IncludesRuntimeObjectLineKindsAndUriPrefab()
        {
            var schemaPath = Path.Combine(AppContext.BaseDirectory, "fuse-mod.schema.json");
            var schema = JObject.Parse(File.ReadAllText(schemaPath));
            var definitions = Assert.IsType<JObject>(schema["$defs"]);
            var properties = Assert.IsType<JObject>(definitions["spliney"]?["properties"]);
            var kinds = Assert.IsType<JArray>(properties["type"]?["enum"])
                .Values<string>()
                .ToArray();

            Assert.Equal(new[]
            {
                "river",
                "road",
                "terrainRoad",
                "trestle",
                "waterfall",
                "objectLine",
                "object-line",
                "fence",
                "retainingWall"
            }, kinds);
            Assert.Equal("#/$defs/uri", properties["prefab"]?["$ref"]?.Value<string>());
        }
    }
}
