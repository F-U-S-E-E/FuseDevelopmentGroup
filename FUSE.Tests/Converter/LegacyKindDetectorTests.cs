using System;
using System.IO;
using FUSE.Converter.Conversion;
using Xunit;

namespace FUSE.Tests.Converter
{
    /// <summary>
    /// Coverage for the legacy-package kind detector — does it pick
    /// the right kind for asset packs, map tile folders, audio
    /// packs, and plain route data?
    /// </summary>
    public sealed class LegacyKindDetectorTests : IDisposable
    {
        private readonly string _workspace;

        public LegacyKindDetectorTests()
        {
            _workspace = Path.Combine(Path.GetTempPath(), "fuse-kind-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workspace);
        }

        public void Dispose()
        {
            try { Directory.Delete(_workspace, recursive: true); } catch { }
        }

        // ------------------------------------------------------------------
        // DetectKind dispatcher
        // ------------------------------------------------------------------

        [Fact]
        public void DetectKind_honours_explicit_request()
        {
            var folder = Path.Combine(_workspace, "any");
            Directory.CreateDirectory(folder);
            Assert.Equal("audio", LegacyKindDetector.DetectKind(folder, "audio"));
        }

        [Fact]
        public void DetectKind_returns_route_for_legacy_route_data()
        {
            var folder = Path.Combine(_workspace, "route");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "tracks.json"),
                "{ \"tracks\": { \"nodes\": {} } }");
            Assert.Equal("route", LegacyKindDetector.DetectKind(folder, "auto"));
        }

        [Fact]
        public void DetectKind_returns_asset_for_asset_pack_folder()
        {
            var folder = Path.Combine(_workspace, "asset-pack");
            Directory.CreateDirectory(folder);
            // Three required files for an asset pack — empty content
            // is fine, only existence matters.
            File.WriteAllText(Path.Combine(folder, "bundle"), "x");
            File.WriteAllText(Path.Combine(folder, "Catalog.json"), "{}");
            File.WriteAllText(Path.Combine(folder, "Definitions.json"), "{}");

            Assert.Equal("asset", LegacyKindDetector.DetectKind(folder, "auto"));
        }

        [Fact]
        public void DetectKind_returns_route_for_map_tile_folder()
        {
            var folder = Path.Combine(_workspace, "tiles");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "tile-0-0.data"), "binary");
            // map-tile folder reports as route (route covers tiles).
            Assert.Equal("route", LegacyKindDetector.DetectKind(folder, "auto"));
        }

        [Fact]
        public void DetectKind_returns_audio_for_horns_layered_json()
        {
            var folder = Path.Combine(_workspace, "audio");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "horns.json"),
                "[ { \"layers\": [ \"a.wav\" ] } ]");
            Assert.Equal("audio", LegacyKindDetector.DetectKind(folder, "auto"));
        }

        [Fact]
        public void DetectKind_returns_unknown_for_empty_folder()
        {
            var folder = Path.Combine(_workspace, "empty");
            Directory.CreateDirectory(folder);
            Assert.Equal("unknown", LegacyKindDetector.DetectKind(folder, "auto"));
        }

        [Fact]
        public void DetectKind_returns_archive_for_zip()
        {
            // Just touch the .zip file (no need to be a real archive
            // for the kind detection — it's a filename-only check).
            var path = Path.Combine(_workspace, "package.zip");
            File.WriteAllText(path, "stub");
            Assert.Equal("archive", LegacyKindDetector.DetectKind(path, "auto"));
        }

        // ------------------------------------------------------------------
        // DetectAudioJson direct
        // ------------------------------------------------------------------

        [Fact]
        public void DetectAudioJson_returns_horns_for_layered_array()
        {
            var path = Path.Combine(_workspace, "horns.json");
            File.WriteAllText(path, "[ { \"layers\": [\"a.wav\"] } ]");
            Assert.Equal("horns", LegacyKindDetector.DetectAudioJson(path));
        }

        [Fact]
        public void DetectAudioJson_returns_whistles_for_clip_entries()
        {
            var path = Path.Combine(_workspace, "whistles.json");
            File.WriteAllText(path, "[ { \"clip\": \"a.wav\" } ]");
            Assert.Equal("whistles", LegacyKindDetector.DetectAudioJson(path));
        }

        [Fact]
        public void DetectAudioJson_returns_bells_when_filename_or_index_times()
        {
            var path = Path.Combine(_workspace, "hellsbells.json");
            File.WriteAllText(path, "[ { \"unrelated\": 1 } ]");
            Assert.Equal("bells", LegacyKindDetector.DetectAudioJson(path));
        }

        [Fact]
        public void DetectAudioJson_returns_empty_for_non_array()
        {
            var path = Path.Combine(_workspace, "wrong.json");
            File.WriteAllText(path, "{ \"clip\": \"a\" }");
            Assert.Equal(string.Empty, LegacyKindDetector.DetectAudioJson(path));
        }

        // ------------------------------------------------------------------
        // FindAssetPackSources
        // ------------------------------------------------------------------

        [Fact]
        public void FindAssetPackSources_returns_dot_for_self_pack()
        {
            var folder = Path.Combine(_workspace, "self");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "bundle"), "x");
            File.WriteAllText(Path.Combine(folder, "Catalog.json"), "{}");
            File.WriteAllText(Path.Combine(folder, "Definitions.json"), "{}");

            var sources = LegacyKindDetector.FindAssetPackSources(folder);
            Assert.Single(sources);
            Assert.Equal(".", sources[0]);
        }

        [Fact]
        public void FindAssetPackSources_detects_SCAssetPacks_subfolder()
        {
            var folder = Path.Combine(_workspace, "sc");
            Directory.CreateDirectory(folder);
            var packFolder = Path.Combine(folder, "SCAssetPacks", "Pack1");
            Directory.CreateDirectory(packFolder);
            File.WriteAllText(Path.Combine(packFolder, "bundle"), "x");
            File.WriteAllText(Path.Combine(packFolder, "Catalog.json"), "{}");
            File.WriteAllText(Path.Combine(packFolder, "Definitions.json"), "{}");

            Assert.Contains("SCAssetPacks", LegacyKindDetector.FindAssetPackSources(folder));
        }

        // ------------------------------------------------------------------
        // FindMapTileSources
        // ------------------------------------------------------------------

        [Fact]
        public void FindMapTileSources_returns_root_when_root_has_tiles()
        {
            var folder = Path.Combine(_workspace, "tiles");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "t.data"), "x");
            var result = LegacyKindDetector.FindMapTileSources(folder);
            Assert.Single(result);
        }

        [Fact]
        public void FindMapTileSources_picks_up_Maps_subfolders()
        {
            var folder = Path.Combine(_workspace, "with-maps");
            Directory.CreateDirectory(folder);
            var mapsSub = Path.Combine(folder, "Maps", "Region");
            Directory.CreateDirectory(mapsSub);
            File.WriteAllText(Path.Combine(mapsSub, "tile.data"), "x");

            var result = LegacyKindDetector.FindMapTileSources(folder);
            Assert.Contains(result, p => p.EndsWith("Region", StringComparison.Ordinal));
        }
    }
}
