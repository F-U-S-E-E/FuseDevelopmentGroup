using System;
using System.Runtime.CompilerServices;
using FUSE.Infrastructure;
using HarmonyLib;
using Helpers;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Diagnostic Harmony patch for issue #76 (asset/scenery culling churn that
    /// may be caused by FUSE). Vanilla
    /// <c>SceneryAssetInstance.CullingSphereStateChanged(isVisible, distanceBand)</c>
    /// calls <c>SetLoaded(distanceBand &lt;= 2)</c> every time the culler reports a
    /// new band, so a model loads when the band drops to &lt;= 2 and unloads when it
    /// rises above 2. If FUSE re-enables scene paths, reloads components, or
    /// rebuilds containers more often than Railloader did, the culler gets extra
    /// chances to unload/reload scenery — visible as "popping".
    ///
    /// This patch logs the load/unload <em>transitions</em> for all scenery,
    /// tagging each line <c>fuse=true</c> (carries a
    /// <see cref="FUSE.Runtime.API.SceneryAPI.FuseSceneryMarker"/>) or
    /// <c>fuse=false</c> (vanilla), so we can confirm whether those events are hot
    /// and line up with the popping — including base-game structures like the
    /// roundhouse that FUSE never marks.
    /// We log threshold crossings rather than every raw call: a model only pops
    /// when <c>SetLoaded</c> actually flips, so repeated same-state callbacks (e.g.
    /// band 3 -&gt; 4 while flying away, both unloaded) are deduplicated to keep the
    /// signal readable. Each line carries a per-object flip count plus session
    /// totals; if a handful of objects rack up dozens of flips during a teleport,
    /// that is the smoking gun.
    ///
    /// Gated behind <see cref="FuseSettings.EnableSceneryCullingDiagnostics"/>
    /// (default off) because the culler fires constantly during play. When the
    /// flag is off the postfix returns on the first line, so it costs nothing in
    /// normal sessions; toggle it on from the FUSE Health → Advanced page right
    /// before a stress test and off afterwards.
    /// </summary>
    [HarmonyPatch(typeof(SceneryAssetInstance), "CullingSphereStateChanged")]
    internal static class FuseSceneryCullingDiagnosticPatch
    {
        // Vanilla: SetLoaded(distanceBand <= 2). Keep in one place so the log's
        // notion of "loaded" stays in lock-step with the game's.
        private const int LoadedBandThreshold = 2;

        // Per-instance last-known loaded state + flip counter. Weak keys so
        // destroyed scenery is collected without us pinning it; the Unity culling
        // callback is main-thread only, so no locking is required.
        private static readonly ConditionalWeakTable<SceneryAssetInstance, CullState> States =
            new ConditionalWeakTable<SceneryAssetInstance, CullState>();

        private static readonly ConditionalWeakTable<SceneryAssetInstance, CullState>.CreateValueCallback CreateState =
            _ => new CullState();

        private static long _fuseLoads;
        private static long _fuseUnloads;
        private static long _vanillaLoads;
        private static long _vanillaUnloads;

        // Snapshot accessors for FuseSceneryBenchmark. These tallies only increment
        // while the cull log is enabled (the Postfix early-returns otherwise), so the
        // benchmark transiently enables it for the duration of a run.
        internal static long FuseLoads => _fuseLoads;
        internal static long FuseUnloads => _fuseUnloads;
        internal static long VanillaLoads => _vanillaLoads;
        internal static long VanillaUnloads => _vanillaUnloads;

        internal static void ResetCounters()
        {
            _fuseLoads = 0;
            _fuseUnloads = 0;
            _vanillaLoads = 0;
            _vanillaUnloads = 0;
        }

        private static void Postfix(SceneryAssetInstance __instance, bool isVisible, int distanceBand)
        {
            if (!FuseSettings.EnableSceneryCullingDiagnostics || __instance == null)
            {
                return;
            }

            try
            {
                // Log BOTH FUSE-owned and vanilla scenery, tagged via fuse=. The
                // churn hypothesis is about the game's culler in general, and the
                // structures that visibly pop (e.g. the roundhouse) are usually
                // base-game scenery with no FUSE marker, so a FUSE-only filter
                // would miss them.
                var marker = __instance.GetComponent<FUSE.Runtime.API.SceneryAPI.FuseSceneryMarker>();
                var isFuse = marker != null;

                var loaded = distanceBand <= LoadedBandThreshold;
                var state = States.GetValue(__instance, CreateState);
                if (state.HasObservation && state.Loaded == loaded)
                {
                    // No load/unload transition -> no pop -> nothing to report.
                    return;
                }

                var isFirst = !state.HasObservation;
                if (!isFirst)
                {
                    state.FlipCount++;
                }

                state.HasObservation = true;
                state.Loaded = loaded;

                string verb;
                if (loaded)
                {
                    if (isFuse) { _fuseLoads++; } else { _vanillaLoads++; }
                    verb = isFirst ? "INIT-LOAD" : "LOAD";
                }
                else
                {
                    if (isFuse) { _fuseUnloads++; } else { _vanillaUnloads++; }
                    verb = isFirst ? "INIT-UNLOAD" : "UNLOAD";
                }

                var loads = isFuse ? _fuseLoads : _vanillaLoads;
                var unloads = isFuse ? _fuseUnloads : _vanillaUnloads;
                var id = isFuse ? (marker.Id ?? "<null>") : (__instance.identifier ?? "<null>");

                FuseLog.Info(
                    $"FUSE diag scenery-cull {verb} fuse={isFuse} id='{id}' name='{__instance.name ?? "<null>"}' " +
                    $"band={distanceBand} visible={isVisible} flips={state.FlipCount} " +
                    $"loads={loads} unloads={unloads} frame={Time.frameCount}.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE diag scenery-cull postfix failed", ex);
            }
        }

        private sealed class CullState
        {
            public bool HasObservation;
            public bool Loaded;
            public int FlipCount;
        }
    }
}
