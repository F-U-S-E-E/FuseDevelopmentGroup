using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FUSE.Runtime.API;
using FUSE.Authoring.Data;
using FUSE.Authoring.Data.Common;
using FUSE.Infrastructure;
using FUSE.Authoring.Serialization;
using Model.Ops;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FUSE.Loading
{
    internal static class FuseLegacyGameGraphCompatibility
    {
        private const string LegacyDataExtensionKey = "legacyData";
        private const string ExpandedFlag = "gameGraphPatchExpanded";
        private const string GameGraphTarget = "game-graph";

        public static void Expand(IReadOnlyList<FuseLoadedMod> loadedMods)
        {
            var candidates = (loadedMods ?? Array.Empty<FuseLoadedMod>())
                .Where(ShouldExpand)
                .ToArray();
            if (candidates.Length == 0)
            {
                return;
            }

            JObject state;
            try
            {
                state = CreateRuntimeState();
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE legacy game-graph compatibility expansion skipped because the runtime graph snapshot failed", ex);
                return;
            }

            var expanded = 0;
            foreach (var loaded in candidates)
            {
                try
                {
                    if (ExpandDefinition(loaded, state))
                    {
                        expanded++;
                    }
                }
                catch (Exception ex)
                {
                    var id = loaded?.Definition?.Id ?? "<unknown>";
                    FusePackageFaultRegistry.RecordFault(id, "legacy game-graph compatibility", ex.Message, ex);
                    FuseLog.Exception($"FUSE failed to expand legacy game-graph definition '{id}'", ex);
                }
            }

            if (expanded > 0)
            {
                FuseLog.Info($"FUSE expanded {expanded} legacy-converted game-graph definition(s) against the runtime graph snapshot.");
            }
        }

        private static bool ExpandDefinition(FuseLoadedMod loaded, JObject state)
        {
            var definition = loaded?.Definition;
            var sourcePath = ResolveSourcePath(loaded);
            if (definition == null || string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                FuseLog.Warning($"FUSE skipped legacy game-graph expansion for '{definition?.Id ?? "<unknown>"}' because the source JSON could not be resolved.");
                return false;
            }

            var source = FuseLegacyDataConverter.ReadLegacyObject(sourcePath);
            FuseLegacyJsonPatch.Apply(state, source, Path.GetFileName(sourcePath));
            MirrorAreaIndustriesToTopLevel(state, source);
            var slice = BuildPatchSlice(state, source);
            if (!slice.HasValues)
            {
                MarkExpanded(definition, sourcePath, false);
                return false;
            }

            var root = CreateConvertedRoot(definition, sourcePath);
            var manifest = CreateManifest(definition, loaded.FolderPath, sourcePath);
            FuseLegacyDataConverter.ConvertSource(slice, root, manifest);

            var expandedDefinition = FuseSerializer.FromJson(root.ToString(Formatting.None));
            definition.Tracks = expandedDefinition.Tracks;
            definition.Operations = expandedDefinition.Operations;
            definition.World = expandedDefinition.World;
            definition.Progression = expandedDefinition.Progression;
            MarkExpanded(definition, sourcePath, true);
            return true;
        }

        private static bool ShouldExpand(FuseLoadedMod loaded)
        {
            var definition = loaded?.Definition;
            if (definition == null || !IsLegacyConverted(definition) || IsExpanded(definition))
            {
                return false;
            }

            var target = definition.Mixinto?.Target;
            return string.IsNullOrWhiteSpace(target) ||
                   string.Equals(target, GameGraphTarget, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLegacyConverted(FuseModDefinition definition)
        {
            if (definition?.Tags?.Any(tag => string.Equals(tag, "legacy-converted", StringComparison.OrdinalIgnoreCase)) == true)
            {
                return true;
            }

            var legacyData = GetLegacyData(definition, false);
            return legacyData != null &&
                   legacyData.TryGetValue("convertedAtRuntime", StringComparison.OrdinalIgnoreCase, out var converted) &&
                   converted.Type == JTokenType.Boolean &&
                   converted.Value<bool>();
        }

        private static bool IsExpanded(FuseModDefinition definition)
        {
            var legacyData = GetLegacyData(definition, false);
            return legacyData != null &&
                   legacyData.TryGetValue(ExpandedFlag, StringComparison.OrdinalIgnoreCase, out var expanded) &&
                   expanded.Type == JTokenType.Boolean &&
                   expanded.Value<bool>();
        }

        private static void MarkExpanded(FuseModDefinition definition, string sourcePath, bool producedSlice)
        {
            var legacyData = GetLegacyData(definition, true);
            legacyData[ExpandedFlag] = true;
            legacyData["expandedSourceFile"] = Path.GetFileName(sourcePath ?? string.Empty);
            legacyData["expandedAgainstRuntimeSnapshot"] = producedSlice;
            legacyData["compatibilityTag"] = "legacy-converted";
            legacyData["supportStatus"] = "temporary";
            definition.Extensions[LegacyDataExtensionKey] = legacyData;
        }

        private static JObject GetLegacyData(FuseModDefinition definition, bool create)
        {
            if (definition == null)
            {
                return null;
            }

            definition.Extensions = definition.Extensions ?? new Dictionary<string, object>();
            if (definition.Extensions.TryGetValue(LegacyDataExtensionKey, out var value) && value != null)
            {
                if (value is JObject obj)
                {
                    return obj;
                }

                return JObject.FromObject(value, FuseSerializer.GetSerializer());
            }

            if (!create)
            {
                return null;
            }

            var created = new JObject();
            definition.Extensions[LegacyDataExtensionKey] = created;
            return created;
        }

        private static string ResolveSourcePath(FuseLoadedMod loaded)
        {
            if (loaded == null || string.IsNullOrWhiteSpace(loaded.FolderPath))
            {
                return null;
            }

            var sourceFile = loaded.Definition?.Mixinto?.SourceFile;
            if (string.IsNullOrWhiteSpace(sourceFile) &&
                !string.IsNullOrWhiteSpace(loaded.DefinitionPath) &&
                loaded.DefinitionPath.StartsWith("legacy://", StringComparison.OrdinalIgnoreCase))
            {
                sourceFile = loaded.DefinitionPath.Substring("legacy://".Length);
            }

            if (string.IsNullOrWhiteSpace(sourceFile))
            {
                return null;
            }

            var path = Path.Combine(loaded.FolderPath, sourceFile);
            return File.Exists(path) ? path : null;
        }

        private static FuseLegacyPackageManifest CreateManifest(FuseModDefinition definition, string folderPath, string sourcePath)
        {
            var legacyData = GetLegacyData(definition, false);
            var legacyId = legacyData?["sourcePackageId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(legacyId))
            {
                legacyId = string.IsNullOrWhiteSpace(folderPath) ? definition.Id : Path.GetFileName(folderPath);
            }

            return new FuseLegacyPackageManifest
            {
                LegacyId = legacyId,
                PackageId = definition.Id,
                DisplayName = definition.Name ?? definition.Id,
                Version = definition.ModVersion ?? "1.0.0",
                Author = definition.Author ?? string.Empty,
                SourceFiles = new[] { sourcePath }
            };
        }

        private static JObject CreateConvertedRoot(FuseModDefinition definition, string sourcePath)
        {
            var manifest = CreateManifest(definition, null, sourcePath);
            var root = FuseLegacyDataConverter.CreateSkeleton(manifest, Path.GetFileNameWithoutExtension(sourcePath ?? "legacy-game-graph"));
            root["id"] = definition.Id;
            root["name"] = definition.Name;
            root["author"] = definition.Author;
            root["modVersion"] = string.IsNullOrWhiteSpace(definition.ModVersion) ? "1.0.0" : definition.ModVersion;
            root["tags"] = new JArray((definition.Tags ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase));
            if (definition.Mixinto != null)
            {
                root["mixinto"] = JObject.FromObject(definition.Mixinto, FuseSerializer.GetSerializer());
            }

            var legacyData = GetLegacyData(definition, false);
            if (legacyData != null)
            {
                ((JObject)root["extensions"])[LegacyDataExtensionKey] = legacyData.DeepClone();
            }

            return root;
        }

        private static JObject CreateRuntimeState()
        {
            var progressions = SnapshotProgressions();
            var mapFeatures = SnapshotMapFeatures();
            return new JObject
            {
                ["tracks"] = SnapshotTracks(),
                ["loads"] = SnapshotLoads(),
                ["areas"] = SnapshotAreas(),
                ["industries"] = SnapshotTopLevelIndustries(),
                ["turntables"] = new JObject(),
                ["scenery"] = SnapshotScenery(),
                ["splineys"] = SnapshotSplineys(),
                ["mandelas"] = new JObject(),
                ["texts"] = new JObject(),
                ["simpleGraphs"] = new JObject(),
                ["progression"] = new JObject
                {
                    ["progressions"] = progressions.DeepClone(),
                    ["mapFeatures"] = mapFeatures.DeepClone()
                },
                ["progressions"] = progressions,
                ["mapFeatures"] = mapFeatures
            };
        }

        private static JObject SnapshotTracks()
        {
            var definition = TrackAPI.GetBaseGraphSnapshotDefinition() ?? TrackAPI.GetRuntimeGraphDefinition() ?? new FuseTrackDefinition();
            return new JObject
            {
                ["nodes"] = ToObject(definition.Nodes, SnapshotNode),
                ["segments"] = ToObject(definition.Segments, SnapshotSegment),
                ["spans"] = ToObject(definition.Spans, SnapshotSpan)
            };
        }

        private static JObject SnapshotLoads()
        {
            var result = new JObject();
            foreach (var load in LoadAPI.GetAllLoads())
            {
                if (load == null || string.IsNullOrWhiteSpace(load.id))
                {
                    continue;
                }

                var definition = LoadAPI.GetDefinition(load);
                result[load.id] = new JObject
                {
                    ["name"] = definition?.Name ?? load.description ?? load.id,
                    ["units"] = definition?.Units ?? load.units.ToString(),
                    ["density"] = ToValue(definition?.Density),
                    ["unitWeightInPounds"] = ToValue(definition?.UnitWeightInPounds),
                    ["importable"] = ToValue(definition?.Importable),
                    ["payPerQuantity"] = ToValue(definition?.PayPerQuantity),
                    ["costPerUnit"] = ToValue(definition?.CostPerUnit),
                    ["carTypeFilter"] = definition?.CarTypeFilter,
                    ["emptyCarType"] = definition?.EmptyCarType,
                    ["loadedCarType"] = definition?.LoadedCarType,
                    ["icon"] = definition?.Icon
                };
            }

            return result;
        }

        private static JObject SnapshotProgressions()
        {
            var result = new JObject();
            foreach (var progression in ProgressionAPI.GetAllProgressions())
            {
                if (progression == null || string.IsNullOrWhiteSpace(progression.identifier))
                {
                    continue;
                }

                var definition = ProgressionAPI.GetDefinition(progression);
                result[progression.identifier] = definition != null
                    ? JObject.FromObject(definition, FuseSerializer.GetSerializer())
                    : new JObject();
            }

            return result;
        }

        private static JObject SnapshotMapFeatures()
        {
            var result = new JObject();
            foreach (var feature in ProgressionAPI.GetAllMapFeatures())
            {
                if (feature == null || string.IsNullOrWhiteSpace(feature.identifier))
                {
                    continue;
                }

                var definition = ProgressionAPI.GetDefinition(feature);
                result[feature.identifier] = definition != null
                    ? JObject.FromObject(definition, FuseSerializer.GetSerializer())
                    : new JObject();
            }

            return result;
        }

        private static JObject SnapshotAreas()
        {
            var industries = IndustryAPI.GetAllIndustries()
                .Where(industry => industry != null && !string.IsNullOrWhiteSpace(industry.identifier))
                .GroupBy(industry => industry.GetComponentInParent<Area>(true)?.identifier ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

            var result = new JObject();
            foreach (var area in TrackAPI.GetAllAreas().Where(area => area != null && !string.IsNullOrWhiteSpace(area.identifier)))
            {
                var areaDefinition = TrackAPI.GetDefinition(area);
                var areaObject = new JObject
                {
                    ["name"] = areaDefinition?.Name ?? area.name,
                    ["localPosition"] = Vector(areaDefinition?.Position ?? area.transform.localPosition),
                    ["radius"] = areaDefinition?.Radius ?? area.radius,
                    ["tagColor"] = areaDefinition?.TagColor != null ? new JArray(areaDefinition.TagColor) : null,
                    ["order"] = ToValue(areaDefinition?.Order),
                    ["spanIds"] = areaDefinition?.SpanIds != null ? new JArray(areaDefinition.SpanIds) : null,
                    ["groupId"] = areaDefinition?.GroupId,
                    ["industries"] = new JObject()
                };

                if (industries.TryGetValue(area.identifier, out var areaIndustries))
                {
                    var industryObject = (JObject)areaObject["industries"];
                    foreach (var industry in areaIndustries)
                    {
                        industryObject[industry.identifier] = SnapshotIndustry(industry);
                    }
                }

                result[area.identifier] = areaObject;
            }

            return result;
        }

        private static JObject SnapshotTopLevelIndustries()
        {
            var result = new JObject();
            foreach (var industry in IndustryAPI.GetAllIndustries().Where(industry => industry != null && !string.IsNullOrWhiteSpace(industry.identifier)))
            {
                result[industry.identifier] = SnapshotIndustry(industry);
            }

            return result;
        }

        private static JObject SnapshotIndustry(Industry industry)
        {
            var definition = IndustryAPI.GetDefinition(industry);
            var result = new JObject
            {
                ["name"] = definition?.Name ?? industry.name,
                ["localPosition"] = Vector(definition?.Position ?? industry.transform.localPosition),
                ["localRotation"] = Vector(definition?.Rotation ?? industry.transform.localEulerAngles),
                ["usesContract"] = definition?.UsesContract ?? industry.usesContract,
                ["components"] = new JObject()
            };

            var components = EnumerateComponents(industry)
                .Where(component => component != null && !string.IsNullOrWhiteSpace(component.subIdentifier))
                .GroupBy(component => component.subIdentifier, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First());
            var componentObject = (JObject)result["components"];
            foreach (var component in components)
            {
                componentObject[component.subIdentifier] = SnapshotComponent(component);
            }

            return result;
        }

        private static IEnumerable<IndustryComponent> EnumerateComponents(Industry industry)
        {
            if (industry == null)
            {
                return Enumerable.Empty<IndustryComponent>();
            }

            try
            {
                if (industry.Components != null)
                {
                    return industry.Components.Where(component => component != null).ToArray();
                }
            }
            catch
            {
                // Fall through to the hierarchy scan.
            }

            return industry.GetComponentsInChildren<IndustryComponent>(true).Where(component => component != null).ToArray();
        }

        private static JObject SnapshotComponent(IndustryComponent component)
        {
            var definition = IndustryAPI.GetDefinition(component);
            var result = new JObject
            {
                ["type"] = definition?.Type,
                ["name"] = definition?.Name ?? component.name,
                ["trackSpans"] = definition?.TrackSpanIds != null ? new JArray(definition.TrackSpanIds) : new JArray(),
                ["carTypeFilter"] = definition?.CarTypeFilter,
                ["loadId"] = definition?.LoadId,
                ["convertedLoadId"] = definition?.ConvertedLoadId,
                ["sharedStorage"] = definition?.SharedStorage ?? component.sharedStorage,
                ["storageChangeRate"] = ToValue(definition?.StorageChangeRate),
                ["maxStorage"] = ToValue(definition?.MaxStorage),
                ["carTransferRate"] = ToValue(definition?.CarTransferRate),
                ["costPerUnit"] = ToValue(definition?.CostPerUnit),
                ["notBeforeHour"] = ToValue(definition?.NotBeforeHour),
                ["notAfterHour"] = ToValue(definition?.NotAfterHour),
                ["fillPercentage"] = ToValue(definition?.FillPercentage),
                ["bookReasons"] = definition?.BookReasons != null ? new JArray(definition.BookReasons) : null,
                ["title"] = definition?.Title,
                ["orderAroundEmpties"] = ToValue(definition?.OrderAroundEmpties),
                ["orderAroundLoaded"] = ToValue(definition?.OrderAroundLoaded),
                ["inputSpans"] = definition?.InputSpanIds != null ? new JArray(definition.InputSpanIds) : null,
                ["outputSpans"] = definition?.OutputSpanIds != null ? new JArray(definition.OutputSpanIds) : null,
                ["inputTermsPerDay"] = definition?.InputTermsPerDay != null ? JObject.FromObject(definition.InputTermsPerDay) : null,
                ["outputTermsPerDay"] = definition?.OutputTermsPerDay != null ? JObject.FromObject(definition.OutputTermsPerDay) : null,
                ["idealCars"] = ToValue(definition?.IdealCars),
                ["teamProfiles"] = definition?.TeamProfiles != null ? JObject.FromObject(definition.TeamProfiles, FuseSerializer.GetSerializer()) : null,
                ["canOverhaul"] = ToValue(definition?.CanOverhaul),
                ["passengerStopId"] = definition?.PassengerStopId,
                ["timetableCode"] = definition?.TimetableCode,
                ["basePopulation"] = ToValue(definition?.BasePopulation),
                ["neighborIds"] = definition?.NeighborIds != null ? new JArray(definition.NeighborIds) : null,
                ["branch"] = definition?.Branch,
                ["fields"] = definition?.Fields != null ? JObject.FromObject(definition.Fields, FuseSerializer.GetSerializer()) : null
            };

            RemoveNullProperties(result);
            return result;
        }

        private static JObject SnapshotScenery()
        {
            var result = new JObject();
            foreach (var scenery in SceneryAPI.GetAllScenery().Where(scenery => scenery != null && !string.IsNullOrWhiteSpace(scenery.name)))
            {
                var definition = SceneryAPI.GetDefinition(scenery);
                result[scenery.name] = new JObject
                {
                    ["assetIdentifier"] = definition?.AssetIdentifier,
                    ["model"] = definition?.Model,
                    ["localPosition"] = Vector(definition?.Position ?? scenery.transform.localPosition),
                    ["localRotation"] = Vector(definition?.Rotation ?? scenery.transform.localEulerAngles),
                    ["localScale"] = Vector(definition?.Scale ?? scenery.transform.localScale),
                    ["anchorSpanIds"] = definition?.AnchorSpanIds != null ? new JArray(definition.AnchorSpanIds) : null
                };
            }

            return result;
        }

        private static JObject SnapshotSplineys()
        {
            var result = new JObject();
            foreach (var root in SplineyAPI.GetAllSplineys().Where(root => root != null && !string.IsNullOrWhiteSpace(root.name)))
            {
                var definition = SplineyAPI.GetDefinition(root);
                if (definition == null)
                {
                    continue;
                }

                result[root.name] = JObject.FromObject(definition, FuseSerializer.GetSerializer());
            }

            return result;
        }

        private static JObject BuildPatchSlice(JObject state, JObject patch)
        {
            var slice = new JObject();
            CopyTracks(slice, state, patch);
            CopyDictionary(slice, state, patch, "loads");
            CopyAreas(slice, state, patch);
            CopyIndustries(slice, state, patch, "industries");
            CopyDictionary(slice, state, patch, "turntables");
            CopyDictionary(slice, state, patch, "scenery");
            CopyDictionary(slice, state, patch, "splineys");
            CopyDictionary(slice, state, patch, "mandelas");
            CopyDictionary(slice, state, patch, "texts");
            CopyDictionary(slice, state, patch, "simpleGraphs");
            CopyRaw(slice, patch, "spawnPoint");
            CopyProgression(slice, state, patch);
            return slice;
        }

        private static void MirrorAreaIndustriesToTopLevel(JObject state, JObject patch)
        {
            if (!(patch["areas"] is JObject patchAreas) ||
                !(state["areas"] is JObject stateAreas) ||
                !(state["industries"] is JObject topIndustries))
            {
                return;
            }

            foreach (var areaPatch in patchAreas.Properties().Where(property => !FuseLegacyJsonPatch.IsDirective(property.Name)))
            {
                if (!(areaPatch.Value is JObject patchArea) ||
                    !(GetPropertyValue(patchArea, "industries") is JObject patchIndustries) ||
                    !(GetPropertyValue(stateAreas, areaPatch.Name) is JObject stateArea) ||
                    !(GetPropertyValue(stateArea, "industries") is JObject stateIndustries))
                {
                    continue;
                }

                foreach (var industryPatch in patchIndustries.Properties().Where(property => !FuseLegacyJsonPatch.IsDirective(property.Name)))
                {
                    if (industryPatch.Value.Type == JTokenType.Null || IsRemovePatch(industryPatch.Value))
                    {
                        topIndustries.Remove(industryPatch.Name);
                        continue;
                    }

                    var stateIndustry = GetPropertyValue(stateIndustries, industryPatch.Name);
                    if (stateIndustry != null)
                    {
                        topIndustries[industryPatch.Name] = stateIndustry.DeepClone();
                    }
                }
            }
        }

        private static void CopyTracks(JObject slice, JObject state, JObject patch)
        {
            if (!(GetPropertyValue(patch, "tracks") is JObject patchTracks) ||
                !(GetPropertyValue(state, "tracks") is JObject stateTracks))
            {
                return;
            }

            var tracks = new JObject();
            foreach (var section in new[] { "nodes", "segments", "spans" })
            {
                if (GetPropertyValue(patchTracks, section) is JObject patchSection &&
                    GetPropertyValue(stateTracks, section) is JObject stateSection)
                {
                    var copied = CopyEntityDictionary(stateSection, patchSection);
                    if (copied.HasValues)
                    {
                        tracks[section] = copied;
                    }
                }
            }

            if (tracks.HasValues)
            {
                slice["tracks"] = tracks;
            }
        }

        private static void CopyAreas(JObject slice, JObject state, JObject patch)
        {
            if (!(GetPropertyValue(patch, "areas") is JObject patchAreas) ||
                !(GetPropertyValue(state, "areas") is JObject stateAreas))
            {
                return;
            }

            var areas = new JObject();
            foreach (var areaPatch in patchAreas.Properties().Where(property => !FuseLegacyJsonPatch.IsDirective(property.Name)))
            {
                if (areaPatch.Value.Type == JTokenType.Null || IsRemovePatch(areaPatch.Value))
                {
                    areas[areaPatch.Name] = JValue.CreateNull();
                    continue;
                }

                if (!(GetPropertyValue(stateAreas, areaPatch.Name) is JObject stateArea))
                {
                    continue;
                }

                if (!(areaPatch.Value is JObject patchArea) || IsWholeObjectReplacement(patchArea))
                {
                    areas[areaPatch.Name] = stateArea.DeepClone();
                    continue;
                }

                var area = CopyAreaMetadata(stateArea);
                if (GetPropertyValue(patchArea, "industries") is JObject patchIndustries &&
                    GetPropertyValue(stateArea, "industries") is JObject stateIndustries)
                {
                    var industries = CopyIndustryDictionary(stateIndustries, patchIndustries);
                    if (industries.HasValues)
                    {
                        area["industries"] = industries;
                    }
                }

                areas[areaPatch.Name] = area;
            }

            if (areas.HasValues)
            {
                slice["areas"] = areas;
            }
        }

        private static void CopyIndustries(JObject slice, JObject state, JObject patch, string key)
        {
            if (!(GetPropertyValue(patch, key) is JObject patchIndustries) ||
                !(GetPropertyValue(state, key) is JObject stateIndustries))
            {
                return;
            }

            var industries = CopyIndustryDictionary(stateIndustries, patchIndustries);
            if (industries.HasValues)
            {
                slice[key] = industries;
            }
        }

        private static JObject CopyIndustryDictionary(JObject stateIndustries, JObject patchIndustries)
        {
            var result = new JObject();
            foreach (var industryPatch in patchIndustries.Properties().Where(property => !FuseLegacyJsonPatch.IsDirective(property.Name)))
            {
                if (industryPatch.Value.Type == JTokenType.Null || IsRemovePatch(industryPatch.Value))
                {
                    result[industryPatch.Name] = JValue.CreateNull();
                    continue;
                }

                if (!(GetPropertyValue(stateIndustries, industryPatch.Name) is JObject stateIndustry))
                {
                    continue;
                }

                if (!(industryPatch.Value is JObject patchIndustry) || IsWholeObjectReplacement(patchIndustry))
                {
                    result[industryPatch.Name] = stateIndustry.DeepClone();
                    continue;
                }

                var industry = CopyIndustryMetadata(stateIndustry);
                if (GetPropertyValue(patchIndustry, "components") is JObject patchComponents &&
                    GetPropertyValue(stateIndustry, "components") is JObject stateComponents)
                {
                    var components = CopyComponentDictionary(stateComponents, patchComponents);
                    if (components.HasValues)
                    {
                        industry["components"] = components;
                    }
                }

                result[industryPatch.Name] = industry;
            }

            return result;
        }

        private static void CopyDictionary(JObject slice, JObject state, JObject patch, string key)
        {
            if (!(GetPropertyValue(patch, key) is JObject patchDictionary) ||
                !(GetPropertyValue(state, key) is JObject stateDictionary))
            {
                return;
            }

            var copied = CopyEntityDictionary(stateDictionary, patchDictionary);
            if (copied.HasValues)
            {
                slice[key] = copied;
            }
        }

        private static void CopyRaw(JObject slice, JObject patch, string key)
        {
            var value = GetPropertyValue(patch, key);
            if (value != null)
            {
                slice[key] = value.DeepClone();
            }
        }

        private static void CopyProgression(JObject slice, JObject state, JObject patch)
        {
            CopyDictionary(slice, state, patch, "progressions");
            CopyDictionary(slice, state, patch, "mapFeatures");

            if (!(GetPropertyValue(patch, "progression") is JObject patchProgression) ||
                !(GetPropertyValue(state, "progression") is JObject stateProgression))
            {
                return;
            }

            var progression = new JObject();
            if (GetPropertyValue(patchProgression, "progressionId") != null &&
                GetPropertyValue(stateProgression, "progressionId") != null)
            {
                progression["progressionId"] = GetPropertyValue(stateProgression, "progressionId").DeepClone();
            }
            else if (GetPropertyValue(patchProgression, "progressionId") != null)
            {
                progression["progressionId"] = GetPropertyValue(patchProgression, "progressionId").DeepClone();
            }

            if (GetPropertyValue(patchProgression, "sections") != null)
            {
                progression["sections"] = GetPropertyValue(patchProgression, "sections").DeepClone();
            }

            CopyProgressionDictionary(progression, stateProgression, patchProgression, "progressions");
            CopyProgressionDictionary(progression, stateProgression, patchProgression, "mapFeatures");
            if (progression.HasValues)
            {
                slice["progression"] = progression;
            }
        }

        private static void CopyProgressionDictionary(JObject progression, JObject stateProgression, JObject patchProgression, string key)
        {
            if (!(GetPropertyValue(patchProgression, key) is JObject patchDictionary) ||
                !(GetPropertyValue(stateProgression, key) is JObject stateDictionary))
            {
                return;
            }

            var copied = CopyEntityDictionary(stateDictionary, patchDictionary);
            if (copied.HasValues)
            {
                progression[key] = copied;
            }
        }

        private static JObject CopyComponentDictionary(JObject stateComponents, JObject patchComponents)
        {
            // Wholesale-replace semantics first. When the mod's source patch
            // declares <c>"components": { "$replace": { ... } }</c> the mod
            // author wants the component dict fully overwritten — that's
            // exactly the directive Foxy's East Whittier RepairIndustry.json
            // uses to drop vanilla's rip / rip-parts repair components when
            // renaming wh-e-engine to "East Whittier Fuel Service". The
            // legacy data converter detects this directive at apply time and
            // emits <c>replaceComponents=true</c> so the loader runs
            // RemoveStaleComponents. Without preserving the directive here,
            // the per-name copy loop below (with its <c>!IsDirective</c>
            // filter) would silently throw the whole $replace block away,
            // leaving the slice with an empty components dict that the
            // re-conversion pass turns into a no-op merge — the very
            // breakage where "East Whittier Fuel Service" was still showing
            // a repair track despite the $replace.
            if (patchComponents != null &&
                patchComponents.TryGetValue("$replace", StringComparison.OrdinalIgnoreCase, out var replaceToken) &&
                replaceToken is JObject replaceChildren)
            {
                return new JObject
                {
                    ["$replace"] = replaceChildren.DeepClone()
                };
            }

            var result = new JObject();
            foreach (var property in patchComponents.Properties().Where(property => !FuseLegacyJsonPatch.IsDirective(property.Name)))
            {
                if (property.Value.Type == JTokenType.Null || IsRemovePatch(property.Value))
                {
                    result[property.Name] = JValue.CreateNull();
                    continue;
                }

                if (property.Value is JObject patchComponent && HasTrackSpanListDirective(patchComponent))
                {
                    result[property.Name] = patchComponent.DeepClone();
                    continue;
                }

                var stateValue = GetPropertyValue(stateComponents, property.Name);
                if (stateValue != null)
                {
                    result[property.Name] = stateValue.DeepClone();
                }
            }

            return result;
        }

        private static JObject CopyEntityDictionary(JObject stateDictionary, JObject patchDictionary)
        {
            var result = new JObject();
            foreach (var property in patchDictionary.Properties().Where(property => !FuseLegacyJsonPatch.IsDirective(property.Name)))
            {
                if (property.Value.Type == JTokenType.Null || IsRemovePatch(property.Value))
                {
                    result[property.Name] = JValue.CreateNull();
                    continue;
                }

                var stateValue = GetPropertyValue(stateDictionary, property.Name);
                if (stateValue != null)
                {
                    result[property.Name] = stateValue.DeepClone();
                }
            }

            return result;
        }

        private static JObject CopyAreaMetadata(JObject stateArea)
        {
            return CopyKnownProperties(stateArea, "name", "localPosition", "position", "radius", "tagColor", "order", "spanIds", "spans", "groupId");
        }

        private static JObject CopyIndustryMetadata(JObject stateIndustry)
        {
            return CopyKnownProperties(stateIndustry, "name", "localPosition", "position", "localRotation", "rotation", "usesContract", "order");
        }

        private static JObject CopyKnownProperties(JObject source, params string[] names)
        {
            var result = new JObject();
            foreach (var name in names)
            {
                var value = GetPropertyValue(source, name);
                if (value != null)
                {
                    result[name] = value.DeepClone();
                }
            }

            return result;
        }

        private static bool HasTrackSpanListDirective(JObject component)
        {
            return HasStringListDirective(GetPropertyValue(component, "trackSpanIds")) ||
                   HasStringListDirective(GetPropertyValue(component, "trackSpans")) ||
                   HasStringListDirective(GetPropertyValue(component, "spans"));
        }

        private static bool HasStringListDirective(JToken token)
        {
            if (token is JObject obj)
            {
                return obj.Properties().Any(property => IsStringListDirective(property.Name));
            }

            return token is JArray array &&
                   array.OfType<JObject>().Any(item =>
                       item.Properties().Any(property => IsStringListDirective(property.Name)));
        }

        private static bool IsStringListDirective(string name)
        {
            switch ((name ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "$add":
                case "$append":
                case "$prepend":
                case "$insert":
                case "$replace":
                case "$remove":
                case "$delete":
                case "$find":
                    return true;
                default:
                    return false;
            }
        }

        private static JToken GetPropertyValue(JObject obj, string name)
        {
            if (obj == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return obj.Properties()
                .FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                ?.Value;
        }

        private static bool IsWholeObjectReplacement(JObject patch)
        {
            return patch != null && patch.TryGetValue("$replace", StringComparison.OrdinalIgnoreCase, out _);
        }

        private static bool IsRemovePatch(JToken token)
        {
            return token is JObject obj && FuseLegacyJsonPatch.IsRemovePatch(obj);
        }

        private static JObject SnapshotNode(string id, FuseNode definition)
        {
            return new JObject
            {
                ["position"] = Vector(definition?.Position ?? default),
                ["rotation"] = Vector(definition?.Rotation ?? default),
                ["flipSwitchStand"] = definition?.FlipSwitchStand ?? false,
                ["groupId"] = definition?.GroupId,
                ["tags"] = definition?.Tags != null ? new JArray(definition.Tags) : null
            };
        }

        private static JObject SnapshotSegment(string id, FuseSegment definition)
        {
            return new JObject
            {
                ["startNodeId"] = definition?.StartNodeId,
                ["endNodeId"] = definition?.EndNodeId,
                ["style"] = definition?.Style,
                ["trackClass"] = definition?.TrackClass,
                ["speedLimit"] = definition?.SpeedLimit ?? 45,
                ["priority"] = definition?.Priority ?? 0,
                ["groupId"] = definition?.GroupId,
                ["gauge"] = definition?.Gauge
            };
        }

        private static JObject SnapshotSpan(string id, FuseSpan definition)
        {
            return new JObject
            {
                ["upper"] = Location(definition?.Upper),
                ["lower"] = Location(definition?.Lower),
                ["normalize"] = definition?.Normalize ?? true,
                ["groupId"] = definition?.GroupId
            };
        }

        private static JObject Location(FuseTrackLocation location)
        {
            var result = new JObject
            {
                ["segmentId"] = location?.SegmentId ?? string.Empty,
                ["end"] = location?.End ?? "A",
                ["offset"] = location?.Offset ?? 0f
            };

            if (location?.Normalized != null)
            {
                result["normalized"] = location.Normalized.Value;
            }
            else
            {
                result["distance"] = location?.Distance ?? 0f;
            }

            return result;
        }

        private static JObject ToObject<T>(IDictionary<string, T> dictionary, Func<string, T, JObject> convert)
        {
            var result = new JObject();
            if (dictionary == null)
            {
                return result;
            }

            foreach (var pair in dictionary)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    result[pair.Key] = convert(pair.Key, pair.Value);
                }
            }

            return result;
        }

        private static JObject Vector(Vector3 value)
        {
            return new JObject
            {
                ["x"] = value.x,
                ["y"] = value.y,
                ["z"] = value.z
            };
        }

        private static JValue ToValue(float? value)
        {
            return value.HasValue ? new JValue(value.Value) : null;
        }

        private static JValue ToValue(int? value)
        {
            return value.HasValue ? new JValue(value.Value) : null;
        }

        private static JValue ToValue(bool? value)
        {
            return value.HasValue ? new JValue(value.Value) : null;
        }

        private static void RemoveNullProperties(JObject obj)
        {
            foreach (var property in obj.Properties().Where(property => property.Value == null || property.Value.Type == JTokenType.Null).ToArray())
            {
                property.Remove();
            }
        }
    }
}
