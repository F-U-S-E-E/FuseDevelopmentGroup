using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Fuse.LiveHarness.Json;

public enum JsonDeltaKind
{
    Added,
    Removed,
    Changed,
}

/// <summary>A single difference between a baseline and a current value, keyed by JSON path.</summary>
public sealed record JsonDelta(string Path, JsonDeltaKind Kind, string? Left, string? Right);

/// <summary>
/// Structural diff of two (normally already-normalized) JSON trees, producing path-keyed deltas
/// so a failure points at exactly what drifted. Compare normalized trees from <see cref="JsonNormalizer"/>.
/// </summary>
public static class JsonDiff
{
    public static IReadOnlyList<JsonDelta> Compare(JToken baseline, JToken current)
    {
        var deltas = new List<JsonDelta>();
        Walk("$", baseline, current, deltas);
        return deltas;
    }

    private static void Walk(string path, JToken? left, JToken? right, List<JsonDelta> deltas)
    {
        if (left is null && right is null)
        {
            return;
        }

        if (left is null)
        {
            deltas.Add(new JsonDelta(path, JsonDeltaKind.Added, null, Short(right)));
            return;
        }

        if (right is null)
        {
            deltas.Add(new JsonDelta(path, JsonDeltaKind.Removed, Short(left), null));
            return;
        }

        if (left.Type != right.Type)
        {
            deltas.Add(new JsonDelta(path, JsonDeltaKind.Changed, Short(left), Short(right)));
            return;
        }

        switch (left)
        {
            case JObject leftObject:
                var rightObject = (JObject)right;
                foreach (var name in leftObject.Properties().Select(p => p.Name)
                             .Union(rightObject.Properties().Select(p => p.Name)))
                {
                    Walk($"{path}.{name}", leftObject[name], rightObject[name], deltas);
                }

                break;

            case JArray leftArray:
                var rightArray = (JArray)right;
                var max = Math.Max(leftArray.Count, rightArray.Count);
                for (var i = 0; i < max; i++)
                {
                    Walk($"{path}[{i}]", i < leftArray.Count ? leftArray[i] : null, i < rightArray.Count ? rightArray[i] : null, deltas);
                }

                break;

            default:
                if (!JToken.DeepEquals(left, right))
                {
                    deltas.Add(new JsonDelta(path, JsonDeltaKind.Changed, Short(left), Short(right)));
                }

                break;
        }
    }

    private static string Short(JToken? token)
    {
        var text = token?.ToString(Formatting.None) ?? "null";
        return text.Length > 120 ? string.Concat(text.AsSpan(0, 117), "...") : text;
    }
}
