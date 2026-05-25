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

        private static FuseSpan CloneSpanDefinition(FuseSpan definition)
        {
            if (definition == null)
            {
                return null;
            }

            return new FuseSpan
            {
                Upper = CloneTrackLocation(definition.Upper),
                Lower = CloneTrackLocation(definition.Lower),
                Normalize = definition.Normalize,
                GroupId = definition.GroupId
            };
        }

        private static FuseNode CloneNodeDefinition(FuseNode definition)
        {
            if (definition == null)
            {
                return null;
            }

            return new FuseNode
            {
                Position = definition.Position,
                Rotation = definition.Rotation,
                FlipSwitchStand = definition.FlipSwitchStand,
                GroupId = definition.GroupId,
                Tags = definition.Tags?.ToArray()
            };
        }

        private static FuseSegment CloneSegmentDefinition(FuseSegment definition)
        {
            if (definition == null)
            {
                return null;
            }

            return new FuseSegment
            {
                StartNodeId = definition.StartNodeId,
                EndNodeId = definition.EndNodeId,
                Style = definition.Style,
                TrackClass = definition.TrackClass,
                SpeedLimit = definition.SpeedLimit,
                Priority = definition.Priority,
                GroupId = definition.GroupId,
                Tags = definition.Tags?.ToArray()
            };
        }

        private static FuseTrackLocation CloneTrackLocation(FuseTrackLocation location)
        {
            if (location == null)
            {
                return null;
            }

            return new FuseTrackLocation
            {
                SegmentId = location.SegmentId,
                Normalized = location.Normalized,
                Distance = location.Distance,
                End = location.End,
                Offset = location.Offset
            };
        }

        private static FuseTrackLocation ToDefinition(Location location)
        {
            if (location.segment == null)
            {
                return null;
            }

            return new FuseTrackLocation
            {
                SegmentId = location.segment.id,
                End = location.end == TrackSegment.End.B ? "B" : "A",
                Distance = location.distance
            };
        }

        private static TrackSegment.End ParseLocationEnd(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return TrackSegment.End.A;
            }

            switch (value.Trim().ToUpperInvariant())
            {
                case "A":
                case "START":
                    return TrackSegment.End.A;
                case "B":
                case "END":
                    return TrackSegment.End.B;
                default:
                    throw new ArgumentException($"Unsupported track location end '{value}'. Expected A/B or Start/End.", nameof(value));
            }
        }

        private static TrackNode RequireNode(string id)
        {
            var node = GetNode(id);
            if (node == null)
            {
                throw new InvalidOperationException($"Track node '{id}' was not found.");
            }

            return node;
        }

        private static TrackSegment RequireSegment(string id)
        {
            var segment = GetSegment(id);
            if (segment == null)
            {
                throw new InvalidOperationException($"Track segment '{id}' was not found.");
            }

            return segment;
        }

        private static TrackSpan RequireSpan(string id)
        {
            var span = GetSpan(id);
            if (span == null)
            {
                throw new InvalidOperationException($"Track span '{id}' was not found.");
            }

            return span;
        }

        private static void RemoveRuntimeObject(Component component)
        {
            if (component == null)
            {
                return;
            }

            if (component is TrackSpan && component.gameObject.GetComponentCount() > 2)
            {
                UnityEngine.Object.Destroy(component);
            }
            else
            {
                var gameObject = component.gameObject;
                PreserveChildTrackSpansBeforeDestroy(component, gameObject);
                gameObject.SetActive(false);
                UnityEngine.Object.Destroy(gameObject);
            }
        }

        private static void PreserveChildTrackSpansBeforeDestroy(Component component, GameObject gameObject)
        {
            if (component is TrackSpan || gameObject == null)
            {
                return;
            }

            var graph = Graph.Shared;
            if (graph == null)
            {
                return;
            }

            foreach (var span in gameObject.GetComponentsInChildren<TrackSpan>(true))
            {
                if (span == null || string.IsNullOrWhiteSpace(span.id))
                {
                    continue;
                }

                try
                {
                    if (span.gameObject == gameObject)
                    {
                        var upper = span.upper;
                        var lower = span.lower;
                        if (!upper.HasValue || !lower.HasValue)
                        {
                            continue;
                        }

                        var clone = CreateGraphChild<TrackSpan>(graph, "Span-" + span.id);
                        clone.id = span.id;
                        clone.upper = upper;
                        clone.lower = lower;
                        FuseSpanRuntimeIndex.Instance.Set(clone.id, clone);
                        RegisterSpanWithGraph(graph, clone);
                    }
                    else
                    {
                        span.transform.SetParent(graph.transform, true);
                        FuseSpanRuntimeIndex.Instance.Set(span.id, span);
                        RegisterSpanWithGraph(graph, span);
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE could not preserve child track span '{span.id}' before destroying '{gameObject.name}'", ex);
                }
            }
        }

        private static TrackSegment.Style ParseSegmentStyle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return TrackSegment.Style.Standard;
            }

            return (TrackSegment.Style)Enum.Parse(typeof(TrackSegment.Style), value, true);
        }

        private static TrackClass ParseTrackClass(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return TrackClass.Mainline;
            }

            if (string.Equals(value, "main", StringComparison.OrdinalIgnoreCase))
            {
                return TrackClass.Mainline;
            }

            return (TrackClass)Enum.Parse(typeof(TrackClass), value, true);
        }
    }
}
