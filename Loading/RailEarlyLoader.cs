using System;
using System.Collections;
using System.Collections.Generic;
using RAIL.Infrastructure;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RAIL.Loading
{
    [Experimental("Early scene-path suppression gates Unity scene activation and may wedge scene loads if Unity or another mod misbehaves.")]
    public static class RailEarlyLoader
    {
        private static readonly HashSet<int> GatedOperations = new HashSet<int>();

        private static RailEarlyLoaderRunner _runner;
        private static bool _initialized;
        private static bool _primedDefinitions;
        private static bool _gateDisabledForSession;
        private static bool _sceneLoadedSubscribed;
        private static bool _patchAvailable;

        public static bool IsEnabled => RailSettings.EnableExperimentalEarlyScenePathSuppression;

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
                RailLog.Info("RAIL experimental early scene-path suppression is disabled in Info.json settings.");
                return;
            }

            if (!_patchAvailable)
            {
                RailLog.Warning("RAIL [Experimental] early scene-path suppression is enabled, but the Harmony patch is unavailable. Scene-path suppression will no-op; normal loading continues.");
                return;
            }

            EnsureRunner();
            SubscribeSceneLoaded();
            RailLog.Warning(
                "RAIL [Experimental] early scene-path suppression is enabled. " +
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

            RailWorldSuppressor.ApplyActiveScenePathSuppressions("map load fallback");
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

            if (!RailWorldSuppressor.HasActiveScenePathSuppressions)
            {
                return;
            }

            RailExperimentalLog.WarnFirstUse(
                "RAIL.Loading.RailEarlyLoader.TryGateSceneLoad",
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
                RailLog.Info($"RAIL [Experimental] gated scene activation for '{sceneName ?? "<unknown>"}' from '{source ?? "unknown"}'.");
            }
            catch (Exception ex)
            {
                GatedOperations.Remove(operationId);
                TryReleaseOperation(operation, $"gate setup failure for '{sceneName ?? "<unknown>"}'");
                RailLog.Warning($"RAIL [Experimental] failed to gate scene load '{sceneName ?? "<unknown>"}'; loading will continue normally: {ex.Message}");
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
                RailDataPackageDiscovery.LoadPackagesFromDisk(false);
                RailWorldSuppressor.RegisterEarlyScenePathSuppressionsFromLoadedDefinitions(reason ?? "early window");
                _primedDefinitions = true;
                return true;
            }
            catch (Exception ex)
            {
                RailLog.Warning($"RAIL [Experimental] early scene-path suppression could not prime package definitions; scene-path suppression will no-op: {ex.Message}");
                return false;
            }
        }

        private static IEnumerator GateRoutine(AsyncOperation operation, string sceneName, string source, int operationId)
        {
            var start = Time.realtimeSinceStartup;
            var timeout = RailSettings.ExperimentalEarlyScenePathSuppressionTimeoutSeconds;

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
                RailLog.Error(
                    $"RAIL [Experimental] early scene gate timed out after {timeout:0.0}s for scene '{sceneName}'. " +
                    "Released activation immediately and disabled additional early gates for this session.");
                yield break;
            }

            RailWorldSuppressor.ApplyActiveScenePathSuppressions($"early gate before activation scene='{sceneName}'");
            TryReleaseOperation(operation, $"ready scene='{sceneName}' source='{source}'");

            while (operation != null && !operation.isDone)
            {
                yield return null;
            }

            GatedOperations.Remove(operationId);
            yield return null;
            RailWorldSuppressor.ApplyActiveScenePathSuppressions($"post-activation scene='{sceneName}'");
        }

        private static void OnGatedOperationCompleted(AsyncOperation operation)
        {
            try
            {
                RailWorldSuppressor.ApplyActiveScenePathSuppressions("async scene completed");
            }
            catch (Exception ex)
            {
                RailLog.Warning($"RAIL [Experimental] failed during post-complete scene-path suppression: {ex.Message}");
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
                RailWorldSuppressor.ApplyActiveScenePathSuppressions($"sceneLoaded '{scene.name}'");
            }
            catch (Exception ex)
            {
                RailLog.Warning($"RAIL [Experimental] sceneLoaded suppression failed for scene '{scene.name}': {ex.Message}");
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
                RailLog.Info($"RAIL [Experimental] released scene activation gate for {reason ?? "unspecified"}.");
            }
            catch (Exception ex)
            {
                RailLog.Warning($"RAIL [Experimental] failed to release scene activation gate for {reason ?? "unspecified"}: {ex.Message}");
            }
        }

        private static void EnsureRunner()
        {
            if (_runner != null)
            {
                return;
            }

            var go = new GameObject("RAIL.EarlyLoader");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<RailEarlyLoaderRunner>();
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

        private sealed class RailEarlyLoaderRunner : MonoBehaviour
        {
        }
    }
}
