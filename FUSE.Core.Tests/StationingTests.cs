using System;
using System.Linq;
using Fuse.Core.Authoring;
using Fuse.Core.Geometry;
using Fuse.Core.Model;
using Xunit;

namespace Fuse.Core.Tests;

public class StationingTests
{
    [Fact]
    public void Straight_Chain_Station_Follows_Connected_Length()
    {
        var tracks = new FuseTrackDefinition();
        var gen = TrackGenerators.Straight(0, 0, 0, 90.0, 100.0, 0.0, 4); // 5 collinear nodes, length 100
        var (nodeIds, _) = TrackGenerators.Commit(tracks, gen);

        var stations = Stationing.PathStations(tracks, nodeIds);
        Assert.Equal(nodeIds.Count, stations.Length);
        Assert.Equal(0.0, stations[0]);
        for (var i = 1; i < stations.Length; i++)
        {
            Assert.True(stations[i] > stations[i - 1]); // monotonic
        }

        // On a straight chain, summed bezier segment length == polyline length == the generated 100 m.
        var xz = nodeIds.Select(id =>
        {
            var p = tracks.Nodes[id].Position;
            return ((double)p.x, (double)p.z);
        }).ToArray();
        Assert.True(Math.Abs(Stationing.PathLength(tracks, nodeIds) - Alignment.PolylineLength(xz)) < 1e-6);
        Assert.True(Math.Abs(Stationing.PathLength(tracks, nodeIds) - 100.0) < 1e-6);
    }

    [Fact]
    public void ShortestPath_Picks_Shorter_Connected_Route()
    {
        var t = new FuseTrackDefinition();
        TrackOps.AddNode(t, "A", new FuseVector3(0, 0, 0), default);
        TrackOps.AddNode(t, "B", new FuseVector3(0, 0, 50), default);
        TrackOps.AddNode(t, "D", new FuseVector3(0, 0, 100), default);
        TrackOps.AddNode(t, "C", new FuseVector3(500, 0, 50), default); // long detour
        TrackOps.ConnectSegment(t, "s1", "A", "B");
        TrackOps.ConnectSegment(t, "s2", "B", "D");
        TrackOps.ConnectSegment(t, "s3", "A", "C");
        TrackOps.ConnectSegment(t, "s4", "C", "D");

        Assert.Equal(new[] { "A", "B", "D" }, Stationing.ShortestPath(t, "A", "D"));
    }

    [Fact]
    public void ShortestPath_Null_When_Disconnected()
    {
        var t = new FuseTrackDefinition();
        TrackOps.AddNode(t, "A", new FuseVector3(0, 0, 0), default);
        TrackOps.AddNode(t, "B", new FuseVector3(0, 0, 50), default);
        Assert.Null(Stationing.ShortestPath(t, "A", "B"));
    }
}
