using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseMemoryLeakFpsCompatibilityTests
    {
        [Theory]
        [InlineData(
            "MemoryLeakFPSfix",
            "MemoryLeakFPSfix.Patches.EnviroUpdateOnPositionPatch",
            "Postfix",
            true)]
        [InlineData(
            "Other",
            "MemoryLeakFPSfix.Patches.EnviroUpdateOnPositionPatch",
            "Postfix",
            false)]
        [InlineData(
            "MemoryLeakFPSfix",
            "MemoryLeakFPSfix.Patches.Other",
            "Postfix",
            false)]
        [InlineData(
            "MemoryLeakFPSfix",
            "MemoryLeakFPSfix.Patches.EnviroUpdateOnPositionPatch",
            "Prefix",
            false)]
        public void LegacyStartPostfixDetection_IsExact(
            string assemblyName,
            string declaringTypeName,
            string methodName,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseMemoryLeakFpsCompatibility.IsLegacyStartPostfix(
                    assemblyName,
                    declaringTypeName,
                    methodName));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ReflectionRefresh_UsesCurrentTwoArgumentApi(bool forced)
        {
            Assert.Equal(
                new object[] { forced, false },
                FuseMemoryLeakFpsCompatibility.CurrentRenderArguments(forced));
        }
    }
}
