using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseUtilitiesMapLoadCompatibilityTests
    {
        [Theory]
        [InlineData("Utilities", "Utilities.UtilitiesMod", "OnMapDidLoad", true)]
        [InlineData("Other", "Utilities.UtilitiesMod", "OnMapDidLoad", false)]
        [InlineData("Utilities", "Utilities.Other", "OnMapDidLoad", false)]
        [InlineData("Utilities", "Utilities.UtilitiesMod", "OnMapWillUnload", false)]
        public void LegacyMapLoadDetection_IsExact(
            string assemblyName,
            string declaringTypeName,
            string methodName,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseUtilitiesMapLoadCompatibility.IsLegacyUtilitiesMapLoadHandler(
                    assemblyName,
                    declaringTypeName,
                    methodName));
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(2, true)]
        public void MapSettings_ApplyOnlyWhenNotAlreadyLoaded(int state, bool expected)
        {
            Assert.Equal(
                expected,
                FuseUtilitiesMapLoadCompatibility.ShouldApplyMapSettings(state));
        }

        [Theory]
        [InlineData(null, 0.3f)]
        [InlineData(-1f, 0.3f)]
        [InlineData(0f, 0.3f)]
        [InlineData(2.5f, 2.5f)]
        [InlineData("4.25", 4.25f)]
        [InlineData("NaN", 0.3f)]
        [InlineData("Infinity", 0.3f)]
        [InlineData("not-a-number", 0.3f)]
        public void Radius_UsesPositiveFiniteSettingOrFallback(
            object value,
            float expected)
        {
            Assert.Equal(
                expected,
                FuseUtilitiesMapLoadCompatibility.NormalizeRadius(value, 0.3f));
        }
    }
}
