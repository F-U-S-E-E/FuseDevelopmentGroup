using System;
using System.Threading.Tasks;
using AssetPack.Runtime;
using FUSE.Infrastructure;
using HarmonyLib;
using Helpers;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// One shared binding of <c>SceneryAssetInstance._wantsLoaded</c> — the game's
    /// private "model load requested" flag — used by the load throttle so the field
    /// name lives in exactly one place (the reflection-surface canary test guards
    /// it). Fail-safe: when the binding is missing, <see cref="IsLoadRequested"/>
    /// returns false and callers fall back to their prior behavior by gating on
    /// <see cref="Available"/>.
    /// </summary>
    internal static class FuseSceneryModelState
    {
        private static readonly AccessTools.FieldRef<SceneryAssetInstance, bool> WantsLoadedRef = Bind();
        private static readonly AccessTools.FieldRef<
            SceneryAssetInstance,
            Task<LoadedAssetReference<GameObject>>> ModelLoadTaskRef = BindLoadTask();
        private static readonly AccessTools.FieldRef<SceneryAssetInstance, GameObject> ModelRef =
            BindModel();

        /// <summary>True when <c>_wantsLoaded</c> bound successfully and can be read.</summary>
        internal static bool Available => WantsLoadedRef != null;

        internal static bool LoadTaskAvailable => ModelLoadTaskRef != null;

        internal static bool ModelAvailable => ModelRef != null;

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

        internal static void SetLoadRequested(SceneryAssetInstance instance, bool value)
        {
            if (WantsLoadedRef != null && instance != null)
            {
                WantsLoadedRef(instance) = value;
            }
        }

        internal static Task<LoadedAssetReference<GameObject>> GetLoadTask(
            SceneryAssetInstance instance)
        {
            return ModelLoadTaskRef != null && instance != null
                ? ModelLoadTaskRef(instance)
                : null;
        }

        internal static void SetLoadTask(
            SceneryAssetInstance instance,
            Task<LoadedAssetReference<GameObject>> task)
        {
            if (ModelLoadTaskRef != null && instance != null)
            {
                ModelLoadTaskRef(instance) = task;
            }
        }

        internal static GameObject GetModel(SceneryAssetInstance instance)
        {
            return ModelRef != null && instance != null
                ? ModelRef(instance)
                : null;
        }

        internal static void SetModel(SceneryAssetInstance instance, GameObject model)
        {
            if (ModelRef != null && instance != null)
            {
                ModelRef(instance) = model;
            }
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
                    "the load throttle falls back to vanilla (unthrottled) loading", ex);
                return null;
            }
        }

        private static AccessTools.FieldRef<
            SceneryAssetInstance,
            Task<LoadedAssetReference<GameObject>>> BindLoadTask()
        {
            try
            {
                return AccessTools.FieldRefAccess<
                    SceneryAssetInstance,
                    Task<LoadedAssetReference<GameObject>>>("_modelLoadTask");
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE scenery could not bind SceneryAssetInstance._modelLoadTask; " +
                    "the load throttle falls back to vanilla (unbounded) loading", ex);
                return null;
            }
        }

        private static AccessTools.FieldRef<SceneryAssetInstance, GameObject> BindModel()
        {
            try
            {
                return AccessTools.FieldRefAccess<SceneryAssetInstance, GameObject>("_model");
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE scenery could not bind SceneryAssetInstance._model; " +
                    "the runtime census cannot report FUSE model residency", ex);
                return null;
            }
        }
    }
}
