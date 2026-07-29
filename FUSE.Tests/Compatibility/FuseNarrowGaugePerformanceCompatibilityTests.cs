using System.Collections.Generic;
using System.Reflection.Emit;
using FUSE.Compatibility;
using HarmonyLib;
using Xunit;

namespace FUSE.Tests.Compatibility
{
    public sealed class FuseNarrowGaugePerformanceCompatibilityTests
    {
        [Fact]
        public void RewriteSampleSpacing_ReplacesOnlyExpectedFloatConstants()
        {
            var instructions = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldc_R4, 0.2f),
                new CodeInstruction(OpCodes.Ldc_R4, 0.35f),
                new CodeInstruction(OpCodes.Ldc_I4_2),
                new CodeInstruction(OpCodes.Ldc_R4, 0.2f)
            };

            var replacements =
                FuseNarrowGaugePerformanceCompatibility.RewriteSampleSpacing(instructions);

            Assert.Equal(
                FuseNarrowGaugePerformanceCompatibility.ExpectedConstantReplacements,
                replacements);
            Assert.Equal(
                FuseNarrowGaugePerformanceCompatibility.OptimizedSampleSpacingMeters,
                (float)instructions[0].operand);
            Assert.Equal(0.35f, (float)instructions[1].operand);
            Assert.Equal(
                FuseNarrowGaugePerformanceCompatibility.OptimizedSampleSpacingMeters,
                (float)instructions[3].operand);
        }

        [Fact]
        public void RewriteSampleSpacing_ReturnsZeroForUnknownMethodShape()
        {
            var instructions = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldc_R8, 0.2d),
                new CodeInstruction(OpCodes.Ldc_R4, 0.25f)
            };

            Assert.Equal(
                0,
                FuseNarrowGaugePerformanceCompatibility.RewriteSampleSpacing(instructions));
        }

        [Theory]
        [InlineData("Gauge graph scan timing: totalMs=100", true)]
        [InlineData("Special-work analysis: objects=13", true)]
        [InlineData("Gauge graph validation passed.", true)]
        [InlineData("Loaded as a FUSE companion module.", true)]
        [InlineData("[FrogAccepted] verbose geometry trace", false)]
        [InlineData("Special-work plan 'switch-1': valid=True", false)]
        [InlineData("Gauge graph: narrow=19, dual=28", false)]
        public void ShouldForwardNarrowGaugeInfo_FiltersVerboseGeometryOnly(
            string message,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseNarrowGaugePerformanceCompatibility.ShouldForwardNarrowGaugeInfo(message));
        }

    }
}
