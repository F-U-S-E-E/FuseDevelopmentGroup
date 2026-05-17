using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace FUSE.Loading
{
    internal static class FuseLegacyJsonPatch
    {
        private static readonly HashSet<string> DirectiveKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "$add",
                "$append",
                "$clone",
                "$delete",
                "$find",
                "$insert",
                "$moveTo",
                "$optional",
                "$prepend",
                "$remove",
                "$replace"
            };

        public static void Apply(JObject target, JObject patch, string source)
        {
            if (target == null || patch == null)
            {
                return;
            }

            ApplyObject(target, patch, target, source ?? string.Empty);
        }

        private static void ApplyObject(JObject target, JObject patch, JObject root, string source)
        {
            foreach (var property in patch.Properties().ToArray())
            {
                if (IsDirective(property.Name))
                {
                    continue;
                }

                ApplyProperty(target, property, root, source);
            }
        }

        private static void ApplyProperty(JObject target, JProperty patchProperty, JObject root, string source)
        {
            var name = patchProperty.Name;
            var patchValue = patchProperty.Value;
            if (patchValue == null || patchValue.Type == JTokenType.Null)
            {
                RemoveProperty(target, name);
                return;
            }

            if (patchValue is JObject patchObject)
            {
                if (TryGetDirective(patchObject, "$replace", out var replacement))
                {
                    SetProperty(target, name, replacement.DeepClone());
                    return;
                }

                var current = GetPropertyValue(target, name);
                if (TryApplyArrayDirectiveProperty(target, name, current, patchObject))
                {
                    return;
                }

                if (IsRemovePatch(patchObject))
                {
                    RemoveProperty(target, name);
                    return;
                }

                if (TryGetDirective(patchObject, "$moveTo", out var moveToken))
                {
                    MoveProperty(target, name, patchObject, moveToken, root, source);
                    return;
                }

                if (current == null || current.Type == JTokenType.Null)
                {
                    SetProperty(target, name, NormalizeNewToken(patchObject, source));
                    return;
                }

                if (current is JObject currentObject)
                {
                    ApplyObject(currentObject, patchObject, root, source);
                    return;
                }

                if (current is JArray currentArray)
                {
                    throw new InvalidOperationException(
                        $"Legacy patch '{source}' cannot merge object directives into array property '{name}'.");
                }

                SetProperty(target, name, NormalizeNewToken(patchObject, source));
                return;
            }

            if (patchValue is JArray patchArray)
            {
                var current = GetPropertyValue(target, name);
                if (current == null || current.Type == JTokenType.Null)
                {
                    SetProperty(target, name, BuildNewArray(patchArray, source));
                    return;
                }

                if (current is JArray currentArray)
                {
                    ApplyArray(currentArray, patchArray, root, source);
                    return;
                }

                SetProperty(target, name, BuildNewArray(patchArray, source));
                return;
            }

            SetProperty(target, name, patchValue.DeepClone());
        }

        private static void MoveProperty(JObject target, string name, JObject patchObject, JToken moveToken, JObject root, string source)
        {
            var destinationPath = moveToken?.Value<string>();
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new InvalidOperationException($"Legacy patch '{source}' has an empty $moveTo path for '{name}'.");
            }

            var moved = GetPropertyValue(target, name)?.DeepClone() ?? new JObject();
            if (moved is JObject movedObject)
            {
                var remainingPatch = StripDirectives(patchObject);
                ApplyObject(movedObject, remainingPatch, root, source);
            }

            RemoveProperty(target, name);
            SetTokenAtPath(root, destinationPath, moved);
        }

        private static bool TryApplyArrayDirectiveProperty(JObject target, string name, JToken current, JObject patchObject)
        {
            var hasArrayDirective =
                TryGetDirective(patchObject, "$add", out _) ||
                TryGetDirective(patchObject, "$append", out _) ||
                TryGetDirective(patchObject, "$prepend", out _) ||
                TryGetDirective(patchObject, "$insert", out _) ||
                (TryGetDirective(patchObject, "$remove", out var remove) && !IsRemoveDirective(remove)) ||
                (TryGetDirective(patchObject, "$delete", out var delete) && !IsRemoveDirective(delete));
            if (!hasArrayDirective)
            {
                return false;
            }

            var array = current as JArray;
            if (array == null)
            {
                array = new JArray();
                SetProperty(target, name, array);
            }

            if (TryGetDirective(patchObject, "$add", out var add))
            {
                AppendValues(array, add);
            }

            if (TryGetDirective(patchObject, "$append", out var append))
            {
                AppendValues(array, append);
            }

            if (TryGetDirective(patchObject, "$prepend", out var prepend))
            {
                PrependValues(array, prepend);
            }

            if (TryGetDirective(patchObject, "$insert", out var insert))
            {
                AppendValues(array, insert);
            }

            if (TryGetDirective(patchObject, "$remove", out remove))
            {
                RemoveValues(array, remove);
            }

            if (TryGetDirective(patchObject, "$delete", out delete))
            {
                RemoveValues(array, delete);
            }

            return true;
        }

        private static JToken NormalizeNewToken(JObject patchObject, string source)
        {
            if (TryGetDirective(patchObject, "$replace", out var replacement))
            {
                return replacement.DeepClone();
            }

            return patchObject.DeepClone();
        }

        private static JArray BuildNewArray(JArray patchArray, string source)
        {
            var result = new JArray();
            foreach (var item in patchArray)
            {
                if (item is JObject instruction)
                {
                    if (TryGetDirective(instruction, "$replace", out var replace))
                    {
                        result.Clear();
                        AppendValues(result, replace);
                        continue;
                    }

                    if (TryGetDirective(instruction, "$add", out var add))
                    {
                        result.Add(add.DeepClone());
                        continue;
                    }

                    if (TryGetDirective(instruction, "$append", out var append))
                    {
                        AppendValues(result, append);
                        continue;
                    }

                    if (TryGetDirective(instruction, "$prepend", out var prepend))
                    {
                        PrependValues(result, prepend);
                        continue;
                    }

                    if (TryGetDirective(instruction, "$insert", out var insert))
                    {
                        AppendValues(result, insert);
                        continue;
                    }

                    if (TryGetDirective(instruction, "$find", out _) && IsTruthy(GetDirective(instruction, "$optional")))
                    {
                        continue;
                    }

                    if (TryGetDirective(instruction, "$find", out _))
                    {
                        throw new InvalidOperationException($"Legacy patch '{source}' could not find an array item matching $find.");
                    }

                    if (TryGetDirective(instruction, "$remove", out _) ||
                        TryGetDirective(instruction, "$delete", out _))
                    {
                        continue;
                    }
                }

                result.Add(item.DeepClone());
            }

            return result;
        }

        private static void ApplyArray(JArray target, JArray patchArray, JObject root, string source)
        {
            foreach (var item in patchArray)
            {
                if (!(item is JObject instruction))
                {
                    target.Add(item.DeepClone());
                    continue;
                }

                if (TryGetDirective(instruction, "$add", out var add))
                {
                    target.Add(add.DeepClone());
                    continue;
                }

                if (TryGetDirective(instruction, "$append", out var append))
                {
                    AppendValues(target, append);
                    continue;
                }

                if (TryGetDirective(instruction, "$prepend", out var prepend))
                {
                    PrependValues(target, prepend);
                    continue;
                }

                if (TryGetDirective(instruction, "$insert", out var insert))
                {
                    AppendValues(target, insert);
                    continue;
                }

                if (TryGetDirective(instruction, "$replace", out var replace) &&
                    !TryGetDirective(instruction, "$find", out _))
                {
                    target.Clear();
                    AppendValues(target, replace);
                    continue;
                }

                if (TryGetDirective(instruction, "$find", out var findToken))
                {
                    ApplyFindInstruction(target, instruction, findToken, root, source);
                    continue;
                }

                if (TryGetDirective(instruction, "$remove", out var remove) ||
                    TryGetDirective(instruction, "$delete", out remove))
                {
                    RemoveValues(target, remove);
                    continue;
                }

                throw new InvalidOperationException(
                    $"Legacy patch '{source}' has an array object without $add, $append, $prepend, $insert, $replace, $remove, or $find.");
            }
        }

        private static void ApplyFindInstruction(JArray target, JObject instruction, JToken findToken, JObject root, string source)
        {
            var conditions = findToken as JArray ?? new JArray(findToken);
            for (var index = 0; index < target.Count; index++)
            {
                var candidate = target[index];
                if (!MatchesAll(candidate, conditions))
                {
                    continue;
                }

                if (IsTruthy(GetDirective(instruction, "$clone")))
                {
                    var clone = candidate.DeepClone();
                    if (clone is JObject cloneObject)
                    {
                        ApplyObject(cloneObject, StripDirectives(instruction), root, source);
                    }

                    target.Add(clone);
                    return;
                }

                if (TryGetDirective(instruction, "$replace", out var replacement))
                {
                    target[index] = replacement.DeepClone();
                    return;
                }

                if (IsRemovePatch(instruction))
                {
                    target.RemoveAt(index);
                    return;
                }

                if (candidate is JObject candidateObject)
                {
                    ApplyObject(candidateObject, StripDirectives(instruction), root, source);
                    return;
                }

                return;
            }

            if (!IsTruthy(GetDirective(instruction, "$optional")))
            {
                throw new InvalidOperationException($"Legacy patch '{source}' could not find an array item matching $find.");
            }
        }

        private static bool MatchesAll(JToken candidate, JArray conditions)
        {
            foreach (var conditionToken in conditions)
            {
                if (!(conditionToken is JObject condition))
                {
                    continue;
                }

                if (!Matches(candidate, condition))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Matches(JToken candidate, JObject condition)
        {
            var path = ReadString(condition, "path");
            var expected = condition["value"];
            var comparison = ReadString(condition, "comp") ?? ReadString(condition, "comparison") ?? "Equals";
            var actual = string.IsNullOrWhiteSpace(path) ? candidate : SelectToken(candidate, path);

            switch ((comparison ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "exists":
                    return actual != null && actual.Type != JTokenType.Null;
                case "notexists":
                case "not_exists":
                case "not-exists":
                    return actual == null || actual.Type == JTokenType.Null;
                case "notequals":
                case "not_equals":
                case "not-equals":
                case "!=":
                    return !LegacyTokenEquals(actual, expected);
                case "startswith":
                case "starts_with":
                case "starts-with":
                    return StringValue(actual).StartsWith(StringValue(expected), StringComparison.OrdinalIgnoreCase);
                case "endswith":
                case "ends_with":
                case "ends-with":
                    return StringValue(actual).EndsWith(StringValue(expected), StringComparison.OrdinalIgnoreCase);
                case "contains":
                    return StringValue(actual).IndexOf(StringValue(expected), StringComparison.OrdinalIgnoreCase) >= 0;
                default:
                    return LegacyTokenEquals(actual, expected);
            }
        }

        private static JToken SelectToken(JToken token, string path)
        {
            if (token == null || string.IsNullOrWhiteSpace(path))
            {
                return token;
            }

            try
            {
                var selected = token.SelectToken(path, false);
                if (selected != null)
                {
                    return selected;
                }
            }
            catch
            {
                // Fall through to simple path traversal below.
            }

            var current = token;
            foreach (var part in path.Split(new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!(current is JObject obj))
                {
                    return null;
                }

                current = obj.Properties()
                    .FirstOrDefault(property => string.Equals(property.Name, part, StringComparison.OrdinalIgnoreCase))
                    ?.Value;
                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }

        private static void AppendValues(JArray target, JToken values)
        {
            if (values is JArray array)
            {
                foreach (var value in array)
                {
                    target.Add(value.DeepClone());
                }
            }
            else if (values != null && values.Type != JTokenType.Null)
            {
                target.Add(values.DeepClone());
            }
        }

        private static void PrependValues(JArray target, JToken values)
        {
            var materialized = MaterializeArrayValues(values).ToArray();
            for (var index = materialized.Length - 1; index >= 0; index--)
            {
                target.Insert(0, materialized[index]);
            }
        }

        private static IEnumerable<JToken> MaterializeArrayValues(JToken values)
        {
            if (values is JArray array)
            {
                foreach (var value in array)
                {
                    yield return value.DeepClone();
                }
            }
            else if (values != null && values.Type != JTokenType.Null)
            {
                yield return values.DeepClone();
            }
        }

        private static void RemoveValues(JArray target, JToken values)
        {
            if (target == null || values == null || values.Type == JTokenType.Null || IsRemoveDirective(values))
            {
                return;
            }

            var removeValues = MaterializeArrayValues(values).ToArray();
            for (var index = target.Count - 1; index >= 0; index--)
            {
                if (removeValues.Any(removeValue => LegacyTokenEquals(target[index], removeValue)))
                {
                    target.RemoveAt(index);
                }
            }
        }

        private static bool LegacyTokenEquals(JToken actual, JToken expected)
        {
            if (actual == null || expected == null)
            {
                return actual == expected;
            }

            if (JToken.DeepEquals(actual, expected))
            {
                return true;
            }

            return string.Equals(StringValue(actual), StringValue(expected), StringComparison.OrdinalIgnoreCase);
        }

        private static JObject StripDirectives(JObject source)
        {
            var result = new JObject();
            foreach (var property in source.Properties())
            {
                if (!IsDirective(property.Name))
                {
                    result[property.Name] = property.Value.DeepClone();
                }
            }

            return result;
        }

        private static void SetTokenAtPath(JObject root, string path, JToken value)
        {
            var parts = path.Split(new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return;
            }

            var current = root;
            for (var index = 0; index < parts.Length - 1; index++)
            {
                var part = parts[index];
                if (!(GetPropertyValue(current, part) is JObject next))
                {
                    next = new JObject();
                    SetProperty(current, part, next);
                }

                current = next;
            }

            SetProperty(current, parts[parts.Length - 1], value.DeepClone());
        }

        private static JToken GetPropertyValue(JObject obj, string name)
        {
            return TryGetProperty(obj, name, out var property) ? property.Value : null;
        }

        private static void SetProperty(JObject obj, string name, JToken value)
        {
            if (obj == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (TryGetProperty(obj, name, out var property))
            {
                property.Value = value ?? JValue.CreateNull();
                return;
            }

            obj[name] = value ?? JValue.CreateNull();
        }

        private static void RemoveProperty(JObject obj, string name)
        {
            if (TryGetProperty(obj, name, out var property))
            {
                property.Remove();
            }
        }

        private static bool TryGetProperty(JObject obj, string name, out JProperty property)
        {
            property = null;
            if (obj == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            property = obj.Properties()
                .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            return property != null;
        }

        internal static bool IsRemovePatch(JObject patchObject)
        {
            return IsRemoveDirective(GetDirective(patchObject, "$remove")) ||
                   IsRemoveDirective(GetDirective(patchObject, "$delete"));
        }

        internal static bool IsDirective(string name)
        {
            return DirectiveKeys.Contains(name ?? string.Empty);
        }

        private static bool TryGetDirective(JObject obj, string name, out JToken value)
        {
            value = GetDirective(obj, name);
            return value != null;
        }

        private static JToken GetDirective(JObject obj, string name)
        {
            if (obj == null)
            {
                return null;
            }

            return obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var value) ? value : null;
        }

        private static bool IsRemoveDirective(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return false;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }

            if (token.Type == JTokenType.Integer)
            {
                return token.Value<int>() != 0;
            }

            return token.Type == JTokenType.String &&
                   string.Equals(token.Value<string>(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTruthy(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return false;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }

            if (token.Type == JTokenType.Integer)
            {
                return token.Value<int>() != 0;
            }

            return string.Equals(token.Value<string>(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadString(JObject obj, string property)
        {
            return obj != null && obj.TryGetValue(property, StringComparison.OrdinalIgnoreCase, out var token)
                ? token.Value<string>()
                : null;
        }

        private static string StringValue(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return string.Empty;
            }

            return token is JValue value ? value.Value?.ToString() ?? string.Empty : token.ToString();
        }
    }
}
