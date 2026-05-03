using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using FUSE.Console;
using FUSE.Events;
using FUSE.Infrastructure;
using FUSE.Lifecycle;
using FUSE.Loading;
using FUSE.Patches;
using UnityModManagerNet;

namespace FUSE
{
    public static class FusePlugin
    {
        private const string HarmonyId = "FUSE";

        private static Harmony _harmony;
        private static bool _isLoaded;
        private static FuseLifecycle _lifecycle;

        public static UnityModManager.ModEntry ModEntry { get; private set; }

        public static bool IsLoaded => _isLoaded;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            FuseLog.Initialize(modEntry?.Logger);

            if (modEntry == null)
            {
                FuseLog.Error("FUSE failed to load because Unity Mod Manager did not provide a mod entry.");
                return false;
            }

            if (_isLoaded)
            {
                FuseLog.Warning("FUSE Load was called while FUSE is already loaded; ignoring duplicate load request.");
                return true;
            }

            try
            {
                FuseSettings.Load(modEntry);
                FuseAssetPackRegistry.MountAllAvailableAssetPacks();

                _harmony = new Harmony(HarmonyId);
                FusePatchResilience.ApplyAll(_harmony, Assembly.GetExecutingAssembly());
                FuseEarlyLoader.SetPatchAvailable(FusePatchResilience.Applied.Any(patch =>
                    string.Equals(patch.TypeName, "FUSE.Patches.FuseEarlyLoaderSceneManagerPatch", StringComparison.Ordinal)));
                _lifecycle = new FuseLifecycle();
                _lifecycle.Register();
                // Console handler may not exist yet during early load; the lifecycle
                // re-attempts registration on the first map load.
                FuseConsoleRegistrar.TryRegisterAll();

                modEntry.OnUnload = OnUnload;
                _isLoaded = true;
                FuseEvents.RaiseFuseLoaded();
                FuseLog.Info("FUSE loaded.");
                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE failed to load", ex);
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
                    FuseLog.Exception("FUSE failed while unpatching Harmony hooks during shutdown", ex);
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
                    FuseLog.Exception("FUSE failed while unregistering lifecycle handlers during shutdown", ex);
                }

                _lifecycle = null;
            }

            if (_isLoaded)
            {
                FuseEvents.RaiseFuseUnloaded();
                FuseLog.Info("FUSE unloaded.");
            }

            _isLoaded = false;
            ModEntry = null;
        }
    }
}
