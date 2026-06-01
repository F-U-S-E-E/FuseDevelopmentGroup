using System;
using System.IO;
using System.Linq;
using FUSE.Converter;
using FUSE.Converter.Conversion;
using FUSE.Converter.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Converter
{
    /// <summary>
    /// Coverage for the legacy audio converter — loose package
    /// conversion (with mixinto-driven dispatch) and asset-pack
    /// wrapper conversion.
    /// </summary>
    public sealed class LegacyAudioConverterTests : IDisposable
    {
        private readonly string _workspace;

        public LegacyAudioConverterTests()
        {
            _workspace = Path.Combine(Path.GetTempPath(), "fuse-audio-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workspace);
        }

        public void Dispose()
        {
            try { Directory.Delete(_workspace, recursive: true); } catch { }
        }

        // ------------------------------------------------------------------
        // Slug + FileRefValue
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("Steam Whistle", "fallback", "steam-whistle")]
        [InlineData("  ", "fallback", "fallback")]
        [InlineData("", "fb", "fb")]
        [InlineData(null, "fb", "fb")]
        public void Slug_normalises_or_falls_back(string input, string fallback, string expected)
        {
            Assert.Equal(expected, LegacyAudioConverter.Slug(input, fallback));
        }

        [Theory]
        [InlineData("file(my.wav)", "my.wav")]
        [InlineData("file(\"quoted.wav\")", "quoted.wav")]
        [InlineData("bare.wav", "bare.wav")]
        public void FileRefValue_strips_wrapping(string input, string expected)
        {
            Assert.Equal(expected, LegacyAudioConverter.FileRefValue(input));
        }

        // ------------------------------------------------------------------
        // ConvertLoosePackage
        // ------------------------------------------------------------------

        [Fact]
        public void ConvertLoosePackage_emits_audio_fragment_for_whistles_mixinto()
        {
            // Set up a loose audio package: Definition.json has a
            // whistles mixinto pointing to whistles.json, which has
            // one entry referencing a wav file in the source root.
            var source = Path.Combine(_workspace, "whistle.mod");
            Directory.CreateDirectory(source);

            File.WriteAllText(Path.Combine(source, "Definition.json"),
                "{ \"id\": \"steamPak\", \"name\": \"Steam Pak\", \"version\": \"1.0\", \"author\": \"alex\", " +
                "\"mixintos\": { \"Whistles\": \"file(whistles.json)\" } }");

            File.WriteAllText(Path.Combine(source, "whistles.json"),
                "[ { \"name\": \"Big Whistle\", \"clip\": \"file(clips/big.wav)\" } ]");

            // Create a stub wav so the copy step succeeds.
            Directory.CreateDirectory(Path.Combine(source, "clips"));
            File.WriteAllText(Path.Combine(source, "clips", "big.wav"), "stub");

            var output = Path.Combine(_workspace, "whistle.mod.FUSE");
            var result = LegacyAudioConverter.ConvertAudioMod(source, output);

            Assert.True(result.Success);
            // audio.fuse.json should exist with the whistle entry.
            var fragmentPath = Path.Combine(output, "audio.fuse.json");
            Assert.True(File.Exists(fragmentPath));
            var rail = JObject.Parse(File.ReadAllText(fragmentPath));
            var whistles = (JObject)((JObject)rail["audio"])["whistles"];
            Assert.Single(whistles);
            // Entry id should be "<modId>.whistle.<slug>".
            Assert.Contains("steamPak.whistle.big-whistle", whistles.Properties().Select(p => p.Name));

            // Audio file got copied.
            Assert.True(File.Exists(Path.Combine(output, "Audio", "whistles", "big.wav")));
        }

        [Fact]
        public void ConvertLoosePackage_warns_when_mixinto_target_missing()
        {
            var source = Path.Combine(_workspace, "broken");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "Definition.json"),
                "{ \"id\": \"x\", \"mixintos\": { \"horns\": \"file(missing.json)\" } }");

            var result = LegacyAudioConverter.ConvertAudioMod(source, Path.Combine(_workspace, "broken.FUSE"));

            Assert.Contains(result.Report, r =>
                r.Concept == "audio-mixinto-missing" &&
                r.Message.Contains("missing.json"));
        }

        [Fact]
        public void ConvertLoosePackage_warns_when_audio_clip_missing()
        {
            var source = Path.Combine(_workspace, "stale-clip");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "Definition.json"),
                "{ \"id\": \"x\", \"mixintos\": { \"bells\": \"file(bells.json)\" } }");
            File.WriteAllText(Path.Combine(source, "bells.json"),
                "[ { \"name\": \"B\", \"file\": \"file(missing.wav)\", \"indexTimes\": [] } ]");

            var output = Path.Combine(_workspace, "stale.FUSE");
            var result = LegacyAudioConverter.ConvertAudioMod(source, output);

            Assert.Contains(result.Report, r =>
                r.Concept == "audio-clip-missing" &&
                r.Message.Contains("missing.wav"));
        }

        [Fact]
        public void ConvertLoosePackage_converts_horns_with_layered_keyframes()
        {
            var source = Path.Combine(_workspace, "horn.mod");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "Definition.json"),
                "{ \"id\": \"hornsPak\", \"mixintos\": { \"horns\": \"file(horns.json)\" } }");
            File.WriteAllText(Path.Combine(source, "horns.json"),
                "[ { \"name\": \"Air Horn\", \"layers\": [ " +
                "  { \"file\": \"file(layer1.wav)\", \"keyframes\": [ { \"t\": 0, \"value\": 1 }, { \"t\": 0.5, \"value\": 0.5 } ] } " +
                "] } ]");
            File.WriteAllText(Path.Combine(source, "layer1.wav"), "stub");

            var output = Path.Combine(_workspace, "horn.mod.FUSE");
            var result = LegacyAudioConverter.ConvertAudioMod(source, output);

            Assert.True(result.Success);
            var rail = JObject.Parse(File.ReadAllText(Path.Combine(output, "audio.fuse.json")));
            var horns = (JObject)((JObject)rail["audio"])["horns"];
            Assert.Single(horns);
            var entry = horns.Properties().First().Value as JObject;
            var layers = (JArray)entry["layers"];
            Assert.Single(layers);
            var keyframes = (JArray)((JObject)layers[0])["keyframes"];
            Assert.Equal(2, keyframes.Count);
        }

        // ------------------------------------------------------------------
        // ConvertPackage dispatcher
        // ------------------------------------------------------------------

        [Fact]
        public void ConvertPackage_routes_audio_kind_through_audio_converter()
        {
            var source = Path.Combine(_workspace, "horn.dispatch");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "Definition.json"),
                "{ \"id\": \"d\", \"mixintos\": { \"horns\": \"file(horns.json)\" } }");
            File.WriteAllText(Path.Combine(source, "horns.json"),
                "[ { \"name\": \"H\", \"layers\": [] } ]");

            var output = Path.Combine(_workspace, "horn.dispatch.FUSE");
            var result = FuseLegacyConverter.ConvertPackage(source, output);
            Assert.True(result.Success);
            // Should have used the audio path → audio.fuse.json on disk.
            Assert.True(File.Exists(Path.Combine(output, "audio.fuse.json")));
        }

        [Fact]
        public void ConvertPackage_routes_route_kind_through_route_converter()
        {
            // Plain route source (tracks-only) should produce a
            // *.fuse.json named after the source file.
            var source = Path.Combine(_workspace, "route.dispatch");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "tracks.json"),
                "{ \"tracks\": { \"nodes\": { \"n1\": { \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 } } } } }");

            var output = Path.Combine(_workspace, "route.dispatch.FUSE");
            var result = FuseLegacyConverter.ConvertPackage(source, output);
            Assert.True(result.Success);
            Assert.True(File.Exists(Path.Combine(output, "tracks.fuse.json")));
        }

        // ------------------------------------------------------------------
        // BuildAudioSkeleton
        // ------------------------------------------------------------------

        [Fact]
        public void BuildAudioSkeleton_carries_audio_subsection_plus_suppress_arrays()
        {
            var rail = LegacyAudioConverter.BuildAudioSkeleton("modX", "Mod X", "2.0", "alex");
            Assert.Equal("modX.FUSE.Audio", rail.Value<string>("id"));
            Assert.Equal("Mod X (FUSE Audio)", rail.Value<string>("name"));
            Assert.NotNull(rail["audio"]);
            Assert.NotNull(((JObject)rail["audio"])["whistles"]);
            Assert.NotNull(((JObject)rail["audio"])["horns"]);
            Assert.NotNull(((JObject)rail["audio"])["bells"]);
            // World carries the suppress arrays for the audio variant.
            var world = (JObject)rail["world"];
            Assert.NotNull(world["suppressBaseScenePaths"]);
            Assert.NotNull(world["suppressBaseTrackGroups"]);
            Assert.NotNull(world["suppressBaseAreas"]);
        }
    }
}
