using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Model.Ops;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Authoring.Data.Common;
using FUSE.Runtime.Events;
using FUSE.Infrastructure;
using Track;
using Track.Signals;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static partial class TrackAPI
    {

        public static TrackSpan AddSpan(string id, FuseTrackLocation upper, FuseTrackLocation lower, bool normalize = true)
        {
            RequireId(id, nameof(id));
            var graph = RequireGraph();
            if (graph.SpanForId(id) != null)
            {
                throw new InvalidOperationException($"Track span '{id}' already exists.");
            }

            var upperLocation = MakeLocation(graph, upper);
            var lowerLocation = MakeLocation(graph, lower);
            ValidateSpanEndpointPair(id, ref upperLocation, ref lowerLocation);

            var span = CreateGraphChild<TrackSpan>(graph, "Span-" + id);
            try
            {
                span.id = id;
                span.upper = upperLocation;
                span.lower = lowerLocation;
                if (normalize)
                {
                    span.NormalizeUpperLower();
                }

                ValidateSpanRoute(id, span);
            }
            catch
            {
                // Unity destruction is deferred. Do not leave a failed temporary
                // object discoverable under the requested id for the remainder
                // of this frame, or a later retry can be mistaken for a duplicate.
                span.id = CreateRetiredSpanId(id);
                RemoveRuntimeObject(span);
                throw;
            }

            FuseSpanRuntimeIndex.Instance.Set(id, span);
            RegisterSpanWithGraph(graph, span);
            FuseEvents.RaiseSpanAdded(span);
            RequestRebuild();
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackSpan, id, GetDefinition(span));
            return span;
        }

        public static TrackSpan AddSpan(string id, FuseSpan definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var span = AddSpan(id, definition.Upper, definition.Lower, definition.Normalize);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackSpan, id, GetDefinition(span));
            return span;
        }

        public static void UpdateSpan(string id, FuseTrackLocation upper, FuseTrackLocation lower, bool normalize = true)
        {
            var span = RequireSpan(id);
            var graph = RequireGraph();
            //EnsureTrackSpanGraphChild(graph, span);
            var upperLocation = MakeLocation(graph, upper);
            var lowerLocation = MakeLocation(graph, lower);
            ValidateSpanEndpointPair(id, ref upperLocation, ref lowerLocation);

            var originalUpper = span.upper;
            var originalLower = span.lower;
            try
            {
                span.upper = upperLocation;
                span.lower = lowerLocation;
                if (normalize)
                {
                    span.NormalizeUpperLower();
                }

                ValidateSpanRoute(id, span);
            }
            catch
            {
                span.upper = originalUpper;
                span.lower = originalLower;
                throw;
            }

            FuseSpanRuntimeIndex.Instance.Set(id, span);
            RegisterSpanWithGraph(graph, span);
            FuseEvents.RaiseSpanUpdated(span);
            RequestRebuild();
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackSpan, id, GetDefinition(span));
        }

        public static void UpdateSpan(string id, FuseSpan definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            UpdateSpan(id, definition.Upper, definition.Lower, definition.Normalize);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackSpan, id, GetSpanDefinition(id));
        }

        public static void RemoveSpan(string id)
        {
            var span = RequireSpan(id);
            UnregisterSpanWithGraph(Graph.Shared, id);
            // Retire the identity before scheduling Unity destruction. A package
            // can legitimately recreate this id in the same apply pass.
            span.id = CreateRetiredSpanId(id);
            RemoveRuntimeObject(span);
            FuseSpanRuntimeIndex.Instance.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.TrackSpan, id);
            FuseEvents.RaiseSpanRemoved(id);
            RequestRebuild();
        }

        /// <summary>
        /// Recreates a span whose endpoint topology changed without exposing two
        /// live scene objects under the same identifier. Existing industry and
        /// interchange components are rebound to the replacement instance before
        /// Unity retires the old component at the end of the frame.
        /// </summary>
        public static TrackSpan ReplaceSpan(string id, FuseSpan definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var graph = RequireGraph();
            var previousSpan = RequireSpan(id);
            UnregisterSpanWithGraph(graph, id);
            FuseSpanRuntimeIndex.Instance.Remove(id);
            previousSpan.id = CreateRetiredSpanId(id);

            TrackSpan replacementSpan;
            try
            {
                replacementSpan = AddSpan(id, definition);
            }
            catch
            {
                // The previous component has not been destroyed yet, so a failed
                // replacement can be rolled back without losing the working span.
                previousSpan.id = id;
                FuseSpanRuntimeIndex.Instance.Set(id, previousSpan);
                RegisterSpanWithGraph(graph, previousSpan);
                throw;
            }

            IndustryAPI.ReplaceTrackSpanReferences(previousSpan, replacementSpan, "TrackAPI.ReplaceSpan");
            RemoveRuntimeObject(previousSpan);
            FuseLog.Info($"FUSE atomically replaced track span id='{id}' and rebound its runtime consumers.");
            return replacementSpan;
        }

        public static TrackSpan GetSpan(string id)
        {
            if (FuseSpanRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                var cachedSpan = (TrackSpan)cached;
                if (cachedSpan != null)
                {
                    return cachedSpan;
                }

                FuseSpanRuntimeIndex.Instance.Remove(id);
            }

            var graph = Graph.Shared;
            if (graph != null && !string.IsNullOrWhiteSpace(id))
            {
                var graphSpan = graph.SpanForId(id);
                if (graphSpan != null)
                {
                    FuseSpanRuntimeIndex.Instance.Set(id, graphSpan);
                    return graphSpan;
                }
            }

            var sceneSpan = FindSpanInScene(id);
            if (sceneSpan != null)
            {
                FuseSpanRuntimeIndex.Instance.Set(id, sceneSpan);
                if (graph != null)
                {
                    RegisterSpanWithGraph(graph, sceneSpan);
                }
            }

            return sceneSpan;
        }

        public static IEnumerable<TrackSpan> GetAllSpans()
        {
            return UnityEngine.Object.FindObjectsOfType<TrackSpan>(true)
                .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id));
        }

        /// <summary>
        /// Returns only spans owned by the live graph dictionary. Scene-wide
        /// searches also find prefab/template components and objects waiting for
        /// Unity's deferred destruction; those are useful to compatibility code
        /// but are not broken runtime track and must not become audit findings.
        /// </summary>
        internal static IEnumerable<TrackSpan> GetRegisteredSpansForDiagnostics()
        {
            var graph = Graph.Shared;
            var spans = graph != null
                ? GraphSpansField?.GetValue(graph) as Dictionary<string, TrackSpan>
                : null;
            if (spans != null)
            {
                return spans.Values
                    .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id))
                    .Distinct()
                    .ToArray();
            }

            return GetAllSpans()
                .Where(span => graph != null && ReferenceEquals(graph.SpanForId(span.id), span))
                .ToArray();
        }

        private static TrackSpan[] GetRegisteredGraphSpans()
        {
            var graph = Graph.Shared;
            var spans = graph != null
                ? GraphSpansField?.GetValue(graph) as Dictionary<string, TrackSpan>
                : null;
            if (spans != null)
            {
                return spans.Values
                    .Where(IsResolvedGraphSpan)
                    .ToArray();
            }

            return GetAllSpans()
                .Where(IsResolvedGraphSpan)
                .ToArray();
        }

        private static bool IsResolvedGraphSpan(TrackSpan span)
        {
            if (span == null || string.IsNullOrWhiteSpace(span.id) ||
                !span.upper.HasValue || !span.lower.HasValue)
            {
                return false;
            }

            var upper = span.upper.Value;
            var lower = span.lower.Value;
            return upper.segment != null &&
                   lower.segment != null &&
                   !string.IsNullOrWhiteSpace(upper.segment.id) &&
                   !string.IsNullOrWhiteSpace(lower.segment.id);
        }

        public static TrackSpan TryEnsureBaseGraphSpan(string id, string reason)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            var existing = GetSpan(id);
            if (existing != null)
            {
                return existing;
            }

            if (!_baseGraphSnapshotCaptured || !BaseSpanDefinitions.TryGetValue(id, out var definition))
            {
                return null;
            }

            try
            {
                var upperSegmentId = definition.Upper?.SegmentId;
                var lowerSegmentId = definition.Lower?.SegmentId;
                if (GetSegment(upperSegmentId) == null || GetSegment(lowerSegmentId) == null)
                {
                    FuseLog.Warning(
                        $"FUSE base graph span restore skipped operation='resolve-base-span' id='{id}' reason='{reason ?? string.Empty}' " +
                        $"upperSegment='{upperSegmentId ?? string.Empty}' lowerSegment='{lowerSegmentId ?? string.Empty}' detail='endpoint segment missing at runtime'.");
                    return null;
                }

                BeginBatch();
                try
                {
                    var span = AddSpan(id, CloneSpanDefinition(definition));
                    FuseLog.Info($"FUSE restored base graph span id='{id}' reason='{reason ?? string.Empty}' from captured Railroader graph snapshot.");
                    return span;
                }
                finally
                {
                    EndBatch(false);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE base graph span restore failed operation='resolve-base-span' id='{id}' reason='{reason ?? string.Empty}' error=''.", ex);
                return null;
            }
        }

        public static FuseSpan GetSpanDefinition(string id)
        {
            return GetDefinition(GetSpan(id));
        }

        public static FuseSpan GetDefinition(TrackSpan span)
        {
            if (span == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.TrackSpan, span.id, out FuseSpan definition);
            definition = definition ?? new FuseSpan();

            // Railroader's TrackSpan location getters can temporarily return
            // null when Graph.TryResolveLocation cannot resolve an otherwise
            // valid serialized endpoint. Keep the authored/cache endpoint in
            // that case. The audit can then distinguish a genuinely missing
            // endpoint from an endpoint whose referenced segment was removed.
            if (span.upper.HasValue)
            {
                definition.Upper = ToDefinition(span.upper.Value);
            }

            if (span.lower.HasValue)
            {
                definition.Lower = ToDefinition(span.lower.Value);
            }

            return definition;
        }

        private static string CreateRetiredSpanId(string id)
        {
            return $"__FUSE_RETIRED_SPAN__{id ?? string.Empty}__{Guid.NewGuid():N}";
        }
    }
}
