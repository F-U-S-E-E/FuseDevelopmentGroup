using System;
using System.Collections.Generic;
using System.IO;
using Model.Database;
using Newtonsoft.Json.Linq;
using FUSE.Infrastructure;
using FUSE.Loading;
using UnityEngine;

namespace FUSE.Runtime.API
{
    /// <summary>
    /// Publishes FUSE-registered loose-file whistles to the vanilla whistle
    /// picker through a generated direct asset-pack store.
    ///
    /// The customize window builds its whistle dropdown from
    /// <c>PrefabStore.AllDefinitionInfosOfType&lt;WhistleDefinition&gt;()</c>,
    /// which enumerates asset-pack stores only. FUSE's converted legacy
    /// whistles (whistles.json packs) live in <see cref="FuseAudioAPI"/>'s
    /// in-memory registry, so without a store entry they never reach the
    /// picker — that is exactly the regression left behind when the closed
    /// generic Harmony patch on the enumeration was removed (patching one T
    /// of a JIT-shared generic body fires for every other T; see
    /// <see cref="FUSE.Patches.FuseWhistleControllerConfigurePatch"/>).
    ///
    /// Instead of any generic patch, FUSE writes the registered whistles as
    /// a Definitions.json in the game's own container schema to a folder
    /// under LocalLow and mounts that folder through the existing
    /// fuseasset:// direct-store path
    /// (<see cref="FuseAssetPackRegistry.EnsureGeneratedDirectStore"/> +
    /// <see cref="FUSE.Patches.FuseAssetPackRuntimeStoreContainerPatch"/>).
    /// Vanilla then lists AND resolves the whistles natively — including
    /// <c>DefinitionForIdentifier&lt;WhistleDefinition&gt;</c>, which used to
    /// throw <c>UnknownIdentifierException</c> for FUSE whistle ids.
    ///
    /// Every generated definition carries an EMPTY audio reference on
    /// purpose: <c>WhistleController.Configure</c> skips its async audio
    /// branch for empty references, leaving the clip to FUSE
    /// (<see cref="FuseAudioAPI.TryConfigureWhistle"/> serves it from disk),
    /// while the model reference stays real so the definition describes the
    /// same 3D whistle FUSE attaches.
    /// </summary>
    internal static class FuseWhistleDefinitionStore
    {
        internal static string StoreFolderPath =>
            Path.Combine(Application.persistentDataPath, "FUSE", "GeneratedStores", "fuse-whistles");

        /// <summary>
        /// Pure generator (unit-tested): the game-schema Definitions.json for
        /// the supplied whistles. Matches the object shape shipping asset
        /// packs use, which is what the direct-store loader deserializes with
        /// the game's own serializer settings.
        /// </summary>
        internal static string BuildDefinitionsJson(IEnumerable<FuseWhistleStoreEntry> whistles)
        {
            var objects = new JArray();
            foreach (var whistle in whistles ?? Array.Empty<FuseWhistleStoreEntry>())
            {
                if (string.IsNullOrWhiteSpace(whistle.Id))
                {
                    continue;
                }

                objects.Add(new JObject
                {
                    ["identifier"] = whistle.Id,
                    ["metadata"] = new JObject
                    {
                        ["name"] = string.IsNullOrWhiteSpace(whistle.Name) ? whistle.Id : whistle.Name,
                        ["description"] = "FUSE loose-file whistle",
                        ["tags"] = new JArray(),
                        ["credits"] = string.Empty
                    },
                    ["definition"] = new JObject
                    {
                        ["kind"] = "Whistle",
                        ["model"] = new JObject
                        {
                            ["assetPackIdentifier"] = whistle.ModelAssetPackIdentifier ?? string.Empty,
                            ["assetIdentifier"] = whistle.ModelAssetIdentifier ?? string.Empty
                        },
                        ["audio"] = new JObject
                        {
                            ["assetPackIdentifier"] = string.Empty,
                            ["assetIdentifier"] = string.Empty
                        },
                        ["components"] = JValue.CreateNull()
                    }
                });
            }

            return new JObject { ["objects"] = objects }.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        /// <summary>
        /// Minimal catalog so the generated folder satisfies the same
        /// file-layout invariants every other direct store does. It declares
        /// no assets: the definitions reference models in OTHER packs and
        /// empty audio, so nothing ever opens this store's (absent) bundle.
        /// </summary>
        internal static string BuildCatalogJson()
        {
            return new JObject
            {
                ["identifier"] = "fuse.generated.whistles",
                ["name"] = "FUSE Whistles",
                ["shared"] = false,
                ["assets"] = new JObject()
            }.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        /// <summary>
        /// Rewrites the generated store from the current whistle registry and
        /// (re)mounts it on the live PrefabStore. Safe to call repeatedly —
        /// files are only rewritten when content changed, and the mounted
        /// store's cached container is invalidated only on a rewrite so the
        /// next picker open re-reads the refreshed definitions.
        /// </summary>
        internal static void Sync(IReadOnlyList<FuseWhistleStoreEntry> whistles)
        {
            try
            {
                var folder = StoreFolderPath;
                Directory.CreateDirectory(folder);
                var changed = WriteIfChanged(Path.Combine(folder, "Definitions.json"), BuildDefinitionsJson(whistles));
                WriteIfChanged(Path.Combine(folder, "Catalog.json"), BuildCatalogJson());

                // PrefabStore is per-map. When none exists yet (early apply),
                // the PrefabStore.Create postfix warm-mounts this folder on
                // the next map load instead.
                if (!(TrainController.Shared?.PrefabStore is PrefabStore prefabStore))
                {
                    return;
                }

                if (FuseAssetPackRegistry.EnsureGeneratedDirectStore(prefabStore, folder, invalidateContainer: changed) &&
                    changed)
                {
                    FuseLog.Info(
                        $"FUSE published {whistles?.Count ?? 0} whistle definition(s) to the generated picker store.");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE could not publish whistle definitions to the generated picker store", ex);
            }
        }

        private static bool WriteIfChanged(string path, string content)
        {
            try
            {
                if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Info(
                    $"FUSE could not compare generated store file '{path}' " +
                    $"({ex.GetBaseException().Message}); rewriting it.");
            }

            File.WriteAllText(path, content);
            return true;
        }
    }

    /// <summary>
    /// The registry facts <see cref="FuseWhistleDefinitionStore"/> needs from
    /// a registered whistle, decoupled from <see cref="FuseAudioAPI"/>'s
    /// private entry types so the JSON generation stays pure and testable.
    /// </summary>
    internal readonly struct FuseWhistleStoreEntry
    {
        internal FuseWhistleStoreEntry(string id, string name, string modelAssetPackIdentifier, string modelAssetIdentifier)
        {
            Id = id;
            Name = name;
            ModelAssetPackIdentifier = modelAssetPackIdentifier;
            ModelAssetIdentifier = modelAssetIdentifier;
        }

        internal string Id { get; }

        internal string Name { get; }

        internal string ModelAssetPackIdentifier { get; }

        internal string ModelAssetIdentifier { get; }
    }
}
