using System;
using System.IO;
using FUSE.Converter;
using FUSE.Converter.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Converter
{
    /// <summary>
    /// Round-trip coverage for the first slice of the legacy-to-FUSE
    /// port: build a fake legacy mod folder on disk, run
    /// FuseLegacyConverter.ConvertMod, then verify the output folder
    /// contains the expected Info.json + *.fuse.json shape.
    /// </summary>
    public sealed class FuseLegacyConverterTests : IDisposable
    {
        private readonly string _workspace;

        public FuseLegacyConverterTests()
        {
            _workspace = Path.Combine(Path.GetTempPath(), "FuseLegacyConverterTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workspace);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true); }
            catch { /* best-effort */ }
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void ConvertMod_writes_info_json_and_fuse_fragment_with_track_data()
        {
            var modFolder = Path.Combine(_workspace, "legacy.mod");
            Directory.CreateDirectory(modFolder);

            File.WriteAllText(Path.Combine(modFolder, "Definition.json"),
                "{ \"id\": \"acme.legacy\", \"name\": \"Acme Legacy\", \"version\": \"1.2.3\", \"author\": \"alex\", " +
                "\"requires\": [{ \"id\": \"railloader\" }] }");
            File.WriteAllText(Path.Combine(modFolder, "tracks.json"),
                "{ \"tracks\": { " +
                "\"nodes\": { \"node-A\": { \"position\": { \"x\": 1, \"y\": 2, \"z\": 3 } } }, " +
                "\"segments\": { \"seg-1\": { \"startNodeId\": \"node-A\", \"endNodeId\": \"node-B\", \"speedLimit\": 25 } } } }");

            var outputFolder = Path.Combine(_workspace, "legacy.mod.FUSE");
            var result = FuseLegacyConverter.ConvertMod(modFolder, outputFolder);

            Assert.True(result.Success);
            Assert.Equal("acme.legacy", result.ModId);
            Assert.Equal("Acme Legacy", result.ModName);
            Assert.Equal("1.2.3", result.ModVersion);
            Assert.Equal("alex", result.Author);

            Assert.True(File.Exists(Path.Combine(outputFolder, "Info.json")));
            Assert.Single(result.WrittenFragments);
            Assert.Equal("tracks.fuse.json", result.WrittenFragments[0]);

            var info = JObject.Parse(File.ReadAllText(Path.Combine(outputFolder, "Info.json")));
            Assert.Equal("acme.legacy.FUSE", info.Value<string>("Id"));
            Assert.Equal("Acme Legacy (FUSE)", info.Value<string>("DisplayName"));
            Assert.Contains("FUSE", info.Value<JArray>("Requirements").Values<string>());

            var fragment = JObject.Parse(File.ReadAllText(Path.Combine(outputFolder, "tracks.fuse.json")));
            var tracks = fragment.Value<JObject>("tracks");
            var nodes = tracks.Value<JObject>("nodes");
            Assert.True(nodes.ContainsKey("node-A"));
            Assert.Equal(1.0, nodes["node-A"]["position"].Value<double>("x"));

            var segments = tracks.Value<JObject>("segments");
            Assert.True(segments.ContainsKey("seg-1"));
            Assert.Equal(25, segments["seg-1"].Value<int>("speedLimit"));
            Assert.Equal("node-A", segments["seg-1"].Value<string>("startNodeId"));
        }

        [Fact]
        public void ConvertMod_refuses_when_output_overlaps_source()
        {
            var modFolder = Path.Combine(_workspace, "inplace.mod");
            Directory.CreateDirectory(modFolder);
            File.WriteAllText(Path.Combine(modFolder, "tracks.json"),
                "{ \"tracks\": { \"nodes\": { \"n\": { \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 } } } } }");

            // output == source: converting in place would overwrite the
            // original files. Must refuse without writing anything.
            var sameResult = FuseLegacyConverter.ConvertMod(modFolder, modFolder);
            Assert.False(sameResult.Success);
            Assert.False(File.Exists(Path.Combine(modFolder, "Info.json")));

            // output nested inside source is equally unsafe.
            var nested = Path.Combine(modFolder, "out");
            var nestedResult = FuseLegacyConverter.ConvertMod(modFolder, nested);
            Assert.False(nestedResult.Success);
            Assert.False(Directory.Exists(nested));
        }

        [Fact]
        public void ConvertMod_handles_partial_segment_by_setting_preserve_flags()
        {
            var modFolder = Path.Combine(_workspace, "partial.mod");
            Directory.CreateDirectory(modFolder);
            File.WriteAllText(Path.Combine(modFolder, "tracks.json"),
                "{ \"tracks\": { \"segments\": { \"seg-partial\": { \"startNodeId\": \"only-start\" } } } }");

            var outputFolder = Path.Combine(_workspace, "partial.mod.FUSE");
            var result = FuseLegacyConverter.ConvertMod(modFolder, outputFolder);

            Assert.True(result.Success);
            var fragment = JObject.Parse(File.ReadAllText(Path.Combine(outputFolder, "tracks.fuse.json")));
            var seg = fragment["tracks"]["segments"]["seg-partial"];

            Assert.True(seg.Value<bool>("partial"));
            // Speed limit wasn't specified, so the preserve flag is true.
            Assert.True(seg.Value<bool>("preserveSpeedLimit"));
            Assert.Equal("only-start", seg.Value<string>("startNodeId"));
            Assert.Null(seg.Value<string>("endNodeId"));
        }

        [Fact]
        public void ConvertMod_skips_segment_with_no_start_or_end()
        {
            var modFolder = Path.Combine(_workspace, "bad.mod");
            Directory.CreateDirectory(modFolder);
            File.WriteAllText(Path.Combine(modFolder, "tracks.json"),
                "{ \"tracks\": { \"segments\": { \"seg-bad\": { \"speedLimit\": 30 } } } }");

            var outputFolder = Path.Combine(_workspace, "bad.mod.FUSE");
            var result = FuseLegacyConverter.ConvertMod(modFolder, outputFolder);

            Assert.True(result.Success); // file still written, just no segment
            var fragment = JObject.Parse(File.ReadAllText(Path.Combine(outputFolder, "tracks.fuse.json")));
            Assert.Empty(fragment["tracks"]["segments"]);

            Assert.Contains(result.Report, r =>
                r.Level == FuseConversionReportLevel.Warning && r.Message.Contains("seg-bad"));
        }

        [Fact]
        public void ConvertMod_falls_back_to_folder_name_when_no_manifest()
        {
            var modFolder = Path.Combine(_workspace, "unmanifested");
            Directory.CreateDirectory(modFolder);
            File.WriteAllText(Path.Combine(modFolder, "tracks.json"),
                "{ \"tracks\": { \"nodes\": { \"n\": { \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 } } } } }");

            var outputFolder = Path.Combine(_workspace, "unmanifested.FUSE");
            var result = FuseLegacyConverter.ConvertMod(modFolder, outputFolder);

            Assert.True(result.Success);
            Assert.Equal("unmanifested", result.ModId);
            Assert.Equal("unmanifested", result.ModName);
            Assert.Equal("1.0.0", result.ModVersion);
        }

        [Fact]
        public void ConvertMod_returns_failure_when_source_folder_missing()
        {
            var result = FuseLegacyConverter.ConvertMod(Path.Combine(_workspace, "nope"), Path.Combine(_workspace, "out"));

            Assert.False(result.Success);
            Assert.Contains(result.Report, r => r.Level == FuseConversionReportLevel.Error);
        }

        [Fact]
        public void ConvertMod_returns_failure_when_no_source_json_files()
        {
            var modFolder = Path.Combine(_workspace, "empty.mod");
            Directory.CreateDirectory(modFolder);
            File.WriteAllText(Path.Combine(modFolder, "Definition.json"),
                "{ \"id\": \"x\", \"requires\": [{ \"id\": \"railloader\" }] }");

            var result = FuseLegacyConverter.ConvertMod(modFolder, Path.Combine(_workspace, "empty.mod.FUSE"));

            Assert.False(result.Success);
            Assert.Contains(result.Report, r => r.Level == FuseConversionReportLevel.Error);
        }

        [Fact]
        public void ConvertMod_skips_signal_files_in_first_pass()
        {
            var modFolder = Path.Combine(_workspace, "signals.mod");
            Directory.CreateDirectory(modFolder);
            File.WriteAllText(Path.Combine(modFolder, "signals.json"),
                "{ \"signals\": { \"sig-1\": {} } }");
            File.WriteAllText(Path.Combine(modFolder, "tracks.json"),
                "{ \"tracks\": { \"nodes\": { \"n\": { \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 } } } } }");

            var outputFolder = Path.Combine(_workspace, "signals.mod.FUSE");
            var result = FuseLegacyConverter.ConvertMod(modFolder, outputFolder);

            Assert.True(result.Success);
            // Only the tracks fragment should be written; signal
            // conversion is a future pass.
            Assert.Single(result.WrittenFragments);
            Assert.Equal("tracks.fuse.json", result.WrittenFragments[0]);
        }

        [Fact]
        public void ConvertMod_translates_spans_field_shape_end_to_end()
        {
            var modFolder = Path.Combine(_workspace, "spans.mod");
            Directory.CreateDirectory(modFolder);
            File.WriteAllText(Path.Combine(modFolder, "tracks.json"),
                "{ \"tracks\": { " +
                "\"spans\": { " +
                "  \"sp-1\": { " +
                "    \"upper\": { \"segmentId\": \"seg-a\", \"end\": \"Start\", \"distance\": 10 }, " +
                "    \"lower\": { \"segmentId\": \"seg-a\", \"end\": \"start\", \"distance\": 5 } " +
                "  } " +
                "} } }");

            var outputFolder = Path.Combine(_workspace, "spans.mod.FUSE");
            var result = FuseLegacyConverter.ConvertMod(modFolder, outputFolder);

            Assert.True(result.Success);
            Assert.Equal(1, result.FragmentCounts["tracks.fuse.json"]["tracks.spans"]);

            var fragment = JObject.Parse(File.ReadAllText(Path.Combine(outputFolder, "tracks.fuse.json")));
            var sp = fragment["tracks"]["spans"]["sp-1"];
            // "Start" / "start" normalised to "A".
            Assert.Equal("A", sp["upper"].Value<string>("end"));
            Assert.Equal("A", sp["lower"].Value<string>("end"));
            Assert.Equal(10.0, sp["upper"].Value<double>("distance"));
            Assert.Equal(5.0, sp["lower"].Value<double>("distance"));
        }

        [Fact]
        public void ConvertMod_handles_full_operations_and_world_round_trip()
        {
            var modFolder = Path.Combine(_workspace, "rich.mod");
            Directory.CreateDirectory(modFolder);
            File.WriteAllText(Path.Combine(modFolder, "Definition.json"),
                "{ \"id\": \"rich.mod\", \"name\": \"Rich\", \"version\": \"2.0\", \"author\": \"alex\", " +
                "\"requires\": [{ \"id\": \"railloader\" }] }");

            // Single source covers every section family. Note the
            // SHAPE: legacy Strange Customs sources keep scenery /
            // splineys / texts / industries / areas / turntables at
            // the TOP level of the JSON, not nested under "world" or
            // "operations". The C# orchestrator matches the Python
            // convert_source which expects that flat layout. (tracks
            // is the one section that keeps its sub-keys.)
            File.WriteAllText(Path.Combine(modFolder, "everything.json"),
                "{ " +
                "\"tracks\": { " +
                "  \"nodes\": { \"n1\": { \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 } } }, " +
                "  \"segments\": { \"s1\": { \"startNodeId\": \"n1\", \"endNodeId\": \"n2\", \"speedLimit\": 35 } } " +
                "}, " +
                "\"areas\": { \"a1\": { \"name\": \"Yard\", \"radius\": 50 } }, " +
                "\"loads\": { \"coal\": { \"name\": \"Coal\", \"units\": \"Tons\" } }, " +
                "\"industries\": { \"sawmill\": { \"name\": \"Sawmill\" } }, " +
                "\"turntables\": { \"tt1\": { \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 }, \"radius\": 20 } }, " +
                "\"scenery\": { \"sc1\": { \"model\": \"oak\", \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 } } }, " +
                "\"texts\": { \"label1\": { \"text\": \"30 MPH\", \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 } } } " +
                "}");

            var outputFolder = Path.Combine(_workspace, "rich.mod.FUSE");
            var result = FuseLegacyConverter.ConvertMod(modFolder, outputFolder);

            Assert.True(result.Success);
            var counts = result.FragmentCounts["everything.fuse.json"];
            // CountContent now uses the Python-style fully-qualified
            // key shape (section.subsection) to match the Python
            // `count_content` so per-fragment ordering can disambiguate
            // tracks vs world content of the same name.
            Assert.Equal(1, counts["tracks.nodes"]);
            Assert.Equal(1, counts["tracks.segments"]);
            Assert.Equal(1, counts["tracks.areas"]);
            Assert.Equal(1, counts["operations.loads"]);
            Assert.Equal(1, counts["operations.industries"]);
            Assert.Equal(1, counts["operations.turntables"]);
            Assert.Equal(1, counts["world.scenery"]);
            Assert.Equal(1, counts["world.mapLabels"]);

            // Spot-check a few values to confirm the section
            // converters wired correctly.
            var fragment = JObject.Parse(File.ReadAllText(Path.Combine(outputFolder, "everything.fuse.json")));
            Assert.Equal("Coal", fragment["operations"]["loads"]["coal"].Value<string>("name"));
            Assert.Equal("speedLimit", fragment["world"]["mapLabels"]["label1"].Value<string>("style"));
            Assert.Equal(30, fragment["world"]["mapLabels"]["label1"].Value<int>("speedLimitMph"));
            Assert.Equal("scenery://oak", fragment["world"]["scenery"]["sc1"].Value<string>("assetIdentifier"));
        }
    }
}
