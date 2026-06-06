using System;
using System.Globalization;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Fuse.Core.Serialization.Converters
{
    public sealed class FlexibleStringArrayJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(string[]);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null || reader.TokenType == JsonToken.Undefined)
            {
                return Array.Empty<string>();
            }

            if (reader.TokenType == JsonToken.StartArray || reader.TokenType == JsonToken.StartObject)
            {
                var list = new List<string>();
                var depth = 0;
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonToken.StartArray:
                        case JsonToken.StartObject:
                            depth++;
                            break;

                        case JsonToken.EndArray:
                        case JsonToken.EndObject:
                            if (depth == 0)
                            {
                                return list.ToArray();
                            }

                            depth--;
                            break;

                        case JsonToken.String:
                        case JsonToken.Integer:
                        case JsonToken.Float:
                        case JsonToken.Boolean:
                        case JsonToken.Date:
                            var valueText = Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
                            if (!string.IsNullOrWhiteSpace(valueText))
                            {
                                list.Add(valueText);
                            }

                            break;

                        case JsonToken.PropertyName:
                            break;
                    }
                }

                return list.ToArray();
            }

            if (reader.Value != null)
            {
                var valueText = Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
                return string.IsNullOrWhiteSpace(valueText)
                    ? Array.Empty<string>()
                    : new[] { valueText };
            }

            return Array.Empty<string>();
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var array = value as string[] ?? Array.Empty<string>();
            writer.WriteStartArray();
            foreach (var entry in array)
            {
                writer.WriteValue(entry);
            }
            writer.WriteEndArray();
        }
    }
}
