using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using FUSE.Converter.Models;
using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Port of <c>convert_scene_clone</c>, <c>convert_label</c>
    /// (text label form, distinct from <c>convert_map_label</c>),
    /// <c>convert_legacy_start</c>, and the DKW spliney expansion.
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="LegacyWorldConverter"/> so the
    /// per-handler dispatchers stay short and reviewable. DKW
    /// expansion is the only one of these that mutates the FUSE
    /// document directly (it generates new track nodes + segments
    /// instead of producing one converted blob).
    /// </remarks>
    internal static class LegacyWorldExtras
    {
        private static readonly Regex SpeedLimitLabelPattern =
            new Regex(@"^\s*(\d{1,3})\s*MPH\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Port of <c>convert_scene_clone</c>. Scene clones are
        /// mandela-style transforms that instantiate a base-game (or
        /// asset-pack) prefab at a new location; converted source ids
        /// without a <c>://</c> scheme get the <c>path://scene/</c>
        /// prefix so the FUSE loader treats them as scene-graph paths.
        /// </summary>
        public static JObject ConvertSceneClone(string key, JToken legacy)
        {
            var obj = legacy as JObject;
            var source = obj?.Value<string>("source") ?? obj?.Value<string>("instantiateFrom");
            if (!string.IsNullOrEmpty(source) && source.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                source = "path://scene/" + source;
            }

            var result = new JObject
            {
                ["targetPath"] = obj?.Value<string>("targetPath") ?? key,
                ["source"] = source,
                ["enabled"] = obj?["enabled"]?.DeepClone(),
                ["localPosition"] = VectorHelper.Vector(obj?["localPosition"] ?? obj?["position"]),
                ["localRotation"] = VectorHelper.Vector(obj?["localRotation"] ?? obj?["rotation"]),
                ["localScale"] = VectorHelper.Vector(obj?["localScale"] ?? obj?["scale"], defaultScale: true),
            };
            return JsonCleanHelper.CleanObject(result);
        }

        /// <summary>
        /// Port of <c>convert_label</c>. Distinct from
        /// <see cref="LegacyWorldConverter.ConvertMapLabel"/>: this
        /// is the version called for the legacy <c>texts</c> dict
        /// (the AMM-style label block). Same "NN MPH"
        /// → speedLimit promotion logic.
        /// </summary>
        public static JObject ConvertLabel(string key, JToken legacy)
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
                    result["text"] = mph.ToString(CultureInfo.InvariantCulture);
                    result["style"] = "speedLimit";
                    result["speedLimitMph"] = mph;
                }
            }

            return JsonCleanHelper.CleanObject(result);
        }

        /// <summary>
        /// Port of <c>convert_legacy_start</c>. Legacy AlinasMapMod
        /// packages carry a single "start option" per file
        /// (spawnPoint + name + identifier + initial money etc.);
        /// FUSE keeps the spawn coordinates as a spawnPoint and
        /// stashes the rest under extensions.legacyStartOption so the
        /// game can present the option in the start-game flow.
        /// </summary>
        public static (JObject SpawnPoint, JObject ExtensionPayload) ConvertLegacyStart(JObject source)
        {
            var spawn = source?["spawnPoint"] as JObject;
            if (spawn == null) return (null, null);

            var spawnPoint = JsonCleanHelper.CleanObject(new JObject
            {
                ["name"] = source.Value<string>("name") ?? source.Value<string>("identifier") ?? "Legacy Start",
                ["position"] = VectorHelper.Vector(spawn["position"] ?? spawn["location"]),
                ["rotation"] = VectorHelper.Vector(spawn["rotation"]),
                ["radius"] = spawn["range"]?.DeepClone() ?? spawn["radius"]?.DeepClone(),
            });

            var extension = JsonCleanHelper.CleanObject(new JObject
            {
                ["identifier"] = source.Value<string>("identifier"),
                ["name"] = source.Value<string>("name"),
                ["progressionId"] = source.Value<string>("progressionId"),
                ["showTutorial"] = source["showTutorial"]?.DeepClone(),
                ["initialMoney"] = source["initialMoney"]?.DeepClone(),
                ["enabledFeatures"] = source["enabledFeatures"]?.DeepClone(),
                ["carPlacements"] = source["carPlacements"]?.DeepClone(),
            });

            return (spawnPoint, extension);
        }

        /// <summary>
        /// Port of <c>convert_dkw_spliney</c>. Expands a DKW (double
        /// kreuzungsweiche / scissors-crossing) spliney into 8 track
        /// nodes + 8 segments laid out via trigonometry. The
        /// crossing angle (legacy <c>crossingAngle</c>) drives the
        /// gauge offset; negative angles mirror the crossing along
        /// its yaw axis.
        /// </summary>
        /// <returns>
        /// True when the spliney converted cleanly (so the caller
        /// should NOT also emit it as a generic spliney); false when
        /// the angle is out of range or unreadable, leaving the
        /// generic conversion path to handle it.
        /// </returns>
        public static bool ConvertDkwSpliney(string splineyId, JObject item, JObject rail)
        {
            if (item == null || rail == null) return false;

            double crossingAngle;
            try
            {
                var raw = item["crossingAngle"]?.Value<double?>() ?? item["CrossingAngle"]?.Value<double?>() ?? 0.0;
                crossingAngle = raw;
            }
            catch (Exception)
            {
                return false;
            }

            var flipped = false;
            var position = VectorHelper.Vector(item["position"] ?? item["localPosition"]);
            var rotation = VectorHelper.Vector(item["rotation"] ?? item["localRotation"]);
            if (crossingAngle < 0)
            {
                flipped = true;
                rotation["y"] = (double)rotation.Value<double>("y") + crossingAngle;
                crossingAngle = -crossingAngle;
            }

            // The physical layout is only meaningful in this range —
            // outside it, the trigonometry degenerates into shapes
            // FUSE can't render coherently.
            if (crossingAngle < 4 || crossingAngle > 15)
            {
                return false;
            }

            const double gaugeInside = 1.435;
            var halfAngle = crossingAngle * Math.PI / 180.0 / 2.0;
            var crossingCenter = gaugeInside * Math.Cos(halfAngle) / (2 * Math.Sin(halfAngle));
            var inner = crossingCenter - 0.5;
            var outer = crossingCenter + 1.5;
            var baseYaw = (double)rotation.Value<double>("y");
            var crossingYaw = baseYaw + crossingAngle;
            var nodePrefix = $"N{splineyId}DKW_Node";
            var segmentPrefix = $"S{splineyId}DKW_Segment";

            var nodes = (rail["tracks"] as JObject)?["nodes"] as JObject;
            var segments = (rail["tracks"] as JObject)?["segments"] as JObject;
            if (nodes == null || segments == null) return false;

            JObject Make(double x, double y, double z) => new JObject
            {
                ["x"] = Math.Round(x, 6),
                ["y"] = Math.Round(y, 6),
                ["z"] = Math.Round(z, 6),
            };

            JObject YawOffset(JObject origin, double yawDegrees, double distance)
            {
                var radians = yawDegrees * Math.PI / 180.0;
                return Make(
                    (double)origin.Value<double>("x") + Math.Sin(radians) * distance,
                    (double)origin.Value<double>("y"),
                    (double)origin.Value<double>("z") + Math.Cos(radians) * distance);
            }

            void AddNode(string suffix, JObject pos, double yaw)
            {
                nodes[nodePrefix + suffix] = JsonCleanHelper.CleanObject(new JObject
                {
                    ["position"] = pos,
                    ["rotation"] = Make((double)rotation.Value<double>("x"), yaw, (double)rotation.Value<double>("z")),
                    ["flipSwitchStand"] = false,
                });
            }

            void AddSegment(string suffix, string startSuffix, string endSuffix, int priority = 0)
            {
                segments[segmentPrefix + suffix] = JsonCleanHelper.CleanObject(new JObject
                {
                    ["startNodeId"] = nodePrefix + startSuffix,
                    ["endNodeId"] = nodePrefix + endSuffix,
                    ["style"] = "standard",
                    ["trackClass"] = "main",
                    ["speedLimit"] = 45,
                    ["priority"] = priority,
                });
            }

            AddNode("P1I", YawOffset(position, baseYaw, -inner), baseYaw);
            AddNode("P1O", YawOffset(position, baseYaw, -outer), baseYaw);
            AddNode("P2I", YawOffset(position, baseYaw, inner), baseYaw);
            AddNode("P2O", YawOffset(position, baseYaw, outer), baseYaw);
            AddNode("P3I", YawOffset(position, crossingYaw, -inner), crossingYaw);
            AddNode("P3O", YawOffset(position, crossingYaw, -outer), crossingYaw);
            AddNode("P4I", YawOffset(position, crossingYaw, inner), crossingYaw);
            AddNode("P4O", YawOffset(position, crossingYaw, outer), crossingYaw);

            AddSegment("1", "P1O", "P1I");
            AddSegment("2", "P2I", "P2O");
            AddSegment("3", "P3O", "P3I");
            AddSegment("4", "P4I", "P4O");
            AddSegment("CR", "P1I", "P4I");
            AddSegment("CL", "P3I", "P2I");
            AddSegment("D1", "P1I", "P2I", flipped ? -1 : 1);
            AddSegment("D2", "P3I", "P4I", flipped ? 1 : -1);
            return true;
        }

        /// <summary>
        /// Port of <c>_normalize_tag_color</c>. FUSE's area-tagColor
        /// schema needs 3 (RGB) or 4 (RGBA) numbers; a small number
        /// of legacy mods shipped 6-element arrays (concatenated RGB
        /// triples) or sub-3 arrays. Normalise quietly, but surface
        /// a report entry so the modder knows.
        /// </summary>
        public static JToken NormalizeTagColor(string areaId, JToken value, List<FuseConversionReportEntry> report)
        {
            if (!(value is JArray arr)) return value;

            if (arr.Count >= 3 && arr.Count <= 4)
            {
                return arr;
            }

            if (arr.Count > 4)
            {
                ReportEntry(report, FuseConversionReportLevel.Info, sourceFile: null,
                    concept: "area-tagColor-overflow",
                    message: $"Area '{areaId}' tagColor has {arr.Count} values; FUSE accepts 3 or 4. " +
                             "Truncated to the first 3 values to keep the package loadable.");
                var truncated = new JArray();
                for (int i = 0; i < 3; i++) truncated.Add(arr[i].DeepClone());
                return truncated;
            }

            if (arr.Count > 0)
            {
                ReportEntry(report, FuseConversionReportLevel.Warning, sourceFile: null,
                    concept: "area-tagColor-underflow",
                    message: $"Area '{areaId}' tagColor has only {arr.Count} value(s); FUSE requires 3 or 4. " +
                             "Padded with zeros to length 3 to keep the package loadable.");
                var padded = new JArray();
                foreach (var item in arr) padded.Add(item.DeepClone());
                while (padded.Count < 3) padded.Add(0.0);
                while (padded.Count > 3) padded.RemoveAt(padded.Count - 1);
                return padded;
            }

            return value;
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
