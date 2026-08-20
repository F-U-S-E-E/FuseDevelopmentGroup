using System;
using System.IO;
using Fuse.Core.Authoring;
using Fuse.Core.Model;
using Fuse.ExternalEditor.Services;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

/// <summary>The Phase 8 headline: import a legacy mod → edit → save as FUSE.</summary>
public class LegacyImportServiceTests
{
    [Fact]
    public void Convert_Legacy_Mod_Writes_Fuse_Package_That_Loads_And_Round_Trips()
    {
        var root = Path.Combine(Path.GetTempPath(), "fuse-legacy-" + Guid.NewGuid().ToString("N"));
        var src = Path.Combine(root, "MyLegacyMod");
        var outDir = Path.Combine(root, "converted");
        Directory.CreateDirectory(src);
        try
        {
            File.WriteAllText(Path.Combine(src, "Definition.json"),
                "{\"id\":\"my.legacy.mod\",\"name\":\"My Legacy Mod\",\"version\":\"1.2.3\",\"author\":\"me\"}");
            File.WriteAllText(Path.Combine(src, "data.json"),
                "{\"tracks\":{\"nodes\":{\"n_legacy\":{\"position\":{\"x\":10,\"y\":20,\"z\":30},\"rotation\":{\"x\":0,\"y\":0,\"z\":0}}}}}");

            var result = new LegacyImportService().Convert(src, outDir);

            Assert.True(result.Success, string.Join(" | ", result.Messages));
            Assert.NotEmpty(result.WrittenFragments);
            Assert.True(File.Exists(Path.Combine(outDir, "Info.json")));
            Assert.NotNull(result.FirstFragmentPath);
            Assert.True(File.Exists(result.FirstFragmentPath!));

            // Loads as a FUSE package; can be edited and saved back out.
            var projects = new ProjectService();
            var def = projects.Load(result.FirstFragmentPath!);
            Assert.False(string.IsNullOrEmpty(def.Id));

            TrackOps.AddNode(def.Tracks, "n_test", new FuseVector3(1, 2, 3), default);
            var editedPath = Path.Combine(outDir, "edited.fuse.json");
            projects.Save(def, editedPath);
            Assert.True(projects.Load(editedPath).Tracks.Nodes.ContainsKey("n_test"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Convert_Reports_Failure_For_Missing_Source()
    {
        var result = new LegacyImportService().Convert(
            Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N")), Path.GetTempPath());
        Assert.False(result.Success);
        Assert.NotEmpty(result.Messages);
    }
}
