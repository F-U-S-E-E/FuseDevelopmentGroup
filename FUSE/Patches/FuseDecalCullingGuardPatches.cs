using System;
using System.Collections;
using System.Reflection;
using Effects.Decals;
using FUSE.Infrastructure;
using HarmonyLib;

namespace FUSE.Patches
{
    /// <summary>
    /// Keeps one orphaned decal from turning the game's decal culling into a
    /// per-frame native memory leak.
    ///
    /// <c>DecalCullingManager.UpdateDecalVisibilityJob</c> allocates five
    /// <c>Allocator.TempJob</c> NativeArrays, then reads
    /// <c>entry.DecalProjector.transform</c> for every registered decal, and only
    /// Disposes the arrays at the end of the method — there is no try/finally. A
    /// projector that was destroyed while still registered (car lettering decals can
    /// be force-registered outside the enable/disable lifecycle by lettering mods,
    /// and <c>DecalProjectorHelper.OnDisable</c> skips its unregister when it throws
    /// — see <see cref="FuseDecalProjectorHelperDisableGuardPatch"/>) makes the
    /// gather loop throw a NullReferenceException BEFORE the Dispose calls, leaking
    /// all five arrays. The throw also exits <c>Update</c> before its
    /// <c>_timeSinceLastUpdate = 0</c> reset, so the normal ~0.25&#160;s cadence
    /// becomes EVERY FRAME: the registry is never pruned, so one bad entry means an
    /// NRE + five leaked TempJob arrays per frame for the rest of the session. In
    /// the field (Aspen, 2026-07-06) that was 21,947 consecutive throws over 30
    /// minutes, a saturated JobTemp allocator (67&#160;M overflow allocations), ~20&#160;GB
    /// process footprint, and ~13&#160;fps.
    ///
    /// The finalizer suppresses the exception — so <c>Update</c>'s own timer reset
    /// runs and the manager retries at its normal cadence instead of storming every
    /// frame — and flags a scrub. On the next invocation the prefix prunes
    /// destroyed/null projectors from the private <c>_decalProjectors</c> registry
    /// (a destroyed Unity object is already fake-null when the next invocation
    /// runs), so the failure heals within one update cycle: a poisoning event costs
    /// one suppressed throw and one leaked set of temp arrays (tens of KB, logged),
    /// not an unbounded per-frame storm. Scrubbing only on demand keeps the
    /// steady-state prefix cost to a single flag check instead of an O(registry)
    /// reflection walk ~4x/second.
    ///
    /// Known residual: pruning the registry entry cannot reset the owning helper's
    /// private <c>_decalRegistered</c> flag, so a still-alive helper whose projector
    /// alone was destroyed can re-register it on a later visibility cycle — each
    /// re-poisoning heals the same way, so the cost stays bounded per cycle rather
    /// than per frame.
    ///
    /// All reflection is fail-open: if the game's private layout changes (guarded by
    /// the reflection-surface canary tests), the scrub no-ops and only the
    /// storm-breaker suppression remains active.
    /// </summary>
    [HarmonyPatch(typeof(DecalCullingManager), "UpdateDecalVisibilityJob")]
    internal static class FuseDecalCullingScrubPatch
    {
        private static readonly FieldInfo DecalProjectorsField =
            AccessTools.Field(typeof(DecalCullingManager), "_decalProjectors");

        private static readonly FieldInfo EntryProjectorField = BindEntryProjectorField();

        private static bool _scrubPending;

        /// <summary>Destroyed registry entries pruned since startup (diagnostics).</summary>
        internal static long ScrubbedEntries => FuseRuntimeGuardCounters.DecalRegistryScrubbed;

        /// <summary>Exceptions suppressed since startup (diagnostics).</summary>
        internal static long SuppressedExceptions => FuseRuntimeGuardCounters.DecalVisibilitySuppressed;

        private static FieldInfo BindEntryProjectorField()
        {
            var entryType = AccessTools.Inner(typeof(DecalCullingManager), "Entry");
            return entryType != null ? AccessTools.Field(entryType, "DecalProjector") : null;
        }

        private static void Prefix(DecalCullingManager __instance)
        {
            if (!_scrubPending || __instance == null ||
                DecalProjectorsField == null || EntryProjectorField == null)
            {
                return; // nothing flagged, or layout changed (fail open).
            }

            _scrubPending = false;
            try
            {
                if (!(DecalProjectorsField.GetValue(__instance) is IList entries))
                {
                    return;
                }

                for (var i = entries.Count - 1; i >= 0; i--)
                {
                    var entry = entries[i];
                    var projector = entry != null
                        ? EntryProjectorField.GetValue(entry) as UnityEngine.Object
                        : null;
                    if (projector != null)
                    {
                        continue; // alive (fake-null covers destroyed AND never-set).
                    }

                    entries.RemoveAt(i);
                    var scrubbed = FuseRuntimeGuardCounters.RecordDecalRegistryScrubbed();
                    if (FuseGuardLog.ShouldLog(scrubbed))
                    {
                        FuseLog.Warning(
                            $"FUSE pruned destroyed decal #{scrubbed} from the decal culling registry. " +
                            "Without this the visibility job would throw every frame and leak its temp " +
                            "job memory each time (usually a car-lettering decal orphaned by a mod " +
                            "destroying the car outside the normal disable path).");
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE decal culling scrub failed; letting the vanilla job run", ex);
            }
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null)
            {
                return null;
            }

            _scrubPending = true;
            var suppressed = FuseRuntimeGuardCounters.RecordDecalVisibilitySuppressed();
            if (FuseGuardLog.ShouldLog(suppressed))
            {
                FuseLog.Warning(
                    $"FUSE suppressed decal visibility job exception #{suppressed}: " +
                    $"{__exception.GetBaseException().Message}. Suppressing lets the manager's own " +
                    "update timer reset, so it retries on its normal ~0.25s cadence instead of " +
                    "re-throwing (and leaking temp job memory) every frame; destroyed registry " +
                    "entries are pruned on the next invocation.");
            }

            return null;
        }
    }

