using System;
using FUSE.Runtime.Lifecycle;
using Xunit;

namespace FUSE.Tests.Runtime.Lifecycle
{
    public class AdaptiveFrameHitchDetectorTests
    {
        [Fact]
        public void FirstFrame_SeedsBaseline_WithoutReportingHitch()
        {
            var detector = new AdaptiveFrameHitchDetector(windowSize: 5, baselineMultiplier: 1.5f);

            var observation = detector.Observe(frameMs: 50f, absoluteFloorMs: 20f);

            Assert.False(observation.IsHitch);
            Assert.Equal(50f, observation.BaselineMs);
            Assert.Equal(75f, observation.EffectiveThresholdMs);
            Assert.Equal(1, detector.SampleCount);
        }

        [Fact]
        public void AbsoluteFloor_DominatesFastRollingBaseline()
        {
            var detector = new AdaptiveFrameHitchDetector(windowSize: 5, baselineMultiplier: 1.5f);
            detector.Observe(frameMs: 16f, absoluteFloorMs: 50f);

            var ordinary = detector.Observe(frameMs: 40f, absoluteFloorMs: 50f);
            var hitch = detector.Observe(frameMs: 55f, absoluteFloorMs: 50f);

            Assert.False(ordinary.IsHitch);
            Assert.Equal(50f, ordinary.EffectiveThresholdMs);
            Assert.True(hitch.IsHitch);
            Assert.Equal(16f, hitch.BaselineMs);
            Assert.Equal(50f, hitch.EffectiveThresholdMs);
        }

        [Fact]
        public void AdaptiveThreshold_DoesNotCallOrdinaryLowFpsFrameAHitch()
        {
            var detector = new AdaptiveFrameHitchDetector(windowSize: 5, baselineMultiplier: 1.5f);
            detector.Observe(frameMs: 50f, absoluteFloorMs: 20f);

            var ordinary = detector.Observe(frameMs: 70f, absoluteFloorMs: 20f);
            var hitch = detector.Observe(frameMs: 76f, absoluteFloorMs: 20f);

            Assert.False(ordinary.IsHitch);
            Assert.Equal(50f, ordinary.BaselineMs);
            Assert.Equal(75f, ordinary.EffectiveThresholdMs);
            Assert.True(hitch.IsHitch);
        }

        [Fact]
        public void IsolatedHitch_DoesNotPullMedianBaselineUpward()
        {
            var detector = new AdaptiveFrameHitchDetector(windowSize: 5, baselineMultiplier: 1.5f);
            for (var i = 0; i < 5; i++)
            {
                detector.Observe(frameMs: 10f, absoluteFloorMs: 5f);
            }

            var hitch = detector.Observe(frameMs: 100f, absoluteFloorMs: 5f);
            var followingFrame = detector.Observe(frameMs: 14f, absoluteFloorMs: 5f);

            Assert.True(hitch.IsHitch);
            Assert.Equal(10f, detector.BaselineMs);
            Assert.False(followingFrame.IsHitch);
            Assert.Equal(15f, followingFrame.EffectiveThresholdMs);
        }

        [Fact]
        public void SustainedSlowdown_AdaptsAfterItBecomesWindowMajority()
        {
            var detector = new AdaptiveFrameHitchDetector(windowSize: 5, baselineMultiplier: 1.5f);
            detector.Observe(frameMs: 10f, absoluteFloorMs: 5f);

            Assert.True(detector.Observe(frameMs: 40f, absoluteFloorMs: 5f).IsHitch);
            Assert.True(detector.Observe(frameMs: 40f, absoluteFloorMs: 5f).IsHitch);

            var adapted = detector.Observe(frameMs: 40f, absoluteFloorMs: 5f);

            Assert.False(adapted.IsHitch);
            Assert.Equal(40f, adapted.BaselineMs);
            Assert.Equal(60f, adapted.EffectiveThresholdMs);
        }

        [Fact]
        public void FullWindow_AgesOutOldSamples()
        {
            var detector = new AdaptiveFrameHitchDetector(windowSize: 3, baselineMultiplier: 1.5f);
            detector.Observe(frameMs: 10f, absoluteFloorMs: 5f);
            detector.Observe(frameMs: 10f, absoluteFloorMs: 5f);
            detector.Observe(frameMs: 10f, absoluteFloorMs: 5f);

            detector.Observe(frameMs: 40f, absoluteFloorMs: 5f);
            detector.Observe(frameMs: 40f, absoluteFloorMs: 5f);

            Assert.Equal(40f, detector.BaselineMs);
            var ordinary = detector.Observe(frameMs: 50f, absoluteFloorMs: 5f);
            Assert.False(ordinary.IsHitch);
            Assert.Equal(60f, ordinary.EffectiveThresholdMs);
        }

        [Fact]
        public void InvalidFrame_IsIgnored_AndResetReusesDetector()
        {
            var detector = new AdaptiveFrameHitchDetector(windowSize: 5, baselineMultiplier: 1.5f);
            detector.Observe(frameMs: 16f, absoluteFloorMs: 20f);

            var invalid = detector.Observe(float.NaN, absoluteFloorMs: 20f);
            Assert.False(invalid.IsHitch);
            Assert.Equal(1, detector.SampleCount);

            detector.Reset();

            Assert.Equal(0, detector.SampleCount);
            Assert.Equal(0f, detector.BaselineMs);
            Assert.False(detector.Observe(frameMs: 80f, absoluteFloorMs: 20f).IsHitch);
            Assert.Equal(80f, detector.BaselineMs);
        }

        [Theory]
        [InlineData(2, 1.5f)]
        [InlineData(5, 1f)]
        [InlineData(5, float.PositiveInfinity)]
        public void Constructor_RejectsInvalidConfiguration(int windowSize, float multiplier)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new AdaptiveFrameHitchDetector(windowSize, multiplier));
        }
    }
}
