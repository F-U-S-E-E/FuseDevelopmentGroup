using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Port of the Python <c>clean()</c> helper. Walks a JObject /
    /// JArray and drops <c>null</c> properties recursively so the
    /// emitted JSON doesn't carry empty placeholders for fields the
    /// legacy source didn't specify.
    /// </summary>
    /// <remarks>
    /// The cleanup is in-place on the supplied token. Returns the
    /// same token reference for chaining.
    ///
    /// Behaviour matches the Python source:
    /// <list type="bullet">
    ///   <item>Object: drop properties with null values. Recurse into
    ///     non-null values; if the recursive result becomes an empty
    ///     container, the property is kept (the Python clean()
    ///     does NOT prune empty containers — only null values).</item>
    ///   <item>Array: recurse into each element, drop null entries.</item>
    ///   <item>Scalar (number / string / bool): returned as-is.</item>
    /// </list>
    /// </remarks>
    internal static class JsonCleanHelper
    {
        public static JToken Clean(JToken value)
        {
            if (value is JObject obj)
            {
                CleanObject(obj);
                return obj;
            }

            if (value is JArray arr)
            {
                CleanArray(arr);
                return arr;
            }

            return value;
        }

        public static JObject CleanObject(JObject obj)
        {
            if (obj == null)
            {
                return null;
            }

            // Iterate properties on a copy because removing during
            // enumeration throws.
            var propertyNames = new System.Collections.Generic.List<string>();
            foreach (var property in obj.Properties())
            {
                propertyNames.Add(property.Name);
            }

            foreach (var name in propertyNames)
            {
                var prop = obj[name];
                if (prop == null || prop.Type == JTokenType.Null)
                {
                    obj.Remove(name);
                    continue;
                }

                Clean(prop);
            }

            return obj;
        }

        private static void CleanArray(JArray arr)
        {
            // Walk the array backwards so RemoveAt by index doesn't
            // shift the cursor.
            for (int i = arr.Count - 1; i >= 0; i--)
            {
                var token = arr[i];
                if (token == null || token.Type == JTokenType.Null)
                {
                    arr.RemoveAt(i);
                    continue;
                }
                Clean(token);
            }
        }
    }
}
