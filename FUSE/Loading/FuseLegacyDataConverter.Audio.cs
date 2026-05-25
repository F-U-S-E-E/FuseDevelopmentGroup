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
    }
}
