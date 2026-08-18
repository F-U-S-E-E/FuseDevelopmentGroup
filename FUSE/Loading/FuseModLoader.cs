using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Model.Ops;
using FUSE.Runtime.API;
using FUSE.Authoring.Entities;
using FUSE.Authoring.Data;
using FUSE.Runtime.Events;
using FUSE.Infrastructure;
using FUSE.Runtime.Registry;
using FUSE.Authoring.Serialization;
using FUSE.Authoring.Validation;
using Newtonsoft.Json.Linq;
using Map.Runtime;
using Track;

namespace FUSE.Loading
{
    public static partial class FuseModLoader
    {
        private static readonly Dictionary<string, FuseLoadedMod> LoadedMods = new Dictionary<string, FuseLoadedMod>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> LoadedOrder = new List<string>();
        private static readonly HashSet<string> AppliedDefinitionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly FuseDefinitionValidator Validator = new FuseDefinitionValidator();
        private static bool _earlyOperationRemovalsApplied;

        public static int LoadedDefinitionCount => LoadedMods.Count;

        public static IReadOnlyList<FuseLoadedMod> GetLoadedModsInOrder()
        {
            return GetLoadedDefinitionIdsInOrder()
                .Where(id => LoadedMods.TryGetValue(id, out var loaded) && loaded?.Definition != null)
                .Select(id => LoadedMods[id])
                .ToArray();
        }

        public static FuseLoadedMod LoadMod(string modFolder)
        {
            if (string.IsNullOrWhiteSpace(modFolder))
            {
                throw new ArgumentException("Mod folder is required.", nameof(modFolder));
            }

            var definitionPaths = ResolveDefinitionPaths(modFolder);
            var definitionIdsInPackage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            FuseLoadedMod loadedMod = null;
            for (var index = 0; index < definitionPaths.Length; index++)
            {
                var definitionPath = definitionPaths[index];
                var definition = FuseSerializer.Load(definitionPath);
                var definitionId = definition?.Id;
                if (string.IsNullOrWhiteSpace(definitionId))
                {
                    FusePackageFaultRegistry.RecordFault(Path.GetFileName(modFolder), "validation", $"Definition file '{definitionPath}' is missing an id.");
                    throw new InvalidOperationException($"FUSE definition file '{definitionPath}' is missing an id.");
                }

                if (definitionIdsInPackage.TryGetValue(definitionId, out var firstPath))
                {
                    var message =
                        $"Duplicate FUSE definition id '{definitionId}' in package folder '{modFolder}'. " +
                        $"First file='{firstPath}', duplicate file='{definitionPath}'.";
                    FusePackageFaultRegistry.RecordFault(definitionId, "validation", message);
                    throw new InvalidOperationException(message);
                }

                definitionIdsInPackage.Add(definitionId, definitionPath);
                LoadDefinition(definition, modFolder, definitionPath);
                loadedMod = LoadedMods[definition.Id];
            }

            FuseLog.Info($"FUSE loaded package definitions from disk folder='{modFolder}' definitionCount={definitionPaths.Length}.");
            return loadedMod;
        }

        public static void LoadDefinition(FuseModDefinition definition, string folderPath = null, string definitionPath = null)
        {
            var validation = Validator.Validate(definition);
            FuseEvents.RaiseValidationCompleted(definition != null ? definition.Id : string.Empty, validation);
            if (!validation.IsValid)
            {
                var packageId = definition?.Id ?? Path.GetFileName(folderPath ?? definitionPath ?? string.Empty);

                // Itemize every validation error before throwing. Without this,
                // package authors see "Definition failed validation with N error(s)"
                // and have no way to know which fields actually failed.
                foreach (var error in validation.Errors)
                {
                    var code = string.IsNullOrWhiteSpace(error.Code) ? string.Empty : $" code='{error.Code}'";
                    var value = error.Value == null ? string.Empty : $" value='{error.Value}'";
                    FuseLog.Error(
                        $"FUSE validation error package='{packageId}' operation='load definition' " +
                        $"kind='{error.Field ?? string.Empty}' message='{error.Message ?? string.Empty}'{code}{value}.");
                }

                foreach (var warning in validation.Warnings)
                {
                    var code = string.IsNullOrWhiteSpace(warning.Code) ? string.Empty : $" code='{warning.Code}'";
                    var value = warning.Value == null ? string.Empty : $" value='{warning.Value}'";
                    FuseLog.Warning(
                        $"FUSE validation warning package='{packageId}' operation='load definition' " +
                        $"kind='{warning.Field ?? string.Empty}' message='{warning.Message ?? string.Empty}'{code}{value}.");
                }

                FusePackageFaultRegistry.RecordFault(packageId, "validation", $"Definition failed validation with {validation.Errors.Count} error(s).");
                throw new InvalidOperationException($"FUSE definition '{definition?.Id ?? "<null>"}' failed validation with {validation.Errors.Count} error(s).");
            }

            RegisterLoadedDefinition(definition, folderPath, definitionPath);
        }

