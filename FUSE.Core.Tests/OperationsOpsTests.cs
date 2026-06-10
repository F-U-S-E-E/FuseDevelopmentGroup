using System.Collections.Generic;
using Fuse.Core.Authoring;
using Fuse.Core.Model;
using Xunit;

namespace Fuse.Core.Tests;

public class OperationsOpsTests
{
    [Fact]
    public void New_Load_Single_Shot_Fills_First_Free_Slot()
    {
        var operations = new FuseOperationsDefinition();

        var id1 = OperationsOps.NewLoadId(operations);
        OperationsOps.AddLoad(operations, id1, "Coal");
        var id2 = OperationsOps.NewLoadId(operations);

        Assert.Equal("load_0001", id1);
        Assert.Equal("load_0002", id2);
        Assert.False(operations.Loads.ContainsKey(id2));
    }

    [Fact]
    public void Batch_Industry_Ids_Match_Repeated_Single_Shot_Calls()
    {
        var single = new FuseOperationsDefinition();
        var batch = new FuseOperationsDefinition();
        foreach (var id in new[] { "ind_0001", "ind_0003", "ind_0004", "unrelated" })
        {
            OperationsOps.AddIndustry(single, id, "Mine");
            OperationsOps.AddIndustry(batch, id, "Mine");
        }

        var takenIds = new HashSet<string>(batch.Industries.Keys);
        var nextIndex = 1;
        var minted = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var expected = OperationsOps.NewIndustryId(single);
            OperationsOps.AddIndustry(single, expected, "Mine");

            var actual = OperationsOps.NewIndustryId(takenIds, ref nextIndex);
            OperationsOps.AddIndustry(batch, actual, "Mine");

            Assert.Equal(expected, actual);
            minted.Add(actual);
        }

        // Gap at ind_0002 is filled first, then the sequence continues past the taken ids.
        Assert.Equal(new[] { "ind_0002", "ind_0005", "ind_0006", "ind_0007", "ind_0008" }, minted);
    }

    [Fact]
    public void Batch_Load_Ids_Use_Prefix_And_Update_Set()
    {
        var takenIds = new HashSet<string> { "load_0001" };
        var nextIndex = 1;

        var first = OperationsOps.NewLoadId(takenIds, ref nextIndex);
        var second = OperationsOps.NewLoadId(takenIds, ref nextIndex);

        Assert.Equal("load_0002", first);
        Assert.Equal("load_0003", second);
        Assert.Contains("load_0002", takenIds);
        Assert.Contains("load_0003", takenIds);
    }
}
