using System;
using System.Reflection;
using FUSE.Authoring.Data;
using FUSE.Runtime.API;
using FUSE.Runtime.Events;
using Xunit;

namespace FUSE.Tests.API
{
    public sealed class TrackApiCompanionExtensionTests
    {
        [Fact]
        public void TrackGraphApplying_PublishesContextToCompanionModules()
        {
            FuseTrackGraphApplyingContext received = null;
            Action<FuseTrackGraphApplyingContext> handler = context => received = context;
            FuseEvents.TrackGraphApplying += handler;

            try
            {
                FuseEvents.RaiseTrackGraphApplying(null);
                Assert.NotNull(received);
                Assert.Null(received.Graph);
            }
            finally
            {
                FuseEvents.TrackGraphApplying -= handler;
            }
        }

        [Fact]
        public void CloneSegmentDefinition_PreservesCompanionMetadata()
        {
            var source = new FuseSegment
            {
                StartNodeId = "a",
                EndNodeId = "b",
                Gauge = "DualGauge",
                BridgeSupportsSteel = true,
                Yard = true,
                Partial = true,
                PreserveStyle = true,
                PreserveBridgeSupportsSteel = true,
                PreserveYard = true,
                PreserveTrackClass = true,
                PreserveSpeedLimit = true,
                PreservePriority = true,
                PreserveGroupId = true
            };

            MethodInfo cloneMethod = typeof(TrackAPI).GetMethod(
                "CloneSegmentDefinition",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(cloneMethod);
            var clone = Assert.IsType<FuseSegment>(cloneMethod.Invoke(null, new object[] { source }));

            Assert.Equal("DualGauge", clone.Gauge);
            Assert.True(clone.BridgeSupportsSteel);
            Assert.True(clone.Yard);
            Assert.True(clone.Partial);
            Assert.True(clone.PreserveStyle);
            Assert.True(clone.PreserveBridgeSupportsSteel);
            Assert.True(clone.PreserveYard);
            Assert.True(clone.PreserveTrackClass);
            Assert.True(clone.PreserveSpeedLimit);
            Assert.True(clone.PreservePriority);
            Assert.True(clone.PreserveGroupId);
        }
    }
}
