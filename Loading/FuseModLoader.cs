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
using Map.Runtime;
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
            ApplyGlobalLoadCatalog(ids, reason);
            PreEnableInitialTrackGroups(ids);

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

        private sealed class FuseStagedApplyCandidate
        {
            public FuseStagedApplyCandidate(FuseLoadedMod loaded, bool isReapply)
            {
                Loaded = loaded;
                IsReapply = isReapply;
            }

            public FuseLoadedMod Loaded { get; }
            public bool IsReapply { get; }
            public FuseApplyTransaction Transaction { get; set; }
            public FuseRegistryTransaction RegistryTransaction { get; set; }
            public bool Prepared { get; set; }
        }

        private sealed class FuseMergedTrackEntry<T>
        {
            public FuseMergedTrackEntry(string id, T value, FuseStagedApplyCandidate owner, int sequence)
            {
                Id = id;
                Value = value;
                Owner = owner;
                Sequence = sequence;
            }

            public string Id { get; }
            public T Value { get; }
            public FuseStagedApplyCandidate Owner { get; }
            public int Sequence { get; }
        }

        private sealed class FuseMergedTrackRemoval
        {
            public FuseMergedTrackRemoval(string id, FuseStagedApplyCandidate owner, int sequence)
            {
                Id = id;
                Owner = owner;
                Sequence = sequence;
            }

            public string Id { get; }
            public FuseStagedApplyCandidate Owner { get; }
            public int Sequence { get; }
        }

        private sealed class FuseMergedTrackPlan
        {
            public readonly Dictionary<string, FuseMergedTrackEntry<FuseNode>> Nodes = new Dictionary<string, FuseMergedTrackEntry<FuseNode>>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, FuseMergedTrackEntry<FuseSegment>> Segments = new Dictionary<string, FuseMergedTrackEntry<FuseSegment>>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, FuseMergedTrackEntry<FuseSpan>> Spans = new Dictionary<string, FuseMergedTrackEntry<FuseSpan>>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, FuseMergedTrackEntry<FuseTurntable>> Turntables = new Dictionary<string, FuseMergedTrackEntry<FuseTurntable>>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, FuseMergedTrackRemoval> RemovedNodes = new Dictionary<string, FuseMergedTrackRemoval>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, FuseMergedTrackRemoval> RemovedSegments = new Dictionary<string, FuseMergedTrackRemoval>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, FuseMergedTrackRemoval> RemovedSpans = new Dictionary<string, FuseMergedTrackRemoval>(StringComparer.OrdinalIgnoreCase);
            public readonly List<FuseStagedApplyCandidate> OrderedCandidates = new List<FuseStagedApplyCandidate>();

            public bool HasStructuralChanges =>
                Nodes.Count > 0 || Segments.Count > 0 || Turntables.Count > 0 || RemovedNodes.Count > 0 || RemovedSegments.Count > 0 || RemovedSpans.Count > 0;

            public bool HasSpanChanges => Spans.Count > 0 || RemovedSpans.Count > 0;

            public bool ShouldValidateNode(string packageId, string id) =>
                ShouldValidate(packageId, id, Nodes, RemovedNodes);

            public bool ShouldValidateSegment(string packageId, string id) =>
                ShouldValidate(packageId, id, Segments, RemovedSegments);

            public bool ShouldValidateSpan(string packageId, string id) =>
                ShouldValidate(packageId, id, Spans, RemovedSpans);

            public bool TryGetSegmentDefinition(string id, out FuseSegment definition)
            {
                definition = null;
                if (string.IsNullOrWhiteSpace(id))
                {
                    return false;
                }

                if (Segments.TryGetValue(id, out var entry))
                {
                    definition = entry.Value;
                    return definition != null;
                }

                return false;
            }

            private static bool ShouldValidate<T>(
                string packageId,
                string id,
                Dictionary<string, FuseMergedTrackEntry<T>> finalDefinitions,
                Dictionary<string, FuseMergedTrackRemoval> finalRemovals)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    return false;
                }

                if (finalRemovals.ContainsKey(id))
                {
                    return false;
                }

                return !finalDefinitions.TryGetValue(id, out var finalEntry) ||
                       string.Equals(finalEntry.Owner?.Loaded?.Definition?.Id, packageId, StringComparison.OrdinalIgnoreCase);
            }
        }

        private sealed class FusePreflightReferenceContext
        {
            public readonly HashSet<string> NodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> SegmentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> SpanIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> LoadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> IndustryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> PassengerStopIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static int ApplyDefinitionsToRuntimeStaged(
            IReadOnlyList<FuseStagedApplyCandidate> candidates,
            string reason,
            IList<PackageApplyOutcome> outcomes)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return 0;
            }

            var appliedCount = 0;
            var prepared = new List<FuseStagedApplyCandidate>();
            var referenceContext = BuildPreflightReferenceContext(
                candidates.Select(candidate => candidate.Loaded?.Definition));
            try
            {
                foreach (var candidate in candidates)
                {
                    var definition = candidate.Loaded?.Definition;
                    if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                    {
                        continue;
                    }

                    try
                    {
                        candidate.Transaction = new FuseApplyTransaction(definition.Id, reason, candidate.IsReapply);
                        candidate.RegistryTransaction = FuseRegistry.BeginReapplyTransaction(definition.Id);
                        candidate.Transaction.RunPhase("preflight-validation", () => RunPreflightValidation(definition, candidate.Transaction, referenceContext));
                        candidate.Prepared = true;
                        prepared.Add(candidate);
                    }
                    catch (Exception ex)
                    {
                        FusePackageFaultRegistry.RecordFault(definition.Id, "runtime apply", ex.Message, ex);
                        FusePackageFaultRegistry.MarkSkipped(definition.Id, "runtime apply exception");
                        outcomes.Add(PackageApplyOutcome.ForErrored(definition.Id, ex.Message));
                        FuseLog.Exception($"Failed to prepare FUSE definition '{definition.Id}' for staged runtime apply", ex);
                    }
                }

                var active = prepared
                    .Where(item => item.Transaction != null && !item.Transaction.Report.IsFatal)
                    .ToArray();

                if (active.Length > 0)
                {
                    var mergedTrackPlan = BuildMergedTrackPlan(active);

                    // Non-track removals are still per-definition operations, but graph
                    // removals are now resolved as final-state deletes in the merged plan.
                    foreach (var candidate in active)
                    {
                        var definition = candidate.Loaded.Definition;
                        var transaction = candidate.Transaction;
                        transaction.RunPhase("staged-apply-world-removals", () => ApplyWorldRemovals(definition, transaction));
                    }

                    ApplyMergedTrackGraph(mergedTrackPlan, reason);

                    foreach (var candidate in active.Where(item => !item.Transaction.Report.IsFatal))
                    {
                        var definition = candidate.Loaded.Definition;
                        var loaded = candidate.Loaded;
                        var transaction = candidate.Transaction;
                        transaction.RunPhase("apply-audio", () => ApplyAudioDefinition(definition, loaded.FolderPath, transaction));
                        transaction.RunPhase("apply-world-objects", () => ApplyWorldDefinition(definition, loaded.FolderPath, loaded.DefinitionPath, transaction));
                    }

                    FuseMapTileRegistry.MountForActiveMapIfLoaded("staged world apply");

                    IndustryAPI.BeginIndustryApplyBatch();
                    try
                    {
                        foreach (var candidate in active.Where(item => !item.Transaction.Report.IsFatal))
                        {
                            var definition = candidate.Loaded.Definition;
                            var transaction = candidate.Transaction;
                            transaction.RunPhase("apply-operations", () =>
                            {
                                ApplyTrackAreas(definition, transaction, false);
                                ApplyOperationsDefinition(definition, transaction);
                            });
                        }
                    }
                    finally
                    {
                        IndustryAPI.EndIndustryApplyBatch("staged ApplyOperationsDefinition");
                    }
                    TrackAPI.ApplyAreaOrdering();

                    ApplyDeferredOperationBindings(
                        active.Select(item => item.Loaded?.Definition?.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray(),
                        reason);

                    foreach (var candidate in active.Where(item => !item.Transaction.Report.IsFatal))
                    {
                        var definition = candidate.Loaded.Definition;
                        var transaction = candidate.Transaction;
                        transaction.RunPhase("apply-progression", () => ApplyProgressionDefinition(definition, transaction));
                        transaction.RunPhase("apply-world-suppressions", () => FuseWorldSuppressor.ApplyDefinition(definition, transaction));
                    }

                    ProgressionAPI.RefreshRuntimeStateAfterApply("staged ApplyProgressionDefinition");

                    foreach (var candidate in active.Where(item => !item.Transaction.Report.IsFatal))
                    {
                        var definition = candidate.Loaded.Definition;
                        var transaction = candidate.Transaction;
                        transaction.RunPhase("post-bind-validation", () => ValidatePostBind(definition, transaction, mergedTrackPlan));
                    }
                }

                foreach (var candidate in prepared)
                {
                    var definition = candidate.Loaded.Definition;
                    var transaction = candidate.Transaction;
                    if (transaction == null)
                    {
                        continue;
                    }

                    if (transaction.Report.IsFatal)
                    {
                        FusePackageFaultRegistry.RecordFault(definition.Id, "runtime apply", transaction.Report.FatalReason);
                        FusePackageFaultRegistry.MarkSkipped(definition.Id, transaction.Report.FatalReason);
                        if (HasRuntimeMutations(transaction.Report))
                        {
                            candidate.RegistryTransaction?.Commit();
                            FuseLog.Warning($"FUSE runtime apply for resident definition '{definition.Id}' failed after mutating runtime state; retained registry claims for created={transaction.Report.CreatedObjects.Count} updated={transaction.Report.UpdatedObjects.Count} removed={transaction.Report.RemovedObjects.Count} object(s).");
                        }

                        outcomes.Add(PackageApplyOutcome.FromReport(definition.Id, transaction.Report, 0, 1, 1));
                    }
                    else if (transaction.Report.HasErrors)
                    {
                        FusePackageFaultRegistry.RecordFault(definition.Id, "runtime apply", $"Runtime apply completed with {transaction.Report.Errors.Count} error(s).");
                        if (HasRuntimeMutations(transaction.Report))
                        {
                            candidate.RegistryTransaction?.Commit();
                            FuseLog.Warning($"FUSE runtime apply for resident definition '{definition.Id}' completed with nonfatal errors after mutating runtime state; retained registry claims for created={transaction.Report.CreatedObjects.Count} updated={transaction.Report.UpdatedObjects.Count} removed={transaction.Report.RemovedObjects.Count} object(s).");
                        }

                        outcomes.Add(PackageApplyOutcome.FromReport(definition.Id, transaction.Report, 0, 0, 1));
                    }
                    else
                    {
                        candidate.RegistryTransaction?.Commit();
                        AppliedDefinitionIds.Add(definition.Id);
                        FusePackageFaultRegistry.MarkAppliedToRuntime(definition.Id);
                        appliedCount++;
                        outcomes.Add(PackageApplyOutcome.FromReport(definition.Id, transaction.Report, 1, 0, 0));
                    }

                    transaction.Report.LogSummary();
                    if (!transaction.Report.IsFatal && !transaction.Report.HasErrors)
                    {
                        FuseLog.Info(candidate.IsReapply
                            ? $"FUSE reapplied resident definition '{definition.Id}' to runtime for '{reason ?? "unspecified"}' ({definition.Operations?.Turntables?.Count ?? 0} turntable(s), {definition.World?.SceneClones?.Count ?? 0} scene clone(s))."
                            : $"FUSE applied resident definition '{definition.Id}' to runtime for '{reason ?? "unspecified"}' ({definition.Operations?.Turntables?.Count ?? 0} turntable(s), {definition.World?.SceneClones?.Count ?? 0} scene clone(s)).");
                    }
                }
            }
            finally
            {
                foreach (var candidate in prepared)
                {
                    candidate.RegistryTransaction?.Dispose();
                    candidate.RegistryTransaction = null;
                }
            }

            FuseLog.Info(
                $"FUSE Strange-Customs-style staged graph resolver completed reason='{reason ?? "unspecified"}' " +
                $"definitions={prepared.Count} applied={appliedCount} " +
                "mode='package-grouped mixinto order, final-state deletes, single graph commit'.");
            return appliedCount;
        }

        private static bool HasRuntimeMutations(FuseApplyReport report)
        {
            return (report?.CreatedObjects?.Count ?? 0) > 0 ||
                   (report?.UpdatedObjects?.Count ?? 0) > 0 ||
                   (report?.RemovedObjects?.Count ?? 0) > 0;
        }

        private static FuseMergedTrackPlan BuildMergedTrackPlan(IReadOnlyList<FuseStagedApplyCandidate> active)
        {
            var plan = new FuseMergedTrackPlan();
            if (active == null || active.Count == 0)
            {
                return plan;
            }

            // Strange Customs effectively respected the mod/package as the unit,
            // then respected Definition.json mixinto order inside that package.
            // FUSE already loads explicit FuseDataFiles in declared order, so the
            // safest compatibility behavior is: first package-folder encounter
            // order, then file encounter order within the folder. No schema-level
            // priority knob is required.
            var ordered = active
                .Select((candidate, index) => new { candidate, index, folder = NormalizePackageFolder(candidate.Loaded?.FolderPath) })
                .GroupBy(item => item.folder, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Min(item => item.index))
                .SelectMany(group => group.OrderBy(item => item.index))
                .ToArray();

            var sequence = 0;
            foreach (var item in ordered)
            {
                var candidate = item.candidate;
                var definition = candidate.Loaded?.Definition;
                if (definition == null)
                {
                    continue;
                }

                plan.OrderedCandidates.Add(candidate);
                MergeFinalDefinitions(plan.Turntables, definition.Operations?.Turntables, candidate, ref sequence);

                var tracks = definition.Tracks;
                if (tracks == null)
                {
                    continue;
                }

                MergeFinalDeletes(plan.RemovedSpans, plan.Spans, tracks.Removals?.Spans, candidate, ref sequence);
                MergeFinalDeletes(plan.RemovedSegments, plan.Segments, tracks.Removals?.Segments, candidate, ref sequence);
                MergeFinalDeletes(plan.RemovedNodes, plan.Nodes, tracks.Removals?.Nodes, candidate, ref sequence);

                MergeFinalDefinitions(plan.Nodes, plan.RemovedNodes, tracks.Nodes, candidate, ref sequence);
                MergeFinalDefinitions(plan.Segments, plan.RemovedSegments, tracks.Segments, candidate, ref sequence);
                MergeFinalDefinitions(plan.Spans, plan.RemovedSpans, tracks.Spans, candidate, ref sequence);
            }

            FuseLog.Info(
                $"FUSE merged graph plan operation='build staged graph state' " +
                $"packages={ordered.Select(item => item.folder).Distinct(StringComparer.OrdinalIgnoreCase).Count()} " +
                $"definitions={ordered.Length} nodes={plan.Nodes.Count} segments={plan.Segments.Count} spans={plan.Spans.Count} turntables={plan.Turntables.Count} " +
                $"removedNodes={plan.RemovedNodes.Count} removedSegments={plan.RemovedSegments.Count} removedSpans={plan.RemovedSpans.Count}.");
            return plan;
        }

        private static string NormalizePackageFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return folderPath;
            }
        }

        private static void MergeFinalDeletes<T>(
            Dictionary<string, FuseMergedTrackRemoval> removals,
            Dictionary<string, FuseMergedTrackEntry<T>> definitions,
            IEnumerable<string> ids,
            FuseStagedApplyCandidate owner,
            ref int sequence)
        {
            if (ids == null)
            {
                return;
            }

            foreach (var rawId in ids)
            {
                if (string.IsNullOrWhiteSpace(rawId))
                {
                    continue;
                }

                var id = rawId.Trim();
                sequence++;
                definitions.Remove(id);
                removals[id] = new FuseMergedTrackRemoval(id, owner, sequence);
            }
        }

        private static void MergeFinalDefinitions<T>(
            Dictionary<string, FuseMergedTrackEntry<T>> definitions,
            IDictionary<string, T> values,
            FuseStagedApplyCandidate owner,
            ref int sequence)
            where T : class
        {
            if (values == null)
            {
                return;
            }

            foreach (var pair in values)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                {
                    continue;
                }

                var id = pair.Key.Trim();
                sequence++;
                definitions[id] = new FuseMergedTrackEntry<T>(id, pair.Value, owner, sequence);
            }
        }

        private static void MergeFinalDefinitions<T>(
            Dictionary<string, FuseMergedTrackEntry<T>> definitions,
            Dictionary<string, FuseMergedTrackRemoval> removals,
            IDictionary<string, T> values,
            FuseStagedApplyCandidate owner,
            ref int sequence)
            where T : class
        {
            if (values == null)
            {
                return;
            }

            foreach (var pair in values)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                {
                    continue;
                }

                var id = pair.Key.Trim();
                sequence++;
                removals.Remove(id);
                definitions[id] = new FuseMergedTrackEntry<T>(id, pair.Value, owner, sequence);
            }
        }

        private static void ApplyMergedTrackGraph(FuseMergedTrackPlan plan, string reason)
        {
            if (plan == null)
            {
                return;
            }

            TrackAPI.BeginBatch();
            try
            {
                foreach (var removal in plan.RemovedSpans.Values.OrderBy(item => item.Sequence))
                {
                    ApplyMergedSpanRemoval(removal);
                }

                foreach (var removal in plan.RemovedSegments.Values.OrderBy(item => item.Sequence))
                {
                    ApplyMergedSegmentRemoval(removal);
                }

                foreach (var removal in plan.RemovedNodes.Values.OrderBy(item => item.Sequence))
                {
                    ApplyMergedNodeRemoval(removal);
                }

                foreach (var entry in plan.Turntables.Values.OrderBy(item => item.Sequence))
                {
                    entry.Owner.Transaction.RunPhase("staged-apply-turntables", () => ApplyMergedTurntable(entry));
                }

                foreach (var entry in plan.Nodes.Values.OrderBy(item => item.Sequence))
                {
                    ApplyMergedNode(entry);
                }

                foreach (var entry in plan.Segments.Values.OrderBy(item => item.Sequence))
                {
                    ApplyMergedSegment(entry);
                }
            }
            finally
            {
                TrackAPI.EndBatch(false);
            }

            if (plan.HasStructuralChanges)
            {
                var graphTransaction = new FuseApplyTransaction("__merged-graph-rebuild__", reason, false);
                graphTransaction.RunPhase("merged-single-graph-rebuild", () => TrackAPI.RebuildGraph());
                if (graphTransaction.Report.IsFatal || graphTransaction.Report.HasErrors)
                {
                    var failure = FormatMergedGraphRebuildFailure(graphTransaction.Report);
                    foreach (var candidate in plan.OrderedCandidates.Where(item => item?.Transaction != null && !item.Transaction.Report.IsFatal))
                    {
                        var definitionId = candidate.Loaded?.Definition?.Id ?? string.Empty;
                        candidate.Transaction.RunPhase("merged-single-graph-rebuild", () =>
                            candidate.Transaction.Fatal("graph", definitionId, failure));
                    }

                    FuseLog.Warning(
                        "FUSE merged graph apply operation='merged-single-graph-rebuild' failed; " +
                        "final span apply and later runtime phases were aborted for active definitions.");
                    return;
                }

                foreach (var candidate in plan.OrderedCandidates.Where(item => !item.Transaction.Report.IsFatal))
                {
                    candidate.Transaction.PostBind("graph", candidate.Loaded.Definition.Id, "merged-rebuilt-before-final-spans");
                }
            }
            else
            {
                FuseLog.Info(
                    "FUSE merged graph apply operation='merged-single-graph-rebuild' skipped: " +
                    "no final structural track mutations in active definitions.");
            }

            TrackAPI.BeginBatch();
            try
            {
                foreach (var entry in plan.Spans.Values.OrderBy(item => item.Sequence))
                {
                    ApplyMergedSpan(entry);
                }
            }
            finally
            {
                TrackAPI.EndBatch(false);
            }
        }

        private static string FormatMergedGraphRebuildFailure(FuseApplyReport report)
        {
            var message = report?.FatalReason;
            if (string.IsNullOrWhiteSpace(message) && report?.Errors != null && report.Errors.Count > 0)
            {
                message = report.Errors[0];
            }

            return string.IsNullOrWhiteSpace(message)
                ? "merged graph rebuild failed"
                : "merged graph rebuild failed: " + message;
        }

        private static void ApplyMergedSpanRemoval(FuseMergedTrackRemoval removal)
        {
            var transaction = removal.Owner.Transaction;
            var definition = removal.Owner.Loaded.Definition;
            if (TrackAPI.GetSpan(removal.Id) != null)
            {
                transaction.TryRemove("track span", removal.Id, () =>
                {
                    FuseTrackRemovalSnapshotStore.CaptureSpanBeforeRemoval(definition.Id, removal.Id, transaction);
                    TrackAPI.RemoveSpan(removal.Id);
                });
            }
            else
            {
                transaction.Skipped("track span removal", removal.Id, "missing");
            }
        }

        private static void ApplyMergedSegmentRemoval(FuseMergedTrackRemoval removal)
        {
            var transaction = removal.Owner.Transaction;
            var definition = removal.Owner.Loaded.Definition;
            if (TrackAPI.GetSegment(removal.Id) != null)
            {
                transaction.TryRemove("track segment", removal.Id, () =>
                {
                    FuseTrackRemovalSnapshotStore.CaptureSegmentBeforeRemoval(definition.Id, removal.Id, transaction);
                    TrackAPI.RemoveSegment(removal.Id);
                });
            }
            else
            {
                transaction.Skipped("track segment removal", removal.Id, "missing");
            }
        }

        private static void ApplyMergedNodeRemoval(FuseMergedTrackRemoval removal)
        {
            var transaction = removal.Owner.Transaction;
            var definition = removal.Owner.Loaded.Definition;
            if (TrackAPI.GetNode(removal.Id) != null)
            {
                transaction.TryRemove("track node", removal.Id, () =>
                {
                    FuseTrackRemovalSnapshotStore.CaptureNodeBeforeRemoval(definition.Id, removal.Id, transaction);
                    TrackAPI.RemoveNode(removal.Id);
                });
            }
            else
            {
                transaction.Skipped("track node removal", removal.Id, "missing");
            }
        }

        private static void ApplyMergedNode(FuseMergedTrackEntry<FuseNode> entry)
        {
            var transaction = entry.Owner.Transaction;
            var definition = entry.Owner.Loaded.Definition;
            if (!TryClaimOrSkip(FuseClaimKind.Node, "track node", entry.Id, definition.Id, transaction))
            {
                return;
            }

            var exists = TrackAPI.GetNode(entry.Id) != null;
            transaction.TryApply("track node", entry.Id, exists, () =>
            {
                if (exists)
                {
                    TrackAPI.UpdateNode(entry.Id, entry.Value);
                }
                else
                {
                    TrackAPI.AddNode(entry.Id, entry.Value);
                }
            });
        }

        private static void ApplyMergedSegment(FuseMergedTrackEntry<FuseSegment> entry)
        {
            var transaction = entry.Owner.Transaction;
            var definition = entry.Owner.Loaded.Definition;
            if (!TryClaimOrSkip(FuseClaimKind.Segment, "track segment", entry.Id, definition.Id, transaction))
            {
                return;
            }

            var runtimeSegment = TrackAPI.GetSegment(entry.Id);
            var exists = runtimeSegment != null;
            var startNode = TrackAPI.GetNode(entry.Value.StartNodeId);
            var endNode = TrackAPI.GetNode(entry.Value.EndNodeId);
            if (startNode == null || endNode == null)
            {
                FuseLog.Warning(
                    $"FUSE apply pre-check package='{definition.Id}' operation='apply-merged-segments' " +
                    $"kind='track segment' id='{entry.Id}' " +
                    $"start='{entry.Value.StartNodeId ?? string.Empty}' startExists={startNode != null} " +
                    $"end='{entry.Value.EndNodeId ?? string.Empty}' endExists={endNode != null} " +
                    "message='node reference not bound in merged final graph; AddSegment may fail per-segment'.");
            }

            transaction.TryApply("track segment", entry.Id, exists, () =>
            {
                if (runtimeSegment == null)
                {
                    TrackAPI.AddSegment(entry.Id, entry.Value);
                }
                else if (runtimeSegment.a.id != entry.Value.StartNodeId || runtimeSegment.b.id != entry.Value.EndNodeId)
                {
                    FuseLog.Info(
                        $"FUSE apply package='{definition.Id}' operation='apply-merged-segments' " +
                        $"kind='track segment' id='{entry.Id}' " +
                        $"message='endpoint change detected " +
                        $"oldStart=\"{runtimeSegment.a.id}\" newStart=\"{entry.Value.StartNodeId}\" " +
                        $"oldEnd=\"{runtimeSegment.b.id}\" newEnd=\"{entry.Value.EndNodeId}\" " +
                        $"newStartExists={startNode != null} newEndExists={endNode != null}'.");

                    TrackAPI.RemoveSegment(entry.Id);
                    TrackAPI.AddSegment(entry.Id, entry.Value);
                }
                else
                {
                    TrackAPI.UpdateSegment(entry.Id, entry.Value);
                }
            });
        }

        private static void ApplyMergedTurntable(FuseMergedTrackEntry<FuseTurntable> entry)
        {
            var transaction = entry.Owner.Transaction;
            var definition = entry.Owner.Loaded.Definition;
            if (!TryClaimOrSkip(FuseClaimKind.Turntable, "turntable", entry.Id, definition.Id, transaction))
            {
                return;
            }

            var exists = TurntableAPI.GetTurntable(entry.Id) != null;
            transaction.TryApply("turntable", entry.Id, exists, () =>
            {
                if (exists)
                {
                    TurntableAPI.UpdateTurntable(entry.Id, entry.Value);
                }
                else
                {
                    TurntableAPI.AddTurntable(entry.Id, entry.Value);
                }
            });
        }

        private static void ApplyMergedSpan(FuseMergedTrackEntry<FuseSpan> entry)
        {
            var transaction = entry.Owner.Transaction;
            var definition = entry.Owner.Loaded.Definition;
            if (!TryClaimOrSkip(FuseClaimKind.Span, "track span", entry.Id, definition.Id, transaction))
            {
                return;
            }

            var exists = TrackAPI.GetSpan(entry.Id) != null;
            transaction.TryApply("track span", entry.Id, exists, () =>
            {
                if (exists)
                {
                    TrackAPI.UpdateSpan(entry.Id, entry.Value);
                }
                else
                {
                    TrackAPI.AddSpan(entry.Id, entry.Value);
                }
            });
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

        // Walks every loaded definition's progression payload and pre-enables
        // every track group whose owning section/feature is initially enabled
        // (or carries no prerequisites and so unlocks at map load).
        //
        // Why this is needed: ApplyDefinitionToRuntime runs apply-segments and
        // RebuildGraph BEFORE apply-progression, so any segment whose groupId
        // points at a not-yet-enabled track group is filtered out by the graph
        // rebuild and shows up as "missing after apply" - cascading into fatal
        // preflight failures for downstream packages (e.g. legacy Asheville
        // town-clyde / town-addie referencing segments in clyde_yard /
        // addie-yard-N). Enabling the groups up-front lets the rebuild keep
        // them, and the actual gating still works because progression apply
        // later flips disabled-on-unlock features that should start disabled.
        private static void PreEnableInitialTrackGroups(IReadOnlyList<string> orderedIds)
        {
            if (orderedIds == null || orderedIds.Count == 0)
            {
                return;
            }

            var groupsToEnable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groupsFromSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in orderedIds)
            {
                if (!LoadedMods.TryGetValue(id, out var loaded))
                {
                    continue;
                }
                CollectInitialTrackGroups(loaded?.Definition?.Progression, groupsToEnable);
                CollectSegmentGroupIds(loaded?.Definition?.Tracks, groupsFromSegments);
            }

            // Every group that any segment belongs to must be present in
            // Graph.enabledGroupIds before the merged graph rebuild, otherwise
            // Graph.AddSegment filters those segments out and dependent spans
            // cannot bind. This is a transient keep-alive only; after
            // progression definitions are applied, ProgressionAPI refreshes the
            // base-game MapFeature state so locked groups/objects are disabled
            // again before post-bind validation.
            groupsToEnable.UnionWith(groupsFromSegments);

            if (groupsToEnable.Count == 0)
            {
                return;
            }

            var graph = Graph.Shared;
            if (graph == null)
            {
                FuseLog.Warning(
                    "FUSE pre-enable track groups skipped package='<all>' operation='pre-enable track groups' " +
                    "kind='graph' id='<shared>' message='Graph.Shared was not available'.");
                return;
            }

            var enabledCount = 0;
            var changedCount = 0;
            foreach (var groupId in groupsToEnable)
            {
                try
                {
                    if (graph.SetGroupEnabled(groupId, true))
                    {
                        changedCount++;
                    }
                    enabledCount++;
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE pre-enable track group failed groupId='{groupId}' " +
                        $"message='{ex.Message}'.");
                }
            }

            FuseLog.Info(
                $"FUSE pre-enabled {enabledCount} track group(s) " +
                $"changed={changedCount} " +
                $"({groupsFromSegments.Count} from segment groupIds) " +
                "before apply-segments to prevent graph-rebuild culling; progression refresh will restore gated state after apply.");
        }

        private static void CollectSegmentGroupIds(FuseTrackDefinition tracks, HashSet<string> sink)
        {
            if (tracks?.Segments == null)
            {
                return;
            }
            foreach (var segment in tracks.Segments.Values)
            {
                if (segment != null && !string.IsNullOrWhiteSpace(segment.GroupId))
                {
                    sink.Add(segment.GroupId);
                }
            }
        }

        private static void CollectInitialTrackGroups(FuseProgressionRoot progression, HashSet<string> sink)
        {
            if (progression == null)
            {
                return;
            }

            if (progression.MapFeatures != null)
            {
                foreach (var feature in progression.MapFeatures.Values)
                {
                    if (feature == null || !feature.InitiallyEnabled)
                    {
                        continue;
                    }
                    AddNonEmpty(sink, feature.TrackGroupsEnableOnUnlock);
                    AddNonEmpty(sink, feature.GroupIds);
                }
            }

            if (progression.Sections != null)
            {
                foreach (var section in progression.Sections)
                {
                    if (section == null)
                    {
                        continue;
                    }
                    if (HasNoPrerequisites(section))
                    {
                        AddNonEmpty(sink, section.TrackGroupsEnableOnUnlock);
                    }
                }
            }

            if (progression.Progressions != null)
            {
                foreach (var sub in progression.Progressions.Values)
                {
                    if (sub?.Sections == null)
                    {
                        continue;
                    }
                    foreach (var section in sub.Sections.Values)
                    {
                        if (section == null)
                        {
                            continue;
                        }
                        if (HasNoPrerequisites(section))
                        {
                            AddNonEmpty(sink, section.TrackGroupsEnableOnUnlock);
                        }
                    }
                }
            }
        }

        private static bool HasNoPrerequisites(FuseSection section)
        {
            return (section.PrerequisiteSections == null || section.PrerequisiteSections.Length == 0)
                && (section.PrerequisiteSectionIds == null || section.PrerequisiteSectionIds.Length == 0);
        }

        private static void AddNonEmpty(HashSet<string> sink, string[] values)
        {
            if (values == null)
            {
                return;
            }
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    sink.Add(value);
                }
            }
        }

        private static void ApplyGlobalLoadCatalog(IReadOnlyList<string> orderedIds, string reason)
        {
            if (orderedIds == null || orderedIds.Count == 0)
            {
                return;
            }

            var transaction = new FuseApplyTransaction("__global-load-catalog__", reason ?? "unspecified", false);
            var attempted = 0;
            foreach (var id in orderedIds)
            {
                if (!LoadedMods.TryGetValue(id, out var loaded) || loaded?.Definition?.Operations?.Loads == null)
                {
                    continue;
                }

                if (!FuseModRequirementResolver.ShouldApply(loaded, out _))
                {
                    continue;
                }

                foreach (var load in loaded.Definition.Operations.Loads)
                {
                    if (string.IsNullOrWhiteSpace(load.Key) || load.Value == null)
                    {
                        transaction.Skipped("load", load.Key ?? string.Empty, "missing id or definition");
                        continue;
                    }

                    attempted++;
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

            if (attempted > 0 || transaction.Report.Warnings.Count > 0 || transaction.Report.Errors.Count > 0)
            {
                FuseLog.Info(
                    $"FUSE global load catalog operation='pre-apply loads' reason='{reason ?? "unspecified"}' " +
                    $"attempted={attempted} errors={transaction.Report.Errors.Count} warnings={transaction.Report.Warnings.Count}.");
                transaction.Report.LogSummary();
            }
        }

        private static void ApplyDeferredOperationBindings(IReadOnlyList<string> orderedIds, string reason)
        {
            if (orderedIds == null || orderedIds.Count == 0)
            {
                return;
            }

            var transaction = new FuseApplyTransaction("__deferred-operation-bindings__", reason ?? "unspecified", false);
            var attempted = 0;
            foreach (var id in orderedIds)
            {
                if (!LoadedMods.TryGetValue(id, out var loaded) || loaded?.Definition?.Operations == null)
                {
                    continue;
                }

                if (!FuseModRequirementResolver.ShouldApply(loaded, out _))
                {
                    continue;
                }

                attempted += ApplyDeferredLoaders(loaded.Definition, transaction);
                attempted += ApplyDeferredStations(loaded.Definition, transaction);
            }

            if (attempted > 0 || transaction.Report.SkippedObjects.Count > 0 || transaction.Report.Errors.Count > 0)
            {
                FuseLog.Info(
                    $"FUSE deferred operation bindings operation='retry loaders/stations' reason='{reason ?? "unspecified"}' " +
                    $"attempted={attempted} skipped={transaction.Report.SkippedObjects.Count} errors={transaction.Report.Errors.Count}.");
                transaction.Report.LogSummary();
            }
        }

        private static int ApplyDeferredLoaders(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            var count = 0;
            foreach (var loader in definition.Operations?.Loaders ?? new Dictionary<string, FuseLoader>())
            {
                if (!LoaderDependenciesAvailable(loader.Value, out var reason))
                {
                    transaction.Skipped("loader", loader.Key, reason);
                    continue;
                }

                if (!TryClaimOrSkip(FuseClaimKind.Loader, "loader", loader.Key, definition.Id, transaction))
                {
                    continue;
                }

                count++;
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

            return count;
        }

        private static int ApplyDeferredStations(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            var count = 0;
            foreach (var station in definition.Operations?.Stations ?? new Dictionary<string, FuseStation>())
            {
                if (!StationDependenciesAvailable(station.Value, out var reason))
                {
                    transaction.Skipped("station", station.Key, reason);
                    continue;
                }

                if (!TryClaimOrSkip(FuseClaimKind.Station, "station", station.Key, definition.Id, transaction))
                {
                    continue;
                }

                count++;
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

            return count;
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
                    // RebuildGraph() makes valid legacy/Strange-Customs spans
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

        private static void RunPreflightValidation(
            FuseModDefinition definition,
            FuseApplyTransaction transaction,
            FusePreflightReferenceContext referenceContext = null)
        {
            var validation = Validator.Validate(definition);
            FuseEvents.RaiseValidationCompleted(definition != null ? definition.Id : string.Empty, validation);

            foreach (var warning in validation.Warnings)
            {
                if (IsResolvedExternalReferenceWarning(warning, referenceContext))
                {
                    continue;
                }

                transaction.Warning("preflight", warning.Field, FormatValidationIssue(warning));
            }

            foreach (var error in validation.Errors)
            {
                transaction.Error("preflight", error.Field, FormatValidationIssue(error));
            }

            ValidateRuntimeReferences(definition, transaction, referenceContext);
            if (transaction.Report.Errors.Count > 0)
            {
                transaction.Fatal("definition", definition?.Id ?? string.Empty, $"preflight validation failed with {transaction.Report.Errors.Count} error(s)");
            }
        }

        private static bool IsResolvedExternalReferenceWarning(
            ValidationIssue issue,
            FusePreflightReferenceContext referenceContext)
        {
            if (issue == null || referenceContext == null)
            {
                return false;
            }

            var value = issue.Value as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (issue.Code)
            {
                case "fuse.track.node.external":
                    return referenceContext.NodeIds.Contains(value) || RuntimeNodeExists(value);
                case "fuse.track.segment.external":
                    return referenceContext.SegmentIds.Contains(value) || RuntimeSegmentExists(value);
                default:
                    return false;
            }
        }

        private static bool RuntimeNodeExists(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return false;
            }

            try
            {
                return TrackAPI.GetNode(nodeId) != null;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE preflight runtime node lookup failed operation='preflight-validation' " +
                    $"id='{nodeId}' reason='{ex.Message}'.");
                return false;
            }
        }

        private static bool RuntimeSegmentExists(string segmentId)
        {
            if (string.IsNullOrWhiteSpace(segmentId))
            {
                return false;
            }

            try
            {
                return TrackAPI.GetSegment(segmentId) != null;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE preflight runtime segment lookup failed operation='preflight-validation' " +
                    $"id='{segmentId}' reason='{ex.Message}'.");
                return false;
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

        private static FusePreflightReferenceContext BuildPreflightReferenceContext(
            IEnumerable<FuseModDefinition> definitions)
        {
            var context = new FusePreflightReferenceContext();
            if (definitions == null)
            {
                return context;
            }

            foreach (var definition in definitions.Where(item => item != null))
            {
                AddKeys(context.NodeIds, definition.Tracks?.Nodes);
                AddKeys(context.SegmentIds, definition.Tracks?.Segments);
                AddKeys(context.SpanIds, definition.Tracks?.Spans);
                AddKeys(context.LoadIds, definition.Operations?.Loads);
                AddKeys(context.IndustryIds, definition.Operations?.Industries);
                context.NodeIds.UnionWith(CollectGeneratedNodeIds(definition));
                context.SegmentIds.UnionWith(CollectGeneratedSegmentIds(definition));

                foreach (var industry in definition.Operations?.Industries ?? new Dictionary<string, FuseIndustry>())
                {
                    foreach (var component in industry.Value?.Components ?? new Dictionary<string, FuseIndustryComponent>())
                    {
                        if (string.Equals(component.Value?.Type, "passengerStop", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(component.Key))
                        {
                            context.PassengerStopIds.Add(component.Key);
                        }

                        if (!string.IsNullOrWhiteSpace(component.Value?.PassengerStopId))
                        {
                            context.PassengerStopIds.Add(component.Value.PassengerStopId);
                        }
                    }
                }
            }

            return context;
        }

        private static void ValidateRuntimeReferences(
            FuseModDefinition definition,
            FuseApplyTransaction transaction,
            FusePreflightReferenceContext referenceContext)
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

            ValidateTrackRuntimeReferences(definition, transaction, referenceContext);
            ValidateOperationRuntimeReferences(definition, transaction, referenceContext);
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

            return MutatesTrackStructure(definition) ||
                   (definition.Tracks?.Spans?.Count ?? 0) > 0;
        }

        /// <summary>
        /// Returns true when the package changes the node/segment topology that
        /// TrackSpan route validation depends on. Spans are intentionally excluded:
        /// they must be applied after this structural graph rebuild, not before it.
        /// </summary>
        private static bool MutatesTrackStructure(FuseModDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return (definition.Tracks?.Nodes?.Count ?? 0) > 0 ||
                   (definition.Tracks?.Segments?.Count ?? 0) > 0 ||
                   HasAny(definition.Tracks?.Removals?.Nodes) ||
                   HasAny(definition.Tracks?.Removals?.Segments) ||
                   HasAny(definition.Tracks?.Removals?.Spans) ||
                   (definition.Operations?.Turntables?.Count ?? 0) > 0;
        }

        private static bool HasAny(IEnumerable<string> values)
        {
            return values != null && values.Any(value => !string.IsNullOrWhiteSpace(value));
        }

        private static void ValidateTrackRuntimeReferences(
            FuseModDefinition definition,
            FuseApplyTransaction transaction,
            FusePreflightReferenceContext referenceContext)
        {
            var tracks = definition.Tracks;
            if (tracks == null)
            {
                return;
            }

            var definedNodes = referenceContext?.NodeIds ?? CollectLoadedNodeIds(definition);
            var definedSegments = referenceContext?.SegmentIds ?? CollectLoadedSegmentIds(definition);
            var generatedNodes = referenceContext != null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : CollectLoadedGeneratedNodeIds(definition);
            var generatedSegments = referenceContext != null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : CollectLoadedGeneratedSegmentIds(definition);

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

        private static void ValidateOperationRuntimeReferences(
            FuseModDefinition definition,
            FuseApplyTransaction transaction,
            FusePreflightReferenceContext referenceContext)
        {
            var operations = definition.Operations;
            if (operations == null)
            {
                return;
            }

            var definedLoads = referenceContext?.LoadIds ?? CollectLoadedLoadIds(definition);
            var definedSpans = referenceContext?.SpanIds ?? CollectLoadedSpanIds(definition);

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
                    (referenceContext == null || !referenceContext.IndustryIds.Contains(loader.Value.IndustryId)) &&
                    (operations.Industries == null || !operations.Industries.ContainsKey(loader.Value.IndustryId)) &&
                    IndustryAPI.GetIndustry(loader.Value.IndustryId) == null)
                {
                    transaction.Warning("loader", loader.Key, $"industryId '{loader.Value.IndustryId}' is not defined in this package or runtime");
                }
            }

            foreach (var station in operations.Stations ?? new Dictionary<string, FuseStation>())
            {
                if (!string.IsNullOrWhiteSpace(station.Value?.PassengerStopId) &&
                    (referenceContext == null || !referenceContext.PassengerStopIds.Contains(station.Value.PassengerStopId)) &&
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

        private static HashSet<string> CollectLoadedNodeIds(FuseModDefinition current)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddKeys(result, current?.Tracks?.Nodes);
            foreach (var loaded in LoadedMods.Values)
            {
                AddKeys(result, loaded?.Definition?.Tracks?.Nodes);
            }

            return result;
        }

        private static HashSet<string> CollectLoadedSegmentIds(FuseModDefinition current)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddKeys(result, current?.Tracks?.Segments);
            foreach (var loaded in LoadedMods.Values)
            {
                AddKeys(result, loaded?.Definition?.Tracks?.Segments);
            }

            return result;
        }

        private static HashSet<string> CollectLoadedSpanIds(FuseModDefinition current)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddKeys(result, current?.Tracks?.Spans);
            foreach (var loaded in LoadedMods.Values)
            {
                AddKeys(result, loaded?.Definition?.Tracks?.Spans);
            }

            return result;
        }

        private static HashSet<string> CollectLoadedLoadIds(FuseModDefinition current)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddKeys(result, current?.Operations?.Loads);
            foreach (var loaded in LoadedMods.Values)
            {
                AddKeys(result, loaded?.Definition?.Operations?.Loads);
            }

            return result;
        }

        private static HashSet<string> CollectLoadedGeneratedNodeIds(FuseModDefinition current)
        {
            var result = CollectGeneratedNodeIds(current);
            foreach (var loaded in LoadedMods.Values)
            {
                result.UnionWith(CollectGeneratedNodeIds(loaded?.Definition));
            }

            return result;
        }

        private static HashSet<string> CollectLoadedGeneratedSegmentIds(FuseModDefinition current)
        {
            var result = CollectGeneratedSegmentIds(current);
            foreach (var loaded in LoadedMods.Values)
            {
                result.UnionWith(CollectGeneratedSegmentIds(loaded?.Definition));
            }

            return result;
        }

        private static void AddKeys<TValue>(ISet<string> sink, IDictionary<string, TValue> dictionary)
        {
            if (sink == null || dictionary == null)
            {
                return;
            }

            foreach (var key in dictionary.Keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    sink.Add(key);
                }
            }
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
            if (FuseRegistry.TryClaim(kind, id, packageId, true, out var owner))
            {
                return true;
            }

            if (CanReplaceSiblingClaim(owner, packageId))
            {
                var previousOwner = owner;
                FuseRegistry.Release(kind, id, owner);
                if (FuseRegistry.TryClaim(kind, id, packageId, out owner))
                {
                    FuseLog.Info(
                        $"FUSE reassigned sibling claim package='{packageId}' operation='claim runtime object' " +
                        $"kind='{transactionKind}' id='{id}' previousOwner='{previousOwner ?? string.Empty}'.");
                    return true;
                }
            }

            // Now that sibling handoff has been ruled out, make the real claim
            // attempt so the registry records a user-facing conflict.
            if (FuseRegistry.TryClaim(kind, id, packageId, out owner))
            {
                return true;
            }

            transaction.Skipped(transactionKind, id, $"claimed-by:{owner ?? "unknown"}");
            return false;
        }

        private static bool CanReplaceSiblingClaim(string existingOwner, string newOwner)
        {
            if (string.IsNullOrWhiteSpace(existingOwner) ||
                string.IsNullOrWhiteSpace(newOwner) ||
                string.Equals(existingOwner, newOwner, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!LoadedMods.TryGetValue(existingOwner, out var existing) ||
                !LoadedMods.TryGetValue(newOwner, out var replacement))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(existing.FolderPath) &&
                   string.Equals(
                       Path.GetFullPath(existing.FolderPath),
                       Path.GetFullPath(replacement.FolderPath),
                       StringComparison.OrdinalIgnoreCase);
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

        private static void ApplyTrackAreas(FuseModDefinition definition, FuseApplyTransaction transaction, bool applyOrdering = true)
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

            if (applyOrdering)
            {
                TrackAPI.ApplyAreaOrdering();
            }
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
                    if (!LoaderDependenciesAvailable(loader.Value, out var dependencyReason))
                    {
                        transaction.Skipped("loader", loader.Key, dependencyReason + "; queued for deferred retry");
                        continue;
                    }

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
                    if (!StationDependenciesAvailable(station.Value, out var dependencyReason))
                    {
                        transaction.Skipped("station", station.Key, dependencyReason + "; queued for deferred retry");
                        continue;
                    }

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

        private static bool LoaderDependenciesAvailable(FuseLoader loader, out string reason)
        {
            reason = string.Empty;
            if (loader == null)
            {
                reason = "loader definition is null";
                return false;
            }

            if (string.IsNullOrWhiteSpace(loader.IndustryId))
            {
                return true;
            }

            if (IndustryAPI.GetIndustry(loader.IndustryId) != null)
            {
                return true;
            }

            reason = $"industryId '{loader.IndustryId}' is not available yet";
            return false;
        }

        private static bool StationDependenciesAvailable(FuseStation station, out string reason)
        {
            reason = string.Empty;
            if (station == null)
            {
                reason = "station definition is null";
                return false;
            }

            if (string.IsNullOrWhiteSpace(station.PassengerStopId))
            {
                reason = "passengerStopId is empty";
                return false;
            }

            if (StationAPI.GetPassengerStop(station.PassengerStopId) != null)
            {
                return true;
            }

            reason = $"passengerStopId '{station.PassengerStopId}' is not available yet";
            return false;
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
                        if (!FuseAuthoringPersistenceService.ApplyPackageEntityToRuntime(entity))
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
                        if (!FuseAuthoringPersistenceService.ApplyPackageEntityToRuntime(entity))
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
                            ProgressionAPI.UpdateProgression(progression.Key, progression.Value, definition.Id);
                        }
                        else
                        {
                            ProgressionAPI.AddProgression(progression.Key, progression.Value, definition.Id);
                        }
                    });
                }
            }
        }

        private static void ValidatePostBind(
            FuseModDefinition definition,
            FuseApplyTransaction transaction,
            FuseMergedTrackPlan mergedTrackPlan = null)
        {
            if (definition == null)
            {
                transaction.Warning("definition", string.Empty, "definition was null during post-bind validation");
                return;
            }

            foreach (var nodeId in definition.Tracks?.Nodes?.Keys ?? Enumerable.Empty<string>())
            {
                if (mergedTrackPlan != null && !mergedTrackPlan.ShouldValidateNode(definition.Id, nodeId))
                {
                    continue;
                }

                if (TrackAPI.GetNode(nodeId) == null)
                {
                    FuseLoadReport.RecordGraphPostBindIssue(definition.Id, "track node", nodeId, "missing after apply");
                    transaction.Warning("track node", nodeId, "missing after apply");
                }
            }

            foreach (var segmentId in definition.Tracks?.Segments?.Keys ?? Enumerable.Empty<string>())
            {
                if (mergedTrackPlan != null && !mergedTrackPlan.ShouldValidateSegment(definition.Id, segmentId))
                {
                    continue;
                }

                if (TrackAPI.GetSegment(segmentId) == null)
                {
                    var segmentDefinition = GetPostBindSegmentDefinition(definition, mergedTrackPlan, segmentId);
                    if (IsSegmentHiddenByDisabledGroup(segmentDefinition))
                    {
                        transaction.PostBind("track segment", segmentId, $"hidden by disabled group '{segmentDefinition.GroupId}'");
                        continue;
                    }

                    FuseLoadReport.RecordGraphPostBindIssue(definition.Id, "track segment", segmentId, "missing after apply");
                    transaction.Warning("track segment", segmentId, "missing after apply");
                }
            }

            foreach (var spanEntry in definition.Tracks?.Spans ?? Enumerable.Empty<KeyValuePair<string, FuseSpan>>())
            {
                var spanId = spanEntry.Key;
                if (mergedTrackPlan != null && !mergedTrackPlan.ShouldValidateSpan(definition.Id, spanId))
                {
                    continue;
                }

                if (TrackAPI.GetSpan(spanId) == null)
                {
                    if (IsSpanHiddenByDisabledGroup(definition, mergedTrackPlan, spanEntry.Value))
                    {
                        transaction.PostBind("track span", spanId, "hidden by disabled endpoint group");
                        continue;
                    }

                    FuseLoadReport.RecordGraphPostBindIssue(definition.Id, "track span", spanId, "missing after apply");
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

        private static FuseSegment GetPostBindSegmentDefinition(
            FuseModDefinition definition,
            FuseMergedTrackPlan mergedTrackPlan,
            string segmentId)
        {
            if (string.IsNullOrWhiteSpace(segmentId))
            {
                return null;
            }

            if (mergedTrackPlan != null && mergedTrackPlan.TryGetSegmentDefinition(segmentId, out var mergedDefinition))
            {
                return mergedDefinition;
            }

            return definition?.Tracks?.Segments != null &&
                   definition.Tracks.Segments.TryGetValue(segmentId, out var localDefinition)
                ? localDefinition
                : null;
        }

        private static bool IsSpanHiddenByDisabledGroup(
            FuseModDefinition definition,
            FuseMergedTrackPlan mergedTrackPlan,
            FuseSpan span)
        {
            if (span == null)
            {
                return false;
            }

            return IsSegmentHiddenByDisabledGroup(GetPostBindSegmentDefinition(definition, mergedTrackPlan, span.Upper?.SegmentId)) ||
                   IsSegmentHiddenByDisabledGroup(GetPostBindSegmentDefinition(definition, mergedTrackPlan, span.Lower?.SegmentId));
        }

        private static bool IsSegmentHiddenByDisabledGroup(FuseSegment segment)
        {
            var groupId = segment?.GroupId;
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return false;
            }

            var graph = Graph.Shared;
            if (graph == null)
            {
                return false;
            }

            return graph.enabledGroupIds == null ||
                   !graph.enabledGroupIds.Contains(groupId);
        }
    }
}