        public static int ApplyLoadedDefinitions(string reason)
        {
            var ids = GetLoadedDefinitionIdsInOrder();
            // Map packages participate in the shared load catalog and initial
            // track groups only while their map is the active session map;
            // otherwise their content must leave the stock map untouched.
            var applicableIds = ids
                .Where(id => !LoadedMods.TryGetValue(id, out var mod) ||
                             mod?.Definition == null ||
                             FuseMapSession.ShouldApplyDefinition(mod.Definition))
                .ToArray();
            ApplyGlobalLoadCatalog(applicableIds, reason);
            PreEnableInitialTrackGroups(applicableIds);

            var candidates = new List<FuseStagedApplyCandidate>();
            var outcomes = new List<PackageApplyOutcome>();
            foreach (var id in ids)
            {
                if (!LoadedMods.TryGetValue(id, out var loaded) || loaded?.Definition == null)
                {
                    FuseLog.Warning($"FUSE apply skipped package='{id}' operation='runtime apply' id='{id}' reason='resident definition missing'.");
                    outcomes.Add(PackageApplyOutcome.ForSkipped(id, "resident definition missing"));
                    continue;
                }

                try
                {
                    if (!FuseModRequirementResolver.ShouldApply(loaded, out var requirementReason))
                    {
                        FusePackageFaultRegistry.MarkSkipped(id, requirementReason);
                        outcomes.Add(PackageApplyOutcome.ForSkipped(id, requirementReason));
                        FuseLog.Info(
                            $"FUSE skipped conditional mixinto package='{id}' operation='runtime apply' " +
                            $"id='{id}' reason='{requirementReason}'.");
                        continue;
                    }

                    if (!FuseMapSession.ShouldApplyDefinition(loaded.Definition))
                    {
                        var mapSkipReason = FuseMapSession.InactiveSkipReason(FuseMapSession.ActiveMapId);
                        ReleaseInactiveMapPackageClaims(id);
                        FusePackageFaultRegistry.MarkSkipped(id, mapSkipReason);
                        outcomes.Add(PackageApplyOutcome.ForSkipped(id, mapSkipReason));
                        FuseLog.Info(
                            $"FUSE skipped map package='{id}' operation='runtime apply' " +
                            $"id='{id}' reason='{mapSkipReason}'.");
                        continue;
                    }

                    candidates.Add(new FuseStagedApplyCandidate(loaded, AppliedDefinitionIds.Contains(id)));
                }
                catch (Exception ex)
                {
                    FusePackageFaultRegistry.RecordFault(id, "runtime apply", ex.Message, ex);
                    FusePackageFaultRegistry.MarkSkipped(id, "runtime apply exception");
                    outcomes.Add(PackageApplyOutcome.ForErrored(id, ex.Message));
                    FuseLog.Exception($"Failed to prepare loaded FUSE definition '{id}' for runtime apply '{reason ?? "unspecified"}'", ex);
                }
            }

            var appliedCount = ApplyDefinitionsToRuntimeStaged(candidates, reason, outcomes);
            LogAggregateApplySummary(reason, outcomes);
            FuseLog.Info($"FUSE applied {appliedCount} resident definition(s) to runtime for '{reason ?? "unspecified"}'.");
            return appliedCount;
        }

