using System;
using System.Reflection;
using HarmonyLib;
using RAIL.Events;
using RAIL.Infrastructure;
using RAIL.Lifecycle;
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

            try
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                _lifecycle = new RailLifecycle();
                _lifecycle.Register();

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
                _harmony.UnpatchAll(HarmonyId);
                _harmony = null;
            }

            if (_lifecycle != null)
            {
                _lifecycle.Unregister();
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
