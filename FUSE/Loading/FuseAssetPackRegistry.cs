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
        private static readonly object LateComponentRegistrationLock = new object();
        private static readonly Dictionary<string, HashSet<string>> StoresMissingComponentKind =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> StoresRequiringLateComponentReload =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object LegacyAssetPackAliasLock = new object();
        private static Dictionary<string, string> LegacyAssetPackAliases;
        // Physical pack folder -> identifier of the store PrefabStore will
        // actually use. AssetLoader may have registered that folder before
        // FUSE runs; aliases must target its identifier rather than inventing a
        // second fuseasset:// store for the same files.
        private static readonly Dictionary<string, string> StoreIdentifiersByPhysicalPath =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

        // Old-loader plugins (notably LegosLibraryOfStuff) install a Harmony postfix on
        // the public ContainerSerialization.Deserialize entry point and mutate shared
        // component instances on every call. A pack must therefore pass through that
        // method exactly ONCE per PrefabStore generation: the cold load of a direct
        // store does so on purpose (see LoadResilientDirectContainer — the game's native
        // Container() body never runs for fuseasset:// stores, so that is the pack's only
        // deserialization), while the per-item mixinto re-deserialize goes through
        // Newtonsoft directly using the same settings the public method would have used,
        // so the postfix does not re-fire on the same identifiers and break per-car
        // ComponentGroup toggles (commit c188ad1: "ERIE LOGO" going dead when AssetLoader's
        // native store and FUSE's direct store both existed for one folder). See
        // FuseLegacyContainerMixintoRegistry for that bypass.
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
            var hadState = _mountComplete ||
                           MountedAssetPackSourcesById.Count > 0 ||
                           DirectAssetPackStoreIdentifiers.Count > 0;
            _mountComplete = false;
            MountedAssetPackSourcesById.Clear();
            DirectAssetPackStoreIdentifiers.Clear();
            lock (LateComponentRegistrationLock)
            {
                StoresMissingComponentKind.Clear();
                StoresRequiringLateComponentReload.Clear();
            }
            lock (LegacyAssetPackAliasLock)
            {
                hadState |= StoreIdentifiersByPhysicalPath.Count > 0 || LegacyAssetPackAliases != null;
                StoreIdentifiersByPhysicalPath.Clear();
                LegacyAssetPackAliases = null;
            }

            lock (CatalogInspectionFailureLock)
            {
                CatalogInspectionFailures.Clear();
            }
            FuseLegacyContainerMixintoRegistry.Reset();
            ResetLegacyDefinitionOverrides();
            FuseAssetCollisionRegistry.Reset();
            FUSE.Runtime.API.SceneryAPI.InvalidateKnownSceneryIdentifierIndex();
            if (hadState)
            {
                FuseLog.Info("FUSE asset pack mount state reset.");
            }
        }

        internal static bool IsFuseManagedStoreIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return false;
            }

            lock (LegacyAssetPackAliasLock)
            {
                return DirectAssetPackStoreIdentifiers.Contains(identifier) ||
                       StoreIdentifiersByPhysicalPath.Values.Contains(
                           identifier,
                           StringComparer.OrdinalIgnoreCase);
            }
        }

        internal static FuseAssetPackDiagnostics GetDiagnostics()
        {
            var folders = EnumerateAvailableAssetPackFolders().ToArray();
            var firstSourceByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var firstDefinitionByKey = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            var duplicateKeys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var duplicateDefinitionsDiffer = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
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
                            firstDefinitionByKey[key] = obj;
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

                        if (!duplicateDefinitionsDiffer.TryGetValue(key, out var definitionsDiffer) || !definitionsDiffer)
                        {
                            duplicateDefinitionsDiffer[key] =
                                !firstDefinitionByKey.TryGetValue(key, out var firstDefinition) ||
                                !JToken.DeepEquals(firstDefinition, obj);
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
                        Sources = item.Value.Select(DescribeAssetPackSource).ToArray(),
                        DefinitionsDiffer = duplicateDefinitionsDiffer.TryGetValue(item.Key, out var differ) && differ
                    })
                    .ToArray(),
                FailedDefinitionLoads = failedLookups.ToArray(),
                LegacyDefinitionOverrides = GetLegacyDefinitionOverrides(),
                LegacyDefinitionOverrideIssues = GetLegacyDefinitionOverrideIssues()
            };
        }

        private static string DescribeAssetPackSource(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return string.Empty;
            }

            try
            {
                var packageFolder = ReadHostingPackageFolder(folder);
                if (!string.IsNullOrWhiteSpace(packageFolder))
                {
                    var packageName = Path.GetFileName(packageFolder);
                    var relative = folder.Substring(packageFolder.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace(Path.DirectorySeparatorChar, '/');
                    return string.IsNullOrWhiteSpace(relative)
                        ? packageName
                        : packageName + "/" + relative;
                }
            }
            catch (Exception ex)
            {
                // Diagnostics must never make an otherwise usable asset pack
                // fail to load, but retaining the reason avoids hiding a path
                // or permissions problem from the live log.
                FuseLog.Warning(
                    $"FUSE could not build a package-relative asset source label for '{folder}': " +
                    ex.GetBaseException().Message);
            }

            return Path.GetFileName(folder);
        }
    }

    internal sealed class FuseAssetPackDiagnostics
    {
        public string[] StoreFolders { get; set; } = Array.Empty<string>();
        public int UniqueAssetKeys { get; set; }
        public FuseDuplicateAssetKey[] DuplicateKeys { get; set; } = Array.Empty<FuseDuplicateAssetKey>();
        public string[] FailedDefinitionLoads { get; set; } = Array.Empty<string>();
        public FuseLegacyDefinitionOverrideRegistration[] LegacyDefinitionOverrides { get; set; } =
            Array.Empty<FuseLegacyDefinitionOverrideRegistration>();
        public string[] LegacyDefinitionOverrideIssues { get; set; } = Array.Empty<string>();
    }

    internal sealed class FuseDuplicateAssetKey
    {
        public string Key { get; set; } = string.Empty;
        public string[] Sources { get; set; } = Array.Empty<string>();
        public bool DefinitionsDiffer { get; set; }
    }
}
