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
            public readonly List<string> ProtectedRemovedSegmentSamples = new List<string>();

            public int ProtectedRemovedSegments { get; set; }

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

        /// <summary>
        /// Cross-package final-state removal plan for non-track operations,
        /// mirroring how <see cref="FuseMergedTrackPlan"/> resolves track
        /// node/segment/span deletes. Without this, the per-package
        /// staged-apply-world-removals and staged-apply-operations-removals
        /// phases ran in a pass that completed before any package's
        /// apply-world-objects/apply-operations adds, so "package B removes
        /// what package A adds" patterns failed silently — B's
        /// <see cref="SceneryAPI.TryRemoveScenery"/> couldn't find a target.
        /// This plan defers all such removals to a single end-of-batch pass
        /// that runs after apply-scene-clones, with later adds of the same id
        /// canceling earlier removals so the "remove base then add custom"
        /// pattern still works inside a single package.
        /// </summary>
        private sealed class FuseMergedRemovalPlan
        {
            public readonly Dictionary<string, FuseMergedTrackRemoval> Scenery =
                new Dictionary<string, FuseMergedTrackRemoval>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, FuseMergedTrackRemoval> SceneClones =
                new Dictionary<string, FuseMergedTrackRemoval>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, FuseMergedTrackRemoval> Splineys =
                new Dictionary<string, FuseMergedTrackRemoval>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, FuseMergedTrackRemoval> MapLabels =
                new Dictionary<string, FuseMergedTrackRemoval>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, FuseMergedTrackRemoval> MapMasks =
                new Dictionary<string, FuseMergedTrackRemoval>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, FuseMergedTrackRemoval> TelegraphPoles =
                new Dictionary<string, FuseMergedTrackRemoval>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, FuseMergedTrackRemoval> Industries =
                new Dictionary<string, FuseMergedTrackRemoval>(StringComparer.OrdinalIgnoreCase);

            public bool HasAny =>
                Scenery.Count > 0 || SceneClones.Count > 0 || Splineys.Count > 0 ||
                MapLabels.Count > 0 || MapMasks.Count > 0 || TelegraphPoles.Count > 0 ||
                Industries.Count > 0;
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
            List<PackageApplyOutcome> outcomes)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return 0;
            }

            var appliedCount = 0;
            var prepared = new List<FuseStagedApplyCandidate>();
            FuseLegacyGameGraphCompatibility.Expand(
                candidates.Select(candidate => candidate.Loaded).Where(loaded => loaded != null).ToArray());
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
                    // World/operations removals also resolve to final-state deletes
                    // across packages, just like the track plan. Per-package
                    // staged-apply-world-removals/staged-apply-operations-removals
                    // phases used to run before any apply-world-objects /
                    // apply-operations adds, so "package B removes what package A
                    // adds" patterns failed because B's target wasn't in the
                    // scene yet — see ApplyMergedRemovalPlan call below for the
                    // matching apply pass.
                    var mergedRemovalPlan = BuildMergedRemovalPlan(active);

                    // Register suppression claims before the one merged graph
                    // rebuild, then apply track-group state without requesting a
                    // second rebuild. The merged rebuild below now consumes the
                    // final enabled/available group sets directly.
                    foreach (var candidate in active.Where(item => !item.Transaction.Report.IsFatal))
                    {
                        var definition = candidate.Loaded.Definition;
                        var transaction = candidate.Transaction;
                        transaction.RunPhase(
                            "register-world-suppressions",
                            () => FuseWorldSuppressor.RegisterDefinition(definition, transaction));
                    }
                    FuseWorldSuppressor.ApplyActiveTrackGroupSuppressionsBeforeGraphRebuild(
                        "merged graph pre-apply");

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

                    // Mandelas (scene clones) run AFTER operations so industries — which can
                    // share display names with vanilla scene paths — cannot re-activate a
                    // GameObject the mandela is supposed to disable. This matches the legacy
                    // patcher's "process mandelas last" ordering.
                    foreach (var candidate in active.Where(item => !item.Transaction.Report.IsFatal))
                    {
                        var definition = candidate.Loaded.Definition;
                        var loaded = candidate.Loaded;
                        var transaction = candidate.Transaction;
                        transaction.RunPhase("apply-scene-clones", () => ApplySceneClones(definition, loaded.DefinitionPath, transaction));
                    }

                    // Drive the BuildSpliney pass before apply-progression so
                    // any scenery items a hosted plugin synthesizes (e.g.
                    // CLB.ModularScenery's lights/foundations/walkways for the
                    // East Whittier repair facility) exist as discoverable
                    // SceneryAssetInstances when ApplyProgressionDefinition
                    // resolves "scenery://<id>" references for the
                    // gameObjectsEnableOnUnlock lock list. Without this,
                    // plugin-built scenery is always visible regardless of
                    // progression state because the gating ResolveGameObject
                    // can't find them.
                    try
                    {
                        var builderResult = FuseSplineyPluginHost.InvokeBuilders(null);
                        if (builderResult.TaskCount > 0 || builderResult.BuiltCount > 0)
                        {
                            FuseLog.Info(
                                "FUSE legacy spliney plugin host invoked builders " +
                                $"tasks={builderResult.TaskCount} " +
                                $"builderTypes={builderResult.BuilderTypeCount} " +
                                $"built={builderResult.BuiltCount} " +
                                $"unmatched={builderResult.UnmatchedCount} " +
                                $"failures={builderResult.FailureCount}.");
                        }
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Exception(
                            "FUSE legacy spliney plugin host BuildSpliney pass " +
                            "raised an exception; some legacy splineys may render " +
                            "without their custom visual meshes",
                            ex);
                    }

                    // World/operations removals fire here, after every package has
                    // had a chance to add its scenery / industries / scene clones
                    // and after legacy plugins built their own scenery. The plan
                    // was built with latest-write-wins-per-id, so a later
                    // package's add already cancelled any earlier package's
                    // remove of the same id (and vice versa).
                    ApplyMergedRemovalPlan(mergedRemovalPlan);

                    ProgressionAPI.BeginScenePathResolutionBatch();
                    try
                    {
                        foreach (var candidate in active.Where(item => !item.Transaction.Report.IsFatal))
                        {
                            var definition = candidate.Loaded.Definition;
                            var transaction = candidate.Transaction;
                            transaction.RunPhase("apply-progression", () => ApplyProgressionDefinition(definition, transaction));
                        }
                    }
                    finally
                    {
                        ProgressionAPI.EndScenePathResolutionBatch();
                    }

                    // One merged suppression apply for all packages. Must run
                    // before the progression refresh below: area suppression
                    // flips ProgressionDisabled flags that the refresh reads.
                    // The batch folds the per-group RequestRebuild calls from
                    // track-group suppression into a single rebuild.
                    var suppressionTransaction = new FuseApplyTransaction("__merged-world-suppressions__", reason, false);
                    // A full graph rebuild and a collections refresh have
                    // already consumed all structural work above. Clear a stale
                    // request raised by object-disable callbacks before opening
                    // the suppression batch, otherwise it is misattributed to
                    // suppression and triggers a redundant second full rebuild.
                    if (!TrackAPI.IsBatching &&
                        TrackAPI.ConsumePendingRebuildRequest())
                    {
                        TrackAPI.RebuildCollectionsOnly();
                    }
                    TrackAPI.BeginBatch();
                    try
                    {
                        suppressionTransaction.RunPhase("apply-world-suppressions", () => FuseWorldSuppressor.ApplyAllActive("merged suppression apply", suppressionTransaction));
                    }
                    finally
                    {
                        // Close WITHOUT rebuilding: EndBatch(true) would run the
                        // deferred rebuild unguarded inside this finally, where a
                        // throw (TrackObjectManager fragility, GraphRebuilt
                        // subscribers) skips the per-package outcome/commit loop
                        // and rolls back every registry transaction while the
                        // runtime mutations stay applied. The rebuild runs below
                        // in its own guarded, timed phase instead.
                        TrackAPI.EndBatch(false);
                    }

                    // With no outer batch, consume the request the suppression
                    // pass deferred and rebuild inside RunPhase — a throw becomes
                    // a fatal on the synthetic report (logged below) and the load
                    // continues, matching the old per-package behavior where
                    // RebuildAfterTrackGroupSuppression caught and warned. If an
                    // outer batch IS open, leave the flag for its EndBatch — the
                    // pre-existing deferral semantics.
                    if (!TrackAPI.IsBatching && TrackAPI.ConsumePendingRebuildRequest())
                    {
                        suppressionTransaction.RunPhase("merged-suppression-graph-rebuild", () => TrackAPI.RebuildGraph());
                    }

                    suppressionTransaction.Report.LogSummary();
                    if (suppressionTransaction.Report.IsFatal || suppressionTransaction.Report.HasErrors)
                    {
                        var failure =
                            "FUSE merged world-suppression apply reported problems: " +
                            $"fatal={suppressionTransaction.Report.IsFatal} errors={suppressionTransaction.Report.Errors.Count} reason='{reason}'.";
                        FuseLog.Warning(failure);
                        FuseLoadReport.RecordNotice(failure);
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
                $"FUSE legacy staged graph resolver completed reason='{reason ?? "unspecified"}' " +
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

            // Legacy graph patches respect the mod/package as the unit, then
            // Definition.json mixinto order inside that package.
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
            var baseGraphSnapshot = TrackAPI.GetBaseGraphSnapshotDefinition();
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
                MergeFinalSegmentDefinitions(plan, tracks.Segments, candidate, baseGraphSnapshot, ref sequence);
                MergeFinalDefinitions(plan.Spans, plan.RemovedSpans, tracks.Spans, candidate, ref sequence);
            }

            FuseLog.Info(
                $"FUSE merged graph plan operation='build staged graph state' " +
                $"packages={ordered.Select(item => item.folder).Distinct(StringComparer.OrdinalIgnoreCase).Count()} " +
                $"definitions={ordered.Length} nodes={plan.Nodes.Count} segments={plan.Segments.Count} spans={plan.Spans.Count} turntables={plan.Turntables.Count} " +
                $"removedNodes={plan.RemovedNodes.Count} removedSegments={plan.RemovedSegments.Count} removedSpans={plan.RemovedSpans.Count} " +
                $"protectedRemovedSegments={plan.ProtectedRemovedSegments}.");
            if (plan.ProtectedRemovedSegments > 0)
            {
                FuseLog.Info(
                    "FUSE preserved explicit legacy track segment removals over later base-segment restatements " +
                    $"count={plan.ProtectedRemovedSegments} samples='{string.Join(", ", plan.ProtectedRemovedSegmentSamples.ToArray())}'.");
            }
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

        private static void MergeFinalSegmentDefinitions(
            FuseMergedTrackPlan plan,
            IDictionary<string, FuseSegment> values,
            FuseStagedApplyCandidate owner,
            FuseTrackDefinition baseGraphSnapshot,
            ref int sequence)
        {
            if (plan == null || values == null)
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
                if (plan.RemovedSegments.TryGetValue(id, out var removal) &&
                    ShouldKeepPriorSegmentRemoval(id, removal, pair.Value, owner, baseGraphSnapshot))
                {
                    plan.ProtectedRemovedSegments++;
                    if (plan.ProtectedRemovedSegmentSamples.Count < 12)
                    {
                        var ownerId = owner?.Loaded?.Definition?.Id ?? "<unknown>";
                        plan.ProtectedRemovedSegmentSamples.Add($"{id}<-{ownerId}");
                    }

                    continue;
                }

                plan.RemovedSegments.Remove(id);
                plan.Segments[id] = new FuseMergedTrackEntry<FuseSegment>(id, pair.Value, owner, sequence);
            }
        }

        private static bool ShouldKeepPriorSegmentRemoval(
            string id,
            FuseMergedTrackRemoval removal,
            FuseSegment laterSegment,
            FuseStagedApplyCandidate laterOwner,
            FuseTrackDefinition baseGraphSnapshot)
        {
            if (string.IsNullOrWhiteSpace(id) || removal == null || laterSegment == null)
            {
                return false;
            }

            if (!IsLegacyGameGraphDefinition(removal.Owner?.Loaded?.Definition) ||
                !IsLegacyGameGraphDefinition(laterOwner?.Loaded?.Definition))
            {
                return false;
            }

            if (baseGraphSnapshot?.Segments == null ||
                !baseGraphSnapshot.Segments.TryGetValue(id, out var baseSegment) ||
                baseSegment == null)
            {
                return false;
            }

            return SegmentTopologyMatches(baseSegment, laterSegment);
        }

        private static bool IsLegacyGameGraphDefinition(FuseModDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            var isLegacyConverted =
                definition.Tags?.Any(tag => string.Equals(tag, "legacy-converted", StringComparison.OrdinalIgnoreCase)) == true;
            if (!isLegacyConverted)
            {
                return false;
            }

            var target = definition.Mixinto?.Target;
            return string.IsNullOrWhiteSpace(target) ||
                   string.Equals(target, "game-graph", StringComparison.OrdinalIgnoreCase);
        }

        private static bool SegmentTopologyMatches(FuseSegment left, FuseSegment right)
        {
            var leftStart = NormalizeTrackId(left?.StartNodeId);
            var leftEnd = NormalizeTrackId(left?.EndNodeId);
            var rightStart = NormalizeTrackId(right?.StartNodeId);
            var rightEnd = NormalizeTrackId(right?.EndNodeId);
            if (string.IsNullOrWhiteSpace(leftStart) ||
                string.IsNullOrWhiteSpace(leftEnd) ||
                string.IsNullOrWhiteSpace(rightStart) ||
                string.IsNullOrWhiteSpace(rightEnd))
            {
                return false;
            }

            return (string.Equals(leftStart, rightStart, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(leftEnd, rightEnd, StringComparison.OrdinalIgnoreCase)) ||
                   (string.Equals(leftStart, rightEnd, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(leftEnd, rightStart, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeTrackId(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        /// <summary>
        /// Builds a final-state removal plan across all active candidates, in
        /// the same package/file order as <see cref="BuildMergedTrackPlan"/>.
        /// Each kind has its own "remove first, then cancel by adds" pass
        /// per package so that:
        ///   1. Within a single package, "remove X then add X" leaves X added
        ///      (matches the legacy SC pattern for replacing base game items).
        ///   2. Across packages, a later add of an id cancels an earlier
        ///      package's removal of that id (latest-write-wins per id).
        /// The returned plan is applied at end-of-batch by
        /// <see cref="ApplyMergedRemovalPlan"/>, after all per-package adds.
        /// </summary>
        private static FuseMergedRemovalPlan BuildMergedRemovalPlan(IReadOnlyList<FuseStagedApplyCandidate> active)
        {
            var plan = new FuseMergedRemovalPlan();
            if (active == null || active.Count == 0)
            {
                return plan;
            }

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

                // Removals first so an in-package add of the same id can clear
                // the removal (mirrors MergeFinalDeletes -> MergeFinalDefinitions
                // ordering in BuildMergedTrackPlan).
                var worldRemovals = definition.World?.Removals;
                if (worldRemovals != null)
                {
                    MergeRemovalIds(plan.Scenery, worldRemovals.Scenery, candidate, ref sequence);
                    MergeRemovalIds(plan.SceneClones, worldRemovals.SceneClones, candidate, ref sequence);
                    MergeRemovalIds(plan.Splineys, worldRemovals.Splineys, candidate, ref sequence);
                    MergeRemovalIds(plan.MapLabels, worldRemovals.MapLabels, candidate, ref sequence);
                    MergeRemovalIds(plan.MapMasks, worldRemovals.MapMasks, candidate, ref sequence);
                    MergeRemovalIds(plan.TelegraphPoles, worldRemovals.TelegraphPoles, candidate, ref sequence);
                }

                var opsRemovals = definition.Operations?.Removals;
                if (opsRemovals != null)
                {
                    MergeRemovalIds(plan.Industries, opsRemovals.Industries, candidate, ref sequence);
                }

                // Adds clear matching removals: a later add of an id cancels
                // any earlier package's removal of that id, and an in-package
                // add cancels an in-package remove.
                var world = definition.World;
                if (world != null)
                {
                    CancelRemovalsByAdds(plan.Scenery, world.Scenery?.Keys, ref sequence);
                    CancelRemovalsByAdds(plan.SceneClones, world.SceneClones?.Keys, ref sequence);
                    CancelRemovalsByAdds(plan.Splineys, world.Splineys?.Keys, ref sequence);
                    CancelRemovalsByAdds(plan.MapLabels, world.MapLabels?.Keys, ref sequence);
                    CancelRemovalsByAdds(plan.MapMasks, world.MapMasks?.Keys, ref sequence);
                    CancelRemovalsByAdds(plan.TelegraphPoles, world.TelegraphPoles?.Keys, ref sequence);
                }

                var ops = definition.Operations;
                if (ops != null)
                {
                    CancelRemovalsByAdds(plan.Industries, ops.Industries?.Keys, ref sequence);
                }
            }

            FuseLog.Info(
                $"FUSE merged removal plan operation='build merged removal plan' " +
                $"packages={ordered.Select(item => item.folder).Distinct(StringComparer.OrdinalIgnoreCase).Count()} " +
                $"definitions={ordered.Length} " +
                $"scenery={plan.Scenery.Count} sceneClones={plan.SceneClones.Count} splineys={plan.Splineys.Count} " +
                $"mapLabels={plan.MapLabels.Count} mapMasks={plan.MapMasks.Count} telegraphPoles={plan.TelegraphPoles.Count} " +
                $"industries={plan.Industries.Count}.");
            return plan;
        }

        private static void MergeRemovalIds(
            Dictionary<string, FuseMergedTrackRemoval> removals,
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
                removals[id] = new FuseMergedTrackRemoval(id, owner, sequence);
            }
        }

        private static void CancelRemovalsByAdds(
            Dictionary<string, FuseMergedTrackRemoval> removals,
            IEnumerable<string> addIds,
            ref int sequence)
        {
            if (addIds == null)
            {
                return;
            }

            foreach (var rawId in addIds)
            {
                if (string.IsNullOrWhiteSpace(rawId))
                {
                    continue;
                }

                var id = rawId.Trim();
                sequence++;
                removals.Remove(id);
            }
        }

        /// <summary>
        /// Applies the cross-package final-state removal plan built by
        /// <see cref="BuildMergedRemovalPlan"/>. Each kind is drained in
        /// sequence order; each removal reports back to its owning candidate's
        /// transaction so the per-package apply report still attributes the
        /// remove (or its "missing" skip) to the package that asked for it.
        /// </summary>
        private static void ApplyMergedRemovalPlan(FuseMergedRemovalPlan plan)
        {
            if (plan == null || !plan.HasAny)
            {
                return;
            }

            ApplyMergedRemovalsForKind(plan.Scenery, "scenery", SceneryAPI.TryRemoveScenery);
            ApplyMergedRemovalsForKind(plan.SceneClones, "scene clone", SceneCloneAPI.TryRemoveSceneClone);
            ApplyMergedRemovalsForKind(plan.Splineys, "spliney", SplineyAPI.TryRemoveSpliney);
            ApplyMergedRemovalsForKind(plan.MapLabels, "map label", MapAPI.TryRemoveMapLabel);
            ApplyMergedRemovalsForKind(plan.MapMasks, "map mask", MapAPI.TryRemoveMapMask);
            ApplyMergedRemovalsForKind(plan.TelegraphPoles, "telegraph pole set", MapAPI.TryRemoveTelegraphPoles);
            ApplyMergedRemovalsForKind(plan.Industries, "industry", IndustryAPI.TryRemoveIndustry);
        }

        private static void ApplyMergedRemovalsForKind(
            Dictionary<string, FuseMergedTrackRemoval> removals,
            string kind,
            Func<string, bool> remover)
        {
            if (removals == null || removals.Count == 0)
            {
                return;
            }

            foreach (var removal in removals.Values.OrderBy(item => item.Sequence))
            {
                var transaction = removal.Owner?.Transaction;
                if (transaction == null)
                {
                    continue;
                }

                try
                {
                    if (remover(removal.Id))
                    {
                        transaction.Removed(kind, removal.Id);
                    }
                    else
                    {
                        transaction.Skipped(kind, removal.Id, "missing");
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE failed to remove world {kind} '{removal.Id}'", ex);
                    transaction.Error(kind, removal.Id, ex.Message);
                }
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

                // Opened after the span removals; the lazily-built index sees the
                // same scene state the per-removal live scan would — including
                // spans removed above, which are still pending deferred Destroy
                // and appear in both (parity with the old per-segment scan, not
                // exclusion). Segment and node removals below share one scene
                // scan instead of one per removal.
                FuseTrackRemovalSnapshotStore.BeginCaptureBatch();
                try
                {
                    foreach (var removal in plan.RemovedSegments.Values.OrderBy(item => item.Sequence))
                    {
                        ApplyMergedSegmentRemoval(removal);
                    }

                    foreach (var removal in plan.RemovedNodes.Values.OrderBy(item => item.Sequence))
                    {
                        ApplyMergedNodeRemoval(removal);
                    }
                }
                finally
                {
                    FuseTrackRemovalSnapshotStore.EndCaptureBatch();
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

            var trackAssemblySummary = ApplyMergedTrackAssemblyPackages(plan, reason);
            var shouldRebuildBeforeFinalSpans =
                plan.HasStructuralChanges
                || trackAssemblySummary.NativeGraphMutationsApplied
                || trackAssemblySummary.HasFailures;

            if (shouldRebuildBeforeFinalSpans)
            {
                var graphTransaction = new FuseApplyTransaction("__merged-graph-rebuild__", reason, false);
                graphTransaction.RunPhase(
                    "merged-single-graph-rebuild",
                    () => TrackAPI.RebuildGraphWithFreshCurves(
                        "staged graph apply after track-graph subscribers"));
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
                    "no final structural track mutations in active definitions or TrackAssembly source packages.");
            }

            // Spans do not affect TrackObjectManager's rail/switch/bumper
            // descriptors. Apply and scrub them in one batch, then refresh only
            // Graph's collections if cleanup requested it. Rebuilding every track
            // mesh/culling descriptor here was redundant and also re-ran every
            // TrackGraphApplying companion subscriber.
            TrackAPI.BeginBatch();
            try
            {
                foreach (var entry in plan.Spans.Values.OrderBy(item => item.Sequence))
                {
                    ApplyMergedSpan(entry);
                }

                TrackAPI.RemoveInvalidTrackSpans("staged graph apply after final spans");
                TrackAPI.ScrubCtcSignalReferences("staged graph apply after final spans");
                IndustryAPI.ScrubIndustryComponentCaches("staged graph apply after final spans");
            }
            finally
            {
                TrackAPI.EndBatch(false);
            }

            if (!TrackAPI.IsBatching &&
                TrackAPI.ConsumePendingRebuildRequest())
            {
                TrackAPI.RebuildCollectionsOnly();
            }

            ReconcileMissingMergedSpans(plan);
        }

        /// <summary>
        /// A structural graph rebuild can retire a base span whose endpoint
        /// segments were removed, while the final lightweight collection refresh
        /// can still omit the replacement span created under the same id. This is
        /// observable with ARC Whittier's Pyc3/Pap9 replacements: their new
        /// definitions point at new single segments, but the old base spans point
        /// at the deleted four-segment topology. Reconcile only spans whose final
        /// plan owner still owns the exclusive registry claim, so this recovery
        /// cannot override a conflict winner or another package's runtime span.
        /// </summary>
        private static void ReconcileMissingMergedSpans(FuseMergedTrackPlan plan)
        {
            if (plan?.Spans == null || plan.Spans.Count == 0)
            {
                return;
            }

            var restored = 0;
            TrackAPI.BeginBatch();
            try
            {
                foreach (var entry in plan.Spans.Values.OrderBy(item => item.Sequence))
                {
                    var packageId = entry.Owner?.Loaded?.Definition?.Id;
                    if (!ShouldReconcileMergedSpan(
                            packageId,
                            FuseRegistry.GetExclusiveOwner(FuseClaimKind.Span, entry.Id),
                            TrackAPI.GetSpan(entry.Id) != null))
                    {
                        continue;
                    }

                    try
                    {
                        TrackAPI.AddSpan(entry.Id, entry.Value);
                        entry.Owner.Transaction.PostBind(
                            "track span",
                            entry.Id,
                            "restored after final graph collection refresh");
                        restored++;
                    }
                    catch (Exception ex)
                    {
                        entry.Owner.Transaction.Error("track span", entry.Id, ex.Message);
                        FuseLog.Exception(
                            $"FUSE final span reconciliation failed package='{packageId ?? string.Empty}' " +
                            $"kind='track span' id='{entry.Id}'",
                            ex);
                    }
                }
            }
            finally
            {
                TrackAPI.EndBatch(false);
            }

            if (restored == 0)
            {
                return;
            }

            FuseLog.Info(
                $"FUSE restored {restored} merged track span(s) that were missing after the final graph collection refresh.");
            if (!TrackAPI.IsBatching && TrackAPI.ConsumePendingRebuildRequest())
            {
                TrackAPI.RebuildCollectionsOnly();
            }
        }

        internal static bool ShouldReconcileMergedSpan(
            string plannedOwner,
            string registryOwner,
            bool spanPresent)
        {
            return !spanPresent &&
                   !string.IsNullOrWhiteSpace(plannedOwner) &&
                   string.Equals(plannedOwner, registryOwner, StringComparison.OrdinalIgnoreCase);
        }

        private static FuseTrackAssemblyBridgeApplySummary ApplyMergedTrackAssemblyPackages(
            FuseMergedTrackPlan plan,
            string reason)
        {
            if (plan == null)
            {
                return FuseTrackAssemblyBridgeApplySummary.Empty;
            }

            var packageIds = plan.OrderedCandidates
                .Where(item => item?.Transaction != null && !item.Transaction.Report.IsFatal)
                .Select(item => item.Loaded?.Definition?.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();
            var summary = FuseTrackAssemblyBridgeHost.ApplyLoadedPackages(packageIds, reason);
            if (!summary.HasFailures)
            {
                return summary;
            }

            foreach (var failure in summary.Failures)
            {
                var candidate = plan.OrderedCandidates.FirstOrDefault(item =>
                    item?.Transaction != null
                    && !item.Transaction.Report.IsFatal
                    && string.Equals(
                        item.Loaded?.Definition?.Id,
                        failure.Key,
                        StringComparison.OrdinalIgnoreCase));
                candidate?.Transaction.RunPhase(
                    "staged-apply-trackassembly-source",
                    () => candidate.Transaction.Fatal("trackAssembly source", failure.Key, failure.Value));
            }

            return summary;
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
            var segmentValue = ResolveSegmentForApply(entry.Id, entry.Value, runtimeSegment, definition.Id, "apply-merged-segments");
            if (segmentValue == null)
            {
                transaction.Skipped("track segment", entry.Id, "definition missing");
                return;
            }

            var startNode = TrackAPI.GetNode(segmentValue.StartNodeId);
            var endNode = TrackAPI.GetNode(segmentValue.EndNodeId);
            if (startNode == null || endNode == null)
            {
                var missingEndpoints = new List<string>();
                if (startNode == null)
                {
                    missingEndpoints.Add($"start='{segmentValue.StartNodeId ?? string.Empty}'");
                }

                if (endNode == null)
                {
                    missingEndpoints.Add($"end='{segmentValue.EndNodeId ?? string.Empty}'");
                }

                FuseLog.Warning(
                    $"FUSE skipped track segment package='{definition.Id}' operation='apply-merged-segments' " +
                    $"kind='track segment' id='{entry.Id}' " +
                    $"start='{segmentValue.StartNodeId ?? string.Empty}' startExists={startNode != null} " +
                    $"end='{segmentValue.EndNodeId ?? string.Empty}' endExists={endNode != null} " +
                    "message='node reference not bound in merged final graph; segment skipped instead of faulting package'.");
                transaction.Skipped("track segment", entry.Id, $"missing endpoint(s): {string.Join(", ", missingEndpoints)}");
                return;
            }

            transaction.TryApply("track segment", entry.Id, exists, () =>
            {
                if (runtimeSegment == null)
                {
                    TrackAPI.AddSegment(entry.Id, segmentValue);
                }
                else if (runtimeSegment.a.id != segmentValue.StartNodeId || runtimeSegment.b.id != segmentValue.EndNodeId)
                {
                    FuseLog.Info(
                        $"FUSE apply package='{definition.Id}' operation='apply-merged-segments' " +
                        $"kind='track segment' id='{entry.Id}' " +
                        $"message='endpoint change detected " +
                        $"oldStart=\"{runtimeSegment.a.id}\" newStart=\"{segmentValue.StartNodeId}\" " +
                        $"oldEnd=\"{runtimeSegment.b.id}\" newEnd=\"{segmentValue.EndNodeId}\" " +
                        $"newStartExists={startNode != null} newEndExists={endNode != null}'.");

                    TrackAPI.RemoveSegment(entry.Id);
                    TrackAPI.AddSegment(entry.Id, segmentValue);
                }
                else
                {
                    TrackAPI.UpdateSegment(entry.Id, segmentValue);
                }
            });
        }

        private static FuseSegment ResolveSegmentForApply(
            string id,
            FuseSegment definition,
            TrackSegment runtimeSegment,
            string packageId,
            string operation)
        {
            if (definition == null)
            {
                return null;
            }

            var hasStartNode = !string.IsNullOrWhiteSpace(definition.StartNodeId);
            var hasEndNode = !string.IsNullOrWhiteSpace(definition.EndNodeId);
            if (!definition.Partial && hasStartNode && hasEndNode)
            {
                return definition;
            }

            if (runtimeSegment == null)
            {
                if (!hasStartNode || !hasEndNode)
                {
                    FuseLog.Warning(
                        $"FUSE legacy segment patch package='{packageId ?? string.Empty}' operation='{operation ?? string.Empty}' " +
                        $"id='{id ?? string.Empty}' start='{definition.StartNodeId ?? string.Empty}' end='{definition.EndNodeId ?? string.Empty}' " +
                        "message='cannot hydrate missing endpoint because the target segment is not present in the runtime graph'.");
                }

                return definition;
            }

            var current = TrackAPI.GetDefinition(runtimeSegment) ?? new FuseSegment();
            var result = new FuseSegment
            {
                StartNodeId = hasStartNode ? definition.StartNodeId : current.StartNodeId,
                EndNodeId = hasEndNode ? definition.EndNodeId : current.EndNodeId,
                Style = definition.PreserveStyle ? current.Style : definition.Style,
                TrackClass = definition.PreserveTrackClass ? current.TrackClass : definition.TrackClass,
                SpeedLimit = definition.PreserveSpeedLimit ? current.SpeedLimit : definition.SpeedLimit,
                Priority = definition.PreservePriority ? current.Priority : definition.Priority,
                GroupId = definition.PreserveGroupId ? current.GroupId : definition.GroupId,
                Tags = definition.Tags ?? current.Tags,
                Gauge = string.IsNullOrWhiteSpace(definition.Gauge) ? current.Gauge : definition.Gauge
            };

            if (!hasStartNode || !hasEndNode)
            {
                FuseLog.Info(
                    $"FUSE legacy segment patch hydrated package='{packageId ?? string.Empty}' operation='{operation ?? string.Empty}' " +
                    $"id='{id ?? string.Empty}' start='{result.StartNodeId ?? string.Empty}' end='{result.EndNodeId ?? string.Empty}'.");
            }

            return result;
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
    }
}