        public static int ReapplyLoadedDefinitions(string reason)
        {
            return ApplyLoadedDefinitions(reason);
        }

        /// <summary>
        /// A map package that was applied in an earlier session but is inactive
        /// for this one still holds registry claims from that apply. Release
        /// them (same set the definition-replacement path releases) so the
        /// claims don't shadow other packages while the map sits dormant.
        /// </summary>
        private static void ReleaseInactiveMapPackageClaims(string id)
        {
            if (!AppliedDefinitionIds.Contains(id))
            {
                return;
            }

            try
            {
                FuseAudioAPI.ReleasePackage(id);
                FuseWorldSuppressor.ReleasePackage(id);
                var released = FuseRegistry.ReleaseAllForPackage(id);
                AppliedDefinitionIds.Remove(id);
                FuseLog.Info(
                    $"FUSE released claims for inactive map package='{id}' operation='runtime apply' " +
                    $"id='{id}' releasedClaims={released}.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE failed to release claims for inactive map package '{id}'", ex);
            }
        }

        private static void RegisterLoadedDefinition(FuseModDefinition definition, string folderPath, string definitionPath)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            {
                throw new InvalidOperationException("FUSE definition is missing an id.");
            }

            var firstLoad = !LoadedMods.ContainsKey(definition.Id);
            if (!firstLoad)
            {
                // A definition is being replaced. Release any registry claims the
                // prior version held; the upcoming apply will re-claim what it
                // still owns. Otherwise stale claims would block the new apply.
                FuseAudioAPI.ReleasePackage(definition.Id);
                FuseWorldSuppressor.ReleasePackage(definition.Id);
                var released = FuseRegistry.ReleaseAllForPackage(definition.Id);
                AppliedDefinitionIds.Remove(definition.Id);
                FuseLog.Info(
                    $"FUSE definition replacement package='{definition.Id}' operation='replace loaded definition' id='{definition.Id}' releasedClaims={released}.");
            }

            LoadedMods[definition.Id] = new FuseLoadedMod(folderPath, definitionPath, definition);
            if (firstLoad && !LoadedOrder.Contains(definition.Id, StringComparer.OrdinalIgnoreCase))
            {
                LoadedOrder.Add(definition.Id);
            }

            try
            {
                FuseMapPackageRegistry.RegisterFromDefinition(LoadedMods[definition.Id]);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE failed to register map declaration for '{definition.Id}'", ex);
            }

            FuseLog.Info($"FUSE loaded definition package='{definition.Id}' operation='load definition' id='{definition.Id}' definitionPath='{definitionPath ?? string.Empty}' folderPath='{folderPath ?? string.Empty}'.");
            if (firstLoad)
            {
                FuseEvents.RaiseModLoaded(definition.Id);
            }
        }

        private static void LogAggregateApplySummary(string reason, List<PackageApplyOutcome> outcomes)
        {
            if (outcomes == null || outcomes.Count == 0)
            {
                FuseLog.Info($"FUSE aggregate apply summary reason='{reason ?? "unspecified"}' total applied=0 skipped=0 errored=0.");
                return;
            }

            foreach (var outcome in outcomes)
            {
                var line =
                    $"FUSE aggregate apply package='{outcome.PackageId}' operation='runtime apply' " +
                    $"applied={outcome.Applied} skipped={outcome.Skipped} errored={outcome.Errored} " +
                    $"created={outcome.CreatedObjects} updated={outcome.UpdatedObjects} removed={outcome.RemovedObjects} " +
                    $"objectSkipped={outcome.SkippedObjects} warnings={outcome.Warnings} errors={outcome.Errors} " +
                    $"reason='{outcome.Reason}'.";
                if (outcome.Errored > 0 ||
                    (outcome.Skipped > 0 && !FusePackageFaultRegistry.IsOptionalSkipReason(outcome.Reason)))
                {
                    FuseLog.Warning(line);
                }
                else
                {
                    FuseLog.Info(line);
                }
            }

            FuseLog.Info(
                $"FUSE aggregate apply total operation='runtime apply' reason='{reason ?? "unspecified"}' " +
                $"applied={outcomes.Sum(item => item.Applied)} skipped={outcomes.Sum(item => item.Skipped)} " +
                $"errored={outcomes.Sum(item => item.Errored)} created={outcomes.Sum(item => item.CreatedObjects)} " +
                $"updated={outcomes.Sum(item => item.UpdatedObjects)} removed={outcomes.Sum(item => item.RemovedObjects)} " +
                $"objectSkipped={outcomes.Sum(item => item.SkippedObjects)} warnings={outcomes.Sum(item => item.Warnings)} " +
                $"errors={outcomes.Sum(item => item.Errors)}.");
        }

