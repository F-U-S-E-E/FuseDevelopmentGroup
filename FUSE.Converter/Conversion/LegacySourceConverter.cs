using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Converter.Models;
using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Port of <c>convert_source</c> from the Python converter. Takes
    /// a single legacy source document and a fragment skeleton, and
    /// applies every converter (tracks / operations / world /
    /// progression) to fill the skeleton in place. Handles the
    /// handler-dispatch logic for legacy splineys: AMM packages
    /// shipped multiple builders under one <c>splineys</c> dict
    /// (turntable, loader, station, label, telegraph-pole-mover,
    /// RR-crossing, DKW, generic), and this converter routes each
    /// entry to the right destination based on its handler string.
    /// </summary>
    /// <remarks>
    /// State carried between source files (order numbering, runtime
    /// duplicate detection) lives in <see cref="OrderState"/>; pass
    /// the same instance into every <see cref="ConvertSource"/> call
    /// in one mod-conversion run.
    /// </remarks>
    internal static class LegacySourceConverter
    {
        /// <summary>
        /// Cross-fragment state used by ordering helpers and
        /// runtime-duplicate detection.
        /// </summary>
        public sealed class OrderState
        {
            public Dictionary<string, int> AreaOrders { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, Dictionary<string, int>> IndustryOrdersByArea { get; } = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> NextIndustryOrderByArea { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<(string kind, string id), string> RuntimeIds { get; } = new Dictionary<(string, string), string>();
        }

        /// <summary>
        /// Top-level dispatcher. Mutates <paramref name="rail"/> in
        /// place so the orchestrator can write the converted fragment
        /// to disk.
        /// </summary>
        public static void ConvertSource(JObject source, JObject rail, string sourceName,
                                          OrderState orderState, List<FuseConversionReportEntry> report)
        {
            if (source == null || rail == null) return;
            orderState = orderState ?? new OrderState();

            ApplyTracks(source, rail, sourceName, report);
            ApplyLoads(source, rail, sourceName, report);
            ApplyAreasAndIndustries(source, rail, sourceName, orderState, report);
            ApplyTurntables(source, rail, sourceName, report);
            ApplyScenery(source, rail, sourceName, report);
            ApplySplineys(source, rail, sourceName, orderState, report);
            ApplySceneClones(source, rail, sourceName, report);
            ApplyTexts(source, rail, sourceName, report);
            ApplySimpleGraphs(source, rail);
            ApplyLegacyStart(source, rail);

            LegacyProgressionConverter.ConvertProgression(source, rail, report);
            LegacyLoadHelpers.EnsureKnownCompatLoads(rail);
        }

        // ------------------------------------------------------------------
        // Section appliers
        // ------------------------------------------------------------------

        private static void ApplyTracks(JObject source, JObject rail, string sourceName, List<FuseConversionReportEntry> report)
        {
            var tracks = source["tracks"] as JObject;
            if (tracks == null) return;

            var railTracks = rail["tracks"] as JObject ?? new JObject();
            var nodes = (railTracks["nodes"] as JObject) ?? new JObject();
            var segments = (railTracks["segments"] as JObject) ?? new JObject();
            var spans = (railTracks["spans"] as JObject) ?? new JObject();
            var removals = (railTracks["removals"] as JObject) ?? new JObject();
            var nodeRemovals = (removals["nodes"] as JArray) ?? new JArray();
            var segmentRemovals = (removals["segments"] as JArray) ?? new JArray();
            var spanRemovals = (removals["spans"] as JArray) ?? new JArray();

            if (tracks["nodes"] is JObject legacyNodes)
            {
                foreach (var prop in legacyNodes.Properties())
                {
                    if (prop.Value == null || prop.Value.Type == JTokenType.Null)
                    {
                        nodeRemovals.Add(prop.Name);
                    }
                    else if (prop.Value is JObject)
                    {
                        SafeConvert(() => LegacyTrackConverter.ConvertNode(prop.Value),
                            converted => nodes[prop.Name] = converted,
                            $"node '{prop.Name}'", sourceName, "tracks.nodes", report);
                    }
                }
            }

            if (tracks["segments"] is JObject legacySegments)
            {
                foreach (var prop in legacySegments.Properties())
                {
                    if (prop.Value == null || prop.Value.Type == JTokenType.Null)
                    {
                        segmentRemovals.Add(prop.Name);
                    }
                    else if (prop.Value is JObject)
                    {
                        var segName = prop.Name;
                        SafeConvert(() => LegacyTrackConverter.ConvertSegment(prop.Value),
                            converted =>
                            {
                                if (converted != null)
                                {
                                    segments[segName] = converted;
                                }
                                else
                                {
                                    // Segment with no startId AND no endId
                                    // is unusable on its own — surface a
                                    // warning so the modder can locate it
                                    // in the source.
                                    ReportEntry(report, FuseConversionReportLevel.Warning, sourceName,
                                        "tracks.segments",
                                        $"Segment '{segName}' had no start/end node id and was skipped.");
                                }
                            },
                            $"segment '{segName}'", sourceName, "tracks.segments", report);
                    }
                }
            }

            if (tracks["spans"] is JObject legacySpans)
            {
                foreach (var prop in legacySpans.Properties())
                {
                    if (prop.Value == null || prop.Value.Type == JTokenType.Null)
                    {
                        spanRemovals.Add(prop.Name);
                    }
                    else if (prop.Value is JObject)
                    {
                        SafeConvert(() => LegacyTrackConverter.ConvertSpan(prop.Name, prop.Value, report, sourceName),
                            converted => spans[prop.Name] = converted,
                            $"span '{prop.Name}'", sourceName, "tracks.spans", report);
                    }
                }
            }
        }

        private static void ApplyLoads(JObject source, JObject rail, string sourceName, List<FuseConversionReportEntry> report)
        {
            if (!(source["loads"] is JObject legacyLoads)) return;
            var loads = (rail["operations"] as JObject)?["loads"] as JObject;
            if (loads == null) return;

            foreach (var prop in legacyLoads.Properties())
            {
                if (!(prop.Value is JObject)) continue;
                SafeConvert(() => LegacyOperationsConverter.ConvertLoad(prop.Name, prop.Value),
                    converted => loads[prop.Name] = converted,
                    $"load '{prop.Name}'", sourceName, "operations.loads", report);
            }
        }

        private static void ApplyAreasAndIndustries(JObject source, JObject rail, string sourceName,
                                                     OrderState orderState, List<FuseConversionReportEntry> report)
        {
            var railTracks = rail["tracks"] as JObject;
            var railOps = rail["operations"] as JObject;
            var areas = railTracks?["areas"] as JObject;
            var industries = railOps?["industries"] as JObject;

            if (source["areas"] is JObject legacyAreas)
            {
                foreach (var areaProp in legacyAreas.Properties())
                {
                    if (!(areaProp.Value is JObject areaObj)) continue;

                    var areaOrder = NextAreaOrder(orderState, areaProp.Name, areaObj);
                    SafeConvert(() => LegacyTrackConverter.ConvertArea(areaProp.Name, areaObj, areaOrder, report),
                        converted => { if (areas != null && converted != null) areas[areaProp.Name] = converted; },
                        $"area '{areaProp.Name}'", sourceName, "tracks.areas", report);

                    if (areaObj["industries"] is JObject inAreaInds)
                    {
                        foreach (var indProp in inAreaInds.Properties())
                        {
                            if (!(indProp.Value is JObject indObj)) continue;
                            var indOrder = NextIndustryOrder(orderState, areaProp.Name, indProp.Name, indObj);
                            SafeConvert(() => LegacyOperationsConverter.ConvertIndustry(indProp.Name, indObj, areaProp.Name, indOrder, sourceName, report),
                                converted => { if (industries != null) industries[indProp.Name] = converted; },
                                $"industry '{indProp.Name}'", sourceName, "operations.industries", report);
                        }
                    }
                }
            }

            if (source["industries"] is JObject topIndustries)
            {
                foreach (var indProp in topIndustries.Properties())
                {
                    if (!(indProp.Value is JObject indObj)) continue;
                    var areaId = indObj.Value<string>("areaId") ?? indObj.Value<string>("area");
                    var indOrder = NextIndustryOrder(orderState, areaId, indProp.Name, indObj);
                    SafeConvert(() => LegacyOperationsConverter.ConvertIndustry(indProp.Name, indObj, areaId: null, order: indOrder, sourceName: sourceName, report: report),
                        converted => { if (industries != null) industries[indProp.Name] = converted; },
                        $"industry '{indProp.Name}'", sourceName, "operations.industries", report);
                }
            }
        }

        private static void ApplyTurntables(JObject source, JObject rail, string sourceName, List<FuseConversionReportEntry> report)
        {
            if (!(source["turntables"] is JObject legacy)) return;
            var sink = (rail["operations"] as JObject)?["turntables"] as JObject;
            if (sink == null) return;

            foreach (var prop in legacy.Properties())
            {
                if (!(prop.Value is JObject)) continue;
                SafeConvert(() => LegacyOperationsConverter.ConvertTurntable(prop.Name, prop.Value),
                    converted => sink[prop.Name] = converted,
                    $"turntable '{prop.Name}'", sourceName, "operations.turntables", report);
            }
        }

        private static void ApplyScenery(JObject source, JObject rail, string sourceName, List<FuseConversionReportEntry> report)
        {
            if (!(source["scenery"] is JObject legacy)) return;
            var world = rail["world"] as JObject;
            var scenery = world?["scenery"] as JObject;
            var removals = (world?["removals"] as JObject)?["scenery"] as JArray;

            foreach (var prop in legacy.Properties())
            {
                if (prop.Value == null || prop.Value.Type == JTokenType.Null)
                {
                    removals?.Add(prop.Name);
                }
                else if (prop.Value is JObject)
                {
                    SafeConvert(() => LegacyWorldConverter.ConvertScenery(prop.Value),
                        converted => { if (scenery != null) scenery[prop.Name] = converted; },
                        $"scenery '{prop.Name}'", sourceName, "world.scenery", report);
                }
            }
        }

        private static void ApplySplineys(JObject source, JObject rail, string sourceName,
                                           OrderState orderState, List<FuseConversionReportEntry> report)
        {
            if (!(source["splineys"] is JObject legacy)) return;

            var world = rail["world"] as JObject;
            var ops = rail["operations"] as JObject;
            var splineys = world?["splineys"] as JObject;
            var turntables = ops?["turntables"] as JObject;
            var loaders = ops?["loaders"] as JObject;
            var stations = ops?["stations"] as JObject;
            var mapLabels = world?["mapLabels"] as JObject;
            var scenery = world?["scenery"] as JObject;
            var extensions = rail["extensions"] as JObject;
            var splineyRemovals = (world?["removals"] as JObject)?["splineys"] as JArray;
            var poleMovements = world?["telegraphPoleMovements"] as JArray;

            foreach (var prop in legacy.Properties())
            {
                if (prop.Value == null || prop.Value.Type == JTokenType.Null)
                {
                    splineyRemovals?.Add(prop.Name);
                    continue;
                }
                if (!(prop.Value is JObject splineyObj)) continue;

                var handler = splineyObj.Value<string>("handler") ?? string.Empty;
                var handlerLower = handler.ToLowerInvariant();
                var consts = LegacyConverterConstants.HandlerMap;

                if (handler == LegacyConverterConstants.TurntableHandler)
                {
                    SafeConvert(() => LegacyOperationsConverter.ConvertTurntable(prop.Name, splineyObj),
                        converted =>
                        {
                            RecordRuntimeDuplicate(orderState, "turntable", prop.Name, sourceName, report);
                            if (turntables != null) turntables[prop.Name] = converted;
                        },
                        $"turntable spliney '{prop.Name}'", sourceName, "operations.turntables", report);
                    continue;
                }

                if (LegacyConverterConstants.LoaderHandlers.Contains(handler))
                {
                    SafeConvert(() => LegacyOperationsConverter.ConvertLoader(splineyObj),
                        converted =>
                        {
                            RecordRuntimeDuplicate(orderState, "loader", prop.Name, sourceName, report);
                            if (loaders != null) loaders[prop.Name] = converted;
                        },
                        $"loader spliney '{prop.Name}'", sourceName, "operations.loaders", report);
                    continue;
                }

                if (LegacyConverterConstants.StationHandlers.Contains(handler))
                {
                    SafeConvert(() => LegacyOperationsConverter.ConvertStation(splineyObj),
                        converted =>
                        {
                            RecordRuntimeDuplicate(orderState, "station", prop.Name, sourceName, report);
                            if (stations != null) stations[prop.Name] = converted;
                        },
                        $"station spliney '{prop.Name}'", sourceName, "operations.stations", report);
                    continue;
                }

                if (handler == LegacyConverterConstants.MapLabelHandler)
                {
                    SafeConvert(() => LegacyWorldExtras.ConvertLabel(prop.Name, splineyObj),
                        converted =>
                        {
                            RecordRuntimeDuplicate(orderState, "map-label", prop.Name, sourceName, report);
                            if (mapLabels != null) mapLabels[prop.Name] = converted;
                        },
                        $"map-label spliney '{prop.Name}'", sourceName, "world.mapLabels", report);
                    continue;
                }

                if (LegacyConverterConstants.TelegraphPoleMoverHandlers.Contains(handler))
                {
                    SafeConvert(() => LegacyWorldConverter.ConvertTelegraphPoleMovements(splineyObj),
                        converted =>
                        {
                            if (poleMovements != null && converted is JArray arr)
                            {
                                foreach (var entry in arr) poleMovements.Add(entry.DeepClone());
                            }
                        },
                        $"telegraph spliney '{prop.Name}'", sourceName, "world.telegraphPoleMovements", report);
                    continue;
                }

                if (LegacyConverterConstants.RrCrossingHandlers.Contains(handlerLower))
                {
                    SafeConvert(() => LegacyWorldConverter.ConvertScenery(splineyObj),
                        converted => { if (scenery != null) scenery[prop.Name] = converted; },
                        $"crossing spliney '{prop.Name}'", sourceName, "world.scenery", report);
                    continue;
                }

                if (string.Equals(handlerLower, LegacyConverterConstants.DkwSplineyHandler, StringComparison.OrdinalIgnoreCase) &&
                    LegacyWorldExtras.ConvertDkwSpliney(prop.Name, splineyObj, rail))
                {
                    continue;
                }

                // Splineys with fewer than 2 points have no
                // meaningful curve — stash them under extensions for
                // a later pass (FUSE doesn't render them, but the
                // modder may still want to inspect the data).
                var points = splineyObj["points"] as JArray;
                if (points == null || points.Count < 2)
                {
                    if (extensions != null)
                    {
                        var legacyBucket = extensions["legacySplineyObjects"] as JObject;
                        if (legacyBucket == null)
                        {
                            legacyBucket = new JObject();
                            extensions["legacySplineyObjects"] = legacyBucket;
                        }
                        legacyBucket[prop.Name] = splineyObj.DeepClone();
                    }
                    continue;
                }

                SafeConvert(() => LegacyWorldConverter.ConvertSpliney(splineyObj),
                    converted => { if (splineys != null) splineys[prop.Name] = converted; },
                    $"spliney '{prop.Name}'", sourceName, "world.splineys", report);
            }
        }

        private static void ApplySceneClones(JObject source, JObject rail, string sourceName, List<FuseConversionReportEntry> report)
        {
            if (!(source["mandelas"] is JObject legacy)) return;
            var world = rail["world"] as JObject;
            var sceneClones = world?["sceneClones"] as JObject;
            var removals = (world?["removals"] as JObject)?["sceneClones"] as JArray;

            foreach (var prop in legacy.Properties())
            {
                if (prop.Value == null || prop.Value.Type == JTokenType.Null)
                {
                    removals?.Add(prop.Name);
                }
                else if (prop.Value is JObject)
                {
                    SafeConvert(() => LegacyWorldExtras.ConvertSceneClone(prop.Name, prop.Value),
                        converted => { if (sceneClones != null) sceneClones[prop.Name] = converted; },
                        $"scene clone '{prop.Name}'", sourceName, "world.sceneClones", report);
                }
            }
        }

        private static void ApplyTexts(JObject source, JObject rail, string sourceName, List<FuseConversionReportEntry> report)
        {
            if (!(source["texts"] is JObject legacy)) return;
            var world = rail["world"] as JObject;
            var labels = world?["mapLabels"] as JObject;
            var removals = (world?["removals"] as JObject)?["mapLabels"] as JArray;

            foreach (var prop in legacy.Properties())
            {
                if (prop.Value == null || prop.Value.Type == JTokenType.Null)
                {
                    removals?.Add(prop.Name);
                }
                else if (prop.Value is JObject)
                {
                    SafeConvert(() => LegacyWorldExtras.ConvertLabel(prop.Name, prop.Value),
                        converted => { if (labels != null) labels[prop.Name] = converted; },
                        $"label '{prop.Name}'", sourceName, "world.mapLabels", report);
                }
            }
        }

        private static void ApplySimpleGraphs(JObject source, JObject rail)
        {
            if (!(source["simpleGraphs"] is JObject sg) || sg.Count == 0) return;
            var extensions = rail["extensions"] as JObject;
            if (extensions == null) return;
            extensions["simpleGraphs"] = (JObject)sg.DeepClone();
        }

        private static void ApplyLegacyStart(JObject source, JObject rail)
        {
            var (spawn, extension) = LegacyWorldExtras.ConvertLegacyStart(source);
            if (spawn == null) return;

            var world = rail["world"] as JObject;
            var spawns = world?["spawnPoints"] as JArray;
            if (spawns != null) spawns.Add(spawn);

            var extensions = rail["extensions"] as JObject;
            if (extensions != null && extension != null)
            {
                extensions["legacyStartOption"] = extension;
            }
        }

        // ------------------------------------------------------------------
        // Order state helpers (port of next_area_order/next_industry_order)
        // ------------------------------------------------------------------

        /// <summary>
        /// Port of <c>_legacy_order_value</c>. Parses an item's
        /// <c>order</c> field as an int, returning null on missing /
        /// non-integer values and emitting a warning in the latter
        /// case so the modder knows the value was ignored.
        /// </summary>
        public static int? LegacyOrderValue(JObject item, List<FuseConversionReportEntry> report)
        {
            if (item == null) return null;
            var value = item["order"];
            if (value == null || value.Type == JTokenType.Null) return null;
            // Booleans cast to ints in Python (False=0, True=1) but
            // the Python source explicitly rejects them.
            if (value.Type == JTokenType.Boolean) return null;

            try
            {
                return Convert.ToInt32(value);
            }
            catch (Exception)
            {
                ReportEntry(report, FuseConversionReportLevel.Warning, sourceFile: null,
                    concept: "invalid-order-value",
                    message: $"Legacy order value '{value}' is not an integer; falling back to source encounter order.");
                return null;
            }
        }

        public static int? NextAreaOrder(OrderState state, string areaId, JObject item)
        {
            if (state == null) return null;
            var key = (areaId ?? string.Empty).ToLowerInvariant();
            var explicitOrder = LegacyOrderValue(item, report: null);
            if (explicitOrder.HasValue)
            {
                state.AreaOrders[key] = explicitOrder.Value;
                return explicitOrder.Value;
            }
            if (state.AreaOrders.TryGetValue(key, out var existing)) return existing;
            // Areas without explicit legacy order intentionally don't
            // get a converter-assigned one — see the Python source's
            // long comment about ordering globally vs per-file.
            return null;
        }

        public static int? NextIndustryOrder(OrderState state, string areaId, string industryId, JObject item)
        {
            if (state == null) return null;
            var areaKey = (areaId ?? "__unassigned__").ToLowerInvariant();
            if (!state.IndustryOrdersByArea.TryGetValue(areaKey, out var areaIndustries))
            {
                areaIndustries = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                state.IndustryOrdersByArea[areaKey] = areaIndustries;
            }
            var industryKey = (industryId ?? string.Empty).ToLowerInvariant();

            var explicitOrder = LegacyOrderValue(item, report: null);
            if (explicitOrder.HasValue)
            {
                areaIndustries[industryKey] = explicitOrder.Value;
                return explicitOrder.Value;
            }
            if (areaIndustries.TryGetValue(industryKey, out var existing)) return existing;

            var next = state.NextIndustryOrderByArea.TryGetValue(areaKey, out var n) ? n : 0;
            state.NextIndustryOrderByArea[areaKey] = next + 1;
            areaIndustries[industryKey] = next;
            return next;
        }

        public static void RecordRuntimeDuplicate(OrderState state, string kind, string objectId,
                                                  string sourceName, List<FuseConversionReportEntry> report)
        {
            if (state == null || string.IsNullOrEmpty(objectId)) return;
            var key = (kind ?? string.Empty, (objectId ?? string.Empty).ToLowerInvariant());
            if (!state.RuntimeIds.ContainsKey(key))
            {
                state.RuntimeIds[key] = sourceName ?? string.Empty;
                return;
            }

            ReportEntry(report, FuseConversionReportLevel.Info, sourceName, "duplicate-" + kind + "-id",
                $"Duplicate legacy {kind} id '{objectId}' in '{sourceName}' also appeared in " +
                $"'{state.RuntimeIds[key]}'. Keeping the same FUSE id so the later mixinto updates/replaces the earlier runtime object.");
        }

        // ------------------------------------------------------------------
        // Count / group coverage
        // ------------------------------------------------------------------

        /// <summary>
        /// Port of <c>count_content</c>. Returns per-section counts
        /// (tracks.nodes, operations.industries, ...) plus per-removal
        /// counts. Used to summarise per-fragment conversion output.
        /// </summary>
        public static Dictionary<string, int> CountContent(JObject rail)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (rail == null) return counts;

            foreach (var sectionName in new[] { "tracks", "operations", "world", "progression" })
            {
                var section = rail[sectionName] as JObject;
                if (section == null) continue;

                foreach (var prop in section.Properties())
                {
                    if ((sectionName == "tracks" || sectionName == "world") && prop.Name == "removals")
                    {
                        continue;
                    }
                    if (prop.Value is JObject child) counts[$"{sectionName}.{prop.Name}"] = child.Count;
                    else if (prop.Value is JArray arr) counts[$"{sectionName}.{prop.Name}"] = arr.Count;
                }
            }

            if (rail["tracks"] is JObject tracks && tracks["removals"] is JObject trackRemovals)
            {
                foreach (var prop in trackRemovals.Properties())
                {
                    counts[$"tracks.removals.{prop.Name}"] = (prop.Value as JArray)?.Count ?? 0;
                }
            }
            if (rail["world"] is JObject world && world["removals"] is JObject worldRemovals)
            {
                foreach (var prop in worldRemovals.Properties())
                {
                    counts[$"world.removals.{prop.Name}"] = (prop.Value as JArray)?.Count ?? 0;
                }
            }

            return counts;
        }

        public static bool HasContent(JObject rail)
        {
            foreach (var count in CountContent(rail).Values)
            {
                if (count > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Port of <c>_collect_initially_enabled_groups</c>. Walks
        /// the fragment's progression payload looking for sections /
        /// map-features marked initiallyEnabled (or the legacy
        /// <c>defaultEnableInSandbox</c> synonym) and harvests their
        /// <c>trackGroupsEnableOnUnlock</c> entries into the supplied
        /// sink set.
        /// </summary>
        public static void CollectInitiallyEnabledGroups(JObject rail, HashSet<string> sink)
        {
            var progression = rail?["progression"] as JObject;
            if (progression == null || sink == null) return;

            foreach (var containerKey in new[] { "sections", "mapFeatures", "progressions" })
            {
                var container = progression[containerKey];
                if (container is JObject dict)
                {
                    foreach (var prop in dict.Properties())
                    {
                        HarvestEnableGroups(prop.Value, sink);
                    }
                }
                else if (container is JArray arr)
                {
                    foreach (var item in arr) HarvestEnableGroups(item, sink);
                }
            }
        }

        private static void HarvestEnableGroups(JToken node, HashSet<string> sink)
        {
            if (!(node is JObject obj)) return;
            var initiallyEnabled = obj["initiallyEnabled"]?.Value<bool?>() ?? obj["defaultEnableInSandbox"]?.Value<bool?>();
            if (initiallyEnabled == true && obj["trackGroupsEnableOnUnlock"] is JArray groups)
            {
                foreach (var group in groups)
                {
                    var text = group.Value<string>();
                    if (!string.IsNullOrEmpty(text)) sink.Add(text);
                }
            }
            foreach (var prop in obj.Properties())
            {
                if (prop.Value is JObject) HarvestEnableGroups(prop.Value, sink);
                else if (prop.Value is JArray arr)
                {
                    foreach (var item in arr) HarvestEnableGroups(item, sink);
                }
            }
        }

        // ------------------------------------------------------------------
        // Rail-data-file ordering (port of rail_data_file_order/weight)
        // ------------------------------------------------------------------

        /// <summary>
        /// Port of <c>rail_data_file_weight</c>. Lower weight = load
        /// earlier. When per-fragment <paramref name="counts"/> are
        /// available, content shape (tracks vs world vs progression)
        /// drives the weight; otherwise, falls back to filename hints.
        /// </summary>
        public static int RailDataFileWeight(string lowerName, Dictionary<string, int> counts)
        {
            if (counts != null && counts.Count > 0)
            {
                bool HasAny(params string[] keys) => keys.Any(k => counts.TryGetValue(k, out var c) && c > 0);
                int Get(string key) => counts.TryGetValue(key, out var c) ? c : 0;

                var hasTrack = HasAny("tracks.nodes", "tracks.segments", "tracks.spans",
                                       "tracks.removals.nodes", "tracks.removals.segments",
                                       "tracks.removals.spans", "operations.turntables");
                var hasLoads = Get("operations.loads") > 0;
                var hasIndustries = Get("operations.industries") > 0 || Get("tracks.areas") > 0;
                var hasLoadersOrStations = Get("operations.loaders") > 0 || Get("operations.stations") > 0;
                var hasProgression = Get("progression.sections") > 0 || Get("progression.progressions") > 0 || Get("progression.mapFeatures") > 0;
                var hasWorld = counts.Any(kv => kv.Key.StartsWith("world.") && kv.Key != "world.mapTiles" && kv.Value > 0);

                if (hasLoads && !(hasTrack || hasIndustries || hasLoadersOrStations || hasWorld || hasProgression)) return 0;
                if (hasTrack) return 10;
                if (Get("world.mapTiles") > 0) return 15;
                if (hasIndustries) return 20;
                if (hasLoadersOrStations) return 30;
                if (hasProgression) return 40;
                if (hasWorld) return 50;
            }

            var name = lowerName ?? string.Empty;
            if (name.IndexOf("loads", StringComparison.Ordinal) >= 0) return 0;
            if (AnyToken(name, "game-graph", "gamegraph", "graph", "track", "yard", "branch", "cutoff", "turntable")) return 10;
            if (AnyToken(name, "industry", "industries", "area", "town")) return 20;
            if (AnyToken(name, "loader", "station", "pax", "passenger")) return 30;
            if (AnyToken(name, "progression", "feature", "unlock")) return 40;
            if (AnyToken(name, "scenery", "spline", "road", "river", "mandela", "text", "label")) return 50;
            return 90;
        }

        private static bool AnyToken(string haystack, params string[] needles)
        {
            foreach (var needle in needles)
            {
                if (haystack.IndexOf(needle, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static void SafeConvert<T>(Func<T> body, Action<T> sink, string what, string sourceName, string concept,
                                            List<FuseConversionReportEntry> report)
        {
            try
            {
                var result = body();
                sink(result);
            }
            catch (Exception ex)
            {
                ReportEntry(report, FuseConversionReportLevel.Warning, sourceName, concept,
                    $"Failed to convert {what}: {ex.Message}");
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
