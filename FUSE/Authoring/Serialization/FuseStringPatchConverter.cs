using System;
using System.Collections.Generic;
using FUSE.Authoring.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FUSE.Authoring.Serialization
{
    /// <summary>
    /// Reads either a JSON array (<c>["foo", "bar"]</c>) or a JSON object
    /// (<c>{"foo": true, "bar": false}</c>) into a <see cref="FuseStringPatch"/>.
    /// Writes always emit the patch-object form when the value carries a
    /// merge dict, the array form when it carries a replacement set, and
    /// nothing when the patch is unset.
    ///
    /// <para>The two JSON shapes are not interchangeable — they describe
    /// different intents (full replacement vs per-entry patch). This is the
    /// only place that distinction is preserved end-to-end; downstream apply
    /// code consults <see cref="FuseStringPatch.Set"/> vs
    /// <see cref="FuseStringPatch.Patch"/> to decide how to combine the
    /// authored value with the live runtime state.</para>
    /// </summary>
    public sealed class FuseStringPatchConverter : JsonConverter<FuseStringPatch>
    {
        public override FuseStringPatch ReadJson(JsonReader reader, Type objectType, FuseStringPatch existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonToken.StartArray)
            {
                var token = JArray.Load(reader);
                var ids = new List<string>(token.Count);
                foreach (var item in token)
                {
                    if (item == null || item.Type == JTokenType.Null)
                    {
                        continue;
                    }
                    var value = item.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        ids.Add(value);
                    }
                }
                return FuseStringPatch.FromSet(ids);
            }

            if (reader.TokenType == JsonToken.StartObject)
            {
                var token = JObject.Load(reader);
                var entries = new List<KeyValuePair<string, bool>>(token.Count);
                foreach (var property in token.Properties())
                {
                    if (property == null || string.IsNullOrWhiteSpace(property.Name))
                    {
                        continue;
                    }
                    var value = property.Value;
                    bool flag;
                    if (value == null || value.Type == JTokenType.Null)
                    {
                        // A null value is treated as "include this id" — we
                        // accept it as a convenience for authors that drop
                        // sentinel JSON nulls into the patch dict. Anything
                        // explicit (true/false) is taken at face value.
                        flag = true;
                    }
                    else if (value.Type == JTokenType.Boolean)
                    {
                        flag = (bool)value;
                    }
                    else
                    {
                        // Any other value type is ignored — neither addition
                        // nor removal is expressible by it.
                        continue;
                    }
                    entries.Add(new KeyValuePair<string, bool>(property.Name, flag));
                }
                return FuseStringPatch.FromPatch(entries);
            }

            return null;
        }

        public override void WriteJson(JsonWriter writer, FuseStringPatch value, JsonSerializer serializer)
        {
            if (value == null || !value.HasValue)
            {
                writer.WriteNull();
                return;
            }

            if (value.Patch != null)
            {
                writer.WriteStartObject();
                foreach (var entry in value.Patch)
                {
                    writer.WritePropertyName(entry.Key);
                    writer.WriteValue(entry.Value);
                }
                writer.WriteEndObject();
                return;
            }

            writer.WriteStartArray();
            foreach (var entry in value.Set)
            {
                writer.WriteValue(entry);
            }
            writer.WriteEndArray();
        }
    }
}
