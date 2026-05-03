using System.Collections.Generic;

namespace FUSE.Cache
{
    public static class FuseCacheRegistry
    {
        private static readonly List<object> Caches = new List<object>();
        private static bool _isReady;

        public static bool IsReady => _isReady;

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
            FuseNodeRuntimeIndex.Instance.Rebuild();
            FuseSegmentRuntimeIndex.Instance.Rebuild();
            FuseSpanRuntimeIndex.Instance.Rebuild();
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
