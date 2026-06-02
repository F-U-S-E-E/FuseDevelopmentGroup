using System;
using System.IO;
using Fuse.Core.Authoring;
using Fuse.Core.Model;
using Fuse.Core.Serialization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Fuse.Core.Tests;

/// <summary>
/// Phase 4 gate: world + operations sections built via the ops helpers must
/// save to <c>*.fuse.json</c> and reload cleanly (no loss, stable JSON).
/// </summary>
public class WorldOpsRoundTripTests
{
    [Fact]
    public void World_And_Operations_RoundTrip_Clean()
    {
        var def = new FuseModDefinition { Id = "fuse.test.worldops", Name = "World/Ops Round-Trip" };

        WorldOps.AddScenery(def.World, "scn_1", "Trees/Oak", new FuseVector3(100.5f, 0f, 50.25f), new FuseVector3(0, 90, 0));
        WorldOps.AddSpliney(def.World, "spl_1", "road", new[]
        {
            new FuseSplineyPoint { Position = new FuseVector3(0, 0, 0), Rotation = new FuseVector3(0, 0, 0) },
            new FuseSplineyPoint { Position = new FuseVector3(10.5f, 0, 20.5f), Rotation = new FuseVector3(0, 45, 0) },
        });
        OperationsOps.AddLoad(def.Operations, "load_1", "Coal", "pounds");
        OperationsOps.AddIndustry(def.Operations, "ind_1", "Mine", areaId: "area_1");

        var dir = Path.Combine(Path.GetTempPath(), "fuse-worldops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "worldops.fuse.json");
            FuseCoreSerializer.SaveJson(def, path);
            var reloaded = FuseCoreSerializer.Load(path);

            Assert.Single(reloaded.World.Scenery);
            Assert.Equal("Trees/Oak", reloaded.World.Scenery["scn_1"].AssetIdentifier);
            Assert.Equal(100.5f, reloaded.World.Scenery["scn_1"].Position.x);
            Assert.Single(reloaded.World.Splineys);
            Assert.Equal(2, reloaded.World.Splineys["spl_1"].Points.Length);
            Assert.Single(reloaded.Operations.Loads);
            Assert.Equal("Coal", reloaded.Operations.Loads["load_1"].Name);
            Assert.Single(reloaded.Operations.Industries);
            Assert.Equal("Mine", reloaded.Operations.Industries["ind_1"].Name);

            var json1 = FuseCoreSerializer.ToJson(reloaded);
            var json2 = FuseCoreSerializer.ToJson(FuseCoreSerializer.FromJson(json1));
            Assert.True(JToken.DeepEquals(JObject.Parse(json1), JObject.Parse(json2)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
