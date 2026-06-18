using System.Linq;
using Fuse.Core.Authoring;
using Fuse.Core.Model;
using Fuse.ExternalEditor.ViewModels;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

public class EntityTreeTests
{
    [Fact]
    public void Build_Creates_Tracks_World_Operations_With_Entities()
    {
        var def = new FuseModDefinition { Id = "t", Name = "t" };
        TrackOps.AddNode(def.Tracks, "n1", new FuseVector3(0, 0, 0), default);
        TrackOps.AddNode(def.Tracks, "n2", new FuseVector3(1, 0, 0), default);
        TrackOps.ConnectSegment(def.Tracks, "s1", "n1", "n2");
        WorldOps.AddScenery(def.World, "scn1", "Tree", new FuseVector3(0, 0, 0), new FuseVector3(0, 0, 0));
        OperationsOps.AddIndustry(def.Operations, "ind1", "Mine");
        OperationsOps.AddLoad(def.Operations, "load1", "Coal");

        var vm = new EntityTreeViewModel();
        vm.Build(def);

        Assert.Equal(3, vm.Roots.Count);

        var tracks = vm.Roots[0];
        Assert.Equal("tracks", tracks.Kind);
        var nodeGroup = tracks.Children.First(c => c.Kind == "node-group");
        Assert.Equal(2, nodeGroup.Children.Count);
        Assert.Contains(nodeGroup.Children, c => c.EntityId == "n1" && c.Kind == "node");
        Assert.Single(tracks.Children.First(c => c.Kind == "segment-group").Children);

        var world = vm.Roots[1];
        Assert.Equal("world", world.Kind);
        Assert.Single(world.Children.First(c => c.Kind == "scenery-group").Children);

        var ops = vm.Roots[2];
        Assert.Equal("operations", ops.Kind);
        Assert.Single(ops.Children.First(c => c.Kind == "industry-group").Children);
        Assert.Single(ops.Children.First(c => c.Kind == "load-group").Children);
    }
}
