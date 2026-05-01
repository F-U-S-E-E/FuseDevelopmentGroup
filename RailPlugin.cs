using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RAIL.Console;
using RAIL.Events;
using RAIL.Infrastructure;
using RAIL.Lifecycle;
using RAIL.Loading;
using RAIL.Patches;
using UnityModManagerNet;

namespace RAIL
{
    public static class RailPlugin
    {
        private const string HarmonyId = "RAIL";

        private static Harmony _harmony;
        private static bool _isLoaded;
        private static RailLifecycle _lifecycle;

        public static UnityModManager.ModEntry ModEntry { get; private set; }

        public static bool IsLoaded => _isLoaded;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            RailLog.Initialize(modEntry?.Logger);

            if (modEntry == null)
            {
                RailLog.Error("RAIL failed to load because Unity Mod Manager did not provide a mod entry.");
                return false;
            }

            if (_isLoaded)
            {
                RailLog.Warning("RAIL Load was called while RAIL is already loaded; ignoring duplicate load request.");
                return true;
            }

            try
            {
                RailSettings.Load(modEntry);
                RailAssetPackRegistry.MountAllAvailableAssetPacks();

                _harmony = new Harmony(HarmonyId);
                RailPatchResilience.ApplyAll(_harmony, Assembly.GetExecutingAssembly());
                RailEarlyLoader.SetPatchAvailable(RailPatchResilience.Applied.Any(patch =>
                    string.Equals(patch.TypeName, "RAIL.Patches.RailEarlyLoaderSceneManagerPatch", StringComparison.Ordinal)));
                _lifecycle = new RailLifecycle();
                _lifecycle.Register();
                // Console handler may not exist yet during early load; the lifecycle
                // re-attempts registration on the first map load.
                RailConsoleRegistrar.TryRegisterAll();

                modEntry.OnUnload = OnUnload;
                _isLoaded = true;
                RailEvents.RaiseRailLoaded();
                RailLog.Info("RAIL loaded.");
                return true;
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL failed to load", ex);
                Shutdown();
                return false;
            }
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            Shutdown();
            return true;
        }

        private static void Shutdown()
        {
            if (_harmony != null)
            {
                try
                {
                    _harmony.UnpatchAll(HarmonyId);
                }
                catch (Exception ex)
                {
                    RailLog.Exception("RAIL failed while unpatching Harmony hooks during shutdown", ex);
                }

                _harmony = null;
            }

            if (_lifecycle != null)
            {
                try
                {
                    _lifecycle.Unregister();
                }
                catch (Exception ex)
                {
                    RailLog.Exception("RAIL failed while unregistering lifecycle handlers during shutdown", ex);
                }

                _lifecycle = null;
            }

            if (_isLoaded)
            {
                RailEvents.RaiseRailUnloaded();
                RailLog.Info("RAIL unloaded.");
            }

            _isLoaded = false;
            ModEntry = null;
        }
    }
}
