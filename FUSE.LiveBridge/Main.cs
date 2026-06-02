using System;
using UnityModManagerNet;

namespace FUSE.LiveBridge
{
    /// <summary>
    /// UMM entry point for the optional in-game live-reload bridge. Spawns a
    /// persistent <see cref="FuseLiveBridgeBehaviour"/> that watches the Mods
    /// folder for the external editor's reload commands.
    /// </summary>
    public static class Main
    {
        public static UnityModManager.ModEntry ModEntry { get; private set; }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            try
            {
                FuseLiveBridgeHost.Ensure(modEntry.Path);
                modEntry.Logger.Log("FUSE.LiveBridge loaded; watching Mods for editor reload commands.");
                return true;
            }
            catch (Exception ex)
            {
                modEntry.Logger.Error("FUSE.LiveBridge failed to load: " + ex);
                return false;
            }
        }
    }
}
