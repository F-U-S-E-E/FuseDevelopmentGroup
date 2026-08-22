using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FUSE.Loading;
using FUSE.Authoring.Data;
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

        [Fact]
        public void ConditionalMixintoRequirement_IsAdvisoryLoadAfter_NotHardRequirement()
        {
            var folder = Path.Combine(_root, "Conditional.Mixinto");
            Directory.CreateDirectory(folder);
            File.WriteAllText(
                Path.Combine(folder, "Definition.json"),
                "{\"id\":\"Conditional.Mixinto\",\"mixintos\":{\"game-graph\":{" +
                "\"mixinto\":\"file(optional.json)\",\"requires\":[\"Optional.Base\"]}}}");
            File.WriteAllText(
                Path.Combine(folder, "optional.json"),
                "{\"nodes\":{\"N1\":{\"position\":{\"x\":1,\"y\":2,\"z\":3}}}}");

            var found = FuseLegacyDataConverter.TryReadLegacyManifest(folder, out var manifest);

            Assert.True(found);
            Assert.Empty(manifest.RequiredPackageIds);
            Assert.Contains("Optional.Base.FUSE", manifest.LoadAfter);
        }

        [Fact]
        public void LegacyConflictsWith_PreservesVersionBounds()
        {
            var folder = Path.Combine(_root, "Legacy.Conflict");
            Directory.CreateDirectory(folder);
            File.WriteAllText(
                Path.Combine(folder, "Definition.json"),
                "{\"id\":\"Legacy.Conflict\",\"conflictsWith\":[{" +
                "\"id\":\"Other.Route\",\"notBefore\":\"2.0\",\"notAfter\":\"3.0\"}]}");
            File.WriteAllText(
                Path.Combine(folder, "game-graph.json"),
                "{\"nodes\":{\"N1\":{\"position\":{\"x\":1,\"y\":2,\"z\":3}}}}");

            Assert.True(FuseLegacyDataConverter.TryReadLegacyManifest(folder, out var manifest));

            var conflict = Assert.Single(manifest.ConflictsWith);
            Assert.Equal("Other.Route", conflict.Id);
            Assert.Equal("2.0", conflict.NotBefore);
            Assert.Equal("3.0", conflict.NotAfter);
        }

        [Fact]
        public void SyntheticMapTileDefinition_IsNotTreatedAsGameGraphPatch()
        {
            var definition = new FuseModDefinition
            {
                Id = "Legacy.MapTiles",
                Tags = new[] { "legacy-converted" }
            };
            var loaded = new FuseLoadedMod(_root, "legacy://map-tiles", definition);

            Assert.False(FuseLegacyGameGraphCompatibility.ShouldExpand(loaded));
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
