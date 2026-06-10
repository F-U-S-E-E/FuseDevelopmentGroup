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
        private const float SpanDistanceTolerance = 0.001f;
        private const int AreaOrderSiblingSpacing = 100;

        private static int _batchDepth;
        private static bool _rebuildRequested;
        private static Transform _fallbackAreaRoot;
        private static readonly Dictionary<string, int> AreaOrders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> AreaFallbackOrders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> AreaAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, FuseNode> BaseNodeDefinitions = new Dictionary<string, FuseNode>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, FuseSegment> BaseSegmentDefinitions = new Dictionary<string, FuseSegment>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, FuseSpan> BaseSpanDefinitions = new Dictionary<string, FuseSpan>(StringComparer.OrdinalIgnoreCase);
        private static readonly MethodInfo GraphAddSpanMethod =
            typeof(Graph).GetMethod("AddSpan", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo GraphNodesField =
            typeof(Graph).GetField("nodes", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo GraphSegmentsField =
            typeof(Graph).GetField("segments", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo GraphSpansField =
            typeof(Graph).GetField("spans", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo[] GraphDerivedCacheFields =
        {
            typeof(Graph).GetField("_nodeConnectionsCache", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(Graph).GetField("_nodeIsDeadEndCache", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(Graph).GetField("_cachedReachableSegments", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(Graph).GetField("_decodedSwitchCache", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(Graph).GetField("_segmentsReachableFromOthers", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(Graph).GetField("_cachedTurntableControllers", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(Graph).GetField("_curvatureSampleCache", BindingFlags.Instance | BindingFlags.NonPublic)
        };
        private static bool _warnedMissingGraphAddSpan;
        private static bool _baseGraphSnapshotCaptured;

        public static bool IsBatching => _batchDepth > 0;

        public static void BeginBatch()
        {
            _batchDepth++;
        }

        public static void EndBatch()
        {
            EndBatch(true);
        }

        public static void EndBatch(bool rebuild)
        {
            if (_batchDepth == 0)
            {
                return;
            }

            _batchDepth--;
            if (_batchDepth == 0 && _rebuildRequested && rebuild)
            {
                RebuildGraph();
            }
        }

        public static void RebuildGraph()
        {
            _rebuildRequested = false;
            var manager = TrackObjectManager.Instance;
            if (manager != null)
            {
                manager.Rebuild();
            }
            else
            {
                RequireGraph().RebuildCollections();
            }

            FuseEvents.RaiseGraphRebuilt();
        }

        /// <summary>
        /// Consumes a rebuild request left pending by a batch that ended with
        /// rebuild=false, so the caller can run <see cref="RebuildGraph"/> under
        /// its own exception guard and timing phase instead of inside EndBatch's
        /// unguarded call. Returns whether a request was pending; the flag is
        /// cleared either way. Only call with no batch open — while batching,
        /// the pending flag belongs to the outermost EndBatch.
        /// </summary>
        public static bool ConsumePendingRebuildRequest()
        {
            var pending = _rebuildRequested;
            _rebuildRequested = false;
            return pending;
        }

        /// <summary>
        /// Request a graph rebuild — runs immediately if no batch is active, or
        /// folds into the outermost <see cref="EndBatch(bool)"/> when callers
        /// have opened one. Cleanup paths that need a rebuild "if the caller
        /// hasn't already arranged one" should use this in preference to
        /// calling <see cref="RebuildGraph"/> directly, otherwise they force a
        /// second full rebuild on top of whatever the caller's batch already
        /// scheduled.
        /// </summary>
        public static void RequestRebuild()
        {
            _rebuildRequested = true;
            if (!IsBatching)
            {
                RebuildGraph();
            }
        }

        private static void RequireId(string id, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("ID is required.", parameterName);
            }
        }
    }
}
