using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Converter.Models;
using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Port of <c>convert_legacy_start</c>, <c>convert_progression</c>,
    /// <c>normalize_delivery_direction</c>, <c>bool_dictionary_to_array</c>,
    /// <c>normalize_progression_value</c>,
    /// <c>_iter_progression_section_definitions</c>,
    /// <c>_append_unique_id</c>,
    /// <c>reconcile_progression_section_feature_aliases</c>.
    /// </summary>
    /// <remarks>
    /// Progression is the trickiest section: legacy mods sometimes
    /// reference a section id as if it were a map-feature id, so the
    /// converter has to fabricate alias map-features for those refs
    /// and enable them when the referencing section unlocks.
    /// </remarks>
    internal static class LegacyProgressionConverter
    {
        /// <summary>
        /// Port of <c>normalize_delivery_direction</c>. Legacy mods
        /// use ints (0/1) or text aliases like "toIndustry" / "import";
        /// FUSE expects "loadToIndustry" / "loadFromIndustry".
        /// </summary>
        public static JToken NormalizeDeliveryDirection(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null) return value;
            var text = value.Value<string>()?.Trim().ToLowerInvariant() ?? string.Empty;
            switch (text)
            {
                case "0":
                case "loadtoindustry":
                case "toindustry":
                case "to":
                case "import":
                    return "loadToIndustry";
                case "1":
                case "loadfromindustry":
                case "fromindustry":
                case "from":
                case "export":
                    return "loadFromIndustry";
                default:
                    return value;
            }
        }

        /// <summary>
        /// Port of <c>bool_dictionary_to_array</c>. Legacy progression
        /// uses <c>{ "FeatureX": true, "FeatureY": false }</c> to
        /// express "these features should be enabled"; FUSE expects a
        /// flat array of the enabled keys. Returns null for non-dict
        /// inputs so the caller can fall through to the
        /// general-purpose normaliser.
        /// </summary>
        public static JArray BoolDictionaryToArray(JToken value)
        {
            if (!(value is JObject obj)) return null;
            var result = new JArray();
            foreach (var prop in obj.Properties())
            {
                if (prop.Value == null || prop.Value.Type == JTokenType.Null) continue;
                if (prop.Value.Type == JTokenType.Boolean && !prop.Value.Value<bool>()) continue;
                var text = (prop.Name ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    result.Add(text);
                }
            }
            return result;
        }

        /// <summary>
        /// Port of <c>normalize_progression_value</c>. Recursively
        /// rewrites legacy-shape progression payloads into the FUSE
        /// canonical shape (camelCase renames, delivery-direction
        /// aliases, bool-dict→array fields, displayName/name unification).
        /// </summary>
        public static JToken NormalizeProgressionValue(JToken value)
        {
            if (value is JArray arr)
            {
                var result = new JArray();
                foreach (var item in arr)
                {
                    if (item == null || item.Type == JTokenType.Null) continue;
                    var normalized = NormalizeProgressionValue(item);
                    if (normalized != null)
                    {
                        result.Add(normalized);
                    }
                }
                return result;
            }

            if (!(value is JObject obj)) return value;

            var output = new JObject();
            foreach (var prop in obj.Properties())
            {
                var key = prop.Name;
                var targetKey = key;
                var lower = (key ?? string.Empty).ToLowerInvariant();

                if (lower == "displayname") targetKey = "displayName";
                else if (lower == "name") targetKey = "displayName";
                else if (lower == "defaultenableinsandbox") targetKey = "initiallyEnabled";
                else if (lower == "prerequisites") targetKey = "prerequisiteFeatureIds";
                else if (lower == "industrycomponent") targetKey = "industryComponentId";
                else if (lower == "load") targetKey = "loadId";

                if (lower == "direction")
                {
                    output[targetKey] = NormalizeDeliveryDirection(prop.Value);
                    continue;
                }

                if (targetKey == "industryComponentId" && string.IsNullOrEmpty(prop.Value?.Value<string>()?.Trim()))
                {
                    output[targetKey] = null;
                    continue;
                }

                if (LegacyConverterConstants.BoolDictionaryArrayFields.Contains(targetKey))
                {
                    var normalizedArray = BoolDictionaryToArray(prop.Value);
                    if (normalizedArray != null)
                    {
                        output[targetKey] = normalizedArray;
                        continue;
                    }
                }

                // Skip the legacy spelling when the canonical key
                // already landed (rename collision: keep the first
                // one — matches Python's `if target_key in result and
                // key != target_key: continue` behaviour).
                if (output[targetKey] != null && key != targetKey) continue;

                output[targetKey] = NormalizeProgressionValue(prop.Value);
            }

            return JsonCleanHelper.CleanObject(output);
        }

        /// <summary>
        /// Port of <c>_iter_progression_section_definitions</c>.
        /// Yields (sectionId, sectionObject) pairs from a progression
        /// root, walking the top-level <c>sections</c> dict or list
        /// plus every nested <c>progressions.*.sections</c>.
        /// </summary>
        public static IEnumerable<KeyValuePair<string, JObject>> IterProgressionSectionDefinitions(JObject progressionRoot)
        {
            if (progressionRoot == null) yield break;

            var topSections = progressionRoot["sections"];
            if (topSections is JObject topDict)
            {
                foreach (var prop in topDict.Properties())
                {
                    if (prop.Value is JObject sectionObj)
                    {
                        yield return new KeyValuePair<string, JObject>(prop.Name, sectionObj);
                    }
                }
            }
            else if (topSections is JArray topArr)
            {
                foreach (var item in topArr)
                {
                    if (!(item is JObject section)) continue;
                    var sectionId = section.Value<string>("id") ?? section.Value<string>("identifier");
                    if (!string.IsNullOrEmpty(sectionId))
                    {
                        yield return new KeyValuePair<string, JObject>(sectionId, section);
                    }
                }
            }

            if (progressionRoot["progressions"] is JObject progressions)
            {
                foreach (var progProp in progressions.Properties())
                {
                    if (!(progProp.Value is JObject prog)) continue;
                    var nested = prog["sections"];
                    if (nested is JObject nestedDict)
                    {
                        foreach (var prop in nestedDict.Properties())
                        {
                            if (prop.Value is JObject sectionObj)
                            {
                                yield return new KeyValuePair<string, JObject>(prop.Name, sectionObj);
                            }
                        }
                    }
                    else if (nested is JArray nestedArr)
                    {
                        foreach (var item in nestedArr)
                        {
                            if (!(item is JObject section)) continue;
                            var sectionId = section.Value<string>("id") ?? section.Value<string>("identifier");
                            if (!string.IsNullOrEmpty(sectionId))
                            {
                                yield return new KeyValuePair<string, JObject>(sectionId, section);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Port of <c>_append_unique_id</c>. Coerces an existing
        /// field (which may be null, a string, a bool-dict, or a
        /// list) to a list of strings, then appends
        /// <paramref name="itemId"/> if not already present (case-
        /// insensitive).
        /// </summary>
        public static void AppendUniqueId(JObject container, string field, string itemId)
        {
            if (container == null || string.IsNullOrEmpty(itemId)) return;

            JArray values;
            var existing = container[field];

            if (existing == null || existing.Type == JTokenType.Null)
            {
                values = new JArray();
            }
            else if (existing is JObject dict)
            {
                values = BoolDictionaryToArray(dict) ?? new JArray();
            }
            else if (existing is JArray arr)
            {
                values = (JArray)arr.DeepClone();
            }
            else
            {
                values = new JArray { existing.DeepClone() };
            }

            var text = itemId.Trim();
            if (string.IsNullOrEmpty(text)) return;
            var exists = values.Any(v => string.Equals(v.Value<string>(), text, StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                values.Add(text);
            }
            container[field] = values;
        }

        /// <summary>
        /// Port of <c>reconcile_progression_section_feature_aliases</c>.
        /// Legacy mods sometimes wired a section id directly into a
        /// map-feature reference (prerequisiteFeatureIds etc.); FUSE
        /// expects map-features and sections to be distinct. The
        /// reconciler emits an alias map-feature for each referenced-
        /// but-undefined section id, and updates the referencing
        /// section to enable that alias on unlock.
        /// </summary>
        public static void ReconcileProgressionSectionFeatureAliases(JObject rail, List<FuseConversionReportEntry> report)
        {
            var progressionRoot = rail?["progression"] as JObject;
            var mapFeatures = progressionRoot?["mapFeatures"] as JObject;
            if (mapFeatures == null) return;

            var sectionDefs = new Dictionary<string, List<JObject>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in IterProgressionSectionDefinitions(progressionRoot))
            {
                var key = (kv.Key ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(key)) continue;
                if (!sectionDefs.TryGetValue(key, out var list))
                {
                    list = new List<JObject>();
                    sectionDefs[key] = list;
                }
                list.Add(kv.Value);
            }

            if (sectionDefs.Count == 0) return;

            var referencedFeatures = new HashSet<string>(StringComparer.Ordinal);
            foreach (var featureProp in mapFeatures.Properties())
            {
                if (!(featureProp.Value is JObject feature)) continue;
                foreach (var field in new[] { "prerequisiteFeatureIds", "enableFeaturesOnUnlock", "disableFeaturesOnUnlock" })
                {
                    if (!(feature[field] is JArray ids)) continue;
                    foreach (var entry in ids)
                    {
                        var text = entry.Value<string>()?.Trim();
                        if (!string.IsNullOrEmpty(text))
                        {
                            referencedFeatures.Add(text);
                        }
                    }
                }
            }

            foreach (var featureId in referencedFeatures.OrderBy(s => s, StringComparer.Ordinal))
            {
                if (!sectionDefs.TryGetValue(featureId, out var sections)) continue;
                if (mapFeatures.ContainsKey(featureId)) continue;

                var firstSection = sections[0];
                mapFeatures[featureId] = JsonCleanHelper.CleanObject(new JObject
                {
                    ["displayName"] = firstSection.Value<string>("displayName") ?? featureId,
                    ["description"] = firstSection.Value<string>("description"),
                    ["initiallyEnabled"] = false,
                });

                foreach (var section in sections)
                {
                    AppendUniqueId(section, "enableFeaturesOnUnlock", featureId);
                }

                ReportEntry(report, FuseConversionReportLevel.Info, sourceFile: null,
                    concept: "progression-section-feature-alias",
                    message: $"Progression map feature reference '{featureId}' points to a section id; " +
                             "emitted a FUSE map-feature alias and enabled it when that section unlocks.");
            }
        }

        /// <summary>
        /// Port of <c>convert_progression</c>. Walks the legacy
        /// source's progression payload (top-level + nested
        /// progressions/mapFeatures), normalises each value, and
        /// extends the rail's progression block. Reconciles
        /// section/feature alias references at the end.
        /// </summary>
        public static void ConvertProgression(JObject source, JObject rail, List<FuseConversionReportEntry> report)
        {
            if (source == null || rail == null) return;

            var railProgression = rail["progression"] as JObject;
            if (railProgression == null)
            {
                railProgression = new JObject();
                rail["progression"] = railProgression;
            }

            // Self-assigning a JToken to its existing parent property
            // detaches and re-attaches it (Newtonsoft's
            // EnsureParentToken always calls item.Remove() first); only
            // attach when the property is genuinely missing.
            if (!(railProgression["sections"] is JArray sections))
            {
                sections = new JArray();
                railProgression["sections"] = sections;
            }
            if (!(railProgression["progressions"] is JObject progressions))
            {
                progressions = new JObject();
                railProgression["progressions"] = progressions;
            }
            if (!(railProgression["mapFeatures"] is JObject mapFeatures))
            {
                mapFeatures = new JObject();
                railProgression["mapFeatures"] = mapFeatures;
            }

            if (source["progression"] is JObject progression)
            {
                var pid = progression.Value<string>("progressionId");
                if (!string.IsNullOrEmpty(pid))
                {
                    railProgression["progressionId"] = pid;
                }

                if (progression["sections"] is JArray progSections)
                {
                    var normalized = NormalizeProgressionValue(progSections) as JArray ?? new JArray();
                    // Snapshot via DeepClone — iterating a JArray
                    // while moving items into another container
                    // detaches them mid-iteration and silently
                    // drops every other entry.
                    foreach (var item in normalized) sections.Add(item.DeepClone());
                }

                if (progression["progressions"] is JObject progNested)
                {
                    var normalized = NormalizeProgressionValue(progNested) as JObject;
                    if (normalized != null) Merge(progressions, normalized);
                }

                if (progression["mapFeatures"] is JObject progMapFeatures)
                {
                    var normalized = NormalizeProgressionValue(progMapFeatures) as JObject;
                    if (normalized != null) Merge(mapFeatures, normalized);
                }
            }

            if (source["progressions"] is JObject siblingProgs)
            {
                var normalized = NormalizeProgressionValue(siblingProgs) as JObject;
                if (normalized != null) Merge(progressions, normalized);
            }

            if (source["mapFeatures"] is JObject siblingMf)
            {
                var normalized = NormalizeProgressionValue(siblingMf) as JObject;
                if (normalized != null) Merge(mapFeatures, normalized);
            }

            ReconcileProgressionSectionFeatureAliases(rail, report);
        }

        private static void Merge(JObject target, JObject source)
        {
            // Snapshot property names — iterating a JObject while
            // moving values to another parent detaches them
            // mid-iteration.
            var names = source.Properties().Select(p => p.Name).ToList();
            foreach (var name in names)
            {
                target[name] = source[name]?.DeepClone();
            }
        }

        private static void ReportEntry(List<FuseConversionReportEntry> report, FuseConversionReportLevel level,
                                         string sourceFile, string concept, string message)
        {
            if (report == null) return;
            report.Add(new FuseConversionReportEntry
            {
                Level = level,
                Message = message,
                SourceFile = sourceFile ?? string.Empty,
                Concept = concept,
            });
        }
    }
}
