using System;
using System.IO;
using Newtonsoft.Json;
using UnityModManagerNet;

namespace FUSE.TestBridge
{
    /// <summary>
    /// UMM entry point for the optional, dev-only test bridge. It stays inert unless
    /// explicitly enabled — either the environment variable <c>FUSE_TEST_BRIDGE=1</c> or
    /// <c>"Enabled": true</c> in this mod's Info.json — so it can never affect a player
    /// even if the DLL lands in their Mods folder. A normal build also never deploys it
    /// (EnableTestBridgeDeploy defaults false).
    /// </summary>
    public static class Main
    {
        public static UnityModManager.ModEntry ModEntry { get; private set; }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            try
            {
                if (!IsEnabled(modEntry))
                {
                    modEntry.Logger.Log(
                        "FUSE.TestBridge present but disabled. Set FUSE_TEST_BRIDGE=1 or \"Enabled\": true " +
                        "in its Info.json to enable (dev only).");
                    return true;
                }

                FuseTestBridgeHost.Ensure(modEntry.Path);
                modEntry.Logger.Log("FUSE.TestBridge enabled; watching its folder for test requests.");
                return true;
            }
            catch (Exception ex)
            {
                modEntry.Logger.Error("FUSE.TestBridge failed to load: " + ex);
                return false;
            }
        }

        private static bool IsEnabled(UnityModManager.ModEntry modEntry)
        {
            if (string.Equals(Environment.GetEnvironmentVariable("FUSE_TEST_BRIDGE"), "1", StringComparison.Ordinal))
            {
                return true;
            }

            try
            {
                var infoPath = Path.Combine(modEntry.Path, "Info.json");
                if (File.Exists(infoPath))
                {
                    var info = JsonConvert.DeserializeObject<EnableFlag>(File.ReadAllText(infoPath));
                    return info != null && info.Enabled;
                }
            }
            catch
            {
                // A malformed or locked Info.json just leaves the bridge disabled.
            }

            return false;
        }

        // Newtonsoft matches the JSON "Enabled" field to this property by name
        // (case-insensitive); other Info.json fields are ignored.
        private sealed class EnableFlag
        {
            public bool Enabled { get; set; }
        }
    }
}
