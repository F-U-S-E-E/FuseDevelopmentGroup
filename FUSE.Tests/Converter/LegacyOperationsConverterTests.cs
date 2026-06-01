using FUSE.Converter.Conversion;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Converter
{
    /// <summary>
    /// Pure-unit coverage for LegacyOperationsConverter — exercise
    /// each individual <c>Convert*</c> against representative legacy
    /// input. End-to-end coverage through <c>FuseLegacyConverter</c>
    /// lives in <see cref="FuseLegacyConverterTests"/>.
    /// </summary>
    public sealed class LegacyOperationsConverterTests
    {
        [Fact]
        public void ConvertLoad_keeps_canonical_fields_and_folds_extras()
        {
            var legacy = JObject.Parse(
                "{ \"name\": \"Iron\", \"units\": \"Pounds\", \"density\": 50, " +
                "\"unitWeightInPounds\": 100, \"importable\": true, \"costPerUnit\": 2.5, " +
                "\"carTypeFilter\": \"hopper\", \"customField\": \"custom value\", " +
                "\"anotherCustom\": 42 }");

            var result = LegacyOperationsConverter.ConvertLoad("iron-id", legacy);

            Assert.Equal("Iron", result.Value<string>("name"));
            Assert.Equal("Pounds", result.Value<string>("units"));
            Assert.Equal(50.0, result.Value<double>("density"));
            Assert.True(result.Value<bool>("importable"));
            Assert.Equal("hopper", result.Value<string>("carTypeFilter"));

            // Custom fields not in the schema set get folded into `fields`.
            var fields = result.Value<JObject>("fields");
            Assert.NotNull(fields);
            Assert.Equal("custom value", fields.Value<string>("customField"));
            Assert.Equal(42, fields.Value<int>("anotherCustom"));
        }

        [Fact]
        public void ConvertLoad_explicit_fields_dict_wins_over_inferred_entries()
        {
            var legacy = JObject.Parse(
                "{ \"name\": \"L\", \"customA\": \"inferred\", " +
                "\"fields\": { \"customA\": \"explicit\", \"customB\": 1 } }");

            var result = LegacyOperationsConverter.ConvertLoad("id", legacy);
            var fields = result.Value<JObject>("fields");

            Assert.Equal("explicit", fields.Value<string>("customA"));
            Assert.Equal(1, fields.Value<int>("customB"));
        }

        [Fact]
        public void ConvertLoad_falls_back_to_id_when_name_absent()
        {
            var legacy = JObject.Parse("{ \"description\": \"My Load\" }");
            var result = LegacyOperationsConverter.ConvertLoad("the-id", legacy);
            Assert.Equal("My Load", result.Value<string>("name"));

            var result2 = LegacyOperationsConverter.ConvertLoad("the-id", JObject.Parse("{}"));
            Assert.Equal("the-id", result2.Value<string>("name"));
        }

        [Fact]
        public void ConvertIndustry_preserves_components_position_rotation()
        {
            var legacy = JObject.Parse(
                "{ \"name\": \"Sawmill\", " +
                "\"localPosition\": { \"x\": 100, \"y\": 0, \"z\": -50 }, " +
                "\"localRotation\": { \"x\": 0, \"y\": 90, \"z\": 0 }, " +
                "\"usesContract\": true, " +
                "\"components\": { \"loader-1\": { \"loadId\": \"logs\", \"capacity\": 200 } } }");

            var result = LegacyOperationsConverter.ConvertIndustry("sawmill-id", legacy, areaId: null, order: 5);

            Assert.Equal("Sawmill", result.Value<string>("name"));
            Assert.Equal(100.0, result["position"].Value<double>("x"));
            Assert.Equal(90.0, result["rotation"].Value<double>("y"));
            Assert.True(result.Value<bool>("usesContract"));
            Assert.Equal(5, result.Value<int>("order"));

            var components = result.Value<JObject>("components");
            Assert.True(components.ContainsKey("loader-1"));
            Assert.Equal("logs", components["loader-1"].Value<string>("loadId"));
        }

        [Fact]
        public void ConvertIndustry_falls_back_to_id_when_name_absent()
        {
            var legacy = JObject.Parse("{}");
            var result = LegacyOperationsConverter.ConvertIndustry("my-industry", legacy, null, null);
            Assert.Equal("my-industry", result.Value<string>("name"));
        }

        [Fact]
        public void ConvertTurntable_uses_nested_roundhouse_when_present()
        {
            var legacy = JObject.Parse(
                "{ \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 }, \"radius\": 20, " +
                "\"roundhouse\": { \"stalls\": 4, \"startAngle\": 30, \"trackLength\": 50 } }");

            var result = LegacyOperationsConverter.ConvertTurntable("tt-1", legacy);
            var rh = result.Value<JObject>("roundhouse");

            Assert.Equal(20.0, result.Value<double>("radius"));
            Assert.Equal(4, rh.Value<int>("stalls"));
            Assert.Equal(30.0, rh.Value<double>("startAngle"));
            Assert.Equal(50.0, rh.Value<double>("trackLength"));
        }

        [Fact]
        public void ConvertTurntable_falls_back_to_flat_roundhouse_form()
        {
            var legacy = JObject.Parse(
                "{ \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 }, " +
                "\"roundhouseStalls\": 6, \"roundhouseTrackLength\": 40 }");

            var result = LegacyOperationsConverter.ConvertTurntable("tt-2", legacy);
            var rh = result.Value<JObject>("roundhouse");

            Assert.Equal(6, rh.Value<int>("stalls"));
            Assert.Equal(40.0, rh.Value<double>("trackLength"));
            Assert.Equal("vanilla://roundhouseStart", rh.Value<string>("startPrefab"));
        }

        [Fact]
        public void ConvertTurntable_defaults_radius_and_subdivisions_when_absent()
        {
            var result = LegacyOperationsConverter.ConvertTurntable("tt-empty",
                JObject.Parse("{ \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 } }"));

            Assert.Equal(15.0, result.Value<double>("radius"));
            Assert.Equal(32, result.Value<int>("subdivisions"));
        }

        [Fact]
        public void ConvertLoader_passes_through_industry_id_and_position()
        {
            var legacy = JObject.Parse(
                "{ \"position\": { \"x\": 10, \"y\": 1, \"z\": 5 }, " +
                "\"industry\": \"sawmill\", \"prefab\": \"path://loader\" }");

            var result = LegacyOperationsConverter.ConvertLoader(legacy);

            Assert.Equal("sawmill", result.Value<string>("industryId"));
            Assert.Equal("path://loader", result.Value<string>("prefab"));
            Assert.Equal(10.0, result["position"].Value<double>("x"));
        }

        [Fact]
        public void ConvertStation_maps_passengerStop_to_passengerStopId()
        {
            var legacy = JObject.Parse(
                "{ \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 }, " +
                "\"passengerStop\": \"depot-platform-1\", \"prefab\": \"path://depot\" }");

            var result = LegacyOperationsConverter.ConvertStation(legacy);

            Assert.Equal("depot-platform-1", result.Value<string>("passengerStopId"));
            Assert.Equal("path://depot", result.Value<string>("prefab"));
        }

        [Fact]
        public void ConvertStation_defaults_prefab_when_absent()
        {
            var legacy = JObject.Parse("{ \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 } }");
            var result = LegacyOperationsConverter.ConvertStation(legacy);
            Assert.Equal("empty://", result.Value<string>("prefab"));
        }
    }
}
