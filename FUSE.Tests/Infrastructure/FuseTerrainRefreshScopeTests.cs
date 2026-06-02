using FUSE.Infrastructure;
using UnityEngine;
using Xunit;

namespace FUSE.Tests.Infrastructure
{
    /// <summary>
    /// Tests for the map-load terrain-refresh coordinator: the scope that lets the
    /// single trailing terrain rebuild absorb per-object mask refreshes (#3) and
    /// accumulates the footprint FUSE touched for optional targeted invalidation (#4).
    /// The depth/reset/union semantics are what the lifecycle and MapAPI rely on, so a
    /// regression here would either leak deferral state across loads or mis-scope the
    /// targeted rebuild. Each test fully opens and closes the scope so the shared
    /// static state can't bleed between cases.
    /// </summary>
    public class FuseTerrainRefreshScopeTests
    {
        [Fact]
        public void Begin_OpensDeferral_DisposeCloses()
        {
            Assert.False(FuseTerrainRefreshScope.IsDeferringMaskRefresh);

            var token = FuseTerrainRefreshScope.Begin();
            Assert.True(FuseTerrainRefreshScope.IsDeferringMaskRefresh);

            token.Dispose();
            Assert.False(FuseTerrainRefreshScope.IsDeferringMaskRefresh);
        }

        [Fact]
        public void NestedScopes_StayOpenUntilOutermostCloses()
        {
            var outer = FuseTerrainRefreshScope.Begin();
            var inner = FuseTerrainRefreshScope.Begin();
            Assert.True(FuseTerrainRefreshScope.IsDeferringMaskRefresh);

            inner.Dispose();
            Assert.True(FuseTerrainRefreshScope.IsDeferringMaskRefresh); // still open: outer holds it

            outer.Dispose();
            Assert.False(FuseTerrainRefreshScope.IsDeferringMaskRefresh);
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            var token = FuseTerrainRefreshScope.Begin();
            token.Dispose();
            token.Dispose(); // must not double-decrement into a negative/leaked depth
            Assert.False(FuseTerrainRefreshScope.IsDeferringMaskRefresh);

            // A fresh scope still opens cleanly after a double-dispose.
            var next = FuseTerrainRefreshScope.Begin();
            Assert.True(FuseTerrainRefreshScope.IsDeferringMaskRefresh);
            next.Dispose();
            Assert.False(FuseTerrainRefreshScope.IsDeferringMaskRefresh);
        }

        [Fact]
        public void NoteDeferredRefresh_AccumulatesCallsAndComponents()
        {
            var token = FuseTerrainRefreshScope.Begin();
            FuseTerrainRefreshScope.NoteDeferredRefresh(0);
            FuseTerrainRefreshScope.NoteDeferredRefresh(3);

            Assert.Equal(2, FuseTerrainRefreshScope.DeferredRefreshCalls);
            Assert.Equal(3, FuseTerrainRefreshScope.DeferredMaskComponents);
            token.Dispose();
        }

        [Fact]
        public void Begin_Outermost_ResetsCountersAndBounds()
        {
            var first = FuseTerrainRefreshScope.Begin();
            FuseTerrainRefreshScope.NoteDeferredRefresh(5);
            FuseTerrainRefreshScope.RecordBounds(new Bounds(new Vector3(10f, 0f, 10f), Vector3.one));
            first.Dispose();

            // A brand-new outermost scope starts clean.
            var second = FuseTerrainRefreshScope.Begin();
            Assert.Equal(0, FuseTerrainRefreshScope.DeferredRefreshCalls);
            Assert.Equal(0, FuseTerrainRefreshScope.DeferredMaskComponents);
            Assert.False(FuseTerrainRefreshScope.TryGetAccumulatedBounds(out _));
            second.Dispose();
        }

        [Fact]
        public void RecordBounds_UnionsFootprints()
        {
            var token = FuseTerrainRefreshScope.Begin();

            Assert.False(FuseTerrainRefreshScope.TryGetAccumulatedBounds(out _));

            FuseTerrainRefreshScope.RecordBounds(new Bounds(new Vector3(0f, 0f, 0f), new Vector3(2f, 2f, 2f)));
            FuseTerrainRefreshScope.RecordBounds(new Bounds(new Vector3(100f, 0f, 0f), new Vector3(2f, 2f, 2f)));

            Assert.True(FuseTerrainRefreshScope.TryGetAccumulatedBounds(out var union));
            // Union must span both footprints on X (1 .. 101).
            Assert.True(union.min.x <= -1f + 0.001f);
            Assert.True(union.max.x >= 101f - 0.001f);
            token.Dispose();
        }
    }
}
