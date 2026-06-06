using System.Collections.Generic;
using System.Diagnostics;
using FUSE.Infrastructure;

namespace FUSE.Runtime.Cache
{
    public static class FuseCacheRegistry
    {
        private static readonly List<object> Caches = new List<object>();
        private static bool _isReady;

        // Diagnostics: how often the full (16-index, scene-scanning) and graph-only
        // (3-index) rebuilds run. Surfaced to FUSE.log + the Health page so the cost
        // of repeated rebuilds during a single map load is measurable (issue: tile/
        // load-time bottleneck investigation).
        private static int _fullRebuildCount;
        private static int _graphRebuildCount;

        public static bool IsReady => _isReady;

        /// <summary>Full (scene-scanning) cache rebuilds since process start.</summary>
        public static int FullRebuildCount => _fullRebuildCount;

        /// <summary>Graph-only cache rebuilds since process start.</summary>
        public static int GraphRebuildCount => _graphRebuildCount;

        static FuseCacheRegistry()
        {
            Register(new FuseNodeRuntimeIndex());
            Register(new FuseSegmentRuntimeIndex());
            Register(new FuseSpanRuntimeIndex());
            Register(new FuseAreaRuntimeIndex());
            Register(new FuseIndustryRuntimeIndex());
            Register(new FuseIndustryComponentRuntimeIndex());
            Register(new FuseLoaderRuntimeIndex());
            Register(new FuseTurntableRuntimeIndex());
            Register(new FuseLoadRuntimeIndex());
            Register(new FuseProgressionRuntimeIndex());
            Register(new FuseSectionRuntimeIndex());
            Register(new FuseMapFeatureRuntimeIndex());
            Register(new FuseStationRuntimeIndex());
            Register(new FuseSceneryRuntimeIndex());
            Register(new FuseSplineyRuntimeIndex());
            Register(new FuseMapLabelRuntimeIndex());
        }

        public static void RebuildAll()
        {
            var stopwatch = Stopwatch.StartNew();

            // Graph-derived indexes (read Track.Graph.Shared).
            FuseNodeRuntimeIndex.Instance.Rebuild();
            FuseSegmentRuntimeIndex.Instance.Rebuild();
            FuseSpanRuntimeIndex.Instance.Rebuild();

            // Scene-scanning indexes (each does a FindObjectsOfType / GameObject.Find
            // pass). These are the expensive ones; the APIs that create/remove these
            // objects keep the indexes current incrementally (Set/Remove), so a full
            // rescan is only needed at map load / explicit reload — NOT on every graph
            // rebuild (see RebuildGraphIndexes).
            FuseAreaRuntimeIndex.Instance.Rebuild();
            FuseIndustryRuntimeIndex.Instance.Rebuild();
            FuseIndustryComponentRuntimeIndex.Instance.Rebuild();
            FuseLoaderRuntimeIndex.Instance.Rebuild();
            FuseTurntableRuntimeIndex.Instance.Rebuild();
            FuseLoadRuntimeIndex.Instance.Rebuild();
            FuseProgressionRuntimeIndex.Instance.Rebuild();
            FuseSectionRuntimeIndex.Instance.Rebuild();
            FuseMapFeatureRuntimeIndex.Instance.Rebuild();
            FuseStationRuntimeIndex.Instance.Rebuild();
            FuseSceneryRuntimeIndex.Instance.Rebuild();
            FuseSplineyRuntimeIndex.Instance.Rebuild();
            FuseMapLabelRuntimeIndex.Instance.Rebuild();
            _isReady = true;

            _fullRebuildCount++;
            FusePerformanceMetrics.RecordCount("cache full rebuilds", _fullRebuildCount);
            FuseLog.Info(
                $"FUSE cache full rebuild #{_fullRebuildCount} (16 indexes) elapsedMs={stopwatch.ElapsedMilliseconds}.");
        }

        /// <summary>
        /// Refreshes only the graph-derived indexes (node / segment / span) that read
        /// <c>Track.Graph.Shared</c>. Use this in response to a graph-rebuild event:
        /// the track topology changed, but the scene-scanning indexes (industry,
        /// scenery, station, …) did not, and they are kept current incrementally by
        /// the APIs that mutate them — so re-running their ~13 FindObjectsOfType scans
        /// on every graph rebuild is wasted work. Falls back to a full rebuild if the
        /// caches have never been built (so the scene indexes still get populated).
        /// </summary>
        public static void RebuildGraphIndexes()
        {
            if (!_isReady)
            {
                RebuildAll();
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            FuseNodeRuntimeIndex.Instance.Rebuild();
            FuseSegmentRuntimeIndex.Instance.Rebuild();
            FuseSpanRuntimeIndex.Instance.Rebuild();

            _graphRebuildCount++;
            FusePerformanceMetrics.RecordCount("cache graph-only rebuilds", _graphRebuildCount);
            FuseLog.Info(
                $"FUSE cache graph-only rebuild #{_graphRebuildCount} (3 graph indexes) elapsedMs={stopwatch.ElapsedMilliseconds}.");
        }

        public static void ClearAll()
        {
            FuseNodeRuntimeIndex.Instance.Clear();
            FuseSegmentRuntimeIndex.Instance.Clear();
            FuseSpanRuntimeIndex.Instance.Clear();
            FuseAreaRuntimeIndex.Instance.Clear();
            FuseIndustryRuntimeIndex.Instance.Clear();
            FuseIndustryComponentRuntimeIndex.Instance.Clear();
            FuseLoaderRuntimeIndex.Instance.Clear();
            FuseTurntableRuntimeIndex.Instance.Clear();
            FuseLoadRuntimeIndex.Instance.Clear();
            FuseProgressionRuntimeIndex.Instance.Clear();
            FuseSectionRuntimeIndex.Instance.Clear();
            FuseMapFeatureRuntimeIndex.Instance.Clear();
            FuseStationRuntimeIndex.Instance.Clear();
            FuseSceneryRuntimeIndex.Instance.Clear();
            FuseSplineyRuntimeIndex.Instance.Clear();
            FuseMapLabelRuntimeIndex.Instance.Clear();
            _isReady = false;
        }

        private static void Register(object cache)
        {
            Caches.Add(cache);
        }
    }
}
