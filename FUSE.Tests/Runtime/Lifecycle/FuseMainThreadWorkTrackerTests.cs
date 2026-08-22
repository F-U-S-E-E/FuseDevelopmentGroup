using FUSE.Runtime.Lifecycle;
using Xunit;

namespace FUSE.Tests.Runtime.Lifecycle
{
    public sealed class FuseMainThreadWorkTrackerTests
    {
        public FuseMainThreadWorkTrackerTests()
        {
            FuseMainThreadWorkTracker.Reset();
        }

        [Fact]
        public void RecordElapsedRetainsSlowestPhaseForFrame()
        {
            FuseMainThreadWorkTracker.RecordElapsed(10, "first", 2d);
            FuseMainThreadWorkTracker.RecordElapsed(10, "slowest", 5d);
            FuseMainThreadWorkTracker.RecordElapsed(10, "later", 3d);

            Assert.True(FuseMainThreadWorkTracker.TryGet(10, out var phase, out var milliseconds));
            Assert.Equal("slowest", phase);
            Assert.Equal(5d, milliseconds);
        }

        [Fact]
        public void RecordElapsedKeepsPreviousFrameUntilNextFrameArrives()
        {
            FuseMainThreadWorkTracker.RecordElapsed(10, "ten", 4d);
            FuseMainThreadWorkTracker.RecordElapsed(11, "eleven", 6d);

            Assert.True(FuseMainThreadWorkTracker.TryGet(10, out var previous, out var previousMs));
            Assert.Equal("ten", previous);
            Assert.Equal(4d, previousMs);
            Assert.True(FuseMainThreadWorkTracker.TryGet(11, out var current, out var currentMs));
            Assert.Equal("eleven", current);
            Assert.Equal(6d, currentMs);

            FuseMainThreadWorkTracker.RecordElapsed(12, "twelve", 1d);
            Assert.False(FuseMainThreadWorkTracker.TryGet(10, out _, out _));
        }

        [Fact]
        public void ResetClearsBothFrames()
        {
            FuseMainThreadWorkTracker.RecordElapsed(10, "ten", 4d);
            FuseMainThreadWorkTracker.RecordElapsed(11, "eleven", 6d);

            FuseMainThreadWorkTracker.Reset();

            Assert.False(FuseMainThreadWorkTracker.TryGet(10, out _, out _));
            Assert.False(FuseMainThreadWorkTracker.TryGet(11, out _, out _));
        }
    }
}
