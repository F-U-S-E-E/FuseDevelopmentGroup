using System.Linq;
using FUSE.Authoring.Serialization;
using Newtonsoft.Json;
using Xunit;

namespace FUSE.Tests.Serialization
{
    public class StringArrayOrBoolDictConverterTests
    {
        private sealed class Holder
        {
            [JsonConverter(typeof(StringArrayOrBoolDictConverter))]
            public string[] Ids { get; set; }
        }

        private static Holder Deserialize(string json)
        {
            return JsonConvert.DeserializeObject<Holder>(json);
        }

        [Fact]
        public void ArrayInput_RoundTripsAsArray()
        {
            var holder = Deserialize("{\"ids\":[\"a\",\"b\"]}");

            Assert.Equal(new[] { "a", "b" }, holder.Ids);
        }

        [Fact]
        public void DictInput_KeepsOnlyTrueKeys()
        {
            var holder = Deserialize("{\"ids\":{\"keep\":true,\"drop\":false,\"alsoKeep\":true}}");

            Assert.Equal(new[] { "keep", "alsoKeep" }.OrderBy(s => s),
                         holder.Ids.OrderBy(s => s));
        }

        [Fact]
        public void DictInput_SkipsNullValues()
        {
            var holder = Deserialize("{\"ids\":{\"a\":true,\"b\":null,\"c\":true}}");

            Assert.Equal(new[] { "a", "c" }.OrderBy(s => s),
                         holder.Ids.OrderBy(s => s));
        }

        [Fact]
        public void NullToken_DeserializesAsNull()
        {
            var holder = Deserialize("{\"ids\":null}");

            Assert.Null(holder.Ids);
        }

        [Fact]
        public void ArrayInput_FiltersWhitespaceAndNullEntries()
        {
            var holder = Deserialize("{\"ids\":[\"a\",\"\",\"   \",null,\"b\"]}");

            Assert.Equal(new[] { "a", "b" }, holder.Ids);
        }

        [Fact]
        public void DictInput_TreatsNonBoolTruthyValuesAsTrue()
        {
            // Per the converter: only false-Bool tokens are dropped. Other token
            // types (number, string) keep the key — documenting the actual contract.
            var holder = Deserialize("{\"ids\":{\"a\":1,\"b\":\"yes\",\"c\":false}}");

            Assert.Equal(new[] { "a", "b" }.OrderBy(s => s),
                         holder.Ids.OrderBy(s => s));
        }

        [Fact]
        public void Write_EmitsArrayForm()
        {
            var holder = new Holder { Ids = new[] { "a", "b" } };

            var json = JsonConvert.SerializeObject(holder);

            Assert.Contains("\"Ids\":[\"a\",\"b\"]", json);
        }

        [Fact]
        public void Write_NullValueEmitsNullToken()
        {
            var holder = new Holder { Ids = null };

            var json = JsonConvert.SerializeObject(holder);

            Assert.Contains("\"Ids\":null", json);
        }
    }
}
