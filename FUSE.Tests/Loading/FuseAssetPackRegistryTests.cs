using FUSE.Loading;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Loading
{
    public sealed class FuseAssetPackRegistryTests
    {
        [Fact]
        public void AssetDefinitionsEqual_IgnoresObjectPropertyOrder()
        {
            var first = JObject.Parse(@"{
  ""name"": ""coal-bunker"",
  ""definition"": { ""mass"": 12, ""enabled"": true }
}");
            var reordered = JObject.Parse(@"{
  ""definition"": { ""enabled"": true, ""mass"": 12 },
  ""name"": ""coal-bunker""
}");

            Assert.True(FuseAssetPackRegistry.AssetDefinitionsEqual(first, reordered));
        }

        [Fact]
        public void AssetDefinitionsEqual_DetectsAChangedValue()
        {
            var first = JObject.Parse(@"{ ""name"": ""coal-bunker"", ""mass"": 12 }");
            var changed = JObject.Parse(@"{ ""mass"": 13, ""name"": ""coal-bunker"" }");

            Assert.False(FuseAssetPackRegistry.AssetDefinitionsEqual(first, changed));
        }
    }
}
