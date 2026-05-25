using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FUSE.Patches;
using HarmonyLib;
using Model.Definition;
using Model.Definition.Data;
using NUnit.Framework;

namespace FUSE.UnityTests
{
    /// <summary>
    /// EditMode end-to-end Harmony tests. Tier A
    /// (FusePatchTargetingTests in FUSE.Tests/) verifies each
    /// patch's target RESOLVES against the game DLLs; this suite
    /// goes further and confirms Harmony's full installation
    /// pipeline succeeds — every prefix/postfix signature must
    /// match its target's parameters, no patch class can crash
    /// during apply, and the patches actually end up in
    /// <see cref="Harmony.GetAllPatchedMethods"/>.
    ///
    /// We also drive one fully-end-to-end scenario through the
    /// hardest-hit patch (<c>FuseAggregateLoadModelMaterialFieldPatch</c>):
    /// install via Harmony, invoke the now-patched game method
    /// against malformed input, assert our prefix substituted a
    /// clean fallback instead of letting the stock implementation
    /// throw. That single test is the proof that the patch
    /// pipeline works as a unit — Tier A says "target resolves",
    /// Tier B says "body logic is correct", this says "the chain
    /// actually fires against the live game type".
    ///
    /// All tests install patches under a distinct Harmony id and
    /// unpatch in [TearDown] so the test process state stays
    /// clean between runs.
    /// </summary>
    public class HarmonyPatchInstallationTests
    {
        private const string TestHarmonyId = "fuse.unitytests.harmony";

        private Harmony _harmony;

        [SetUp]
        public void SetUp()
        {
            _harmony = new Harmony(TestHarmonyId);
        }

        [TearDown]
        public void TearDown()
        {
            // UnpatchAll with the id leaves other Harmony users
            // (the production FUSE patches running inside Railroader
            // itself, were this test somehow running in-game) alone.
            _harmony?.UnpatchAll(TestHarmonyId);
            _harmony = null;
        }

        [Test]
        public void PatchAll_AcrossFusePatchesAssembly_InstallsWithoutExceptions()
        {
            // The headline smoke test: every FUSE patch class
            // (~20 today) must install cleanly via PatchAll. This
            // catches the signature-mismatch failure mode Tier A
            // cannot reach — e.g. a prefix that declared
            // `ref string value` when the patched method takes
            // `out string value` would compile fine but blow up
            // at Harmony installation time with an obscure
            // ArgumentException, only visible in the live game log.
            var fuseAssembly = typeof(FuseAssetPackPatchHelpers).Assembly;

            Assert.DoesNotThrow(() => _harmony.PatchAll(fuseAssembly),
                "Harmony.PatchAll must install every FUSE patch without exceptions. " +
                "A failure here typically means a prefix/postfix signature does not " +
                "match its target method, or the target itself could not be resolved.");

            var patched = Harmony.GetAllPatchedMethods().ToList();
            Assert.That(patched.Count, Is.GreaterThan(0),
                "After PatchAll, Harmony's global patched-method index must be non-empty.");
        }

        [Test]
        public void PatchAll_RegistersTryGetFieldInterceptor()
        {
            // After PatchAll, the specific
            // FuseAggregateLoadModelMaterialFieldPatch must show up
            // as a prefix on AggregateLoadModelController.TryGetField.
            // If it doesn't, the patch silently detached.
            _harmony.PatchAll(typeof(FuseAssetPackPatchHelpers).Assembly);

            var targetType = AccessTools.TypeByName("RollingStock.LoadModels.AggregateLoadModelController");
            Assert.NotNull(targetType,
                "Cannot find AggregateLoadModelController — the test's game-DLL setup is broken.");
            var target = AccessTools.Method(targetType, "TryGetField");
            Assert.NotNull(target,
                "Cannot find AggregateLoadModelController.TryGetField — the game DLL renamed the method.");

            var info = Harmony.GetPatchInfo(target);
            Assert.NotNull(info,
                "Harmony has no patch info for TryGetField — the patch was not installed against this target.");
            Assert.That(info.Prefixes.Count, Is.GreaterThan(0),
                "No prefixes registered on TryGetField — FuseAggregateLoadModelMaterialFieldPatch failed to attach.");

            var fuseAttached = info.Prefixes.Any(p => p.owner == TestHarmonyId);
            Assert.True(fuseAttached,
                $"None of the prefixes on TryGetField are owned by our test Harmony id '{TestHarmonyId}'. " +
                $"Registered prefix owners: [{string.Join(", ", info.Prefixes.Select(p => p.owner))}].");
        }

        [Test]
        public void TryGetField_PatchedBehavior_SubstitutesFallbackOnMalformedInput()
        {
            // End-to-end: install our patch via Harmony, then drive
            // the LIVE game method (via reflection) with a malformed
            // MaterialDefinition and assert our prefix supplied a
            // clean "no match" result instead of letting the stock
            // implementation throw.
            //
            // The malformed shape (Fields == null) is the one that
            // first surfaced the ArrayTypeMismatchException
            // regression in production logs.
            _harmony.PatchAll(typeof(FuseAssetPackPatchHelpers).Assembly);

            var targetType = AccessTools.TypeByName("RollingStock.LoadModels.AggregateLoadModelController");
            var target = AccessTools.Method(targetType, "TryGetField");
            Assert.NotNull(target);

            var definition = new MaterialDefinition
            {
                AssetIdentifier = "test/end-to-end-null-fields",
                Fields = null
            };

            // TryGetField is static (per the patch's Prefix signature).
            // The stock signature is roughly:
            //   bool TryGetField(MaterialDefinition definition, string key, out string value)
            object[] args = { definition, "any-key", null };

            bool result;
            try
            {
                result = (bool)target.Invoke(null, args);
            }
            catch (TargetInvocationException ex)
            {
                // If we land here, the patch was either not installed
                // or our prefix did NOT return false to skip the
                // original. Either way, the regression is back.
                throw new AssertionException(
                    $"Patched TryGetField threw {ex.InnerException?.GetType().FullName} — " +
                    $"our prefix did not intercept the malformed-input path. " +
                    $"Underlying: {ex.InnerException?.Message}", ex);
            }

            Assert.False(result,
                "Patched TryGetField on a null-Fields definition must return false (our prefix's fallback).");
            Assert.IsNull(args[2],
                "Patched TryGetField on a null-Fields definition must produce a null out value.");
        }

        [Test]
        public void Unpatch_RemovesAllFusePatches()
        {
            // After UnpatchAll, Harmony's global index must no longer
            // attribute any of our test-id patches to the previously
            // patched methods. This is a state-hygiene check —
            // a leaked patch from a previous test run would mutate
            // game-method behaviour for the next test in unexpected
            // ways.
            _harmony.PatchAll(typeof(FuseAssetPackPatchHelpers).Assembly);
            var patchedBefore = Harmony.GetAllPatchedMethods().ToList();
            Assert.That(patchedBefore.Count, Is.GreaterThan(0));

            _harmony.UnpatchAll(TestHarmonyId);

            var leakedFusePatches = new List<string>();
            foreach (var method in patchedBefore)
            {
                var info = Harmony.GetPatchInfo(method);
                if (info == null) continue;
                var owners = info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers)
                                          .Select(p => p.owner);
                if (owners.Any(o => o == TestHarmonyId))
                {
                    leakedFusePatches.Add($"{method.DeclaringType?.FullName}.{method.Name}");
                }
            }

            Assert.IsEmpty(leakedFusePatches,
                $"UnpatchAll('{TestHarmonyId}') left patches attached: [{string.Join(", ", leakedFusePatches)}].");
        }
    }
}
