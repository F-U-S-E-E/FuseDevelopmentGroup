using System;
using FUSE.Infrastructure;
using Game.Progression;
using HarmonyLib;
using KeyValue.Runtime;

namespace FUSE.Patches
{
    /// <summary>
    /// Harmony postfix on <see cref="Progression.Configure(KeyValueObject)"/> that
    /// notifies <see cref="FUSE.Runtime.API.ProgressionAPI"/> as soon as the
    /// game's progression has finished initializing from save data. From this
    /// point on, <c>StateManager.IsSandbox</c> returns the real value (before
    /// Configure it defaults to true because GameMode hasn't been deserialized
    /// yet).
    ///
    /// This is the gate FUSE uses to know when it is safe to invoke the game's
    /// HandleFeatureEnablesChanged via reflection. Calling that pass with a
    /// stale IsSandbox=true causes graph.SetGroupEnabled to be issued for every
    /// feature whose defaultEnableInSandbox is true, and the game's later
    /// dict-change detection won't undo it (oldDefault and newValue evaluate to
    /// the same value once IsSandbox flips, so the change-detector skips the
    /// feature and no SetGroupEnabled(false) is emitted). That was the Alarka
    /// branch / Ela bridge regression: the bridge stayed visible because its
    /// track group was added to enabledGroupIds during the racy pre-Configure
    /// window and never removed.
    ///
    /// Any pre-Configure refresh request that <see cref="FUSE.Runtime.API.ProgressionAPI.RefreshRuntimeStateAfterApply"/>
    /// parked is replayed inside <see cref="FUSE.Runtime.API.ProgressionAPI.NotifyGameProgressionConfigured"/>.
    /// </summary>
    [HarmonyPatch(typeof(Progression), nameof(Progression.Configure))]
    internal static class FuseProgressionConfigureHookPatch
    {
        private static void Postfix()
        {
            try
            {
                FUSE.Runtime.API.ProgressionAPI.NotifyGameProgressionConfigured();
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE progression Configure postfix failed", ex);
            }
        }
    }

    /// <summary>
    /// Counterpart to <see cref="FuseProgressionConfigureHookPatch"/>: clears
    /// the configured-flag when the active save is torn down. Without this,
    /// reloading or switching saves would leave the flag in its previous-load
    /// state, and the next load's pre-Configure refresh window would run the
    /// racy code path immediately (with stale IsSandbox=true and an empty
    /// MapFeatureManager KVO that hasn't been restored from snapshot yet).
    /// That is exactly how Alarka / Ela bridge re-appeared after a reload:
    /// ForceApplyCurrentMapFeatureState invoked HandleFeatureEnablesChanged
    /// on the second load with empty KVO, defaulted el-br to true, and
    /// pushed SetGroupEnabled(ela-bridge, true) into the graph.
    /// </summary>
    [HarmonyPatch(typeof(Progression), nameof(Progression.Unconfigure))]
    internal static class FuseProgressionUnconfigureHookPatch
    {
        private static void Postfix()
        {
            try
            {
                FUSE.Runtime.API.ProgressionAPI.NotifyGameProgressionUnconfigured();
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE progression Unconfigure postfix failed", ex);
            }
        }
    }

    /// <summary>
    /// The sandbox-side counterpart of <see cref="FuseProgressionConfigureHookPatch"/>.
    ///
    /// The game decides once per load — inside ProgressionManager's
    /// properties-restore handler — whether a Progression gets configured at
    /// all. Sandbox saves discard their progression blob ("Game is sandbox but
    /// has progression … Ignoring.") and take the "No progression specified."
    /// branch; a company save whose progression id resolves to nothing ends up
    /// the same way. On those loads <c>Progression.Configure</c> NEVER runs, so
    /// a FUSE refresh parked on the Configure postfix waits forever — observed
    /// in the field as a whole sandbox session where the transiently
    /// pre-enabled track groups were never re-finalized (orphan mod sidings
    /// stayed player-routable) and live-reference rebinding never ran.
    ///
    /// This postfix fires when that restore handler returns. By then the
    /// save's GameMode has been deserialized (the handler runs after the
    /// snapshot's properties are restored), so <c>StateManager.IsSandbox</c>
    /// carries the same trust Configure gives on company loads. If the handler
    /// finished WITHOUT configuring a current progression, we notify
    /// ProgressionAPI to replay any parked refresh under the reduced
    /// no-progression profile. When Configure DID run, its own postfix (which
    /// fires first, from inside the same handler) has already notified, and
    /// both the current-progression check here and the configured-flag check
    /// inside the notify keep this hook from demoting that.
    /// </summary>
    [HarmonyPatch(typeof(ProgressionManager), "OnEnableWithProperties")]
    internal static class FuseProgressionManagerNoProgressionHookPatch
    {
        private static void Postfix(ProgressionManager __instance)
        {
            try
            {
                if (FUSE.Runtime.API.ProgressionAPI.HasConfiguredCurrentProgression(__instance))
                {
                    return;
                }

                FUSE.Runtime.API.ProgressionAPI.NotifyGameProgressionSettledWithoutProgression();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE progression no-progression settle postfix failed", ex);
            }
        }
    }
}
