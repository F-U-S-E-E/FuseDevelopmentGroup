using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FUSE.Serialization
{
    /// <summary>
    /// Accepts both legacy AMM-shaped <c>{"id": true, "id2": false}</c>
    /// dict-of-bool and FUSE-native <c>["id", "id2"]</c> string arrays for
    /// progression unlock-fan-out fields. When the input is a dict, only the
    /// keys whose values are <c>true</c> are kept. Writes always emit the
    /// FUSE-native array form so converted output stays canonical.
    ///
    /// This is the migration that lets converted progression files continue
    /// to load while the converter (or hand-authored content) catches up to
    /// the canonical shape.
    /// </summary>
    public sealed class StringArrayOrBoolDictConverter : JsonConverter<string[]>
    {
        public override string[] ReadJson(JsonReader reader, Type objectType, string[] existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonToken.StartArray)
            {
                var list = new List<string>();
                var array = JArray.Load(reader);
                foreach (var token in array)
                {
                    if (token == null || token.Type == JTokenType.Null)
                    {
                        continue;
                    }

                    var value = token.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        list.Add(value);
                    }
                }

                return list.ToArray();
            }

            if (reader.TokenType == JsonToken.StartObject)
            {
                var dict = JObject.Load(reader);
                var list = new List<string>();
                foreach (var property in dict.Properties())
                {
                    if (property == null || string.IsNullOrWhiteSpace(property.Name))
                    {
                        continue;
                    }

                    // Take the key only when the value is truthy. AMM legacy
                    // emits both true and false entries; false means "not
                    // included," equivalent to the key being absent.
                    var value = property.Value;
                    if (value == null || value.Type == JTokenType.Null)
                    {
                        continue;
                    }

                    if (value.Type == JTokenType.Boolean && !(bool)value)
                    {
                        continue;
                    }

                    list.Add(property.Name);
                }

                return list.ToArray();
            }

            // Anything else (scalar, etc.) is unsupported — fall through to a
            // null result so the migration step downstream can normalize.
            return null;
        }

        public override void WriteJson(JsonWriter writer, string[] value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartArray();
            foreach (var entry in value)
            {
                writer.WriteValue(entry);
            }

            writer.WriteEndArray();
        }
    }
}
