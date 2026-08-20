using FUSE.Converter.Conversion;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Converter
{
    public sealed class LegacyTrackStructureConverterTests
    {
        [Fact]
        public void ConvertNode_PreservesDiamondCrossingMarker()
        {
            var converted = LegacyTrackConverter.ConvertNode(JObject.Parse(
                "{ 'position': [1,2,3], 'rotation': [0,90,0], 'isDiamond': true }"));

            Assert.True(converted.Value<bool>("isDiamond"));
        }

        [Theory]
        [InlineData(0, "standard", false, false)]
        [InlineData(2, "bridge", false, false)]
        [InlineData(6, "bridge", true, false)]
        [InlineData(8, "tunnel", false, false)]
        [InlineData(18, "bridge", false, true)]
        public void ConvertSegment_DecodesFlagsBasedTrackStructure(
            int flags,
            string expectedStyle,
            bool expectedSteel,
            bool expectedYard)
        {
            var legacy = new JObject
            {
                ["a"] = "node-a",
                ["b"] = "node-b",
                ["flags"] = flags
            };

            var converted = LegacyTrackConverter.ConvertSegment(legacy);

            Assert.Equal(expectedStyle, converted.Value<string>("style"));
            Assert.Equal(expectedSteel, converted.Value<bool>("bridgeSupportsSteel"));
            Assert.Equal(expectedYard, converted.Value<bool>("yard"));
        }

        [Fact]
        public void ConvertPartialSegment_PreservesUnspecifiedStructureFields()
        {
            var converted = LegacyTrackConverter.ConvertSegment(JObject.Parse("{ 'a': 'node-a' }"));

            Assert.True(converted.Value<bool>("partial"));
            Assert.True(converted.Value<bool>("preserveStyle"));
            Assert.True(converted.Value<bool>("preserveBridgeSupportsSteel"));
            Assert.True(converted.Value<bool>("preserveYard"));
        }
    }
}
