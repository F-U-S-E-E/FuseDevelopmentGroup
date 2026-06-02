using System;
using System.IO;
using Fuse.Core.Authoring;
using Fuse.Core.Model;
using Fuse.Core.Serialization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Fuse.Core.Tests;

/// <summary>
/// Phase 2 gate: a track graph built via <see cref="TrackOps"/> must save to
/// <c>*.fuse.json</c> and reload cleanly (no data loss, stable JSON).
/// </summary>
public class TrackRoundTripTests
{
    [Fact]
    public void Build_Save_Reload_Is_Clean()
    {
        var def = new FuseModDefinition { Id = "fuse.test.track", Name = "Track Round-Trip" };
        TrackOps.AddNode(def.Tracks, "n1", new FuseVector3(100.5f, 0f, 200.25f), new FuseVector3(0f, 45f, 0f));
        TrackOps.AddNode(def.Tracks, "n2", new FuseVector3(300.5f, 10.5f, -50.25f), new FuseVector3(-2.5f, 135f, 0f));
        TrackOps.ConnectSegment(def.Tracks, "s1", "n1", "n2", trackClass: "main", style: "standard", speedLimit: 45);

        var dir = Path.Combine(Path.GetTempPath(), "fuse-trackrt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "track.fuse.json");
            FuseCoreSerializer.SaveJson(def, path);

            var reloaded = FuseCoreSerializer.Load(path);

            Assert.Equal(2, reloaded.Tracks.Nodes.Count);
            Assert.Single(reloaded.Tracks.Segments);
            Assert.Equal(100.5f, reloaded.Tracks.Nodes["n1"].Position.x);
            Assert.Equal(200.25f, reloaded.Tracks.Nodes["n1"].Position.z);
            Assert.Equal(45f, reloaded.Tracks.Nodes["n1"].Rotation.y);
            Assert.Equal("n1", reloaded.Tracks.Segments["s1"].StartNodeId);
            Assert.Equal("n2", reloaded.Tracks.Segments["s1"].EndNodeId);
            Assert.Equal(45, reloaded.Tracks.Segments["s1"].SpeedLimit);

            // Stable JSON: re-serializing the reloaded document is a fixed point.
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
