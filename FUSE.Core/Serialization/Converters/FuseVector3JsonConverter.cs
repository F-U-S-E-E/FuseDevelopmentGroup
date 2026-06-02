using System;
using Fuse.Core.Model;
using Newtonsoft.Json;

namespace Fuse.Core.Serialization.Converters
{
    public sealed class FuseVector3JsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            var targetType = Nullable.GetUnderlyingType(objectType) ?? objectType;
            return targetType == typeof(FuseVector3);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return objectType == typeof(FuseVector3?) ? (FuseVector3?)null : FuseVector3.zero;
            }

            var x = 0f;
            var y = 0f;
            var z = 0f;

            if (reader.TokenType != JsonToken.StartObject)
            {
                throw new JsonSerializationException("Vector3 must be an object with x, y, and z properties.");
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndObject)
                {
                    var vector = new FuseVector3(x, y, z);
                    return objectType == typeof(FuseVector3?) ? (FuseVector3?)vector : vector;
                }

                if (reader.TokenType != JsonToken.PropertyName)
                {
                    continue;
                }

                var propertyName = (string)reader.Value;
                if (!reader.Read())
                {
                    break;
                }

                var value = Convert.ToSingle(reader.Value);
                if (string.Equals(propertyName, "x", StringComparison.OrdinalIgnoreCase))
                {
                    x = value;
                }
                else if (string.Equals(propertyName, "y", StringComparison.OrdinalIgnoreCase))
                {
                    y = value;
                }
                else if (string.Equals(propertyName, "z", StringComparison.OrdinalIgnoreCase))
                {
                    z = value;
                }
            }

            throw new JsonSerializationException("Unexpected end while reading Vector3.");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var vector = value is FuseVector3 typed ? typed : FuseVector3.zero;
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(vector.x);
            writer.WritePropertyName("y");
            writer.WriteValue(vector.y);
            writer.WritePropertyName("z");
            writer.WriteValue(vector.z);
            writer.WriteEndObject();
        }
    }
}
