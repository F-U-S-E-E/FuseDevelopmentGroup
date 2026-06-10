using System.Collections.Generic;
using Fuse.Core.Authoring;
using Fuse.Core.Model;
using Xunit;

namespace Fuse.Core.Tests;

public class TrackOpsTests
{
    [Fact]
    public void Add_Connect_Then_DeleteNode_Cascades_Segments()
    {
        var tracks = new FuseTrackDefinition();
        TrackOps.AddNode(tracks, "n1", new FuseVector3(0, 0, 0), new FuseVector3(0, 0, 0));
        TrackOps.AddNode(tracks, "n2", new FuseVector3(100, 0, 0), new FuseVector3(0, 90, 0));
        TrackOps.ConnectSegment(tracks, "s1", "n1", "n2");

        Assert.Equal(2, tracks.Nodes.Count);
        Assert.Single(tracks.Segments);
        Assert.Equal(1, TrackOps.NodeValency(tracks, "n1"));

        var removed = TrackOps.DeleteNode(tracks, "n1");

        Assert.Equal(1, removed);
        Assert.False(tracks.Nodes.ContainsKey("n1"));
        Assert.Empty(tracks.Segments);
        Assert.Equal(-1, TrackOps.DeleteNode(tracks, "missing"));
    }

    [Fact]
    public void MoveNode_And_MoveGroup_Translate_Positions()
    {
        var tracks = new FuseTrackDefinition();
        TrackOps.AddNode(tracks, "n1", new FuseVector3(1, 2, 3), default);
        TrackOps.AddNode(tracks, "n2", new FuseVector3(0, 0, 0), default);

        TrackOps.MoveNode(tracks, "n1", new FuseVector3(10, 20, 30));
        Assert.Equal(10f, tracks.Nodes["n1"].Position.x);

        var moved = TrackOps.MoveGroup(tracks, new[] { "n1", "n2" }, 5, 0, -5);
        Assert.Equal(2, moved);
        Assert.Equal(15f, tracks.Nodes["n1"].Position.x);
        Assert.Equal(-5f, tracks.Nodes["n2"].Position.z);
    }

    [Fact]
    public void SetSegmentProps_Updates_Only_Given_Fields()
    {
        var tracks = new FuseTrackDefinition();
        TrackOps.ConnectSegment(tracks, "s1", "a", "b");

        TrackOps.SetSegmentProps(tracks, "s1", trackClass: "branch", speedLimit: 25);

        Assert.Equal("branch", tracks.Segments["s1"].TrackClass);
        Assert.Equal(25, tracks.Segments["s1"].SpeedLimit);
        Assert.Equal("standard", tracks.Segments["s1"].Style); // untouched
    }

    [Fact]
    public void New_Ids_Are_Unique()
    {
        var tracks = new FuseTrackDefinition();
        var id1 = TrackOps.NewNodeId(tracks);
        TrackOps.AddNode(tracks, id1, default, default);
        var id2 = TrackOps.NewNodeId(tracks);

        Assert.NotEqual(id1, id2);
        Assert.False(tracks.Nodes.ContainsKey(id2));
    }

    [Fact]
    public void Batch_Ids_Match_Repeated_Single_Shot_Calls()
    {
        var single = new FuseTrackDefinition();
        var batch = new FuseTrackDefinition();
        foreach (var id in new[] { "n_0001", "n_0003", "n_0004", "unrelated" })
        {
            TrackOps.AddNode(single, id, default, default);
            TrackOps.AddNode(batch, id, default, default);
        }

        var takenIds = new HashSet<string>(batch.Nodes.Keys);
        var nextIndex = 1;
        var minted = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var expected = TrackOps.NewNodeId(single);
            TrackOps.AddNode(single, expected, default, default);

            var actual = TrackOps.NewNodeId(takenIds, ref nextIndex);
            TrackOps.AddNode(batch, actual, default, default);

            Assert.Equal(expected, actual);
            minted.Add(actual);
        }

        // Gap at n_0002 is filled first, then the sequence continues past the taken ids.
        Assert.Equal(new[] { "n_0002", "n_0005", "n_0006", "n_0007", "n_0008" }, minted);
    }

    [Fact]
    public void Batch_Segment_Ids_Use_Segment_Prefix_And_Update_Set()
    {
        var takenIds = new HashSet<string> { "s_0001" };
        var nextIndex = 1;

        var first = TrackOps.NewSegmentId(takenIds, ref nextIndex);
        var second = TrackOps.NewSegmentId(takenIds, ref nextIndex);

        Assert.Equal("s_0002", first);
        Assert.Equal("s_0003", second);
        Assert.Contains("s_0002", takenIds);
        Assert.Contains("s_0003", takenIds);
    }

    [Fact]
    public void Batch_Ids_Widen_Past_9999()
    {
        var takenIds = new HashSet<string>();
        for (var i = 1; i <= 9999; i++)
        {
            takenIds.Add($"n_{i:D4}");
        }

        var nextIndex = 1;

        Assert.Equal("n_10000", TrackOps.NewNodeId(takenIds, ref nextIndex));
        Assert.Equal("n_10001", TrackOps.NewNodeId(takenIds, ref nextIndex));
        Assert.Contains("n_10001", takenIds);
    }

    [Fact]
    public void Batch_Cursor_Below_One_Is_Clamped()
    {
        var takenIds = new HashSet<string>();
        var nextIndex = -3;

        Assert.Equal("n_0001", TrackOps.NewNodeId(takenIds, ref nextIndex));
        Assert.Equal(1, nextIndex);
    }
}
