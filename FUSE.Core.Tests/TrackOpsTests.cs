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
}
