using System;
using FUSE.Authoring.Validation;
using FUSE.Infrastructure;
using Model.Ops;
using Track;

namespace FUSE.Runtime.Events
{
    /// <summary>
    /// Published immediately before FUSE rebuilds Railroader's native track graph.
    /// Handlers run inside a TrackAPI batch, so companion modules may use public
    /// TrackAPI methods without causing a nested or additional graph rebuild.
    /// </summary>
    public sealed class FuseTrackGraphApplyingContext
    {
        internal FuseTrackGraphApplyingContext(Graph graph)
        {
            Graph = graph;
        }

        public Graph Graph { get; }
    }

    public static class FuseEvents
    {
        public static event Action FuseLoaded;
        public static event Action FuseUnloaded;
        public static event Action<TrackNode> NodeAdded;
        public static event Action<TrackNode> NodeUpdated;
        public static event Action<string> NodeRemoved;
        public static event Action<TrackSegment> SegmentAdded;
        public static event Action<TrackSegment> SegmentUpdated;
        public static event Action<string> SegmentRemoved;
        public static event Action<TrackSpan> SpanAdded;
        public static event Action<TrackSpan> SpanUpdated;
        public static event Action<string> SpanRemoved;
        public static event Action<Industry> IndustryAdded;
        public static event Action<Industry> IndustryUpdated;
        public static event Action<string> IndustryRemoved;
        public static event Action<IndustryComponent> IndustryComponentAdded;
        public static event Action<IndustryComponent> IndustryComponentUpdated;
        public static event Action<string> IndustryComponentRemoved;
        public static event Action<FuseTrackGraphApplyingContext> TrackGraphApplying;
        public static event Action GraphRebuilt;
        public static event Action<string, ValidationResult> ValidationCompleted;
        public static event Action<string> ModLoaded;
        public static event Action<string> ModUnloaded;
        public static event Action<string> ModSetAdded;
        public static event Action<string> ModSetRemoved;

        internal static void RaiseFuseLoaded()
        {
            FuseLoaded?.Invoke();
        }

        internal static void RaiseFuseUnloaded()
        {
            FuseUnloaded?.Invoke();
        }

        public static void RaiseNodeAdded(TrackNode node)
        {
            NodeAdded?.Invoke(node);
        }

        public static void RaiseNodeUpdated(TrackNode node)
        {
            NodeUpdated?.Invoke(node);
        }

        public static void RaiseNodeRemoved(string id)
        {
            NodeRemoved?.Invoke(id);
        }

        public static void RaiseSegmentAdded(TrackSegment segment)
        {
            SegmentAdded?.Invoke(segment);
        }

        public static void RaiseSegmentUpdated(TrackSegment segment)
        {
            SegmentUpdated?.Invoke(segment);
        }

        public static void RaiseSegmentRemoved(string id)
        {
            SegmentRemoved?.Invoke(id);
        }

        public static void RaiseSpanAdded(TrackSpan span)
        {
            SpanAdded?.Invoke(span);
        }

        public static void RaiseSpanUpdated(TrackSpan span)
        {
            SpanUpdated?.Invoke(span);
        }

        public static void RaiseSpanRemoved(string id)
        {
            SpanRemoved?.Invoke(id);
        }

        public static void RaiseIndustryAdded(Industry industry)
        {
            IndustryAdded?.Invoke(industry);
        }

        public static void RaiseIndustryUpdated(Industry industry)
        {
            IndustryUpdated?.Invoke(industry);
        }

        public static void RaiseIndustryRemoved(string id)
        {
            IndustryRemoved?.Invoke(id);
        }

        public static void RaiseIndustryComponentAdded(IndustryComponent component)
        {
            IndustryComponentAdded?.Invoke(component);
        }

        public static void RaiseIndustryComponentUpdated(IndustryComponent component)
        {
            IndustryComponentUpdated?.Invoke(component);
        }

        public static void RaiseIndustryComponentRemoved(string id)
        {
            IndustryComponentRemoved?.Invoke(id);
        }

        internal static void RaiseTrackGraphApplying(Graph graph)
        {
            var handlers = TrackGraphApplying;
            if (handlers == null)
            {
                return;
            }

            var context = new FuseTrackGraphApplyingContext(graph);
            foreach (Action<FuseTrackGraphApplyingContext> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(context);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception(
                        $"FUSE track-graph applying subscriber '{handler.Method?.DeclaringType?.FullName ?? "<unknown>"}.{handler.Method?.Name ?? "<unknown>"}' failed",
                        ex);
                }
            }
        }

        public static void RaiseGraphRebuilt()
        {
            GraphRebuilt?.Invoke();
        }

        public static void RaiseValidationCompleted(string objectId, ValidationResult result)
        {
            ValidationCompleted?.Invoke(objectId, result);
        }

        public static void RaiseModLoaded(string modId)
        {
            ModLoaded?.Invoke(modId);
        }

        public static void RaiseModUnloaded(string modId)
        {
            ModUnloaded?.Invoke(modId);
        }

        public static void RaiseModSetAdded(string modId)
        {
            ModSetAdded?.Invoke(modId);
        }

        public static void RaiseModSetRemoved(string modId)
        {
            ModSetRemoved?.Invoke(modId);
        }
    }
}
