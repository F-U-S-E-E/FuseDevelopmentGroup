using System;
using Newtonsoft.Json.Linq;
using FUSE.Authoring.Serialization;

namespace FUSE.Authoring.Entities
{
    internal static class FuseAuthoringValueConverter
    {
        public static object ConvertValue(object value, Type targetType)
        {
            if (targetType == null)
            {
                return value;
            }

            if (value == null)
            {
                return targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null
                    ? Activator.CreateInstance(targetType)
                    : null;
            }

            var nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType != null)
            {
                targetType = nullableType;
            }

            if (targetType.IsInstanceOfType(value))
            {
                return value;
            }

            var token = value as JToken;
            if (token != null)
            {
                return token.ToObject(targetType, FuseSerializer.GetSerializer());
            }

            if (targetType.IsEnum)
            {
                return value is string text
                    ? Enum.Parse(targetType, text, true)
                    : Enum.ToObject(targetType, value);
            }

            if (targetType == typeof(Guid))
            {
                return Guid.Parse(value.ToString());
            }

            return System.Convert.ChangeType(value, targetType);
        }
    }
}
