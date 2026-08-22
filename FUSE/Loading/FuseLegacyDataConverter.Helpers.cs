using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Authoring.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FUSE.Loading
{
    internal static partial class FuseLegacyDataConverter
    {

        private static void ConvertDictionary(JToken source, JObject target, JArray removals, Func<JToken, JToken> converter)
        {
            ConvertDictionary(source, target, removals, (_, token) => converter(token));
        }

        private static void ConvertDictionary(JToken source, JObject target, JArray removals, Func<string, JToken, JToken> converter)
        {
            if (!(source is JObject obj) || target == null)
            {
                return;
            }

            foreach (var property in obj.Properties())
            {
                if (property.Value.Type == JTokenType.Null)
                {
                    removals?.Add(property.Name);
                    continue;
                }

                var converted = converter(property.Name, property.Value);
                if (converted != null)
                {
                    target[property.Name] = Clean(converted);
                }
            }
        }

        /// <summary>
        /// Combines dictionary aliases in legacy-precedence order. Some older
        /// RailLoader packages wrote nodes, segments, and spans at the document
        /// root while newer packages write them under tracks. Later sources win so the
        /// canonical nested form can intentionally override the legacy alias,
        /// including overriding an entry with null to request a removal.
        /// </summary>
        private static JObject MergeLegacyDictionaries(params JObject[] sources)
        {
            var merged = new JObject();
            if (sources == null)
            {
                return merged;
            }

            foreach (var source in sources)
            {
                if (source == null)
                {
                    continue;
                }

                foreach (var property in source.Properties())
                {
                    merged[property.Name] = property.Value.DeepClone();
                }
            }

            return merged;
        }

        private static JObject Vector(JToken value, bool defaultScale)
        {
            var fallback = defaultScale ? 1f : 0f;
            if (value is JArray array)
            {
                return new JObject
                {
                    ["x"] = ReadFloat(array.Count > 0 ? array[0] : null, fallback),
                    ["y"] = ReadFloat(array.Count > 1 ? array[1] : null, fallback),
                    ["z"] = ReadFloat(array.Count > 2 ? array[2] : null, fallback)
                };
            }

            if (value is JObject obj)
            {
                return new JObject
                {
                    ["x"] = ReadFloat(obj["x"], fallback),
                    ["y"] = ReadFloat(obj["y"], fallback),
                    ["z"] = ReadFloat(obj["z"], fallback)
                };
            }

            return new JObject
            {
                ["x"] = fallback,
                ["y"] = fallback,
                ["z"] = fallback
            };
        }

        private static LegacyVector ReadVector(JToken value, bool defaultScale)
        {
            var vector = Vector(value, defaultScale);
            return new LegacyVector(
                ReadFloat(vector["x"], defaultScale ? 1f : 0f),
                ReadFloat(vector["y"], defaultScale ? 1f : 0f),
                ReadFloat(vector["z"], defaultScale ? 1f : 0f));
        }

        private static JObject Vector(LegacyVector value)
        {
            return new JObject
            {
                ["x"] = value.X,
                ["y"] = value.Y,
                ["z"] = value.Z
            };
        }

        private static string ResolvePackageFile(string folderPath, string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return string.Empty;
            }

            var relative = reference.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(folderPath, relative);
        }

        private static string GetPackageRelativePath(string folderPath, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                var fullPath = Path.GetFullPath(path);
                var fullFolder = string.IsNullOrWhiteSpace(folderPath)
                    ? string.Empty
                    : Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.IsNullOrWhiteSpace(fullFolder) &&
                    fullPath.StartsWith(fullFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return NormalizePackagePath(fullPath.Substring(fullFolder.Length + 1));
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not normalize package-relative path '{path ?? string.Empty}' " +
                    $"against folder '{folderPath ?? string.Empty}'; using the provided path. Reason: {ex.Message}");
            }

            return NormalizePackagePath(path);
        }

        private static string NormalizePackagePath(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .Replace('\\', '/');
        }

        private static string UniqueFragment(string fragment, HashSet<string> used)
        {
            var value = string.IsNullOrWhiteSpace(fragment) ? "fragment" : fragment;
            var result = value;
            var index = 2;
            while (used.Contains(result))
            {
                result = value + "-" + index.ToString(CultureInfo.InvariantCulture);
                index++;
            }

            used.Add(result);
            return result;
        }

        private static string UniqueObjectKey(string key, JObject obj)
        {
            var value = string.IsNullOrWhiteSpace(key) ? "item" : key.Trim();
            var result = value;
            var index = 2;
            while (obj != null && obj[result] != null)
            {
                result = value + "-" + index.ToString(CultureInfo.InvariantCulture);
                index++;
            }

            return result;
        }

        private static string Slug(string value)
        {
            var slug = Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant();
            return string.IsNullOrWhiteSpace(slug) ? "fragment" : slug;
        }

        private static JToken Clean(JToken value)
        {
            if (value is JObject obj)
            {
                return CleanObject(obj);
            }

            if (value is JArray array)
            {
                var result = new JArray();
                foreach (var item in array.Select(Clean).Where(item => item != null && !IsEmpty(item)))
                {
                    result.Add(item);
                }

                return result;
            }

            return value?.DeepClone();
        }

        private static JObject CleanObject(JObject obj)
        {
            var result = new JObject();
            foreach (var property in obj.Properties())
            {
                var cleaned = Clean(property.Value);
                if (cleaned == null ||
                    cleaned.Type == JTokenType.Null ||
                    cleaned is JValue scalar && scalar.Value == null ||
                    IsEmpty(cleaned))
                {
                    continue;
                }

                result[property.Name] = cleaned;
            }

            return result;
        }

        private static bool IsEmpty(JToken value)
        {
            return value is JObject obj && !obj.HasValues ||
                   value is JArray array && array.Count == 0;
        }

        private static JToken Clone(JToken value)
        {
            return value == null || value.Type == JTokenType.Null ? null : value.DeepClone();
        }

        private static void AddUniqueString(JArray array, string value)
        {
            if (array == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (array.Values<string>().Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            array.Add(value);
        }

        private static string ReadString(JObject obj, params string[] names)
        {
            if (obj == null || names == null)
            {
                return null;
            }

            foreach (var name in names)
            {
                var token = obj[name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    continue;
                }

                var value = token.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static bool HasAnyProperty(JObject obj, params string[] names)
        {
            if (obj == null || names == null)
            {
                return false;
            }

            return names.Any(name => obj.Properties().Any(property =>
                string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool ReadBool(JObject obj, string name, bool defaultValue)
        {
            var token = obj?[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                return defaultValue;
            }

            return token.Type == JTokenType.Boolean
                ? token.Value<bool>()
                : bool.TryParse(token.ToString(), out var parsed) ? parsed : defaultValue;
        }

        private static int ReadInt(JToken token, int defaultValue)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return defaultValue;
            }

            // Numeric tokens must be read through the typed accessor. JValue.ToString()
            // formats with the current culture, so a comma-decimal locale (pt-BR, de-DE,
            // fr-FR, ...) turns 12.5 into "12,5", which the invariant parse below
            // rejects. That silently collapsed every fractional legacy coordinate to the
            // default (issue #219).
            switch (token.Type)
            {
                case JTokenType.Integer:
                    return token.Value<int>();
                case JTokenType.Float:
                    return (int)Math.Round(token.Value<double>());
            }

            return int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : defaultValue;
        }

        private static float ReadFloat(JToken token, float defaultValue)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return defaultValue;
            }

            // See ReadInt: never round-trip a numeric token through ToString().
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                return token.Value<float>();
            }

            return float.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : defaultValue;
        }

        private static string NormalizeEnd(string value)
        {
            var text = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (text == "start" || text == "a")
            {
                return "A";
            }

            if (text == "end" || text == "b")
            {
                return "B";
            }

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static bool IsLoadSchemaKey(string key)
        {
            switch ((key ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "name":
                case "description":
                case "units":
                case "density":
                case "unitweightinpounds":
                case "importable":
                case "payperquantity":
                case "costperunit":
                case "cartypefilter":
                case "fields":
                    return true;
                default:
                    return false;
            }
        }
    }
}
