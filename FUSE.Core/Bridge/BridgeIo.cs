using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Fuse.Core.Bridge
{
    /// <summary>
    /// Atomic JSON read/write for the live-reload channel files. Writes go to a
    /// same-directory temp file then rename over the target so a reader never
    /// sees a half-written file; reads are tolerant (return null on any error).
    /// </summary>
    public static class BridgeIo
    {
        private static JsonSerializerSettings CreateSettings() => new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
        };

        public static string BridgeDir(string packageDir) =>
            Path.Combine(packageDir, BridgeProtocol.BridgeSubfolder);

        public static string CommandPath(string packageDir) =>
            Path.Combine(BridgeDir(packageDir), BridgeProtocol.CommandFileName);

        /// <summary>Heartbeat path under a game Mods directory: <c>Mods/FUSE.LiveBridge/bridge_state.json</c>.</summary>
        public static string HeartbeatPath(string gameModsDir) =>
            Path.Combine(gameModsDir, BridgeProtocol.BridgeModFolderName, BridgeProtocol.StateFileName);

        public static void WriteAtomic(string path, object value)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(tmp, JsonConvert.SerializeObject(value, CreateSettings()));
            try
            {
                if (File.Exists(path))
                {
                    File.Replace(tmp, path, null);
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
            catch
            {
                // Fallback for filesystems where Replace isn't supported.
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(tmp, path);
            }
        }

        public static T TryRead<T>(string path)
            where T : class
        {
            try
            {
                return File.Exists(path)
                    ? JsonConvert.DeserializeObject<T>(File.ReadAllText(path), CreateSettings())
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
