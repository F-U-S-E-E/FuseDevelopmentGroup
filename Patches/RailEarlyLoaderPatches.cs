using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RAIL.Infrastructure;
using RAIL.Loading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RAIL.Patches
{
    [Experimental("Experimental early scene-path suppression patch. Failure is non-fatal through RailPatchResilience.")]
    [HarmonyPatch]
    internal static class RailEarlyLoaderSceneManagerPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var stringMode = AccessTools.Method(typeof(SceneManager), nameof(SceneManager.LoadSceneAsync), new[] { typeof(string), typeof(LoadSceneMode) });
            if (stringMode != null)
            {
                yield return stringMode;
            }

            var stringParameters = AccessTools.Method(typeof(SceneManager), nameof(SceneManager.LoadSceneAsync), new[] { typeof(string), typeof(LoadSceneParameters) });
            if (stringParameters != null)
            {
                yield return stringParameters;
            }
        }

        private static void Postfix(string sceneName, AsyncOperation __result)
        {
            try
            {
                RailEarlyLoader.TryGateSceneLoad(sceneName, __result, "SceneManager.LoadSceneAsync");
            }
            catch (Exception ex)
            {
                RailLog.Warning($"RAIL [Experimental] early scene loader patch failed; scene loading will continue normally: {ex.Message}");
            }
        }
    }
}
