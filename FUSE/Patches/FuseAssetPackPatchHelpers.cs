using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AssetPack.Runtime;
using HarmonyLib;
using Model.Definition;
using Model.Definition.Data;
using Model.Database;
using FUSE.Infrastructure;
using FUSE.Loading;

namespace FUSE.Patches
{

    /// <summary>
    /// Helpers shared between the AssetBundlePath redirect patch and the
    /// LoadedBundle delegation patch. Centralizes the reflection cache so
    /// we only resolve <c>BasePath</c> /
    /// <c>LoadedBundle</c> / the <c>_stores</c> field once. Also owns the
    /// one-shot diagnostic-log dedup state and the path-shortening
    /// formatter so both can be exercised from unit tests without
    /// constructing a live Unity store.
    /// </summary>
    internal static class FuseAssetPackPatchHelpers
    {
        private static System.Reflection.PropertyInfo _basePathProperty;
        private static System.Reflection.MethodInfo _loadedBundleMethod;
        private static System.Reflection.FieldInfo _prefabStoreStoresField;

        // First-fire tracking for diagnostic logs. The LoadedBundle
        // patch fires every time a colliding loser store's bundle is
        // requested — which is many times per session per pack — so we
        // dedupe by loser folder and emit each outcome once per process
        // lifetime. Exposed as internal so tests can drive the dedup
        // logic directly and reset between cases.
        private static readonly System.Collections.Generic.HashSet<string> LoggedLoserRedirects =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        private static readonly object LoggedLoserRedirectsSync = new object();

        /// <summary>
        /// Returns true the first time the (loserFolder) tuple is seen
        /// this process lifetime, false on every subsequent call. Used
        /// by the LoadedBundle Prefix to log each redirect's outcome
        /// exactly once.
        /// </summary>
        public static bool ShouldLogLoserRedirectOnce(string loserFolder)
        {
            var key = loserFolder ?? "<null>";
            lock (LoggedLoserRedirectsSync)
            {
                return LoggedLoserRedirects.Add(key);
            }
        }

        /// <summary>
        /// Resets the one-shot redirect-log dedup state. Provided so
        /// tests can run case-after-case against a deterministic
        /// initial state; production callers never invoke this.
        /// </summary>
        public static void ResetLoggedLoserRedirects()
        {
            lock (LoggedLoserRedirectsSync)
            {
                LoggedLoserRedirects.Clear();
            }
        }

