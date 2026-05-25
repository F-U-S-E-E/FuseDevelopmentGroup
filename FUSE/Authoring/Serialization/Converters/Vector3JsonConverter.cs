using System;
using Newtonsoft.Json;
using UnityEngine;

namespace FUSE.Authoring.Serialization.Converters
{
    public sealed class Vector3JsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            var targetType = Nullable.GetUnderlyingType(objectType) ?? objectType;
            return targetType == typeof(Vector3);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return objectType == typeof(Vector3?) ? (Vector3?)null : Vector3.zero;
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
                    var vector = new Vector3(x, y, z);
                    return objectType == typeof(Vector3?) ? (Vector3?)vector : vector;
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
            var vector = value is Vector3 typed ? typed : Vector3.zero;
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
