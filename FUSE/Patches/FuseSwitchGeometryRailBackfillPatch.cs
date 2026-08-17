using System;
using Core;
using FUSE.Infrastructure;
using HarmonyLib;
using Track;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Backfills null rail curves on <see cref="SwitchGeometry.Calculate"/>
    /// results before they are stored in the shared switch-descriptor
    /// dictionary.
    ///
    /// The vanilla calculator either populates every rail
    /// <see cref="LineCurve"/> or throws (and throwing switches are dropped
    /// by the caller), so a vanilla descriptor can never carry a null rail.
    /// Modded geometry producers can: the narrow-gauge control-shell path
    /// substitutes a result that carries stand/frog data but leaves all
    /// eight rail curves null. The producer's own mesh building suppresses
    /// those hidden descriptors, so the game never notices — but the
    /// descriptor dictionary is shared surface, and any third-party consumer
    /// that walks it (Map Enhancer's junction markers are the known field
    /// case) dereferences <c>geometry.aPointRail</c> and dies. In the field
    /// that single throw aborted Map Enhancer's whole Rebuild before its
    /// culling-sphere array was allocated, which then turned EVERY
    /// world-origin shift into a further NRE: one poisoned descriptor became
    /// a session-long exception stream, dead map markers, and a killed
    /// game-side track-rebuild coroutine.
    ///
    /// Priority.Last puts this postfix after every producer, and Harmony
    /// runs postfixes even when another mod's prefix skipped the original —
    /// so the substituted result is seen too. Null rails are replaced with a
    /// degenerate-but-valid two-point stub anchored at the switch home;
    /// consumers get a well-formed descriptor, and the hidden descriptors
    /// still never reach mesh building.
    /// </summary>
    [HarmonyPatch(typeof(SwitchGeometry), nameof(SwitchGeometry.Calculate))]
    [HarmonyPriority(Priority.Last)]
    internal static class FuseSwitchGeometryRailBackfillPatch
    {
        private static void Postfix(ref SwitchGeometry __result)
        {
            try
            {
                var railsNeedBackfill =
                    __result.aPointRail == null || __result.bPointRail == null ||
                    __result.aClosureRail == null || __result.bClosureRail == null ||
                    __result.leftStockRail == null || __result.rightStockRail == null ||
                    __result.leftGuardRail == null || __result.rightGuardRail == null;
                var frogNeedsBackfill = __result.frogPoints == null;
                if (!railsNeedBackfill && !frogNeedsBackfill)
                {
                    return;
                }

                var rotation = __result.standRotation;
                if (rotation.x == 0f && rotation.y == 0f && rotation.z == 0f && rotation.w == 0f)
                {
                    rotation = Quaternion.identity;
                }

                var origin = __result.switchHome;

                // One fresh curve per field: consumers may mutate a curve in
                // place or read Hand for left/right semantics, and a shared
                // aliased instance would leak edits across all eight rails.
                LineCurve Stub(Hand hand) => new LineCurve(new[]
                {
                    new LinePoint(origin, rotation),
                    new LinePoint(origin + rotation * Vector3.forward * 0.1f, rotation),
                }, hand);

                __result.aPointRail ??= Stub(Hand.Right);
                __result.bPointRail ??= Stub(Hand.Left);
                __result.aClosureRail ??= Stub(Hand.Right);
                __result.bClosureRail ??= Stub(Hand.Left);
                __result.leftStockRail ??= Stub(Hand.Left);
                __result.rightStockRail ??= Stub(Hand.Right);
                __result.leftGuardRail ??= Stub(Hand.Left);
                __result.rightGuardRail ??= Stub(Hand.Right);
                __result.frogPoints ??= new LinePoint[3];

                // The counter and warning are specifically about rail curves —
                // a frog-only repair (rails all present) is patched above but
                // must not report as a rail backfill.
                if (!railsNeedBackfill)
                {
                    return;
                }

                var count = FuseRuntimeGuardCounters.RecordSwitchGeometryRailsBackfilled();
                if (FuseGuardLog.ShouldLog(count))
                {
                    FuseLog.Warning(
                        $"FUSE backfilled null rail curves on a switch-geometry descriptor near {origin} " +
                        $"(occurrence #{count}). A modded geometry producer left the rail curves unset; " +
                        "without the backfill, shared-descriptor consumers such as Map Enhancer's junction " +
                        "markers crash on the first null rail and stay broken for the whole session.");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE switch-geometry rail backfill failed", ex);
            }
        }
    }
}
