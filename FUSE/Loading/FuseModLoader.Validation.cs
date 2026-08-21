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

        private static void ValidateNodeReference(string segmentId, string field, string nodeId, HashSet<string> definedNodes, HashSet<string> generatedNodes, FuseApplyTransaction transaction)
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

        private static void ValidateSegmentReference(string spanId, string field, string segmentId, HashSet<string> definedSegments, HashSet<string> generatedSegments, FuseApplyTransaction transaction)
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

        private static void ValidateOptionalSpanReferences(string componentId, IEnumerable<string> spanIds, HashSet<string> definedSpans, FuseApplyTransaction transaction)
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

        private static void ValidateOptionalLoadReference(string componentId, string loadId, HashSet<string> definedLoads, FuseApplyTransaction transaction)
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

                    var missingReason = DescribeMissingSegmentAfterApply(segmentDefinition, mergedTrackPlan);
                    FuseLoadReport.RecordGraphPostBindIssue(definition.Id, "track segment", segmentId, missingReason);
                    transaction.Warning("track segment", segmentId, missingReason);
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

                    var missingReason = DescribeMissingSpanAfterApply(spanEntry.Value, mergedTrackPlan);
                    FuseLoadReport.RecordGraphPostBindIssue(definition.Id, "track span", spanId, missingReason);
                    transaction.Warning("track span", spanId, missingReason);
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

            foreach (var waterSurfaceId in definition.World?.WaterSurfaces?.Keys ?? Enumerable.Empty<string>())
            {
                if (WaterSurfaceAPI.GetWaterSurface(waterSurfaceId) == null)
                    transaction.Warning("water surface", waterSurfaceId, "missing after apply");
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

        private static string DescribeMissingSegmentAfterApply(
            FuseSegment segment,
            FuseMergedTrackPlan mergedTrackPlan)
        {
            if (segment == null || mergedTrackPlan == null)
            {
                return "missing after apply";
            }

            var causes = new List<string>();
            AddRemovalCause(causes, "endpoint node", segment.StartNodeId, mergedTrackPlan.RemovedNodes);
            AddRemovalCause(causes, "endpoint node", segment.EndNodeId, mergedTrackPlan.RemovedNodes);
            return causes.Count == 0
                ? "missing after apply"
                : "missing after apply; " + string.Join("; ", causes);
        }

        private static string DescribeMissingSpanAfterApply(
            FuseSpan span,
            FuseMergedTrackPlan mergedTrackPlan)
        {
            if (span == null || mergedTrackPlan == null)
            {
                return "missing after apply";
            }

            var causes = new List<string>();
            AddRemovalCause(causes, "endpoint segment", span.Upper?.SegmentId, mergedTrackPlan.RemovedSegments);
            AddRemovalCause(causes, "endpoint segment", span.Lower?.SegmentId, mergedTrackPlan.RemovedSegments);
            return causes.Count == 0
                ? "missing after apply"
                : "missing after apply; " + string.Join("; ", causes);
        }

        private static void AddRemovalCause(
            List<string> causes,
            string kind,
            string id,
            IReadOnlyDictionary<string, FuseMergedTrackRemoval> removals)
        {
            if (causes == null || removals == null || string.IsNullOrWhiteSpace(id) ||
                !removals.TryGetValue(id, out var removal))
            {
                return;
            }

            var removingPackage = removal.Owner?.Loaded?.Definition?.Id;
            var cause = string.IsNullOrWhiteSpace(removingPackage)
                ? $"{kind} '{id}' was removed by another package"
                : $"{kind} '{id}' was removed by '{removingPackage}'";
            if (!causes.Contains(cause))
            {
                causes.Add(cause);
            }
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
