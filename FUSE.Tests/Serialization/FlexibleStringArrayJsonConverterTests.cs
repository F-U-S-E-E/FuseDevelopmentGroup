using System;
using FUSE.Serialization.Converters;
using Newtonsoft.Json;
using Xunit;

namespace FUSE.Tests.Serialization
{
    public class FlexibleStringArrayJsonConverterTests
    {
        private sealed class Holder
        {
            public string[] Tags { get; set; }
        }

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new FlexibleStringArrayJsonConverter() }
        };

        private static Holder Deserialize(string json)
        {
            return JsonConvert.DeserializeObject<Holder>(json, Settings);
        }

        [Fact]
        public void ArrayOfStrings_DeserializesAsArray()
        {
            var holder = Deserialize("{\"tags\":[\"a\",\"b\"]}");

            Assert.Equal(new[] { "a", "b" }, holder.Tags);
        }

        [Fact]
        public void SingleString_PromotedToArray()
        {
            var holder = Deserialize("{\"tags\":\"single\"}");

            Assert.Equal(new[] { "single" }, holder.Tags);
        }

        [Fact]
        public void NullValue_ReturnsEmptyArray()
        {
            var holder = Deserialize("{\"tags\":null}");

            Assert.Equal(Array.Empty<string>(), holder.Tags);
        }

        [Fact]
        public void Number_ConvertedToStringEntry()
        {
            var holder = Deserialize("{\"tags\":[1,2.5]}");

            Assert.Equal(new[] { "1", "2.5" }, holder.Tags);
        }

        [Fact]
        public void Boolean_ConvertedToStringEntry()
        {
            var holder = Deserialize("{\"tags\":[true,false]}");

            Assert.Equal(new[] { "True", "False" }, holder.Tags);
        }

        [Fact]
        public void WhitespaceEntries_AreFiltered()
        {
            var holder = Deserialize("{\"tags\":[\"a\",\"\",\"   \",\"b\"]}");

            Assert.Equal(new[] { "a", "b" }, holder.Tags);
        }

        [Fact]
        public void ObjectInput_ExtractsScalarValues()
        {
            var holder = Deserialize("{\"tags\":{\"first\":\"a\",\"second\":\"b\"}}");

            Assert.Equal(new[] { "a", "b" }, holder.Tags);
        }

        // Regression: WriteJson previously called serializer.Serialize(writer, value),
        // which re-dispatched into this same converter (because CanConvert(string[])
        // returns true) and threw a self-referencing-loop exception. Fix landed in
        // commit f8eba5f — these tests lock in that the converter now writes the array
        // tokens directly to the JsonWriter.
        [Fact]
        public void Write_RoundTripsStringArray_AsArrayLiteral()
        {
            var holder = new Holder { Tags = new[] { "x", "y" } };

            var json = JsonConvert.SerializeObject(holder, Settings);

            Assert.Contains("\"Tags\":[\"x\",\"y\"]", json);
        }

        [Fact]
        public void Write_EmptyArray_EmitsEmptyJsonArray()
        {
            var holder = new Holder { Tags = Array.Empty<string>() };

            var json = JsonConvert.SerializeObject(holder, Settings);

            Assert.Contains("\"Tags\":[]", json);
        }

        [Fact]
        public void Write_NullArray_EmitsJsonNull()
        {
            // Newtonsoft writes null property values directly without invoking
            // converter WriteJson, so the `?? Array.Empty<string>()` guard in the
            // converter is defensive — it covers a hypothetical reentrant call,
            // not the normal property-serialization path. The observable contract
            // for a null Tags field is therefore JSON null, not an empty array.
            var holder = new Holder { Tags = null };

            var json = JsonConvert.SerializeObject(holder, Settings);

            Assert.Contains("\"Tags\":null", json);
        }
    }
}
