using System;
using System.Collections.Generic;
using FUSE.Infrastructure;
using FUSE.Loading;
using Game.Messages;
using Newtonsoft.Json.Linq;

namespace FUSE.Migrations
{
    /// <summary>
    /// Applies "game-migrations" data carried in FUSE definitions'
    /// <c>Extensions["gameMigrations"]</c> bag to a save Snapshot before the
    /// game's StateManager reads it. The legacy mixinto format has two
    /// dictionaries:
    /// <list type="bullet">
    ///   <item>
    ///     <c>properties</c>: <c>oldIndustryId -> newIndustryId</c>. Used when
    ///     an authoring mod renames an industry between versions; the saved
    ///     <see cref="Snapshot.Properties"/> dictionary is keyed by entity id
    ///     and would otherwise still point at the obsolete id.
    ///   </item>
    ///   <item>
    ///     <c>waybillDestinations</c>: <c>"oldIndustry.oldSlot" -> "newIndustry.newSlot"</c>.
    ///     Cars carry a serialized waybill in their per-car property bag
    ///     (<c>ops.waybill</c>) and industries carry pending-contract refs
    ///     (<c>nextContract</c>, <c>_recvdCars</c>); each has destId/originId
    ///     strings of the form <c>industry.slot</c> that need to be rewritten
    ///     when the slot moved.
    ///   </item>
    /// </list>
    /// The implementation walks the snapshot once, renames outer property
    /// keys, then rewrites waybill destination/origin ids in place. A
    /// <c>properties</c> entry also acts as a fallback prefix mapper for
    /// destination strings whose new slot id was not given explicitly: if
    /// <c>old -&gt; new</c> is in the properties map, then any waybill id of
    /// the form <c>old.&lt;slot&gt;</c> is rewritten to <c>new.&lt;slot&gt;</c>.
    /// This matches the legacy expectation that renaming an industry also
    /// rewrites every waybill that referenced it.
    /// </summary>
    internal static class FuseGameMigrationApplier
    {
        public static void ApplyToSnapshot(ref Snapshot snapshot, string reason)
        {
            try
            {
                var maps = CollectMaps();
                if (maps.Properties.Count == 0 && maps.Waybills.Count == 0)
                {
                    return;
                }

                var renamedProperties = RenamePropertyKeys(ref snapshot, maps.Properties);
                var rewrittenWaybills = RewriteWaybillIds(ref snapshot, maps);
                FuseLog.Info(
                    $"FUSE game-migrations '{reason ?? "snapshot"}' applied: " +
                    $"{renamedProperties} property bag(s) renamed, " +
                    $"{rewrittenWaybills} waybill-id rewrite(s) performed " +
                    $"(propertyMap={maps.Properties.Count}, waybillMap={maps.Waybills.Count}).");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE game-migrations apply for '{reason ?? "snapshot"}' failed.", ex);
            }
        }

        /// <summary>
        /// Returns true iff at least one loaded FUSE definition advertises a
        /// non-empty <c>gameMigrations</c> bag. Cheaper than running the full
        /// applier on every snapshot, and lets the patches log when migration
        /// data is in play.
        /// </summary>
        public static bool HasAnyMigrations()
        {
            foreach (var mod in FuseModLoader.GetLoadedModsInOrder())
            {
                if (TryReadMigrationToken(mod, out var token) && (HasNonEmpty(token, "properties") ||
                                                                  HasNonEmpty(token, "Properties") ||
                                                                  HasNonEmpty(token, "waybillDestinations") ||
                                                                  HasNonEmpty(token, "WaybillDestinations")))
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct MigrationMaps
        {
            public MigrationMaps(Dictionary<string, string> properties, Dictionary<string, string> waybills)
            {
                Properties = properties;
                Waybills = waybills;
            }

            public Dictionary<string, string> Properties { get; }
            public Dictionary<string, string> Waybills { get; }
        }

        private static MigrationMaps CollectMaps()
        {
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            var waybills = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var mod in FuseModLoader.GetLoadedModsInOrder())
            {
                if (!TryReadMigrationToken(mod, out var token))
                {
                    continue;
                }

                AccumulateMap(properties, token["properties"] as JObject);
                AccumulateMap(properties, token["Properties"] as JObject);
                AccumulateMap(waybills, token["waybillDestinations"] as JObject);
                AccumulateMap(waybills, token["WaybillDestinations"] as JObject);
            }

            return new MigrationMaps(properties, waybills);
        }

        private static bool TryReadMigrationToken(FuseLoadedMod mod, out JObject token)
        {
            token = null;
            var extensions = mod?.Definition?.Extensions;
            if (extensions == null)
            {
                return false;
            }

            if (!extensions.TryGetValue("gameMigrations", out var raw) || raw == null)
            {
                return false;
            }

            token = ConvertToJObject(raw);
            return token != null;
        }

        private static JObject ConvertToJObject(object value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is JObject jo)
            {
                return jo;
            }

            try
            {
                return JObject.FromObject(value);
            }
            catch
            {
                return null;
            }
        }

        private static bool HasNonEmpty(JObject token, string key)
        {
            if (token == null)
            {
                return false;
            }

            if (!(token[key] is JObject child))
            {
                return false;
            }

            return child.Count > 0;
        }

        private static void AccumulateMap(Dictionary<string, string> target, JObject source)
        {
            if (source == null)
            {
                return;
            }

            foreach (var property in source.Properties())
            {
                var key = property.Name?.Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var value = property.Value?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (string.Equals(key, value, StringComparison.Ordinal))
                {
                    continue;
                }

                target[key] = value;
            }
        }

