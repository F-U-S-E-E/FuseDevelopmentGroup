using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RAIL.API;
using RAIL.Data;
using RAIL.Events;
using RAIL.Infrastructure;
using RAIL.Serialization;
using RAIL.Validation;
using Newtonsoft.Json.Linq;

namespace RAIL.Loading
{
    public static class RailModLoader
    {
        private static readonly Dictionary<string, RailLoadedMod> LoadedMods = new Dictionary<string, RailLoadedMod>(StringComparer.OrdinalIgnoreCase);
        private static readonly RailDefinitionValidator Validator = new RailDefinitionValidator();

        public static RailLoadedMod LoadMod(string modFolder)
        {
            if (string.IsNullOrWhiteSpace(modFolder))
            {
                throw new ArgumentException("Mod folder is required.", nameof(modFolder));
            }

            var definitionPaths = ResolveDefinitionPaths(modFolder);
            RailLoadedMod loadedMod = null;
            for (var index = 0; index < definitionPaths.Length; index++)
            {
                var definitionPath = definitionPaths[index];
                var definition = RailSerializer.Load(definitionPath);
                LoadDefinition(definition, modFolder, definitionPath);
                loadedMod = LoadedMods[definition.Id];
            }

            return loadedMod;
        }

        public static void LoadDefinition(RailModDefinition definition, string folderPath = null, string definitionPath = null)
        {
            var validation = Validator.Validate(definition);
            RailEvents.RaiseValidationCompleted(definition != null ? definition.Id : string.Empty, validation);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException($"RAIL definition '{definition?.Id ?? "<null>"}' failed validation with {validation.Errors.Count} error(s).");
            }

            var firstLoad = !LoadedMods.TryGetValue(definition.Id, out var previousLoad);

            ApplyTrackRemovals(definition);
            ApplyTrackNodes(definition);
            ApplyTurntables(definition);
            ApplyTrackSegmentsAndSpans(definition);
            Track.Graph.Shared?.RebuildCollections();
            ApplyTrackAreas(definition);
            ApplyOperationsDefinition(definition);
            ApplyWorldDefinition(definition, folderPath);
            ApplyProgressionDefinition(definition);
            LoadedMods[definition.Id] = new RailLoadedMod(
                folderPath ?? previousLoad?.FolderPath,
                definitionPath ?? previousLoad?.DefinitionPath,
                definition);
            RailLog.Info($"RAIL applied data package '{definition.Id}' ({definition.Operations?.Turntables?.Count ?? 0} turntable(s), {definition.World?.SceneClones?.Count ?? 0} scene clone(s)).");

            if (firstLoad)
            {
                RailEvents.RaiseModLoaded(definition.Id);
            }
        }

