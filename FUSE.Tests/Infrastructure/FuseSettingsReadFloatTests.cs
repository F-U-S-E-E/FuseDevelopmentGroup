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

    /// <summary>
    /// Clamp contract for the spike-floor slider preview (visible via
    /// InternalsVisibleTo). Preview mutates the live static value without
    /// persisting, so each test restores the default afterwards.
    /// </summary>
    public class FuseSettingsFrameSpikeThresholdTests
    {
        [Theory]
        [InlineData(5f, FuseSettings.MinFrameSpikeThresholdMs)]     // below floor clamps up
        [InlineData(50f, 50f)]                                       // in-range passes through
        [InlineData(9999f, FuseSettings.MaxFrameSpikeThresholdMs)]   // above ceiling clamps down
        [InlineData(float.NaN, FuseSettings.DefaultFrameSpikeThresholdMs)]           // NaN degrades to default
        [InlineData(float.PositiveInfinity, FuseSettings.MaxFrameSpikeThresholdMs)]  // +Inf clamps down
        [InlineData(float.NegativeInfinity, FuseSettings.MinFrameSpikeThresholdMs)]  // -Inf clamps up
        public void PreviewFrameSpikeThresholdMs_ClampsToDocumentedRange(float input, float expected)
        {
            var original = FuseSettings.FrameSpikeThresholdMs;
            try
            {
                FuseSettings.PreviewFrameSpikeThresholdMs(input);
                Assert.Equal(expected, FuseSettings.FrameSpikeThresholdMs);
            }
            finally
            {
                FuseSettings.PreviewFrameSpikeThresholdMs(original);
            }
        }
    }

}