        /// <summary>
        /// Formats an absolute path for log lines by keeping only the
        /// last three path components prefixed with <c>".../"</c>. Empty
        /// or null input becomes <c>"&lt;unknown&gt;"</c>. Short paths
        /// (two segments or fewer) are returned unchanged. Designed for
        /// human readability in the FUSE log — long paths through deeply
        /// nested mods become "FUSE asset-collision redirect on
        /// '.../TOFC Cars/SCAssetPacks/spinecar1' -> winner '.../...'"
        /// rather than the full 70+ character absolute paths.
        /// </summary>
        public static string ShortenForLog(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return "<unknown>";
            }
            try
            {
                var parts = fullPath.Split(
                    new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar },
                    System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length <= 2)
                {
                    return fullPath;
                }
                return ".../" + string.Join("/", parts.Skip(parts.Length - 3));
            }
            catch
            {
                return fullPath;
            }
        }

        public static string ResolveBasePath(AssetPackRuntimeStore store)
        {
            if (store == null)
            {
                return null;
            }
            var property = _basePathProperty
                           ?? (_basePathProperty = AccessTools.Property(typeof(AssetPackRuntimeStore), "BasePath"));
            return property?.GetValue(store, null) as string;
        }

        // Cached reflection handle for the private _loadAssetBundleTask
        // field on AssetPackRuntimeStore. Cached on first use; the field
        // signature is stable across Unity versions and pack-runtime
        // builds so a single MissingFieldException would be a strong
        // signal that the host SDK changed (handle by falling back to
        // null, which makes the LoadedBundle dedup degrade gracefully).
        private static System.Reflection.FieldInfo _loadAssetBundleTaskField;

        private static System.Reflection.FieldInfo GetLoadAssetBundleTaskField()
        {
            return _loadAssetBundleTaskField
                   ?? (_loadAssetBundleTaskField =
                       AccessTools.Field(typeof(AssetPackRuntimeStore), "_loadAssetBundleTask"));
        }

        /// <summary>
        /// Reads the existing cached <c>_loadAssetBundleTask</c> off a
        /// store. The original <c>LoadedBundle</c> uses this field to
        /// avoid issuing a second <c>LoadFromFileAsync</c> for the same
        /// store; mirroring it in the Prefix lets us hand back the same
        /// task on subsequent calls without re-running the CAB dedup.
        /// Returns null when the field is missing, the store is null, or
        /// the cached value is not yet set.
        /// </summary>
        public static System.Threading.Tasks.Task<UnityEngine.AssetBundle> GetCachedLoadAssetBundleTask(
            AssetPackRuntimeStore store)
        {
            if (store == null)
            {
                return null;
            }

            try
            {
                var field = GetLoadAssetBundleTaskField();
                return field?.GetValue(store) as System.Threading.Tasks.Task<UnityEngine.AssetBundle>;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Stamps a resolved <c>Task&lt;AssetBundle&gt;</c> onto the
        /// store's private cache field so the next call to the original
        /// <c>LoadedBundle</c> returns it directly. Best-effort; on
        /// reflection failure the call silently no-ops (the dedup
        /// outcome is still correct because the Prefix re-checks the
        /// cache on every entry).
        /// </summary>
        public static void SetCachedLoadAssetBundleTask(
            AssetPackRuntimeStore store,
            System.Threading.Tasks.Task<UnityEngine.AssetBundle> task)
        {
            if (store == null || task == null)
            {
                return;
            }

            try
            {
                var field = GetLoadAssetBundleTaskField();
                field?.SetValue(store, task);
            }
            catch
            {
                // Field-set failure means the original LoadedBundle will
                // re-enter the Prefix and re-run the CAB dedup, which
                // still produces the correct result — just slower.
            }
        }

        /// <summary>
        /// Reads the embedded CAB-&lt;hex&gt; name from the first bytes
        /// of a Unity AssetBundle file. The bundle file format
        /// (<c>UnityFS</c>, the only one Railroader emits) prefixes its
        /// SerializedFile metadata with a null-terminated ASCII string
        /// of the form <c>CAB-&lt;guid-hex&gt;</c> within the first ~128
        /// bytes, even when block data is compressed; Unity uses the
        /// embedded name (not the file path) as the bundle's identity
        /// for the "already loaded" check. Returns null when the file
        /// is missing, unreadable, smaller than the header window, or
        /// when the header does not start with <c>UnityFS</c>.
        /// </summary>
        public static string TryReadBundleCabName(string bundlePath)
        {
            if (string.IsNullOrWhiteSpace(bundlePath))
            {
                return null;
            }

            try
            {
                if (!System.IO.File.Exists(bundlePath))
                {
                    return null;
                }

                // 512 bytes is comfortably larger than every CAB-name
                // offset I've measured (typically ~58-70). Bundles
                // smaller than this header are almost certainly not
                // valid UnityFS bundles anyway.
                var buffer = new byte[512];
                int read;
                using (var stream = System.IO.File.OpenRead(bundlePath))
                {
                    read = stream.Read(buffer, 0, buffer.Length);
                }

                if (read < 16)
                {
                    return null;
                }

                // Quick signature check; saves us from scanning garbage
                // bytes on a non-AssetBundle file that happens to share
                // the .Bundle name extension.
                if (buffer[0] != (byte)'U' || buffer[1] != (byte)'n' ||
                    buffer[2] != (byte)'i' || buffer[3] != (byte)'t' ||
                    buffer[4] != (byte)'y' || buffer[5] != (byte)'F' ||
                    buffer[6] != (byte)'S')
                {
                    return null;
                }

                for (var i = 0; i < read - 5; i++)
                {
                    if (buffer[i] != (byte)'C' || buffer[i + 1] != (byte)'A' ||
                        buffer[i + 2] != (byte)'B' || buffer[i + 3] != (byte)'-')
                    {
                        continue;
                    }

                    var end = i + 4;
                    while (end < read && buffer[end] != 0)
                    {
                        // Only accept hex digits inside the CAB suffix
                        // so a stray "CAB-" prefix in a random data
                        // block doesn't get mistaken for the manifest
                        // name. The real CAB name is always
                        // hexadecimal.
                        var c = (char)buffer[end];
                        var isHex = (c >= '0' && c <= '9') ||
                                    (c >= 'a' && c <= 'f') ||
                                    (c >= 'A' && c <= 'F');
                        if (!isHex)
                        {
                            break;
                        }

                        end++;
                    }

                    if (end <= i + 4)
                    {
                        continue;
                    }

                    return System.Text.Encoding.ASCII.GetString(buffer, i, end - i);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public static System.Threading.Tasks.Task<UnityEngine.AssetBundle> InvokeLoadedBundle(AssetPackRuntimeStore store)
        {
            if (store == null)
            {
                return null;
            }
            var method = _loadedBundleMethod
                         ?? (_loadedBundleMethod = AccessTools.Method(typeof(AssetPackRuntimeStore), "LoadedBundle"));
            if (method == null)
            {
                return null;
            }
            return method.Invoke(store, null) as System.Threading.Tasks.Task<UnityEngine.AssetBundle>;
        }

        public static AssetPackRuntimeStore FindStoreByBasePath(string folderAbsolutePath)
        {
            if (string.IsNullOrWhiteSpace(folderAbsolutePath))
            {
                return null;
            }

            var prefabStore = TryGetSharedPrefabStore();
            if (prefabStore == null)
            {
                return null;
            }

            var storesField = _prefabStoreStoresField
                              ?? (_prefabStoreStoresField =
                                  AccessTools.Field(typeof(Model.Database.PrefabStore), "_stores"));
            if (storesField == null)
            {
                return null;
            }

            var stores = storesField.GetValue(prefabStore) as System.Collections.Generic.IEnumerable<AssetPackRuntimeStore>;
            if (stores == null)
            {
                return null;
            }

            string targetNormalized;
            try
            {
                targetNormalized = System.IO.Path.GetFullPath(folderAbsolutePath);
            }
            catch
            {
                return null;
            }

            foreach (var store in stores)
            {
                if (store == null)
                {
                    continue;
                }
                var storeBase = ResolveBasePath(store);
                if (string.IsNullOrWhiteSpace(storeBase))
                {
                    continue;
                }
                string storeNormalized;
                try
                {
                    storeNormalized = System.IO.Path.GetFullPath(storeBase);
                }
                catch
                {
                    continue;
                }
                if (string.Equals(storeNormalized, targetNormalized, StringComparison.OrdinalIgnoreCase))
                {
                    return store;
                }
            }

            return null;
        }

        private static Model.Database.PrefabStore TryGetSharedPrefabStore()
        {
            // PrefabStore is normally reached via TrainController.Shared.PrefabStore,
            // but TrainController may not yet exist when the asset pack
            // collision patches first fire. Look up reflectively so an
            // early call returns null rather than crashes.
            try
            {
                var trainControllerType = AccessTools.TypeByName("RollingStock.TrainController");
                if (trainControllerType == null)
                {
                    return null;
                }
                var sharedProp = AccessTools.Property(trainControllerType, "Shared");
                var shared = sharedProp?.GetValue(null, null);
                if (shared == null)
                {
                    return null;
                }
                var prefabStoreProp = AccessTools.Property(trainControllerType, "PrefabStore")
                                      ?? AccessTools.Property(shared.GetType(), "PrefabStore");
                return prefabStoreProp?.GetValue(shared, null) as Model.Database.PrefabStore;
            }
            catch
            {
                return null;
            }
        }
    }
}
