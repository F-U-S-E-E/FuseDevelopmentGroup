using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public class FuseAlinasUtilitiesCompatibilityTests
    {
        [Theory]
        [InlineData("AlinasUtils", true)]
        [InlineData("AlinasUtils1", true)]
        [InlineData("alinasutils-recovered", true)]
        [InlineData("Utilities", false)]
        [InlineData("AlinasMapMod", false)]
        [InlineData(null, false)]
        public void AssemblyMatch_AcceptsRecoveredAlinasUtilitiesIdentities(
            string assemblyName,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseAlinasUtilitiesCompatibility.IsTargetAssemblyName(assemblyName));
        }

        [Fact]
        public void SettingsResolution_PreservesCurrentInstance()
        {
            var current = new object();
            var umm = new object();

            var resolved = FuseAlinasUtilitiesCompatibility.ResolveSettings(
                current,
                umm,
                () => new object());

            Assert.Same(current, resolved);
        }

        [Fact]
        public void SettingsResolution_UsesUmmSettingsBeforeDefault()
        {
            var umm = new object();
            var created = false;

            var resolved = FuseAlinasUtilitiesCompatibility.ResolveSettings(
                null,
                umm,
                () =>
                {
                    created = true;
                    return new object();
                });

            Assert.Same(umm, resolved);
            Assert.False(created);
        }

        [Fact]
        public void SettingsResolution_CreatesDefaultAsLastResort()
        {
            var fallback = new object();

            var resolved = FuseAlinasUtilitiesCompatibility.ResolveSettings(
                null,
                null,
                () => fallback);

            Assert.Same(fallback, resolved);
        }
    }
}
