using FUSE.Serialization.Converters;
using Newtonsoft.Json;
using Xunit;

namespace FUSE.Tests.Serialization
{
    public class FuseSchemaVersionJsonConverterTests
    {
        private sealed class Holder
        {
            [JsonConverter(typeof(FuseSchemaVersionJsonConverter))]
            public string Version { get; set; }
        }

        [Fact]
        public void StringInput_RoundTrips()
        {
            var holder = JsonConvert.DeserializeObject<Holder>("{\"version\":\"1.2.3\"}");

            Assert.Equal("1.2.3", holder.Version);
        }

        [Fact]
        public void NumericInput_ConvertedToStringInvariantCulture()
        {
            var holder = JsonConvert.DeserializeObject<Holder>("{\"version\":1.5}");

            Assert.Equal("1.5", holder.Version);
        }

        [Fact]
        public void IntegerInput_ConvertedToString()
        {
            var holder = JsonConvert.DeserializeObject<Holder>("{\"version\":2}");

            Assert.Equal("2", holder.Version);
        }

        [Fact]
        public void NullInput_DeserializesAsNull()
        {
            var holder = JsonConvert.DeserializeObject<Holder>("{\"version\":null}");

            Assert.Null(holder.Version);
        }

        [Fact]
        public void StringValue_WritesAsJsonString()
        {
            var holder = new Holder { Version = "1.0" };

            var json = JsonConvert.SerializeObject(holder);

            Assert.Contains("\"Version\":\"1.0\"", json);
        }

        [Fact]
        public void NullValue_WritesAsNullToken()
        {
            var holder = new Holder { Version = null };

            var json = JsonConvert.SerializeObject(holder);

            Assert.Contains("\"Version\":null", json);
        }
    }
}
