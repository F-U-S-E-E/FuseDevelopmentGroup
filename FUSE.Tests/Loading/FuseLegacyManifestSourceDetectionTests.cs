using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    /// <summary>
    /// The runtime legacy-package reader filters candidate source files by their
    /// top-level keys. RailLoader-era packages keep <c>nodes</c> / <c>segments</c> /
    /// <c>spans</c> at the document root instead of under <c>tracks</c> (issue #210);
    /// the converters merge those, so the filter must admit them too — otherwise a
    /// file whose only payload is a root-level dictionary is dropped before
    /// conversion ever runs.
    /// </summary>
    public sealed class FuseLegacyManifestSourceDetectionTests : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "fuse-legacy-source-detection-" + Guid.NewGuid().ToString("N"));

        public FuseLegacyManifestSourceDetectionTests()
        {
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"Test cleanup could not delete '{_root}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"Test cleanup could not delete '{_root}': {ex.Message}");
            }
        }

        [Theory]
        [InlineData("segments", "{\"segments\":{\"S1\":{\"startId\":\"N1\",\"endId\":\"N2\"}}}")]
        [InlineData("nodes", "{\"nodes\":{\"N1\":{\"position\":{\"x\":1,\"y\":2,\"z\":3}}}}")]
        [InlineData("spans", "{\"spans\":{\"SP1\":{\"lower\":{\"segmentId\":\"S1\",\"end\":\"Start\"},\"upper\":{\"segmentId\":\"S1\",\"end\":\"End\"}}}}")]
        public void RootLevelTrackDictionary_IsRecognisedAsALegacyDataSource(string key, string json)
        {
            var folder = CreatePackage("Root." + key, json);

            var found = FuseLegacyDataConverter.TryReadLegacyManifest(folder, out var manifest);

            Assert.True(found, $"a legacy file whose only top-level key is '{key}' must be admitted");
            Assert.NotNull(manifest);
            Assert.Contains(manifest.SourceFiles, path => Path.GetFileName(path) == "game-graph.json");
        }

        [Fact]
        public void UnrelatedTopLevelKeys_AreStillIgnored()
        {
            var folder = CreatePackage("Root.Unrelated", "{\"somethingElse\":{\"a\":1}}");

            var found = FuseLegacyDataConverter.TryReadLegacyManifest(folder, out _);

            Assert.False(found);
        }

        private string CreatePackage(string id, string graphJson)
        {
            var folder = Path.Combine(_root, id);
            Directory.CreateDirectory(folder);
            File.WriteAllText(
                Path.Combine(folder, "Definition.json"),
                "{\"id\":\"" + id + "\",\"name\":\"" + id + "\",\"version\":\"1.0.0\",\"author\":\"tester\"}");
            File.WriteAllText(Path.Combine(folder, "game-graph.json"), graphJson);
            return folder;
        }
    }
}
