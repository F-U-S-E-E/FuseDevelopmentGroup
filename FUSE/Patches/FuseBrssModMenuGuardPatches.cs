using System;
using System.Reflection;
using FUSE.Infrastructure;
using HarmonyLib;

namespace FUSE.Patches
{
    /// <summary>
    /// Defers Beemans Rolling Stock Scripts' aux-tender window creation until
    /// BRSS has a selected car. BRSS 4.0.0 handles MapDidLoad by calling
    /// ModMenu.CreateWindow before snapshot restore necessarily selects a car,
    /// then unconditionally reads _selectedCar.name while _selectedCar is null.
    /// FUSE's longer map-load pipeline makes that pre-existing timing race
    /// reproducible; Railloader's shorter path usually hides it.
    ///
    /// Installed by name because BRSS is optional. The prefix only skips the
    /// unsafe no-window/no-selection call. BRSS's SelectedCarChanged handler
    /// updates _selectedCar before calling Show(), which calls CreateWindow
    /// again and follows the original healthy path.
    /// </summary>
    internal static class FuseBrssModMenuGuardPatches
    {
        private static bool _createWindowPatched;
        private static FieldInfo _windowField;
        private static FieldInfo _selectedCarField;

        internal static bool Installed => _createWindowPatched;

        internal static string EnsureInstalled(Harmony harmony)
        {
            if (_createWindowPatched)
            {
                return "installed";
            }

            if (harmony == null)
            {
                return "unavailable (no harmony)";
            }

            var modMenuType = AccessTools.TypeByName("BRSS.Windows.ModMenu");
            if (modMenuType == null)
            {
                return "idle (not present)";
            }

            var createWindow = AccessTools.DeclaredMethod(modMenuType, "CreateWindow", Type.EmptyTypes);
            var windowField = AccessTools.Field(modMenuType, "_window");
            var selectedCarField = AccessTools.Field(modMenuType, "_selectedCar");
            if (createWindow == null || windowField == null || selectedCarField == null)
            {
                return "idle (surface changed)";
            }

            _windowField = windowField;
            _selectedCarField = selectedCarField;
            harmony.Patch(
                createWindow,
                prefix: new HarmonyMethod(
                    typeof(FuseBrssModMenuGuardPatches),
                    nameof(CreateWindowPrefix)));
            _createWindowPatched = true;
            return "installed";
        }

        internal static bool ShouldRunOriginal(bool hasWindow, bool hasSelectedCar)
        {
            return hasWindow || hasSelectedCar;
        }

        private static bool CreateWindowPrefix(object __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return true;
                }

                var hasWindow = _windowField.GetValue(__instance) != null;
                var hasSelectedCar = _selectedCarField.GetValue(__instance) != null;
                if (ShouldRunOriginal(hasWindow, hasSelectedCar))
                {
                    return true;
                }

                var deferred = FuseRuntimeGuardCounters.RecordBrssModMenuDeferred();
                if (FuseGuardLog.ShouldLog(deferred))
                {
                    FuseLog.Warning(
                        $"FUSE deferred BRSS aux-tender mod-menu window creation #{deferred}: " +
                        "MapDidLoad arrived before BRSS had a selected car, and BRSS 4.0.0 would " +
                        "dereference _selectedCar.name in this state. BRSS will create the window " +
                        "after a compatible car is selected.");
                }

                return false;
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE BRSS mod-menu guard failed; letting BRSS CreateWindow run unchanged",
                    ex);
                return true;
            }
        }
    }
}
