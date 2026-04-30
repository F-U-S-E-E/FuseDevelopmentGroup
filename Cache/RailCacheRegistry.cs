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
            Register(new RailNodeRuntimeIndex());
            Register(new RailSegmentRuntimeIndex());
            Register(new RailSpanRuntimeIndex());
            Register(new RailAreaRuntimeIndex());
            Register(new RailIndustryRuntimeIndex());
            Register(new RailIndustryComponentRuntimeIndex());
            Register(new RailLoaderRuntimeIndex());
            Register(new RailTurntableRuntimeIndex());
            Register(new RailLoadRuntimeIndex());
            Register(new RailProgressionRuntimeIndex());
            Register(new RailSectionRuntimeIndex());
            Register(new RailMapFeatureRuntimeIndex());
            Register(new RailStationRuntimeIndex());
            Register(new RailSceneryRuntimeIndex());
            Register(new RailSplineyRuntimeIndex());
            Register(new RailMapLabelRuntimeIndex());
        }

        public static void RebuildAll()
        {
            RailNodeRuntimeIndex.Instance.Rebuild();
            RailSegmentRuntimeIndex.Instance.Rebuild();
            RailSpanRuntimeIndex.Instance.Rebuild();
            RailAreaRuntimeIndex.Instance.Rebuild();
            RailIndustryRuntimeIndex.Instance.Rebuild();
            RailIndustryComponentRuntimeIndex.Instance.Rebuild();
            RailLoaderRuntimeIndex.Instance.Rebuild();
            RailTurntableRuntimeIndex.Instance.Rebuild();
            RailLoadRuntimeIndex.Instance.Rebuild();
            RailProgressionRuntimeIndex.Instance.Rebuild();
            RailSectionRuntimeIndex.Instance.Rebuild();
            RailMapFeatureRuntimeIndex.Instance.Rebuild();
            RailStationRuntimeIndex.Instance.Rebuild();
            RailSceneryRuntimeIndex.Instance.Rebuild();
            RailSplineyRuntimeIndex.Instance.Rebuild();
            RailMapLabelRuntimeIndex.Instance.Rebuild();
            _isReady = true;
        }

        public static void ClearAll()
        {
            RailNodeRuntimeIndex.Instance.Clear();
            RailSegmentRuntimeIndex.Instance.Clear();
            RailSpanRuntimeIndex.Instance.Clear();
            RailAreaRuntimeIndex.Instance.Clear();
            RailIndustryRuntimeIndex.Instance.Clear();
            RailIndustryComponentRuntimeIndex.Instance.Clear();
            RailLoaderRuntimeIndex.Instance.Clear();
            RailTurntableRuntimeIndex.Instance.Clear();
            RailLoadRuntimeIndex.Instance.Clear();
            RailProgressionRuntimeIndex.Instance.Clear();
            RailSectionRuntimeIndex.Instance.Clear();
            RailMapFeatureRuntimeIndex.Instance.Clear();
            RailStationRuntimeIndex.Instance.Clear();
            RailSceneryRuntimeIndex.Instance.Clear();
            RailSplineyRuntimeIndex.Instance.Clear();
            RailMapLabelRuntimeIndex.Instance.Clear();
            _isReady = false;
        }

        private static void Register(object cache)
        {
            Caches.Add(cache);
        }
    }
}
