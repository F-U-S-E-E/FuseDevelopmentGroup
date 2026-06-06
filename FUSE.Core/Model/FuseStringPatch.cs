using System;
using System.Collections.Generic;
using System.Linq;
using Fuse.Core.Serialization;
using Newtonsoft.Json;

namespace Fuse.Core.Model
{
    /// <summary>
    /// Two-shape container for the unlock-fan-out fields on
    /// <see cref="FuseMapFeature"/> and <see cref="FuseSection"/> — track
    /// groups, areas, game objects, industries, prerequisites, etc.
    ///
    /// <para>The JSON contract supports two distinct intents per field:</para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///     <b>Replace</b> (JSON array form, e.g. <c>["foo","bar"]</c>) — the
    ///     authored list is the complete set. Apply replaces whatever the
    ///     live runtime object had with exactly these ids.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     <b>Merge</b> (JSON object form, e.g.
    ///     <c>{"foo": true, "bar": false}</c>) — the authored entries are a
    ///     per-id patch. Keys with <c>true</c> are added to whatever the live
    ///     object already has; keys with <c>false</c> are removed. Existing
    ///     ids not mentioned by the patch are left intact.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// <para>Field-omitted-from-JSON (<see cref="HasValue"/> false) means
    /// "no change requested" — runtime state is preserved.</para>
    ///
    /// <para>FUSE's earlier converter behaviour collapsed both shapes into
    /// "the truthy keys" and then replaced the runtime array — losing the
    /// distinction between "replace" and "merge" and silently dropping the
    /// false-valued removals. That broke mod patches that only intended to
    /// add a single entry: every field they omitted was preserved (good),
    /// but every field they touched got their full base-game value wiped
    /// out (bad). The MaconCounty mod's <c>alarka</c> map feature patch is
    /// the original reproducer — the patch only specifies a single new
    /// track group on top of whatever the base map feature already enables,
    /// not a wholesale replacement.</para>
    /// </summary>
    [JsonConverter(typeof(FuseStringPatchConverter))]
    public sealed class FuseStringPatch
    {
        /// <summary>
        /// When non-null, this is the full replacement set (JSON array form).
        /// Mutually exclusive with <see cref="Patch"/>.
        /// </summary>
        public string[] Set { get; private set; }

        /// <summary>
        /// When non-null, this is the per-id merge patch (JSON object form).
        /// Mutually exclusive with <see cref="Set"/>.
        /// </summary>
        public Dictionary<string, bool> Patch { get; private set; }

        public FuseStringPatch()
        {
        }

        public static FuseStringPatch FromSet(IEnumerable<string> ids)
        {
            return new FuseStringPatch
            {
                Set = (ids ?? Array.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToArray()
            };
        }

        public static FuseStringPatch FromPatch(IEnumerable<KeyValuePair<string, bool>> entries)
        {
            var dict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries ?? Array.Empty<KeyValuePair<string, bool>>())
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }
                dict[entry.Key] = entry.Value;
            }
            return new FuseStringPatch { Patch = dict };
        }

        /// <summary>
        /// True when this patch carries an explicit instruction (either a
        /// replacement set or a merge dict). Callers use this to distinguish
        /// "no change" from "empty replacement" — both look like an empty
        /// array via <see cref="EffectiveAdditions"/>.
        /// </summary>
        public bool HasValue => Set != null || Patch != null;

        /// <summary>
        /// The additions-only view, used by code paths that historically
        /// expected the converter's truthy-keys-only behaviour. For the
        /// "replace" shape this is the whole set; for the "merge" shape this
        /// is just the keys with <c>true</c>. Removals are not surfaced here —
        /// callers that care about removals should call <see cref="ApplyTo"/>.
        /// </summary>
        public string[] EffectiveAdditions
        {
            get
            {
                if (Set != null)
                {
                    return Set;
                }
                if (Patch != null)
                {
                    return Patch
                        .Where(kvp => kvp.Value)
                        .Select(kvp => kvp.Key)
                        .ToArray();
                }
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Returns the array of ids that would result from applying this
        /// patch on top of <paramref name="existing"/>. Comparisons are
        /// case-insensitive (matches the convention FUSE uses throughout the
        /// progression / track-group identifier space). The returned order
        /// is unspecified for the merge shape; callers that need a stable
        /// order should sort the result themselves.
        /// </summary>
        public string[] ApplyTo(IEnumerable<string> existing)
        {
            if (Set != null)
            {
                return Set
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existing != null)
            {
                foreach (var id in existing)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        result.Add(id);
                    }
                }
            }
            if (Patch != null)
            {
                foreach (var kvp in Patch)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key))
                    {
                        continue;
                    }
                    if (kvp.Value)
                    {
                        result.Add(kvp.Key);
                    }
                    else
                    {
                        result.Remove(kvp.Key);
                    }
                }
            }
            return result.ToArray();
        }
    }
}