        public static void UnloadMod(string modId)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                return;
            }

            if (LoadedMods.Remove(modId))
            {
                RailMapTileRegistry.UnregisterTileSources(modId);
                RailEvents.RaiseModUnloaded(modId);
            }
        }

        public static void UnloadAll()
        {
            var loadedIds = LoadedMods.Keys.ToArray();
            for (var index = 0; index < loadedIds.Length; index++)
            {
                UnloadMod(loadedIds[index]);
            }

            RailDataPackageDiscovery.ResetDiscovery();
        }

        public static IEnumerable<string> GetLoadedMods()
        {
            return LoadedMods.Keys;
        }

        public static RailModDefinition GetLoadedDefinition(string modId)
        {
            return !string.IsNullOrWhiteSpace(modId) && LoadedMods.TryGetValue(modId, out var loaded)
                ? loaded.Definition
                : null;
        }

        public static RailModDefinition ImportFromJson(string jsonPath)
        {
            return RailSerializer.Load(jsonPath);
        }

        public static void ExportToJson(string modId, string outputPath)
        {
            var definition = GetLoadedDefinition(modId);
            if (definition == null)
            {
                throw new InvalidOperationException($"RAIL mod '{modId}' is not loaded.");
            }

            RailSerializer.SaveJson(definition, outputPath);
        }

        public static void SaveAsBson(RailModDefinition definition, string outputPath)
        {
            RailSerializer.SaveBson(definition, outputPath);
        }

        internal static string ResolveDefinitionPath(string modFolder)
        {
            return ResolveDefinitionPaths(modFolder)[0];
        }

        internal static string[] ResolveDefinitionPaths(string modFolder)
        {
            var infoPath = Path.Combine(modFolder, "Info.json");
            if (File.Exists(infoPath))
            {
                var info = JObject.Parse(File.ReadAllText(infoPath));
                var explicitPaths = ResolveExplicitDefinitionPaths(modFolder, info).ToArray();
                if (explicitPaths.Length > 0)
                {
                    return explicitPaths;
                }
            }

            var bsonFiles = Directory.GetFiles(modFolder, "*.bson", SearchOption.TopDirectoryOnly);
            if (bsonFiles.Length > 0)
            {
                return new[] { bsonFiles[0] };
            }

            var jsonFiles = Directory.GetFiles(modFolder, "*.json", SearchOption.TopDirectoryOnly);
            for (var index = 0; index < jsonFiles.Length; index++)
            {
                if (!string.Equals(Path.GetFileName(jsonFiles[index]), "Info.json", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { jsonFiles[index] };
                }
            }

            throw new FileNotFoundException($"No RAIL .bson or .json definition was found in '{modFolder}'.");
        }

        private static IEnumerable<string> ResolveExplicitDefinitionPaths(string modFolder, JObject info)
        {
            foreach (var railDataFile in EnumerateRailDataFiles(info["RailDataFile"]))
            {
                yield return ResolveExistingDefinitionPath(modFolder, railDataFile);
            }

            foreach (var railDataFile in EnumerateRailDataFiles(info["RailDataFiles"]))
            {
                yield return ResolveExistingDefinitionPath(modFolder, railDataFile);
            }
        }

        private static IEnumerable<string> EnumerateRailDataFiles(JToken token)
        {
            if (token == null)
            {
                yield break;
            }

            if (token.Type == JTokenType.String)
            {
                var value = (string)token;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }

                yield break;
            }

            if (token.Type != JTokenType.Array)
            {
                yield break;
            }

            foreach (var item in token.Children())
            {
                if (item.Type != JTokenType.String)
                {
                    continue;
                }

                var value = (string)item;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }

        private static string ResolveExistingDefinitionPath(string modFolder, string railDataFile)
        {
            var explicitPath = Path.Combine(modFolder, railDataFile);
            if (!File.Exists(explicitPath))
            {
                throw new FileNotFoundException($"RAIL data file '{railDataFile}' was not found in '{modFolder}'.", explicitPath);
            }

            return explicitPath;
        }

        private static void ApplyTrackNodes(RailModDefinition definition)
        {
            if (definition?.Tracks == null)
            {
                return;
            }

            TrackAPI.BeginBatch();
            try
            {
                foreach (var node in definition.Tracks.Nodes)
                {
                    if (TrackAPI.GetNode(node.Key) == null)
                    {
                        TrackAPI.AddNode(node.Key, node.Value);
                    }
                    else
                    {
                        TrackAPI.UpdateNode(node.Key, node.Value);
                    }
                }
            }
            finally
            {
                TrackAPI.EndBatch();
            }
        }

        private static void ApplyTrackRemovals(RailModDefinition definition)
        {
            var removals = definition?.Tracks?.Removals;
            if (removals == null)
            {
                return;
            }

            TrackAPI.BeginBatch();
            try
            {
                foreach (var spanId in removals.Spans ?? Array.Empty<string>())
                {
                    if (TrackAPI.GetSpan(spanId) != null)
                    {
                        TrackAPI.RemoveSpan(spanId);
                    }
                }

                foreach (var segmentId in removals.Segments ?? Array.Empty<string>())
                {
                    if (TrackAPI.GetSegment(segmentId) != null)
                    {
                        TrackAPI.RemoveSegment(segmentId);
                    }
                }

                foreach (var nodeId in removals.Nodes ?? Array.Empty<string>())
                {
                    if (TrackAPI.GetNode(nodeId) != null)
                    {
                        TrackAPI.RemoveNode(nodeId);
                    }
                }
            }
            finally
            {
                TrackAPI.EndBatch();
            }
        }

        private static void ApplyTrackSegmentsAndSpans(RailModDefinition definition)
        {
            if (definition?.Tracks == null)
            {
                return;
            }

            TrackAPI.BeginBatch();
            try
            {
                foreach (var segment in definition.Tracks.Segments)
                {
                    var runtimeSegment = TrackAPI.GetSegment(segment.Key);
                    if (runtimeSegment == null)
                    {
                        TrackAPI.AddSegment(segment.Key, segment.Value);
                    }
                    else if (runtimeSegment.a.id != segment.Value.StartNodeId || runtimeSegment.b.id != segment.Value.EndNodeId)
                    {
                        TrackAPI.RemoveSegment(segment.Key);
                        TrackAPI.AddSegment(segment.Key, segment.Value);
                    }
                    else
                    {
                        TrackAPI.UpdateSegment(segment.Key, segment.Value);
                    }
                }

                foreach (var span in definition.Tracks.Spans)
                {
                    if (TrackAPI.GetSpan(span.Key) == null)
                    {
                        TrackAPI.AddSpan(span.Key, span.Value);
                    }
                    else
                    {
                        TrackAPI.UpdateSpan(span.Key, span.Value);
                    }
                }
            }
            finally
            {
                TrackAPI.EndBatch();
            }
        }

        private static void ApplyTrackAreas(RailModDefinition definition)
        {
            if (definition?.Tracks?.Areas == null)
            {
                return;
            }

            foreach (var area in definition.Tracks.Areas)
            {
                if (TrackAPI.GetArea(area.Key) == null)
                {
                    TrackAPI.AddArea(area.Key, area.Value);
                }
                else
                {
                    TrackAPI.UpdateArea(area.Key, area.Value);
                }
            }

            TrackAPI.ApplyAreaOrdering();
        }

        private static void ApplyTurntables(RailModDefinition definition)
        {
            if (definition?.Operations?.Turntables == null)
            {
                return;
            }

            foreach (var turntable in definition.Operations.Turntables)
            {
                if (TurntableAPI.GetTurntable(turntable.Key) == null)
                {
                    TurntableAPI.AddTurntable(turntable.Key, turntable.Value);
                }
                else
                {
                    TurntableAPI.UpdateTurntable(turntable.Key, turntable.Value);
                }
            }
        }

        private static void ApplyOperationsDefinition(RailModDefinition definition)
        {
            if (definition?.Operations == null)
            {
                return;
            }

            if (definition.Operations.Loads != null)
            {
                foreach (var load in definition.Operations.Loads)
                {
                    if (LoadAPI.GetLoad(load.Key) == null)
                    {
                        LoadAPI.AddLoad(load.Key, load.Value);
                    }
                    else
                    {
                        LoadAPI.UpdateLoad(load.Key, load.Value);
                    }
                }
            }

            if (definition.Operations.Industries != null)
            {
                var industriesTouched = false;
                foreach (var industry in definition.Operations.Industries)
                {
                    if (IndustryAPI.GetIndustry(industry.Key) == null)
                    {
                        IndustryAPI.AddIndustry(industry.Key, industry.Value, false);
                    }
                    else
                    {
                        IndustryAPI.UpdateIndustry(industry.Key, industry.Value, false);
                    }

                    industriesTouched = true;
                }

                if (industriesTouched)
                {
                    IndustryAPI.RefreshIndustriesAfterBatch("ApplyOperationsDefinition");
                }
            }

            if (definition.Operations.Loaders != null)
            {
                foreach (var loader in definition.Operations.Loaders)
                {
                    if (LoaderAPI.GetLoader(loader.Key) == null)
                    {
                        LoaderAPI.AddLoader(loader.Key, loader.Value);
                    }
                    else
                    {
                        LoaderAPI.UpdateLoader(loader.Key, loader.Value);
                    }
                }
            }

            if (definition.Operations.Stations != null)
            {
                foreach (var station in definition.Operations.Stations)
                {
                    if (StationAPI.GetStationAgent(station.Key) == null)
                    {
                        StationAPI.AddStationAgent(station.Key, station.Value);
                    }
                    else
                    {
                        StationAPI.UpdateStationAgent(station.Key, station.Value);
                    }
                }
            }
        }

        private static void ApplyWorldDefinition(RailModDefinition definition, string folderPath)
        {
            if (definition?.World == null)
            {
                return;
            }

            RailMapTileRegistry.RegisterTileSources(definition.Id, folderPath, definition.World);

            if (definition.World.Scenery != null)
            {
                foreach (var scenery in definition.World.Scenery)
                {
                    if (SceneryAPI.GetScenery(scenery.Key) == null)
                    {
                        SceneryAPI.AddScenery(scenery.Key, scenery.Value);
                    }
                    else
                    {
                        SceneryAPI.UpdateScenery(scenery.Key, scenery.Value);
                    }
                }
            }

            if (definition.World.MapLabels != null)
            {
                foreach (var label in definition.World.MapLabels)
                {
                    if (MapAPI.GetMapLabel(label.Key) == null)
                    {
                        MapAPI.AddMapLabel(label.Key, label.Value);
                    }
                    else
                    {
                        MapAPI.UpdateMapLabel(label.Key, label.Value);
                    }
                }
            }

            if (definition.World.Splineys != null)
            {
                foreach (var spliney in definition.World.Splineys)
                {
                    if (SplineyAPI.GetSpliney(spliney.Key) == null)
                    {
                        SplineyAPI.AddSpliney(spliney.Key, spliney.Value);
                    }
                    else
                    {
                        SplineyAPI.UpdateSpliney(spliney.Key, spliney.Value);
                    }
                }
            }

            if (definition.World.TelegraphPoles != null)
            {
                foreach (var telegraph in definition.World.TelegraphPoles)
                {
                    if (MapAPI.GetTelegraphPoles(telegraph.Key) == null)
                    {
                        MapAPI.AddTelegraphPoles(telegraph.Key, telegraph.Value);
                    }
                    else
                    {
                        MapAPI.UpdateTelegraphPoles(telegraph.Key, telegraph.Value);
                    }
                }
            }

            if (definition.World.MapMasks != null)
            {
                foreach (var mask in definition.World.MapMasks)
                {
                    if (MapAPI.GetMapMask(mask.Key) == null)
                    {
                        MapAPI.AddMapMask(mask.Key, mask.Value);
                    }
                    else
                    {
                        MapAPI.UpdateMapMask(mask.Key, mask.Value);
                    }
                }
            }

            if (definition.World.SceneClones != null)
            {
                foreach (var sceneClone in definition.World.SceneClones)
                {
                    if (SceneCloneAPI.GetSceneClone(sceneClone.Key) == null)
                    {
                        SceneCloneAPI.AddSceneClone(sceneClone.Key, sceneClone.Value);
                    }
                    else
                    {
                        SceneCloneAPI.UpdateSceneClone(sceneClone.Key, sceneClone.Value);
                    }
                }
            }
        }

        private static void ApplyProgressionDefinition(RailModDefinition definition)
        {
            if (definition?.Progression == null)
            {
                return;
            }

            if (definition.Progression.MapFeatures != null)
            {
                foreach (var feature in definition.Progression.MapFeatures)
                {
                    if (ProgressionAPI.GetMapFeature(feature.Key) == null)
                    {
                        ProgressionAPI.AddMapFeature(feature.Key, feature.Value);
                    }
                    else
                    {
                        ProgressionAPI.UpdateMapFeature(feature.Key, feature.Value);
                    }
                }
            }

            if (definition.Progression.Progressions != null)
            {
                foreach (var progression in definition.Progression.Progressions)
                {
                    if (ProgressionAPI.GetProgression(progression.Key) == null)
                    {
                        ProgressionAPI.AddProgression(progression.Key, progression.Value);
                    }
                    else
                    {
                        ProgressionAPI.UpdateProgression(progression.Key, progression.Value);
                    }
                }
            }
        }
    }
}
