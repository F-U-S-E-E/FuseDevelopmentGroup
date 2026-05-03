using System;
using System.Collections;
using System.Collections.Generic;
using FUSE.Infrastructure;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FUSE.Loading
{
    [Experimental("Early scene-path suppression gates Unity scene activation and may wedge scene loads if Unity or another mod misbehaves.")]
    public static class FuseEarlyLoader
    {
        private static readonly HashSet<int> GatedOperations = new HashSet<int>();

        private static FuseEarlyLoaderRunner _runner;
        private static bool _initialized;
        private static bool _primedDefinitions;
        private static bool _gateDisabledForSession;
        private static bool _sceneLoadedSubscribed;
        private static bool _patchAvailable;

        public static bool IsEnabled => FuseSettings.EnableExperimentalEarlyScenePathSuppression;

        public static void SetPatchAvailable(bool available)
        {
            _patchAvailable = available;
        }

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            if (!IsEnabled)
            {
                FuseLog.Info("FUSE experimental early scene-path suppression is disabled in Info.json settings.");
                return;
            }

            if (!_patchAvailable)
            {
                FuseLog.Warning("FUSE [Experimental] early scene-path suppression is enabled, but the Harmony patch is unavailable. Scene-path suppression will no-op; normal loading continues.");
                return;
            }

            EnsureRunner();
            SubscribeSceneLoaded();
            FuseLog.Warning(
                "FUSE [Experimental] early scene-path suppression is enabled. " +
                "This gates AsyncOperation.allowSceneActivation and carries a non-zero risk of wedging scene loads.");
        }

        public static void Shutdown()
        {
            UnsubscribeSceneLoaded();
            _initialized = false;
            _primedDefinitions = false;
            _gateDisabledForSession = false;
            _patchAvailable = false;
            GatedOperations.Clear();

            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner.gameObject);
                _runner = null;
            }
        }

        public static void RestoreOnMapUnload()
        {
            _primedDefinitions = false;
            _gateDisabledForSession = false;
            GatedOperations.Clear();
        }

        public static void ApplyFallbackAfterMapLoad()
        {
            if (!IsEnabled || !_patchAvailable)
            {
                return;
            }

            if (!PrimeEarlyWindow("map load fallback"))
            {
                return;
            }

            FuseWorldSuppressor.ApplyActiveScenePathSuppressions("map load fallback");
        }

        public static void TryGateSceneLoad(string sceneName, AsyncOperation operation, string source)
        {
            if (!IsEnabled || !_patchAvailable || _gateDisabledForSession || operation == null)
            {
                return;
            }

            if (!ShouldGateScene(sceneName))
            {
                return;
            }

            if (!PrimeEarlyWindow(source ?? "scene load"))
            {
                return;
            }

            if (!FuseWorldSuppressor.HasActiveScenePathSuppressions)
            {
                return;
            }

            FuseExperimentalLog.WarnFirstUse(
                "FUSE.Loading.FuseEarlyLoader.TryGateSceneLoad",
                "AsyncOperation.allowSceneActivation gating");

            var operationId = operation.GetHashCode();
            if (!GatedOperations.Add(operationId))
            {
                return;
            }

            try
            {
                EnsureRunner();
                SubscribeSceneLoaded();
                operation.allowSceneActivation = false;
                operation.completed += OnGatedOperationCompleted;
                _runner.StartCoroutine(GateRoutine(operation, sceneName ?? string.Empty, source ?? "scene load", operationId));
                FuseLog.Info($"FUSE [Experimental] gated scene activation for '{sceneName ?? "<unknown>"}' from '{source ?? "unknown"}'.");
            }
            catch (Exception ex)
            {
                GatedOperations.Remove(operationId);
                TryReleaseOperation(operation, $"gate setup failure for '{sceneName ?? "<unknown>"}'");
                FuseLog.Warning($"FUSE [Experimental] failed to gate scene load '{sceneName ?? "<unknown>"}'; loading will continue normally: {ex.Message}");
            }
        }

        private static bool PrimeEarlyWindow(string reason)
        {
            if (_primedDefinitions)
            {
                return true;
            }

            try
            {
                FuseDataPackageDiscovery.LoadPackagesFromDisk(false);
                FuseWorldSuppressor.RegisterEarlyScenePathSuppressionsFromLoadedDefinitions(reason ?? "early window");
                _primedDefinitions = true;
                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE [Experimental] early scene-path suppression could not prime package definitions; scene-path suppression will no-op: {ex.Message}");
                return false;
            }
        }

        private static IEnumerator GateRoutine(AsyncOperation operation, string sceneName, string source, int operationId)
        {
            var start = Time.realtimeSinceStartup;
            var timeout = FuseSettings.ExperimentalEarlyScenePathSuppressionTimeoutSeconds;

            while (operation != null &&
                   !operation.isDone &&
                   operation.progress < 0.9f &&
                   Time.realtimeSinceStartup - start < timeout)
            {
                yield return null;
            }

            if (operation != null && !operation.isDone && Time.realtimeSinceStartup - start >= timeout)
            {
                _gateDisabledForSession = true;
                TryReleaseOperation(operation, $"timeout after {timeout:0.0}s scene='{sceneName}' source='{source}'");
                FuseLog.Error(
                    $"FUSE [Experimental] early scene gate timed out after {timeout:0.0}s for scene '{sceneName}'. " +
                    "Released activation immediately and disabled additional early gates for this session.");
                yield break;
            }

            FuseWorldSuppressor.ApplyActiveScenePathSuppressions($"early gate before activation scene='{sceneName}'");
            TryReleaseOperation(operation, $"ready scene='{sceneName}' source='{source}'");

            while (operation != null && !operation.isDone)
            {
                yield return null;
            }

            GatedOperations.Remove(operationId);
            yield return null;
            FuseWorldSuppressor.ApplyActiveScenePathSuppressions($"post-activation scene='{sceneName}'");
        }

        private static void OnGatedOperationCompleted(AsyncOperation operation)
        {
            try
            {
                FuseWorldSuppressor.ApplyActiveScenePathSuppressions("async scene completed");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE [Experimental] failed during post-complete scene-path suppression: {ex.Message}");
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsEnabled || !_patchAvailable)
            {
                return;
            }

            try
            {
                PrimeEarlyWindow("sceneLoaded");
                FuseWorldSuppressor.ApplyActiveScenePathSuppressions($"sceneLoaded '{scene.name}'");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE [Experimental] sceneLoaded suppression failed for scene '{scene.name}': {ex.Message}");
            }
        }

        private static bool ShouldGateScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return true;
            }

            return sceneName.IndexOf("MainMenu", StringComparison.OrdinalIgnoreCase) < 0 &&
                   sceneName.IndexOf("GameUI", StringComparison.OrdinalIgnoreCase) < 0 &&
                   sceneName.IndexOf("Persistent", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static void TryReleaseOperation(AsyncOperation operation, string reason)
        {
            if (operation == null)
            {
                return;
            }

            try
            {
                operation.allowSceneActivation = true;
                FuseLog.Info($"FUSE [Experimental] released scene activation gate for {reason ?? "unspecified"}.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE [Experimental] failed to release scene activation gate for {reason ?? "unspecified"}: {ex.Message}");
            }
        }

        private static void EnsureRunner()
        {
            if (_runner != null)
            {
                return;
            }

            var go = new GameObject("FUSE.EarlyLoader");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<FuseEarlyLoaderRunner>();
        }

        private static void SubscribeSceneLoaded()
        {
            if (_sceneLoadedSubscribed)
            {
                return;
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            _sceneLoadedSubscribed = true;
        }

        private static void UnsubscribeSceneLoaded()
        {
            if (!_sceneLoadedSubscribed)
            {
                return;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            _sceneLoadedSubscribed = false;
        }

        private sealed class FuseEarlyLoaderRunner : MonoBehaviour
        {
        }
    }
}
