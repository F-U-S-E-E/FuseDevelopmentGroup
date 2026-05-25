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
                FuseLog.Warning($"FUSE progression Configure postfix failed: {ex.Message}");
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
                FuseLog.Warning($"FUSE progression Unconfigure postfix failed: {ex.Message}");
            }
        }
    }
}
