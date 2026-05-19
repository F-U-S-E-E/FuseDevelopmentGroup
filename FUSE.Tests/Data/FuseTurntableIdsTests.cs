using FUSE.Data;
using Xunit;

namespace FUSE.Tests.Data
{
    public class FuseTurntableIdsTests
    {
        [Theory]
        [InlineData("yard-t1", 0, "yard-t1.pit.00")]
        [InlineData("yard-t1", 5, "yard-t1.pit.05")]
        [InlineData("yard-t1", 15, "yard-t1.pit.15")]
        public void Pit_NoLegacy_UsesDottedFormatWithTwoDigitIndex(string turntableId, int index, string expected)
        {
            Assert.Equal(expected, FuseTurntableIds.GetPitNodeId(turntableId, index));
            Assert.Equal(expected, FuseTurntableIds.GetPitNodeId(turntableId, index, new FuseTurntable()));
            Assert.Equal(expected, FuseTurntableIds.GetPitNodeId(turntableId, index, definition: null));
        }

        [Fact]
        public void Pit_WithLegacyIdentifier_UsesNPrefixFormat()
        {
            var definition = new FuseTurntable { LegacyIdentifier = "Robinson" };

            Assert.Equal("NRobinsonTurntableNode3",
                FuseTurntableIds.GetPitNodeId("yard-t1", 3, definition));
        }

        [Fact]
        public void Pit_BlankLegacyIdentifier_FallsBackToDottedFormat()
        {
            var definition = new FuseTurntable { LegacyIdentifier = "   " };

            Assert.Equal("yard-t1.pit.03",
                FuseTurntableIds.GetPitNodeId("yard-t1", 3, definition));
        }

        [Theory]
        [InlineData(1, "yard-t1.roundhouse.node.01")]
        [InlineData(7, "yard-t1.roundhouse.node.07")]
        [InlineData(12, "yard-t1.roundhouse.node.12")]
        public void RoundhouseNode_NoLegacy_UsesDottedFormat(int index, string expected)
        {
            Assert.Equal(expected,
                FuseTurntableIds.GetRoundhouseNodeId("yard-t1", index, new FuseTurntable()));
        }

        [Fact]
        public void RoundhouseNode_WithLegacyIdentifier_UsesNPrefixFormat()
        {
            var definition = new FuseTurntable { LegacyIdentifier = "Robinson" };

            Assert.Equal("NRobinsonRoundhouseNode4",
                FuseTurntableIds.GetRoundhouseNodeId("yard-t1", 4, definition));
        }

        [Theory]
        [InlineData(1, "yard-t1.roundhouse.segment.01")]
        [InlineData(7, "yard-t1.roundhouse.segment.07")]
        public void RoundhouseSegment_NoLegacy_UsesDottedFormat(int index, string expected)
        {
            Assert.Equal(expected,
                FuseTurntableIds.GetRoundhouseSegmentId("yard-t1", index, new FuseTurntable()));
        }

        [Fact]
        public void RoundhouseSegment_WithLegacyIdentifier_UsesSPrefixFormat()
        {
            // Note: segments use 'S' prefix, nodes use 'N' prefix.
            var definition = new FuseTurntable { LegacyIdentifier = "Robinson" };

            Assert.Equal("SRobinsonRoundhouseSegment2",
                FuseTurntableIds.GetRoundhouseSegmentId("yard-t1", 2, definition));
        }
    }
}
