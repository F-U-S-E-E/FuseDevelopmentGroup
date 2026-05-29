using FUSE.Authoring.Serialization;
using FUSE.Loading;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Loading
{
    public class FuseLegacyProgressionConverterTests
    {
        private static JObject Convert(JObject source)
        {
            var manifest = new FuseLegacyPackageManifest
            {
                PackageId = "test-pkg",
                DisplayName = "Test Package",
                Author = "tester",
                Version = "1.0.0"
            };
            var root = FuseLegacyDataConverter.CreateSkeleton(manifest, "progression-fragment");
            FuseLegacyDataConverter.ConvertSource(source, root, manifest);
            return root;
        }

        [Fact]
        public void EmptyDeliveryPhase_BecomesFreePhaseInsteadOfBeingRemoved()
        {
            var root = Convert(new JObject
            {
                ["progressions"] = new JObject
                {
                    ["ewh"] = new JObject
                    {
                        ["sections"] = new JObject
                        {
                            ["AP-AR-W-SAWMILL"] = new JObject
                            {
                                ["displayName"] = "Whittier Sawmill",
                                ["deliveryPhases"] = new JArray(new JObject()),
                                ["enableFeaturesOnUnlock"] = new JObject
                                {
                                    ["AR-Whittier-Sawmill"] = true
                                }
                            }
                        }
                    }
                }
            });

            var phase = (JObject)root["progression"]["progressions"]["ewh"]["sections"]["AP-AR-W-SAWMILL"]["deliveryPhases"][0];
            Assert.Equal(0, (int)phase["cost"]);

            var definition = FuseSerializer.FromJson(root.ToString());
            var section = definition.Progression.Progressions["ewh"].Sections["AP-AR-W-SAWMILL"];
            Assert.Single(section.DeliveryPhases);
            Assert.Equal(0, section.DeliveryPhases[0].Cost);
        }
    }
}
