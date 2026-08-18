using System;
using System.Globalization;
using System.Threading;
using FUSE.Loading;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Loading
{
    /// <summary>
    /// Regression coverage for issue #219: the runtime legacy converter round-tripped
    /// numeric tokens through <c>JValue.ToString()</c>, which formats with the current
    /// culture. Under a comma-decimal locale every fractional coordinate failed the
    /// invariant parse and collapsed to 0, so whole map mods folded onto the world
    /// origin (zero-length segments, non-intersecting switches, scenery at the group
    /// root).
    /// </summary>
    public sealed class FuseLegacyConverterCultureTests
    {
        [Theory]
        [InlineData("pt-BR")]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        [InlineData("en-US")]
        public void FractionalCoordinates_SurviveConversion_UnderCommaDecimalCultures(string cultureName)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            var previousCulture = Thread.CurrentThread.CurrentCulture;
            var previousUiCulture = Thread.CurrentThread.CurrentUICulture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            try
            {
                // Sanity check that this culture would actually have exercised the bug:
                // the double must format with a comma for the non-invariant cases.
                if (culture.NumberFormat.NumberDecimalSeparator == ",")
                {
                    Assert.Contains(",", new JValue(12278.53).ToString());
                }

                var source = new JObject
                {
                    ["tracks"] = new JObject
                    {
                        ["nodes"] = new JObject
                        {
                            ["N_AWHI_1"] = new JObject
                            {
                                ["position"] = new JObject { ["x"] = 12278.53, ["y"] = 560.25, ["z"] = 5770.75 },
                                ["rotation"] = new JObject { ["x"] = 0, ["y"] = 152.5, ["z"] = 0 }
                            }
                        }
                    },
                    ["scenery"] = new JObject
                    {
                        ["awhi-sign"] = new JObject
                        {
                            ["modelIdentifier"] = "sign",
                            ["position"] = new JObject { ["x"] = 12299.28, ["y"] = 557.17, ["z"] = 5846.18 },
                            ["rotation"] = new JObject { ["x"] = 0, ["y"] = 90.5, ["z"] = 0 },
                            ["scale"] = new JObject { ["x"] = 1.5, ["y"] = 1.5, ["z"] = 1.5 }
                        }
                    }
                };
                var manifest = new FuseLegacyPackageManifest
                {
                    PackageId = "culture-pkg",
                    DisplayName = "Culture Package",
                    Author = "tester",
                    Version = "1.0.0"
                };
                var root = FuseLegacyDataConverter.CreateSkeleton(manifest, "culture-fragment");

                FuseLegacyDataConverter.ConvertSource(source, root, manifest);

                var node = (JObject)root["tracks"]["nodes"]["N_AWHI_1"];
                Assert.Equal(12278.53f, (float)node["position"]["x"], 2);
                Assert.Equal(560.25f, (float)node["position"]["y"], 2);
                Assert.Equal(5770.75f, (float)node["position"]["z"], 2);
                Assert.Equal(152.5f, (float)node["rotation"]["y"], 2);

                var scenery = (JObject)root["world"]["scenery"]["awhi-sign"];
                Assert.Equal(12299.28f, (float)scenery["position"]["x"], 2);
                Assert.Equal(557.17f, (float)scenery["position"]["y"], 2);
                Assert.Equal(5846.18f, (float)scenery["position"]["z"], 2);
                Assert.Equal(90.5f, (float)scenery["rotation"]["y"], 2);
                Assert.Equal(1.5f, (float)scenery["scale"]["x"], 2);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previousCulture;
                Thread.CurrentThread.CurrentUICulture = previousUiCulture;
            }
        }
    }
}