    /// <summary>
    /// Silences the enable-time crash of <c>DecalProjectorHelper</c> (the game's
    /// car-lettering decal driver) when its prefab is enabled somewhere without a
    /// <c>Car</c> ancestor: vanilla <c>OnEnable</c> dereferences the parent car
    /// unconditionally (<c>OnCarVisibilityDidChange(_car.IsVisible)</c>). The throw
    /// already aborts the rest of the helper's setup, so suppressing it changes no
    /// behavior — it removes the exception spam (and crash-report uploads) for a
    /// state the helper ends up in either way. A finalizer rather than a
    /// pre-emptive skip so a future game version that supports car-less decals
    /// keeps working: this guard only acts on an observed throw.
    /// </summary>
    [HarmonyPatch(typeof(DecalProjectorHelper), "OnEnable")]
    internal static class FuseDecalProjectorHelperEnableGuardPatch
    {
        /// <summary>OnEnable exceptions suppressed since startup (diagnostics).</summary>
        internal static long SuppressedExceptions => FuseRuntimeGuardCounters.DecalHelperEnableSuppressed;

        private static Exception Finalizer(Exception __exception, DecalProjectorHelper __instance)
        {
            if (__exception == null)
            {
                return null;
            }

            var suppressed = FuseRuntimeGuardCounters.RecordDecalHelperEnableSuppressed();
            if (FuseGuardLog.ShouldLog(suppressed))
            {
                // Unity's overloaded null covers a destroyed instance, so .name is safe.
                var name = __instance != null ? __instance.name : "<destroyed>";
                FuseLog.Warning(
                    $"FUSE suppressed car-decal helper enable exception #{suppressed} on '{name}': " +
                    $"{__exception.GetBaseException().Message}. The helper usually has no Car ancestor " +
                    "(car decal machinery mounted on scenery); it stays inert either way.");
            }

            return null;
        }
    }

    /// <summary>
    /// Keeps a decal from staying registered in <c>DecalCullingManager</c> when
    /// <c>DecalProjectorHelper.OnDisable</c> throws. Vanilla ordering unsubscribes
    /// <c>_car.OnVisibleDidChange</c> FIRST (throws when <c>_car</c> is null — e.g. a
    /// helper force-registered by a lettering mod without ever running its own
    /// enable) and only calls <c>SetDecalRegistered(false)</c> LAST, so the throw
    /// skips the unregister and strands a destroyed projector in the culling
    /// registry — the exact poisoning that
    /// <see cref="FuseDecalCullingScrubPatch"/> then has to heal. This finalizer
    /// attempts the unregister anyway and suppresses the exception. Best-effort by
    /// design: it no-ops when the manager singleton is already gone (reading its
    /// private <c>_shared</c> field — going through the public <c>Shared</c> getter
    /// would RESURRECT the manager as a new GameObject mid scene-teardown), and it
    /// cannot unregister decals that bypassed the helper's own registered flag; the
    /// scrub patch covers whatever slips through.
    /// </summary>
    [HarmonyPatch(typeof(DecalProjectorHelper), "OnDisable")]
    internal static class FuseDecalProjectorHelperDisableGuardPatch
    {
        private static readonly MethodInfo SetDecalRegisteredMethod =
            AccessTools.Method(typeof(DecalProjectorHelper), "SetDecalRegistered");

        private static readonly FieldInfo SharedManagerField =
            AccessTools.Field(typeof(DecalCullingManager), "_shared");

        /// <summary>OnDisable exceptions suppressed since startup (diagnostics).</summary>
        internal static long SuppressedExceptions => FuseRuntimeGuardCounters.DecalHelperDisableSuppressed;

        private static Exception Finalizer(Exception __exception, DecalProjectorHelper __instance)
        {
            if (__exception == null)
            {
                return null;
            }

            try
            {
                // Only unregister while a manager actually exists: its registry dies
                // with it, and touching the public Shared getter here would lazily
                // create a replacement GameObject during scene teardown.
                var manager = SharedManagerField?.GetValue(null) as UnityEngine.Object;
                if (manager != null && __instance != null && SetDecalRegisteredMethod != null)
                {
                    // No-ops internally when the helper never registered its decal.
                    SetDecalRegisteredMethod.Invoke(__instance, new object[] { false });
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE decal helper disable guard could not unregister", ex);
            }

            var suppressed = FuseRuntimeGuardCounters.RecordDecalHelperDisableSuppressed();
            if (FuseGuardLog.ShouldLog(suppressed))
            {
                FuseLog.Warning(
                    $"FUSE suppressed car-decal helper disable exception #{suppressed}: " +
                    $"{__exception.GetBaseException().Message}. The helper's decal registration was " +
                    "released where possible; the decal culling scrub covers anything left behind.");
            }

            return null;
        }
    }

}
