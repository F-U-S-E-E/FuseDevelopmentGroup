using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FUSE.Converter.Models;
using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Deep port of the Python <c>convert_component</c> family —
    /// industry-component normalisation, type inference, partial-shape
    /// detection, custom-field bucketing, and sub-id generation.
    /// </summary>
    /// <remarks>
    /// The big design decision here mirrors the Python source: a
    /// component's <c>type</c> is inferred from (in priority order)
    /// an explicit <c>type</c> field, a normalisation of its dict
    /// key, the shape of its other fields (input/output terms,
    /// teamProfiles, etc.), and finally whether the parent industry's
    /// other components mention it as an input or output. Falling
    /// out at the bottom, "loader" is the conservative default.
    /// </remarks>
    internal static class LegacyIndustryComponentConverter
    {
        /// <summary>
        /// Aggregate of which load ids are mentioned as inputs vs
        /// outputs across an industry's other components. Used by
        /// <see cref="InferComponentType"/> to disambiguate a
        /// bare-id component as a loader (output) or unloader (input).
        /// </summary>
        public sealed class InferenceContext
        {
            public HashSet<string> Inputs { get; } = new HashSet<string>(StringComparer.Ordinal);
            public HashSet<string> Outputs { get; } = new HashSet<string>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Builds an <see cref="InferenceContext"/> from the components
        /// dictionary on the parent industry. Walks each component's
        /// <c>inputTermsPerDay</c> / <c>outputTermsPerDay</c> dict
        /// and collects the load ids.
        /// </summary>
        public static InferenceContext BuildInferenceContext(JObject components)
        {
            var ctx = new InferenceContext();
            if (components == null) return ctx;

            foreach (var prop in components.Properties())
            {
                if (!(prop.Value is JObject component)) continue;

                if (component["inputTermsPerDay"] is JObject inputs)
                {
                    foreach (var loadProp in inputs.Properties())
                    {
                        var trimmed = loadProp.Name?.Trim();
                        if (!string.IsNullOrEmpty(trimmed)) ctx.Inputs.Add(trimmed);
                    }
                }

                if (component["outputTermsPerDay"] is JObject outputs)
                {
                    foreach (var loadProp in outputs.Properties())
                    {
                        var trimmed = loadProp.Name?.Trim();
                        if (!string.IsNullOrEmpty(trimmed)) ctx.Outputs.Add(trimmed);
                    }
                }
            }

            return ctx;
        }

        /// <summary>
        /// Port of <c>normalize_component_type</c>. Lowercases the
        /// input, looks it up in
        /// <see cref="LegacyConverterConstants.ComponentTypeAliases"/>,
        /// returning the alias target or the original string unchanged.
        /// </summary>
        public static string NormalizeComponentType(string componentType)
        {
            var value = (componentType ?? string.Empty).Trim();
            var normalized = value.ToLowerInvariant();
            if (LegacyConverterConstants.ComponentTypeAliases.TryGetValue(normalized, out var mapped))
            {
                return mapped;
            }
            return value;
        }

        public static bool IsSupportedCustomComponentType(string componentType)
        {
            var normalized = (NormalizeComponentType(componentType) ?? string.Empty).Trim().ToLowerInvariant();
            return LegacyConverterConstants.SupportedCustomIndustryComponentTypes.Contains(normalized);
        }

        // Field-shape probes used by infer_component_type and
        // should_convert_component_as_partial. Keep these as simple
        // ContainsKey checks against the legacy item so caller code
        // stays declarative.

        private static readonly string[] LegacyLoadOperationKeys =
        {
            "loadId", "LoadId", "load",
            "convertedLoadId", "convertedLoad",
            "maxStorage", "MaxStorage",
            "storageChangeRate", "StorageChangeRate",
            "carTransferRate", "CarTransferRate",
            "costPerUnit", "notBeforeHour", "notAfterHour",
            "fillPercentage", "bookReasons", "title",
            "orderAroundEmpties", "orderAroundLoaded",
        };

        public static bool HasLegacyLoadOperationShape(JObject item)
        {
            if (item == null) return false;
            return LegacyLoadOperationKeys.Any(k => item.ContainsKey(k));
        }

        // Only real track-span keys make a component a standalone load
        // operation. A bare loadId is NOT a binding: a spanless load-op
        // block (e.g. a Production-Tweaks patch that only adjusts
        // storageChangeRate/maxStorage/carTransferRate on an existing
        // base-game loader) is a partial field-merge onto the component
        // that already owns the spans, not a brand-new loader. Counting
        // loadId here forces such patches to convert as full loaders,
        // which then fail the "loader requires at least one track span"
        // validation rule and fault the whole package.
        private static readonly string[] LoadComponentBindingKeys =
        {
            "trackSpanIds", "trackSpans", "spans",
        };

        public static bool HasLoadComponentBindingShape(JObject item)
        {
            if (item == null) return false;
            return LoadComponentBindingKeys.Any(k => item.ContainsKey(k));
        }

        public static bool HasStandaloneComponentShape(JObject item)
        {
            if (item == null) return false;
            return
                item["inputTermsPerDay"] != null
                || item["outputTermsPerDay"] != null
                || item["teamProfiles"] != null
                || item["passengerStopId"] != null
                || item["passengerStop"] != null
                || item["timetableCode"] != null
                || item["basePopulation"] != null
                || item["canOverhaul"] != null
                || HasLegacyLoadOperationShape(item);
        }

        public static bool ShouldConvertComponentAsPartial(JObject item)
        {
            if (item == null) return false;
            var explicitType = item.Value<string>("type") ?? item.Value<string>("Type");
            if (!string.IsNullOrWhiteSpace(explicitType))
            {
                var normalizedType = NormalizeComponentType(explicitType);
                var isLegacyRuntimeType = !string.Equals(
                    explicitType.Trim(),
                    normalizedType,
                    StringComparison.OrdinalIgnoreCase);
                return isLegacyRuntimeType
                    && HasComponentPatchPayload(item)
                    && RequiresTrackSpan(normalizedType)
                    && normalizedType != "passengerStop"
                    && !HasLoadComponentBindingShape(item);
            }
            // No standalone shape → partial.
            if (!HasStandaloneComponentShape(item)) return true;
            // Legacy load-op fields without span/load binding → partial.
            return HasLegacyLoadOperationShape(item) && !HasLoadComponentBindingShape(item);
        }

        private static bool HasComponentPatchPayload(JObject item)
        {
            return item != null && item.Properties().Any(property =>
                !string.Equals(property.Name, "type", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(property.Name, "name", StringComparison.OrdinalIgnoreCase));
        }

        private static bool RequiresTrackSpan(string normalizedType)
        {
            switch (normalizedType)
            {
                case "loader":
                case "unloader":
                case "repairTrack":
                case "teamTrack":
                case "interchange":
                case "interchangedLoader":
                case "interchangedUnloader":
                case "progression":
                case "passengerStop":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Port of <c>infer_component_type</c>. Tries (in order):
        /// explicit <c>type</c>, normalised component id, shape
        /// heuristics, and finally the input/output inference context.
        /// Falls back to "loader" as the conservative default.
        /// </summary>
        public static string InferComponentType(string componentId, JObject item, InferenceContext context)
        {
            var explicitType = item?.Value<string>("type") ?? item?.Value<string>("Type");
            if (!string.IsNullOrEmpty(explicitType))
            {
                return NormalizeComponentType(explicitType);
            }

            var normalizedIdType = NormalizeComponentType(componentId);
            if (LegacyConverterConstants.CanonicalComponentTypes.Contains(normalizedIdType) ||
                IsSupportedCustomComponentType(normalizedIdType))
            {
                return normalizedIdType;
            }

            if (item?["inputTermsPerDay"] != null || item?["outputTermsPerDay"] != null)
            {
                return "formulaic";
            }
            if (item?["teamProfiles"] != null)
            {
                return "teamTrack";
            }
            if (item?["passengerStopId"] != null
                || item?["passengerStop"] != null
                || item?["timetableCode"] != null
                || item?["basePopulation"] != null)
            {
                return "passengerStop";
            }
            if (item?["canOverhaul"] != null)
            {
                return "repairTrack";
            }

            var rawId = (componentId ?? string.Empty).Trim();
            if (context != null && !string.IsNullOrEmpty(rawId))
            {
                if (context.Inputs.Contains(rawId) && !context.Outputs.Contains(rawId)) return "unloader";
                if (context.Outputs.Contains(rawId) && !context.Inputs.Contains(rawId)) return "loader";
            }

            return "loader";
        }

        /// <summary>
        /// Port of <c>infer_load_id_from_component_id</c>. When a
        /// component looks like a load-binding type AND doesn't
        /// declare its own load id, reuse the component id as the
        /// load id (legacy convention: the component is "the loader
        /// for the load whose name matches it").
        /// </summary>
        public static string InferLoadIdFromComponentId(string componentId, string componentType, JObject item)
        {
            var raw = (componentId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(raw)) return null;
            if (componentType == null) return null;
            if (!LegacyConverterConstants.LoadComponentTypes.Contains(componentType)) return null;
            if (componentType == "passengerStop" || componentType == "repairTrack") return null;
            if (!HasLegacyLoadOperationShape(item)) return null;
            return raw;
        }

        /// <summary>
        /// Port of <c>collect_custom_component_fields</c>. For an
        /// unknown component type, gather all fields that aren't in
        /// the canonical schema into a single <c>fields</c> dict.
        /// Explicit <c>fields</c> dict on the item wins over inferred
        /// entries (Python uses <c>setdefault</c>).
        /// </summary>
        public static JObject CollectCustomComponentFields(string componentType, JObject item, JObject extra)
        {
            var normalized = (componentType ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalized) || LegacyConverterConstants.CanonicalComponentTypes.Contains(normalized))
            {
                return null;
            }

            var fields = new JObject();
            if (item?["fields"] is JObject explicitFields)
            {
                foreach (var prop in explicitFields.Properties())
                {
                    fields[prop.Name] = prop.Value?.DeepClone();
                }
            }

            void Fold(JObject source)
            {
                if (source == null) return;
                foreach (var prop in source.Properties())
                {
                    if (prop.Value == null || prop.Value.Type == JTokenType.Null) continue;
                    if (LegacyConverterConstants.ComponentSchemaKeys.Contains(prop.Name)) continue;
                    if (!fields.ContainsKey(prop.Name))
                    {
                        fields[prop.Name] = prop.Value.DeepClone();
                    }
                }
            }

            Fold(item);
            Fold(extra);

            return fields.Count > 0 ? fields : null;
        }

        /// <summary>
        /// Port of <c>convert_component</c>. Builds the canonical
        /// FUSE component shape with all the optional fields, fills in
        /// inferred type / load id / sharedStorage, and stashes
        /// non-schema fields under <c>fields</c> for unknown types.
        /// </summary>
        public static JObject ConvertComponent(string componentId, JObject item, InferenceContext context)
        {
            if (item == null) return new JObject();

            var isPartial = ShouldConvertComponentAsPartial(item);
            var componentType = isPartial ? null : InferComponentType(componentId, item, context);
            var isPassenger = componentType == "passengerStop";
            var extra = (item["extraData"] as JObject) ?? (item["ExtraData"] as JObject) ?? new JObject();

            JToken GetField(params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (item.ContainsKey(key)) return item[key];
                }
                foreach (var key in keys)
                {
                    if (extra.ContainsKey(key)) return extra[key];
                }
                return null;
            }

            var loadIdToken = GetField("loadId", "LoadId", "load");
            string loadId = loadIdToken?.Type == JTokenType.String ? loadIdToken.Value<string>() : null;
            if (string.IsNullOrEmpty(loadId))
            {
                if (isPartial)
                {
                    loadId = null;
                }
                else if (isPassenger)
                {
                    loadId = "passengers";
                }
                else
                {
                    loadId = InferLoadIdFromComponentId(componentId, componentType, item);
                }
            }

            var result = new JObject
            {
                ["partial"] = isPartial ? (JToken)true : null,
                ["type"] = isPartial ? null : (JToken)componentType,
                ["name"] = isPartial
                    ? item.Value<string>("name")
                    : (JToken)(item.Value<string>("name") ?? componentId),
                ["trackSpanIds"] = FirstNonNull(item, "trackSpanIds", "trackSpans", "spans") ?? new JArray(),
                ["carTypeFilter"] = item["carTypeFilter"]?.DeepClone(),
                ["loadId"] = loadId,
                ["convertedLoadId"] = GetField("convertedLoadId", "convertedLoadID", "convertedLoad", "ConvertedLoadId")?.DeepClone(),
                ["sharedStorage"] = isPartial ? null : (JToken)(item.Value<bool?>("sharedStorage") ?? true),
                ["storageChangeRate"] = GetField("storageChangeRate", "StorageChangeRate")?.DeepClone(),
                ["maxStorage"] = GetField("maxStorage", "MaxStorage")?.DeepClone(),
                ["carTransferRate"] = GetField("carTransferRate", "CarTransferRate")?.DeepClone(),
                ["costPerUnit"] = GetField("costPerUnit")?.DeepClone(),
                ["notBeforeHour"] = GetField("notBeforeHour")?.DeepClone(),
                ["notAfterHour"] = GetField("notAfterHour")?.DeepClone(),
                ["fillPercentage"] = GetField("fillPercentage")?.DeepClone(),
                ["bookReasons"] = GetField("bookReasons")?.DeepClone(),
                ["title"] = GetField("title")?.DeepClone(),
                ["orderAroundEmpties"] = item["orderAroundEmpties"]?.DeepClone(),
                ["orderAroundLoaded"] = item["orderAroundLoaded"]?.DeepClone(),
                ["inputSpanIds"] = item["inputSpanIds"]?.DeepClone(),
                ["outputSpanIds"] = item["outputSpanIds"]?.DeepClone(),
                ["inputTermsPerDay"] = (item["inputTermsPerDay"] as JObject)?.DeepClone() ?? new JObject(),
                ["outputTermsPerDay"] = (item["outputTermsPerDay"] as JObject)?.DeepClone() ?? new JObject(),
                ["idealCars"] = item["idealCars"]?.DeepClone(),
                ["teamProfiles"] = (item["teamProfiles"] as JObject)?.DeepClone() ?? new JObject(),
                ["canOverhaul"] = item["canOverhaul"]?.DeepClone(),
                ["passengerStopId"] = item.Value<string>("passengerStopId") ?? (isPassenger ? componentId : null),
                ["timetableCode"] = item["timetableCode"]?.DeepClone(),
                ["basePopulation"] = item["basePopulation"]?.DeepClone(),
                ["neighborIds"] = item["neighborIds"]?.DeepClone(),
                ["branch"] = item["branch"]?.DeepClone(),
                ["branchDefinitions"] = item["branchDefinitions"]?.DeepClone() ?? item["branches"]?.DeepClone(),
                ["carLoadPeriod"] = item["carLoadPeriod"]?.DeepClone(),
                ["carLengthFeet"] = item["carLengthFeet"]?.DeepClone(),
            };

            var customFields = CollectCustomComponentFields(componentType, item, extra);
            if (customFields != null && customFields.Count > 0)
            {
                result["fields"] = customFields;
            }

            return PrunePythonStyle(result);
        }

        /// <summary>
        /// Port of the Python <c>clean</c> as applied to industry
        /// components: in addition to dropping nulls, also drops
        /// empty dicts and empty lists (Python's <c>clean</c> walks
        /// dicts/lists recursively and skips them when they collapse
        /// to <c>{}</c> / <c>[]</c>). This is stricter than
        /// <see cref="JsonCleanHelper.CleanObject"/> which only drops
        /// nulls (matching the dict-walking <c>clean()</c> rather
        /// than the per-property clean we have today).
        /// </summary>
        public static JObject PrunePythonStyle(JObject obj)
        {
            if (obj == null) return null;
            var names = obj.Properties().Select(p => p.Name).ToList();
            foreach (var name in names)
            {
                var value = obj[name];
                var cleaned = PruneToken(value);
                if (cleaned == null)
                {
                    obj.Remove(name);
                }
                else
                {
                    obj[name] = cleaned;
                }
            }
            return obj;
        }

        private static JToken PruneToken(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null) return null;

            if (value is JObject obj)
            {
                var pruned = PrunePythonStyle(obj);
                return pruned == null || pruned.Count == 0 ? null : pruned;
            }

            if (value is JArray arr)
            {
                for (int i = arr.Count - 1; i >= 0; i--)
                {
                    var inner = PruneToken(arr[i]);
                    if (inner == null)
                    {
                        arr.RemoveAt(i);
                    }
                    else
                    {
                        arr[i] = inner;
                    }
                }
                return arr.Count == 0 ? null : (JToken)arr;
            }

            return value;
        }

        /// <summary>
        /// Port of <c>_make_component_sub_id</c>. Generates a stable
        /// sub-id when the legacy component dictionary keyed a
        /// component under an empty string. Picks a deterministic
        /// preferred slug ("formula" for formulaic, "repair" for
        /// repair track, etc.) and disambiguates with a numeric
        /// suffix if it collides with an already-generated id.
        /// </summary>
        public static string MakeComponentSubId(string industryId, string componentId, JObject converted,
                                                 IDictionary<string, JToken> existing,
                                                 List<FuseConversionReportEntry> report)
        {
            var raw = (componentId ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(raw)) return raw;

            var componentType = (converted?.Value<string>("type") ?? string.Empty).Trim();
            string preferred;
            if (componentType == "formulaic") preferred = "formula";
            else if (componentType == "repairTrack") preferred = "repair";
            else if (componentType == "teamTrack") preferred = "teamtrack";
            else if (!string.IsNullOrEmpty(converted?.Value<string>("loadId"))) preferred = converted.Value<string>("loadId");
            else if (!string.IsNullOrEmpty(converted?.Value<string>("name"))) preferred = converted.Value<string>("name");
            else preferred = "component";

            // re.sub(r"[^0-9A-Za-z]+", "-", ...).strip("-").lower()
            var sanitized = Regex.Replace(preferred.Trim().ToLowerInvariant(), "[^0-9a-z]+", "-").Trim('-');
            var baseId = string.IsNullOrEmpty(sanitized) ? "component" : sanitized;

            var subId = baseId;
            int index = 2;
            while (existing.ContainsKey(subId))
            {
                subId = baseId + "-" + index;
                index++;
            }

            ReportEntry(report, FuseConversionReportLevel.Info, sourceFile: null, concept: "industry-component-empty-id",
                message: $"Industry '{industryId}' had a legacy component with a blank id; generated component id '{subId}'.");
            return subId;
        }

        /// <summary>
        /// Port of <c>_flag_spanless_passenger_stop</c>. Passenger
        /// stops with no trackSpans still load as "virtual" stops
        /// (after a runtime relaxation that mirrors AlinasMapMod),
        /// so the converter just annotates the report so the modder
        /// knows to add a platform if they want one.
        /// </summary>
        public static void FlagSpanlessPassengerStop(string industryId, string componentId, JObject converted,
                                                      string sourceName, List<FuseConversionReportEntry> report)
        {
            if (converted == null) return;
            if ((converted.Value<string>("type") ?? string.Empty).Trim() != "passengerStop") return;
            var spans = converted["trackSpanIds"] as JArray;
            if (spans != null && spans.Count > 0) return;

            ReportEntry(report, FuseConversionReportLevel.Warning, sourceName, "passenger-stop-spanless",
                $"Industry '{industryId}' component '{componentId}' is a passengerStop with no trackSpans; " +
                "emitting as a virtual stop. Add 'trackSpans' in the legacy source to give it a physical platform.");
        }

        private static JToken FirstNonNull(JObject obj, params string[] keys)
        {
            if (obj == null) return null;
            foreach (var key in keys)
            {
                var token = obj[key];
                if (token != null && token.Type != JTokenType.Null)
                {
                    return token.DeepClone();
                }
            }
            return null;
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
