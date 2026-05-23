using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FUSE.Data;
using FUSE.Infrastructure;
using FUSE.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FUSE.Loading
{
    internal static class FuseLegacyDataConverter
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
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE ignored legacy data package '{folderPath}' because Definition.json could not be parsed: {ex.Message}");
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
                    catch (Exception ex)
                    {
                        FuseLog.Warning($"FUSE skipped legacy map tile folder '{directory}' because it could not be enumerated: {ex.Message}");
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
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE skipped legacy data candidate '{path}' because it could not be parsed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Legacy audio file shapes the converter can route into the FUSE
        /// audio root. Classification is by filename keyword because the
        /// file contents are an unkeyed JSON array — there is no
        /// distinguishing top-level field to look at.
        /// </summary>
        private enum LegacyAudioKind
        {
            None,
            Whistles,
            Horns,
            Bells
        }

        /// <summary>
        /// Returns true when <paramref name="path"/> looks like a legacy
        /// Strange-Customs era audio pack (whistles.json / horns.json /
        /// bells.json or SC variants like myhorns.json,
        /// CollieQuillHorns.json, custom-bells.json). The legacy convention
        /// is a top-level JSON ARRAY of entries with no shared registry
        /// key, so we cannot detect them by content the way ConvertSource
        /// detects tracks / industries / progression — we have to fall
        /// back to filename keywords. Bells deliberately also matches
        /// "Bell" with a capital, since some packs ship CamelCase names.
        /// </summary>
        private static bool TryClassifyLegacyAudioFile(string path, out LegacyAudioKind kind)
        {
            kind = LegacyAudioKind.None;
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var stem = Path.GetFileNameWithoutExtension(fileName);
            // Use the case-insensitive substring match so files named
            // Whistles.json, myhorns.json, foo-bells.json, etc. all sort
            // into the right bucket. Order matters when more than one
            // keyword could match (e.g. a hypothetical
            // "horns-and-whistles.json"): the first match wins, and
            // whistles takes precedence over horns over bells because
            // that matches the most-common naming intent.
            if (stem.IndexOf("whistle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = LegacyAudioKind.Whistles;
                return true;
            }
            if (stem.IndexOf("horn", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = LegacyAudioKind.Horns;
                return true;
            }
            if (stem.IndexOf("bell", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = LegacyAudioKind.Bells;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reads a legacy audio pack file and inserts its entries into
        /// <paramref name="root"/>'s audio dictionary (whistles / horns /
        /// bells). Skips silently when the file is empty, malformed, or
        /// contains a value of an unexpected shape — those cases warn
        /// rather than throw so a single broken entry can't sink the
        /// surrounding package.
        ///
        /// The legacy entry shape is array-of-object with a top-level
        /// <c>name</c> and either <c>clip</c> (whistles), <c>layers</c>
        /// (horns), or <c>file</c>+<c>indexTimes</c> (bells). FUSE's
        /// audio root is dictionary-keyed by id, so we derive each id
        /// from the slug of the entry's name and disambiguate duplicates
        /// with a numeric suffix. The <c>clip</c> / <c>file</c> URI is
        /// passed through unchanged because <c>FuseAudioAPI.ResolveAudioPath</c>
        /// already accepts both modern <c>file://</c> URIs and the
        /// SC-era <c>file(filename.wav)</c> notation that these packs
        /// historically use.
        /// </summary>
        private static void ConvertLegacyAudioSource(string sourceFile, LegacyAudioKind kind, JObject root)
        {
            JArray entries;
            try
            {
                entries = ReadLegacyArray(sourceFile);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE skipped legacy audio source '{sourceFile}' because it could not be parsed as a JSON array: {ex.Message}");
                return;
            }

            var audio = root["audio"] as JObject;
            if (audio == null)
            {
                FuseLog.Warning(
                    $"FUSE could not insert legacy audio entries from '{sourceFile}' because the FUSE definition skeleton has no audio root.");
                return;
            }

            var bucketName = kind switch
            {
                LegacyAudioKind.Whistles => "whistles",
                LegacyAudioKind.Horns => "horns",
                LegacyAudioKind.Bells => "bells",
                _ => null
            };
            if (bucketName == null)
            {
                return;
            }

            var bucket = audio[bucketName] as JObject;
            if (bucket == null)
            {
                bucket = new JObject();
                audio[bucketName] = bucket;
            }

            var fileStem = Path.GetFileNameWithoutExtension(sourceFile) ?? "audio";
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var converted = 0;
            for (var index = 0; index < entries.Count; index++)
            {
                if (!(entries[index] is JObject entry))
                {
                    FuseLog.Warning(
                        $"FUSE skipped legacy audio entry #{index} in '{sourceFile}' because it is not a JSON object.");
                    continue;
                }

                var nameToken = entry["name"] ?? entry["Name"];
                var displayName = nameToken?.Type == JTokenType.String
                    ? nameToken.Value<string>()
                    : null;

                // CRITICAL: legacy whistle/horn/bell ids MUST match the
                // identifier shape that Strange-Customs (the old loader)
                // registers under, because existing save files store
                // <c>whistle.custom = "sc.&lt;name&gt;"</c> or
                // <c>horn.custom = "&lt;name&gt;"</c> as the per-loco
                // selection and the game does an exact-key lookup against
                // FuseAudioAPI.Whistles / .Horns / .Bells at configure
                // time. Slugifying the name (the previous behaviour here)
                // would make every legacy loco fall back to its default
                // whistle/horn on first load — the user-visible symptom
                // that prompted this change. The SC conventions, derived
                // from observing the save data, are:
                //   * whistles -> "sc." + raw name
                //   * horns    -> raw name (no prefix)
                //   * bells    -> raw name (no prefix); bells were
                //                 historically per-loco add-on dlls, but
                //                 we preserve the raw-name convention so
                //                 any save that does carry a bell id
                //                 keeps resolving.
                // Unnamed entries still need *some* id, so for those we
                // fall back to a "<fileStem>-<index>" placeholder.
                string idBase;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    idBase = $"{fileStem}-{index + 1}";
                }
                else if (kind == LegacyAudioKind.Whistles)
                {
                    idBase = "sc." + displayName;
                }
                else
                {
                    idBase = displayName;
                }

                var entryId = UniqueFragment(idBase, used);

                JObject converted_entry;
                switch (kind)
                {
                    case LegacyAudioKind.Whistles:
                        converted_entry = ConvertLegacyWhistleEntry(entry, displayName);
                        break;
                    case LegacyAudioKind.Horns:
                        converted_entry = ConvertLegacyHornEntry(entry, displayName);
                        break;
                    case LegacyAudioKind.Bells:
                        converted_entry = ConvertLegacyBellEntry(entry, displayName);
                        break;
                    default:
                        continue;
                }

                if (converted_entry == null)
                {
                    continue;
                }

                bucket[entryId] = converted_entry;
                converted++;
            }

            FuseLog.Info(
                $"FUSE converted legacy audio source '{sourceFile}' kind='{bucketName}' " +
                $"entries={entries.Count} convertedToFuse={converted}.");
        }

        private static JObject ConvertLegacyWhistleEntry(JObject entry, string name)
        {
            var fuseEntry = new JObject
            {
                ["name"] = name ?? string.Empty,
                ["clip"] = (string)(entry["clip"] ?? entry["Clip"]) ?? string.Empty
            };
            CopyAudioAssetReference(entry, fuseEntry);
            CopyOptionalNumber(entry, fuseEntry, "rampUpPitch", "RampUpPitch");
            CopyOptionalNumber(entry, fuseEntry, "lerpSpeed", "LerpSpeed");
            CopyOptionalNumber(entry, fuseEntry, "airLerpSpeed", "AirLerpSpeed");
            return fuseEntry;
        }

        private static JObject ConvertLegacyHornEntry(JObject entry, string name)
        {
            var layers = entry["layers"] ?? entry["Layers"];
            var fuseLayers = new JArray();
            if (layers is JArray layerArray)
            {
                foreach (var layerToken in layerArray)
                {
                    if (!(layerToken is JObject layer))
                    {
                        continue;
                    }
                    var convertedLayer = new JObject
                    {
                        ["file"] = (string)(layer["file"] ?? layer["File"]) ?? string.Empty
                    };
                    var keyframes = layer["keyframes"] ?? layer["Keyframes"];
                    if (keyframes is JArray keyframeArray)
                    {
                        var fuseKeyframes = new JArray();
                        foreach (var keyframeToken in keyframeArray)
                        {
                            if (!(keyframeToken is JObject keyframe))
                            {
                                continue;
                            }
                            var t = keyframe["t"] ?? keyframe["T"];
                            var value = keyframe["value"] ?? keyframe["Value"];
                            if (t == null || value == null)
                            {
                                continue;
                            }
                            fuseKeyframes.Add(new JObject
                            {
                                ["t"] = t.DeepClone(),
                                ["value"] = value.DeepClone()
                            });
                        }
                        if (fuseKeyframes.Count > 0)
                        {
                            convertedLayer["keyframes"] = fuseKeyframes;
                        }
                    }
                    fuseLayers.Add(convertedLayer);
                }
            }
            return new JObject
            {
                ["name"] = name ?? string.Empty,
                ["layers"] = fuseLayers
            };
        }

        private static JObject ConvertLegacyBellEntry(JObject entry, string name)
        {
            var fuseEntry = new JObject
            {
                ["name"] = name ?? string.Empty,
                ["file"] = (string)(entry["file"] ?? entry["File"]) ?? string.Empty
            };
            var indexTimes = entry["indexTimes"] ?? entry["IndexTimes"];
            if (indexTimes is JArray array)
            {
                fuseEntry["indexTimes"] = array.DeepClone();
            }
            return fuseEntry;
        }

        private static void CopyAudioAssetReference(JObject entry, JObject target)
        {
            var model = entry["model"] ?? entry["Model"];
            if (!(model is JObject modelObject))
            {
                return;
            }
            var reference = new JObject();
            var packId = modelObject["assetPackIdentifier"] ?? modelObject["AssetPackIdentifier"];
            var assetId = modelObject["assetIdentifier"] ?? modelObject["AssetIdentifier"];
            if (packId != null)
            {
                reference["assetPackIdentifier"] = packId.DeepClone();
            }
            if (assetId != null)
            {
                reference["assetIdentifier"] = assetId.DeepClone();
            }
            if (reference.HasValues)
            {
                target["model"] = reference;
            }
        }

        private static void CopyOptionalNumber(JObject entry, JObject target, string fuseProperty, string legacyProperty)
        {
            var token = entry[fuseProperty] ?? entry[legacyProperty];
            if (token == null || token.Type == JTokenType.Null)
            {
                return;
            }
            target[fuseProperty] = token.DeepClone();
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

        private static void ConvertDictionary(JToken source, JObject target, JArray removals, Func<JToken, JToken> converter)
        {
            ConvertDictionary(source, target, removals, (_, token) => converter(token));
        }

        private static void ConvertDictionary(JToken source, JObject target, JArray removals, Func<string, JToken, JToken> converter)
        {
            if (!(source is JObject obj) || target == null)
            {
                return;
            }

            foreach (var property in obj.Properties())
            {
                if (property.Value.Type == JTokenType.Null)
                {
                    removals?.Add(property.Name);
                    continue;
                }

                var converted = converter(property.Name, property.Value);
                if (converted != null)
                {
                    target[property.Name] = Clean(converted);
                }
            }
        }

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
                ["order"] = Clone(item["order"]),
                ["spanIds"] = ToStringArray(item["spanIds"] ?? item["spans"]),
                ["groupId"] = ReadString(item, "groupId", "GroupId")
            });
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

            return CleanObject(new JObject
            {
                ["name"] = ReadString(item, "name") ?? id,
                ["areaId"] = areaId ?? ReadString(item, "areaId", "area"),
                ["order"] = Clone(item["order"]),
                ["position"] = Vector(item["localPosition"] ?? item["position"], false),
                ["rotation"] = Vector(item["localRotation"] ?? item["rotation"], false),
                ["usesContract"] = ReadBool(item, "usesContract", false),
                ["mergeComponents"] = !isReplace,
                ["replaceComponents"] = isReplace,
                ["components"] = components
            });
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

        private static JObject ConvertComponent(string id, JObject item, ComponentTypeInferenceContext inferenceContext)
        {
            var explicitType = ReadString(item, "type", "Type");
            var trackSpanToken = item["trackSpanIds"] ?? item["trackSpans"] ?? item["spans"];
            var trackSpanPatch = ToStringListPatch(trackSpanToken);
            var isPartial = ShouldConvertAsPartialComponent(item, explicitType, trackSpanPatch);
            var type = isPartial ? null : NormalizeComponentType(explicitType ?? InferComponentType(id, item, inferenceContext));
            var normalizedType = isPartial ? null : FuseIndustryComponentTypes.Normalize(type);
            var isPassengerStop = string.Equals(normalizedType, FuseIndustryComponentTypes.PassengerStop, StringComparison.OrdinalIgnoreCase);
            var explicitLoadId = ReadString(item, "loadId", "LoadId", "load");
            var result = new JObject();
            if (isPartial)
            {
                result["partial"] = true;
            }
            else
            {
                result["type"] = type;
                result["name"] = ReadString(item, "name") ?? id;
            }

            if (isPartial && !string.IsNullOrWhiteSpace(ReadString(item, "name")))
            {
                result["name"] = ReadString(item, "name");
            }

            result["trackSpanIds"] = trackSpanPatch != null ? ToStringArrayFromPatch(trackSpanPatch) : ToStringArray(trackSpanToken);
            result["trackSpanPatch"] = trackSpanPatch;
            result["carTypeFilter"] = Clone(item["carTypeFilter"]);
            result["loadId"] = explicitLoadId ?? (isPartial ? null : (isPassengerStop ? "passengers" : InferLoadIdFromComponentId(id, normalizedType, item)));
            result["convertedLoadId"] = ReadString(item, "convertedLoadId", "convertedLoad");
            result["sharedStorage"] = isPartial ? null : Clone(item["sharedStorage"]);
            result["storageChangeRate"] = Clone(item["storageChangeRate"]);
            result["maxStorage"] = Clone(item["maxStorage"]);
            result["carTransferRate"] = Clone(item["carTransferRate"]);
            result["costPerUnit"] = Clone(item["costPerUnit"]);
            result["notBeforeHour"] = Clone(item["notBeforeHour"]);
            result["notAfterHour"] = Clone(item["notAfterHour"]);
            result["fillPercentage"] = Clone(item["fillPercentage"]);
            result["bookReasons"] = ToStringArray(item["bookReasons"]);
            result["title"] = ReadString(item, "title");
            result["orderAroundEmpties"] = Clone(item["orderAroundEmpties"]);
            result["orderAroundLoaded"] = Clone(item["orderAroundLoaded"]);
            result["inputSpanIds"] = ToStringArray(item["inputSpanIds"] ?? item["inputSpans"]);
            result["outputSpanIds"] = ToStringArray(item["outputSpanIds"] ?? item["outputSpans"]);
            result["inputTermsPerDay"] = Clone(item["inputTermsPerDay"]);
            result["outputTermsPerDay"] = Clone(item["outputTermsPerDay"]);
            result["idealCars"] = Clone(item["idealCars"]);
            result["teamProfiles"] = Clone(item["teamProfiles"]);
            result["canOverhaul"] = Clone(item["canOverhaul"]);
            result["passengerStopId"] = ReadString(item, "passengerStopId", "passengerStop") ?? (isPassengerStop ? id : null);
            result["timetableCode"] = ReadString(item, "timetableCode");
            result["basePopulation"] = Clone(item["basePopulation"]);
            result["neighborIds"] = ToStringArray(item["neighborIds"] ?? item["neighbors"]);
            result["branch"] = ReadString(item, "branch");
            result["fields"] = ConvertCustomComponentFields(type, item);

            return CleanObject(result);
        }

        private sealed class ComponentTypeInferenceContext
        {
            private readonly HashSet<string> inputLoadIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            private readonly HashSet<string> outputLoadIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public void AddInputLoad(string loadId)
            {
                AddLoadId(inputLoadIds, loadId);
            }

            public void AddOutputLoad(string loadId)
            {
                AddLoadId(outputLoadIds, loadId);
            }

            public bool IsInputOnly(string loadId)
            {
                return !string.IsNullOrWhiteSpace(loadId) &&
                       inputLoadIds.Contains(loadId.Trim()) &&
                       !outputLoadIds.Contains(loadId.Trim());
            }

            public bool IsOutputOnly(string loadId)
            {
                return !string.IsNullOrWhiteSpace(loadId) &&
                       outputLoadIds.Contains(loadId.Trim()) &&
                       !inputLoadIds.Contains(loadId.Trim());
            }

            private static void AddLoadId(ISet<string> sink, string loadId)
            {
                if (!string.IsNullOrWhiteSpace(loadId))
                {
                    sink.Add(loadId.Trim());
                }
            }
        }

        private static bool ShouldConvertAsPartialComponent(JObject item, string explicitType, JObject trackSpanPatch)
        {
            if (item == null || !string.IsNullOrWhiteSpace(explicitType))
            {
                return false;
            }

            if (trackSpanPatch != null && trackSpanPatch.HasValues)
            {
                return true;
            }

            if (!HasStandaloneComponentShape(item))
            {
                return true;
            }

            return HasLoadOperationShape(item) && !HasLoadComponentBindingShape(item);
        }

        private static bool HasStandaloneComponentShape(JObject item)
        {
            if (item == null)
            {
                return false;
            }

            return item["inputTermsPerDay"] != null ||
                   item["outputTermsPerDay"] != null ||
                   item["teamProfiles"] != null ||
                   item["passengerStopId"] != null ||
                   item["passengerStop"] != null ||
                   item["timetableCode"] != null ||
                   item["basePopulation"] != null ||
                   item["canOverhaul"] != null ||
                   HasLoadOperationShape(item);
        }

        private static bool HasLoadComponentBindingShape(JObject item)
        {
            if (item == null)
            {
                return false;
            }

            return item["trackSpanIds"] != null ||
                   item["trackSpans"] != null ||
                   item["spans"] != null ||
                   item["loadId"] != null ||
                   item["LoadId"] != null ||
                   item["load"] != null;
        }

        private static bool HasLoadOperationShape(JObject item)
        {
            if (item == null)
            {
                return false;
            }

            return item["loadId"] != null ||
                   item["LoadId"] != null ||
                   item["load"] != null ||
                   item["convertedLoadId"] != null ||
                   item["convertedLoad"] != null ||
                   item["maxStorage"] != null ||
                   item["MaxStorage"] != null ||
                   item["storageChangeRate"] != null ||
                   item["StorageChangeRate"] != null ||
                   item["carTransferRate"] != null ||
                   item["CarTransferRate"] != null ||
                   item["costPerUnit"] != null ||
                   item["notBeforeHour"] != null ||
                   item["notAfterHour"] != null ||
                   item["fillPercentage"] != null ||
                   item["bookReasons"] != null ||
                   item["title"] != null ||
                   item["orderAroundEmpties"] != null ||
                   item["orderAroundLoaded"] != null;
        }

        private static string InferComponentType(string id, JObject item, ComponentTypeInferenceContext inferenceContext)
        {
            var normalizedIdType = NormalizeComponentType(id);
            if (!string.IsNullOrWhiteSpace(normalizedIdType) &&
                FuseIndustryComponentTypes.IsKnown(normalizedIdType))
            {
                return normalizedIdType;
            }

            if (item != null)
            {
                if (item["inputTermsPerDay"] != null || item["outputTermsPerDay"] != null)
                {
                    return FuseIndustryComponentTypes.Formulaic;
                }

                if (item["teamProfiles"] != null)
                {
                    return FuseIndustryComponentTypes.TeamTrack;
                }

                if (item["passengerStopId"] != null || item["passengerStop"] != null || item["timetableCode"] != null || item["basePopulation"] != null)
                {
                    return FuseIndustryComponentTypes.PassengerStop;
                }

                if (item["canOverhaul"] != null)
                {
                    return FuseIndustryComponentTypes.RepairTrack;
                }
            }

            if (inferenceContext != null)
            {
                if (inferenceContext.IsInputOnly(id))
                {
                    return FuseIndustryComponentTypes.Unloader;
                }

                if (inferenceContext.IsOutputOnly(id))
                {
                    return FuseIndustryComponentTypes.Loader;
                }
            }

            return FuseIndustryComponentTypes.Loader;
        }

        private static string InferLoadIdFromComponentId(string id, string normalizedType, JObject item)
        {
            if (string.IsNullOrWhiteSpace(id) ||
                !FuseIndustryComponentTypes.UsesLoadId(normalizedType) ||
                string.Equals(normalizedType, FuseIndustryComponentTypes.PassengerStop, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedType, FuseIndustryComponentTypes.RepairTrack, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (item == null ||
                (item["maxStorage"] == null &&
                 item["storageChangeRate"] == null &&
                 item["carTransferRate"] == null &&
                 item["costPerUnit"] == null &&
                 item["orderAroundEmpties"] == null &&
                 item["orderAroundLoaded"] == null))
            {
                return null;
            }

            return id.Trim();
        }

        private static JObject ConvertComponents(JObject sourceComponents)
        {
            var components = new JObject();
            if (sourceComponents == null)
            {
                return components;
            }

            var inferenceContext = BuildComponentTypeInferenceContext(sourceComponents);
            foreach (var component in sourceComponents.Properties())
            {
                // Legacy SC convention: setting a component's value to null
                // requests deletion of the matching runtime sub-component.
                // The old <c>Where(p => p.Value is JObject)</c> filter
                // discarded these sentinels before the apply path could see
                // them — so removal mods such as CollieSylvaRemoval (which
                // nulls every component on sylva-tannery / sylva-paperboard /
                // sylva-interchange to delete them) became silent no-ops and
                // the vanilla components lingered in the industry list.
                // We can't pass a JSON <c>null</c> straight through because
                // <see cref="FUSE.Serialization.FuseSerializer.GetSettings"/>
                // sets <c>NullValueHandling.Ignore</c>, which would drop the
                // entry during deserialization. Convert the null into an
                // explicit <c>{ "remove": true }</c> sentinel that the
                // <see cref="FUSE.Data.Operations.FuseIndustryComponent.Remove"/>
                // flag picks up at apply time.
                if (component.Value == null || component.Value.Type == JTokenType.Null)
                {
                    components[component.Name] = new JObject { ["remove"] = true };
                    continue;
                }

                if (!(component.Value is JObject obj))
                {
                    continue;
                }

                if (IsLegacyDirectiveKey(component.Name))
                {
                    ConvertDirectiveComponents(obj, components, inferenceContext);
                    continue;
                }

                AddConvertedComponent(components, component.Name, obj, inferenceContext);
            }

            return components;
        }

        private static ComponentTypeInferenceContext BuildComponentTypeInferenceContext(JObject sourceComponents)
        {
            var context = new ComponentTypeInferenceContext();
            CollectComponentTypeInferenceTerms(sourceComponents, context);
            return context;
        }

        private static void CollectComponentTypeInferenceTerms(JObject sourceComponents, ComponentTypeInferenceContext context)
        {
            if (sourceComponents == null || context == null)
            {
                return;
            }

            foreach (var component in sourceComponents.Properties().Where(p => p.Value is JObject))
            {
                var item = (JObject)component.Value;
                if (IsLegacyDirectiveKey(component.Name))
                {
                    CollectComponentTypeInferenceTerms(item, context);
                    continue;
                }

                AddFormulaLoadTermIds(item["inputTermsPerDay"], context.AddInputLoad);
                AddFormulaLoadTermIds(item["outputTermsPerDay"], context.AddOutputLoad);
            }
        }

        private static void AddFormulaLoadTermIds(JToken terms, Action<string> add)
        {
            var obj = terms as JObject;
            if (obj == null || add == null)
            {
                return;
            }

            foreach (var term in obj.Properties())
            {
                add(term.Name);
            }
        }

        private static void ConvertDirectiveComponents(JObject directive, JObject components, ComponentTypeInferenceContext inferenceContext)
        {
            if (directive == null || components == null)
            {
                return;
            }

            foreach (var child in directive.Properties().Where(p => p.Value is JObject))
            {
                if (IsLegacyDirectiveKey(child.Name))
                {
                    ConvertDirectiveComponents((JObject)child.Value, components, inferenceContext);
                    continue;
                }

                AddConvertedComponent(components, child.Name, (JObject)child.Value, inferenceContext);
            }
        }

        private static void AddConvertedComponent(JObject components, string id, JObject item, ComponentTypeInferenceContext inferenceContext)
        {
            if (components == null || item == null)
            {
                return;
            }

            var componentId = UniqueObjectKey(
                string.IsNullOrWhiteSpace(id) ? "component" : id,
                components);
            components[componentId] = ConvertComponent(componentId, item, inferenceContext);
        }

        private static string NormalizeComponentType(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "load":
                case "loader":
                    return "loader";
                case "unload":
                case "unloader":
                    return "unloader";
                case "formula":
                case "formulaic":
                    return "formulaic";
                case "repair":
                case "repairtrack":
                    return "repairTrack";
                case "teamtrack":
                case "team_track":
                    return "teamTrack";
                case "interchange":
                    return "interchange";
                case "passengerstop":
                case "passenger_stop":
                    return "passengerStop";
                default:
                    return string.IsNullOrWhiteSpace(value) ? "loader" : value.Trim();
            }
        }

        private static bool IsLegacyDirectiveKey(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.TrimStart().StartsWith("$", StringComparison.Ordinal);
        }

        private static bool IsTurntableHandler(string handler)
        {
            return !string.IsNullOrWhiteSpace(handler) && TurntableHandlers.Contains(handler.Trim());
        }

        private static bool IsMapLabelHandler(string handler)
        {
            return !string.IsNullOrWhiteSpace(handler) &&
                   (string.Equals(handler.Trim(), MapLabelHandler, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(handler.Trim(), "AlinasMapMod.MapLabelBuilder", StringComparison.OrdinalIgnoreCase));
        }

        private static JObject ConvertCustomComponentFields(string type, JObject item)
        {
            var fields = item["fields"] is JObject fieldObject
                ? (JObject)fieldObject.DeepClone()
                : new JObject();
            if (FuseIndustryComponentTypes.IsKnown(type))
            {
                return fields.HasValues ? fields : null;
            }

            AddCustomComponentFields(fields, item["extraData"] as JObject ?? item["ExtraData"] as JObject);
            AddCustomComponentFields(fields, item);
            return fields.HasValues ? fields : null;
        }

        private static void AddCustomComponentFields(JObject fields, JObject source)
        {
            if (fields == null || source == null)
            {
                return;
            }

            foreach (var property in source.Properties())
            {
                if (property.Value == null ||
                    property.Value.Type == JTokenType.Null ||
                    ComponentSchemaKeys.Contains(property.Name) ||
                    IsLegacyDirectiveKey(property.Name) ||
                    fields[property.Name] != null)
                {
                    continue;
                }

                fields[property.Name] = property.Value.DeepClone();
            }
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
                         (item["points"] as JArray) == null)
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
                else if ((item["points"] as JArray)?.Count >= 2)
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

        private static JToken ConvertSpliney(JObject item)
        {
            var handler = ReadHandler(item);
            var offsetY = item["offsetY"] ?? item["offsety"];
            if (offsetY == null && string.Equals(handler, "StrangeCustoms.FlowyThingBuilder", StringComparison.OrdinalIgnoreCase))
            {
                offsetY = new JValue(-0.1f);
            }

            var points = new JArray();
            foreach (var point in (item["points"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
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

        private static JToken ConvertLoader(JObject item)
        {
            return CleanObject(new JObject
            {
                ["position"] = Vector(item["position"] ?? item["Position"] ?? item["localPosition"] ?? item["LocalPosition"], false),
                ["rotation"] = Vector(item["rotation"] ?? item["Rotation"] ?? item["localRotation"] ?? item["LocalRotation"], false),
                ["prefab"] = ReadString(item, "prefab", "Prefab") ?? "empty://",
                ["industryId"] = ReadString(item, "industry", "Industry")
            });
        }

        private static JToken ConvertStation(JObject item)
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

        private static JToken ConvertSceneClone(string id, JToken token)
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

        private static JToken ConvertLabel(string id, JObject item)
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

        private static void ConvertProgression(JObject source, JObject root)
        {
            var progression = source["progression"] as JObject;
            if (progression != null)
            {
                var progressionId = ReadString(progression, "progressionId");
                if (!string.IsNullOrWhiteSpace(progressionId))
                {
                    root["progression"]["progressionId"] = progressionId;
                }

                if (progression["sections"] is JArray sections)
                {
                    foreach (var section in sections)
                    {
                        ((JArray)root["progression"]["sections"]).Add(NormalizeProgressionValue(section));
                    }
                }

                MergeProgressionDictionary(progression["progressions"], root["progression"]["progressions"] as JObject);
                MergeMapFeatureDictionary(progression["mapFeatures"], root["progression"]["mapFeatures"] as JObject);
            }

            MergeProgressionDictionary(source["progressions"], root["progression"]["progressions"] as JObject);
            MergeMapFeatureDictionary(source["mapFeatures"], root["progression"]["mapFeatures"] as JObject);
        }

        private static void MergeProgressionDictionary(JToken source, JObject target)
        {
            if (!(source is JObject obj) || target == null)
            {
                return;
            }

            foreach (var property in obj.Properties())
            {
                target[property.Name] = NormalizeProgressionValue(property.Value);
            }
        }

        private static void MergeMapFeatureDictionary(JToken source, JObject target)
        {
            if (!(source is JObject obj) || target == null)
            {
                return;
            }

            foreach (var property in obj.Properties())
            {
                var value = NormalizeProgressionValue(property.Value);
                if (value is JObject mapFeature &&
                    string.IsNullOrWhiteSpace(ReadString(mapFeature, "displayName")))
                {
                    mapFeature["displayName"] = property.Name;
                }

                target[property.Name] = value;
            }
        }

        private static JToken NormalizeProgressionValue(JToken value)
        {
            if (value is JArray array)
            {
                return new JArray(array.Select(NormalizeProgressionValue));
            }

            if (!(value is JObject obj))
            {
                return value?.DeepClone();
            }

            var result = new JObject();
            foreach (var property in obj.Properties())
            {
                var targetKey = NormalizeProgressionKey(property.Name);
                if (string.Equals(targetKey, "direction", StringComparison.OrdinalIgnoreCase))
                {
                    result[targetKey] = NormalizeDeliveryDirection(property.Value);
                }
                else if (IsBooleanDictionaryArrayField(targetKey) && property.Value is JObject boolDict)
                {
                    result[targetKey] = BoolDictionaryToArray(boolDict);
                }
                else
                {
                    result[targetKey] = NormalizeProgressionValue(property.Value);
                }
            }

            return CleanObject(result);
        }

        private static string NormalizeProgressionKey(string key)
        {
            var lower = (key ?? string.Empty).ToLowerInvariant();
            switch (lower)
            {
                case "displayname":
                    return "displayName";
                case "name":
                    return "name";
                case "defaultenableinsandbox":
                    return "initiallyEnabled";
                case "prerequisites":
                    return "prerequisiteFeatureIds";
                case "industrycomponent":
                    return "industryComponentId";
                case "load":
                    return "loadId";
                default:
                    return key;
            }
        }

        private static JToken NormalizeDeliveryDirection(JToken value)
        {
            var text = value == null ? string.Empty : value.ToString().Trim().ToLowerInvariant();
            if (text == "0" || text == "loadtoindustry" || text == "toindustry" || text == "to" || text == "import")
            {
                return "loadToIndustry";
            }

            if (text == "1" || text == "loadfromindustry" || text == "fromindustry" || text == "from" || text == "export")
            {
                return "loadFromIndustry";
            }

            return value?.DeepClone();
        }

        private static bool IsBooleanDictionaryArrayField(string key)
        {
            switch (key)
            {
                case "prerequisiteFeatureIds":
                case "prerequisiteSections":
                case "prerequisiteSectionIds":
                case "enableFeaturesOnUnlock":
                case "disableFeaturesOnUnlock":
                case "enableFeaturesOnAvailable":
                case "unlockIncludeIndustries":
                case "unlockExcludeIndustries":
                case "unlockIncludeIndustryComponents":
                case "areasEnableOnUnlock":
                case "gameObjectsEnableOnUnlock":
                case "trackGroupsEnableOnUnlock":
                case "trackGroupsAvailableOnUnlock":
                    return true;
                default:
                    return false;
            }
        }

        private static JArray BoolDictionaryToArray(JObject value)
        {
            var result = new JArray();
            foreach (var property in value.Properties())
            {
                if (property.Value.Type == JTokenType.Boolean && !property.Value.Value<bool>())
                {
                    continue;
                }

                if (property.Value.Type != JTokenType.Null && !string.IsNullOrWhiteSpace(property.Name))
                {
                    result.Add(property.Name);
                }
            }

            return result;
        }

        private static JObject Vector(JToken value, bool defaultScale)
        {
            var fallback = defaultScale ? 1f : 0f;
            if (value is JArray array)
            {
                return new JObject
                {
                    ["x"] = ReadFloat(array.Count > 0 ? array[0] : null, fallback),
                    ["y"] = ReadFloat(array.Count > 1 ? array[1] : null, fallback),
                    ["z"] = ReadFloat(array.Count > 2 ? array[2] : null, fallback)
                };
            }

            if (value is JObject obj)
            {
                return new JObject
                {
                    ["x"] = ReadFloat(obj["x"], fallback),
                    ["y"] = ReadFloat(obj["y"], fallback),
                    ["z"] = ReadFloat(obj["z"], fallback)
                };
            }

            return new JObject
            {
                ["x"] = fallback,
                ["y"] = fallback,
                ["z"] = fallback
            };
        }

        private static LegacyVector ReadVector(JToken value, bool defaultScale)
        {
            var vector = Vector(value, defaultScale);
            return new LegacyVector(
                ReadFloat(vector["x"], defaultScale ? 1f : 0f),
                ReadFloat(vector["y"], defaultScale ? 1f : 0f),
                ReadFloat(vector["z"], defaultScale ? 1f : 0f));
        }

        private static JObject Vector(LegacyVector value)
        {
            return new JObject
            {
                ["x"] = value.X,
                ["y"] = value.Y,
                ["z"] = value.Z
            };
        }

        private static JObject ToStringListPatch(JToken value)
        {
            var result = new JObject();
            CollectStringListPatch(value, result);
            return result.HasValues ? result : null;
        }

        private static void CollectStringListPatch(JToken value, JObject result)
        {
            if (value == null || result == null || value.Type == JTokenType.Null)
            {
                return;
            }

            if (value is JArray array)
            {
                foreach (var item in array)
                {
                    CollectStringListPatch(item, result);
                }

                return;
            }

            if (!(value is JObject obj))
            {
                return;
            }

            if (TryCollectStringListFindPatch(obj, result))
            {
                return;
            }

            foreach (var property in obj.Properties())
            {
                var operation = NormalizeStringListPatchOperation(property.Name);
                if (string.IsNullOrWhiteSpace(operation))
                {
                    continue;
                }

                AddStringListPatchValues(result, operation, property.Value);
            }
        }

        private static bool TryCollectStringListFindPatch(JObject obj, JObject result)
        {
            if (obj == null || result == null || !TryGetDirective(obj, "$find", out var findToken))
            {
                return false;
            }

            var findValues = ReadStringListFindValues(findToken).ToArray();
            if (findValues.Length == 0)
            {
                return true;
            }

            if (TryGetDirective(obj, "$remove", out var removeToken) ||
                TryGetDirective(obj, "$delete", out removeToken))
            {
                if (IsTruthyDirective(removeToken))
                {
                    AddStringListPatchValues(result, "remove", new JArray(findValues));
                }

                return true;
            }

            if (TryGetDirective(obj, "$replace", out var replacement))
            {
                AddStringListPatchValues(result, "remove", new JArray(findValues));
                AddStringListPatchValues(result, "append", replacement);
                return true;
            }

            return true;
        }

        private static IEnumerable<string> ReadStringListFindValues(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                yield break;
            }

            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    foreach (var value in ReadStringListFindValues(item))
                    {
                        yield return value;
                    }
                }

                yield break;
            }

            if (token is JObject obj)
            {
                var value = ReadString(obj, "value", "Value", "id", "Id");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value.Trim();
                }

                yield break;
            }

            var scalar = token.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(scalar))
            {
                yield return scalar;
            }
        }

        private static bool TryGetDirective(JObject obj, string name, out JToken value)
        {
            value = null;
            return obj != null &&
                   obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out value);
        }

        private static bool IsTruthyDirective(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                return false;
            }

            if (value.Type == JTokenType.Boolean)
            {
                return value.Value<bool>();
            }

            return true;
        }

        private static string NormalizeStringListPatchOperation(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "$add":
                case "add":
                case "$append":
                case "append":
                    return "append";
                case "$insert":
                case "insert":
                    return "insert";
                case "$prepend":
                case "prepend":
                    return "prepend";
                case "$replace":
                case "replace":
                    return "replace";
                case "$remove":
                case "remove":
                case "$delete":
                case "delete":
                    return "remove";
                default:
                    return null;
            }
        }

        private static void AddStringListPatchValues(JObject result, string operation, JToken value)
        {
            var target = result[operation] as JArray;
            if (target == null)
            {
                target = new JArray();
                result[operation] = target;
            }

            foreach (var item in ReadStringArray(value))
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    target.Add(item);
                }
            }
        }

        private static JArray ToStringArrayFromPatch(JObject patch)
        {
            var result = new JArray();
            if (patch == null)
            {
                return result;
            }

            var replace = patch["replace"] as JArray;
            if (replace != null)
            {
                AddStringArrayItems(result, replace);
                return result;
            }

            AddStringArrayItems(result, patch["prepend"] as JArray);
            AddStringArrayItems(result, patch["add"] as JArray);
            AddStringArrayItems(result, patch["append"] as JArray);
            AddStringArrayItems(result, patch["insert"] as JArray);
            return result;
        }

        private static void AddStringArrayItems(JArray target, JArray source)
        {
            if (target == null || source == null)
            {
                return;
            }

            foreach (var item in source.Values<string>())
            {
                if (!string.IsNullOrWhiteSpace(item) &&
                    !target.Values<string>().Any(existing => string.Equals(existing, item, StringComparison.OrdinalIgnoreCase)))
                {
                    target.Add(item);
                }
            }
        }

        private static JArray ToStringArray(JToken value)
        {
            var result = new JArray();
            foreach (var item in ReadStringArray(value))
            {
                result.Add(item);
            }

            return result;
        }

        private static IEnumerable<string> ReadStringArray(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                yield break;
            }

            if (value is JArray array)
            {
                foreach (var item in array)
                {
                    if (item?.Type == JTokenType.String)
                    {
                        var text = item.Value<string>()?.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            yield return text;
                        }

                        continue;
                    }

                    foreach (var text in ReadStringArray(item))
                    {
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            yield return text;
                        }
                    }
                }

                yield break;
            }

            if (value is JObject obj)
            {
                var scalarId = ReadString(obj, "id", "Id");
                if (!string.IsNullOrWhiteSpace(scalarId))
                {
                    yield return scalarId.Trim();
                    yield break;
                }

                var directiveProperties = obj.Properties()
                    .Where(property => IsStringArrayDirectiveKey(property.Name))
                    .ToArray();
                if (directiveProperties.Length > 0)
                {
                    foreach (var directive in directiveProperties)
                    {
                        foreach (var item in ReadStringArray(directive.Value))
                        {
                            if (!string.IsNullOrWhiteSpace(item))
                            {
                                yield return item;
                            }
                        }
                    }

                    yield break;
                }

                foreach (var property in obj.Properties())
                {
                    if (property.Value.Type == JTokenType.Boolean && !property.Value.Value<bool>())
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(property.Name))
                    {
                        yield return property.Name;
                    }
                }

                yield break;
            }

            var scalar = value.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(scalar))
            {
                yield return scalar;
            }
        }

        private static bool IsStringArrayDirectiveKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "$add":
                case "$append":
                case "$prepend":
                case "$insert":
                case "$replace":
                case "$remove":
                case "$delete":
                    return true;
                default:
                    return false;
            }
        }

        internal static JObject ReadLegacyObject(string path)
        {
            return JObject.Parse(ReadLegacyJsonText(path));
        }

        // Counterpart for legacy sources whose top-level token is a JSON
        // array (whistles.json / horns.json / bells.json / myhorns.json
        // and so on are top-level arrays in the Strange-Customs era
        // audio-pack convention). Routing those files through
        // ReadLegacyObject would throw "Current JsonReader item is not
        // an object: StartArray" and the loader would silently drop the
        // file, leaving every horn/whistle/bell entry unregistered.
        internal static JArray ReadLegacyArray(string path)
        {
            return JArray.Parse(ReadLegacyJsonText(path));
        }

        private static string ReadLegacyJsonText(string path)
        {
            var text = File.ReadAllText(path);
            text = StripJsonControlCharacters(text, path);
            text = StripJsonComments(text);
            text = RemoveTrailingCommas(text);
            text = CloseUnbalancedJson(text);
            text = RemoveTrailingCommas(text);
            return text;
        }

        // Drop bare ASCII control characters (other than tab/LF/CR) that
        // occasionally slip into hand-edited legacy mod manifests — e.g. a
        // paste from a terminal that included an embedded SYN, or a
        // Ctrl+V Ctrl+V verbatim-insert slip in vim. Newtonsoft rejects
        // those when they show up inside a string, which would otherwise
        // sink the entire legacy mod.
        //
        // This leniency is INTENTIONALLY scoped to the legacy pipeline.
        // Native FUSE definitions (*.fuse.json) load through
        // <see cref="FUSE.Serialization.FuseSerializer.FromJson"/>, which
        // hands the text straight to a strict JsonConvert and surfaces
        // Newtonsoft's parser error with line/column. Authors of new
        // FUSE addons are expected to format their JSON correctly; we
        // only smooth over breakage in the legacy ecosystem so that
        // years-old packages keep loading.
        //
        // When we do strip anything, log a loud warning that names the
        // file, the count, and the first offending byte+offset so the
        // author can find and fix it. Silently swallowing the byte
        // would let the authoring bug propagate forever.
        private static string StripJsonControlCharacters(string text, string path)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            System.Text.StringBuilder builder = null;
            var stripped = 0;
            var firstOffset = -1;
            var firstByte = (char)0;
            for (var index = 0; index < text.Length; index++)
            {
                var current = text[index];
                if (current >= 0x20 || current == '\t' || current == '\n' || current == '\r')
                {
                    builder?.Append(current);
                    continue;
                }

                if (builder == null)
                {
                    builder = new System.Text.StringBuilder(text.Length);
                    builder.Append(text, 0, index);
                }

                if (stripped == 0)
                {
                    firstOffset = index;
                    firstByte = current;
                }

                stripped++;
            }

            if (stripped > 0)
            {
                // Convert the offset into a 1-based line:column so the
                // author can find it in their editor without counting
                // bytes.
                var line = 1;
                var column = 1;
                for (var i = 0; i < firstOffset && i < text.Length; i++)
                {
                    if (text[i] == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else if (text[i] != '\r')
                    {
                        column++;
                    }
                }

                FuseLog.Warning(
                    $"FUSE stripped {stripped} stray control byte(s) from legacy file '{path}' " +
                    $"before parsing (first occurrence: 0x{(int)firstByte:X2} at line {line}, column {column}). " +
                    "This is almost always an editor accident (e.g. a Ctrl+V verbatim insert) and the " +
                    "mod author should fix the source file. FUSE only tolerates this on the legacy " +
                    "pipeline; native FUSE addons (*.fuse.json) must be valid JSON.");
            }

            return builder == null ? text : builder.ToString();
        }

        private static string StripJsonComments(string text)
        {
            var output = new System.Text.StringBuilder();
            var inString = false;
            var escaped = false;
            for (var index = 0; index < (text ?? string.Empty).Length; index++)
            {
                var current = text[index];
                if (inString)
                {
                    output.Append(current);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    output.Append(current);
                    continue;
                }

                if (current == '/' && index + 1 < text.Length && text[index + 1] == '/')
                {
                    index += 2;
                    while (index < text.Length && text[index] != '\r' && text[index] != '\n')
                    {
                        index++;
                    }

                    if (index < text.Length)
                    {
                        output.Append(text[index]);
                    }

                    continue;
                }

                if (current == '/' && index + 1 < text.Length && text[index + 1] == '*')
                {
                    index += 2;
                    while (index + 1 < text.Length && !(text[index] == '*' && text[index + 1] == '/'))
                    {
                        index++;
                    }

                    index = Math.Min(index + 1, text.Length - 1);
                    continue;
                }

                output.Append(current);
            }

            return output.ToString();
        }

        private static string RemoveTrailingCommas(string text)
        {
            string previous;
            var current = text ?? string.Empty;
            do
            {
                previous = current;
                current = Regex.Replace(current, @",\s*([}\]])", "$1");
            }
            while (!string.Equals(previous, current, StringComparison.Ordinal));
            return current;
        }

        private static string CloseUnbalancedJson(string text)
        {
            var stack = new Stack<char>();
            var inString = false;
            var escaped = false;
            foreach (var current in text ?? string.Empty)
            {
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                }
                else if (current == '{')
                {
                    stack.Push('}');
                }
                else if (current == '[')
                {
                    stack.Push(']');
                }
                else if ((current == '}' || current == ']') && stack.Count > 0 && stack.Peek() == current)
                {
                    stack.Pop();
                }
            }

            if (stack.Count == 0)
            {
                return text;
            }

            return (text ?? string.Empty).TrimEnd() + Environment.NewLine + new string(stack.ToArray()) + Environment.NewLine;
        }

        private static string ResolvePackageFile(string folderPath, string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return string.Empty;
            }

            var relative = reference.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(folderPath, relative);
        }

        private static string GetPackageRelativePath(string folderPath, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                var fullPath = Path.GetFullPath(path);
                var fullFolder = string.IsNullOrWhiteSpace(folderPath)
                    ? string.Empty
                    : Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.IsNullOrWhiteSpace(fullFolder) &&
                    fullPath.StartsWith(fullFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return NormalizePackagePath(fullPath.Substring(fullFolder.Length + 1));
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not normalize package-relative path '{path ?? string.Empty}' " +
                    $"against folder '{folderPath ?? string.Empty}'; using the provided path. Reason: {ex.Message}");
            }

            return NormalizePackagePath(path);
        }

        private static string NormalizePackagePath(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .Replace('\\', '/');
        }

        private static string UniqueFragment(string fragment, ISet<string> used)
        {
            var value = string.IsNullOrWhiteSpace(fragment) ? "fragment" : fragment;
            var result = value;
            var index = 2;
            while (used.Contains(result))
            {
                result = value + "-" + index.ToString(CultureInfo.InvariantCulture);
                index++;
            }

            used.Add(result);
            return result;
        }

        private static string UniqueObjectKey(string key, JObject obj)
        {
            var value = string.IsNullOrWhiteSpace(key) ? "item" : key.Trim();
            var result = value;
            var index = 2;
            while (obj != null && obj[result] != null)
            {
                result = value + "-" + index.ToString(CultureInfo.InvariantCulture);
                index++;
            }

            return result;
        }

        private static string Slug(string value)
        {
            var slug = Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant();
            return string.IsNullOrWhiteSpace(slug) ? "fragment" : slug;
        }

        private static JToken Clean(JToken value)
        {
            if (value is JObject obj)
            {
                return CleanObject(obj);
            }

            if (value is JArray array)
            {
                var result = new JArray();
                foreach (var item in array.Select(Clean).Where(item => item != null && !IsEmpty(item)))
                {
                    result.Add(item);
                }

                return result;
            }

            return value?.DeepClone();
        }

        private static JObject CleanObject(JObject obj)
        {
            var result = new JObject();
            foreach (var property in obj.Properties())
            {
                var cleaned = Clean(property.Value);
                if (cleaned == null || cleaned.Type == JTokenType.Null || IsEmpty(cleaned))
                {
                    continue;
                }

                result[property.Name] = cleaned;
            }

            return result;
        }

        private static bool IsEmpty(JToken value)
        {
            return value is JObject obj && !obj.HasValues ||
                   value is JArray array && array.Count == 0;
        }

        private static JToken Clone(JToken value)
        {
            return value == null || value.Type == JTokenType.Null ? null : value.DeepClone();
        }

        private static void AddUniqueString(JArray array, string value)
        {
            if (array == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (array.Values<string>().Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            array.Add(value);
        }

        private static string ReadString(JObject obj, params string[] names)
        {
            if (obj == null || names == null)
            {
                return null;
            }

            foreach (var name in names)
            {
                var token = obj[name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    continue;
                }

                var value = token.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static bool HasAnyProperty(JObject obj, params string[] names)
        {
            if (obj == null || names == null)
            {
                return false;
            }

            return names.Any(name => obj.Properties().Any(property =>
                string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool ReadBool(JObject obj, string name, bool defaultValue)
        {
            var token = obj?[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                return defaultValue;
            }

            return token.Type == JTokenType.Boolean
                ? token.Value<bool>()
                : bool.TryParse(token.ToString(), out var parsed) ? parsed : defaultValue;
        }

        private static int ReadInt(JToken token, int defaultValue)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return defaultValue;
            }

            return int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : defaultValue;
        }

        private static float ReadFloat(JToken token, float defaultValue)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return defaultValue;
            }

            return float.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : defaultValue;
        }

        private static string NormalizeEnd(string value)
        {
            var text = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (text == "start" || text == "a")
            {
                return "A";
            }

            if (text == "end" || text == "b")
            {
                return "B";
            }

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static bool IsLoadSchemaKey(string key)
        {
            switch ((key ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "name":
                case "description":
                case "units":
                case "density":
                case "unitweightinpounds":
                case "importable":
                case "payperquantity":
                case "costperunit":
                case "cartypefilter":
                case "fields":
                    return true;
                default:
                    return false;
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
