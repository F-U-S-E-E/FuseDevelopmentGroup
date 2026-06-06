using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using FUSE.Infrastructure;
using FUSE.Loading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FUSE.Patches
{
    [Experimental("Experimental early scene-path suppression patch. Failure is non-fatal through FusePatchResilience.")]
    [HarmonyPatch]
    internal static class FuseEarlyLoaderSceneManagerPatch
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
                FuseEarlyLoader.TryGateSceneLoad(sceneName, __result, "SceneManager.LoadSceneAsync");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE [Experimental] early scene loader patch failed; scene loading will continue normally", ex);
            }
        }
    }
}
