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

        private static JObject ToStringListPatch(JToken value)
        {
            var result = new JObject();
            CollectStringListPatch(value, result);
            return result.HasValues ? result : null;
        }

        private static void CollectStringListPatch(JToken value, JObject result)
        {
            if (value == null || result == null || value.Type == JTokenType.Null)
            {
                return;
            }

            if (value is JArray array)
            {
                foreach (var item in array)
                {
                    CollectStringListPatch(item, result);
                }

                return;
            }

            if (!(value is JObject obj))
            {
                return;
            }

            if (TryCollectStringListFindPatch(obj, result))
            {
                return;
            }

            foreach (var property in obj.Properties())
            {
                var operation = NormalizeStringListPatchOperation(property.Name);
                if (string.IsNullOrWhiteSpace(operation))
                {
                    continue;
                }

                AddStringListPatchValues(result, operation, property.Value);
            }
        }

        private static bool TryCollectStringListFindPatch(JObject obj, JObject result)
        {
            if (obj == null || result == null || !TryGetDirective(obj, "$find", out var findToken))
            {
                return false;
            }

            var findValues = ReadStringListFindValues(findToken).ToArray();
            if (findValues.Length == 0)
            {
                return true;
            }

            if (TryGetDirective(obj, "$remove", out var removeToken) ||
                TryGetDirective(obj, "$delete", out removeToken))
            {
                if (IsTruthyDirective(removeToken))
                {
                    AddStringListPatchValues(result, "remove", new JArray(findValues));
                }

                return true;
            }

            if (TryGetDirective(obj, "$replace", out var replacement))
            {
                AddStringListPatchValues(result, "remove", new JArray(findValues));
                AddStringListPatchValues(result, "append", replacement);
                return true;
            }

            return true;
        }

        private static IEnumerable<string> ReadStringListFindValues(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                yield break;
            }

            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    foreach (var value in ReadStringListFindValues(item))
                    {
                        yield return value;
                    }
                }

                yield break;
            }

            if (token is JObject obj)
            {
                var value = ReadString(obj, "value", "Value", "id", "Id");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value.Trim();
                }

                yield break;
            }

            var scalar = token.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(scalar))
            {
                yield return scalar;
            }
        }

        private static bool TryGetDirective(JObject obj, string name, out JToken value)
        {
            value = null;
            return obj != null &&
                   obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out value);
        }

        private static bool IsTruthyDirective(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                return false;
            }

            if (value.Type == JTokenType.Boolean)
            {
                return value.Value<bool>();
            }

            return true;
        }

        private static string NormalizeStringListPatchOperation(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "$add":
                case "add":
                case "$append":
                case "append":
                    return "append";
                case "$insert":
                case "insert":
                    return "insert";
                case "$prepend":
                case "prepend":
                    return "prepend";
                case "$replace":
                case "replace":
                    return "replace";
                case "$remove":
                case "remove":
                case "$delete":
                case "delete":
                    return "remove";
                default:
                    return null;
            }
        }

        private static void AddStringListPatchValues(JObject result, string operation, JToken value)
        {
            var target = result[operation] as JArray;
            if (target == null)
            {
                target = new JArray();
                result[operation] = target;
            }

            foreach (var item in ReadStringArray(value))
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    target.Add(item);
                }
            }
        }

        private static JArray ToStringArrayFromPatch(JObject patch)
        {
            var result = new JArray();
            if (patch == null)
            {
                return result;
            }

            var replace = patch["replace"] as JArray;
            if (replace != null)
            {
                AddStringArrayItems(result, replace);
                return result;
            }

            AddStringArrayItems(result, patch["prepend"] as JArray);
            AddStringArrayItems(result, patch["add"] as JArray);
            AddStringArrayItems(result, patch["append"] as JArray);
            AddStringArrayItems(result, patch["insert"] as JArray);
            return result;
        }

        private static void AddStringArrayItems(JArray target, JArray source)
        {
            if (target == null || source == null)
            {
                return;
            }

            foreach (var item in source.Values<string>())
            {
                if (!string.IsNullOrWhiteSpace(item) &&
                    !target.Values<string>().Any(existing => string.Equals(existing, item, StringComparison.OrdinalIgnoreCase)))
                {
                    target.Add(item);
                }
            }
        }

        private static JArray ToStringArray(JToken value)
        {
            var result = new JArray();
            foreach (var item in ReadStringArray(value))
            {
                result.Add(item);
            }

            return result;
        }

        private static IEnumerable<string> ReadStringArray(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                yield break;
            }

            if (value is JArray array)
            {
                foreach (var item in array)
                {
                    if (item?.Type == JTokenType.String)
                    {
                        var text = item.Value<string>()?.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            yield return text;
                        }

                        continue;
                    }

                    foreach (var text in ReadStringArray(item))
                    {
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            yield return text;
                        }
                    }
                }

                yield break;
            }

            if (value is JObject obj)
            {
                var scalarId = ReadString(obj, "id", "Id");
                if (!string.IsNullOrWhiteSpace(scalarId))
                {
                    yield return scalarId.Trim();
                    yield break;
                }

                var directiveProperties = obj.Properties()
                    .Where(property => IsStringArrayDirectiveKey(property.Name))
                    .ToArray();
                if (directiveProperties.Length > 0)
                {
                    foreach (var directive in directiveProperties)
                    {
                        foreach (var item in ReadStringArray(directive.Value))
                        {
                            if (!string.IsNullOrWhiteSpace(item))
                            {
                                yield return item;
                            }
                        }
                    }

                    yield break;
                }

                foreach (var property in obj.Properties())
                {
                    if (property.Value.Type == JTokenType.Boolean && !property.Value.Value<bool>())
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(property.Name))
                    {
                        yield return property.Name;
                    }
                }

                yield break;
            }

            var scalar = value.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(scalar))
            {
                yield return scalar;
            }
        }

        private static bool IsStringArrayDirectiveKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "$add":
                case "$append":
                case "$prepend":
                case "$insert":
                case "$replace":
                case "$remove":
                case "$delete":
                    return true;
                default:
                    return false;
            }
        }
    }
}
