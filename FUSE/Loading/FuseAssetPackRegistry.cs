using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AssetPack.Runtime;
using HarmonyLib;
using Model.Definition;
using Model.Database;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Loading
{
    internal static partial class FuseAssetPackRegistry
    {
        private const string FuseAssetPacksProperty = "FuseAssetPacks";
        private const string DirectStorePrefix = "fuseasset://";

        private static readonly HashSet<string> SupportedDefinitionComponentKinds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "AggregateLoadModel",
                "Bell",
                "PrefabControl",
                "FireboxEffect",
                "Chuff",
                "ClassLight",
                "Colorizer",
                "Compressor",
                "CylinderCock",
                "Decal",
                "DetailModel",
                "DieselExhaust",
                "Dynamo",
                "Gauge",
                "Headlight",
                "Horn",
                "Ladder",
                "LightFixture",
                "LoadAnimation",
                "LoadModel",
                "LoadTarget",
                "MapMask",
                "RectangleMapMask",
                "CircleMapMask",
                "MarkerLight",
                "RadialControl",
                "Seat",
                "ToolshedFlowOrigin",
                "ToolshedServiceLoadPoint",
                "ToolshedServiceStorage",
                "ToggleAnimation",
                "ToggleControl",
                "Whistle"
            };

        private static bool _mountComplete;
        private static readonly Dictionary<string, string> MountedAssetPackSourcesById =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> DirectAssetPackStoreIdentifiers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> SanitizedDirectContainerWarnings =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object LegacyAssetPackAliasLock = new object();
        private static Dictionary<string, string> LegacyAssetPackAliases;

        // Catalog.json inspection failures, surfaced to the user as load-report
        // notices. Kept beside the alias cache (not in FuseLoadReport's map-scoped
        // registries) because the aliases are built lazily ONCE per mount: a later
        // map load in the same session clears the report's notices without
        // re-running the inspection, so the report re-reads this list on every
        // snapshot instead.
        private static readonly object CatalogInspectionFailureLock = new object();
        private static readonly List<string> CatalogInspectionFailures = new List<string>();

        internal static string[] GetCatalogInspectionFailures()
        {
            lock (CatalogInspectionFailureLock)
            {
                return CatalogInspectionFailures.ToArray();
            }
        }

        private static void RecordCatalogInspectionFailure(string message)
        {
            lock (CatalogInspectionFailureLock)
            {
                if (!CatalogInspectionFailures.Contains(message))
                {
                    CatalogInspectionFailures.Add(message);
                }
            }
        }
        private static readonly FieldInfo RuntimeStoreContainerField =
            AccessTools.Field(typeof(AssetPackRuntimeStore), "_container");

        // The direct-mount sanitize path used to call ContainerSerialization.Deserialize,
        // but old-loader plugins (notably LegosLibraryOfStuff) install a Harmony postfix
        // on that public entry point and mutate shared component instances on every call.
        // The game already deserializes each pack once via its native path, so re-entering
        // the public method here re-fires the postfix on the same identifiers and breaks
        // per-car ComponentGroup toggles (e.g. ERIE LOGO going dead). We deserialize
        // through Newtonsoft directly using the same settings the public method would have
        // used. See FuseLegacyContainerMixintoRegistry for the same bypass on the per-item
        // mixinto re-deserialize.
        private static readonly MethodInfo ContainerSerializerSettingsMethod =
            AccessTools.Method(typeof(ContainerSerialization), "JsonSerializerSettings");

        public static int MountAllAvailableAssetPacks()
        {
            if (_mountComplete)
            {
                return 0;
            }

            if (!FuseSettings.MirrorAssetPacksToLocalLow)
            {
                _mountComplete = true;
                FuseLog.Info("FUSE skipped LocalLow asset pack mirror because direct asset pack stores are the default.");
                return 0;
            }

            var modsRoot = FuseDataPackageDiscovery.GetModsRoot();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                FuseLog.Warning("FUSE could not locate the Unity Mod Manager Mods folder for asset pack discovery.");
                return 0;
            }

            var mountedCount = 0;
            foreach (var packagePath in Directory.GetDirectories(modsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!ShouldInspectPackage(packagePath))
                {
                    continue;
                }

                try
                {
                    mountedCount += MountAssetPacksFromPackage(packagePath);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE failed to mount asset packs from '{packagePath}'", ex);
                }
            }

            _mountComplete = true;
            if (mountedCount > 0)
            {
                FuseLog.Info($"FUSE mounted {mountedCount} asset pack(s).");
            }

            return mountedCount;
        }

        public static void Reset()
        {
            if (!_mountComplete)
            {
                return;
            }

            _mountComplete = false;
            MountedAssetPackSourcesById.Clear();
            DirectAssetPackStoreIdentifiers.Clear();
            lock (LegacyAssetPackAliasLock)
            {
                LegacyAssetPackAliases = null;
            }

            lock (CatalogInspectionFailureLock)
            {
                CatalogInspectionFailures.Clear();
            }
            FuseLegacyContainerMixintoRegistry.Reset();
            FuseAssetCollisionRegistry.Reset();
            FUSE.Runtime.API.SceneryAPI.InvalidateKnownSceneryIdentifierIndex();
            FuseLog.Info("FUSE asset pack mount state reset.");
        }

        internal static FuseAssetPackDiagnostics GetDiagnostics()
        {
            var folders = EnumerateAvailableAssetPackFolders().ToArray();
            var firstSourceByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var duplicateKeys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var failedLookups = new List<string>();

            foreach (var folder in folders)
            {
                var definitionsPath = Path.Combine(folder, "Definitions.json");
                if (!File.Exists(definitionsPath))
                {
                    continue;
                }

                try
                {
                    var root = FuseLegacyDataConverter.ReadLegacyObject(definitionsPath);
                    foreach (var obj in (root["objects"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                    {
                        var key =
                            GetStringProperty(obj, "name") ??
                            GetStringProperty(obj, "identifier") ??
                            GetStringProperty(obj, "id") ??
                            GetStringProperty(obj["definition"] as JObject, "name") ??
                            GetStringProperty(obj["definition"] as JObject, "identifier") ??
                            GetStringProperty(obj["definition"] as JObject, "id");
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            continue;
                        }

                        if (!firstSourceByKey.TryGetValue(key, out var existing))
                        {
                            firstSourceByKey[key] = folder;
                            continue;
                        }

                        if (!duplicateKeys.TryGetValue(key, out var sources))
                        {
                            sources = new List<string> { existing };
                            duplicateKeys[key] = sources;
                        }

                        if (!sources.Contains(folder, StringComparer.OrdinalIgnoreCase))
                        {
                            sources.Add(folder);
                        }
                    }
                }
                catch (Exception ex)
                {
                    failedLookups.Add($"{Path.GetFileName(folder)}: {ex.Message}");
                }
            }

            return new FuseAssetPackDiagnostics
            {
                StoreFolders = folders,
                UniqueAssetKeys = firstSourceByKey.Count,
                DuplicateKeys = duplicateKeys
                    .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new FuseDuplicateAssetKey
                    {
                        Key = item.Key,
                        Sources = item.Value.Select(Path.GetFileName).ToArray()
                    })
                    .ToArray(),
                FailedDefinitionLoads = failedLookups.ToArray()
            };
        }
    }

    internal sealed class FuseAssetPackDiagnostics
    {
        public string[] StoreFolders { get; set; } = Array.Empty<string>();
        public int UniqueAssetKeys { get; set; }
        public FuseDuplicateAssetKey[] DuplicateKeys { get; set; } = Array.Empty<FuseDuplicateAssetKey>();
        public string[] FailedDefinitionLoads { get; set; } = Array.Empty<string>();
    }

    internal sealed class FuseDuplicateAssetKey
    {
        public string Key { get; set; } = string.Empty;
        public string[] Sources { get; set; } = Array.Empty<string>();
    }
}
