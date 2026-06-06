using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Port of the <c>convert_load</c> / <c>convert_industry</c> /
    /// <c>convert_loader</c> / <c>convert_turntable</c> /
    /// <c>convert_station</c> family from the Python source. Each
    /// returns a JObject in the FUSE-canonical shape ready to drop
    /// into the fragment's <c>operations.*</c> dictionary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The deep component dispatcher (Python <c>convert_component</c>)
    /// has ~40 specialised type inferences for things like passenger
    /// stops, freight stops, fuel stops, etc. Industries here pass
    /// component payloads through with a shallow cleanup; the
    /// component-type inference can land in a separate pass once the
    /// editor has a UI for the most common kinds.
    /// </para>
    /// </remarks>
    internal static class LegacyOperationsConverter
    {
        /// <summary>
        /// Canonical schema keys for a FUSE load. The Python source
        /// uses a normalised lowercase set; anything else in the legacy
        /// item gets folded into a <c>fields</c> dict so custom data
        /// round-trips without the converter needing to know about it.
        /// </summary>
        private static readonly HashSet<string> LoadSchemaKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "name", "description", "units", "density",
            "unitWeightInPounds", "importable", "payPerQuantity",
            "costPerUnit", "carTypeFilter", "emptyCarType",
            "loadedCarType", "icon", "fields",
        };

        public static JObject ConvertLoad(string loadId, JToken legacy)
        {
            var obj = legacy as JObject;
            var result = new JObject
            {
                ["name"] = obj?.Value<string>("name") ?? obj?.Value<string>("description") ?? loadId,
                ["units"] = obj?.Value<string>("units") ?? "Quantity",
                ["density"] = obj?["density"]?.DeepClone(),
                ["unitWeightInPounds"] = obj?["unitWeightInPounds"]?.DeepClone(),
                ["importable"] = obj?["importable"]?.DeepClone(),
                ["payPerQuantity"] = obj?["payPerQuantity"]?.DeepClone(),
                ["costPerUnit"] = obj?["costPerUnit"]?.DeepClone(),
                ["carTypeFilter"] = obj?["carTypeFilter"]?.DeepClone(),
            };

            // Custom fields: anything outside the canonical key set
            // gets folded into a `fields` dict. Explicit `fields` on
            // the legacy item wins over inferred entries (matches the
            // Python `setdefault` order).
            var fields = new JObject();
            if (obj?["fields"] is JObject explicitFields)
            {
                foreach (var kv in explicitFields)
                {
                    fields[kv.Key] = kv.Value?.DeepClone();
                }
            }
            if (obj != null)
            {
                foreach (var prop in obj.Properties())
                {
                    if (prop.Value == null || prop.Value.Type == JTokenType.Null) continue;
                    if (LoadSchemaKeys.Contains(prop.Name)) continue;
                    if (!fields.ContainsKey(prop.Name))
                    {
                        fields[prop.Name] = prop.Value.DeepClone();
                    }
                }
            }
            if (fields.Count > 0)
            {
                result["fields"] = fields;
            }

            return JsonCleanHelper.CleanObject(result);
        }

        /// <summary>
        /// Port of <c>convert_industry</c>. Each child component
        /// flows through
        /// <see cref="LegacyIndustryComponentConverter.ConvertComponent"/>
        /// to pick up type inference + custom-field bucketing. The
        /// industry-level inference context (which loads are inputs
        /// vs outputs across the WHOLE industry) is built once up
        /// front and threaded into every component conversion.
        /// </summary>
        public static JObject ConvertIndustry(string industryId, JToken legacy, string areaId, int? order,
                                              string sourceName = null,
                                              List<Models.FuseConversionReportEntry> report = null)
        {
            var obj = legacy as JObject;
            var components = new JObject();
            var componentsSource = obj?["components"] as JObject;
            var context = LegacyIndustryComponentConverter.BuildInferenceContext(componentsSource);

            if (componentsSource != null)
            {
                // Use a temp dict to support sub-id generation /
                // collision detection (the converted JObject grows
                // as we add entries, and we want stable iteration
                // over input keys).
                var existing = new Dictionary<string, JToken>(System.StringComparer.Ordinal);
                foreach (var prop in componentsSource.Properties())
                {
                    if (!(prop.Value is JObject componentObj)) continue;

                    var converted = LegacyIndustryComponentConverter.ConvertComponent(prop.Name, componentObj, context);
                    var subId = LegacyIndustryComponentConverter.MakeComponentSubId(industryId, prop.Name, converted, existing, report);
                    LegacyIndustryComponentConverter.FlagSpanlessPassengerStop(industryId, subId, converted, sourceName, report);
                    existing[subId] = converted;
                    components[subId] = converted;
                }
            }

            var result = new JObject
            {
                ["name"] = obj?.Value<string>("name") ?? industryId,
                ["areaId"] = areaId ?? obj?.Value<string>("areaId") ?? obj?.Value<string>("area"),
                ["order"] = order.HasValue ? (JToken)order.Value : null,
                ["position"] = VectorHelper.Vector(obj?["localPosition"] ?? obj?["position"]),
                ["rotation"] = VectorHelper.Vector(obj?["localRotation"] ?? obj?["rotation"]),
                ["usesContract"] = obj?.Value<bool?>("usesContract") ?? false,
                ["components"] = components,
            };
            return LegacyIndustryComponentConverter.PrunePythonStyle(result);
        }

        public static JObject ConvertTurntable(string tableId, JToken legacy)
        {
            var obj = legacy as JObject;
            var result = new JObject
            {
                ["position"] = VectorHelper.Vector(obj?["position"] ?? obj?["localPosition"]),
                ["rotation"] = VectorHelper.Vector(obj?["rotation"] ?? obj?["localRotation"]),
                ["radius"] = obj?.Value<double?>("radius") ?? obj?.Value<double?>("Radius") ?? 15.0,
                ["subdivisions"] = obj?.Value<int?>("subdivisions") ?? obj?.Value<int?>("Subdivisions") ?? 32,
                ["legacyIdentifier"] = obj?.Value<string>("legacyIdentifier"),
            };

            // Roundhouse data lives in one of two shapes: a nested
            // `roundhouse` object, or flat `roundhouseStalls` + sibling
            // keys. Match the Python behaviour: prefer the nested form
            // when present, fall back to the flat form.
            if (obj?["roundhouse"] is JObject rh)
            {
                result["roundhouse"] = new JObject
                {
                    ["stalls"] = rh.Value<int?>("stalls") ?? 0,
                    ["startAngle"] = rh.Value<double?>("startAngle") ?? 0.0,
                    ["stallAngle"] = rh["stallAngle"]?.DeepClone(),
                    ["trackLength"] = rh.Value<double?>("trackLength") ?? 46.0,
                    ["startPrefab"] = rh.Value<string>("startPrefab"),
                    ["endPrefab"] = rh.Value<string>("endPrefab"),
                    ["stallPrefab"] = rh.Value<string>("stallPrefab"),
                };
            }
            else
            {
                var stalls = obj?.Value<int?>("roundhouseStalls") ?? obj?.Value<int?>("RoundhouseStalls");
                if (stalls.HasValue && stalls.Value > 0)
                {
                    result["roundhouse"] = new JObject
                    {
                        ["stalls"] = stalls.Value,
                        ["trackLength"] = obj?.Value<double?>("roundhouseTrackLength") ?? obj?.Value<double?>("RoundhouseTrackLength") ?? 46.0,
                        ["startPrefab"] = obj?.Value<string>("startPrefab") ?? obj?.Value<string>("StartPrefab") ?? "vanilla://roundhouseStart",
                        ["endPrefab"] = obj?.Value<string>("endPrefab") ?? obj?.Value<string>("EndPrefab") ?? "vanilla://roundhouseEnd",
                        ["stallPrefab"] = obj?.Value<string>("stallPrefab") ?? obj?.Value<string>("StallPrefab") ?? "vanilla://roundhouseStall",
                    };
                }
            }

            return JsonCleanHelper.CleanObject(result);
        }

        public static JObject ConvertLoader(JToken legacy)
        {
            var obj = legacy as JObject;
            var result = new JObject
            {
                ["position"] = VectorHelper.Vector(obj?["position"] ?? obj?["localPosition"]),
                ["rotation"] = VectorHelper.Vector(obj?["rotation"] ?? obj?["localRotation"]),
                ["prefab"] = obj?.Value<string>("prefab") ?? "empty://",
                ["industryId"] = obj?.Value<string>("industry"),
            };
            return JsonCleanHelper.CleanObject(result);
        }

        public static JObject ConvertStation(JToken legacy)
        {
            var obj = legacy as JObject;
            var result = new JObject
            {
                ["position"] = VectorHelper.Vector(obj?["position"] ?? obj?["localPosition"]),
                ["rotation"] = VectorHelper.Vector(obj?["rotation"] ?? obj?["localRotation"]),
                ["prefab"] = obj?.Value<string>("prefab") ?? "empty://",
                ["passengerStopId"] = obj?.Value<string>("passengerStop"),
            };
            return JsonCleanHelper.CleanObject(result);
        }
    }
}
