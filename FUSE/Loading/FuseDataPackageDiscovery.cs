using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Runtime.Lifecycle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FUSE.Loading
{
    public static class FuseDataPackageDiscovery
    {
        private static bool _discoveryComplete;
        private static bool _definitionsLoadedFromDisk;
        private static string[] _discoveredPackageFolders = Array.Empty<string>();
        private static FusePackageManifest[] _discoveredPackageManifests = Array.Empty<FusePackageManifest>();

        public static int LoadAllAvailablePackages()
        {
            return LoadPackagesFromDisk(false);
        }

        public static IReadOnlyList<string> DiscoverPackagesOnce()
        {
            if (_discoveryComplete)
            {
                FuseLog.Info($"FUSE package discovery already completed with {_discoveredPackageFolders.Length} candidate data package(s).");
                return _discoveredPackageFolders;
            }

            var stopwatch = Stopwatch.StartNew();
            var modsRoot = GetModsRoot();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                FuseLog.Warning("FUSE could not locate the Unity Mod Manager Mods folder.");
                _discoveredPackageFolders = Array.Empty<string>();
                _discoveryComplete = true;
                FusePerformanceMetrics.RecordTiming("discover packages", stopwatch.ElapsedMilliseconds);
                FuseLog.Info($"FUSE load timing phase='discover packages' elapsedMs={stopwatch.ElapsedMilliseconds} packageCount=0 status='mods-root-missing'.");
                return _discoveredPackageFolders;
            }

            try
            {
                _discoveredPackageManifests = DiscoverPackageManifests(modsRoot).ToArray();
                _discoveredPackageFolders = _discoveredPackageManifests.Select(manifest => manifest.FolderPath).ToArray();
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE failed while discovering data packages in '{modsRoot}'", ex);
                _discoveredPackageFolders = Array.Empty<string>();
                _discoveredPackageManifests = Array.Empty<FusePackageManifest>();
            }

            _discoveryComplete = true;
            FuseLog.Info($"FUSE discovered {_discoveredPackageFolders.Length} candidate data package(s) in '{modsRoot}'.");
            FusePerformanceMetrics.RecordTiming("discover packages", stopwatch.ElapsedMilliseconds);
            FuseLog.Info($"FUSE load timing phase='discover packages' elapsedMs={stopwatch.ElapsedMilliseconds} packageCount={_discoveredPackageFolders.Length}.");
            for (var index = 0; index < _discoveredPackageFolders.Length; index++)
            {
                var manifest = index < _discoveredPackageManifests.Length ? _discoveredPackageManifests[index] : null;
                var status = manifest == null
                    ? "unknown"
                    : manifest.Disabled
                        ? "disabled"
                        : manifest.HasBlockingFaults
                            ? "faulted"
                            : "ready";
                FuseLog.Info($"FUSE discovered package[{index}] '{Path.GetFileName(_discoveredPackageFolders[index])}' id='{manifest?.Id ?? string.Empty}' status='{status}' priority={manifest?.Priority ?? 0} path='{_discoveredPackageFolders[index]}'.");
            }

            return _discoveredPackageFolders;
        }

        public static IReadOnlyList<string> RefreshDiscoveredPackages()
        {
            ResetDiscovery();
            return DiscoverPackagesOnce();
        }

        internal static IReadOnlyList<FusePackageManifestSnapshot> GetPackageManifestSnapshots()
        {
            DiscoverPackagesOnce();
            return _discoveredPackageManifests
                .Select((manifest, index) => new FusePackageManifestSnapshot
                {
                    Order = index + 1,
                    Id = manifest.Id ?? string.Empty,
                    DisplayName = manifest.DisplayName ?? string.Empty,
                    Version = manifest.Version ?? string.Empty,
                    FolderName = string.IsNullOrWhiteSpace(manifest.FolderPath) ? string.Empty : Path.GetFileName(manifest.FolderPath),
                    FolderPath = manifest.FolderPath ?? string.Empty,
                    Priority = manifest.Priority,
                    LoadAfter = manifest.LoadAfter ?? Array.Empty<string>(),
                    LoadBefore = manifest.LoadBefore ?? Array.Empty<string>(),
                    RequiredPackageIds = manifest.RequiredPackageIds ?? Array.Empty<string>(),
                    ConflictsWith = manifest.ConflictsWith ?? Array.Empty<FuseModRequirement>(),
                    Disabled = manifest.Disabled,
                    DisabledReason = manifest.DisabledReason ?? string.Empty,
                    IsLegacyConverted = manifest.IsLegacyDataPackage,
                    Faults = manifest.Faults.ToArray()
                })
                .ToArray();
        }

        public static int LoadPackagesFromDisk(bool forceReload)
        {
            var stopwatch = Stopwatch.StartNew();
            if (_definitionsLoadedFromDisk && !forceReload)
            {
                FuseLog.Info($"FUSE package disk load skipped because {FuseModLoader.LoadedDefinitionCount} definition(s) are already loaded.");
                FusePerformanceMetrics.RecordTiming("load packages from disk", stopwatch.ElapsedMilliseconds);
                FuseLog.Info($"FUSE load timing phase='load packages from disk' elapsedMs={stopwatch.ElapsedMilliseconds} loaded=0 skipped='resident-definitions-already-loaded'.");
                FusePackageFaultRegistry.LogFinalReport("disk load skipped", FuseModLoader.LoadedDefinitionCount);
                return 0;
            }

            if (forceReload)
            {
                FuseLog.Info("FUSE forced package disk reload requested; using existing discovery cache.");
                FuseModLoader.UnloadAll(resetDiscovery: false, restoreTrackSnapshots: false);
                FusePackageFaultRegistry.Reset();
                _definitionsLoadedFromDisk = false;
            }

            var packagePaths = DiscoverPackagesOnce();
            if (packagePaths.Count == 0)
            {
                _definitionsLoadedFromDisk = true;
                FuseLog.Info("FUSE loaded 0 package definition(s) from disk because discovery found no packages.");
                FusePerformanceMetrics.RecordTiming("load packages from disk", stopwatch.ElapsedMilliseconds);
                FuseLog.Info($"FUSE load timing phase='load packages from disk' elapsedMs={stopwatch.ElapsedMilliseconds} loaded=0 packageCount=0.");
                FusePackageFaultRegistry.LogFinalReport("disk load", FuseModLoader.LoadedDefinitionCount);
                return 0;
            }

            var assetStopwatch = Stopwatch.StartNew();
            try
            {
                FuseAssetPackRegistry.MountAllAvailableAssetPacks();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE asset pack mount failed before package disk load; continuing with definition loading.", ex);
            }
            FusePerformanceMetrics.RecordTiming("asset pack registration", assetStopwatch.ElapsedMilliseconds);
            FuseLog.Info($"FUSE load timing phase='asset pack registration' elapsedMs={assetStopwatch.ElapsedMilliseconds}.");

            var loadedCount = 0;
            var manifests = _discoveredPackageManifests.Length > 0
                ? _discoveredPackageManifests
                : packagePaths.Select(path => new FusePackageManifest
                {
                    Id = Path.GetFileName(path),
                    FolderPath = path
                }).ToArray();

            foreach (var manifest in manifests)
            {
                var packagePath = manifest.FolderPath;
                if (manifest.Disabled)
                {
                    FusePackageFaultRegistry.MarkDisabled(manifest.Id, manifest.DisabledReason);
                    FuseLog.Warning($"FUSE skipped disabled data package '{manifest.Id}' path='{packagePath}' reason='{manifest.DisabledReason}'.");
                    continue;
                }

                if (manifest.HasBlockingFaults)
                {
                    if (manifest.ManifestReadException != null)
                    {
                        FusePackageFaultRegistry.RecordFault(
                            manifest.Id,
                            "manifest JSON",
                            manifest.Faults.FirstOrDefault() ?? manifest.ManifestReadException.GetBaseException().Message,
                            manifest.ManifestReadException,
                            manifest.FolderPath,
                            manifest.ManifestPath,
                            packageName: manifest.DisplayName,
                            expectedShape: "A valid Info.json or Definition.json package manifest.",
                            receivedValue: manifest.ManifestReadException.GetBaseException().Message);
                    }
                    else
                    {
                        foreach (var fault in manifest.Faults)
                        {
                            FusePackageFaultRegistry.RecordFault(
                                manifest.Id,
                                "dependency/load-order",
                                fault,
                                null,
                                manifest.FolderPath,
                                manifest.ManifestPath,
                                InferManifestFaultJsonPath(manifest, fault),
                                suggestedAction: SuggestedActionForManifestFault(fault),
                                packageName: manifest.DisplayName,
                                expectedShape: "Package dependency and load-order declarations accepted by FUSE.",
                                receivedValue: fault);
                        }
                    }

                    FusePackageFaultRegistry.MarkSkipped(manifest.Id, "dependency/load-order fault");
                    FuseLog.Warning($"FUSE skipped faulted data package '{manifest.Id}' path='{packagePath}' faultCount={manifest.Faults.Count}.");
                    continue;
                }

                var faultCountBeforeLoad = FusePackageFaultRegistry.FaultCount;
                try
                {
                    var packageStopwatch = Stopwatch.StartNew();
                    if (manifest.IsLegacyDataPackage)
                    {
                        FuseLegacyDataConverter.LoadPackage(packagePath);
                    }
                    else
                    {
                        FuseModLoader.LoadMod(packagePath, manifest.Id);
                    }
                    FusePackageFaultRegistry.MarkLoadedFromDisk(manifest.Id);
                    FuseLog.Info($"FUSE loaded package '{Path.GetFileName(packagePath)}' id='{manifest.Id}' from disk into resident definitions in {packageStopwatch.ElapsedMilliseconds} ms. Runtime apply has not run in this step.");
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    if (FusePackageFaultRegistry.FaultCount == faultCountBeforeLoad)
                    {
                        FusePackageFaultRegistry.RecordFault(
                            manifest.Id,
                            "package load",
                            ex.GetBaseException().Message,
                            ex,
                            packagePath);
                    }
                    FusePackageFaultRegistry.MarkSkipped(manifest.Id, "deserialization failed");
                    FuseLog.Exception($"Failed to deserialize FUSE data package '{packagePath}' from disk", ex);
                }
            }

            _definitionsLoadedFromDisk = true;
            FuseLog.Info($"FUSE loaded {loadedCount} data package folder(s) from disk; {FuseModLoader.LoadedDefinitionCount} resident definition(s). Runtime apply is separate.");
            FusePerformanceMetrics.RecordTiming("load packages from disk", stopwatch.ElapsedMilliseconds);
            FuseLog.Info($"FUSE load timing phase='load packages from disk' elapsedMs={stopwatch.ElapsedMilliseconds} loaded={loadedCount} residentDefinitions={FuseModLoader.LoadedDefinitionCount}.");
            FusePackageFaultRegistry.LogFinalReport("disk load", FuseModLoader.LoadedDefinitionCount);
            return loadedCount;
        }

        internal static IReadOnlyList<string> GetLegacyConvertedPackageIds()
        {
            DiscoverPackagesOnce();
            return _discoveredPackageManifests
                .Where(manifest => manifest.IsLegacyDataPackage)
                .Select(manifest => manifest.Id ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static int ApplyLoadedPackages(string reason)
        {
            var stopwatch = Stopwatch.StartNew();
            FusePerformanceMetrics.ResetApplyTimings();
            if (!FuseMultiplayerGuard.CanApplyWorldMutations(reason ?? "runtime apply"))
            {
                FusePerformanceMetrics.RecordTiming("apply resident definitions", stopwatch.ElapsedMilliseconds);
                FuseLog.Info($"FUSE load timing phase='apply resident definitions' reason='{reason ?? "unspecified"}' elapsedMs={stopwatch.ElapsedMilliseconds} applied=0 residentDefinitions={FuseModLoader.LoadedDefinitionCount} skipped='non-host-multiplayer-client'.");
                FusePackageFaultRegistry.LogFinalReport(reason ?? "runtime apply skipped", FuseModLoader.LoadedDefinitionCount);
                return 0;
            }

            // If a deferred scenery wave from the initial map load is still in flight,
            // realize it now so this apply (e.g. a live reapply) operates on
            // fully-activated scenery rather than racing the wave.
            FuseDeferredSceneryActivator.FlushSynchronously(reason ?? "apply");
            var appliedCount = FuseModLoader.ApplyLoadedDefinitions(reason);
            FuseLog.Info($"FUSE applied {appliedCount} resident definition(s) to runtime for '{reason ?? "unspecified"}'.");
            FusePerformanceMetrics.RecordTiming("apply resident definitions", stopwatch.ElapsedMilliseconds);
            FuseLog.Info($"FUSE load timing phase='apply resident definitions' reason='{reason ?? "unspecified"}' elapsedMs={stopwatch.ElapsedMilliseconds} applied={appliedCount} residentDefinitions={FuseModLoader.LoadedDefinitionCount}.");
            FusePackageFaultRegistry.LogFinalReport(reason ?? "runtime apply", FuseModLoader.LoadedDefinitionCount);
            return appliedCount;
        }

        public static int ReapplyLoadedPackages(string reason)
        {
            return ApplyLoadedPackages(reason);
        }

        public static int LoadAndApplyAvailablePackages(string reason, bool forceReload = false)
        {
            var stopwatch = Stopwatch.StartNew();
            var loadedCount = LoadPackagesFromDisk(forceReload);
            var appliedCount = ApplyLoadedPackages(reason);
            FuseLog.Info($"FUSE load/apply complete for '{reason ?? "unspecified"}': loadedFromDisk={loadedCount}, appliedToRuntime={appliedCount}.");
            FusePerformanceMetrics.RecordTiming("load and apply packages", stopwatch.ElapsedMilliseconds);
            FuseLog.Info($"FUSE load timing phase='load and apply packages' reason='{reason ?? "unspecified"}' elapsedMs={stopwatch.ElapsedMilliseconds} loadedFromDisk={loadedCount} appliedToRuntime={appliedCount}.");
            return appliedCount;
        }

        public static void ReloadPackagesFromDisk()
        {
            LoadPackagesFromDisk(true);
        }

        public static void ResetDiscovery()
        {
            FuseLegacyCapabilityActivation.Reset();
            if (!_discoveryComplete && !_definitionsLoadedFromDisk && _discoveredPackageFolders.Length == 0)
            {
                return;
            }

            _discoveryComplete = false;
            _definitionsLoadedFromDisk = false;
            _discoveredPackageFolders = Array.Empty<string>();
            _discoveredPackageManifests = Array.Empty<FusePackageManifest>();
            FusePackageFaultRegistry.Reset();
            FuseLog.Info("FUSE package discovery state reset.");
        }

        public static IEnumerable<string> DiscoverPackageFolders(string modsRoot)
        {
            return DiscoverPackageManifests(modsRoot)
                .Select(manifest => manifest.FolderPath)
                .ToArray();
        }

        private static IEnumerable<FusePackageManifest> DiscoverPackageManifests(string modsRoot)
        {
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                return Enumerable.Empty<FusePackageManifest>();
            }

            var manifests = new List<FusePackageManifest>();
            string[] packagePaths;
            try
            {
                packagePaths = Directory.GetDirectories(modsRoot)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                FuseLog.Exception($"FUSE could not enumerate Mods folder '{modsRoot}' for data packages", ex);
                return Enumerable.Empty<FusePackageManifest>();
            }

            foreach (var packagePath in packagePaths)
            {
                if (TryReadFusePackageManifest(packagePath, out var manifest))
                {
                    manifests.Add(manifest);
                }
            }

            return SortPackages(manifests)
                .ToArray();
        }

        private static bool IsFuseDataPackage(string folderPath)
        {
            return TryReadFusePackageManifest(folderPath, out _);
        }

        private static bool TryReadFusePackageManifest(string folderPath, out FusePackageManifest manifest)
        {
            var infoPath = Path.Combine(folderPath, "Info.json");
            manifest = null;
            if (!File.Exists(infoPath))
            {
                return TryReadLegacyDataPackageManifest(folderPath, null, out manifest);
            }

            JObject info;
            try
            {
                info = FuseLegacyDataConverter.ReadManifestObject(infoPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                // A malformed Info.json is an authoring error in a FUSE/UMM package, not a signal to
                // reinterpret the folder as a legacy data package. Surface the parse failure and stop.
                FuseLog.Exception($"FUSE ignored package '{folderPath}' because Info.json could not be parsed", ex);
                if (!LooksLikeFusePackageWithBrokenInfo(folderPath))
                {
                    return false;
                }

                var fallbackId = Path.GetFileName(folderPath);
                manifest = new FusePackageManifest
                {
                    Id = fallbackId,
                    DisplayName = fallbackId,
                    FolderPath = folderPath,
                    ManifestPath = infoPath,
                    ManifestReadException = ex
                };
                manifest.Faults.Add(ex.GetBaseException().Message);
                return true;
            }

            var id = ((string)info["Id"] ?? Path.GetFileName(folderPath)).Trim();
            if (string.Equals(id, "FUSE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var dataFileToken = info["FuseDataFile"] ?? info["FuseDataFiles"];
            var declaresDataFiles = dataFileToken != null;
            var hasExplicitDataFiles = HasFuseDataFile(dataFileToken);
            var isAssetPackOnly = HasFuseAssetPacks(info) && !declaresDataFiles;
            var isDataPackage = declaresDataFiles ||
                (!isAssetPackOnly && HasRootDefinitionFile(folderPath) && (ContainsFuseReference(info["Requirements"]) || ContainsFuseReference(info["LoadAfter"])));
            if (!isDataPackage)
            {
                return TryReadLegacyDataPackageManifest(folderPath, info, out manifest);
            }

            if (FuseUmmState.TryGetDisabledReason(folderPath, id, out var ummDisabledReason))
            {
                FuseLog.Info($"FUSE ignored UMM-disabled data package '{id}' path='{folderPath}' reason='{ummDisabledReason}'.");
                return false;
            }

            var disabled = ReadDisabled(info);
            var disabledReason = ReadDisabledReason(info);
            if (!disabled && !FuseModSetService.IsPackageEnabledByActiveSet(id, folderPath))
            {
                disabled = true;
                disabledReason = FuseModSetService.GetPackageDisabledReason(id, folderPath);
            }

            manifest = new FusePackageManifest
            {
                Id = string.IsNullOrWhiteSpace(id) ? Path.GetFileName(folderPath) : id,
                DisplayName = ((string)info["DisplayName"] ?? (string)info["Name"] ?? (string)info["name"] ?? id ?? string.Empty).Trim(),
                Version = ((string)info["Version"] ?? (string)info["version"] ?? string.Empty).Trim(),
                FolderPath = folderPath,
                ManifestPath = infoPath,
                Priority = ReadPriority(info["FuseLoadPriority"]),
                RequiredPackageIds = ReadDependencyIds(info["FuseRequires"]),
                LoadAfter = ReadDependencyIds(info["FuseLoadAfter"]),
                LoadBefore = ReadDependencyIds(info["FuseLoadBefore"]),
                ConflictsWith = ReadPackageReferences(info["FuseConflictsWith"]),
                Disabled = disabled,
                DisabledReason = disabledReason
            };
            if (declaresDataFiles && !hasExplicitDataFiles)
            {
                manifest.Faults.Add(
                    "Info.json field 'FuseDataFile'/'FuseDataFiles' must be a non-empty file name or an array of non-empty file names.");
            }
            return true;
        }

        private static bool TryReadLegacyDataPackageManifest(string folderPath, JObject info, out FusePackageManifest manifest)
        {
            manifest = null;
            if (!FuseLegacyDataConverter.TryReadLegacyManifest(folderPath, out var legacy))
            {
                return false;
            }

            var id = legacy.PackageId;
            if (FuseUmmState.TryGetDisabledReason(folderPath, id, out var ummDisabledReason))
            {
                FuseLog.Info($"FUSE ignored UMM-disabled legacy data package '{id}' path='{folderPath}' reason='{ummDisabledReason}'.");
                return false;
            }

            var disabled = ReadDisabled(info);
            var disabledReason = ReadDisabledReason(info);
            if (!disabled && !FuseModSetService.IsPackageEnabledByActiveSet(id, folderPath))
            {
                disabled = true;
                disabledReason = FuseModSetService.GetPackageDisabledReason(id, folderPath);
            }

            manifest = new FusePackageManifest
            {
                Id = id,
                DisplayName = legacy.DisplayName ?? id,
                Version = legacy.Version ?? string.Empty,
                FolderPath = folderPath,
                ManifestPath = Path.Combine(folderPath, "Definition.json"),
                Priority = ReadPriority(info?["FuseLoadPriority"]),
                RequiredPackageIds = legacy.RequiredPackageIds ?? Array.Empty<string>(),
                LoadAfter = legacy.LoadAfter ?? Array.Empty<string>(),
                LoadBefore = ReadDependencyIds(info?["FuseLoadBefore"]),
                ConflictsWith = legacy.ConflictsWith ?? Array.Empty<FuseModRequirement>(),
                Disabled = disabled,
                DisabledReason = disabledReason,
                IsLegacyDataPackage = true
            };
            return true;
        }

        private static bool LooksLikeFusePackageWithBrokenInfo(string folderPath)
        {
            try
            {
                return File.Exists(Path.Combine(folderPath, "Definition.json")) ||
                       Directory.GetFiles(folderPath, "*.fuse.json", SearchOption.TopDirectoryOnly).Length > 0 ||
                       Directory.GetFiles(folderPath, "*.bson", SearchOption.TopDirectoryOnly).Length > 0;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                FuseLog.Exception($"FUSE could not inspect malformed package folder '{folderPath}'", ex);
                return false;
            }
        }

        private static string InferManifestFaultJsonPath(FusePackageManifest manifest, string message)
        {
            if (message?.IndexOf("requires", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return manifest != null && manifest.IsLegacyDataPackage ? "requires" : "FuseRequires";
            }

            if (message?.IndexOf("LoadAfter", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return manifest != null && manifest.IsLegacyDataPackage ? "loadAfter" : "FuseLoadAfter";
            }

            if (message?.IndexOf("LoadBefore", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return manifest != null && manifest.IsLegacyDataPackage ? "loadBefore" : "FuseLoadBefore";
            }

            if (message?.IndexOf("conflictsWith", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return manifest != null && manifest.IsLegacyDataPackage ? "conflictsWith" : "FuseConflictsWith";
            }

            if (message?.IndexOf("FuseDataFile", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "FuseDataFiles";
            }

            return string.Empty;
        }

        private static string SuggestedActionForManifestFault(string message)
        {
            if (message?.IndexOf("FuseDataFile", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Correct the FuseDataFile/FuseDataFiles value in Info.json, then reload package discovery.";
            }

            if (message?.IndexOf("requires", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Install and enable the named dependency, or correct this package's required dependency id.";
            }

            if (message?.IndexOf("LoadAfter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message?.IndexOf("LoadBefore", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Correct the load-order id or install and enable the referenced package.";
            }

            if (message?.IndexOf("conflictsWith", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Disable one of the author-declared incompatible packages, or correct the conflictsWith declaration if the packages are compatible.";
            }

            return "Correct the reported package manifest problem, then reload package discovery.";
        }

        private static IReadOnlyList<FusePackageManifest> SortPackages(IReadOnlyList<FusePackageManifest> packages)
        {
            var fallbackOrder = packages
                .OrderBy(package => package.Priority)
                .ThenBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(package => package.FolderPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (fallbackOrder.Length == 0)
            {
                return fallbackOrder;
            }

            var byId = new Dictionary<string, FusePackageManifest>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in fallbackOrder)
            {
                if (!byId.ContainsKey(package.Id))
                {
                    byId.Add(package.Id, package);
                    continue;
                }

                var message = $"Package id '{package.Id}' appears more than once; dependency ordering will target the first matching package.";
                package.Faults.Add(message);
                FuseLog.Warning($"FUSE {message}");
            }

            var installedMods = FuseModRequirementResolver.CollectInstalledMods();
            foreach (var package in fallbackOrder)
            {
                if (package.Disabled)
                {
                    continue;
                }

                foreach (var conflict in package.ConflictsWith ?? Array.Empty<FuseModRequirement>())
                {
                    string conflictingId;
                    string conflictingVersion;
                    string conflictingSource;
                    if (TryResolvePackage(byId, conflict?.Id, out var conflictingPackage) &&
                        !ReferenceEquals(package, conflictingPackage) &&
                        IsDeclaredConflictMatch(
                            conflict,
                            conflictingPackage.Id,
                            conflictingPackage.Version,
                            conflictingPackage.Disabled))
                    {
                        conflictingId = conflictingPackage.Id;
                        conflictingVersion = conflictingPackage.Version;
                        conflictingSource = "discovered data package";
                    }
                    else if (TryMatchInstalledConflict(
                                 package.Id,
                                 package.FolderPath,
                                 conflict,
                                 installedMods,
                                 out var installed))
                    {
                        conflictingId = installed.Id;
                        conflictingVersion = installed.Version;
                        conflictingSource = installed.Source;
                    }
                    else
                    {
                        continue;
                    }

                    var bounds = FormatVersionBounds(conflict);
                    var message =
                        $"Package '{package.Id}' conflictsWith '{conflict.Id}'{bounds}; " +
                        $"matching package '{conflictingId}' version '{conflictingVersion}' is enabled " +
                        $"(source: {conflictingSource}).";
                    package.Faults.Add(message);
                    FuseLog.Warning($"FUSE {message}");
                }
            }

            var outgoing = fallbackOrder.ToDictionary(package => package, _ => new HashSet<FusePackageManifest>());
            var incomingCount = fallbackOrder.ToDictionary(package => package, _ => 0);

            foreach (var package in fallbackOrder)
            {
                // Disabled packages stay in the lookup table so an enabled package
                // that requires one can report that dependency accurately. Their
                // own requirements and order declarations are intentionally inert:
                // a profile-disabled package must not fault the active mod set.
                if (package.Disabled)
                {
                    continue;
                }

                foreach (var dependencyId in package.RequiredPackageIds)
                {
                    if (TryResolvePackage(byId, dependencyId, out var dependency))
                    {
                        if (dependency.Disabled)
                        {
                            var message = $"Package '{package.Id}' requires '{dependencyId}', but that package is disabled.";
                            package.Faults.Add(message);
                            FuseLog.Warning($"FUSE {message}");
                            continue;
                        }

                        AddPackageOrderEdge(dependency, package, outgoing, incomingCount);
                        continue;
                    }

                    if (FuseReplacementCapabilityCatalog.IsProvided(dependencyId))
                    {
                        FuseLog.Info(
                            $"FUSE dependency '{dependencyId}' required by '{package.Id}' is supplied by the FUSE runtime; " +
                            "the runtime initializes before data packages, so no package load-order edge was needed.");
                        continue;
                    }

                    if (IsPackagePresentInModsRoot(dependencyId))
                    {
                        FuseLog.Info(
                            $"FUSE dependency '{dependencyId}' required by '{package.Id}' is present as a non-data or asset-only mod; " +
                            "no package load-order edge was needed.");
                        continue;
                    }

                    var missing = $"Package '{package.Id}' requires '{dependencyId}', but no matching package was discovered.";
                    package.Faults.Add(missing);
                    FuseLog.Warning($"FUSE {missing}");
                }

                foreach (var dependencyId in package.LoadAfter)
                {
                    if (TryResolvePackage(byId, dependencyId, out var dependency))
                    {
                        if (dependency.Disabled)
                        {
                            var message = $"Package '{package.Id}' declares FuseLoadAfter '{dependencyId}', but that package is disabled.";
                            if (package.IsLegacyDataPackage)
                            {
                                FuseLog.Info($"FUSE ignored legacy order reference because it is advisory: {message}");
                            }
                            else
                            {
                                package.Faults.Add(message);
                                FuseLog.Warning($"FUSE {message}");
                            }
                            continue;
                        }

                        AddPackageOrderEdge(dependency, package, outgoing, incomingCount);
                    }
                    else if (FuseReplacementCapabilityCatalog.IsProvided(dependencyId))
                    {
                        FuseLog.Info(
                            $"FUSE loadAfter '{dependencyId}' declared by '{package.Id}' resolves to a FUSE runtime replacement; " +
                            "the runtime is already initialized before this package.");
                    }
                    else
                    {
                        var message = $"Package '{package.Id}' declares FuseLoadAfter '{dependencyId}', but no matching FUSE data package was discovered.";
                        if (package.IsLegacyDataPackage)
                        {
                            FuseLog.Info($"FUSE ignored legacy order reference because it is advisory: {message}");
                        }
                        else
                        {
                            package.Faults.Add(message);
                            FuseLog.Warning($"FUSE {message}");
                        }
                    }
                }

                foreach (var dependencyId in package.LoadBefore)
                {
                    if (TryResolvePackage(byId, dependencyId, out var dependency))
                    {
                        if (dependency.Disabled)
                        {
                            var message = $"Package '{package.Id}' declares FuseLoadBefore '{dependencyId}', but that package is disabled.";
                            if (package.IsLegacyDataPackage)
                            {
                                FuseLog.Info($"FUSE ignored legacy order reference because it is advisory: {message}");
                            }
                            else
                            {
                                package.Faults.Add(message);
                                FuseLog.Warning($"FUSE {message}");
                            }
                            continue;
                        }

                        AddPackageOrderEdge(package, dependency, outgoing, incomingCount);
                    }
                    else if (FuseReplacementCapabilityCatalog.IsProvided(dependencyId))
                    {
                        var message =
                            $"Package '{package.Id}' declares loadBefore '{dependencyId}', but '{dependencyId}' is a FUSE runtime replacement " +
                            "that necessarily initializes before data packages.";
                        package.Faults.Add(message);
                        FuseLog.Warning($"FUSE {message}");
                    }
                    else
                    {
                        var message = $"Package '{package.Id}' declares FuseLoadBefore '{dependencyId}', but no matching FUSE data package was discovered.";
                        if (package.IsLegacyDataPackage)
                        {
                            FuseLog.Info($"FUSE ignored legacy order reference because it is advisory: {message}");
                        }
                        else
                        {
                            package.Faults.Add(message);
                            FuseLog.Warning($"FUSE {message}");
                        }
                    }
                }
            }

            var result = new List<FusePackageManifest>(fallbackOrder.Length);
            var ready = fallbackOrder.Where(package => incomingCount[package] == 0).ToList();
            SortReadyPackages(ready);
            while (ready.Count > 0)
            {
                var package = ready[0];
                ready.RemoveAt(0);
                result.Add(package);

                foreach (var after in outgoing[package].OrderBy(candidate => candidate.Priority).ThenBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase))
                {
                    incomingCount[after]--;
                    if (incomingCount[after] == 0)
                    {
                        ready.Add(after);
                    }
                }

                SortReadyPackages(ready);
            }

            if (result.Count != fallbackOrder.Length)
            {
                var cycle = fallbackOrder.Where(package => !result.Contains(package)).ToArray();
                var cycleIds = string.Join(", ", cycle.Select(package => package.Id).ToArray());
                FuseLog.Warning($"FUSE package load-order cycle detected among: {cycleIds}. Appending those packages by priority/name fallback order as faulted packages.");
                foreach (var package in cycle)
                {
                    package.Faults.Add($"Load-order cycle detected among: {cycleIds}.");
                }

                result.AddRange(cycle);
            }

            FuseLog.Info("FUSE package load order: " + string.Join(", ", result.Select(package => $"{package.Id}(priority={package.Priority})").ToArray()));
            return result;
        }

        private static bool TryResolvePackage(
            IReadOnlyDictionary<string, FusePackageManifest> packagesById,
            string dependencyId,
            out FusePackageManifest package)
        {
            package = null;
            if (packagesById == null || string.IsNullOrWhiteSpace(dependencyId))
            {
                return false;
            }

            if (packagesById.TryGetValue(dependencyId.Trim(), out package))
            {
                return true;
            }

            package = packagesById.Values.FirstOrDefault(candidate =>
                FuseDeclaredPackageRelationship.SamePackageId(candidate?.Id, dependencyId));
            return package != null;
        }

        internal static bool IsDeclaredConflictMatch(
            FuseModRequirement conflict,
            string targetId,
            string targetVersion,
            bool targetDisabled)
        {
            if (conflict == null || string.IsNullOrWhiteSpace(conflict.Id) ||
                string.IsNullOrWhiteSpace(targetId) || targetDisabled ||
                !FuseDeclaredPackageRelationship.SamePackageId(conflict.Id, targetId))
            {
                return false;
            }

            var installed = new FuseModRequirementResolver.InstalledMod
            {
                Id = targetId,
                Version = targetVersion ?? string.Empty,
                Source = "discovered data package"
            };
            return FuseModRequirementResolver.VersionSatisfies(
                "declared-conflict",
                conflict,
                installed,
                out _);
        }

        internal static bool TryMatchInstalledConflict(
            string declaringPackageId,
            string declaringFolderPath,
            FuseModRequirement conflict,
            Dictionary<string, FuseModRequirementResolver.InstalledMod> installedMods,
            out FuseModRequirementResolver.InstalledMod installed)
        {
            installed = null;
            if (string.IsNullOrWhiteSpace(declaringPackageId) || conflict == null || string.IsNullOrWhiteSpace(conflict.Id) ||
                !FuseModRequirementResolver.TryFindInstalled(conflict.Id, installedMods, out var candidate))
            {
                return false;
            }

            if (FuseDeclaredPackageRelationship.SamePackageId(declaringPackageId, candidate.Id) ||
                (!string.IsNullOrWhiteSpace(declaringFolderPath) &&
                 !string.IsNullOrWhiteSpace(candidate.FolderPath) &&
                 string.Equals(
                     NormalizePackagePath(declaringFolderPath),
                     NormalizePackagePath(candidate.FolderPath),
                     StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (!candidate.IsReplacementCapability &&
                !IsDeclaredConflictMatch(conflict, candidate.Id, candidate.Version, targetDisabled: false))
            {
                return false;
            }

            installed = candidate;
            return true;
        }

        private static string NormalizePackagePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private static string FormatVersionBounds(FuseModRequirement reference)
        {
            if (reference == null ||
                (string.IsNullOrWhiteSpace(reference.NotBefore) && string.IsNullOrWhiteSpace(reference.NotAfter)))
            {
                return string.Empty;
            }

            return $" (notBefore='{reference.NotBefore ?? string.Empty}', notAfter='{reference.NotAfter ?? string.Empty}')";
        }

        private static void AddPackageOrderEdge(
            FusePackageManifest before,
            FusePackageManifest after,
            IDictionary<FusePackageManifest, HashSet<FusePackageManifest>> outgoing,
            IDictionary<FusePackageManifest, int> incomingCount)
        {
            if (before == null || after == null || ReferenceEquals(before, after))
            {
                return;
            }

            if (outgoing[before].Add(after))
            {
                incomingCount[after]++;
            }
        }

        private static void SortReadyPackages(List<FusePackageManifest> ready)
        {
            ready.Sort((left, right) =>
            {
                var priorityCompare = left.Priority.CompareTo(right.Priority);
                if (priorityCompare != 0)
                {
                    return priorityCompare;
                }

                var idCompare = string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
                return idCompare != 0
                    ? idCompare
                    : string.Compare(left.FolderPath, right.FolderPath, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// Batch form of <see cref="IsPackagePresentInModsRoot"/> for UI: scans the
        /// Mods root once and returns which of <paramref name="dependencyIds"/>
        /// resolve to an enabled mod folder that is *not* a discovered FUSE data
        /// package (asset-only packs, hosted code-only plugins). The Dependency
        /// Graph page uses this to stop painting installed asset/plugin mods as
        /// MISSING (issues #207, #223).
        /// </summary>
        internal static HashSet<string> ResolvePackagesPresentInModsRoot(IEnumerable<string> dependencyIds)
        {
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var wanted = (dependencyIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var dependencyId in wanted.Where(FuseReplacementCapabilityCatalog.IsProvided))
            {
                present.Add(dependencyId);
            }
            if (wanted.Length == 0)
            {
                return present;
            }

            var modsRoot = GetModsRoot();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                return present;
            }

            try
            {
                foreach (var packagePath in Directory.GetDirectories(modsRoot))
                {
                    var packageId = TryReadPackageId(packagePath);
                    if (FuseUmmState.TryGetDisabledReason(packagePath, packageId, out _) ||
                        !FuseModSetService.IsPackageEnabledByActiveSet(packageId, packagePath))
                    {
                        continue;
                    }

                    var folderName = Path.GetFileName(packagePath);
                    foreach (var dependencyId in wanted)
                    {
                        if (present.Contains(dependencyId))
                        {
                            continue;
                        }

                        var legacyNormalizedDependency = FuseLegacyDataConverter.EnsureFusePackageId(dependencyId);
                        if (PackageIdMatches(packageId, dependencyId, legacyNormalizedDependency) ||
                            PackageIdMatches(folderName, dependencyId, legacyNormalizedDependency))
                        {
                            present.Add(dependencyId);
                        }
                    }

                    if (present.Count == wanted.Length)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                FuseLog.Exception("FUSE could not inspect Mods folder while resolving dependency graph rows", ex);
            }

            return present;
        }

        private static bool IsPackagePresentInModsRoot(string dependencyId)
        {
            if (string.IsNullOrWhiteSpace(dependencyId))
            {
                return false;
            }

            if (FuseReplacementCapabilityCatalog.IsProvided(dependencyId))
            {
                return true;
            }

            var modsRoot = GetModsRoot();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                return false;
            }

            var normalizedDependency = dependencyId.Trim();
            var legacyNormalizedDependency = FuseLegacyDataConverter.EnsureFusePackageId(normalizedDependency);
            try
            {
                foreach (var packagePath in Directory.GetDirectories(modsRoot))
                {
                    var packageId = TryReadPackageId(packagePath);
                    if (FuseUmmState.TryGetDisabledReason(packagePath, packageId, out _) ||
                        !FuseModSetService.IsPackageEnabledByActiveSet(packageId, packagePath))
                    {
                        continue;
                    }

                    if (PackageIdMatches(packageId, normalizedDependency, legacyNormalizedDependency) ||
                        PackageIdMatches(Path.GetFileName(packagePath), normalizedDependency, legacyNormalizedDependency))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                FuseLog.Exception($"FUSE could not inspect Mods folder for required package '{dependencyId}'", ex);
            }

            return false;
        }

        private static string TryReadPackageId(string packagePath)
        {
            try
            {
                var infoPath = Path.Combine(packagePath, "Info.json");
                if (File.Exists(infoPath))
                {
                    var info = FuseLegacyDataConverter.ReadLegacyObject(infoPath);
                    var id = (string)info["Id"];
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        return id.Trim();
                    }
                }

                var definitionPath = Path.Combine(packagePath, "Definition.json");
                if (File.Exists(definitionPath))
                {
                    var definition = FuseLegacyDataConverter.ReadLegacyObject(definitionPath);
                    var id = (string)definition["id"] ?? (string)definition["Id"];
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        return id.Trim();
                    }
                }
            }
            catch
            {
                // Dependency checks can still fall back to the folder name.
            }

            return Path.GetFileName(packagePath);
        }

        private static bool PackageIdMatches(string candidate, string dependencyId, string legacyDependencyId)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            var trimmed = candidate.Trim();
            return string.Equals(trimmed, dependencyId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(FuseLegacyDataConverter.EnsureFusePackageId(trimmed), dependencyId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, legacyDependencyId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(FuseLegacyDataConverter.EnsureFusePackageId(trimmed), legacyDependencyId, StringComparison.OrdinalIgnoreCase);
        }

        private static int ReadPriority(JToken token)
        {
            if (token == null)
            {
                return 0;
            }

            if (token.Type == JTokenType.Integer)
            {
                return (int)token;
            }

            return int.TryParse(token.ToString(), out var priority) ? priority : 0;
        }

        private static bool ReadDisabled(JObject info)
        {
            if (info == null)
            {
                return false;
            }

            if (ReadBooleanFlag(info["FuseDisabled"], false) ||
                ReadBooleanFlag(info["Disabled"], false) ||
                ReadBooleanFlag(info["disabled"], false))
            {
                return true;
            }

            return !ReadBooleanFlag(info["Enabled"], true) ||
                   !ReadBooleanFlag(info["enabled"], true);
        }

        private static string ReadDisabledReason(JObject info)
        {
            if (info == null)
            {
                return "disabled by package manifest";
            }

            var reason =
                (string)info["FuseDisabledReason"] ??
                (string)info["DisabledReason"] ??
                (string)info["disabledReason"];
            return string.IsNullOrWhiteSpace(reason) ? "disabled by package manifest" : reason.Trim();
        }

        private static bool ReadBooleanFlag(JToken token, bool defaultValue)
        {
            if (token == null)
            {
                return defaultValue;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return (bool)token;
            }

            var value = token.ToString();
            if (bool.TryParse(value, out var boolean))
            {
                return boolean;
            }

            if (int.TryParse(value, out var integer))
            {
                return integer != 0;
            }

            return defaultValue;
        }

        private static string[] ReadDependencyIds(JToken token)
        {
            if (token == null)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            AddDependencyId(token, result);
            return result
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static FuseModRequirement[] ReadPackageReferences(JToken token)
        {
            if (token == null)
            {
                return Array.Empty<FuseModRequirement>();
            }

            IEnumerable<JToken> items = token.Type == JTokenType.Array
                ? token.Children()
                : new[] { token };
            return items
                .Select(item => item.Type == JTokenType.String
                    ? new FuseModRequirement { Id = ((string)item)?.Trim() }
                    : new FuseModRequirement
                    {
                        Id = ((string)item["Id"] ?? (string)item["id"])?.Trim(),
                        NotBefore = ((string)item["NotBefore"] ?? (string)item["notBefore"])?.Trim(),
                        NotAfter = ((string)item["NotAfter"] ?? (string)item["notAfter"])?.Trim()
                    })
                .Where(reference => !string.IsNullOrWhiteSpace(reference.Id))
                .GroupBy(reference =>
                    (reference.Id ?? string.Empty) + "\0" +
                    (reference.NotBefore ?? string.Empty) + "\0" +
                    (reference.NotAfter ?? string.Empty),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }

        private static void AddDependencyId(JToken token, ICollection<string> result)
        {
            if (token == null)
            {
                return;
            }

            if (token.Type == JTokenType.Array)
            {
                foreach (var child in token.Children())
                {
                    AddDependencyId(child, result);
                }

                return;
            }

            if (token.Type == JTokenType.Object)
            {
                var objectId = (string)token["Id"] ?? (string)token["id"];
                if (!string.IsNullOrWhiteSpace(objectId))
                {
                    result.Add(objectId.Trim());
                }

                return;
            }

            var value = (string)token;
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(value.Trim());
            }
        }

        private static bool HasFuseDataFile(JToken token)
        {
            if (token == null)
            {
                return false;
            }

            if (token.Type == JTokenType.String)
            {
                return !string.IsNullOrWhiteSpace((string)token);
            }

            if (token.Type != JTokenType.Array)
            {
                return false;
            }

            foreach (var item in token.Children())
            {
                if (item.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)item))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasFuseAssetPacks(JObject info)
        {
            return info != null && info["FuseAssetPacks"] != null;
        }

        private static bool HasRootDefinitionFile(string folderPath)
        {
            try
            {
                return FuseDefinitionFileDiscovery.HasFallbackDefinitionFile(folderPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                FuseLog.Exception($"FUSE could not inspect package root definition files in '{folderPath}'", ex);
                return false;
            }
        }

        private static bool ContainsFuseReference(JToken token)
        {
            if (token == null)
            {
                return false;
            }

            if (token.Type == JTokenType.String)
            {
                return string.Equals((string)token, "FUSE", StringComparison.OrdinalIgnoreCase);
            }

            if (token.Type != JTokenType.Array)
            {
                return false;
            }

            foreach (var item in token.Children())
            {
                if (item.Type == JTokenType.String && string.Equals((string)item, "FUSE", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (item.Type == JTokenType.Object)
                {
                    var itemId = (string)item["Id"] ?? (string)item["id"];
                    if (string.Equals(itemId, "FUSE", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static string GetModsRoot()
        {
            var candidates = new[]
            {
                FusePlugin.ModEntry?.Path,
                AppDomain.CurrentDomain.BaseDirectory
            };

            for (var index = 0; index < candidates.Length; index++)
            {
                var resolved = TryResolveModsRootFromPath(candidates[index]);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            var directFallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods");
            return LooksLikeModsRoot(directFallback) ? directFallback : null;
        }

        private static string TryResolveModsRootFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
            {
                if (LooksLikeModsRoot(current.FullName))
                {
                    return current.FullName;
                }

                var childMods = Path.Combine(current.FullName, "Mods");
                if (LooksLikeModsRoot(childMods))
                {
                    return childMods;
                }
            }

            return null;
        }

        private static bool LooksLikeModsRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return false;
            }

            if (!string.Equals(Path.GetFileName(path), "Mods", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                return Directory.GetDirectories(path)
                    .Any(child => File.Exists(Path.Combine(child, "Info.json")));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                FuseLog.Exception($"FUSE could not inspect potential Mods root '{path}'", ex);
                return false;
            }
        }

        private sealed class FusePackageManifest
        {
            public string Id { get; set; }
            public string DisplayName { get; set; }
            public string Version { get; set; }
            public string FolderPath { get; set; }
            public string ManifestPath { get; set; } = string.Empty;
            public Exception ManifestReadException { get; set; }
            public int Priority { get; set; }
            public string[] RequiredPackageIds { get; set; } = Array.Empty<string>();
            public string[] LoadAfter { get; set; } = Array.Empty<string>();
            public string[] LoadBefore { get; set; } = Array.Empty<string>();
            public FuseModRequirement[] ConflictsWith { get; set; } = Array.Empty<FuseModRequirement>();
            public bool Disabled { get; set; }
            public string DisabledReason { get; set; } = string.Empty;
            public bool IsLegacyDataPackage { get; set; }
            public List<string> Faults { get; } = new List<string>();
            public bool HasBlockingFaults => Faults.Count > 0;
        }
    }

    internal sealed class FusePackageManifestSnapshot
    {
        public int Order { get; set; }
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public int Priority { get; set; }
        public string[] LoadAfter { get; set; } = Array.Empty<string>();
        public string[] LoadBefore { get; set; } = Array.Empty<string>();
        public string[] RequiredPackageIds { get; set; } = Array.Empty<string>();
        public FuseModRequirement[] ConflictsWith { get; set; } = Array.Empty<FuseModRequirement>();
        public bool Disabled { get; set; }
        public string DisabledReason { get; set; } = string.Empty;
        public bool IsLegacyConverted { get; set; }
        public bool IsLegacyHosted { get; set; }
        public string[] Faults { get; set; } = Array.Empty<string>();
        public bool LoadedFromDisk { get; set; }
        public bool AppliedToRuntime { get; set; }
        public string SkipReason { get; set; } = string.Empty;
        public string[] SkipReasons { get; set; } = Array.Empty<string>();
        public string[] RuntimeFaults { get; set; } = Array.Empty<string>();
    }
}
