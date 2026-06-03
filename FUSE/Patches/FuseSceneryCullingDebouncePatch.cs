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
        // Vanilla: SetLoaded(distanceBand <= 2). Bands 0-2 keep the model resident
        // (band 0-1 render, band 2 resident-but-invisible); band 3 unloads. Holding
        // an object at band 2 is a no-op visually but skips the destroy + reload.
        private const int ResidentBandCeiling = 2;

        // Deadband outer edge. Band 3 begins ~1500m out (the SceneryDistanceBands
        // ceiling); we keep FUSE scenery resident until it is this far so jitter near
        // 1500m can't flap, then allow the unload. Squared to avoid a sqrt per call.
        internal const float UnloadDistance = 3000f;
        private const float UnloadDistanceSqr = UnloadDistance * UnloadDistance;

        // Benchmark-only override (NOT a user setting): null = normal always-on
        // behavior; false = force the debounce OFF for an A/B baseline pass; true =
        // force ON. Set transiently by FuseSceneryBenchmark and cleared after a run.
        internal static bool? BenchmarkDebounceOverride;

        // Cached camera (the culler's distance reference is Camera.main). Refreshed
        // when it is destroyed OR merely disabled (camera-mode / scene transition),
        // so a stale, no-longer-active camera can't make the hold decision misjudge
        // distance after the player changes views or teleports.
        private static Camera _camera;

        // Typed accessor for the game's private load-state flag. The debounce holds
        // only scenery that is ALREADY loaded (true anti-flap). Force-loading a
        // not-yet-loaded object inside the deadband would expand the resident set to
        // the whole ~3 km sphere (~8x the game's ~1.5 km load volume) and pour it into
        // the load throttle on every teleport. Null only if the field was renamed (the
        // reflection-surface canary test guards the name), in which case we fall back
        // to the prior "hold anything in range" behavior.
        private static readonly AccessTools.FieldRef<SceneryAssetInstance, bool> WantsLoadedRef =
            BuildWantsLoadedRef();

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
            // Only the unload band is in scope; in-range bands keep vanilla behavior.
            // BenchmarkDebounceOverride == false forces a baseline (no-debounce) pass.
            if (distanceBand <= ResidentBandCeiling || __instance == null || BenchmarkDebounceOverride == false)
            {
                return;
            }

            try
            {
                if (__instance.GetComponent<FUSE.Runtime.API.SceneryAPI.FuseSceneryMarker>() == null)
                {
                    return; // FUSE-owned scenery only; vanilla culls normally.
                }

                // Anti-flap only: hold scenery the game has ALREADY loaded. A
                // not-yet-loaded object inside the deadband isn't flapping — leaving
                // it unloaded keeps the post-teleport working set to the game's real
                // load band instead of force-streaming the whole deadband sphere
                // through the throttle. (Fail-open: if the field binding is missing we
                // skip the gate and hold any in-range scenery, the prior behavior.)
                if (WantsLoadedRef != null && !WantsLoadedRef(__instance))
                {
                    return;
                }

                var camera = ResolveActiveCamera();
                if (camera == null)
                {
                    return; // No reference to measure against: let the game unload.
                }

                // transform.position and the camera are in the same (floating-origin
                // shifted) space, so the delta is correct regardless of world shifts.
                if (!ShouldHoldResident(camera.transform.position, __instance.transform.position))
                {
                    return; // Genuinely far: let band 3 through so the game unloads it.
                }

                // Inside the deadband and already loaded: hold it resident so the
                // ~1500m boundary can't thrash load/unload.
                distanceBand = ResidentBandCeiling;
                _suppressedUnloads++;
                if (FuseSettings.EnableSceneryCullingDiagnostics && _suppressedUnloads % 1000 == 0)
                {
                    FuseLog.Info(
                        $"FUSE diag scenery-debounce active: suppressedUnloads={_suppressedUnloads} " +
                        $"(holding FUSE scenery resident within {UnloadDistance:0}m).");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery unload debounce prefix failed", ex);
            }
        }

        // Camera.main, refreshed when the cached one is destroyed or merely disabled
        // (a camera-mode or scene transition leaves the old gameplay camera disabled
        // but not destroyed, so a == null check alone would keep a stale reference).
        private static Camera ResolveActiveCamera()
        {
            if (_camera == null || !_camera.isActiveAndEnabled)
            {
                _camera = Camera.main;
            }

            return _camera;
        }

        private static AccessTools.FieldRef<SceneryAssetInstance, bool> BuildWantsLoadedRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<SceneryAssetInstance, bool>("_wantsLoaded");
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE scenery debounce could not bind SceneryAssetInstance._wantsLoaded; " +
                    "holding any in-range FUSE scenery (prior behavior)", ex);
                return null;
            }
        }
    }
}