        private static string[] GetLoadedDefinitionIdsInOrder()
        {
            return LoadedOrder
                .Where(id => LoadedMods.ContainsKey(id))
                .Concat(LoadedMods.Keys.Where(id => !LoadedOrder.Contains(id, StringComparer.OrdinalIgnoreCase)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
                .ToArray();
        }

        private static FuseApplyReport ApplyDefinitionToRuntime(FuseLoadedMod loaded, bool reapply, string reason)
        {
            var definition = loaded?.Definition;
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            {
                throw new InvalidOperationException("FUSE definition is missing an id.");
            }

            var transaction = new FuseApplyTransaction(definition.Id, reason, reapply);
            var referenceContext = BuildPreflightReferenceContext(
                GetLoadedModsInOrder()
                    .Select(mod => mod?.Definition)
                    .Concat(new[] { definition }));
            transaction.RunPhase("preflight-validation", () => RunPreflightValidation(definition, transaction, referenceContext));

            using (var registryTx = FuseRegistry.BeginReapplyTransaction(definition.Id))
            {
                if (!transaction.Report.IsFatal)
                {
                    TrackAPI.BeginBatch();
                    try
                    {
                        transaction.RunPhase("apply-removals", () =>
                        {
                            ApplyTrackRemovals(definition, transaction);
                            ApplyWorldRemovals(definition, transaction);
                            ApplyOperationsRemovals(definition, transaction);
                        });
                        transaction.RunPhase("apply-nodes", () =>
                        {
                            ApplyTrackNodes(definition, transaction);
                            ApplyTurntables(definition, transaction);
                        });
                        transaction.RunPhase("apply-segments", () => ApplyTrackSegments(definition, transaction));
                    }
                    finally
                    {
                        TrackAPI.EndBatch(false);
                    }

                    if (MutatesTrackStructure(definition))
                    {
                        transaction.RunPhase("single-graph-rebuild", () =>
                        {
                            TrackAPI.RebuildGraph();
                            transaction.PostBind("graph", definition.Id, "rebuilt-before-spans");
                        });
                    }
                    else
                    {
                        // Spans validate against the current route graph, but they
                        // do not themselves require node/segment graph rebuilds.
                        // If this package only adds spans/areas/ops, keep the
                        // existing graph and avoid an unnecessary rebuild.
                        FuseLog.Info(
                            $"FUSE apply phase package='{definition.Id}' " +
                            "operation='single-graph-rebuild' skipped: no structural track mutations in this package.");
                        transaction.PostBind("graph", definition.Id, "rebuild-skipped");
                    }

                    // IMPORTANT: spans must be applied after the node/segment
                    // graph has been rebuilt. TrackSpan route validation asks
                    // the runtime graph for a valid route; applying spans before
                    // RebuildGraph() makes valid legacy spans
                    // intermittently fail with "span did not resolve to a valid route".
                    TrackAPI.BeginBatch();
                    try
                    {
                        transaction.RunPhase("apply-spans", () => ApplyTrackSpans(definition, transaction));
                    }
                    finally
                    {
                        TrackAPI.EndBatch(false);
                    }
                    transaction.RunPhase("apply-audio", () => ApplyAudioDefinition(definition, loaded.FolderPath, transaction));
                    transaction.RunPhase("apply-world-objects", () => ApplyWorldDefinition(definition, loaded.FolderPath, loaded.DefinitionPath, transaction));
                    transaction.RunPhase("apply-operations", () =>
                    {
                        IndustryAPI.BeginIndustryApplyBatch();
                        try
                        {
                            ApplyTrackAreas(definition, transaction, false);
                            ApplyOperationsDefinition(definition, transaction);
                        }
                        finally
                        {
                            IndustryAPI.EndIndustryApplyBatch("resident ApplyOperationsDefinition");
                        }
                        TrackAPI.ApplyAreaOrdering();
                    });
                    ApplyDeferredOperationBindings(new[] { definition.Id }, reason);
                    transaction.RunPhase("apply-scene-clones", () => ApplySceneClones(definition, loaded.DefinitionPath, transaction));
                    transaction.RunPhase("apply-progression", () => ApplyProgressionDefinition(definition, transaction));
                    transaction.RunPhase("apply-world-suppressions", () => FuseWorldSuppressor.ApplyDefinition(definition, transaction));
                    transaction.RunPhase("post-bind-validation", () => ValidatePostBind(definition, transaction));
                }

                // Commit only on non-fatal apply. A fatal apply leaves the prior
                // claim snapshot intact (transaction Dispose triggers Rollback).
                if (!transaction.Report.IsFatal)
                {
                    registryTx.Commit();
                }
            }

            if (transaction.Report.IsFatal)
            {
                FuseLog.Warning($"FUSE skipped runtime mutation for definition '{definition.Id}' after fatal apply report reason='{transaction.Report.FatalReason}'.");
            }

            transaction.Report.LogSummary();

            if (!transaction.Report.IsFatal && !transaction.Report.HasErrors)
            {
                FuseLog.Info(reapply
                    ? $"FUSE reapplied resident definition '{definition.Id}' to runtime for '{reason ?? "unspecified"}' ({definition.Operations?.Turntables?.Count ?? 0} turntable(s), {definition.World?.SceneClones?.Count ?? 0} scene clone(s))."
                    : $"FUSE applied resident definition '{definition.Id}' to runtime for '{reason ?? "unspecified"}' ({definition.Operations?.Turntables?.Count ?? 0} turntable(s), {definition.World?.SceneClones?.Count ?? 0} scene clone(s)).");
            }
            else
            {
                FuseLog.Warning($"FUSE runtime apply for resident definition '{definition.Id}' completed with fatal={transaction.Report.IsFatal} errors={transaction.Report.Errors.Count}; package marked faulted.");
            }

            return transaction.Report;
        }

        public static void UnloadMod(string modId)
        {
            UnloadMod(modId, restoreTrackSnapshots: true);
        }

        internal static void UnloadMod(string modId, bool restoreTrackSnapshots)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                return;
            }

            if (LoadedMods.Remove(modId))
            {
                if (restoreTrackSnapshots)
                {
                    try
                    {
                        FuseTrackRemovalSnapshotStore.RestorePackage(modId);
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Exception($"FUSE failed to restore removed base-game track for unloaded mod '{modId}'", ex);
                    }
                }
                else
                {
                    if (FuseTrackRemovalSnapshotStore.ClearPackage(modId))
                    {
                        FuseLog.Info($"FUSE skipped removed base-game track snapshot restore for '{modId}' because the map is unloading or a staged reload requested no restore.");
                    }
                }

                try
                {
                    FuseAudioAPI.ReleasePackage(modId);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE failed to release audio definitions for unloaded mod '{modId}'", ex);
                }

                try
                {
                    FuseWorldSuppressor.ReleasePackage(modId);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE failed to release world suppressions for unloaded mod '{modId}'", ex);
                }

                try
                {
                    MapAPI.ReleaseTelegraphPoleMovements(modId);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE failed to release telegraph pole movements for unloaded mod '{modId}'", ex);
                }

                try
                {
                    SpawnPointAPI.ReleasePackage(modId);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE failed to release spawn points for unloaded mod '{modId}'", ex);
                }

                // Release registry claims before any other unload work so other
                // packages can claim those ids on the next apply if needed.
                try
                {
                    var released = FuseRegistry.ReleaseAllForPackage(modId);
                    if (released > 0)
                    {
                        FuseLog.Info($"FUSE released {released} registry claim(s) for unloaded mod '{modId}'.");
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE failed to release registry claims for unloaded mod '{modId}'", ex);
                }

                LoadedOrder.RemoveAll(id => string.Equals(id, modId, StringComparison.OrdinalIgnoreCase));
                AppliedDefinitionIds.Remove(modId);
                FusePackageFaultRegistry.ClearPackage(modId);
                try
                {
                    FuseMapTileRegistry.UnregisterTileSources(modId);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE failed to unregister map tile sources for unloaded mod '{modId}'", ex);
                }

                FuseMapPackageRegistry.Unregister(modId);

                try
                {
                    FuseEvents.RaiseModUnloaded(modId);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE mod-unloaded event failed for '{modId}'", ex);
                }
            }
        }

        public static void UnloadAll()
        {
            UnloadAll(true);
        }

        internal static void UnloadAll(bool resetDiscovery)
        {
            UnloadAll(resetDiscovery, restoreTrackSnapshots: true);
        }

        internal static void UnloadAll(bool resetDiscovery, bool restoreTrackSnapshots)
        {
            var loadedIds = LoadedMods.Keys.ToArray();
            for (var index = 0; index < loadedIds.Length; index++)
            {
                try
                {
                    UnloadMod(loadedIds[index], restoreTrackSnapshots);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE failed to unload mod '{loadedIds[index]}'", ex);
                }
            }

            LoadedOrder.Clear();
            AppliedDefinitionIds.Clear();
            _earlyOperationRemovalsApplied = false;
            FuseTrackRemovalSnapshotStore.ClearAll();
            if (restoreTrackSnapshots)
            {
                MapAPI.RestoreAllTelegraphPoleMovements("unload all");
            }
            SpawnPointAPI.ClearRuntimeCache();
            TrackAPI.ClearRuntimeMetadata();
            if (restoreTrackSnapshots)
            {
                FuseWorldSuppressor.RestoreAll("unload all");
            }
            FuseMapPackageRegistry.Clear();
            // Per-mod claims were released in UnloadMod; reset registry to drop
            // any orphaned shared claims and the conflict history.
            FuseRegistry.Reset();
            if (resetDiscovery)
            {
                FuseDataPackageDiscovery.ResetDiscovery();
                FuseAssetPackRegistry.Reset();
            }
        }

        public static IEnumerable<string> GetLoadedMods()
        {
            return LoadedMods.Keys;
        }

        internal static int ApplyLoadedOperationRemovalsEarly(string reason)
        {
            if (_earlyOperationRemovalsApplied)
            {
                return 0;
            }

            _earlyOperationRemovalsApplied = true;
            var removed = 0;
            var operation = string.IsNullOrWhiteSpace(reason) ? "early operation removals" : reason;

            foreach (var loaded in GetLoadedModsInOrder())
            {
                var definition = loaded?.Definition;
                if (!FuseMapSession.ShouldApplyDefinition(definition))
                {
                    continue;
                }

                var industryIds = definition?.Operations?.Removals?.Industries;
                if (industryIds == null || industryIds.Length == 0)
                {
                    continue;
                }

                foreach (var id in industryIds)
                {
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    try
                    {
                        if (IndustryAPI.TryRemoveIndustry(id, false))
                        {
                            removed++;
                            FuseLog.Info(
                                $"FUSE early operation removal removed industry '{id}' " +
                                $"package='{definition.Id ?? string.Empty}' reason='{operation}'.");
                        }
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Warning(
                            $"FUSE early operation removal failed industry='{id}' " +
                            $"package='{definition.Id ?? string.Empty}' reason='{operation}' message='{ex.Message}'.");
                    }
                }
            }

            if (removed > 0)
            {
                FuseLog.Info($"FUSE early operation removals completed reason='{operation}' removedIndustries={removed}.");
            }

            return removed;
        }

        public static bool IsApplied(string modId)
        {
            return !string.IsNullOrWhiteSpace(modId) && AppliedDefinitionIds.Contains(modId);
        }

        public static FuseModDefinition GetLoadedDefinition(string modId)
        {
            return !string.IsNullOrWhiteSpace(modId) && LoadedMods.TryGetValue(modId, out var loaded)
                ? loaded.Definition
                : null;
        }

        public static bool TryGetLoadedMod(string modId, out FuseLoadedMod loaded)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                loaded = null;
                return false;
            }

