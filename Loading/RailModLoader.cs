using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Model.Ops;
using RAIL.API;
using RAIL.Authoring;
using RAIL.Data;
using RAIL.Events;
using RAIL.Infrastructure;
using RAIL.Registry;
using RAIL.Serialization;
using RAIL.Validation;
using Newtonsoft.Json.Linq;
using Track;

namespace RAIL.Loading
{
    public static class RailModLoader
    {
        private static readonly Dictionary<string, RailLoadedMod> LoadedMods = new Dictionary<string, RailLoadedMod>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> LoadedOrder = new List<string>();
        private static readonly HashSet<string> AppliedDefinitionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly RailDefinitionValidator Validator = new RailDefinitionValidator();

        public static int LoadedDefinitionCount => LoadedMods.Count;

        public static IReadOnlyList<RailLoadedMod> GetLoadedModsInOrder()
        {
            return GetLoadedDefinitionIdsInOrder()
                .Where(id => LoadedMods.TryGetValue(id, out var loaded) && loaded?.Definition != null)
                .Select(id => LoadedMods[id])
                .ToArray();
        }

        public static RailLoadedMod LoadMod(string modFolder)
        {
            if (string.IsNullOrWhiteSpace(modFolder))
            {
                throw new ArgumentException("Mod folder is required.", nameof(modFolder));
            }

            var definitionPaths = ResolveDefinitionPaths(modFolder);
            var definitionIdsInPackage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            RailLoadedMod loadedMod = null;
            for (var index = 0; index < definitionPaths.Length; index++)
            {
                var definitionPath = definitionPaths[index];
                var definition = RailSerializer.Load(definitionPath);
                var definitionId = definition?.Id;
                if (string.IsNullOrWhiteSpace(definitionId))
                {
                    RailPackageFaultRegistry.RecordFault(Path.GetFileName(modFolder), "validation", $"Definition file '{definitionPath}' is missing an id.");
                    throw new InvalidOperationException($"RAIL definition file '{definitionPath}' is missing an id.");
                }

                if (definitionIdsInPackage.TryGetValue(definitionId, out var firstPath))
                {
                    var message =
                        $"Duplicate RAIL definition id '{definitionId}' in package folder '{modFolder}'. " +
                        $"First file='{firstPath}', duplicate file='{definitionPath}'.";
                    RailPackageFaultRegistry.RecordFault(definitionId, "validation", message);
                    throw new InvalidOperationException(message);
                }

                definitionIdsInPackage.Add(definitionId, definitionPath);
                LoadDefinition(definition, modFolder, definitionPath);
                loadedMod = LoadedMods[definition.Id];
            }

            RailLog.Info($"RAIL loaded package definitions from disk folder='{modFolder}' definitionCount={definitionPaths.Length}.");
            return loadedMod;
        }

        public static void LoadDefinition(RailModDefinition definition, string folderPath = null, string definitionPath = null)
        {
            var validation = Validator.Validate(definition);
            RailEvents.RaiseValidationCompleted(definition != null ? definition.Id : string.Empty, validation);
            if (!validation.IsValid)
            {
                RailPackageFaultRegistry.RecordFault(definition?.Id ?? Path.GetFileName(folderPath ?? definitionPath ?? string.Empty), "validation", $"Definition failed validation with {validation.Errors.Count} error(s).");
                throw new InvalidOperationException($"RAIL definition '{definition?.Id ?? "<null>"}' failed validation with {validation.Errors.Count} error(s).");
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
                    RailLog.Warning($"RAIL apply skipped package='{id}' operation='runtime apply' id='{id}' reason='resident definition missing'.");
                    outcomes.Add(PackageApplyOutcome.ForSkipped(id, "resident definition missing"));
                    continue;
                }

                var isReapply = AppliedDefinitionIds.Contains(id);
                try
                {
                    var report = ApplyDefinitionToRuntime(loaded, isReapply, reason);
                    if (report.IsFatal)
                    {
                        RailPackageFaultRegistry.RecordFault(id, "runtime apply", report.FatalReason);
                        RailPackageFaultRegistry.MarkSkipped(id, report.FatalReason);
                        outcomes.Add(PackageApplyOutcome.FromReport(id, report, 0, 1, 1));
                        continue;
                    }

                    if (report.HasErrors)
                    {
                        RailPackageFaultRegistry.RecordFault(id, "runtime apply", $"Runtime apply completed with {report.Errors.Count} error(s).");
                        outcomes.Add(PackageApplyOutcome.FromReport(id, report, 0, 0, 1));
                        continue;
                    }

                    AppliedDefinitionIds.Add(id);
                    RailPackageFaultRegistry.MarkAppliedToRuntime(id);
                    appliedCount++;
                    outcomes.Add(PackageApplyOutcome.FromReport(id, report, 1, 0, 0));
                }
                catch (Exception ex)
                {
                    RailPackageFaultRegistry.RecordFault(id, "runtime apply", ex.Message, ex);
                    RailPackageFaultRegistry.MarkSkipped(id, "runtime apply exception");
                    outcomes.Add(PackageApplyOutcome.ForErrored(id, ex.Message));
                    RailLog.Exception($"Failed to apply loaded RAIL definition '{id}' to runtime for '{reason ?? "unspecified"}'", ex);
                }
            }

