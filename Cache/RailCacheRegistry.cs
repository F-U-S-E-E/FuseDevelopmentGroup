using System.Collections.Generic;

namespace RAIL.Cache
{
    public static class RailCacheRegistry
    {
        private static readonly List<object> Caches = new List<object>();
        private static bool _isReady;

        public static bool IsReady => _isReady;

        static RailCacheRegistry()
        {
            Register(new TrackNodeCache());
            Register(new TrackSegmentCache());
            Register(new TrackSpanCache());
            Register(new AreaCache());
            Register(new IndustryCache());
            Register(new IndustryComponentCache());
            Register(new LoaderCache());
            Register(new TurntableCache());
            Register(new LoadCache());
            Register(new ProgressionCache());
            Register(new SectionCache());
            Register(new MapFeatureCache());
            Register(new StationAgentCache());
            Register(new SceneryCache());
            Register(new SplineyCache());
            Register(new MapLabelCache());
        }

        public static void RebuildAll()
        {
            TrackNodeCache.Instance.Rebuild();
            TrackSegmentCache.Instance.Rebuild();
            TrackSpanCache.Instance.Rebuild();
            AreaCache.Instance.Rebuild();
            IndustryCache.Instance.Rebuild();
            IndustryComponentCache.Instance.Rebuild();
            LoaderCache.Instance.Rebuild();
            TurntableCache.Instance.Rebuild();
            LoadCache.Instance.Rebuild();
            ProgressionCache.Instance.Rebuild();
            SectionCache.Instance.Rebuild();
            MapFeatureCache.Instance.Rebuild();
            StationAgentCache.Instance.Rebuild();
            SceneryCache.Instance.Rebuild();
            SplineyCache.Instance.Rebuild();
            MapLabelCache.Instance.Rebuild();
            _isReady = true;
        }

        public static void ClearAll()
        {
            TrackNodeCache.Instance.Clear();
            TrackSegmentCache.Instance.Clear();
            TrackSpanCache.Instance.Clear();
            AreaCache.Instance.Clear();
            IndustryCache.Instance.Clear();
            IndustryComponentCache.Instance.Clear();
            LoaderCache.Instance.Clear();
            TurntableCache.Instance.Clear();
            LoadCache.Instance.Clear();
            ProgressionCache.Instance.Clear();
            SectionCache.Instance.Clear();
            MapFeatureCache.Instance.Clear();
            StationAgentCache.Instance.Clear();
            SceneryCache.Instance.Clear();
            SplineyCache.Instance.Clear();
            MapLabelCache.Instance.Clear();
            _isReady = false;
        }

        private static void Register(object cache)
        {
            Caches.Add(cache);
        }
    }
}
