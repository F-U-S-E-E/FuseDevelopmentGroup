using FUSE.Loading;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Loading
{
    public class FuseLegacyTextConverterTests
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
            var root = FuseLegacyDataConverter.CreateSkeleton(manifest, "text-fragment");
            FuseLegacyDataConverter.ConvertSource(source, root, manifest);
            return root;
        }

        [Fact]
        public void StringTextReplacement_BecomesMapLabelTextUpdate()
        {
            var root = Convert(new JObject
            {
                ["texts"] = new JObject
                {
                    ["Alarka Jct"] = "DeHart"
                }
            });

            var label = (JObject)root["world"]["mapLabels"]["Alarka Jct"];
            Assert.NotNull(label);
            Assert.Equal("DeHart", (string)label["text"]);
        }

        [Fact]
        public void NullTextReplacement_BecomesMapLabelRemoval()
        {
            var root = Convert(new JObject
            {
                ["texts"] = new JObject
                {
                    ["Old Label"] = JValue.CreateNull()
                }
            });

            var removals = (JArray)root["world"]["removals"]["mapLabels"];
            Assert.Contains("Old Label", removals.Values<string>());
        }
    }
}
