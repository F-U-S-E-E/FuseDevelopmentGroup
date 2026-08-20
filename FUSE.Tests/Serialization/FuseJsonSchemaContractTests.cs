using System;
using System.IO;
using System.Linq;
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

            Assert.Contains("Standard", values);
            Assert.Contains("Narrow", values);
            Assert.Contains("DualGauge", values);
            Assert.Contains("DualGauge_L", values);
            Assert.Contains("DualGauge_R", values);
            Assert.Contains("DualGauge_T", values);
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

            Assert.Contains("fuse", pattern, StringComparison.Ordinal);
        }
    }
}
