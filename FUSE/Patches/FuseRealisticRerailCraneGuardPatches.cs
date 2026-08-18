using System;
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
        private static bool _installed;

        internal static bool Installed => _installed;

        internal static string EnsureInstalled(Harmony harmony)
        {
            if (_installed)
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

            var target = AccessTools.DeclaredMethod(
                helperType,
                "CountCoupledMowCars",
                new[] { typeof(Car) });
            if (target == null || target.ReturnType != typeof(int))
            {
                return "idle (surface changed)";
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(
                    typeof(FuseRealisticRerailCraneGuardPatches),
                    nameof(CountCoupledMowCarsPrefix)));
            _installed = true;
            FuseLog.Info(
                "FUSE installed Realistic Rerail's no-selected-crane startup guard.");
            return "installed";
        }

        internal static bool ShouldRunCountCoupledMowCars(bool hasCraneCar)
        {
            return hasCraneCar;
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
    }
}
