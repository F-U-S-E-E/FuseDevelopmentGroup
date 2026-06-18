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
        private static void PreEnableInitialTrackGroups(string[] orderedIds)
        {
            if (orderedIds == null || orderedIds.Length == 0)
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
            var availabilityChangedCount = 0;
            foreach (var groupId in groupsToEnable)
            {
                try
                {
                    if (graph.SetGroupEnabled(groupId, true))
                    {
                        changedCount++;
                    }
                    // ALSO flip available=true. The merged graph rebuild that
                    // runs after this method builds the visible track mesh,
                    // and mesh visibility uses Graph.availableGroupIds — not
                    // enabledGroupIds. If we leave available=false here, the
                    // mesh build skips the segments outright, and any later
                    // SetGroupAvailable(groupId, true) (e.g. by the orphan
                    // finalise tail of RefreshRuntimeStateAfterApply) cannot
                    // reconstruct mesh entries that were never created.
                    // This was the CollieDillsboroOverhaul e-c1 regression:
                    // segments bound to the graph because enabled=true, but
                    // never appeared because available=false at build time.
                    // The per-feature refresh later in RefreshRuntimeStateAfterApply
                    // owns SetGroupAvailable for feature-claimed groups, so
                    // locked features will still hide their track correctly.
                    if (graph.SetGroupAvailable(groupId, true))
                    {
                        availabilityChangedCount++;
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

            // Hand the segment-referenced groups to ProgressionAPI so the
            // refresh tail can revoke any of them that no MapFeature ends up
            // claiming. We deliberately record EVERY segment-derived groupId,
            // not just the ones whose SetGroupEnabled flipped — base-scene
            // pre-baked enabledGroupIds entries (e.g. the MaconCounty mod's
            // s3a base-map siding tracks at Alarka Jct, which the base scene
            // already has in enabledGroupIds so PreEnable returns changed=
            // false) would otherwise stay enabled with no progression
            // control and render permanently. Groups also referenced by a
            // MapFeature's tracksEnable / tracksAvail are NOT revoked; the
            // game's HandleFeatureEnablesChanged still owns those.
            foreach (var groupId in groupsFromSegments)
            {
                FUSE.Runtime.API.ProgressionAPI.RecordTransientlyPreEnabledTrackGroup(groupId);
            }

            FuseLog.Info(
                $"FUSE pre-enabled {enabledCount} track group(s) " +
                $"changed={changedCount} availabilityChanged={availabilityChangedCount} " +
                $"({groupsFromSegments.Count} from segment groupIds) " +
                "before apply-segments to prevent graph-rebuild culling and mesh-build skipping; " +
                "progression refresh will restore gated state after apply.");
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
                    AddNonEmpty(sink, feature.TrackGroupsEnableOnUnlock?.EffectiveAdditions);
                    AddNonEmpty(sink, feature.GroupIds?.EffectiveAdditions);
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
                        AddNonEmpty(sink, section.TrackGroupsEnableOnUnlock?.EffectiveAdditions);
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
                            AddNonEmpty(sink, section.TrackGroupsEnableOnUnlock?.EffectiveAdditions);
                        }
                    }
                }
            }
        }

        private static bool HasNoPrerequisites(FuseSection section)
        {
            // "Has no prerequisites" here means "the authored patch does not
            // add any prereq ids." If the patch removes ids (false-valued
            // entries) we still count it as "no prereqs added" — the goal
            // is to decide whether this section's TrackGroupsEnableOnUnlock
            // should be considered an initially-enabled track group.
            return PatchHasNoAdditions(section.PrerequisiteSections)
                && PatchHasNoAdditions(section.PrerequisiteSectionIds);
        }

        private static bool PatchHasNoAdditions(FuseStringPatch patch)
        {
            if (patch == null || !patch.HasValue) return true;
            var additions = patch.EffectiveAdditions;
            return additions == null || additions.Length == 0;
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

        private static void ApplyGlobalLoadCatalog(string[] orderedIds, string reason)
        {
            if (orderedIds == null || orderedIds.Length == 0)
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

        private static void ApplyDeferredOperationBindings(string[] orderedIds, string reason)
        {
            if (orderedIds == null || orderedIds.Length == 0)
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
                    var segmentValue = ResolveSegmentForApply(segment.Key, segment.Value, runtimeSegment, definition.Id, "apply-segments");
                    if (segmentValue == null)
                    {
                        transaction.Skipped("track segment", segment.Key, "definition missing");
                        continue;
                    }

                    // Pre-check: surface node-binding problems before AddSegment
                    // throws. The preflight-warning downgrade lets segments with
                    // missing endpoints reach apply, where they fail per-segment.
                    // Emitting this warning makes the cause visible without
                    // adding any per-success log noise.
                    var startNode = TrackAPI.GetNode(segmentValue.StartNodeId);
                    var endNode = TrackAPI.GetNode(segmentValue.EndNodeId);
                    if (startNode == null || endNode == null)
                    {
                        FuseLog.Warning(
                            $"FUSE apply pre-check package='{definition.Id}' operation='apply-segments' " +
                            $"kind='track segment' id='{segment.Key}' " +
                            $"start='{segmentValue.StartNodeId ?? string.Empty}' startExists={startNode != null} " +
                            $"end='{segmentValue.EndNodeId ?? string.Empty}' endExists={endNode != null} " +
                            "message='node reference not yet bound; AddSegment may fail per-segment'.");
                    }

                    transaction.TryApply("track segment", segment.Key, exists, () =>
                    {
                        if (runtimeSegment == null)
                        {
                            TrackAPI.AddSegment(segment.Key, segmentValue);
                        }
                        else if (runtimeSegment.a.id != segmentValue.StartNodeId || runtimeSegment.b.id != segmentValue.EndNodeId)
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
                                $"oldStart=\"{runtimeSegment.a.id}\" newStart=\"{segmentValue.StartNodeId}\" " +
                                $"oldEnd=\"{runtimeSegment.b.id}\" newEnd=\"{segmentValue.EndNodeId}\" " +
                                $"newStartExists={startNode != null} newEndExists={endNode != null}'.");

                            TrackAPI.RemoveSegment(segment.Key);
                            TrackAPI.AddSegment(segment.Key, segmentValue);
                        }
                        else
                        {
                            TrackAPI.UpdateSegment(segment.Key, segmentValue);
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

                    var industryDefinition = industry.Value;
                    var exists = IndustryAPI.GetIndustry(industry.Key) != null;
                    // Diagnostic: surface the actual deserialized flag values so we
                    // can match converter intent against what the apply path sees.
                    // Logged unconditionally for any industry the converter touched
                    // so we can correlate $replace + replaceComponents in the log.
                    if (industryDefinition != null)
                    {
                        FuseLog.Info(
                            $"FUSE industry apply-time flags id='{industry.Key}' package='{definition.Id}' exists={exists} " +
                            $"mergeComponents={industryDefinition.MergeComponents} " +
                            $"replaceComponents={industryDefinition.ReplaceComponents} " +
                            $"componentDictCount={(industryDefinition.Components?.Count ?? 0)} " +
                            $"componentKeys=[{(industryDefinition.Components == null ? "" : string.Join(",", industryDefinition.Components.Keys))}].");
                    }

                    if (exists && industryDefinition != null && !industryDefinition.ReplaceComponents)
                    {
                        // The industry already exists from a prior package's apply; treat this
                        // apply as a mixinto-style patch and force MergeComponents so the
                        // existing components are preserved instead of wiped by Update.
                        //
                        // Skipped when the definition was authored with the legacy
                        // <c>"components": { "$replace": { ... } }</c> directive — in that
                        // case the mod explicitly opted out of merge semantics and wants
                        // <see cref="IndustryAPI.AddOrUpdateComponents"/> to run
                        // <c>RemoveStaleComponents</c> so anything not in the new dictionary
                        // (e.g. the vanilla repair track on Foxy's wh-e-engine after the
                        // mod renames it to "East Whittier Fuel Service") is dropped.
                        industryDefinition.MergeComponents = true;
                    }
                    if (transaction.TryApply("industry", industry.Key, exists, () =>
                    {
                        if (exists)
                        {
                            IndustryAPI.UpdateIndustry(industry.Key, industryDefinition, false);
                        }
                        else
                        {
                            IndustryAPI.AddIndustry(industry.Key, industryDefinition, false);
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
                        if (SceneryAPI.IsKnownOptionalLegacyAssetReference(scenery.Value))
                        {
                            FuseLog.Info(
                                $"FUSE skipped optional legacy scenery '{scenery.Key}' in package '{definition.Id}' " +
                                $"because asset '{scenery.Value?.AssetIdentifier ?? scenery.Value?.Model ?? string.Empty}' is not installed.");
                            transaction.Skipped("scenery", scenery.Key, "optional legacy asset unavailable");
                            continue;
                        }

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

            // Scene clones (mandelas) are applied in their own later phase (apply-scene-clones)
            // so that industries, areas, and progression have already settled. Applying them
            // here, before apply-operations, lets an industry whose display name collides with a
            // disabled scene clone path (e.g. a renamed Stenzel Mfg industry under the matching
            // scenery branch) re-activate the GameObject the mandela just disabled.
        }

        private static void ApplySceneClones(FuseModDefinition definition, string definitionPath, FuseApplyTransaction transaction)
        {
            if (definition?.World?.SceneClones == null)
            {
                return;
            }

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

        private static void ApplyOperationsRemovals(FuseModDefinition definition, FuseApplyTransaction transaction)
        {
            var removals = definition?.Operations?.Removals;
            if (removals == null)
            {
                return;
            }

            RemoveWorldItems("industry", removals.Industries, IndustryAPI.TryRemoveIndustry, transaction);
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
                    FuseLog.Exception($"FUSE failed to remove world {kind} '{id}'", exception);
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
    }
}
