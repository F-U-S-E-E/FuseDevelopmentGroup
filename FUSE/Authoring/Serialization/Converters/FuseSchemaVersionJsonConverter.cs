using System;
using System.Globalization;
using Newtonsoft.Json;

namespace FUSE.Authoring.Serialization.Converters
{
    public sealed class FuseSchemaVersionJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(string);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null || reader.TokenType == JsonToken.Undefined)
            {
                return null;
            }

            if (reader.Value == null)
            {
                return null;
            }

            return Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture));
        }
    }
}
