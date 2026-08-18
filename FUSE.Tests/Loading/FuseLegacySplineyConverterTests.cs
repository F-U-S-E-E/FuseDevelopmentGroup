using FUSE.Authoring.Serialization;
using FUSE.Loading;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Loading
{
    public sealed class FuseLegacySplineyConverterTests
    {
        [Fact]
        public void PointsReplace_RoundTripsAsSplineyGeometry()
        {
            var source = new JObject
            {
                ["splineys"] = new JObject
                {
                    ["World/Roads Sylva/Chipper Curve"] = new JObject
                    {
                        ["profile"] = "Railroader Paved Road",
                        ["style"] = "Road",
                        ["points"] = new JObject
                        {
                            ["$replace"] = new JArray(
                                Point(25052.1875f, 623.3373f, -911.4685f),
                                Point(25047.8848f, 623.9138f, -903.40564f))
                        }
                    }
                }
            };
            var manifest = new FuseLegacyPackageManifest
            {
                PackageId = "test-pkg",
                DisplayName = "Test Package",
                Author = "tester",
                Version = "1.0.0"
            };
            var root = FuseLegacyDataConverter.CreateSkeleton(manifest, "spliney-fragment");

            FuseLegacyDataConverter.ConvertSource(source, root, manifest);

            var converted = (JObject)root["world"]["splineys"]["World/Roads Sylva/Chipper Curve"];
            Assert.Equal(2, ((JArray)converted["points"]).Count);
            var definition = FuseSerializer.FromJson(root.ToString());
            Assert.Equal(
                2,
                definition.World.Splineys["World/Roads Sylva/Chipper Curve"].Points.Length);
        }

        private static JObject Point(float x, float y, float z)
        {
            return new JObject
            {
                ["position"] = new JObject
                {
                    ["x"] = x,
                    ["y"] = y,
                    ["z"] = z
                },
                ["rotation"] = new JObject
                {
                    ["x"] = 0f,
                    ["y"] = 0f,
                    ["z"] = 0f
                },
                ["width"] = 6.7056f
            };
        }
    }
}
