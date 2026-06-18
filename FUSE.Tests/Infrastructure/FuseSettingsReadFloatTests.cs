using FUSE.Infrastructure;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Infrastructure
{
    /// <summary>
    /// Tests for the float reader behind the Info.json / user-override
    /// settings (visible via InternalsVisibleTo). Mirrors the contract
    /// of the existing bool reader: missing or malformed input degrades
    /// to the supplied default instead of throwing, and string values
    /// parse with the invariant culture so a comma-decimal OS locale
    /// can't flip "0.75" into 75.
    /// </summary>
    public class FuseSettingsReadFloatTests
    {
        [Fact]
        public void ReadFloat_NullSettings_ReturnsDefault()
        {
            Assert.Equal(0.6f, FuseSettings.ReadFloat(null, "Key", 0.6f));
        }

        [Fact]
        public void ReadFloat_MissingKey_ReturnsDefault()
        {
            var settings = JObject.Parse("{}");
            Assert.Equal(0.6f, FuseSettings.ReadFloat(settings, "Key", 0.6f));
        }

        [Fact]
        public void ReadFloat_FloatToken_ReturnsValue()
        {
            var settings = JObject.Parse("{\"Key\": 0.75}");
            Assert.Equal(0.75f, FuseSettings.ReadFloat(settings, "Key", 0.6f));
        }

        [Fact]
        public void ReadFloat_IntegerToken_ReturnsValue()
        {
            var settings = JObject.Parse("{\"Key\": 1}");
            Assert.Equal(1f, FuseSettings.ReadFloat(settings, "Key", 0.6f));
        }

        [Fact]
        public void ReadFloat_NumericString_ParsesInvariant()
        {
            var settings = JObject.Parse("{\"Key\": \"0.25\"}");
            Assert.Equal(0.25f, FuseSettings.ReadFloat(settings, "Key", 0.6f));
        }

        [Fact]
        public void ReadFloat_NonNumericString_ReturnsDefault()
        {
            var settings = JObject.Parse("{\"Key\": \"weathered\"}");
            Assert.Equal(0.6f, FuseSettings.ReadFloat(settings, "Key", 0.6f));
        }

        [Fact]
        public void ReadFloat_NullKey_ReturnsDefault()
        {
            var settings = JObject.Parse("{\"Key\": 0.75}");
            Assert.Equal(0.6f, FuseSettings.ReadFloat(settings, null, 0.6f));
        }
    }
}
