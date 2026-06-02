using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Port of the track-section converters from the Python source:
    /// <c>convert_node</c> + <c>convert_segment</c>. Operates on raw
    /// JObject inputs so the converter doesn't need typed FUSE schemas
    /// at hand.
    /// </summary>
    /// <remarks>
    /// Span conversion is intentionally NOT in this first slice — it
    /// involves geometry repair (clamping endpoints, walking the
    /// segment graph to repair multi-segment spans, splitting same-
    /// segment spans into two locations). That logic lives in
    /// ~700 lines of Python and gets its own port pass.
    /// </remarks>
    internal static class LegacyTrackConverter
    {
        /// <summary>
        /// Converts a legacy track node payload into the FUSE shape.
        /// Accepts both the legacy "position/rotation" naming and the
        /// older "localPosition/localRotation" names.
        /// </summary>
        public static JObject ConvertNode(JToken legacy)
        {
            var obj = legacy as JObject;
            return new JObject
            {
                ["position"] = VectorHelper.Vector(obj?["position"] ?? obj?["localPosition"]),
                ["rotation"] = VectorHelper.Vector(obj?["rotation"] ?? obj?["localRotation"]),
                ["flipSwitchStand"] = obj?.Value<bool?>("flipSwitchStand") ?? false,
            };
        }

        /// <summary>
        /// Converts a legacy track segment payload. The Python source
        /// has detailed casing tolerance (startId / startNodeId /
        /// nodeA / a, etc.) — ported verbatim so heterogeneous legacy
        /// packages round-trip identically.
        /// </summary>
        public static JObject ConvertSegment(JToken legacy)
        {
            var obj = legacy as JObject;
            if (obj == null)
            {
                return null;
            }

            var groupId = obj.Value<string>("groupId") ?? obj.Value<string>("GroupId");
            var startNodeId = FirstString(obj, "startId", "startNodeId", "nodeA", "a");
            var endNodeId = FirstString(obj, "endId", "endNodeId", "nodeB", "b");

            if (string.IsNullOrEmpty(startNodeId) && string.IsNullOrEmpty(endNodeId))
            {
                return null;
            }

            var partial = string.IsNullOrEmpty(startNodeId) || string.IsNullOrEmpty(endNodeId);

            var result = new JObject
            {
                ["style"] = obj.Value<string>("Style") ?? obj.Value<string>("style") ?? "standard",
                ["trackClass"] = obj.Value<string>("trackClass") ?? obj.Value<string>("TrackClass") ?? "main",
                ["speedLimit"] = FirstInt(obj, 45, "speedLimit", "SpeedLimit"),
                ["priority"] = obj.Value<int?>("priority") ?? 0,
                ["groupId"] = groupId,
                ["gauge"] = obj.Value<string>("gauge") ?? obj.Value<string>("Gauge"),
            };

            if (!string.IsNullOrEmpty(startNodeId))
            {
                result["startNodeId"] = startNodeId;
            }
            if (!string.IsNullOrEmpty(endNodeId))
            {
                result["endNodeId"] = endNodeId;
            }

            if (partial)
            {
                // Partial segments preserve un-specified fields so a
                // mixinto-style overlay against an already-defined
                // segment in a different package doesn't accidentally
                // overwrite values the original author committed to.
                result["partial"] = true;
                result["preserveStyle"] = !obj.ContainsKey("Style") && !obj.ContainsKey("style");
                result["preserveTrackClass"] = !obj.ContainsKey("trackClass") && !obj.ContainsKey("TrackClass");
                result["preserveSpeedLimit"] = !obj.ContainsKey("speedLimit") && !obj.ContainsKey("SpeedLimit");
                result["preservePriority"] = !obj.ContainsKey("priority");
                result["preserveGroupId"] = !obj.ContainsKey("groupId") && !obj.ContainsKey("GroupId");
            }

            return result;
        }

        /// <summary>
        /// Port of <c>convert_location</c>. A location pinpoints a
        /// position along a track segment via segmentId + end (A/B)
        /// + distance OR normalized (0..1). Optional <c>offset</c>
        /// adds an extra distance after the anchor.
        /// </summary>
        public static JObject ConvertLocation(JToken legacy)
        {
            var obj = legacy as JObject;
            if (obj == null)
            {
                return new JObject
                {
                    ["segmentId"] = string.Empty,
                    ["distance"] = 0.0,
                    ["end"] = "A",
                };
            }

            var result = new JObject
            {
                ["segmentId"] = FirstString(obj, "segmentId", "segmentID", "SegmentId", "SegmentID", "segment") ?? string.Empty,
                ["end"] = NormalizeEnd(obj.Value<string>("end")) ?? "A",
            };

            if (obj.ContainsKey("normalized"))
            {
                result["normalized"] = obj.Value<double?>("normalized") ?? 0.0;
            }
            else
            {
                result["distance"] = obj.Value<double?>("distance") ?? 0.0;
            }

            if (obj.ContainsKey("offset"))
            {
                result["offset"] = obj.Value<double?>("offset") ?? 0.0;
            }

            return result;
        }

        /// <summary>
        /// Normalises legacy <c>end</c> values. "Start" / "a" / "A"
        /// → "A"; "End" / "b" / "B" → "B"; anything else passes
        /// through unchanged so the loader's validator can flag it.
        /// </summary>
        public static string NormalizeEnd(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var lower = value.Trim().ToLowerInvariant();
            if (lower == "start" || lower == "a") return "A";
            if (lower == "end" || lower == "b") return "B";
            return value;
        }

        /// <summary>
        /// Port of <c>convert_span</c>. A span anchors two locations
        /// (upper / lower) along the track graph. Geometry repair
        /// (clamping endpoints, walking the segment graph,
        /// same-segment splits) lives in a separate pass that runs
        /// after every fragment has been converted; this just
        /// translates the field shape and surfaces a crossed-
        /// endpoint warning where the check doesn't need segment
        /// length info.
        /// </summary>
        /// <param name="report">
        /// Optional report to attach the crossed-endpoint warning to.
        /// May be null for tests / contexts that don't track reports.
        /// </param>
        public static JObject ConvertSpan(string spanId, JToken legacy,
                                          System.Collections.Generic.List<Models.FuseConversionReportEntry> report = null,
                                          string sourceName = null)
        {
            var obj = legacy as JObject;
            var upper = ConvertLocation(obj?["upper"]);
            var lower = ConvertLocation(obj?["lower"]);

            ValidateSpanGeometry(spanId, upper, lower, report, sourceName);

            return new JObject
            {
                ["upper"] = upper,
                ["lower"] = lower,
            };
        }

        /// <summary>
        /// Detects spans with crossed endpoints when both endpoints
        /// are on the SAME segment AND share the SAME anchor end
        /// (legacy modders sometimes swap distances within one
        /// anchor side). The general crossed case (different ends)
        /// needs the segment's physical length, which we don't have
        /// at conversion time — the FUSE runtime catches those at
        /// preflight.
        /// </summary>
        private static void ValidateSpanGeometry(string spanId, JObject upper, JObject lower,
                                                  System.Collections.Generic.List<Models.FuseConversionReportEntry> report,
                                                  string sourceName)
        {
            if (string.IsNullOrEmpty(spanId) || upper == null || lower == null) return;

            var upperSeg = upper.Value<string>("segmentId");
            var lowerSeg = lower.Value<string>("segmentId");
            if (upperSeg != lowerSeg || string.IsNullOrEmpty(upperSeg)) return;

            var upperEnd = upper.Value<string>("end");
            var lowerEnd = lower.Value<string>("end");
            if (upperEnd != lowerEnd) return;

            var upperD = upper.Value<double?>("distance");
            var lowerD = lower.Value<double?>("distance");
            if (!upperD.HasValue || !lowerD.HasValue) return;

            // Both endpoints anchored to the same end. Upper should
            // sit farther from the anchor than Lower; for end "A" that
            // means upperD > lowerD, for end "B" the reverse.
            string warning = null;
            if (upperEnd == "A" && upperD.Value < lowerD.Value)
            {
                warning = $"Span '{spanId}' on segment '{upperSeg}': both endpoints anchored to A " +
                          $"but upper.distance ({upperD}) < lower.distance ({lowerD}). " +
                          "FUSE will reject as crossed; check legacy 'Start'/'End' mapping.";
            }
            else if (upperEnd == "B" && upperD.Value > lowerD.Value)
            {
                warning = $"Span '{spanId}' on segment '{upperSeg}': both endpoints anchored to B " +
                          $"but upper.distance ({upperD}) > lower.distance ({lowerD}). " +
                          "FUSE will reject as crossed; check legacy 'Start'/'End' mapping.";
            }

            if (warning != null && report != null)
            {
                report.Add(new Models.FuseConversionReportEntry
                {
                    Level = Models.FuseConversionReportLevel.Warning,
                    Message = warning,
                    SourceFile = sourceName ?? string.Empty,
                    Concept = "span-geometry-crossed",
                });
            }
        }

        /// <summary>
        /// Port of <c>convert_area</c>. Areas are simple track-section
        /// groupings; the <c>tagColor</c> field gets normalised to a
        /// 3- or 4-component float array via
        /// <see cref="LegacyWorldExtras.NormalizeTagColor"/>, with
        /// the warning routed into <paramref name="report"/> when
        /// supplied.
        /// </summary>
        public static JObject ConvertArea(string areaId, JToken legacy, int? order,
                                          System.Collections.Generic.List<Models.FuseConversionReportEntry> report = null)
        {
            var obj = legacy as JObject;
            var result = new JObject
            {
                ["name"] = obj?.Value<string>("name") ?? areaId,
                ["position"] = OptionalVector(obj?["position"] ?? obj?["localPosition"]),
                ["radius"] = obj?.Value<double?>("radius"),
                ["tagColor"] = LegacyWorldExtras.NormalizeTagColor(areaId, obj?["tagColor"]?.DeepClone(), report),
                ["order"] = order.HasValue ? (JToken)order.Value : null,
                ["spanIds"] = StringIds(obj?["spanIds"] ?? obj?["spans"]),
                ["groupId"] = obj?.Value<string>("groupId") ?? obj?.Value<string>("GroupId"),
            };
            return JsonCleanHelper.CleanObject(result);
        }

        /// <summary>
        /// Port of <c>optional_vector</c>: returns null when no value
        /// was supplied (rather than the zero vector the standard
        /// <see cref="VectorHelper.Vector"/> produces). Used for fields
        /// where "absent" must round-trip as null so the FUSE loader
        /// uses the runtime default instead of overriding to zero.
        /// </summary>
        public static JToken OptionalVector(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }
            return VectorHelper.Vector(token);
        }

        /// <summary>
        /// Port of <c>string_ids</c>: collapses a value into a string
        /// array, tolerating single-string / comma-separated / list
        /// inputs, dropping empties + duplicates while preserving
        /// first-seen order.
        /// </summary>
        public static JArray StringIds(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            var result = new JArray();

            void Add(string value)
            {
                var trimmed = value?.Trim();
                if (string.IsNullOrEmpty(trimmed)) return;
                if (seen.Add(trimmed))
                {
                    result.Add(trimmed);
                }
            }

            if (token is JArray arr)
            {
                foreach (var item in arr)
                {
                    if (item == null || item.Type == JTokenType.Null) continue;
                    Add(item.Value<string>());
                }
            }
            else
            {
                var str = token.Value<string>();
                if (str != null && str.IndexOf(',') >= 0)
                {
                    foreach (var part in str.Split(','))
                    {
                        Add(part);
                    }
                }
                else
                {
                    Add(str);
                }
            }

            return result.Count > 0 ? result : null;
        }

        private static string FirstString(JObject obj, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = obj.Value<string>(key);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            return null;
        }

        private static int FirstInt(JObject obj, int fallback, params string[] keys)
        {
            foreach (var key in keys)
            {
                var token = obj[key];
                if (token != null && token.Type != JTokenType.Null)
                {
                    var value = token.Value<int?>();
                    if (value.HasValue)
                    {
                        return value.Value;
                    }
                }
            }
            return fallback;
        }
    }
}