        private static int RenamePropertyKeys(ref Snapshot snapshot, Dictionary<string, string> map)
        {
            if (map == null || map.Count == 0 || snapshot.Properties == null)
            {
                return 0;
            }

            // Snapshot a stable key list before mutating the underlying dictionary
            // so that we don't enumerate while modifying.
            var keys = new List<string>(snapshot.Properties.Keys);
            var renamed = 0;
            foreach (var oldKey in keys)
            {
                if (!map.TryGetValue(oldKey, out var newKey) || string.IsNullOrWhiteSpace(newKey))
                {
                    continue;
                }

                if (string.Equals(oldKey, newKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!snapshot.Properties.TryGetValue(oldKey, out var bag))
                {
                    continue;
                }

                if (snapshot.Properties.TryGetValue(newKey, out var existing) && existing != null && existing.Count > 0)
                {
                    // The new key is already populated (perhaps by the new
                    // definition's default property bag). Merge stale entries
                    // from the old bag without overwriting any field the new
                    // owner already published.
                    if (bag != null)
                    {
                        foreach (var pair in bag)
                        {
                            if (!existing.ContainsKey(pair.Key))
                            {
                                existing[pair.Key] = pair.Value;
                            }
                        }
                    }
                }
                else
                {
                    snapshot.Properties[newKey] = bag;
                }

                snapshot.Properties.Remove(oldKey);
                renamed++;
            }

            return renamed;
        }

        private static int RewriteWaybillIds(ref Snapshot snapshot, MigrationMaps maps)
        {
            if (snapshot.Properties == null)
            {
                return 0;
            }

            if (maps.Waybills.Count == 0 && maps.Properties.Count == 0)
            {
                return 0;
            }

            var rewrites = 0;
            foreach (var bag in snapshot.Properties.Values)
            {
                if (bag == null)
                {
                    continue;
                }

                rewrites += RewriteSerializedWaybill(bag, "ops.waybill", maps);
                rewrites += RewriteSerializedWaybill(bag, "nextContract", maps);
                rewrites += RewriteFreeFormProperty(bag, "_recvdCars", maps);
                rewrites += RewriteFreeFormProperty(bag, "contract", maps);
            }

            return rewrites;
        }

        private static int RewriteSerializedWaybill(Dictionary<string, IPropertyValue> bag, string key, MigrationMaps maps)
        {
            if (!bag.TryGetValue(key, out var value) || !(value is DictionaryPropertyValue dpv) || dpv.Value == null)
            {
                return 0;
            }

            var rewrites = 0;
            foreach (var idField in new[] { "destId", "originId" })
            {
                if (!dpv.Value.TryGetValue(idField, out var rawId) || !(rawId is StringPropertyValue spv))
                {
                    continue;
                }

                var mapped = MapDestination(spv.Value, maps);
                if (mapped == null)
                {
                    continue;
                }

                dpv.Value[idField] = new StringPropertyValue(mapped);
                rewrites++;
            }

            return rewrites;
        }

        private static int RewriteFreeFormProperty(Dictionary<string, IPropertyValue> bag, string key, MigrationMaps maps)
        {
            if (!bag.TryGetValue(key, out var value))
            {
                return 0;
            }

            return RewriteStringsInside(value, maps);
        }

        // Best-effort recursive walk: any StringPropertyValue whose contents
        // look like an industry+slot identifier gets passed through
        // MapDestination so structures we don't have a specific schema for
        // (industry contracts, _recvdCars manifests) still pick up the
        // rename without us hard-coding their layout.
        private static int RewriteStringsInside(IPropertyValue value, MigrationMaps maps)
        {
            switch (value)
            {
                case DictionaryPropertyValue dpv when dpv.Value != null:
                {
                    var rewrites = 0;
                    var pending = new List<string>(dpv.Value.Keys);
                    foreach (var inner in pending)
                    {
                        if (!dpv.Value.TryGetValue(inner, out var child))
                        {
                            continue;
                        }

                        if (child is StringPropertyValue spv)
                        {
                            var mapped = MapDestination(spv.Value, maps);
                            if (mapped != null)
                            {
                                dpv.Value[inner] = new StringPropertyValue(mapped);
                                rewrites++;
                            }
                        }
                        else
                        {
                            rewrites += RewriteStringsInside(child, maps);
                        }
                    }

                    return rewrites;
                }

                case ArrayPropertyValue apv when apv.Value != null:
                {
                    var rewrites = 0;
                    for (var i = 0; i < apv.Value.Count; i++)
                    {
                        var child = apv.Value[i];
                        if (child is StringPropertyValue spv)
                        {
                            var mapped = MapDestination(spv.Value, maps);
                            if (mapped != null)
                            {
                                apv.Value[i] = new StringPropertyValue(mapped);
                                rewrites++;
                            }
                        }
                        else
                        {
                            rewrites += RewriteStringsInside(child, maps);
                        }
                    }

                    return rewrites;
                }
            }

            return 0;
        }

        private static string MapDestination(string id, MigrationMaps maps)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            if (maps.Waybills.TryGetValue(id, out var direct))
            {
                return direct;
            }

            // Fallback: if the property map redirects the industry portion of
            // an "<industry>.<slot>" id, rewrite the prefix so waybills aimed
            // at a renamed industry move to the new owner without the author
            // having to enumerate every slot.
            var dot = id.IndexOf('.');
            if (dot <= 0)
            {
                return null;
            }

            var industry = id.Substring(0, dot);
            if (!maps.Properties.TryGetValue(industry, out var newIndustry) || string.IsNullOrWhiteSpace(newIndustry))
            {
                return null;
            }

            return newIndustry + id.Substring(dot);
        }
    }
}
