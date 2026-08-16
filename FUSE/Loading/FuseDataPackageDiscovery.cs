using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
                    FusePackageFaultRegistry.MarkSkipped(manifest.Id, manifest.DisabledReason);
                    FuseLog.Warning($"FUSE skipped disabled data package '{manifest.Id}' path='{packagePath}' reason='{manifest.DisabledReason}'.");
                    continue;
                }

                if (manifest.HasBlockingFaults)
                {
                    foreach (var fault in manifest.Faults)
                    {
                        FusePackageFaultRegistry.RecordFault(manifest.Id, "dependency/load-order", fault);
                    }

                    FusePackageFaultRegistry.MarkSkipped(manifest.Id, "dependency/load-order fault");
                    FuseLog.Warning($"FUSE skipped faulted data package '{manifest.Id}' path='{packagePath}' faultCount={manifest.Faults.Count}.");
                    continue;
                }

                try
                {
                    var packageStopwatch = Stopwatch.StartNew();
                    if (manifest.IsLegacyDataPackage)
                    {
                        FuseLegacyDataConverter.LoadPackage(packagePath);
                    }
                    else
                    {
                        FuseModLoader.LoadMod(packagePath);
                    }
                    FusePackageFaultRegistry.MarkLoadedFromDisk(manifest.Id);
                    FuseLog.Info($"FUSE loaded package '{Path.GetFileName(packagePath)}' id='{manifest.Id}' from disk into resident definitions in {packageStopwatch.ElapsedMilliseconds} ms. Runtime apply has not run in this step.");
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    FusePackageFaultRegistry.RecordFault(manifest.Id, "deserialization", ex.Message, ex);
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
                info = FuseLegacyDataConverter.ReadLegacyObject(infoPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                // A malformed Info.json is an authoring error in a FUSE/UMM package, not a signal to
                // reinterpret the folder as a legacy data package. Surface the parse failure and stop.
                FuseLog.Exception($"FUSE ignored package '{folderPath}' because Info.json could not be parsed", ex);
                return false;
            }

            var id = ((string)info["Id"] ?? Path.GetFileName(folderPath)).Trim();
            if (string.Equals(id, "FUSE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var hasExplicitDataFiles = HasFuseDataFile(info["FuseDataFile"]) ||
                HasFuseDataFile(info["FuseDataFiles"]);
            var isAssetPackOnly = HasFuseAssetPacks(info) && !hasExplicitDataFiles;
            var isDataPackage = hasExplicitDataFiles ||
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
                Priority = ReadPriority(info["FuseLoadPriority"]),
                RequiredPackageIds = ReadDependencyIds(info["FuseRequires"]),
                LoadAfter = ReadDependencyIds(info["FuseLoadAfter"]),
                LoadBefore = ReadDependencyIds(info["FuseLoadBefore"]),
                Disabled = disabled,
                DisabledReason = disabledReason
            };
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
                Priority = ReadPriority(info?["FuseLoadPriority"]),
                RequiredPackageIds = legacy.RequiredPackageIds ?? Array.Empty<string>(),
                LoadAfter = legacy.LoadAfter ?? Array.Empty<string>(),
                LoadBefore = ReadDependencyIds(info?["FuseLoadBefore"]),
                Disabled = disabled,
                DisabledReason = disabledReason,
                IsLegacyDataPackage = true
            };
            return true;
        }

        private static IReadOnlyList<FusePackageManifest> SortPackages(IReadOnlyList<FusePackageManifest> packages)
        {
            var fallbackOrder = packages
                .OrderBy(package => package.Priority)
                .ThenBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(package => package.FolderPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (fallbackOrder.Length <= 1)
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

            var outgoing = fallbackOrder.ToDictionary(package => package, _ => new HashSet<FusePackageManifest>());
            var incomingCount = fallbackOrder.ToDictionary(package => package, _ => 0);

            foreach (var package in fallbackOrder)
            {
                foreach (var dependencyId in package.RequiredPackageIds)
                {
                    if (byId.TryGetValue(dependencyId, out var dependency))
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
                    if (byId.TryGetValue(dependencyId, out var dependency))
                    {
                        if (dependency.Disabled)
                        {
                            var message = $"Package '{package.Id}' declares FuseLoadAfter '{dependencyId}', but that package is disabled.";
                            if (package.IsLegacyDataPackage)
                            {
                                FuseLog.Warning($"FUSE ignored legacy order reference because it is advisory: {message}");
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
                    else
                    {
                        var message = $"Package '{package.Id}' declares FuseLoadAfter '{dependencyId}', but no matching FUSE data package was discovered.";
                        if (package.IsLegacyDataPackage)
                        {
                            FuseLog.Warning($"FUSE ignored legacy order reference because it is advisory: {message}");
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
                    if (byId.TryGetValue(dependencyId, out var dependency))
                    {
                        if (dependency.Disabled)
                        {
                            var message = $"Package '{package.Id}' declares FuseLoadBefore '{dependencyId}', but that package is disabled.";
                            if (package.IsLegacyDataPackage)
                            {
                                FuseLog.Warning($"FUSE ignored legacy order reference because it is advisory: {message}");
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
                    else
                    {
                        var message = $"Package '{package.Id}' declares FuseLoadBefore '{dependencyId}', but no matching FUSE data package was discovered.";
                        if (package.IsLegacyDataPackage)
                        {
                            FuseLog.Warning($"FUSE ignored legacy order reference because it is advisory: {message}");
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

        private static bool IsPackagePresentInModsRoot(string dependencyId)
        {
            if (string.IsNullOrWhiteSpace(dependencyId))
            {
                return false;
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
            public int Priority { get; set; }
            public string[] RequiredPackageIds { get; set; } = Array.Empty<string>();
            public string[] LoadAfter { get; set; } = Array.Empty<string>();
            public string[] LoadBefore { get; set; } = Array.Empty<string>();
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
        public bool Disabled { get; set; }
        public string DisabledReason { get; set; } = string.Empty;
        public bool IsLegacyConverted { get; set; }
        public bool IsLegacyHosted { get; set; }
        public string[] Faults { get; set; } = Array.Empty<string>();
    }
}
