using System.Collections.Generic;
using Fuse.Core.Authoring;
using Fuse.Core.Model;
using Xunit;

namespace Fuse.Core.Tests;

public class WorldOpsTests
{
    [Fact]
    public void New_Scenery_Single_Shot_Fills_First_Free_Slot()
    {
        var world = new FuseWorldDefinition();

        var id1 = WorldOps.NewSceneryId(world);
        WorldOps.AddScenery(world, id1, "asset", default, default);
        var id2 = WorldOps.NewSceneryId(world);

        Assert.Equal("scn_0001", id1);
        Assert.Equal("scn_0002", id2);
        Assert.False(world.Scenery.ContainsKey(id2));
    }

    [Fact]
    public void Batch_Scenery_Ids_Match_Repeated_Single_Shot_Calls()
    {
        var single = new FuseWorldDefinition();
        var batch = new FuseWorldDefinition();
        foreach (var id in new[] { "scn_0001", "scn_0003", "scn_0004", "unrelated" })
        {
            WorldOps.AddScenery(single, id, "asset", default, default);
            WorldOps.AddScenery(batch, id, "asset", default, default);
        }

        var takenIds = new HashSet<string>(batch.Scenery.Keys);
        var nextIndex = 1;
        var minted = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var expected = WorldOps.NewSceneryId(single);
            WorldOps.AddScenery(single, expected, "asset", default, default);

            var actual = WorldOps.NewSceneryId(takenIds, ref nextIndex);
            WorldOps.AddScenery(batch, actual, "asset", default, default);

            Assert.Equal(expected, actual);
            minted.Add(actual);
        }

        // Gap at scn_0002 is filled first, then the sequence continues past the taken ids.
        Assert.Equal(new[] { "scn_0002", "scn_0005", "scn_0006", "scn_0007", "scn_0008" }, minted);
    }

    [Fact]
    public void Batch_Spliney_Ids_Use_Prefix_And_Update_Set()
    {
        var takenIds = new HashSet<string> { "spl_0001" };
        var nextIndex = 1;

        var first = WorldOps.NewSplineyId(takenIds, ref nextIndex);
        var second = WorldOps.NewSplineyId(takenIds, ref nextIndex);

        Assert.Equal("spl_0002", first);
        Assert.Equal("spl_0003", second);
        Assert.Contains("spl_0002", takenIds);
        Assert.Contains("spl_0003", takenIds);
    }
}
