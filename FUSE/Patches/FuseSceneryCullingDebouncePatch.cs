using System;
using FUSE.Infrastructure;
using HarmonyLib;
using Helpers;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Issue #76 fix: adds a distance deadband (hysteresis) to scenery model
    /// unloading for FUSE-owned scenery, killing the boundary load/unload flap.
    ///
    /// The game culls scenery with no hysteresis. Distance bands {100, 1000, 1500}m
    /// drive <c>SetLoaded(distanceBand &lt;= 2)</c>, so a model unloads the instant its
    /// culling sphere crosses band 3 (&gt;1500m) and reloads (async asset-bundle load +
    /// Instantiate) when it returns. During a teleport / camera-settle the reference
    /// point swings across that boundary repeatedly, so clusters of FUSE scenery flap
    /// load/unload many times in seconds — the popping, and a big share of the slow
    /// settle (each object reloads ~8-16x instead of once).
    ///
    /// This prefix only acts when the game wants to UNLOAD (band &gt;= 3). It measures
    /// the real distance from the active camera to the instance: while the object is
    /// inside <see cref="UnloadDistance"/> it clamps the band to 2 (the game's own
    /// "resident but renderers-off" state) so boundary jitter between ~1500m and the
    /// deadband edge can't thrash load/unload; once the object is genuinely beyond
    /// the deadband it lets band 3 through and the game unloads it normally. The
    /// resident working set is therefore bounded to a radius around the camera — it
    /// still culls, it just doesn't flap.
    ///
    /// A distance test is used rather than a time-based grace because
    /// <c>CullingSphereStateChanged</c> is event / world-shift driven (not called
    /// every frame), so a timed grace never reliably expires and ends up pinning
    /// everything. The distance check is correct on every individual call regardless
    /// of cadence, and needs no per-instance state.
    ///
    /// Scoped to FUSE scenery (<see cref="FUSE.Runtime.API.SceneryAPI.FuseSceneryMarker"/>)
    /// so vanilla culling is untouched. Always active on this branch (no runtime
    /// toggle). Runs alongside the diagnostic postfix on the same method: in-deadband
    /// objects are clamped (so they log no UNLOAD), genuinely-far objects pass band 3
    /// through (so the postfix logs <c>scenery-cull UNLOAD fuse=True</c>) — that is the
    /// signal that culling is still happening.
    /// </summary>
    [HarmonyPatch(typeof(SceneryAssetInstance), "CullingSphereStateChanged")]
    internal static class FuseSceneryCullingDebouncePatch
    {
        // Vanilla: SetLoaded(distanceBand <= 2). Band 0-1 = loaded AND rendered,
        // band 2 = loaded but renderers OFF (resident-but-invisible), band 3 = unloaded.
        // So a RENDER band is <= 1; the model merely stays loaded up to <= 2.
        private const int RenderBandCeiling = 1;
        private const int ResidentBandCeiling = 2;

        // Mask-bearing scenery is pinned to this nearest band so it stays BOTH loaded and
        // rendered instead of being parked invisible at band 2 or unloaded at band 3. This
        // is a safety net layered over the real fix (MapAPI.DecoupleAttachedMapMasks, which
        // moves the terrain mask off the streamed model); once that is verified in-game the
        // pin is redundant and masked scenery can stream like any other.
        private const int RenderResidentBand = 0;

        // Deadband outer edge. Band 3 begins ~1500m out (the SceneryDistanceBands
        // ceiling); we keep FUSE scenery resident until it is this far so jitter near
        // 1500m can't flap, then allow the unload. Squared to avoid a sqrt per call.
        internal const float UnloadDistance = 3000f;
        private const float UnloadDistanceSqr = UnloadDistance * UnloadDistance;

        // Benchmark-only override (NOT a user setting): null = normal always-on
        // behavior; false = force the debounce OFF for an A/B baseline pass; true =
        // force ON. Set transiently by FuseSceneryBenchmark and cleared after a run.
        internal static bool? BenchmarkDebounceOverride;

        private static long _suppressedUnloads;

        /// <summary>Unloads suppressed (objects held resident) since the last reset.</summary>
        internal static long SuppressedUnloads => _suppressedUnloads;

        internal static void ResetSuppressedUnloads() => _suppressedUnloads = 0;

        /// <summary>
        /// Pure debounce decision: should a band-3 FUSE scenery object be held
        /// resident (true) rather than unloaded (false)? True while it is inside the
        /// <see cref="UnloadDistance"/> deadband of the camera. Extracted for
        /// deterministic unit testing (see FUSE.UnityTests).
        /// </summary>
        internal static bool ShouldHoldResident(Vector3 cameraPos, Vector3 objectPos)
        {
            return (cameraPos - objectPos).sqrMagnitude < UnloadDistanceSqr;
        }

        /// <summary>
        /// Full hold decision (pure, unit-testable): hold a band-3 FUSE object
        /// resident only when its model load has already been requested
        /// (<paramref name="modelLoadRequested"/>) AND it is inside the deadband. The
        /// load-requested gate keeps the debounce to its anti-flap purpose and stops
        /// it from force-loading the whole deadband sphere on a teleport.
        /// </summary>
        internal static bool ShouldHold(bool modelLoadRequested, Vector3 cameraPos, Vector3 objectPos)
        {
            return modelLoadRequested && ShouldHoldResident(cameraPos, objectPos);
        }

        private static void Prefix(SceneryAssetInstance __instance, ref int distanceBand)
        {
            // Bands 0-1 are already loaded AND rendered, so there is nothing to keep
            // resident there. We act only at band 2 (loaded but renderers off) and band 3
            // (unload). BenchmarkDebounceOverride == false forces a no-debounce baseline.
            if (distanceBand <= RenderBandCeiling || __instance == null || BenchmarkDebounceOverride == false)
            {
                return;
            }

            try
            {
                var marker = __instance.GetComponent<FUSE.Runtime.API.SceneryAPI.FuseSceneryMarker>();
                if (marker == null)
                {
                    return; // FUSE-owned scenery only; vanilla culls normally.
                }

                // Only ever hold scenery the game has ALREADY loaded; a not-yet-loaded
                // object has nothing to keep resident. Fail-open: when _wantsLoaded can't
                // be read we treat it as loaded (the prior behavior).
                var modelLoadRequested = !FuseSceneryModelState.Available
                    || FuseSceneryModelState.IsLoadRequested(__instance);
                if (!modelLoadRequested)
                {
                    return;
                }

                if (marker.IsMaskBearing)
                {
                    // Pin a loaded mask-bearing building to the nearest band so it stays BOTH
                    // loaded and RENDERED. Left alone, the culler pushes it to band 2 (loaded
                    // but renderers OFF) or band 3 (unloaded) when far, and the on-return
                    // re-show/reload is unreliable across a teleport world-origin shift —
                    // which is how you end up standing inside an invisible building. Never
                    // letting it leave the rendered band sidesteps that. The load throttle is
                    // bypassed for these too, so the first load is immediate. Unity's
                    // per-renderer frustum culling still skips it when off-screen, so this is
                    // "always eligible to draw", not "always drawn". Safety net over the real
                    // fix (MapAPI.DecoupleAttachedMapMasks); removable once that is verified.
                    distanceBand = RenderResidentBand;
                    _suppressedUnloads++;
                    LogSuppressedIfDue();
                    return;
                }

                // Non-mask FUSE scenery: anti-flap only, and only against the UNLOAD band.
                // Band 2 (loaded-but-hidden) is the game's own behaviour — leave it. At
                // band 3, hold inside the ~3km deadband so jitter near the ~1500m boundary
                // can't thrash load/unload, otherwise let the game unload. transform.position
                // and the camera share the same (floating-origin shifted) space, so the
                // delta is world-shift safe. Mirrors the unit-tested ShouldHoldResident.
                if (distanceBand <= ResidentBandCeiling)
                {
                    return;
                }

                var camera = FuseSceneryCameraRef.Resolve();
                if (camera == null)
                {
                    return; // No reference to measure against: let the game unload.
                }

                if (!ShouldHoldResident(camera.transform.position, __instance.transform.position))
                {
                    return; // Genuinely far: let band 3 through.
                }

                distanceBand = ResidentBandCeiling; // clamp 3 -> 2 (resident anti-flap).
                _suppressedUnloads++;
                LogSuppressedIfDue();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery unload debounce prefix failed", ex);
            }
        }

        private static void LogSuppressedIfDue()
        {
            if (FuseSettings.EnableSceneryCullingDiagnostics && _suppressedUnloads % 1000 == 0)
            {
                FuseLog.Info(
                    $"FUSE diag scenery-debounce active: suppressedUnloads={_suppressedUnloads} " +
                    $"(mask-bearing pinned loaded+rendered; non-mask held within {UnloadDistance:0}m).");
            }
        }
    }
}
