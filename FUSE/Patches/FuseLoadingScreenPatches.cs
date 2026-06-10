using System;
using System.Collections;
using System.Reflection;
using Game.State;
using HarmonyLib;
using Model.Ops;
using UI.Menu;
using UnityEngine;
using FUSE.Infrastructure;
using FUSE.Interface;

namespace FUSE.Patches
{
    // Drives the FUSE enhanced loading screen (issue #83) from the game's load
    // sequence. Kept separate from FuseLoadingScreen so the feature is one cohesive
    // unit, and deliberately implemented as INDEPENDENT patch classes on methods
    // FUSE already patches elsewhere (PopulateFromRemoteSnapshot, HandleSnapshot*,
    // OpsController.Awake) — Harmony allows multiple patches per method, so these
    // coexist with the existing snapshot patches without editing those files.
    //
    // Every body is gated on FuseSettings.EnableEnhancedLoadingScreen and wrapped in
    // try/catch: none of them alter game behavior or control flow — they only push a
    // label or a progress value and return. A throw here can never wedge a load.
    internal static class FuseLoadingScreenPatchSupport
    {
        internal static int CountOf(object collection)
        {
            return collection is ICollection countable ? countable.Count : -1;
        }
    }

    [HarmonyPatch(typeof(PersistentLoader), "ShowLoadingScreen")]
    internal static class FuseLoadingScreenShowPatch
    {
        private static readonly FieldInfo LoadingScreenField =
            AccessTools.Field(typeof(PersistentLoader), "loadingScreen");

        private static void Postfix(bool show, PersistentLoader __instance)
        {
            if (!FuseSettings.EnableEnhancedLoadingScreen)
            {
                return;
            }

            try
            {
                if (__instance != null && LoadingScreenField != null)
                {
                    FuseLoadingScreen.CaptureStockLoadingScreen(LoadingScreenField.GetValue(__instance) as GameObject);
                }

                if (!show)
                {
                    FuseLoadingScreen.NotifyGameScreenHidden();
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning("FUSE loading-screen ShowLoadingScreen hook failed: " + ex.GetBaseException().Message);
            }
        }
    }

    [HarmonyPatch(typeof(PersistentLoader), "ShowProgress")]
    internal static class FuseLoadingScreenProgressPatch
    {
        private static void Postfix(int intProgress, string text)
        {
            if (!FuseSettings.EnableEnhancedLoadingScreen)
            {
                return;
            }

            try
            {
                FuseLoadingScreen.SetProgressFromGame(intProgress, text);
            }
            catch (Exception ex)
            {
                FuseLog.Warning("FUSE loading-screen ShowProgress hook failed: " + ex.GetBaseException().Message);
            }
        }
    }

    [HarmonyPatch(typeof(StateManager), "PopulateFromRemoteSnapshot")]
    internal static class FuseLoadingScreenSnapshotPatch
    {
        private static void Prefix()
        {
            if (!FuseSettings.EnableEnhancedLoadingScreen)
            {
                return;
            }

            try
            {
                FuseLoadingScreen.SetStep("Restoring saved game", "Reading world snapshot");
            }
            catch (Exception ex)
            {
                FuseLog.Warning("FUSE loading-screen snapshot hook failed: " + ex.GetBaseException().Message);
            }
        }
    }

    [HarmonyPatch(typeof(TrainController), "HandleSnapshotTurntables")]
    internal static class FuseLoadingScreenTurntablesPatch
    {
        private static void Prefix(object states)
        {
            if (!FuseSettings.EnableEnhancedLoadingScreen)
            {
                return;
            }

            try
            {
                var count = FuseLoadingScreenPatchSupport.CountOf(states);
                FuseLoadingScreen.SetStep("Restoring turntables", FuseLoadingLabels.DescribeCount(count, "turntable", "turntables"));
            }
            catch (Exception ex)
            {
                FuseLog.Warning("FUSE loading-screen turntables hook failed: " + ex.GetBaseException().Message);
            }
        }
    }

    [HarmonyPatch(typeof(TrainController), "HandleSnapshotCars")]
    internal static class FuseLoadingScreenCarsPatch
    {
        private static void Prefix(object snapshotCars)
        {
            if (!FuseSettings.EnableEnhancedLoadingScreen)
            {
                return;
            }

            try
            {
                var count = FuseLoadingScreenPatchSupport.CountOf(snapshotCars);
                FuseLoadingScreen.SetStep("Restoring rail cars", FuseLoadingLabels.DescribeCount(count, "car", "cars"));
            }
            catch (Exception ex)
            {
                FuseLog.Warning("FUSE loading-screen cars hook failed: " + ex.GetBaseException().Message);
            }
        }
    }

    [HarmonyPatch(typeof(OpsController), "Awake")]
    internal static class FuseLoadingScreenIndustriesPatch
    {
        private static void Prefix()
        {
            if (!FuseSettings.EnableEnhancedLoadingScreen)
            {
                return;
            }

            try
            {
                FuseLoadingScreen.SetStep("Restoring industries", null);
            }
            catch (Exception ex)
            {
                FuseLog.Warning("FUSE loading-screen industries hook failed: " + ex.GetBaseException().Message);
            }
        }
    }
}