            LogAggregateApplySummary(reason, outcomes);
            RailLog.Info($"RAIL applied {appliedCount} resident definition(s) to runtime for '{reason ?? "unspecified"}'.");
            return appliedCount;
        }

        public static int ReapplyLoadedDefinitions(string reason)
        {
            return ApplyLoadedDefinitions(reason);
        }

        private static void RegisterLoadedDefinition(RailModDefinition definition, string folderPath, string definitionPath)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            {
                throw new InvalidOperationException("RAIL definition is missing an id.");
            }

            var firstLoad = !LoadedMods.ContainsKey(definition.Id);
            if (!firstLoad)
            {
                // A definition is being replaced. Release any registry claims the
                // prior version held; the upcoming apply will re-claim what it
                // still owns. Otherwise stale claims would block the new apply.
                RailWorldSuppressor.ReleasePackage(definition.Id);
                var released = RailRegistry.ReleaseAllForPackage(definition.Id);
                AppliedDefinitionIds.Remove(definition.Id);
                RailLog.Info(
                    $"RAIL definition replacement package='{definition.Id}' operation='replace loaded definition' id='{definition.Id}' releasedClaims={released}.");
            }

            LoadedMods[definition.Id] = new RailLoadedMod(folderPath, definitionPath, definition);
            if (firstLoad && !LoadedOrder.Contains(definition.Id, StringComparer.OrdinalIgnoreCase))
            {
                LoadedOrder.Add(definition.Id);
            }

