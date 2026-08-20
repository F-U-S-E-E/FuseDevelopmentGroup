using System;
using FUSE.Authoring.Data;
using FUSE.Authoring.Data.Common;
using FUSE.Runtime.API;
using UnityEngine;
using Xunit;

namespace FUSE.Tests.Runtime.API
{
    public sealed class FuseRuntimeDefinitionCacheTests
    {
        [Fact]
        public void Span_reads_are_deep_clones_and_cannot_erase_cached_endpoints()
        {
            var id = "span-cache-test-" + Guid.NewGuid().ToString("N");
            var source = new FuseSpan
            {
                Upper = new FuseTrackLocation { SegmentId = "upper-segment", Normalized = 0.25f },
                Lower = new FuseTrackLocation { SegmentId = "lower-segment", Distance = 12.5f },
                Normalize = false,
                GroupId = "test-group",
            };

            FuseRuntimeDefinitionCache.Store(FuseDefinitionKind.TrackSpan, id, source);
            Assert.True(FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.TrackSpan, id, out FuseSpan first));

            first.Upper.SegmentId = "mutated";
            first.Lower = null;
            first.GroupId = "mutated-group";

            Assert.True(FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.TrackSpan, id, out FuseSpan second));
            Assert.Equal("upper-segment", second.Upper.SegmentId);
            Assert.Equal(0.25f, second.Upper.Normalized);
            Assert.Equal("lower-segment", second.Lower.SegmentId);
            Assert.Equal(12.5f, second.Lower.Distance);
            Assert.False(second.Normalize);
            Assert.Equal("test-group", second.GroupId);

            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.TrackSpan, id);
        }

        [Fact]
        public void Water_surface_reads_clone_boundary_points()
        {
            var id = "water-cache-test-" + Guid.NewGuid().ToString("N");
            var source = new FuseWaterSurface
            {
                Points = new[] { Vector3.zero, Vector3.right, Vector3.forward },
                MaterialName = "Stock Water",
            };

            FuseRuntimeDefinitionCache.Store(FuseDefinitionKind.WaterSurface, id, source);
            Assert.True(FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.WaterSurface, id, out FuseWaterSurface first));
            first.Points[0] = Vector3.one;

            Assert.True(FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.WaterSurface, id, out FuseWaterSurface second));
            Assert.Equal(Vector3.zero, second.Points[0]);
            Assert.Equal("Stock Water", second.MaterialName);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.WaterSurface, id);
        }
    }
}
