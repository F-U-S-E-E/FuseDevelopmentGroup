using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Port of <c>vector()</c> / <c>make_vector()</c> from the Python
    /// converter. Legacy data uses a few shapes for a 3D vector —
    /// <c>{ "x": 0, "y": 0, "z": 0 }</c> the common one, but some
    /// older packages have <c>{ "X": ..., "Y": ..., "Z": ... }</c> or
    /// even <c>[x, y, z]</c> arrays. All collapse into the canonical
    /// FUSE shape (lowercase x/y/z, zero default).
    /// </summary>
    internal static class VectorHelper
    {
        public static JObject Vector(JToken token)
        {
            return Vector(token, defaultScale: false);
        }

        public static JObject Vector(JToken token, bool defaultScale)
        {
            var defaultValue = defaultScale ? 1f : 0f;
            var x = defaultValue;
            var y = defaultValue;
            var z = defaultValue;

            if (token is JObject obj)
            {
                x = ReadFloat(obj, "x", "X", defaultValue);
                y = ReadFloat(obj, "y", "Y", defaultValue);
                z = ReadFloat(obj, "z", "Z", defaultValue);
            }
            else if (token is JArray arr && arr.Count >= 3)
            {
                x = (float)(arr[0].Value<double?>() ?? defaultValue);
                y = (float)(arr[1].Value<double?>() ?? defaultValue);
                z = (float)(arr[2].Value<double?>() ?? defaultValue);
            }

            return Make(x, y, z);
        }

        public static JObject Make(float x, float y, float z)
        {
            return new JObject
            {
                ["x"] = x,
                ["y"] = y,
                ["z"] = z,
            };
        }

        private static float ReadFloat(JObject obj, string lower, string upper, float fallback)
        {
            if (obj[lower] != null && obj[lower].Type != JTokenType.Null)
            {
                return (float)(obj.Value<double?>(lower) ?? fallback);
            }
            if (obj[upper] != null && obj[upper].Type != JTokenType.Null)
            {
                return (float)(obj.Value<double?>(upper) ?? fallback);
            }
            return fallback;
        }
    }
}
