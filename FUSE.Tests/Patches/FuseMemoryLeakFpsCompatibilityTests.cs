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

        [Fact]
        public void ReflectionRefresh_UsesCurrentTwoArgumentApi()
        {
            Assert.Equal(
                new object[] { true, false },
                FuseMemoryLeakFpsCompatibility.CurrentRenderArguments(true));
        }
    }
}
