using System.Collections.Generic;
using Fuse.Core.Authoring;
using Fuse.Core.Geometry;
using Fuse.Core.Model;
using Xunit;

namespace Fuse.Core.Tests;

/// <summary>
/// Id assignment by <see cref="TrackGenerators.Commit"/>. The batched allocator must keep
/// the original first-free-slot semantics (gaps filled, then sequential) while building the
/// taken-id set only once per commit instead of once per minted id.
/// </summary>
public class TrackGeneratorsCommitTests
{
    [Fact]
    public void Commit_Into_Empty_Definition_Assigns_Sequential_Ids()
    {
        var tracks = new FuseTrackDefinition();
        var generated = TrackGenerators.Straight(0, 0, 0, 0, length: 100, nSegments: 4);

        var (nodeIds, segmentIds) = TrackGenerators.Commit(tracks, generated);

        Assert.Equal(new[] { "n_0001", "n_0002", "n_0003", "n_0004", "n_0005" }, nodeIds);
        Assert.Equal(new[] { "s_0001", "s_0002", "s_0003", "s_0004" }, segmentIds);
    }

    [Fact]
    public void Commit_Fills_Gaps_Then_Continues_Past_Existing_Ids()
    {
        var tracks = new FuseTrackDefinition();
        TrackOps.AddNode(tracks, "n_0001", default, default);
        TrackOps.AddNode(tracks, "n_0003", default, default);
        TrackOps.ConnectSegment(tracks, "s_0002", "n_0001", "n_0003");

        var generated = TrackGenerators.Straight(0, 0, 0, 0, length: 60, nSegments: 2);

        var (nodeIds, segmentIds) = TrackGenerators.Commit(tracks, generated);

        Assert.Equal(new[] { "n_0002", "n_0004", "n_0005" }, nodeIds);
        Assert.Equal(new[] { "s_0001", "s_0003" }, segmentIds);
    }

    [Fact]
    public void Commit_Wires_Segments_To_Minted_Node_Ids()
    {
        var tracks = new FuseTrackDefinition();
        TrackOps.AddNode(tracks, "n_0001", default, default);

        var generated = TrackGenerators.Turnout(0, 0, 0, 0);

        var (nodeIds, segmentIds) = TrackGenerators.Commit(tracks, generated);

        Assert.Equal(generated.Nodes.Count, nodeIds.Count);
        Assert.Equal(generated.Segments.Count, segmentIds.Count);
        for (var i = 0; i < generated.Segments.Count; i++)
        {
            var seg = tracks.Segments[segmentIds[i]];
            Assert.Equal(nodeIds[generated.Segments[i].StartIndex], seg.StartNodeId);
            Assert.Equal(nodeIds[generated.Segments[i].EndIndex], seg.EndNodeId);
        }
    }

    [Fact]
    public void Commit_Of_Large_Chain_Assigns_Unique_Sequential_Ids()
    {
        var tracks = new FuseTrackDefinition();
        var generated = TrackGenerators.Straight(0, 0, 0, 0, length: 10000, nSegments: 2000);

        var (nodeIds, segmentIds) = TrackGenerators.Commit(tracks, generated);

        Assert.Equal(2001, new HashSet<string>(nodeIds).Count);
        Assert.Equal(2000, new HashSet<string>(segmentIds).Count);
        Assert.Equal("n_0001", nodeIds[0]);
        Assert.Equal("n_2001", nodeIds[2000]);
        Assert.Equal("s_2000", segmentIds[1999]);
    }
}
