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
        private static readonly HashSet<string> ExcludedSourceFiles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Definition.json",
                "Info.json",
                "Catalog.json",
                "Definitions.json",
                "conversion-report.json"
            };

        private static readonly HashSet<string> LoaderHandlers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "StrangeCustoms.LoaderBuilder",
                "StrangeCustoms.UnloaderBuilder",
                "AlinasMapMod.LoaderBuilder",
                "AlinasMapMod.Loaders.LoaderBuilder",
                "AlinasMapMod.UnloaderBuilder"
            };

        private static readonly HashSet<string> StationHandlers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "StrangeCustoms.StationBuilder",
                "AlinasMapMod.StationBuilder",
                "AlinasMapMod.Stations.StationAgentBuilder"
            };

        private static readonly HashSet<string> TelegraphPoleMoverHandlers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "StrangeCustoms.TelegraphPoleMover",
                "AlinasMapMod.TelegraphPoleMover",
                "AlinasMapMod.TelegraphPoles.TelegraphPoleMover"
            };

        private static readonly HashSet<string> TurntableHandlers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "StrangeCustoms.TurntableBuilder",
                "StrangeCustoms.Turntable.TurntableBuilder",
                "AlinasMapMod.TurntableBuilder",
                "AlinasMapMod.Turntable.TurntableBuilder"
            };

        private static readonly HashSet<string> RailroadCrossingHandlers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "strangecustoms.railroadcrossingbuilder",
                "strangecustoms.rrcrossingbuilder",
                "alinasmapmod.railroadcrossingbuilder",
                "alinasmapmod.rrcrossingbuilder"
            };

        private static readonly Dictionary<string, string> SplineyHandlerMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "StrangeCustoms.FlowyThingBuilder", "road" },
                { "StrangeCustoms.Tracks.FlowyThingBuilder", "road" },
                { "AlinasMapMod.FlowyThingBuilder", "road" },
                { "StrangeCustoms.RiverBuilder", "river" },
                { "AlinasMapMod.RiverBuilder", "river" },
                { "StrangeCustoms.RoadBuilder", "road" },
                { "AlinasMapMod.RoadBuilder", "road" },
                { "StrangeCustoms.AutoTrestle", "trestle" },
                { "StrangeCustoms.AutoTrestleBuilder", "trestle" },
                { "StrangeCustoms.TrestleBuilder", "trestle" },
                { "AlinasMapMod.TrestleBuilder", "trestle" }
            };

        private static readonly HashSet<string> ComponentSchemaKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "type",
                "Type",
                "partial",
                "name",
                "trackSpanIds",
                "trackSpans",
                "spans",
                "carTypeFilter",
                "loadId",
                "LoadId",
                "load",
                "convertedLoadId",
                "convertedLoad",
                "sharedStorage",
                "storageChangeRate",
                "maxStorage",
                "carTransferRate",
                "costPerUnit",
                "notBeforeHour",
                "notAfterHour",
                "fillPercentage",
                "bookReasons",
                "title",
                "orderAroundEmpties",
                "orderAroundLoaded",
                "inputSpanIds",
                "inputSpans",
                "outputSpanIds",
                "outputSpans",
                "inputTermsPerDay",
                "outputTermsPerDay",
                "idealCars",
                "teamProfiles",
                "canOverhaul",
                "passengerStopId",
                "passengerStop",
                "timetableCode",
                "basePopulation",
                "neighborIds",
                "neighbors",
                "branch",
                "fields",
                "extraData",
                "ExtraData"
            };

        private const string MapLabelHandler = "StrangeCustoms.MapLabelBuilder";

        public static bool TryReadLegacyManifest(string folderPath, out FuseLegacyPackageManifest manifest)
        {
            manifest = null;
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return false;
            }

            var definitionPath = Path.Combine(folderPath, "Definition.json");
            if (!File.Exists(definitionPath))
            {
                return false;
            }

            JObject definition;
            try
            {
                definition = ReadLegacyObject(definitionPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                FuseLog.Exception($"FUSE ignored legacy data package '{folderPath}' because Definition.json could not be parsed", ex);
                return false;
            }

            var sourceFiles = EnumerateLegacySourceFiles(folderPath, definition)
                .Where(LooksLikeLegacyDataSource)
                .ToArray();
            var mapTileSources = EnumerateLegacyMapTileSources(folderPath)
                .ToArray();
            if (sourceFiles.Length == 0 && mapTileSources.Length == 0)
            {
                return false;
            }

            var legacyId = ReadString(definition, "id", "Id");
            if (string.IsNullOrWhiteSpace(legacyId))
            {
                legacyId = Path.GetFileName(folderPath);
            }

            var packageId = EnsureFusePackageId(legacyId);
            var displayName = ReadString(definition, "name", "DisplayName", "Name");
            manifest = new FuseLegacyPackageManifest
            {
                LegacyId = legacyId,
                PackageId = packageId,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? legacyId : displayName,
                Version = ReadString(definition, "version", "Version") ?? string.Empty,
                Author = ReadString(definition, "author", "Author") ?? string.Empty,
                RequiredPackageIds = ReadLegacyRequiredDependencyIds(definition),
                LoadAfter = ReadLegacyDependencyIds(definition),
                SourceFiles = sourceFiles,
                MapTileSources = mapTileSources
            };
            return true;
        }

        public static FuseLoadedMod LoadPackage(string folderPath)
        {
            if (!TryReadLegacyManifest(folderPath, out var manifest))
            {
                throw new InvalidOperationException($"'{folderPath}' is not a supported legacy data package.");
            }

            var mixintos = ReadMixintoMetadata(folderPath);
            var loadedDefinitions = new List<FuseLoadedMod>();
            var usedFragments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (manifest.MapTileSources != null && manifest.MapTileSources.Length > 0)
            {
                var root = CreateSkeleton(manifest, "map-tiles");
                var mapTiles = (JObject)root["world"]["mapTiles"];
                foreach (var source in manifest.MapTileSources)
                {
                    mapTiles[source.Id] = CleanObject(new JObject
                    {
                        ["directory"] = source.Directory,
                        ["sourceFolder"] = source.SourceFolder,
                        ["priority"] = source.Priority
                    });
                }

                var definition = FuseSerializer.FromJson(root.ToString(Formatting.None));
                var definitionPath = "legacy://map-tiles";
                FuseModLoader.LoadDefinition(definition, folderPath, definitionPath);
                loadedDefinitions.Add(new FuseLoadedMod(folderPath, definitionPath, definition));
                usedFragments.Add("map-tiles");
            }

            foreach (var sourceFile in SortSourceFiles(manifest.SourceFiles, mixintos, folderPath))
            {
                var fragment = UniqueFragment(Slug(Path.GetFileNameWithoutExtension(sourceFile)), usedFragments);
                var root = CreateSkeleton(manifest, fragment);
                var sourceKey = GetPackageRelativePath(folderPath, sourceFile);
                if (!mixintos.TryGetValue(sourceKey, out var mixinto))
                {
                    mixintos.TryGetValue(Path.GetFileName(sourceFile), out mixinto);
                }

                if (mixinto != null)
                {
                    root["mixinto"] = mixinto;
                }

                // Audio packs (whistles.json / horns.json / bells.json
                // and SC-era variants like myhorns.json or
                // CollieQuillHorns.json) use a JSON ARRAY at the root
                // instead of an object, and have no shared key with the
                // dictionary-shaped legacy sources ConvertSource expects.
                // Route them through the audio-specific converter so the
                // entries land in the FUSE audio dict instead of being
                // dropped by the "not an object" branch in ReadLegacyObject.
                if (TryClassifyLegacyAudioFile(sourceFile, out var audioKind))
                {
                    ConvertLegacyAudioSource(sourceFile, audioKind, root);
                }
                else
                {
                    var source = ReadLegacyObject(sourceFile);
                    ConvertSource(source, root, manifest);
                }
                var definition = FuseSerializer.FromJson(root.ToString(Formatting.None));
                var definitionPath = "legacy://" + sourceKey;
                FuseModLoader.LoadDefinition(definition, folderPath, definitionPath);
                loadedDefinitions.Add(new FuseLoadedMod(folderPath, definitionPath, definition));
            }

            FuseLog.Info(
                $"FUSE converted legacy data package '{manifest.LegacyId}' " +
                $"to {loadedDefinitions.Count} in-memory FUSE definition(s) from '{folderPath}'.");
            return loadedDefinitions.LastOrDefault();
        }

        public static string EnsureFusePackageId(string id)
        {
            var value = string.IsNullOrWhiteSpace(id) ? "LegacyDataPackage" : id.Trim();
            return value.EndsWith(".FUSE", StringComparison.OrdinalIgnoreCase)
                ? value
                : value + ".FUSE";
        }

        private static IEnumerable<string> EnumerateLegacySourceFiles(string folderPath, JObject definition)
        {
            var manifestSourceFiles = EnumerateManifestSourceFiles(folderPath, definition)
                .Where(IsLegacySourceFile)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (manifestSourceFiles.Length > 0)
            {
                foreach (var path in manifestSourceFiles)
                {
                    yield return path;
                }

                yield break;
            }

            foreach (var path in Directory.GetFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly)
                         .Where(IsLegacySourceFile)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }

        private static IEnumerable<string> EnumerateManifestSourceFiles(string folderPath, JObject definition)
        {
            foreach (var reference in EnumerateMixintoReferences(definition["mixintos"] ?? definition["Mixintos"]))
            {
                var path = ResolvePackageFile(folderPath, reference);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    yield return path;
                }
            }
        }

        private static IEnumerable<FuseLegacyMapTileSource> EnumerateLegacyMapTileSources(string folderPath)
        {
            foreach (var mapsRootName in new[] { "Maps", "MapTiles" })
            {
                var mapsRoot = Path.Combine(folderPath, mapsRootName);
                if (!Directory.Exists(mapsRoot))
                {
                    continue;
                }

                foreach (var directory in Directory.GetDirectories(mapsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    string[] tileFiles;
                    try
                    {
                        tileFiles = Directory.GetFiles(directory, "tile_*_*.data", SearchOption.TopDirectoryOnly);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        FuseLog.Exception($"FUSE skipped legacy map tile folder '{directory}' because it could not be enumerated", ex);
                        continue;
                    }

                    if (tileFiles.Length == 0)
                    {
                        continue;
                    }

                    var directoryName = Path.GetFileName(directory);
                    yield return new FuseLegacyMapTileSource
                    {
                        Id = Slug(mapsRootName + "-" + directoryName),
                        Directory = directoryName,
                        SourceFolder = NormalizePackagePath(Path.Combine(mapsRootName, directoryName)),
                        Priority = 100
                    };
                }
            }
        }

        private static IEnumerable<string> SortSourceFiles(IEnumerable<string> sourceFiles, IDictionary<string, JObject> mixintos, string folderPath)
        {
            return sourceFiles
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => TryGetMixintoOrder(mixintos, folderPath, path, out _) ? 0 : 1)
                .ThenBy(path => TryGetMixintoOrder(mixintos, folderPath, path, out var index) ? index : int.MaxValue)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryGetMixintoOrder(IDictionary<string, JObject> mixintos, string folderPath, string sourceFile, out int order)
        {
            order = int.MaxValue;
            if (mixintos == null || string.IsNullOrWhiteSpace(sourceFile))
            {
                return false;
            }

            var sourceKey = GetPackageRelativePath(folderPath, sourceFile);
            if (!mixintos.TryGetValue(sourceKey, out var mixinto) &&
                !mixintos.TryGetValue(Path.GetFileName(sourceFile), out mixinto))
            {
                return false;
            }

            order = ReadInt(mixinto?["order"], int.MaxValue);
            return true;
        }

        private static bool IsLegacySourceFile(string path)
        {
            var fileName = Path.GetFileName(path);
            return !string.IsNullOrWhiteSpace(fileName) &&
                   !ExcludedSourceFiles.Contains(fileName) &&
                   !fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) &&
                   fileName.IndexOf("signal", StringComparison.OrdinalIgnoreCase) < 0 &&
                   string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeLegacyDataSource(string path)
        {
            // Audio packs (whistles.json / horns.json / bells.json and
            // SC-era variants) live at JSON array root, which makes
            // ReadLegacyObject below throw "Current JsonReader item is
            // not an object: StartArray" — historically that dropped
            // every horn/whistle/bell pack from a load. Recognise them
            // by filename here so the source-file pipeline picks them
            // up and routes them through ConvertLegacyAudioSource.
            if (TryClassifyLegacyAudioFile(path, out _))
            {
                return true;
            }

            try
            {
                var source = ReadLegacyObject(path);
                var dataKeys = new[]
                {
                    "tracks",
                    "loads",
                    "areas",
                    "industries",
                    "turntables",
                    "scenery",
                    "splineys",
                    "mandelas",
                    "texts",
                    "progression",
                    "progressions",
                    "mapFeatures",
                    "simpleGraphs",
                    "spawnPoint",
                    // game-migrations payload. Legacy mods (e.g. mods that
                    // renamed an industry id between versions) ship a
                    // separate migrations.json referenced from
                    // <c>mixintos["game-migrations"]</c>. The file's only
                    // top-level keys are <c>waybillDestinations</c> and
                    // <c>properties</c>, so without these in the recognized
                    // set the file gets dropped silently and the mod can't
                    // forward-port older saves.
                    "waybillDestinations",
                    "WaybillDestinations"
                };

                return dataKeys.Any(key => source[key] != null);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                FuseLog.Exception($"FUSE skipped legacy data candidate '{path}' because it could not be parsed", ex);
                return false;
            }
        }

        private static Dictionary<string, JObject> ReadMixintoMetadata(string folderPath)
        {
            var result = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            var definitionPath = Path.Combine(folderPath, "Definition.json");
            if (!File.Exists(definitionPath))
            {
                return result;
            }

            var definition = ReadLegacyObject(definitionPath);
            var mixintos = definition["mixintos"] ?? definition["Mixintos"];
            if (!(mixintos is JObject mixintoObject))
            {
                return result;
            }

            var order = 0;
            foreach (var property in mixintoObject.Properties())
            {
                VisitMixinto(property.Name, property.Value, result, ref order);
            }

            return result;
        }

        private static void VisitMixinto(string target, JToken value, IDictionary<string, JObject> result, ref int order)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                return;
            }

            if (value.Type == JTokenType.String)
            {
                RecordMixinto(target, value.Value<string>(), null, result, order++);
                return;
            }

            if (value is JArray array)
            {
                foreach (var item in array)
                {
                    VisitMixinto(target, item, result, ref order);
                }

                return;
            }

            if (value is JObject obj)
            {
                RecordMixinto(
                    target,
                    ReadString(obj, "mixinto", "Mixinto"),
                    ConvertRequirements(obj["requires"] ?? obj["Requires"]),
                    result,
                    order++);
            }
        }

        private static void RecordMixinto(string target, string reference, JArray requirements, IDictionary<string, JObject> result, int order)
        {
            var sourceFile = ExtractFileReference(reference);
            if (string.IsNullOrWhiteSpace(sourceFile))
            {
                return;
            }

            var key = NormalizePackagePath(sourceFile);
            result[key] = CleanObject(new JObject
            {
                ["target"] = target ?? string.Empty,
                ["sourceFile"] = key,
                ["order"] = order,
                ["requires"] = requirements ?? new JArray()
            });
        }

        private static IEnumerable<string> EnumerateMixintoReferences(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                yield break;
            }

            if (value.Type == JTokenType.String)
            {
                var reference = ExtractFileReference(value.Value<string>());
                if (!string.IsNullOrWhiteSpace(reference))
                {
                    yield return reference;
                }

                yield break;
            }

            if (value is JArray array)
            {
                foreach (var item in array)
                {
                    foreach (var reference in EnumerateMixintoReferences(item))
                    {
                        yield return reference;
                    }
                }

                yield break;
            }

            if (value is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    foreach (var reference in EnumerateMixintoReferences(property.Value))
                    {
                        yield return reference;
                    }
                }
            }
        }

        private static string ExtractFileReference(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var match = Regex.Match(value, @"\(([^)]+\.json)\)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim().Trim('"', '\'') : value.Trim();
        }

        private static JArray ConvertRequirements(JToken value)
        {
            var result = new JArray();
            if (!(value is JArray array))
            {
                return result;
            }

            foreach (var item in array)
            {
                var id = item.Type == JTokenType.String
                    ? item.Value<string>()
                    : ReadString(item as JObject, "id", "Id");
                if (string.IsNullOrWhiteSpace(id) || IsCoreLegacyRequirement(id))
                {
                    continue;
                }

                result.Add(CleanObject(new JObject
                {
                    ["id"] = id,
                    ["notBefore"] = ReadString(item as JObject, "notBefore", "NotBefore"),
                    ["notAfter"] = ReadString(item as JObject, "notAfter", "NotAfter")
                }));
            }

            return result;
        }

        private static string[] ReadLegacyDependencyIds(JObject definition)
        {
            var result = new List<string>(ReadLegacyRequiredDependencyIds(definition));

            foreach (var id in ReadStringArray(definition["loadAfter"] ?? definition["LoadAfter"]))
            {
                if (!IsCoreLegacyRequirement(id))
                {
                    result.Add(EnsureFusePackageId(id));
                }
            }

            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string[] ReadLegacyRequiredDependencyIds(JObject definition)
        {
            var result = new List<string>();
            foreach (var requirement in ConvertRequirements(ReadLegacyRequirements(definition)).OfType<JObject>())
            {
                var id = ReadString(requirement, "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    result.Add(EnsureFusePackageId(id));
                }
            }

            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static JToken ReadLegacyRequirements(JObject definition)
        {
            return definition?["requires"] ??
                   definition?["Requires"] ??
                   definition?["requirements"] ??
                   definition?["Requirements"];
        }

        private static bool IsCoreLegacyRequirement(string id)
        {
            var value = (id ?? string.Empty).Trim().ToLowerInvariant();
            return value == "railroader" ||
                   value == "railloader" ||
                   value == "rail-loader" ||
                   value == "alinanova21.alinasmapmod" ||
                   value == "alinasmapmod" ||
                   value == "alinamapmod" ||
                   value == "alinanova21.mapeditor" ||
                   value == "mapeditor" ||
                   value == "mmapeditor" ||
                   value == "alinanova21.alinasmapexpansion" ||
                   value == "alinasmapexpansion" ||
                   value == "zamu.strangecustoms" ||
                   value == "strangecustoms" ||
                   value == "zamu.confusingsupplements" ||
                   value == "confusingsupplements";
        }

        internal static JObject CreateSkeleton(FuseLegacyPackageManifest manifest, string fragment)
        {
            var id = manifest.PackageId + "." + fragment;
            return new JObject
            {
                ["schemaVersion"] = "1.0",
                ["id"] = id,
                ["name"] = manifest.DisplayName,
                ["author"] = manifest.Author,
                ["modVersion"] = string.IsNullOrWhiteSpace(manifest.Version) ? "1.0.0" : manifest.Version,
                ["tags"] = new JArray("legacy-converted"),
                ["coordinateSpace"] = "world",
                ["tracks"] = new JObject
                {
                    ["nodes"] = new JObject(),
                    ["segments"] = new JObject(),
                    ["spans"] = new JObject(),
                    ["areas"] = new JObject(),
                    ["removals"] = new JObject
                    {
                        ["nodes"] = new JArray(),
                        ["segments"] = new JArray(),
                        ["spans"] = new JArray()
                    }
                },
                ["operations"] = new JObject
                {
                    ["loads"] = new JObject(),
                    ["industries"] = new JObject(),
                    ["loaders"] = new JObject(),
                    ["turntables"] = new JObject(),
                    ["stations"] = new JObject(),
                    ["removals"] = new JObject
                    {
                        ["industries"] = new JArray()
                    }
                },
                ["world"] = new JObject
                {
                    ["scenery"] = new JObject(),
                    ["spawnPoints"] = new JArray(),
                    ["splineys"] = new JObject(),
                    ["telegraphPoles"] = new JObject(),
                    ["telegraphPoleMovements"] = new JArray(),
                    ["mapLabels"] = new JObject(),
                    ["mapMasks"] = new JObject(),
                    ["mapTiles"] = new JObject(),
                    ["sceneClones"] = new JObject(),
                    ["suppressBaseScenePaths"] = new JArray(),
                    ["removals"] = new JObject
                    {
                        ["scenery"] = new JArray(),
                        ["splineys"] = new JArray(),
                        ["telegraphPoles"] = new JArray(),
                        ["mapLabels"] = new JArray(),
                        ["mapMasks"] = new JArray(),
                        ["sceneClones"] = new JArray()
                    }
                },
                ["progression"] = new JObject
                {
                    ["sections"] = new JArray(),
                    ["progressions"] = new JObject(),
                    ["mapFeatures"] = new JObject()
                },
                ["audio"] = new JObject
                {
                    ["whistles"] = new JObject(),
                    ["horns"] = new JObject(),
                    ["bells"] = new JObject()
                },
                ["extensions"] = new JObject
                {
                    ["legacyData"] = new JObject
                    {
                        ["sourcePackageId"] = manifest.LegacyId,
                        ["sourceFragment"] = fragment,
                        ["convertedAtRuntime"] = true,
                        ["compatibilityTag"] = "legacy-converted",
                        ["supportStatus"] = "temporary"
                    }
                }
            };
        }

        internal static void ConvertSource(JObject source, JObject root, FuseLegacyPackageManifest manifest)
        {
            var tracks = source["tracks"] as JObject;
            if (tracks != null)
            {
                ConvertDictionary(tracks["nodes"], root["tracks"]["nodes"] as JObject, root["tracks"]["removals"]["nodes"] as JArray, ConvertNode);
                ConvertDictionary(tracks["segments"], root["tracks"]["segments"] as JObject, root["tracks"]["removals"]["segments"] as JArray, ConvertSegment);
                ConvertDictionary(tracks["spans"], root["tracks"]["spans"] as JObject, root["tracks"]["removals"]["spans"] as JArray, ConvertSpan);
            }

            ConvertDictionary(source["loads"], root["operations"]["loads"] as JObject, null, ConvertLoad);

            var areas = source["areas"] as JObject;
            if (areas != null)
            {
                foreach (var area in areas.Properties())
                {
                    if (!(area.Value is JObject areaObject))
                    {
                        continue;
                    }

                    (root["tracks"]["areas"] as JObject)[area.Name] = ConvertArea(area.Name, areaObject);
                    var industries = areaObject["industries"] as JObject;
                    if (industries == null)
                    {
                        continue;
                    }

                    foreach (var industry in industries.Properties())
                    {
                        if (industry.Value.Type == JTokenType.Null)
                        {
                            AddUniqueString(root["operations"]?["removals"]?["industries"] as JArray, industry.Name);
                            continue;
                        }

                        if (industry.Value is JObject industryObject)
                        {
                            (root["operations"]["industries"] as JObject)[industry.Name] =
                                ConvertIndustry(industry.Name, industryObject, area.Name);
                        }
                    }
                }
            }

            var topIndustries = source["industries"] as JObject;
            if (topIndustries != null)
            {
                foreach (var industry in topIndustries.Properties())
                {
                    if (industry.Value.Type == JTokenType.Null)
                    {
                        AddUniqueString(root["operations"]?["removals"]?["industries"] as JArray, industry.Name);
                        continue;
                    }

                    if (industry.Value is JObject industryObject)
                    {
                        ((JObject)root["operations"]["industries"])[industry.Name] =
                            ConvertIndustry(industry.Name, industryObject, null);
                    }
                }
            }

            var turntables = source["turntables"] as JObject;
            if (turntables != null)
            {
                foreach (var table in turntables.Properties().Where(p => p.Value is JObject))
                {
                    ((JObject)root["operations"]["turntables"])[table.Name] =
                        ConvertTurntable(table.Name, (JObject)table.Value);
                }
            }
            ConvertDictionary(source["scenery"], root["world"]["scenery"] as JObject, root["world"]["removals"]["scenery"] as JArray, ConvertScenery);
            ConvertSplineys(source["splineys"] as JObject, root);
            ConvertMandelas(source["mandelas"] as JObject, root);
            var texts = source["texts"] as JObject;
            if (texts != null)
            {
                foreach (var label in texts.Properties())
                {
                    if (label.Value.Type == JTokenType.Null)
                    {
                        ((JArray)root["world"]["removals"]["mapLabels"]).Add(label.Name);
                    }
                    else if (label.Value is JObject labelObject)
                    {
                        ((JObject)root["world"]["mapLabels"])[label.Name] = ConvertLabel(label.Name, labelObject);
                    }
                }
            }

            if (source["simpleGraphs"] != null)
            {
                ((JObject)root["extensions"])["simpleGraphs"] = source["simpleGraphs"].DeepClone();
            }

            var spawn = ConvertLegacyStart(source);
            if (spawn != null)
            {
                ((JArray)root["world"]["spawnPoints"]).Add(spawn);
            }

            ConvertProgression(source, root);
            ConvertGameMigrations(source, root);
        }

        // Carry the legacy "game-migrations" mixinto payload through to
        // <c>extensions.gameMigrations</c>. The legacy file format has two
        // top-level dictionaries:
        //   waybillDestinations: "<oldIndustry>.<oldLoadOrSlot>" ->
        //                        "<newIndustry>.<newLoadOrSlot>"
        //       — rewrite old waybill targets that no longer exist (the
        //         author renamed an industry or its slot).
        //   properties:          "<oldIndustry>" -> "<newIndustry>"
        //       — rewrite saved property-bag keys keyed by industry id.
        // The actual application of these renames to a loaded save runs in
        // <c>FuseGameMigrationApplier</c>; this method just captures the
        // data so the runtime can find it.
        private static void ConvertGameMigrations(JObject source, JObject root)
        {
            if (source == null || root == null)
            {
                return;
            }

            var waybillSource = source["waybillDestinations"] as JObject ??
                                source["WaybillDestinations"] as JObject;
            var propertiesSource = source["properties"] as JObject ??
                                   source["Properties"] as JObject;
            if (waybillSource == null && propertiesSource == null)
            {
                return;
            }

            var extensions = root["extensions"] as JObject;
            if (extensions == null)
            {
                extensions = new JObject();
                root["extensions"] = extensions;
            }

            if (!(extensions["gameMigrations"] is JObject migrations))
            {
                migrations = new JObject();
                extensions["gameMigrations"] = migrations;
            }

            if (waybillSource != null)
            {
                var waybillTarget = migrations["waybillDestinations"] as JObject ?? new JObject();
                foreach (var property in waybillSource.Properties())
                {
                    if (property.Value == null || property.Value.Type == JTokenType.Null)
                    {
                        continue;
                    }

                    var value = property.Value.ToString();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    waybillTarget[property.Name] = value.Trim();
                }

                migrations["waybillDestinations"] = waybillTarget;
            }

            if (propertiesSource != null)
            {
                var propertiesTarget = migrations["properties"] as JObject ?? new JObject();
                foreach (var property in propertiesSource.Properties())
                {
                    if (property.Value == null || property.Value.Type == JTokenType.Null)
                    {
                        continue;
                    }

                    var value = property.Value.ToString();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    propertiesTarget[property.Name] = value.Trim();
                }

                migrations["properties"] = propertiesTarget;
            }
        }
    }

    internal sealed class FuseLegacyPackageManifest
    {
        public string LegacyId { get; set; }
        public string PackageId { get; set; }
        public string DisplayName { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public string[] RequiredPackageIds { get; set; } = Array.Empty<string>();
        public string[] LoadAfter { get; set; } = Array.Empty<string>();
        public string[] SourceFiles { get; set; } = Array.Empty<string>();
        public FuseLegacyMapTileSource[] MapTileSources { get; set; } = Array.Empty<FuseLegacyMapTileSource>();
    }

    internal sealed class FuseLegacyMapTileSource
    {
        public string Id { get; set; }
        public string Directory { get; set; }
        public string SourceFolder { get; set; }
        public int Priority { get; set; }
    }

    internal readonly struct LegacyVector
    {
        public LegacyVector(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
    }
}
