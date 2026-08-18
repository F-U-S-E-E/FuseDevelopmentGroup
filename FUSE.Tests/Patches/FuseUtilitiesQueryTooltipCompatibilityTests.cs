using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseUtilitiesQueryTooltipCompatibilityTests
    {
        [Theory]
        [InlineData("Utilities", "Utilities.QueryToolDistancePatch", "Prefix", true)]
        [InlineData("OtherMod", "Utilities.QueryToolDistancePatch", "Prefix", false)]
        [InlineData("Utilities", "Utilities.OtherPatch", "Prefix", false)]
        [InlineData("Utilities", "Utilities.QueryToolDistancePatch", "Postfix", false)]
        public void LegacyPrefixDetection_IsExact(
            string assemblyName,
            string declaringTypeName,
            string methodName,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseUtilitiesQueryTooltipCompatibility.IsLegacyUtilitiesQueryPrefix(
                    assemblyName,
                    declaringTypeName,
                    methodName));
        }

        [Theory]
        [InlineData(null, 1500f)]
        [InlineData(-1f, 1500f)]
        [InlineData(0f, 1500f)]
        [InlineData(2500f, 2500f)]
        [InlineData("750.5", 750.5f)]
        public void QueryDistance_UsesValidSettingOrSafeDefault(object value, float expected)
        {
            Assert.Equal(
                expected,
                FuseUtilitiesQueryTooltipCompatibility.NormalizeQueryDistance(value));
        }
    }
}
