using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Fuse.LiveHarness.Json;

/// <summary>
/// Canonicalizes a JSON tree so golden-master comparisons ignore non-deterministic noise:
/// strips volatile keys (timestamps, pids, request ids), rounds floating-point values to a
/// fixed precision (graph coordinates are live Transform reads that wobble in the last ulps),
/// recursively sorts object keys (JSON object/Dictionary key order is not contractual), and —
/// by default — sorts arrays by canonical content so reordering is not flagged as drift.
/// </summary>
public sealed class JsonNormalizer
{
    /// <summary>Per-run fields stripped before comparison (from the dump JSON and bridge state).</summary>
    public static readonly string[] DefaultStripKeys =
    {
        "createdLocal", "pid", "heartbeatUtc", "lastReloadUtc", "lastRequestId", "issuedUtc", "completedUtc",
    };

    private readonly HashSet<string> _stripKeys;
    private readonly int _decimals;
    private readonly bool _sortArrays;

    public JsonNormalizer(IEnumerable<string>? stripKeys = null, int decimals = 4, bool sortArrays = true)
    {
        _stripKeys = new HashSet<string>(stripKeys ?? DefaultStripKeys, StringComparer.OrdinalIgnoreCase);
        _decimals = decimals;
        _sortArrays = sortArrays;
    }

    public JToken Normalize(JToken token)
    {
        switch (token)
        {
            case JObject obj:
                var normalizedObject = new JObject();
                foreach (var property in obj.Properties()
                             .Where(p => !_stripKeys.Contains(p.Name))
                             .OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    normalizedObject.Add(property.Name, Normalize(property.Value));
                }

                return normalizedObject;

            case JArray array:
                var items = array.Select(Normalize).ToList();
                if (_sortArrays)
                {
                    items.Sort((a, b) => string.CompareOrdinal(a.ToString(Formatting.None), b.ToString(Formatting.None)));
                }

                return new JArray(items);

            case JValue value when value.Type == JTokenType.Float:
                return new JValue(Math.Round(value.Value<double>(), _decimals));

            default:
                return token.DeepClone();
        }
    }

    /// <summary>Normalize and render as stable, indented JSON (what gets stored as a baseline).</summary>
    public string Canonical(JToken token) => Normalize(token).ToString(Formatting.Indented);

    public string Canonical(string json) => Canonical(JToken.Parse(json));
}
