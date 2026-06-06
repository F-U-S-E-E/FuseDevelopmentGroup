using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Port of <c>convert_scenery</c>, <c>convert_spliney</c>,
    /// <c>convert_label</c>, and <c>convert_telegraph_pole_movements</c>
    /// from the Python source. Each returns a FUSE-shape JObject /
    /// JArray ready to drop into the fragment's <c>world.*</c>
    /// section.
    /// </summary>
    /// <remarks>
    /// The DKW spliney expansion (Python <c>convert_dkw_spliney</c>:
    /// 100+ lines of trigonometry that explodes a crossing-angle item
    /// into multiple track nodes + segments) is NOT in this slice.
    /// It needs the segment graph + node-id mint logic which lands
    /// alongside the span converter.
    /// </remarks>
    internal static class LegacyWorldConverter
    {
        /// <summary>
        /// Strange Customs / AlinasMapMod handler strings → canonical
        /// FUSE spliney type. Sourced from
        /// <see cref="LegacyConverterConstants.HandlerMap"/> so other
        /// converters share the same table.
        /// </summary>
        private static Dictionary<string, string> HandlerMap => LegacyConverterConstants.HandlerMap;

        private static readonly Regex SpeedLimitLabelPattern =
            new Regex(@"^\s*(\d{1,3})\s*MPH\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static JObject ConvertScenery(JToken legacy)
        {
            var obj = legacy as JObject;
            var model = ResolveSceneryModelIdentifier(obj);
            if (!string.IsNullOrEmpty(model) && model.IndexOf("://", System.StringComparison.Ordinal) < 0)
            {
                model = "scenery://" + model;
            }

            var result = new JObject
            {
                ["assetIdentifier"] = model,
                ["position"] = VectorHelper.Vector(obj?["position"] ?? obj?["localPosition"]),
                ["rotation"] = VectorHelper.Vector(obj?["rotation"] ?? obj?["localRotation"]),
                ["scale"] = VectorHelper.Vector(obj?["scale"] ?? obj?["localScale"], defaultScale: true),
            };

            var anchors = LegacyTrackConverter.StringIds(
                obj?["anchorSpanIds"] ?? obj?["spanIds"] ?? obj?["spans"]
                ?? obj?["trackSpanIds"] ?? obj?["trackSpans"]);
            if (anchors != null && anchors.Count > 0)
            {
                result["anchorSpanIds"] = anchors;
            }

            return JsonCleanHelper.CleanObject(result);
        }

        private static string ResolveSceneryModelIdentifier(JObject obj)
        {
            if (obj == null) return null;
            return obj.Value<string>("assetIdentifier")
                ?? obj.Value<string>("model")
                ?? obj.Value<string>("modelIdentifier")
                ?? obj.Value<string>("prefabIdentifier")
                ?? obj.Value<string>("prefab")
                ?? string.Empty;
        }

        /// <summary>
        /// Port of <c>convert_spliney</c>. Maps StrangeCustoms /
        /// AlinasMapMod handler strings to FUSE spliney types via
        /// <see cref="HandlerMap"/>, with one explicit override:
        /// FlowyThingBuilder with style "river" → "river" (the
        /// handler is shared between roads and rivers).
        /// </summary>
        public static JObject ConvertSpliney(JToken legacy)
        {
            var obj = legacy as JObject;
            var handler = obj?.Value<string>("handler") ?? string.Empty;
            var splineyType = InferSplineyType(obj, handler);

            // Strange Customs' FlowyData defaults OffsetY to -0.1.
            // Preserve that explicitly so the FUSE loader doesn't
            // default the missing float to 0.
            var offsetY = obj?.Value<double?>("offsetY") ?? obj?.Value<double?>("offsety");
            if (!offsetY.HasValue && string.Equals(handler, "StrangeCustoms.FlowyThingBuilder", System.StringComparison.Ordinal))
            {
                offsetY = -0.1;
            }

            var points = new JArray();
            if (obj?["points"] is JArray inPoints)
            {
                foreach (var point in inPoints)
                {
                    if (!(point is JObject pointObj)) continue;
                    var converted = new JObject
                    {
                        ["position"] = VectorHelper.Vector(pointObj["position"] ?? pointObj["localPosition"]),
                        ["rotation"] = VectorHelper.Vector(pointObj["rotation"] ?? pointObj["localRotation"]),
                    };
                    if (pointObj["width"] != null && pointObj["width"].Type != JTokenType.Null)
                    {
                        converted["width"] = pointObj.Value<double?>("width") ?? 0.0;
                    }
                    points.Add(converted);
                }
            }

            var result = new JObject
            {
                ["type"] = splineyType,
                ["profile"] = obj?.Value<string>("profile"),
                ["style"] = obj?.Value<string>("style"),
                ["offsetY"] = offsetY.HasValue ? (JToken)offsetY.Value : null,
                ["headStyle"] = obj?.Value<string>("headStyle") ?? obj?.Value<string>("headstyle"),
                ["tailStyle"] = obj?.Value<string>("tailStyle") ?? obj?.Value<string>("tailstyle"),
                ["points"] = points,
            };

            if (!string.IsNullOrEmpty(handler) && !HandlerMap.ContainsKey(handler))
            {
                result["extensions"] = new JObject { ["originalHandler"] = handler };
            }

            return JsonCleanHelper.CleanObject(result);
        }

        private static string InferSplineyType(JObject obj, string handler)
        {
            var style = obj?.Value<string>("style") ?? string.Empty;
            var profile = obj?.Value<string>("profile") ?? string.Empty;

            if (string.Equals(handler, "StrangeCustoms.FlowyThingBuilder", System.StringComparison.Ordinal) &&
                (string.Equals(style, "river", System.StringComparison.OrdinalIgnoreCase) ||
                 profile.IndexOf("river", System.StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return "river";
            }

            if (HandlerMap.TryGetValue(handler ?? string.Empty, out var mapped))
            {
                return mapped;
            }

            var explicitType = obj?.Value<string>("type");
            if (!string.IsNullOrEmpty(explicitType))
            {
                return explicitType;
            }

            return "unknown";
        }

        /// <summary>
        /// Port of <c>convert_label</c>: special-cases an "NN MPH"
        /// text payload into a structured speed-limit label so the
        /// FUSE world layer can render it with the proper sign
        /// style instead of as raw text.
        /// </summary>
        public static JObject ConvertMapLabel(string key, JToken legacy)
        {
            var obj = legacy as JObject;
            var text = obj?.Value<string>("text") ?? key;

            var result = new JObject
            {
                ["text"] = text,
                ["position"] = VectorHelper.Vector(obj?["position"] ?? obj?["localPosition"]),
                ["rotation"] = VectorHelper.Vector(obj?["rotation"] ?? obj?["localRotation"]),
                ["size"] = obj?["size"]?.DeepClone() ?? obj?["fontSize"]?.DeepClone(),
                ["color"] = obj?["color"]?.DeepClone(),
            };

            if (text != null)
            {
                var match = SpeedLimitLabelPattern.Match(text);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var mph))
                {
                    result["text"] = mph.ToString();
                    result["style"] = "speedLimit";
                    result["speedLimitMph"] = mph;
                }
            }

            return JsonCleanHelper.CleanObject(result);
        }

        /// <summary>
        /// Port of <c>convert_telegraph_pole_movements</c>. Legacy
        /// AMM packages encode pole moves as parallel arrays
        /// (polesToMove[i] + poleMovement[i]); FUSE groups them by
        /// offset for compactness so a single offset applies to many
        /// poles.
        /// </summary>
        public static JArray ConvertTelegraphPoleMovements(JToken legacy)
        {
            var obj = legacy as JObject;
            var poles = obj?["polesToMove"] as JArray ?? obj?["PolesToMove"] as JArray;
            var movements = obj?["poleMovement"] as JArray ?? obj?["PoleMovement"] as JArray;

            if (poles == null) return new JArray();

            var grouped = new Dictionary<(double, double, double), JArray>();
            var ordered = new List<(double, double, double)>();

            for (int i = 0; i < poles.Count; i++)
            {
                var pole = poles[i];
                if (pole == null || pole.Type == JTokenType.Null) continue;

                var moveToken = movements != null && i < movements.Count ? movements[i] : null;
                var offset = ParseMovementOffset(moveToken);

                var key = (offset.x, offset.y, offset.z);
                if (!grouped.TryGetValue(key, out var poleIndices))
                {
                    poleIndices = new JArray();
                    grouped[key] = poleIndices;
                    ordered.Add(key);
                }
                poleIndices.Add(pole.Value<int?>() ?? 0);
            }

            var result = new JArray();
            foreach (var key in ordered)
            {
                var entry = new JObject
                {
                    ["poleIndices"] = grouped[key],
                    ["offset"] = new JObject
                    {
                        ["x"] = key.Item1,
                        ["y"] = key.Item2,
                        ["z"] = key.Item3,
                    },
                };
                result.Add(entry);
            }
            return result;
        }

        private static (double x, double y, double z) ParseMovementOffset(JToken movement)
        {
            if (movement is JObject mo)
            {
                var v = VectorHelper.Vector(mo);
                return (System.Math.Round(v.Value<double>("x"), 6),
                        System.Math.Round(v.Value<double>("y"), 6),
                        System.Math.Round(v.Value<double>("z"), 6));
            }

            if (movement is JArray arr)
            {
                return (
                    System.Math.Round(arr.Count > 0 ? (arr[0].Value<double?>() ?? 0.0) : 0.0, 6),
                    System.Math.Round(arr.Count > 1 ? (arr[1].Value<double?>() ?? 0.0) : 0.0, 6),
                    System.Math.Round(arr.Count > 2 ? (arr[2].Value<double?>() ?? 0.0) : 0.0, 6));
            }

            return (0.0, 0.0, 0.0);
        }
    }
}
