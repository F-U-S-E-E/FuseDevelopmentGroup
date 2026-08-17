using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AssetPack.Runtime;
using FUSE.Infrastructure;
using HarmonyLib;
using Helpers;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Replaces <see cref="SceneryAssetInstance.SetLoaded"/> with a
    /// generation-owned load. An async completion may instantiate only while it
    /// still owns the instance's current generation; a load invalidated by an
    /// intervening unload is disposed instead of resurrecting stale scenery and
    /// retaining its asset bundle.
    ///
    /// The FUSE load-throttle prefix runs first and decides when this replacement
    /// may start. Keeping both patches on the same method preserves bounded
    /// concurrency while removing the original load/unload race.
    /// </summary>
    [HarmonyPatch(typeof(SceneryAssetInstance), "SetLoaded", new[] { typeof(bool) })]
    [HarmonyPriority(Priority.Normal)]
    [HarmonyBefore("SceneryRaceFix")]
    internal static class FuseScenerySetLoadedRaceFixPatch
    {
        private sealed class LoadState
        {
            internal int Generation;
            internal Task<LoadedAssetReference<GameObject>> ActiveTask;
            internal LoadedAssetReference<GameObject> LoadedReference;
        }

        private static readonly ConditionalWeakTable<SceneryAssetInstance, LoadState> States =
            new ConditionalWeakTable<SceneryAssetInstance, LoadState>();

        private static readonly FieldInfo CullRenderersField =
            AccessTools.Field(typeof(SceneryAssetInstance), "_cullRenderers");

        private static readonly MethodInfo DidLoadModelMethod =
            AccessTools.Method(typeof(SceneryAssetInstance), "DidLoadModel");

        private static readonly MethodInfo WillUnloadModelMethod =
            AccessTools.Method(typeof(SceneryAssetInstance), "WillUnloadModel");

        private static readonly bool ReflectionReady =
            FuseSceneryModelState.Available &&
            FuseSceneryModelState.LoadTaskAvailable &&
            FuseSceneryModelState.ModelAvailable &&
            CullRenderersField != null &&
            DidLoadModelMethod != null &&
            WillUnloadModelMethod != null;

        private static long _staleCompletions;
        private static int _loggedCleanupFailure;

        internal static long StaleCompletions => Interlocked.Read(ref _staleCompletions);

        internal static bool Available => ReflectionReady;

        private static bool Prefix(SceneryAssetInstance __instance, bool loaded)
        {
            if (!ReflectionReady)
            {
                return true;
            }

            if (__instance == null)
            {
                return false;
            }

            var state = States.GetOrCreateValue(__instance);
            if (loaded)
            {
                StartLoadIfNeeded(__instance, state);
            }
            else
            {
                Unload(__instance, state);
            }

            return false;
        }

        private static void StartLoadIfNeeded(SceneryAssetInstance instance, LoadState state)
        {
            var wantsLoaded = FuseSceneryModelState.IsLoadRequested(instance);
            var model = FuseSceneryModelState.GetModel(instance);
            var existingTask = FuseSceneryModelState.GetLoadTask(instance);
            if (wantsLoaded && (model != null || existingTask != null))
            {
                return;
            }

            if (model != null)
            {
                FuseSceneryModelState.SetLoadRequested(instance, true);
                return;
            }

            var abandonedReference = state.LoadedReference;
            state.LoadedReference = null;
            FuseDeferredAssetReferenceReleaseQueue.DisposeSafely(abandonedReference);

            var generation = unchecked(++state.Generation);
            var identifier = instance.identifier;
            FuseSceneryModelState.SetLoadRequested(instance, true);

            Task<LoadedAssetReference<GameObject>> loadTask;
            try
            {
                loadTask = SceneryAssetManager.Shared.LoadScenery(identifier);
                state.ActiveTask = loadTask;
                FuseSceneryModelState.SetLoadTask(instance, loadTask);
            }
            catch (Exception ex)
            {
                FuseSceneryModelState.SetLoadRequested(instance, false);
                FuseSceneryModelState.SetLoadTask(instance, null);
                FuseLog.Exception($"Error loading scenery '{identifier}'", ex);
                return;
            }

            _ = AwaitLoadAsync(instance, state, generation, identifier, loadTask);
        }

        private static async Task AwaitLoadAsync(
            SceneryAssetInstance instance,
            LoadState state,
            int generation,
            string identifier,
            Task<LoadedAssetReference<GameObject>> loadTask)
        {
            LoadedAssetReference<GameObject> loadedReference;
            try
            {
                loadedReference = await loadTask;
            }
            catch (Exception ex)
            {
                if (IsCurrentLoad(instance, state, generation, loadTask))
                {
                    if (state.ActiveTask == loadTask)
                    {
                        state.ActiveTask = null;
                    }

                    FuseSceneryModelState.SetLoadTask(instance, null);
                    FuseSceneryModelState.SetLoadRequested(instance, false);
                    FuseLog.Exception($"Error loading scenery '{identifier}'", ex);
                }

                return;
            }

            if (instance == null ||
                !IsCurrentLoad(instance, state, generation, loadTask) ||
                !FuseSceneryModelState.IsLoadRequested(instance))
            {
                if (state.ActiveTask == loadTask)
                {
                    state.ActiveTask = null;
                }

                FuseDeferredAssetReferenceReleaseQueue.DisposeSafely(loadedReference);
                if (instance != null && FuseSceneryModelState.GetLoadTask(instance) == loadTask)
                {
                    FuseSceneryModelState.SetLoadTask(instance, null);
                }

                var stale = Interlocked.Increment(ref _staleCompletions);
                if (stale == 1)
                {
                    FuseLog.Info(
                        "FUSE discarded a stale scenery load completion; later stale " +
                        "completions are counted and cleaned silently.");
                }

                return;
            }

            if (FuseSceneryModelState.GetModel(instance) != null)
            {
                if (state.ActiveTask == loadTask)
                {
                    state.ActiveTask = null;
                }

                FuseDeferredAssetReferenceReleaseQueue.DisposeSafely(loadedReference);
                FuseSceneryModelState.SetLoadTask(instance, null);
                return;
            }

            GameObject model = null;
            try
            {
                model = InstantiateLoadedModel(
                    instance,
                    loadedReference.Asset);
                model.hideFlags = HideFlags.DontSave;
                FuseSceneryModelState.SetModel(instance, model);
                state.ActiveTask = null;
                state.LoadedReference = loadedReference;
                DidLoadModelMethod.Invoke(instance, Array.Empty<object>());

                // The loaded reference is owned by state. Do not retain a second
                // reference through Task<TResult> for the model's whole lifetime.
                FuseSceneryModelState.SetLoadTask(instance, null);
            }
            catch (Exception ex)
            {
                if (state.LoadedReference == loadedReference)
                {
                    state.LoadedReference = null;
                }

                if (state.ActiveTask == loadTask)
                {
                    state.ActiveTask = null;
                }

                FuseSceneryModelState.SetLoadTask(instance, null);
                FuseSceneryModelState.SetLoadRequested(instance, false);
                var failedModel = model ?? FuseSceneryModelState.GetModel(instance);
                if (failedModel != null)
                {
                    InvokeWillUnloadModel(instance);
                }

                ClearCullRenderers(instance);
                FuseSceneryModelState.SetModel(instance, null);
                DestroyModel(failedModel);
                if (failedModel != null)
                {
                    FuseDeferredAssetReferenceReleaseQueue.ReleaseAfterCurrentFrame(loadedReference);
                }
                else
                {
                    FuseDeferredAssetReferenceReleaseQueue.DisposeSafely(loadedReference);
                }

                FuseLog.Exception($"Error preparing loaded scenery '{identifier}'", ex);
            }
        }

        private static GameObject InstantiateLoadedModel(
            SceneryAssetInstance instance,
            GameObject prefab)
        {
            var parent = instance.transform;
            return UnityEngine.Object.Instantiate(
                prefab,
                parent.position,
                parent.rotation,
                parent);
        }

        private static void Unload(SceneryAssetInstance instance, LoadState state)
        {
            var wantedLoaded = FuseSceneryModelState.IsLoadRequested(instance);
            var model = FuseSceneryModelState.GetModel(instance);
            var loadTask = FuseSceneryModelState.GetLoadTask(instance);
            var taskOwnedByPatch = state.ActiveTask == loadTask;

            FuseSceneryModelState.SetLoadRequested(instance, false);
            unchecked
            {
                state.Generation++;
            }

            var loadedReference = state.LoadedReference;
            state.LoadedReference = null;
            state.ActiveTask = null;
            if (!taskOwnedByPatch &&
                loadedReference == null &&
                loadTask != null &&
                loadTask.Status == TaskStatus.RanToCompletion)
            {
                loadedReference = loadTask.Result;
            }

            FuseSceneryModelState.SetLoadTask(instance, null);

            if (!wantedLoaded && model == null)
            {
                ClearCullRenderers(instance);
                FuseDeferredAssetReferenceReleaseQueue.DisposeSafely(loadedReference);
                return;
            }

            if (model == null)
            {
                ClearCullRenderers(instance);
                FuseDeferredAssetReferenceReleaseQueue.DisposeSafely(loadedReference);
                return;
            }

            InvokeWillUnloadModel(instance);
            ClearCullRenderers(instance);
            FuseSceneryModelState.SetModel(instance, null);
            DestroyModel(model);
            FuseDeferredAssetReferenceReleaseQueue.ReleaseAfterCurrentFrame(loadedReference);
        }

        private static bool IsCurrentLoad(
            SceneryAssetInstance instance,
            LoadState state,
            int generation,
            Task<LoadedAssetReference<GameObject>> loadTask)
        {
            return instance != null &&
                   state.Generation == generation &&
                   FuseSceneryModelState.GetLoadTask(instance) == loadTask;
        }

        private static void ClearCullRenderers(SceneryAssetInstance instance)
        {
            TryCleanupAction(
                "clearing the scenery renderer cache",
                () =>
                {
                    if (CullRenderersField.GetValue(instance) is ICollection<Renderer> renderers)
                    {
                        renderers.Clear();
                    }
                });
        }

        private static void InvokeWillUnloadModel(SceneryAssetInstance instance)
        {
            TryCleanupAction(
                "running WillUnloadModel",
                () => WillUnloadModelMethod.Invoke(instance, Array.Empty<object>()));
        }

        private static void DestroyModel(GameObject model)
        {
            if (model == null)
            {
                return;
            }

            TryCleanupAction(
                "destroying a scenery model",
                () =>
                {
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(model);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(model);
                    }
                });
        }

        private static void TryCleanupAction(string operation, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _loggedCleanupFailure, 1) == 0)
                {
                    FuseLog.Exception(
                        $"FUSE scenery race guard cleanup failed while {operation}; " +
                        "later cleanup failures are suppressed",
                        ex);
                }
            }
        }
    }
}
