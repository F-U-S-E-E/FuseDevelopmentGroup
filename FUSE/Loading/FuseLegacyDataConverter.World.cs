using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Authoring.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FUSE.Loading
{
    internal static partial class FuseLegacyDataConverter
    {

        private static void ConvertMandelas(JObject source, JObject root)
        {
            if (source == null || root == null)
            {
                return;
            }

            var world = root["world"] as JObject;
            var sceneClones = world?["sceneClones"] as JObject;
            var removals = world?["removals"]?["sceneClones"] as JArray;
            var suppressions = world?["suppressBaseScenePaths"] as JArray;
            if (world == null || sceneClones == null)
            {
                return;
            }

            foreach (var property in source.Properties())
            {
                if (property.Value.Type == JTokenType.Null)
                {
                    removals?.Add(property.Name);
                    continue;
                }

                var item = property.Value as JObject;
                if (TryConvertMandelaSuppression(property.Name, item, suppressions))
                {
                    continue;
                }

                var converted = ConvertSceneClone(property.Name, property.Value);
                if (converted != null)
                {
                    sceneClones[property.Name] = Clean(converted);
                }
            }
        }

        private static bool TryConvertMandelaSuppression(string id, JObject item, JArray suppressions)
        {
            if (item == null || suppressions == null || !HasAnyProperty(item, "enabled"))
            {
                return false;
            }

            if (ReadBool(item, "enabled", true))
            {
                return false;
            }

            var source = ReadString(item, "source", "instantiateFrom");
            if (!string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            var path = ReadString(item, "targetPath") ?? id;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            AddUniqueString(suppressions, path);
            return true;
        }

        private static bool HasTrackSpan(JObject root, string spanId)
        {
            return !string.IsNullOrWhiteSpace(spanId) &&
                   root?["tracks"]?["spans"]?[spanId] is JObject;
        }

        private static bool HasSceneryAsset(JObject source, string assetIdentifier)
        {
            var scenery = source?["scenery"] as JObject;
            if (scenery == null || string.IsNullOrWhiteSpace(assetIdentifier))
            {
                return false;
            }

            return scenery.Properties().Any(property =>
                property.Value is JObject item &&
                string.Equals(ReadString(item, "assetIdentifier", "modelIdentifier", "definitionIdentifier", "model"), assetIdentifier, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasPassengerStopId(JObject industries, string passengerStopId)
        {
            if (industries == null || string.IsNullOrWhiteSpace(passengerStopId))
            {
                return false;
            }

            foreach (var industry in industries.Properties().Select(property => property.Value as JObject).Where(item => item != null))
            {
                var components = industry["components"] as JObject;
                if (components == null)
                {
                    continue;
                }

                foreach (var component in components.Properties().Select(property => property.Value as JObject).Where(item => item != null))
                {
                    if (string.Equals(ReadString(component, "passengerStopId", "passengerStop"), passengerStopId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static JObject TryGetLocalPositionForScenery(JObject source, JObject root, string assetIdentifier, string areaId)
        {
            var scenery = source?["scenery"] as JObject;
            var areas = root?["tracks"]?["areas"] as JObject;
            var area = areas?[areaId] as JObject;
            if (scenery == null || area == null)
            {
                return null;
            }

            var areaPosition = area["position"] as JObject;
            if (!TryReadVector(areaPosition, out var areaX, out var areaY, out var areaZ))
            {
                return null;
            }

            if (Math.Abs(areaX) < 0.001 && Math.Abs(areaY) < 0.001 && Math.Abs(areaZ) < 0.001)
            {
                return null;
            }

            foreach (var item in scenery.Properties().Select(property => property.Value as JObject).Where(item => item != null))
            {
                if (!string.Equals(ReadString(item, "assetIdentifier", "modelIdentifier", "definitionIdentifier", "model"), assetIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryReadVector(item["position"] as JObject, out var x, out var y, out var z))
                {
                    continue;
                }

                return new JObject
                {
                    ["x"] = x - areaX,
                    ["y"] = y - areaY,
                    ["z"] = z - areaZ
                };
            }

            return null;
        }

        private static bool TryReadVector(JObject item, out double x, out double y, out double z)
        {
            x = 0;
            y = 0;
            z = 0;
            if (item == null)
            {
                return false;
            }

            x = ReadFloat(item["x"], float.NaN);
            y = ReadFloat(item["y"], float.NaN);
            z = ReadFloat(item["z"], float.NaN);
            return !double.IsNaN(x) && !double.IsNaN(y) && !double.IsNaN(z);
        }

        private static JToken ConvertNode(JToken token)
        {
            var item = token as JObject;
            if (item == null)
            {
                return null;
            }

            return new JObject
            {
                ["position"] = Vector(item["position"] ?? item["localPosition"], false),
                ["rotation"] = Vector(item["rotation"] ?? item["localRotation"], false),
                ["flipSwitchStand"] = ReadBool(item, "flipSwitchStand", false)
            };
        }

        private static JToken ConvertSegment(string id, JToken token)
        {
            var item = token as JObject;
            if (item == null)
            {
                return null;
            }

            var startNodeId = ReadString(item, "startId", "startNodeId", "nodeA", "a");
            var endNodeId = ReadString(item, "endId", "endNodeId", "nodeB", "b");
            if (string.IsNullOrWhiteSpace(startNodeId) && string.IsNullOrWhiteSpace(endNodeId))
            {
                return null;
            }

            var hasStyle = HasAnyProperty(item, "Style", "style");
            var hasTrackClass = HasAnyProperty(item, "trackClass", "TrackClass");
            var hasSpeedLimit = HasAnyProperty(item, "speedLimit", "SpeedLimit");
            var hasPriority = HasAnyProperty(item, "priority");
            var hasGroupId = HasAnyProperty(item, "groupId", "GroupId");
            var partial = string.IsNullOrWhiteSpace(startNodeId) || string.IsNullOrWhiteSpace(endNodeId);

            var result = new JObject
            {
                ["style"] = ReadString(item, "Style", "style") ?? "standard",
                ["trackClass"] = ReadString(item, "trackClass", "TrackClass") ?? "main",
                ["speedLimit"] = ReadInt(item["speedLimit"] ?? item["SpeedLimit"], 45),
                ["priority"] = ReadInt(item["priority"], 0),
                ["groupId"] = ReadString(item, "groupId", "GroupId"),
                ["gauge"] = ReadString(item, "gauge", "Gauge")
            };

            if (!string.IsNullOrWhiteSpace(startNodeId))
            {
                result["startNodeId"] = startNodeId;
            }

            if (!string.IsNullOrWhiteSpace(endNodeId))
            {
                result["endNodeId"] = endNodeId;
            }

            if (partial)
            {
                result["partial"] = true;
                result["preserveStyle"] = !hasStyle;
                result["preserveTrackClass"] = !hasTrackClass;
                result["preserveSpeedLimit"] = !hasSpeedLimit;
                result["preservePriority"] = !hasPriority;
                result["preserveGroupId"] = !hasGroupId;
            }

            return result;
        }

        private static JToken ConvertSpan(JToken token)
        {
            var item = token as JObject;
            if (item == null)
            {
                return null;
            }

            return new JObject
            {
                ["upper"] = ConvertLocation(item["upper"] ?? item["Upper"] ?? item["start"] ?? item["Start"]),
                ["lower"] = ConvertLocation(item["lower"] ?? item["Lower"] ?? item["end"] ?? item["End"]),
                ["normalize"] = item["normalize"] ?? item["Normalize"] ?? JValue.CreateNull(),
                ["groupId"] = ReadString(item, "groupId", "GroupId")
            };
        }

        private static JObject ConvertLocation(JToken token)
        {
            var item = token as JObject;
            if (item == null)
            {
                return new JObject { ["segmentId"] = string.Empty, ["distance"] = 0, ["end"] = "A" };
            }

            var result = new JObject
            {
                ["segmentId"] = ReadString(item, "segmentId", "segment", "id") ?? string.Empty,
                ["end"] = NormalizeEnd(ReadString(item, "end", "End")) ?? "A"
            };

            if (item["normalized"] != null)
            {
                result["normalized"] = item["normalized"].DeepClone();
            }
            else
            {
                result["distance"] = item["distance"] ?? item["Distance"] ?? new JValue(0);
            }

            return result;
        }

        private static JToken ConvertLoad(string id, JToken token)
        {
            var item = token as JObject;
            if (item == null)
            {
                return null;
            }

            var result = new JObject
            {
                ["name"] = ReadString(item, "name", "description") ?? id,
                ["units"] = ReadString(item, "units") ?? "Quantity",
                ["density"] = Clone(item["density"]),
                ["unitWeightInPounds"] = Clone(item["unitWeightInPounds"]),
                ["importable"] = Clone(item["importable"]),
                ["payPerQuantity"] = Clone(item["payPerQuantity"]),
                ["costPerUnit"] = Clone(item["costPerUnit"]),
                ["carTypeFilter"] = Clone(item["carTypeFilter"])
            };

            var fields = item["fields"] as JObject != null
                ? (JObject)item["fields"].DeepClone()
                : new JObject();
            foreach (var property in item.Properties())
            {
                if (!IsLoadSchemaKey(property.Name) && property.Value.Type != JTokenType.Null && fields[property.Name] == null)
                {
                    fields[property.Name] = property.Value.DeepClone();
                }
            }

            if (fields.HasValues)
            {
                result["fields"] = fields;
            }

            return result;
        }

        private static JObject ConvertArea(string id, JObject item)
        {
            return CleanObject(new JObject
            {
                ["name"] = ReadString(item, "name") ?? id,
                ["position"] = Vector(item["localPosition"] ?? item["position"], false),
                ["radius"] = Clone(item["radius"]),
                ["tagColor"] = NormalizeAreaTagColor(id, item["tagColor"] ?? item["TagColor"]),
                ["order"] = Clone(item["order"]),
                ["spanIds"] = ToStringArray(item["spanIds"] ?? item["spans"]),
                ["groupId"] = ReadString(item, "groupId", "GroupId")
            });
        }

        /// <summary>
        /// Coerces a legacy <c>tagColor</c> value into the 3- or 4-element
        /// RGB / RGBA shape FUSE's schema validator requires. A handful of
        /// legacy mods (Graham County, Macon County) shipped 6-element
        /// arrays by accidentally concatenating two RGB triples; trim
        /// those down. Sub-3 arrays get zero-padded so we never reject
        /// the package on a malformed color when the data IS otherwise
        /// loadable. Anything that isn't an array passes through —
        /// the runtime treats non-array tagColor as the default tint.
        /// </summary>
        private static JToken NormalizeAreaTagColor(string areaId, JToken value)
        {
            var cloned = Clone(value);
            if (!(cloned is JArray arr))
            {
                return cloned;
            }

            if (arr.Count >= 3 && arr.Count <= 4)
            {
                return arr;
            }

            if (arr.Count > 4)
            {
                FuseLog.Info(
                    $"FUSE legacy converter: area '{areaId}' tagColor has {arr.Count} values; " +
                    $"FUSE accepts 3 or 4. Truncated to the first 3 values to keep the package loadable.");
                var truncated = new JArray();
                for (int i = 0; i < 3; i++) truncated.Add(arr[i].DeepClone());
                return truncated;
            }

            if (arr.Count > 0)
            {
                FuseLog.Warning(
                    $"FUSE legacy converter: area '{areaId}' tagColor has only {arr.Count} value(s); " +
                    $"FUSE requires 3 or 4. Padded with zeros to length 3 to keep the package loadable.");
                var padded = new JArray();
                foreach (var item in arr) padded.Add(item.DeepClone());
                while (padded.Count < 3) padded.Add(0.0);
                return padded;
            }

            return arr;
        }

        private static JObject ConvertIndustry(string id, JObject item, string areaId)
        {
            if (item == null)
            {
                return null;
            }

            var sourceComponents = item["components"] as JObject;
            var components = ConvertComponents(sourceComponents);
            // A top-level <c>$replace</c> directive on the source
            // <c>components</c> dictionary means the mod author wants the
            // converted set to FULLY supersede any existing component
            // dictionary at apply time. Without this signal, the loader's
            // "industry already exists → force MergeComponents=true"
            // safety net leaves vanilla components alive (see Foxy's
            // CF.EWhittier.Yard RepairIndustry.json — wh-e-engine kept
            // its vanilla rip+rip-parts repair components after Foxy's
            // $replace, so "East Whittier Fuel Service" still showed a
            // repair track despite the mod intending to remove it).
            // Emit BOTH flags: replaceComponents tells the loader to
            // skip the force-merge override, and mergeComponents=false
            // is the resulting behaviour the apply path actually reads.
            var isReplace = HasTopLevelReplaceDirective(sourceComponents);
            if (isReplace)
            {
                // Single-line breadcrumb for FUSE.log so package-author
                // problem reports can be diagnosed at a glance — "did the
                // converter actually pick up $replace for industry X?"
                // The corresponding apply-time flags log in FuseModLoader
                // is the matched receipt that proves the directive
                // survived all the way to the loader.
                FuseLog.Info(
                    $"FUSE legacy converter detected $replace on components for industry '{id}'; " +
                    "emitting replaceComponents=true / mergeComponents=false so the apply phase " +
                    "trims any vanilla components not in the converted set.");
            }

            // Emit areaId / position / rotation only when the SC source
            // actually provided them. Vector(null, false) returns
            // {x:0,y:0,z:0}, which CleanObject would NOT strip — so
            // unconditional emission used to feed FuseIndustry.Position =
            // (0,0,0) into UpdateIndustry and yank existing base-game
            // industries to the origin. Same story for areaId on top-level
            // industry patches (e.g.
            // industries.whittier-sawmill.components.{R1: {...}}): a
            // string-typed JValue with null content survives CleanObject and
            // gets deserialized as a directive that reparents the industry
            // to an arbitrary first Area.
            var positionToken = item["localPosition"] ?? item["position"];
            var rotationToken = item["localRotation"] ?? item["rotation"];
            var resolvedAreaId = areaId ?? ReadString(item, "areaId", "area");
            var result = new JObject
            {
                ["name"] = ReadString(item, "name") ?? id,
                ["order"] = Clone(item["order"]),
                ["usesContract"] = ReadBool(item, "usesContract", false),
                ["mergeComponents"] = !isReplace,
                ["replaceComponents"] = isReplace,
                ["components"] = components
            };
            if (!string.IsNullOrWhiteSpace(resolvedAreaId))
            {
                result["areaId"] = resolvedAreaId;
            }

            if (positionToken != null && positionToken.Type != JTokenType.Null)
            {
                result["position"] = Vector(positionToken, false);
            }

            if (rotationToken != null && rotationToken.Type != JTokenType.Null)
            {
                result["rotation"] = Vector(rotationToken, false);
            }

            return CleanObject(result);
        }

        // Looks for a top-level <c>$replace</c> entry whose value is the
        // new component dictionary. We accept any directive-key spelling
        // accepted elsewhere by the converter (case-insensitive) so a
        // hand-edited <c>"$REPLACE"</c> still works. Nested $replace
        // inside an individual component is intentionally NOT counted
        // here — that's a sub-object replacement, not a wholesale
        // component-list rewrite.
        private static bool HasTopLevelReplaceDirective(JObject sourceComponents)
        {
            if (sourceComponents == null)
            {
                return false;
            }

            foreach (var property in sourceComponents.Properties())
            {
                if (string.Equals(property.Name, "$replace", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static JToken ConvertTurntable(string id, JObject item)
        {
            if (item == null)
            {
                return null;
            }

            var result = new JObject
            {
                ["position"] = Vector(item["position"] ?? item["Position"] ?? item["localPosition"] ?? item["LocalPosition"], false),
                ["rotation"] = Vector(item["rotation"] ?? item["Rotation"] ?? item["localRotation"] ?? item["LocalRotation"], false),
                ["radius"] = ReadFloat(item["radius"] ?? item["Radius"], 15f),
                ["subdivisions"] = ReadInt(item["subdivisions"] ?? item["Subdivisions"], 32),
                ["legacyIdentifier"] = ReadString(item, "legacyIdentifier") ??
                                       (IsTurntableHandler(ReadHandler(item)) ? id : null)
            };

            var roundhouse = item["roundhouse"] as JObject;
            var stalls = item["roundhouseStalls"] ?? item["RoundhouseStalls"];
            if (roundhouse != null)
            {
                result["roundhouse"] = CleanObject(new JObject
                {
                    ["stalls"] = ReadInt(roundhouse["stalls"], 0),
                    ["startAngle"] = ReadFloat(roundhouse["startAngle"], 0),
                    ["stallAngle"] = Clone(roundhouse["stallAngle"]),
                    ["trackLength"] = ReadFloat(roundhouse["trackLength"], 46),
                    ["startPrefab"] = ReadString(roundhouse, "startPrefab"),
                    ["endPrefab"] = ReadString(roundhouse, "endPrefab"),
                    ["stallPrefab"] = ReadString(roundhouse, "stallPrefab")
                });
            }
            else if (stalls != null && stalls.Type != JTokenType.Null)
            {
                result["roundhouse"] = CleanObject(new JObject
                {
                    ["stalls"] = ReadInt(stalls, 0),
                    ["trackLength"] = ReadFloat(item["roundhouseTrackLength"] ?? item["RoundhouseTrackLength"], 46),
                    ["startPrefab"] = ReadString(item, "startPrefab", "StartPrefab") ?? "vanilla://roundhouseStart",
                    ["endPrefab"] = ReadString(item, "endPrefab", "EndPrefab") ?? "vanilla://roundhouseEnd",
                    ["stallPrefab"] = ReadString(item, "stallPrefab", "StallPrefab") ?? "vanilla://roundhouseStall"
                });
            }

            return Clean(result);
        }

        private static JToken ConvertScenery(JToken token)
        {
            var item = token as JObject;
            if (item == null)
            {
                return null;
            }

            var model = ReadString(item, "assetIdentifier", "model", "modelIdentifier", "prefabIdentifier", "prefab") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(model) && !model.Contains("://"))
            {
                model = "scenery://" + model;
            }

            return CleanObject(new JObject
            {
                ["assetIdentifier"] = model,
                ["position"] = Vector(item["position"] ?? item["localPosition"], false),
                ["rotation"] = Vector(item["rotation"] ?? item["localRotation"], false),
                ["scale"] = Vector(item["scale"] ?? item["localScale"], true),
                ["anchorSpanIds"] = ToStringArray(item["anchorSpanIds"] ?? item["spanIds"] ?? item["spans"] ?? item["trackSpanIds"] ?? item["trackSpans"])
            });
        }

        private static void ConvertSplineys(JObject source, JObject root)
        {
            if (source == null)
            {
                return;
            }

            foreach (var property in source.Properties())
            {
                if (property.Value.Type == JTokenType.Null)
                {
                    ((JArray)root["world"]["removals"]["splineys"]).Add(property.Name);
                    continue;
                }

                if (!(property.Value is JObject item))
                {
                    continue;
                }

                var handler = ReadHandler(item);
                var points = ReadLegacyReplacementArray(item["points"]);
                if (IsTurntableHandler(handler))
                {
                    ((JObject)root["operations"]["turntables"])[property.Name] = ConvertTurntable(property.Name, item);
                }
                else if (LoaderHandlers.Contains(handler))
                {
                    ((JObject)root["operations"]["loaders"])[property.Name] = ConvertLoader(item);
                }
                else if (StationHandlers.Contains(handler))
                {
                    ((JObject)root["operations"]["stations"])[property.Name] = ConvertStation(item);
                }
                else if (IsMapLabelHandler(handler))
                {
                    ((JObject)root["world"]["mapLabels"])[property.Name] = ConvertLabel(property.Name, item);
                }
                else if (TelegraphPoleMoverHandlers.Contains(handler))
                {
                    foreach (var movement in ConvertTelegraphPoleMovements(item))
                    {
                        ((JArray)root["world"]["telegraphPoleMovements"]).Add(movement);
                    }
                }
                else if (RailroadCrossingHandlers.Contains(handler))
                {
                    ((JObject)root["world"]["scenery"])[property.Name] = ConvertScenery(item);
                }
                else if (!string.IsNullOrWhiteSpace(handler) &&
                         !SplineyHandlerMap.ContainsKey(handler) &&
                         points == null)
                {
                    // FUSE doesn't recognize this handler natively and the
                    // spliney has no curve geometry of its own; defer to
                    // whichever hosted old-loader plugin claims it via the
                    // GraphWillChangeEvent dispatched in Flush() below. FUSE
                    // intentionally does not know which plugin owns which
                    // handler — plugins self-select inside their event handler.
                    FuseSplineyPluginHost.Register(property.Name, item);
                    continue;
                }
                else if (points?.Count >= 2)
                {
                    ((JObject)root["world"]["splineys"])[property.Name] = ConvertSpliney(item);
                }
                else
                {
                    var legacyObjects = ((JObject)root["extensions"])["legacySplineyObjects"] as JObject ?? new JObject();
                    legacyObjects[property.Name] = item.DeepClone();
                    ((JObject)root["extensions"])["legacySplineyObjects"] = legacyObjects;
                }
            }

            // Fire GraphWillChangeEvent so any hosted old-loader plugin
            // subscribed via Messenger.Default can synthesize its own topology
            // from the deferred splineys. We then merge anything new the
            // plugins wrote into state.Tracks back into the converter root.
            var flush = FuseSplineyPluginHost.Flush(root);
            if (flush.PendingSplineyCount > 0)
            {
                FuseLog.Info(
                    "FUSE legacy spliney plugin host dispatched GraphWillChangeEvent " +
                    $"pendingSplineys={flush.PendingSplineyCount} " +
                    $"nodesAdded={flush.NodesAdded} segmentsAdded={flush.SegmentsAdded}.");
            }
        }

        private static string ReadHandler(JObject item)
        {
            return ReadString(item, "handler", "Handler") ?? string.Empty;
        }

        private static JArray ReadLegacyReplacementArray(JToken value)
        {
            if (value is JArray array)
            {
                return array;
            }

            if (value is JObject patch &&
                patch.TryGetValue("$replace", StringComparison.OrdinalIgnoreCase, out var replacement) &&
                replacement is JArray replacementArray)
            {
                return replacementArray;
            }

            return null;
        }

        private static JObject ConvertSpliney(JObject item)
        {
            var handler = ReadHandler(item);
            var offsetY = item["offsetY"] ?? item["offsety"];
            if (offsetY == null && string.Equals(handler, "StrangeCustoms.FlowyThingBuilder", StringComparison.OrdinalIgnoreCase))
            {
                offsetY = new JValue(-0.1f);
            }

            var points = new JArray();
            foreach (var point in ReadLegacyReplacementArray(item["points"])?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                points.Add(CleanObject(new JObject
                {
                    ["position"] = Vector(point["position"] ?? point["localPosition"], false),
                    ["rotation"] = Vector(point["rotation"] ?? point["localRotation"], false),
                    ["width"] = Clone(point["width"])
                }));
            }

            return CleanObject(new JObject
            {
                ["type"] = InferSplineyType(item, handler),
                ["profile"] = ReadString(item, "profile"),
                ["style"] = ReadString(item, "style"),
                ["offsetY"] = Clone(offsetY),
                ["headStyle"] = ReadString(item, "headStyle", "headstyle"),
                ["tailStyle"] = ReadString(item, "tailStyle", "tailstyle"),
                ["points"] = points
            });
        }

        private static string InferSplineyType(JObject item, string handler)
        {
            var style = ReadString(item, "style") ?? string.Empty;
            var profile = ReadString(item, "profile") ?? string.Empty;
            if (string.Equals(handler, "StrangeCustoms.FlowyThingBuilder", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(style, "river", StringComparison.OrdinalIgnoreCase) ||
                 profile.IndexOf("river", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return "river";
            }

            if (SplineyHandlerMap.TryGetValue(handler ?? string.Empty, out var type))
            {
                return type;
            }

            return ReadString(item, "type") ?? "unknown";
        }

        private static JObject ConvertLoader(JObject item)
        {
            return CleanObject(new JObject
            {
                ["position"] = Vector(item["position"] ?? item["Position"] ?? item["localPosition"] ?? item["LocalPosition"], false),
                ["rotation"] = Vector(item["rotation"] ?? item["Rotation"] ?? item["localRotation"] ?? item["LocalRotation"], false),
                ["prefab"] = ReadString(item, "prefab", "Prefab") ?? "empty://",
                ["industryId"] = ReadString(item, "industry", "Industry")
            });
        }

        private static JObject ConvertStation(JObject item)
        {
            return CleanObject(new JObject
            {
                ["position"] = Vector(item["position"] ?? item["Position"] ?? item["localPosition"] ?? item["LocalPosition"], false),
                ["rotation"] = Vector(item["rotation"] ?? item["Rotation"] ?? item["localRotation"] ?? item["LocalRotation"], false),
                ["prefab"] = ReadString(item, "prefab", "Prefab") ?? "empty://",
                ["passengerStopId"] = ReadString(item, "passengerStop", "PassengerStop")
            });
        }

        private static IEnumerable<JObject> ConvertTelegraphPoleMovements(JObject item)
        {
            var poles = item["polesToMove"] as JArray ?? item["PolesToMove"] as JArray ?? new JArray();
            var movements = item["poleMovement"] as JArray ?? item["PoleMovement"] as JArray ?? new JArray();
            for (var index = 0; index < poles.Count; index++)
            {
                var pole = ReadInt(poles[index], int.MinValue);
                if (pole == int.MinValue)
                {
                    continue;
                }

                yield return new JObject
                {
                    ["poleIndices"] = new JArray(pole),
                    ["offset"] = Vector(index < movements.Count ? movements[index] : null, false)
                };
            }
        }

        private static JObject ConvertSceneClone(string id, JToken token)
        {
            var item = token as JObject;
            if (item == null)
            {
                return null;
            }

            var source = ReadString(item, "source", "instantiateFrom");
            if (!string.IsNullOrWhiteSpace(source) && !source.Contains("://"))
            {
                source = "path://scene/" + source;
            }

            var result = new JObject
            {
                ["targetPath"] = ReadString(item, "targetPath") ?? id,
                ["source"] = source,
                ["enabled"] = Clone(item["enabled"])
            };

            if (HasAnyProperty(item, "localPosition", "position"))
            {
                result["localPosition"] = Vector(item["localPosition"] ?? item["position"], false);
            }

            if (HasAnyProperty(item, "localRotation", "rotation"))
            {
                result["localRotation"] = Vector(item["localRotation"] ?? item["rotation"], false);
            }

            if (HasAnyProperty(item, "localScale", "scale"))
            {
                result["localScale"] = Vector(item["localScale"] ?? item["scale"], true);
            }

            return CleanObject(result);
        }

        private static JObject ConvertLabel(string id, JObject item)
        {
            if (item == null)
            {
                return null;
            }

            var text = ReadString(item, "text");
            if (string.IsNullOrWhiteSpace(text))
            {
                text = id ?? string.Empty;
            }

            var result = new JObject
            {
                ["text"] = text,
                ["position"] = Vector(item["position"] ?? item["localPosition"], false),
                ["rotation"] = Vector(item["rotation"] ?? item["localRotation"], false),
                ["size"] = Clone(item["size"] ?? item["fontSize"]),
                ["color"] = Clone(item["color"])
            };

            var match = Regex.Match(text, @"^\s*(\d{1,3})\s*MPH\.?\s*$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var speed = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                result["text"] = speed.ToString(CultureInfo.InvariantCulture);
                result["style"] = "speedLimit";
                result["speedLimitMph"] = speed;
            }

            return CleanObject(result);
        }

        private static JObject ConvertLegacyStart(JObject source)
        {
            var spawn = source["spawnPoint"] as JObject;
            if (spawn == null)
            {
                return null;
            }

            return CleanObject(new JObject
            {
                ["name"] = ReadString(source, "name", "identifier") ?? "Legacy Start",
                ["position"] = Vector(spawn["position"] ?? spawn["location"], false),
                ["rotation"] = Vector(spawn["rotation"], false),
                ["radius"] = Clone(spawn["range"] ?? spawn["radius"])
            });
        }
    }
}
