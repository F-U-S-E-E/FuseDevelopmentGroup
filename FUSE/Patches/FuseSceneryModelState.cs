using System;
using FUSE.Infrastructure;
using HarmonyLib;
using Helpers;

namespace FUSE.Patches
{
    /// <summary>
    /// One shared binding of <c>SceneryAssetInstance._wantsLoaded</c> — the game's
    /// private "model load requested" flag — used by both the cull debounce and the
    /// load throttle so the field name lives in exactly one place (the
    /// reflection-surface canary test guards it). Fail-safe: when the binding is
    /// missing, <see cref="IsLoadRequested"/> returns false and callers fall back to
    /// their prior behavior by gating on <see cref="Available"/>.
    /// </summary>
    internal static class FuseSceneryModelState
    {
        private static readonly AccessTools.FieldRef<SceneryAssetInstance, bool> WantsLoadedRef = Bind();

        /// <summary>True when <c>_wantsLoaded</c> bound successfully and can be read.</summary>
        internal static bool Available => WantsLoadedRef != null;

        /// <summary>
        /// True when the game has requested/started this scenery's model load. Never
        /// throws: returns false for a null instance or when the binding is
        /// unavailable — callers that must distinguish "not loaded" from "can't tell"
        /// check <see cref="Available"/>.
        /// </summary>
        internal static bool IsLoadRequested(SceneryAssetInstance instance)
        {
            return WantsLoadedRef != null && instance != null && WantsLoadedRef(instance);
        }

        private static AccessTools.FieldRef<SceneryAssetInstance, bool> Bind()
        {
            try
            {
                return AccessTools.FieldRefAccess<SceneryAssetInstance, bool>("_wantsLoaded");
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE scenery could not bind SceneryAssetInstance._wantsLoaded; " +
                    "debounce/throttle fall back to distance-only behavior", ex);
                return null;
            }
        }
    }
}