            RailLog.Info($"RAIL loaded definition package='{definition.Id}' operation='load definition' id='{definition.Id}' definitionPath='{definitionPath ?? string.Empty}' folderPath='{folderPath ?? string.Empty}'.");
            if (firstLoad)
            {
                RailEvents.RaiseModLoaded(definition.Id);
            }
        }

        private static void LogAggregateApplySummary(string reason, IReadOnlyList<PackageApplyOutcome> outcomes)
        {
            if (outcomes == null || outcomes.Count == 0)
            {
                RailLog.Info($"RAIL aggregate apply summary reason='{reason ?? "unspecified"}' total applied=0 skipped=0 errored=0.");
                return;
            }

            foreach (var outcome in outcomes)
            {
                var line =
                    $"RAIL aggregate apply package='{outcome.PackageId}' operation='runtime apply' " +
                    $"applied={outcome.Applied} skipped={outcome.Skipped} errored={outcome.Errored} " +
                    $"created={outcome.CreatedObjects} updated={outcome.UpdatedObjects} removed={outcome.RemovedObjects} " +
                    $"objectSkipped={outcome.SkippedObjects} warnings={outcome.Warnings} errors={outcome.Errors} " +
                    $"reason='{outcome.Reason}'.";
                if (outcome.Errored > 0 || outcome.Skipped > 0)
                {
                    RailLog.Warning(line);
                }
                else
                {
                    RailLog.Info(line);
                }
            }

            RailLog.Info(
                $"RAIL aggregate apply total operation='runtime apply' reason='{reason ?? "unspecified"}' " +
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

        private static RailApplyReport ApplyDefinitionToRuntime(RailLoadedMod loaded, bool reapply, string reason)
        {
            var definition = loaded?.Definition;
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            {
                throw new InvalidOperationException("RAIL definition is missing an id.");
            }

            var transaction = new RailApplyTransaction(definition.Id, reason, reapply);
            transaction.RunPhase("preflight-validation", () => RunPreflightValidation(definition, transaction));

            using (var registryTx = RailRegistry.BeginReapplyTransaction(definition.Id))
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

                    transaction.RunPhase("single-graph-rebuild", () =>
                    {
                        TrackAPI.RebuildGraph();
                        transaction.PostBind("graph", definition.Id, "rebuilt");
                    });
                    transaction.RunPhase("apply-world-objects", () => ApplyWorldDefinition(definition, loaded.FolderPath, loaded.DefinitionPath, transaction));
                    transaction.RunPhase("apply-operations", () =>
                    {
                        ApplyTrackAreas(definition, transaction);
                        ApplyOperationsDefinition(definition, transaction);
                    });
                    transaction.RunPhase("apply-progression", () => ApplyProgressionDefinition(definition, transaction));
                    transaction.RunPhase("apply-world-suppressions", () => RailWorldSuppressor.ApplyDefinition(definition, transaction));
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
                RailLog.Warning($"RAIL skipped runtime mutation for definition '{definition.Id}' after fatal apply report reason='{transaction.Report.FatalReason}'.");
            }

            transaction.Report.LogSummary();

            if (!transaction.Report.IsFatal && !transaction.Report.HasErrors)
            {
                RailLog.Info(reapply
                    ? $"RAIL reapplied resident definition '{definition.Id}' to runtime for '{reason ?? "unspecified"}' ({definition.Operations?.Turntables?.Count ?? 0} turntable(s), {definition.World?.SceneClones?.Count ?? 0} scene clone(s))."
                    : $"RAIL applied resident definition '{definition.Id}' to runtime for '{reason ?? "unspecified"}' ({definition.Operations?.Turntables?.Count ?? 0} turntable(s), {definition.World?.SceneClones?.Count ?? 0} scene clone(s)).");
            }
            else
            {
                RailLog.Warning($"RAIL runtime apply for resident definition '{definition.Id}' completed with fatal={transaction.Report.IsFatal} errors={transaction.Report.Errors.Count}; package marked faulted.");
            }

            return transaction.Report;
        }

        private static void RunPreflightValidation(RailModDefinition definition, RailApplyTransaction transaction)
        {
            var validation = Validator.Validate(definition);
            RailEvents.RaiseValidationCompleted(definition != null ? definition.Id : string.Empty, validation);

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

        private static void ValidateRuntimeReferences(RailModDefinition definition, RailApplyTransaction transaction)
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

        private static bool RequiresGraph(RailModDefinition definition)
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

        private static bool HasAny(IEnumerable<string> values)
        {
            return values != null && values.Any(value => !string.IsNullOrWhiteSpace(value));
        }

        private static void ValidateTrackRuntimeReferences(RailModDefinition definition, RailApplyTransaction transaction)
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

            foreach (var segment in tracks.Segments ?? new Dictionary<string, RailSegment>())
            {
                ValidateNodeReference(segment.Key, "startNodeId", segment.Value?.StartNodeId, definedNodes, generatedNodes, transaction);
                ValidateNodeReference(segment.Key, "endNodeId", segment.Value?.EndNodeId, definedNodes, generatedNodes, transaction);
            }

            foreach (var span in tracks.Spans ?? new Dictionary<string, RailSpan>())
            {
                ValidateSegmentReference(span.Key, "upper", span.Value?.Upper?.SegmentId, definedSegments, generatedSegments, transaction);
                ValidateSegmentReference(span.Key, "lower", span.Value?.Lower?.SegmentId, definedSegments, generatedSegments, transaction);
            }
        }

        private static void ValidateOperationRuntimeReferences(RailModDefinition definition, RailApplyTransaction transaction)
        {
            var operations = definition.Operations;
            if (operations == null)
            {
                return;
            }

            var definedLoads = new HashSet<string>(operations.Loads?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var definedSpans = new HashSet<string>(definition.Tracks?.Spans?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (var industry in operations.Industries ?? new Dictionary<string, RailIndustry>())
            {
                foreach (var component in industry.Value?.Components ?? new Dictionary<string, RailIndustryComponent>())
                {
                    var componentId = $"{industry.Key}.{component.Key}";
                    ValidateOptionalLoadReference(componentId, component.Value?.LoadId, definedLoads, transaction);
                    ValidateOptionalSpanReferences(componentId, component.Value?.TrackSpanIds, definedSpans, transaction);
                    ValidateOptionalSpanReferences(componentId, component.Value?.InputSpanIds, definedSpans, transaction);
                }
            }

            foreach (var loader in operations.Loaders ?? new Dictionary<string, RailLoader>())
            {
                if (!string.IsNullOrWhiteSpace(loader.Value?.IndustryId) &&
                    (operations.Industries == null || !operations.Industries.ContainsKey(loader.Value.IndustryId)) &&
                    IndustryAPI.GetIndustry(loader.Value.IndustryId) == null)
                {
                    transaction.Warning("loader", loader.Key, $"industryId '{loader.Value.IndustryId}' is not defined in this package or runtime");
                }
            }

            foreach (var station in operations.Stations ?? new Dictionary<string, RailStation>())
            {
                if (!string.IsNullOrWhiteSpace(station.Value?.PassengerStopId) &&
                    !HasPassengerStop(definition, station.Value.PassengerStopId))
                {
                    transaction.Warning("station", station.Key, $"passengerStopId '{station.Value.PassengerStopId}' was not found in this package or runtime");
                }
            }
        }

        private static void ValidateNodeReference(string segmentId, string field, string nodeId, ISet<string> definedNodes, ISet<string> generatedNodes, RailApplyTransaction transaction)
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
                $"{field} references node '{nodeId}' that is not defined in this RAIL document. " +
                "It must exist in the base game graph at runtime or be generated during apply.");
        }

        private static void ValidateSegmentReference(string spanId, string field, string segmentId, ISet<string> definedSegments, ISet<string> generatedSegments, RailApplyTransaction transaction)
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

        private static void ValidateOptionalSpanReferences(string componentId, IEnumerable<string> spanIds, ISet<string> definedSpans, RailApplyTransaction transaction)
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

        private static void ValidateOptionalLoadReference(string componentId, string loadId, ISet<string> definedLoads, RailApplyTransaction transaction)
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

        private static bool HasPassengerStop(RailModDefinition definition, string passengerStopId)
        {
            foreach (var industry in definition.Operations?.Industries ?? new Dictionary<string, RailIndustry>())
            {
                foreach (var component in industry.Value?.Components ?? new Dictionary<string, RailIndustryComponent>())
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

        private static HashSet<string> CollectGeneratedSegmentIds(RailModDefinition definition)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var turntable in definition.Operations?.Turntables ?? new Dictionary<string, RailTurntable>())
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

        private static HashSet<string> CollectGeneratedNodeIds(RailModDefinition definition)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var turntable in definition.Operations?.Turntables ?? new Dictionary<string, RailTurntable>())
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
                    RailTrackRemovalSnapshotStore.RestorePackage(modId);
                }
                catch (Exception ex)
                {
                    RailLog.Exception($"RAIL failed to restore removed base-game track for unloaded mod '{modId}'", ex);
                }

                try
                {
                    RailWorldSuppressor.ReleasePackage(modId);
                }
                catch (Exception ex)
                {
                    RailLog.Exception($"RAIL failed to release world suppressions for unloaded mod '{modId}'", ex);
                }

                // Release registry claims before any other unload work so other
                // packages can claim those ids on the next apply if needed.
                try
                {
                    var released = RailRegistry.ReleaseAllForPackage(modId);
                    if (released > 0)
                    {
                        RailLog.Info($"RAIL released {released} registry claim(s) for unloaded mod '{modId}'.");
                    }
                }
                catch (Exception ex)
                {
                    RailLog.Exception($"RAIL failed to release registry claims for unloaded mod '{modId}'", ex);
                }

                LoadedOrder.RemoveAll(id => string.Equals(id, modId, StringComparison.OrdinalIgnoreCase));
                AppliedDefinitionIds.Remove(modId);
                RailPackageFaultRegistry.ClearPackage(modId);
                try
                {
                    RailMapTileRegistry.UnregisterTileSources(modId);
                }
                catch (Exception ex)
                {
                    RailLog.Exception($"RAIL failed to unregister map tile sources for unloaded mod '{modId}'", ex);
                }

                try
                {
                    RailEvents.RaiseModUnloaded(modId);
                }
                catch (Exception ex)
                {
                    RailLog.Exception($"RAIL mod-unloaded event failed for '{modId}'", ex);
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
                    RailLog.Exception($"RAIL failed to unload mod '{loadedIds[index]}'", ex);
                }
            }

            LoadedOrder.Clear();
            AppliedDefinitionIds.Clear();
            RailTrackRemovalSnapshotStore.ClearAll();
            RailWorldSuppressor.RestoreAll("unload all");
            // Per-mod claims were released in UnloadMod; reset registry to drop
            // any orphaned shared claims and the conflict history.
            RailRegistry.Reset();
            if (resetDiscovery)
            {
                RailDataPackageDiscovery.ResetDiscovery();
                RailAssetPackRegistry.Reset();
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

        public static RailModDefinition GetLoadedDefinition(string modId)
        {
            return !string.IsNullOrWhiteSpace(modId) && LoadedMods.TryGetValue(modId, out var loaded)
                ? loaded.Definition
                : null;
        }

        public static bool TryGetLoadedMod(string modId, out RailLoadedMod loaded)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                loaded = null;
                return false;
            }

            return LoadedMods.TryGetValue(modId, out loaded);
        }

        public static RailLoadedMod GetLoadedMod(string modId)
        {
            TryGetLoadedMod(modId, out var loaded);
            return loaded;
        }

        public static RailModDefinition ImportFromJson(string jsonPath)
        {
            return RailSerializer.Load(jsonPath);
        }

        public static void ExportToJson(string modId, string outputPath)
        {
            var definition = GetLoadedDefinition(modId);
            if (definition == null)
            {
                throw new InvalidOperationException($"RAIL mod '{modId}' is not loaded.");
            }

            RailSerializer.SaveJson(definition, outputPath);
        }

        public static void SaveAsBson(RailModDefinition definition, string outputPath)
        {
            RailSerializer.SaveBson(definition, outputPath);
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
            }

            var bsonFiles = Directory.GetFiles(modFolder, "*.bson", SearchOption.TopDirectoryOnly);
            if (bsonFiles.Length > 0)
            {
                return new[] { bsonFiles[0] };
            }

            var jsonFiles = Directory.GetFiles(modFolder, "*.json", SearchOption.TopDirectoryOnly);
            for (var index = 0; index < jsonFiles.Length; index++)
            {
                if (!string.Equals(Path.GetFileName(jsonFiles[index]), "Info.json", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { jsonFiles[index] };
                }
            }

            throw new FileNotFoundException($"No RAIL .bson or .json definition was found in '{modFolder}'.");
        }

        private static IEnumerable<string> ResolveExplicitDefinitionPaths(string modFolder, JObject info)
        {
            foreach (var railDataFile in EnumerateRailDataFiles(info["RailDataFile"]))
            {
                yield return ResolveExistingDefinitionPath(modFolder, railDataFile);
            }

            foreach (var railDataFile in EnumerateRailDataFiles(info["RailDataFiles"]))
            {
                yield return ResolveExistingDefinitionPath(modFolder, railDataFile);
            }
        }

        private static IEnumerable<string> EnumerateRailDataFiles(JToken token)
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
                throw new FileNotFoundException($"RAIL data file '{railDataFile}' was not found in '{modFolder}'.", explicitPath);
            }

            return explicitPath;
        }

        private static void ApplyTrackNodes(RailModDefinition definition, RailApplyTransaction transaction)
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
                    if (!TryClaimOrSkip(RailClaimKind.Node, "track node", node.Key, definition.Id, transaction))
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
            RailClaimKind kind,
            string transactionKind,
            string id,
            string packageId,
            RailApplyTransaction transaction)
        {
            if (RailRegistry.TryClaim(kind, id, packageId, out var owner))
            {
                return true;
            }

            transaction.Skipped(transactionKind, id, $"claimed-by:{owner ?? "unknown"}");
            return false;
        }

        private static void ApplyTrackRemovals(RailModDefinition definition, RailApplyTransaction transaction)
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
                            RailTrackRemovalSnapshotStore.CaptureSpanBeforeRemoval(definition.Id, spanId, transaction);
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
                            RailTrackRemovalSnapshotStore.CaptureSegmentBeforeRemoval(definition.Id, segmentId, transaction);
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
                            RailTrackRemovalSnapshotStore.CaptureNodeBeforeRemoval(definition.Id, nodeId, transaction);
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

        private static void ApplyTrackSegments(RailModDefinition definition, RailApplyTransaction transaction)
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
                    if (!TryClaimOrSkip(RailClaimKind.Segment, "track segment", segment.Key, definition.Id, transaction))
                    {
                        continue;
                    }

                    var runtimeSegment = TrackAPI.GetSegment(segment.Key);
                    var exists = runtimeSegment != null;
                    transaction.TryApply("track segment", segment.Key, exists, () =>
                    {
                        if (runtimeSegment == null)
                        {
                            TrackAPI.AddSegment(segment.Key, segment.Value);
                        }
                        else if (runtimeSegment.a.id != segment.Value.StartNodeId || runtimeSegment.b.id != segment.Value.EndNodeId)
                        {
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

        private static void ApplyTrackSpans(RailModDefinition definition, RailApplyTransaction transaction)
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
                    if (!TryClaimOrSkip(RailClaimKind.Span, "track span", span.Key, definition.Id, transaction))
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

        private static void ApplyTrackAreas(RailModDefinition definition, RailApplyTransaction transaction)
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

        private static void ApplyTurntables(RailModDefinition definition, RailApplyTransaction transaction)
        {
            if (definition?.Operations?.Turntables == null)
            {
                return;
            }

            foreach (var turntable in definition.Operations.Turntables)
            {
                if (!TryClaimOrSkip(RailClaimKind.Turntable, "turntable", turntable.Key, definition.Id, transaction))
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

        private static void ApplyOperationsDefinition(RailModDefinition definition, RailApplyTransaction transaction)
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
                    if (!TryClaimOrSkip(RailClaimKind.Industry, "industry", industry.Key, definition.Id, transaction))
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
                    if (!TryClaimOrSkip(RailClaimKind.Loader, "loader", loader.Key, definition.Id, transaction))
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
                    if (!TryClaimOrSkip(RailClaimKind.Station, "station", station.Key, definition.Id, transaction))
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

        private static void ApplyWorldDefinition(RailModDefinition definition, string folderPath, string definitionPath, RailApplyTransaction transaction)
        {
            if (definition?.World == null)
            {
                return;
            }

            RailMapTileRegistry.RegisterTileSources(definition.Id, folderPath, definition.World);

            if (definition.World.Scenery != null)
            {
                foreach (var scenery in definition.World.Scenery)
                {
                    if (!SceneryAPI.TryResolveAssetIdentifier(scenery.Key, scenery.Value, out _))
                    {
                        RailLog.Warning(
                            $"RAIL skipping scenery '{scenery.Key}' in package '{definition.Id}': " +
                            $"AssetIdentifier='{scenery.Value?.AssetIdentifier ?? string.Empty}', " +
                            $"Model='{scenery.Value?.Model ?? string.Empty}' did not resolve to a known PrefabStore asset.");
                        RailLoadReport.RecordUnknownSceneryAsset(
                            definition.Id,
                            scenery.Key,
                            scenery.Value?.AssetIdentifier,
                            scenery.Value?.Model);
                        transaction.Skipped("scenery", scenery.Key, "unknown asset identifier");
                        continue;
                    }

                    if (!TryClaimOrSkip(RailClaimKind.Scenery, "scenery", scenery.Key, definition.Id, transaction))
                    {
                        continue;
                    }

                    var exists = SceneryAPI.GetScenery(scenery.Key) != null;
                    transaction.TryApply("scenery", scenery.Key, exists, () =>
                    {
                        var entity = RailAuthoringRegistry.Get(scenery.Key) as RailSceneryEntity ??
                                     new RailSceneryEntity(scenery.Key, definition.Id);
                        entity.InitializeIdentity(scenery.Key, definition.Id);
                        entity.BindDefinition(definition, definitionPath);
                        entity.LoadDefinition(scenery.Value);
                        if (!RailAuthoringPersistenceService.ApplyToRuntime(entity))
                        {
                            throw new InvalidOperationException($"Scenery authoring entity '{scenery.Key}' failed validation.");
                        }
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
                        var entity = RailAuthoringRegistry.Get(sceneClone.Key) as RailConfigurableStructureEntity ??
                                     new RailConfigurableStructureEntity(sceneClone.Key, definition.Id);
                        entity.InitializeIdentity(sceneClone.Key, definition.Id);
                        entity.BindDefinition(definition, definitionPath);
                        entity.LoadDefinition(sceneClone.Value);
                        if (!RailAuthoringPersistenceService.ApplyToRuntime(entity))
                        {
                            throw new InvalidOperationException($"Configurable structure authoring entity '{sceneClone.Key}' failed validation.");
                        }
                    });
                }
            }
        }

        private static void ApplyWorldRemovals(RailModDefinition definition, RailApplyTransaction transaction)
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

        private static void RemoveWorldItems(string kind, IEnumerable<string> ids, Func<string, bool> remover, RailApplyTransaction transaction)
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
                    RailLog.Warning($"RAIL failed to remove world {kind} '{id}': {exception.Message}");
                    transaction.Error(kind, id, exception.Message);
                }
            }
        }

        private static void ApplyProgressionDefinition(RailModDefinition definition, RailApplyTransaction transaction)
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

        private static void ValidatePostBind(RailModDefinition definition, RailApplyTransaction transaction)
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
