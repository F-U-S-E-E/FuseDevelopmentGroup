using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Model.Ops;
using FUSE.API;
using FUSE.Authoring;
using FUSE.Data;
using FUSE.Events;
using FUSE.Infrastructure;
using FUSE.Registry;
using FUSE.Serialization;
using FUSE.Validation;
using Newtonsoft.Json.Linq;
using Track;

namespace FUSE.Loading
{
    public static class FuseModLoader
    {
        private static readonly Dictionary<string, FuseLoadedMod> LoadedMods = new Dictionary<string, FuseLoadedMod>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> LoadedOrder = new List<string>();
        private static readonly HashSet<string> AppliedDefinitionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly FuseDefinitionValidator Validator = new FuseDefinitionValidator();

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
                FusePackageFaultRegistry.RecordFault(definition?.Id ?? Path.GetFileName(folderPath ?? definitionPath ?? string.Empty), "validation", $"Definition failed validation with {validation.Errors.Count} error(s).");
                throw new InvalidOperationException($"FUSE definition '{definition?.Id ?? "<null>"}' failed validation with {validation.Errors.Count} error(s).");
            }

            RegisterLoadedDefinition(definition, folderPath, definitionPath);
        }

        public static int ApplyLoadedDefinitions(string reason)
        {
            var ids = GetLoadedDefinitionIdsInOrder();
            var appliedCount = 0;
            var outcomes = new List<PackageApplyOutcome>();
            foreach (var id in ids)
            {
                if (!LoadedMods.TryGetValue(id, out var loaded) || loaded?.Definition == null)
                {
                    FuseLog.Warning($"FUSE apply skipped package='{id}' operation='runtime apply' id='{id}' reason='resident definition missing'.");
                    outcomes.Add(PackageApplyOutcome.ForSkipped(id, "resident definition missing"));
                    continue;
                }

                var isReapply = AppliedDefinitionIds.Contains(id);
                try
                {
                    var report = ApplyDefinitionToRuntime(loaded, isReapply, reason);
                    if (report.IsFatal)
                    {
                        FusePackageFaultRegistry.RecordFault(id, "runtime apply", report.FatalReason);
                        FusePackageFaultRegistry.MarkSkipped(id, report.FatalReason);
                        outcomes.Add(PackageApplyOutcome.FromReport(id, report, 0, 1, 1));
                        continue;
                    }

                    if (report.HasErrors)
                    {
                        FusePackageFaultRegistry.RecordFault(id, "runtime apply", $"Runtime apply completed with {report.Errors.Count} error(s).");
                        outcomes.Add(PackageApplyOutcome.FromReport(id, report, 0, 0, 1));
                        continue;
                    }

                    AppliedDefinitionIds.Add(id);
                    FusePackageFaultRegistry.MarkAppliedToRuntime(id);
                    appliedCount++;
                    outcomes.Add(PackageApplyOutcome.FromReport(id, report, 1, 0, 0));
                }
                catch (Exception ex)
                {
                    FusePackageFaultRegistry.RecordFault(id, "runtime apply", ex.Message, ex);
                    FusePackageFaultRegistry.MarkSkipped(id, "runtime apply exception");
                    outcomes.Add(PackageApplyOutcome.ForErrored(id, ex.Message));
                    FuseLog.Exception($"Failed to apply loaded FUSE definition '{id}' to runtime for '{reason ?? "unspecified"}'", ex);
                }
            }

            LogAggregateApplySummary(reason, outcomes);
            FuseLog.Info($"FUSE applied {appliedCount} resident definition(s) to runtime for '{reason ?? "unspecified"}'.");
            return appliedCount;
        }

        public static int ReapplyLoadedDefinitions(string reason)
        {
            return ApplyLoadedDefinitions(reason);
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

            FuseLog.Info($"FUSE loaded definition package='{definition.Id}' operation='load definition' id='{definition.Id}' definitionPath='{definitionPath ?? string.Empty}' folderPath='{folderPath ?? string.Empty}'.");
            if (firstLoad)
            {
                FuseEvents.RaiseModLoaded(definition.Id);
            }
        }

        private static void LogAggregateApplySummary(string reason, IReadOnlyList<PackageApplyOutcome> outcomes)
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
                if (outcome.Errored > 0 || outcome.Skipped > 0)
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
            transaction.RunPhase("preflight-validation", () => RunPreflightValidation(definition, transaction));

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
                        });
                        transaction.RunPhase("apply-nodes", () =>
                        {
                            ApplyTrackNodes(definition, transaction);
                            ApplyTurntables(definition, transaction);
                        });
                        transaction.RunPhase("apply-segments", () => ApplyTrackSegments(definition, transaction));
                        transaction.RunPhase("apply-spans", () => ApplyTrackSpans(definition, transaction));
                    }
                    finally
                    {
                        TrackAPI.EndBatch(false);
                    }

                    if (MutatesTrackGraph(definition))
                    {
                        transaction.RunPhase("single-graph-rebuild", () =>
                        {
                            TrackAPI.RebuildGraph();
                            transaction.PostBind("graph", definition.Id, "rebuilt");
                        });
                    }
                    else
                    {
                        // No track nodes/segments/spans/turntables/removals were
                        // mutated by this package, so the existing graph is still
                        // current. Skipping the rebuild typically saves 300-700 ms
                        // per non-track package on multi-package map loads.
                        FuseLog.Info(
                            $"FUSE apply phase package='{definition.Id}' " +
                            "operation='single-graph-rebuild' skipped: no track mutations in this package.");
                        transaction.PostBind("graph", definition.Id, "rebuild-skipped");
                    }
                    transaction.RunPhase("apply-audio", () => ApplyAudioDefinition(definition, loaded.FolderPath, transaction));
                    transaction.RunPhase("apply-world-objects", () => ApplyWorldDefinition(definition, loaded.FolderPath, loaded.DefinitionPath, transaction));
                    transaction.RunPhase("apply-operations", () =>
                    {
                        ApplyTrackAreas(definition, transaction);
                        ApplyOperationsDefinition(definition, transaction);
                    });
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

        private static void RunPreflightValidation(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            var validation = Validator.Validate(definition);
            FuseEvents.RaiseValidationCompleted(definition != null ? definition.Id : string.Empty, validation);

            foreach (var warning in validation.Warnings)
            {
                transaction.Warning("preflight", warning.Field, FormatValidationIssue(warning));
            }

            foreach (var error in validation.Errors)
            {
                transaction.Error("preflight", error.Field, FormatValidationIssue(error));
            }

            ValidateRuntimeReferences(definition, transaction);
            if (transaction.Report.Errors.Count > 0)
            {
                transaction.Fatal("definition", definition?.Id ?? string.Empty, $"preflight validation failed with {transaction.Report.Errors.Count} error(s)");
            }
        }

        private static string FormatValidationIssue(ValidationIssue issue)
        {
            if (issue == null)
            {
                return string.Empty;
            }

            var code = string.IsNullOrWhiteSpace(issue.Code) ? string.Empty : $" code='{issue.Code}'";
            var value = issue.Value == null ? string.Empty : $" value='{issue.Value}'";
            return $"{issue.Message}{code}{value}";
        }

        private static void ValidateRuntimeReferences(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            if (definition == null)
            {
                transaction.Error("definition", string.Empty, "definition is null");
                return;
            }

            if (RequiresGraph(definition) && Graph.Shared == null)
            {
                transaction.Error("graph", definition.Id, "Railroader Graph.Shared is not available before track apply");
                return;
            }

            ValidateTrackRuntimeReferences(definition, transaction);
            ValidateOperationRuntimeReferences(definition, transaction);
        }

        private static bool RequiresGraph(FuseModDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return (definition.Tracks?.Nodes?.Count ?? 0) > 0 ||
                   (definition.Tracks?.Segments?.Count ?? 0) > 0 ||
                   (definition.Tracks?.Spans?.Count ?? 0) > 0 ||
                   (definition.Tracks?.Areas?.Count ?? 0) > 0 ||
                   HasAny(definition.Tracks?.Removals?.Nodes) ||
                   HasAny(definition.Tracks?.Removals?.Segments) ||
                   HasAny(definition.Tracks?.Removals?.Spans) ||
                   (definition.Operations?.Turntables?.Count ?? 0) > 0;
        }

        /// <summary>
        /// Returns true only when this definition adds, removes, or modifies
        /// track nodes/segments/spans, or contains turntables (which create
        /// pit nodes and roundhouse segments). Areas reference existing
        /// segments but do not require a graph rebuild.
        /// </summary>
        private static bool MutatesTrackGraph(FuseModDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return (definition.Tracks?.Nodes?.Count ?? 0) > 0 ||
                   (definition.Tracks?.Segments?.Count ?? 0) > 0 ||
                   (definition.Tracks?.Spans?.Count ?? 0) > 0 ||
                   HasAny(definition.Tracks?.Removals?.Nodes) ||
                   HasAny(definition.Tracks?.Removals?.Segments) ||
                   HasAny(definition.Tracks?.Removals?.Spans) ||
                   (definition.Operations?.Turntables?.Count ?? 0) > 0;
        }

        private static bool HasAny(IEnumerable<string> values)
        {
            return values != null && values.Any(value => !string.IsNullOrWhiteSpace(value));
        }

        private static void ValidateTrackRuntimeReferences(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            var tracks = definition.Tracks;
            if (tracks == null)
            {
                return;
            }

            var definedNodes = new HashSet<string>(tracks.Nodes?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var definedSegments = new HashSet<string>(tracks.Segments?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var generatedNodes = CollectGeneratedNodeIds(definition);
            var generatedSegments = CollectGeneratedSegmentIds(definition);

            foreach (var segment in tracks.Segments ?? new Dictionary<string, FuseSegment>())
            {
                ValidateNodeReference(segment.Key, "startNodeId", segment.Value?.StartNodeId, definedNodes, generatedNodes, transaction);
                ValidateNodeReference(segment.Key, "endNodeId", segment.Value?.EndNodeId, definedNodes, generatedNodes, transaction);
            }

            foreach (var span in tracks.Spans ?? new Dictionary<string, FuseSpan>())
            {
                ValidateSegmentReference(span.Key, "upper", span.Value?.Upper?.SegmentId, definedSegments, generatedSegments, transaction);
                ValidateSegmentReference(span.Key, "lower", span.Value?.Lower?.SegmentId, definedSegments, generatedSegments, transaction);
            }
        }

        private static void ValidateOperationRuntimeReferences(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            var operations = definition.Operations;
            if (operations == null)
            {
                return;
            }

            var definedLoads = new HashSet<string>(operations.Loads?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var definedSpans = new HashSet<string>(definition.Tracks?.Spans?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (var industry in operations.Industries ?? new Dictionary<string, FuseIndustry>())
            {
                foreach (var component in industry.Value?.Components ?? new Dictionary<string, FuseIndustryComponent>())
                {
                    var componentId = $"{industry.Key}.{component.Key}";
                    ValidateOptionalLoadReference(componentId, component.Value?.LoadId, definedLoads, transaction);
                    ValidateOptionalSpanReferences(componentId, component.Value?.TrackSpanIds, definedSpans, transaction);
                    ValidateOptionalSpanReferences(componentId, component.Value?.InputSpanIds, definedSpans, transaction);
                }
            }

            foreach (var loader in operations.Loaders ?? new Dictionary<string, FuseLoader>())
            {
                if (!string.IsNullOrWhiteSpace(loader.Value?.IndustryId) &&
                    (operations.Industries == null || !operations.Industries.ContainsKey(loader.Value.IndustryId)) &&
                    IndustryAPI.GetIndustry(loader.Value.IndustryId) == null)
                {
                    transaction.Warning("loader", loader.Key, $"industryId '{loader.Value.IndustryId}' is not defined in this package or runtime");
                }
            }

            foreach (var station in operations.Stations ?? new Dictionary<string, FuseStation>())
            {
                if (!string.IsNullOrWhiteSpace(station.Value?.PassengerStopId) &&
                    !HasPassengerStop(definition, station.Value.PassengerStopId))
                {
                    transaction.Warning("station", station.Key, $"passengerStopId '{station.Value.PassengerStopId}' was not found in this package or runtime");
                }
            }
        }

        private static void ValidateNodeReference(string segmentId, string field, string nodeId, ISet<string> definedNodes, ISet<string> generatedNodes, FuseApplyTransaction transaction)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || definedNodes.Contains(nodeId) || generatedNodes.Contains(nodeId))
            {
                return;
            }

            if (TrackAPI.GetNode(nodeId) != null)
            {
                return;
            }

            // Not yet defined; treat as a soft external reference. apply-nodes runs
            // before apply-segments, and turntables in the same package contribute
            // pit/roundhouse nodes during apply-nodes too. If still missing at
            // apply-segments, the segment apply itself will surface the failure.
            transaction.Warning(
                "track segment",
                segmentId,
                $"{field} references node '{nodeId}' that is not defined in this FUSE document. " +
                "It must exist in the base game graph at runtime or be generated during apply.");
        }

        private static void ValidateSegmentReference(string spanId, string field, string segmentId, ISet<string> definedSegments, ISet<string> generatedSegments, FuseApplyTransaction transaction)
        {
            if (string.IsNullOrWhiteSpace(segmentId) || definedSegments.Contains(segmentId) || generatedSegments.Contains(segmentId))
            {
                return;
            }

            if (TrackAPI.GetSegment(segmentId) == null)
            {
                transaction.Error("track span", spanId, $"{field} references missing segment '{segmentId}'");
            }
        }

        private static void ValidateOptionalSpanReferences(string componentId, IEnumerable<string> spanIds, ISet<string> definedSpans, FuseApplyTransaction transaction)
        {
            if (spanIds == null)
            {
                return;
            }

            foreach (var spanId in spanIds)
            {
                if (string.IsNullOrWhiteSpace(spanId) || definedSpans.Contains(spanId))
                {
                    continue;
                }

                if (TrackAPI.GetSpan(spanId) == null)
                {
                    transaction.Warning("industry component", componentId, $"track span '{spanId}' was not found in this package or runtime");
                }
            }
        }

        private static void ValidateOptionalLoadReference(string componentId, string loadId, ISet<string> definedLoads, FuseApplyTransaction transaction)
        {
            if (string.IsNullOrWhiteSpace(loadId) || definedLoads.Contains(loadId))
            {
                return;
            }

            if (LoadAPI.GetLoad(loadId) == null)
            {
                transaction.Warning("industry component", componentId, $"loadId '{loadId}' was not found in this package or runtime");
            }
        }

        private static bool HasPassengerStop(FuseModDefinition definition, string passengerStopId)
        {
            foreach (var industry in definition.Operations?.Industries ?? new Dictionary<string, FuseIndustry>())
            {
                foreach (var component in industry.Value?.Components ?? new Dictionary<string, FuseIndustryComponent>())
                {
                    if (string.Equals(component.Value?.PassengerStopId, passengerStopId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(component.Key, passengerStopId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return IndustryAPI.GetIndustry(passengerStopId) != null;
        }

        private static HashSet<string> CollectGeneratedSegmentIds(FuseModDefinition definition)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var turntable in definition.Operations?.Turntables ?? new Dictionary<string, FuseTurntable>())
            {
                var roundhouse = turntable.Value?.Roundhouse;
                if (roundhouse == null || roundhouse.Stalls <= 0)
                {
                    continue;
                }

                for (var index = 1; index <= roundhouse.Stalls; index++)
                {
                    result.Add(TurntableAPI.GetRoundhouseSegmentId(turntable.Key, index, turntable.Value));
                }
            }

            return result;
        }

        private static HashSet<string> CollectGeneratedNodeIds(FuseModDefinition definition)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var turntable in definition.Operations?.Turntables ?? new Dictionary<string, FuseTurntable>())
            {
                var value = turntable.Value;
                if (value == null)
                {
                    continue;
                }

                // Pit nodes are created for every subdivision of the turntable.
                var subdivisions = value.Subdivisions > 0 ? value.Subdivisions : 16;
                for (var index = 0; index < subdivisions; index++)
                {
                    result.Add(TurntableAPI.GetPitNodeId(turntable.Key, index, value));
                }

                // Roundhouse stalls add their own roundhouse nodes (1-based).
                var roundhouse = value.Roundhouse;
                if (roundhouse != null && roundhouse.Stalls > 0)
                {
                    for (var index = 1; index <= roundhouse.Stalls; index++)
                    {
                        result.Add(TurntableAPI.GetRoundhouseNodeId(turntable.Key, index, value));
                    }
                }
            }

            return result;
        }

        public static void UnloadMod(string modId)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                return;
            }

            if (LoadedMods.Remove(modId))
            {
                try
                {
                    FuseTrackRemovalSnapshotStore.RestorePackage(modId);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE failed to restore removed base-game track for unloaded mod '{modId}'", ex);
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
            var loadedIds = LoadedMods.Keys.ToArray();
            for (var index = 0; index < loadedIds.Length; index++)
            {
                try
                {
                    UnloadMod(loadedIds[index]);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE failed to unload mod '{loadedIds[index]}'", ex);
                }
            }

            LoadedOrder.Clear();
            AppliedDefinitionIds.Clear();
            FuseTrackRemovalSnapshotStore.ClearAll();
            MapAPI.RestoreAllTelegraphPoleMovements("unload all");
            SpawnPointAPI.ClearRuntimeCache();
            FuseWorldSuppressor.RestoreAll("unload all");
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
                var info = JObject.Parse(File.ReadAllText(infoPath));
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

            var bsonFiles = Directory.GetFiles(modFolder, "*.bson", SearchOption.TopDirectoryOnly);
            if (bsonFiles.Length > 0)
            {
                return new[] { bsonFiles[0] };
            }

            var jsonFiles = Directory.GetFiles(modFolder, "*.json", SearchOption.TopDirectoryOnly)
                .Where(IsFallbackDefinitionJsonFile)
                .ToArray();
            if (jsonFiles.Length > 0)
            {
                return new[] { jsonFiles[0] };
            }

            throw new FileNotFoundException($"No FUSE .bson or .json definition was found in '{modFolder}'.");
        }

        private static bool HasFuseAssetPacks(JObject info)
        {
            return info != null && info["FuseAssetPacks"] != null;
        }

        private static bool IsFallbackDefinitionJsonFile(string path)
        {
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            if (string.Equals(fileName, "Info.json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "conversion-report.json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "Catalog.json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "Definitions.json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "Definition.json", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return fileName.EndsWith(".fuse.json", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> ResolveExplicitDefinitionPaths(string modFolder, JObject info)
        {
            foreach (var railDataFile in EnumerateFuseDataFiles(info["FuseDataFile"]))
            {
                yield return ResolveExistingDefinitionPath(modFolder, railDataFile);
            }

            foreach (var railDataFile in EnumerateFuseDataFiles(info["FuseDataFiles"]))
            {
                yield return ResolveExistingDefinitionPath(modFolder, railDataFile);
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

        private static string ResolveExistingDefinitionPath(string modFolder, string railDataFile)
        {
            var explicitPath = Path.Combine(modFolder, railDataFile);
            if (!File.Exists(explicitPath))
            {
                throw new FileNotFoundException($"FUSE data file '{railDataFile}' was not found in '{modFolder}'.", explicitPath);
            }

            return explicitPath;
        }

        private static void ApplyTrackNodes(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            if (definition?.Tracks == null)
            {
                return;
            }

            TrackAPI.BeginBatch();
            try
            {
                foreach (var node in definition.Tracks.Nodes)
                {
                    if (!TryClaimOrSkip(FuseClaimKind.Node, "track node", node.Key, definition.Id, transaction))
                    {
                        continue;
                    }

                    var exists = TrackAPI.GetNode(node.Key) != null;
                    transaction.TryApply("track node", node.Key, exists, () =>
                    {
                        if (exists)
                        {
                            TrackAPI.UpdateNode(node.Key, node.Value);
                        }
                        else
                        {
                            TrackAPI.AddNode(node.Key, node.Value);
                        }
                    });
                }
            }
            finally
            {
                TrackAPI.EndBatch();
            }
        }

        private static bool TryClaimOrSkip(
            FuseClaimKind kind,
            string transactionKind,
            string id,
            string packageId,
            FuseApplyTransaction transaction)
        {
            if (FuseRegistry.TryClaim(kind, id, packageId, out var owner))
            {
                return true;
            }

            transaction.Skipped(transactionKind, id, $"claimed-by:{owner ?? "unknown"}");
            return false;
        }

        private static void ApplyTrackRemovals(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            var removals = definition?.Tracks?.Removals;
            if (removals == null)
            {
                return;
            }

            TrackAPI.BeginBatch();
            try
            {
                foreach (var spanId in removals.Spans ?? Array.Empty<string>())
                {
                    if (TrackAPI.GetSpan(spanId) != null)
                    {
                        transaction.TryRemove("track span", spanId, () =>
                        {
                            FuseTrackRemovalSnapshotStore.CaptureSpanBeforeRemoval(definition.Id, spanId, transaction);
                            TrackAPI.RemoveSpan(spanId);
                        });
                    }
                    else
                    {
                        transaction.Skipped("track span removal", spanId, "missing");
                    }
                }

                foreach (var segmentId in removals.Segments ?? Array.Empty<string>())
                {
                    if (TrackAPI.GetSegment(segmentId) != null)
                    {
                        transaction.TryRemove("track segment", segmentId, () =>
                        {
                            FuseTrackRemovalSnapshotStore.CaptureSegmentBeforeRemoval(definition.Id, segmentId, transaction);
                            TrackAPI.RemoveSegment(segmentId);
                        });
                    }
                    else
                    {
                        transaction.Skipped("track segment removal", segmentId, "missing");
                    }
                }

                foreach (var nodeId in removals.Nodes ?? Array.Empty<string>())
                {
                    if (TrackAPI.GetNode(nodeId) != null)
                    {
                        transaction.TryRemove("track node", nodeId, () =>
                        {
                            FuseTrackRemovalSnapshotStore.CaptureNodeBeforeRemoval(definition.Id, nodeId, transaction);
                            TrackAPI.RemoveNode(nodeId);
                        });
                    }
                    else
                    {
                        transaction.Skipped("track node removal", nodeId, "missing");
                    }
                }
            }
            finally
            {
                TrackAPI.EndBatch();
            }
        }

        private static void ApplyTrackSegments(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            if (definition?.Tracks == null)
            {
                return;
            }

            TrackAPI.BeginBatch();
            try
            {
                foreach (var segment in definition.Tracks.Segments)
                {
                    if (!TryClaimOrSkip(FuseClaimKind.Segment, "track segment", segment.Key, definition.Id, transaction))
                    {
                        continue;
                    }

                    var runtimeSegment = TrackAPI.GetSegment(segment.Key);
                    var exists = runtimeSegment != null;

                    // Pre-check: surface node-binding problems before AddSegment
                    // throws. The preflight-warning downgrade lets segments with
                    // missing endpoints reach apply, where they fail per-segment.
                    // Emitting this warning makes the cause visible without
                    // adding any per-success log noise.
                    var startNode = TrackAPI.GetNode(segment.Value.StartNodeId);
                    var endNode = TrackAPI.GetNode(segment.Value.EndNodeId);
                    if (startNode == null || endNode == null)
                    {
                        FuseLog.Warning(
                            $"FUSE apply pre-check package='{definition.Id}' operation='apply-segments' " +
                            $"kind='track segment' id='{segment.Key}' " +
                            $"start='{segment.Value.StartNodeId ?? string.Empty}' startExists={startNode != null} " +
                            $"end='{segment.Value.EndNodeId ?? string.Empty}' endExists={endNode != null} " +
                            "message='node reference not yet bound; AddSegment may fail per-segment'.");
                    }

                    transaction.TryApply("track segment", segment.Key, exists, () =>
                    {
                        if (runtimeSegment == null)
                        {
                            TrackAPI.AddSegment(segment.Key, segment.Value);
                        }
                        else if (runtimeSegment.a.id != segment.Value.StartNodeId || runtimeSegment.b.id != segment.Value.EndNodeId)
                        {
                            // Endpoint change: the existing segment must be torn
                            // down and re-added because TrackSegment endpoints
                            // are not mutable in place. Log the diff so a future
                            // graph-state regression can be tied to a specific
                            // segment + reload pair.
                            FuseLog.Info(
                                $"FUSE apply package='{definition.Id}' operation='apply-segments' " +
                                $"kind='track segment' id='{segment.Key}' " +
                                $"message='endpoint change detected " +
                                $"oldStart=\"{runtimeSegment.a.id}\" newStart=\"{segment.Value.StartNodeId}\" " +
                                $"oldEnd=\"{runtimeSegment.b.id}\" newEnd=\"{segment.Value.EndNodeId}\" " +
                                $"newStartExists={startNode != null} newEndExists={endNode != null}'.");

                            TrackAPI.RemoveSegment(segment.Key);
                            TrackAPI.AddSegment(segment.Key, segment.Value);
                        }
                        else
                        {
                            TrackAPI.UpdateSegment(segment.Key, segment.Value);
                        }
                    });
                }
            }
            finally
            {
                TrackAPI.EndBatch();
            }
        }

        private static void ApplyTrackSpans(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            if (definition?.Tracks == null)
            {
                return;
            }

            TrackAPI.BeginBatch();
            try
            {
                foreach (var span in definition.Tracks.Spans)
                {
                    if (!TryClaimOrSkip(FuseClaimKind.Span, "track span", span.Key, definition.Id, transaction))
                    {
                        continue;
                    }

                    var exists = TrackAPI.GetSpan(span.Key) != null;
                    transaction.TryApply("track span", span.Key, exists, () =>
                    {
                        if (exists)
                        {
                            TrackAPI.UpdateSpan(span.Key, span.Value);
                        }
                        else
                        {
                            TrackAPI.AddSpan(span.Key, span.Value);
                        }
                    });
                }
            }
            finally
            {
                TrackAPI.EndBatch();
            }
        }

        private static void ApplyTrackAreas(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            if (definition?.Tracks?.Areas == null)
            {
                return;
            }

            foreach (var area in definition.Tracks.Areas)
            {
                var exists = TrackAPI.GetArea(area.Key) != null;
                transaction.TryApply("area", area.Key, exists, () =>
                {
                    if (exists)
                    {
                        TrackAPI.UpdateArea(area.Key, area.Value);
                    }
                    else
                    {
                        TrackAPI.AddArea(area.Key, area.Value);
                    }
                });
            }

            TrackAPI.ApplyAreaOrdering();
        }

        private static void ApplyTurntables(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            if (definition?.Operations?.Turntables == null)
            {
                return;
            }

            foreach (var turntable in definition.Operations.Turntables)
            {
                if (!TryClaimOrSkip(FuseClaimKind.Turntable, "turntable", turntable.Key, definition.Id, transaction))
                {
                    continue;
                }

                var exists = TurntableAPI.GetTurntable(turntable.Key) != null;
                transaction.TryApply("turntable", turntable.Key, exists, () =>
                {
                    if (exists)
                    {
                        TurntableAPI.UpdateTurntable(turntable.Key, turntable.Value);
                    }
                    else
                    {
                        TurntableAPI.AddTurntable(turntable.Key, turntable.Value);
                    }
                });
            }
        }

        private static void ApplyOperationsDefinition(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            if (definition?.Operations == null)
            {
                return;
            }

            if (definition.Operations.Loads != null)
            {
                foreach (var load in definition.Operations.Loads)
                {
                    var exists = LoadAPI.GetLoad(load.Key) != null;
                    transaction.TryApply("load", load.Key, exists, () =>
                    {
                        if (exists)
                        {
                            LoadAPI.UpdateLoad(load.Key, load.Value);
                        }
                        else
                        {
                            LoadAPI.AddLoad(load.Key, load.Value);
                        }
                    });
                }
            }

            if (definition.Operations.Industries != null)
            {
                var industriesTouched = false;
                foreach (var industry in definition.Operations.Industries)
                {
                    if (!TryClaimOrSkip(FuseClaimKind.Industry, "industry", industry.Key, definition.Id, transaction))
                    {
                        continue;
                    }

                    var exists = IndustryAPI.GetIndustry(industry.Key) != null;
                    if (transaction.TryApply("industry", industry.Key, exists, () =>
                    {
                        if (exists)
                        {
                            IndustryAPI.UpdateIndustry(industry.Key, industry.Value, false);
                        }
                        else
                        {
                            IndustryAPI.AddIndustry(industry.Key, industry.Value, false);
                        }
                    }))
                    {
                        industriesTouched = true;
                    }
                }

                if (industriesTouched)
                {
                    IndustryAPI.RefreshIndustriesAfterBatch("ApplyOperationsDefinition");
                }
            }

            if (definition.Operations.Loaders != null)
            {
                foreach (var loader in definition.Operations.Loaders)
                {
                    if (!TryClaimOrSkip(FuseClaimKind.Loader, "loader", loader.Key, definition.Id, transaction))
                    {
                        continue;
                    }

                    var exists = LoaderAPI.GetLoader(loader.Key) != null;
                    transaction.TryApply("loader", loader.Key, exists, () =>
                    {
                        if (exists)
                        {
                            LoaderAPI.UpdateLoader(loader.Key, loader.Value);
                        }
                        else
                        {
                            LoaderAPI.AddLoader(loader.Key, loader.Value);
                        }
                    });
                }
            }

            if (definition.Operations.Stations != null)
            {
                foreach (var station in definition.Operations.Stations)
                {
                    if (!TryClaimOrSkip(FuseClaimKind.Station, "station", station.Key, definition.Id, transaction))
                    {
                        continue;
                    }

                    var exists = StationAPI.GetStationAgent(station.Key) != null;
                    transaction.TryApply("station", station.Key, exists, () =>
                    {
                        if (exists)
                        {
                            StationAPI.UpdateStationAgent(station.Key, station.Value);
                        }
                        else
                        {
                            StationAPI.AddStationAgent(station.Key, station.Value);
                        }
                    });
                }
            }
        }

        private static void ApplyWorldDefinition(FuseModDefinition definition, string folderPath, string definitionPath, FuseApplyTransaction transaction)
        {
            if (definition?.World == null)
            {
                return;
            }

            FuseMapTileRegistry.RegisterTileSources(definition.Id, folderPath, definition.World);

            if (definition.World.Scenery != null)
            {
                foreach (var scenery in definition.World.Scenery)
                {
                    if (!SceneryAPI.TryResolveAssetIdentifier(scenery.Key, scenery.Value, out _))
                    {
                        FuseLog.Warning(
                            $"FUSE skipping scenery '{scenery.Key}' in package '{definition.Id}': " +
                            $"AssetIdentifier='{scenery.Value?.AssetIdentifier ?? string.Empty}', " +
                            $"Model='{scenery.Value?.Model ?? string.Empty}' did not resolve to a known PrefabStore asset.");
                        FuseLoadReport.RecordUnknownSceneryAsset(
                            definition.Id,
                            scenery.Key,
                            scenery.Value?.AssetIdentifier,
                            scenery.Value?.Model);
                        transaction.Skipped("scenery", scenery.Key, "unknown asset identifier");
                        continue;
                    }

                    if (!TryClaimOrSkip(FuseClaimKind.Scenery, "scenery", scenery.Key, definition.Id, transaction))
                    {
                        continue;
                    }

                    var exists = SceneryAPI.GetScenery(scenery.Key) != null;
                    transaction.TryApply("scenery", scenery.Key, exists, () =>
                    {
                        var entity = FuseAuthoringRegistry.Get(scenery.Key) as FuseSceneryEntity ??
                                     new FuseSceneryEntity(scenery.Key, definition.Id);
                        entity.InitializeIdentity(scenery.Key, definition.Id);
                        entity.BindDefinition(definition, definitionPath);
                        entity.LoadDefinition(scenery.Value);
                        if (!FuseAuthoringPersistenceService.ApplyToRuntime(entity))
                        {
                            throw new InvalidOperationException($"Scenery authoring entity '{scenery.Key}' failed validation.");
                        }
                    });
                }
            }

            if (definition.World.SpawnPoints != null)
            {
                foreach (var spawnPoint in definition.World.SpawnPoints)
                {
                    if (spawnPoint == null || string.IsNullOrWhiteSpace(spawnPoint.Name))
                    {
                        transaction.Skipped("spawn point", definition.Id, "missing name");
                        continue;
                    }

                    var key = definition.Id + "/" + spawnPoint.Name.Trim();
                    var exists = SpawnPointAPI.GetSpawnPoint(definition.Id, spawnPoint.Name) != null;
                    transaction.TryApply("spawn point", key, exists, () =>
                    {
                        SpawnPointAPI.AddOrUpdateSpawnPoint(definition.Id, spawnPoint);
                    });
                }
            }

            if (definition.World.MapLabels != null)
            {
                foreach (var label in definition.World.MapLabels)
                {
                    var exists = MapAPI.GetMapLabel(label.Key) != null;
                    transaction.TryApply("map label", label.Key, exists, () =>
                    {
                        if (exists)
                        {
                            MapAPI.UpdateMapLabel(label.Key, label.Value);
                        }
                        else
                        {
                            MapAPI.AddMapLabel(label.Key, label.Value);
                        }
                    });
                }
            }

            if (definition.World.Splineys != null)
            {
                foreach (var spliney in definition.World.Splineys)
                {
                    var exists = SplineyAPI.GetSpliney(spliney.Key) != null;
                    transaction.TryApply("spliney", spliney.Key, exists, () =>
                    {
                        if (exists)
                        {
                            SplineyAPI.UpdateSpliney(spliney.Key, spliney.Value);
                        }
                        else
                        {
                            SplineyAPI.AddSpliney(spliney.Key, spliney.Value);
                        }
                    });
                }
            }

            if (definition.World.TelegraphPoles != null)
            {
                foreach (var telegraph in definition.World.TelegraphPoles)
                {
                    var exists = MapAPI.GetTelegraphPoles(telegraph.Key) != null;
                    transaction.TryApply("telegraph pole set", telegraph.Key, exists, () =>
                    {
                        if (exists)
                        {
                            MapAPI.UpdateTelegraphPoles(telegraph.Key, telegraph.Value);
                        }
                        else
                        {
                            MapAPI.AddTelegraphPoles(telegraph.Key, telegraph.Value);
                        }
                    });
                }
            }

            if (definition.World.TelegraphPoleMovements != null &&
                (definition.World.TelegraphPoleMovements.Length > 0 || MapAPI.HasTelegraphPoleMovementClaim(definition.Id)))
            {
                transaction.TryApply("telegraph pole movements", definition.Id, MapAPI.HasTelegraphPoleMovementClaim(definition.Id), () =>
                {
                    MapAPI.ApplyTelegraphPoleMovements(definition.Id, definition.World.TelegraphPoleMovements);
                });
            }

            if (definition.World.MapMasks != null)
            {
                foreach (var mask in definition.World.MapMasks)
                {
                    var exists = MapAPI.GetMapMask(mask.Key) != null;
                    transaction.TryApply("map mask", mask.Key, exists, () =>
                    {
                        if (exists)
                        {
                            MapAPI.UpdateMapMask(mask.Key, mask.Value);
                        }
                        else
                        {
                            MapAPI.AddMapMask(mask.Key, mask.Value);
                        }
                    });
                }
            }

            if (definition.World.SceneClones != null)
            {
                foreach (var sceneClone in definition.World.SceneClones)
                {
                    var exists = SceneCloneAPI.GetSceneClone(sceneClone.Key) != null;
                    transaction.TryApply("scene clone", sceneClone.Key, exists, () =>
                    {
                        var entity = FuseAuthoringRegistry.Get(sceneClone.Key) as FuseConfigurableStructureEntity ??
                                     new FuseConfigurableStructureEntity(sceneClone.Key, definition.Id);
                        entity.InitializeIdentity(sceneClone.Key, definition.Id);
                        entity.BindDefinition(definition, definitionPath);
                        entity.LoadDefinition(sceneClone.Value);
                        if (!FuseAuthoringPersistenceService.ApplyToRuntime(entity))
                        {
                            throw new InvalidOperationException($"Configurable structure authoring entity '{sceneClone.Key}' failed validation.");
                        }
                    });
                }
            }
        }

        private static void ApplyAudioDefinition(FuseModDefinition definition, string folderPath, FuseApplyTransaction transaction)
        {
            if (definition?.Audio == null)
            {
                return;
            }

            var count = (definition.Audio.Whistles?.Count ?? 0) +
                        (definition.Audio.Horns?.Count ?? 0) +
                        (definition.Audio.Bells?.Count ?? 0);
            if (count == 0)
            {
                return;
            }

            transaction.TryApply("audio package", definition.Id, FuseAudioAPI.HasWhistles || FuseAudioAPI.HasHorns || FuseAudioAPI.HasBells, () =>
            {
                FuseAudioAPI.RegisterDefinition(definition.Id, definition.Audio, folderPath, transaction);
            });
        }

        private static void ApplyWorldRemovals(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            var removals = definition?.World?.Removals;
            if (removals == null)
            {
                return;
            }

            RemoveWorldItems("scene clone", removals.SceneClones, SceneCloneAPI.TryRemoveSceneClone, transaction);
            RemoveWorldItems("scenery", removals.Scenery, SceneryAPI.TryRemoveScenery, transaction);
            RemoveWorldItems("spliney", removals.Splineys, SplineyAPI.TryRemoveSpliney, transaction);
            RemoveWorldItems("telegraph pole set", removals.TelegraphPoles, MapAPI.TryRemoveTelegraphPoles, transaction);
            RemoveWorldItems("map label", removals.MapLabels, MapAPI.TryRemoveMapLabel, transaction);
            RemoveWorldItems("map mask", removals.MapMasks, MapAPI.TryRemoveMapMask, transaction);
        }

        private static void RemoveWorldItems(string kind, IEnumerable<string> ids, Func<string, bool> remover, FuseApplyTransaction transaction)
        {
            if (ids == null)
            {
                return;
            }

            foreach (var id in ids)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                try
                {
                    if (remover(id))
                    {
                        transaction.Removed(kind, id);
                    }
                    else
                    {
                        transaction.Skipped(kind, id, "missing");
                    }
                }
                catch (Exception exception)
                {
                    FuseLog.Warning($"FUSE failed to remove world {kind} '{id}': {exception.Message}");
                    transaction.Error(kind, id, exception.Message);
                }
            }
        }

        private static void ApplyProgressionDefinition(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            if (definition?.Progression == null)
            {
                return;
            }

            if (definition.Progression.MapFeatures != null)
            {
                foreach (var feature in definition.Progression.MapFeatures)
                {
                    var exists = ProgressionAPI.GetMapFeature(feature.Key) != null;
                    transaction.TryApply("map feature", feature.Key, exists, () =>
                    {
                        if (exists)
                        {
                            ProgressionAPI.UpdateMapFeature(feature.Key, feature.Value);
                        }
                        else
                        {
                            ProgressionAPI.AddMapFeature(feature.Key, feature.Value);
                        }
                    });
                }
            }

            if (definition.Progression.Progressions != null)
            {
                foreach (var progression in definition.Progression.Progressions)
                {
                    var exists = ProgressionAPI.GetProgression(progression.Key) != null;
                    transaction.TryApply("progression", progression.Key, exists, () =>
                    {
                        if (exists)
                        {
                            ProgressionAPI.UpdateProgression(progression.Key, progression.Value);
                        }
                        else
                        {
                            ProgressionAPI.AddProgression(progression.Key, progression.Value);
                        }
                    });
                }
            }
        }

        private static void ValidatePostBind(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            if (definition == null)
            {
                transaction.Warning("definition", string.Empty, "definition was null during post-bind validation");
                return;
            }

            foreach (var nodeId in definition.Tracks?.Nodes?.Keys ?? Enumerable.Empty<string>())
            {
                if (TrackAPI.GetNode(nodeId) == null)
                {
                    transaction.Warning("track node", nodeId, "missing after apply");
                }
            }

            foreach (var segmentId in definition.Tracks?.Segments?.Keys ?? Enumerable.Empty<string>())
            {
                if (TrackAPI.GetSegment(segmentId) == null)
                {
                    transaction.Warning("track segment", segmentId, "missing after apply");
                }
            }

            foreach (var spanId in definition.Tracks?.Spans?.Keys ?? Enumerable.Empty<string>())
            {
                if (TrackAPI.GetSpan(spanId) == null)
                {
                    transaction.Warning("track span", spanId, "missing after apply");
                }
            }

            foreach (var areaId in definition.Tracks?.Areas?.Keys ?? Enumerable.Empty<string>())
            {
                if (TrackAPI.GetArea(areaId) == null)
                {
                    transaction.Warning("area", areaId, "missing after apply");
                }
            }

            foreach (var loadId in definition.Operations?.Loads?.Keys ?? Enumerable.Empty<string>())
            {
                if (LoadAPI.GetLoad(loadId) == null)
                {
                    transaction.Warning("load", loadId, "missing after apply");
                }
            }

            foreach (var industryId in definition.Operations?.Industries?.Keys ?? Enumerable.Empty<string>())
            {
                var industry = IndustryAPI.GetIndustry(industryId);
                if (industry == null)
                {
                    transaction.Warning("industry", industryId, "missing after apply");
                    continue;
                }

                var componentCount = industry.GetComponentsInChildren<IndustryComponent>(true)
                    .Count(component => component != null && !string.IsNullOrWhiteSpace(component.subIdentifier));
                transaction.PostBind("industry", industryId, $"componentCount={componentCount}");
            }

            foreach (var loaderId in definition.Operations?.Loaders?.Keys ?? Enumerable.Empty<string>())
            {
                if (LoaderAPI.GetLoader(loaderId) == null)
                {
                    transaction.Warning("loader", loaderId, "missing after apply");
                }
            }

            foreach (var turntableId in definition.Operations?.Turntables?.Keys ?? Enumerable.Empty<string>())
            {
                if (TurntableAPI.GetTurntable(turntableId) == null)
                {
                    transaction.Warning("turntable", turntableId, "missing after apply");
                }
            }

            foreach (var stationId in definition.Operations?.Stations?.Keys ?? Enumerable.Empty<string>())
            {
                if (StationAPI.GetStationAgent(stationId) == null)
                {
                    transaction.Warning("station", stationId, "missing after apply");
                }
            }

            foreach (var sceneryId in definition.World?.Scenery?.Keys ?? Enumerable.Empty<string>())
            {
                if (SceneryAPI.GetScenery(sceneryId) == null)
                {
                    transaction.Warning("scenery", sceneryId, "missing after apply");
                }
            }

            foreach (var splineyId in definition.World?.Splineys?.Keys ?? Enumerable.Empty<string>())
            {
                if (SplineyAPI.GetSpliney(splineyId) == null)
                {
                    transaction.Warning("spliney", splineyId, "missing after apply");
                }
            }

            foreach (var sceneCloneId in definition.World?.SceneClones?.Keys ?? Enumerable.Empty<string>())
            {
                if (SceneCloneAPI.GetSceneClone(sceneCloneId) == null)
                {
                    transaction.Warning("scene clone", sceneCloneId, "missing after apply");
                }
            }

            foreach (var labelId in definition.World?.MapLabels?.Keys ?? Enumerable.Empty<string>())
            {
                if (MapAPI.GetMapLabel(labelId) == null)
                {
                    transaction.Warning("map label", labelId, "missing after apply");
                }
            }

            transaction.PostBind("scene", definition.Id, $"industries={IndustryAPI.GetAllIndustries().Count()} components={UnityEngine.Object.FindObjectsOfType<IndustryComponent>(true).Length}");
        }
    }
}