            return LoadedMods.TryGetValue(modId, out loaded);
        }

        public static FuseLoadedMod GetLoadedMod(string modId)
        {
            TryGetLoadedMod(modId, out var loaded);
            return loaded;
        }

        public static FuseModDefinition ImportFromJson(string jsonPath)
        {
            return FuseSerializer.Load(jsonPath);
        }

        public static void ExportToJson(string modId, string outputPath)
        {
            var definition = GetLoadedDefinition(modId);
            if (definition == null)
            {
                throw new InvalidOperationException($"FUSE mod '{modId}' is not loaded.");
            }

            FuseSerializer.SaveJson(definition, outputPath);
        }

        public static void SaveAsBson(FuseModDefinition definition, string outputPath)
        {
            FuseSerializer.SaveBson(definition, outputPath);
        }

        internal static string ResolveDefinitionPath(string modFolder)
        {
            return ResolveDefinitionPaths(modFolder)[0];
        }

        internal static string[] ResolveDefinitionPaths(string modFolder)
        {
            var infoPath = Path.Combine(modFolder, "Info.json");
            if (File.Exists(infoPath))
            {
                var info = FuseLegacyDataConverter.ReadLegacyObject(infoPath);
                var explicitPaths = ResolveExplicitDefinitionPaths(modFolder, info).ToArray();
                if (explicitPaths.Length > 0)
                {
                    return explicitPaths;
                }

                if (HasFuseAssetPacks(info))
                {
                    throw new FileNotFoundException($"FUSE package '{modFolder}' is asset-pack-only and does not declare FuseDataFile or FuseDataFiles.");
                }
            }

            var fallbackPaths = FuseDefinitionFileDiscovery.ResolveFallbackDefinitionPaths(modFolder);
            if (fallbackPaths.Length > 0)
            {
                return fallbackPaths;
            }

            throw new FileNotFoundException($"No FUSE .bson or .json definition was found in '{modFolder}'.");
        }

        private static bool HasFuseAssetPacks(JObject info)
        {
            return info != null && info["FuseAssetPacks"] != null;
        }

        private static IEnumerable<string> ResolveExplicitDefinitionPaths(string modFolder, JObject info)
        {
            foreach (var fuseDataFile in EnumerateFuseDataFiles(info["FuseDataFile"]))
            {
                yield return ResolveExistingDefinitionPath(modFolder, fuseDataFile);
            }

            foreach (var fuseDataFile in EnumerateFuseDataFiles(info["FuseDataFiles"]))
            {
                yield return ResolveExistingDefinitionPath(modFolder, fuseDataFile);
            }
        }

        private static IEnumerable<string> EnumerateFuseDataFiles(JToken token)
        {
            if (token == null)
            {
                yield break;
            }

            if (token.Type == JTokenType.String)
            {
                var value = (string)token;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }

                yield break;
            }

            if (token.Type != JTokenType.Array)
            {
                yield break;
            }

            foreach (var item in token.Children())
            {
                if (item.Type != JTokenType.String)
                {
                    continue;
                }

                var value = (string)item;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }

        private static string ResolveExistingDefinitionPath(string modFolder, string fuseDataFile)
        {
            var explicitPath = Path.Combine(modFolder, fuseDataFile);
            if (!File.Exists(explicitPath))
            {
                throw new FileNotFoundException($"FUSE data file '{fuseDataFile}' was not found in '{modFolder}'.", explicitPath);
            }

            return explicitPath;
        }
    }
}
