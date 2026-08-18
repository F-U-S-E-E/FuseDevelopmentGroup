using System;
using System.Reflection;
using FUSE.Infrastructure;
using HarmonyLib;
using Model;

namespace FUSE.Patches
{
    /// <summary>
    /// Realistic Rerail 0.1.1 creates its window before a crane car is selected.
    /// Its first Populate call passes null to CountCoupledMowCars, which
    /// immediately dereferences the car. Treating "no selected crane" as zero
    /// coupled MOW cars lets the window finish registering and preserves the
    /// mod's normal calculation as soon as a crane is selected.
    /// </summary>
    internal static class FuseRealisticRerailCraneGuardPatches
    {
        private static bool _countGuardInstalled;
        private static bool _windowGuardInstalled;

        private static PropertyInfo _builderAssetsProperty;

        internal static bool Installed => _countGuardInstalled || _windowGuardInstalled;

        internal static string EnsureInstalled(Harmony harmony)
        {
            if (_countGuardInstalled && _windowGuardInstalled)
            {
                return "installed";
            }

            if (harmony == null)
            {
                return "unavailable (no harmony)";
            }

            var helperType = AccessTools.TypeByName("RealisticRerail.CraneCarHelper");
            if (helperType == null)
            {
                return "idle (not present)";
            }

            var countTarget = AccessTools.DeclaredMethod(
                helperType,
                "CountCoupledMowCars",
                new[] { typeof(Car) });
            if (!_countGuardInstalled && countTarget != null && countTarget.ReturnType == typeof(int))
            {
                harmony.Patch(
                    countTarget,
                    prefix: new HarmonyMethod(
                        typeof(FuseRealisticRerailCraneGuardPatches),
                        nameof(CountCoupledMowCarsPrefix)));
                _countGuardInstalled = true;
            }

            var windowType = AccessTools.TypeByName("RealisticRerail.RerailWindow");
            var onEnableTarget = windowType == null
                ? null
                : AccessTools.DeclaredMethod(windowType, "OnEnable", Type.EmptyTypes);
            _builderAssetsProperty = _builderAssetsProperty ??
                                     (windowType == null
                                         ? null
                                         : AccessTools.Property(windowType, "BuilderAssets"));
            if (!_windowGuardInstalled && onEnableTarget != null && _builderAssetsProperty != null)
            {
                harmony.Patch(
                    onEnableTarget,
                    prefix: new HarmonyMethod(
                        typeof(FuseRealisticRerailCraneGuardPatches),
                        nameof(OnEnablePrefix)));
                _windowGuardInstalled = true;
            }

            if (Installed)
            {
                FuseLog.Info(
                    "FUSE installed Realistic Rerail's startup guards for the initial " +
                    "unconfigured window and no-selected-crane state.");
                return "installed";
            }

            return "idle (surface changed)";
        }

        internal static bool ShouldRunCountCoupledMowCars(bool hasCraneCar)
        {
            return hasCraneCar;
        }

        internal static bool ShouldPopulateOnEnable(bool hasBuilderAssets)
        {
            return hasBuilderAssets;
        }

        private static bool CountCoupledMowCarsPrefix(Car craneCar, ref int __result)
        {
            if (ShouldRunCountCoupledMowCars(craneCar != null))
            {
                return true;
            }

            __result = 0;
            return false;
        }

        private static bool OnEnablePrefix(object __instance)
        {
            object builderAssets = null;
            try
            {
                builderAssets = _builderAssetsProperty?.GetValue(__instance, null);
            }
            catch
            {
                // If the third-party surface changes, let its original method run
                // rather than hiding an unrelated failure.
                return true;
            }

            // ProgrammaticWindowCreator.AddComponent invokes OnEnable before it
            // assigns IBuilderWindow.BuilderAssets. The window is closed directly
            // afterward, then its own Toggle path repopulates it after the assets
            // have been assigned and a crane has been selected.
            return ShouldPopulateOnEnable(builderAssets != null);
        }
    }
}
