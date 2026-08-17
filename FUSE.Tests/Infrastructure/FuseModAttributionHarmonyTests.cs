using System;
using System.Collections.Generic;
using System.Reflection;
using FUSE.Infrastructure;
using HarmonyLib;
using Xunit;

namespace FUSE.Tests.Infrastructure
{
    /// <summary>
    /// Integration coverage for the Harmony half of dynamic-method wrapper
    /// attribution: a REAL Harmony patch applied inside the test process,
    /// resolved through the same <c>TryAttributeStack</c> entry the log-hook
    /// drain uses. The pure wrapper parsing and merge rules live in
    /// FuseModAttributionMapTests; this class proves the production resolver
    /// walks Harmony's live patch state and maps the patch assembly through
    /// the published assembly map. Joins the registry test collection because
    /// it publishes/reset the attribution map's static state.
    /// </summary>
    [Collection(FuseModExceptionRegistryTestCollection.Name)]
    public class FuseModAttributionHarmonyTests
    {
        // A method name unlikely to collide with anything else Harmony has
        // patched in this test process.
        internal static int WrapperAttributionProbeTarget(int value) => value + 1;

        internal static bool ProbePrefix() => true;

        // The wrapper frame Mono would print for a throw inside the probe
        // target's rewritten body — shared by every test in this class.
        // Deliberately a fixture (captured verbatim from a field log), NOT a
        // stack recorded from throwing through the patched probe: this suite
        // runs on the .NET Framework CLR, whose dynamic-method frames never
        // use Mono's "(wrapper dynamic-method) MonoMod..." shape, so a live
        // throw here cannot produce the production format these tests exist
        // to cover.
        private const string ProbeWrapperTrace =
            "at (wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition" +
            ".FuseModAttributionHarmonyTests.WrapperAttributionProbeTarget_Patch1(int)";

        [Fact]
        public void WrapperFrame_ResolvesThroughLiveHarmonyPatchState_ToTheOwningMod()
        {
            var harmony = new Harmony("fuse.tests.wrapper-attribution");
            var target = typeof(FuseModAttributionHarmonyTests).GetMethod(
                nameof(WrapperAttributionProbeTarget), BindingFlags.Static | BindingFlags.NonPublic);
            var prefix = typeof(FuseModAttributionHarmonyTests).GetMethod(
                nameof(ProbePrefix), BindingFlags.Static | BindingFlags.NonPublic);

            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));

                // Publish this test assembly as a known mod so the resolver's
                // assembly-map lookup lands on it.
                FuseModAttributionMap.SetMapsForTests(
                    tokenMap: new Dictionary<string, (string modId, string displayName)>(
                        StringComparer.OrdinalIgnoreCase),
                    assemblyMap: new Dictionary<Assembly, (string modId, string displayName)>
                    {
                        [typeof(FuseModAttributionHarmonyTests).Assembly] = ("test.mod", "Test Mod")
                    });

                var trace = ProbeWrapperTrace;

                Assert.True(FuseModAttributionMap.TryAttributeStack(
                    trace, out var modId, out var displayName, out var frame));
                Assert.Equal("test.mod", modId);
                Assert.Equal("Test Mod", displayName);
                Assert.Equal(
                    "FuseModAttributionHarmonyTests.WrapperAttributionProbeTarget [via Harmony patch]",
                    frame);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                FuseModAttributionMap.ResetForTests();
            }
        }

        [Fact]
        public void WrapperFrame_WithAnUnmappedCoPatch_StaysUnattributed()
        {
            // The wrong-blame guard: the probe method carries one patch from
            // this (mapped) assembly and one from the FUSE assembly, which is
            // never in the map — exactly the shape of a game method co-patched
            // by FUSE and one third-party mod. The unknown owner must poison
            // the method: blaming the mapped mod would pin FUSE's own faults
            // (or the game's) on an innocent bystander.
            var harmony = new Harmony("fuse.tests.wrapper-attribution-copatch");
            var target = typeof(FuseModAttributionHarmonyTests).GetMethod(
                nameof(WrapperAttributionProbeTarget), BindingFlags.Static | BindingFlags.NonPublic);
            var prefix = typeof(FuseModAttributionHarmonyTests).GetMethod(
                nameof(ProbePrefix), BindingFlags.Static | BindingFlags.NonPublic);

            // A static void no-arg method from the FUSE assembly works as a
            // Harmony prefix; the target is never invoked, so it never runs.
            var fuseOwnedPrefix = typeof(FuseModAttributionMap).GetMethod(
                nameof(FuseModAttributionMap.Invalidate), BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(fuseOwnedPrefix);

            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                harmony.Patch(target, prefix: new HarmonyMethod(fuseOwnedPrefix));

                FuseModAttributionMap.SetMapsForTests(
                    tokenMap: new Dictionary<string, (string modId, string displayName)>(
                        StringComparer.OrdinalIgnoreCase),
                    assemblyMap: new Dictionary<Assembly, (string modId, string displayName)>
                    {
                        [typeof(FuseModAttributionHarmonyTests).Assembly] = ("test.mod", "Test Mod")
                    });

                var trace = ProbeWrapperTrace;

                Assert.False(FuseModAttributionMap.TryAttributeStack(
                    trace, out var modId, out _, out _));
                Assert.Null(modId);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                FuseModAttributionMap.ResetForTests();
            }
        }

        [Fact]
        public void WrapperFrame_WithAnEmptyAssemblyMap_StaysUnattributed()
        {
            var harmony = new Harmony("fuse.tests.wrapper-attribution-unmapped");
            var target = typeof(FuseModAttributionHarmonyTests).GetMethod(
                nameof(WrapperAttributionProbeTarget), BindingFlags.Static | BindingFlags.NonPublic);
            var prefix = typeof(FuseModAttributionHarmonyTests).GetMethod(
                nameof(ProbePrefix), BindingFlags.Static | BindingFlags.NonPublic);

            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));

                // No assembly map entries: the patch exists but belongs to no
                // known mod (FUSE's own patches take this path in production).
                FuseModAttributionMap.SetMapsForTests(
                    tokenMap: new Dictionary<string, (string modId, string displayName)>(
                        StringComparer.OrdinalIgnoreCase),
                    assemblyMap: new Dictionary<Assembly, (string modId, string displayName)>());

                var trace = ProbeWrapperTrace;

                Assert.False(FuseModAttributionMap.TryAttributeStack(
                    trace, out var modId, out _, out _));
                Assert.Null(modId);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                FuseModAttributionMap.ResetForTests();
            }
        }
    }
}
