using System;
using System.Collections.Generic;
using System.Reflection;
using FUSE.Runtime.API;
using FUSE.Authoring.Data;
using Game.Progression;

namespace FUSE.Interface
{
    /// <summary>
    /// Walks loaded FUSE map features and progression sections to surface which progression
    /// constructs reference a given scenery game object or track group. Used by the debug
    /// overlays so authors can see "this building is gated by Stage 2 of progression X"
    /// without dumping the runtime graph.
    /// </summary>
    internal static class FuseProgressionImpactLookup
    {
        private static readonly PropertyInfo MapFeatureUnlockedProperty =
            typeof(MapFeature).GetProperty("Unlocked", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo MapFeatureUnlockedField =
            typeof(MapFeature).GetField("Unlocked", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public readonly struct Impact
        {
            public Impact(string sourceKind, string sourceId, string effect, string state = null, string target = null)
            {
                SourceKind = sourceKind;
                SourceId = sourceId;
                Effect = effect;
                State = state;
                Target = target;
            }

            public string SourceKind { get; }
            public string SourceId { get; }
            public string Effect { get; }
            public string State { get; }
            public string Target { get; }
        }

        public static List<Impact> FindForGameObject(string scenePath, string leafName, string fuseId)
        {
            var hits = new List<Impact>();
            var candidates = BuildCandidateSet(scenePath, leafName, fuseId);
            if (candidates.Count == 0)
            {
                return hits;
            }

            CollectFromMapFeatures(hits, feature =>
            {
                AddIfMatches(hits, candidates, "MapFeature", feature.Identifier, "enables game object on unlock", feature.Definition?.GameObjectsEnableOnUnlock, feature.State);
            });

            CollectFromSections(hits, section =>
            {
                AddIfMatches(hits, candidates, "Section", section.QualifiedId, "enables game object on unlock", section.Definition?.GameObjectsEnableOnUnlock);
            });

            return hits;
        }

        public static List<Impact> FindForTrackGroup(string groupId)
        {
            var hits = new List<Impact>();
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return hits;
            }

            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { groupId };

            CollectFromMapFeatures(hits, feature =>
            {
                AddIfMatches(hits, candidates, "MapFeature", feature.Identifier, "enables track group on unlock", feature.Definition?.TrackGroupsEnableOnUnlock, feature.State);
                AddIfMatches(hits, candidates, "MapFeature", feature.Identifier, "makes track group available on unlock", feature.Definition?.TrackGroupsAvailableOnUnlock, feature.State);
            });

            CollectFromSections(hits, section =>
            {
                AddIfMatches(hits, candidates, "Section", section.QualifiedId, "enables track group on unlock", section.Definition?.TrackGroupsEnableOnUnlock);
                AddIfMatches(hits, candidates, "Section", section.QualifiedId, "makes track group available on unlock", section.Definition?.TrackGroupsAvailableOnUnlock);
            });

            return hits;
        }

        public static List<Impact> FindForIndustry(string industryId)
        {
            var hits = new List<Impact>();
            if (string.IsNullOrWhiteSpace(industryId))
            {
                return hits;
            }

            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { industryId };

            CollectFromMapFeatures(hits, feature =>
            {
                AddIfMatches(hits, candidates, "MapFeature", feature.Identifier, "include industry on unlock", feature.Definition?.UnlockIncludeIndustries, feature.State);
                AddIfMatches(hits, candidates, "MapFeature", feature.Identifier, "exclude industry on unlock", feature.Definition?.UnlockExcludeIndustries, feature.State);
            });

            CollectFromSections(hits, section =>
            {
                AddIfMatches(hits, candidates, "Section", section.QualifiedId, "include industry on unlock", section.Definition?.UnlockIncludeIndustries);
                AddIfMatches(hits, candidates, "Section", section.QualifiedId, "exclude industry on unlock", section.Definition?.UnlockExcludeIndustries);
            });

            return hits;
        }

        public static List<Impact> FindForArea(string areaId)
        {
            var hits = new List<Impact>();
            if (string.IsNullOrWhiteSpace(areaId))
            {
                return hits;
            }

            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { areaId };

            CollectFromMapFeatures(hits, feature =>
            {
                AddIfMatches(hits, candidates, "MapFeature", feature.Identifier, "enables area on unlock", feature.Definition?.AreasEnableOnUnlock, feature.State);
            });

            CollectFromSections(hits, section =>
            {
                AddIfMatches(hits, candidates, "Section", section.QualifiedId, "enables area on unlock", section.Definition?.AreasEnableOnUnlock);
            });

            return hits;
        }

        private static HashSet<string> BuildCandidateSet(string scenePath, string leafName, string fuseId)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(scenePath))
            {
                set.Add(scenePath);
            }

            if (!string.IsNullOrWhiteSpace(leafName))
            {
                set.Add(leafName);
            }

            if (!string.IsNullOrWhiteSpace(fuseId))
            {
                set.Add(fuseId);
            }

            return set;
        }

        private static void CollectFromMapFeatures(List<Impact> hits, Action<MapFeatureEntry> visitor)
        {
            IEnumerable<MapFeature> features;
            try
            {
                features = ProgressionAPI.GetAllMapFeatures();
            }
            catch
            {
                return;
            }

            if (features == null)
            {
                return;
            }

            foreach (var feature in features)
            {
                if (feature == null || string.IsNullOrWhiteSpace(feature.identifier))
                {
                    continue;
                }

                FuseMapFeature definition = null;
                try
                {
                    definition = ProgressionAPI.GetDefinition(feature);
                }
                catch
                {
                    definition = null;
                }

                if (definition == null)
                {
                    continue;
                }

                visitor(new MapFeatureEntry(feature.identifier, definition, FormatMapFeatureState(feature)));
            }
        }

        private static void CollectFromSections(List<Impact> hits, Action<SectionEntry> visitor)
        {
            IEnumerable<Progression> progressions;
            try
            {
                progressions = ProgressionAPI.GetAllProgressions();
            }
            catch
            {
                return;
            }

            if (progressions == null)
            {
                return;
            }

            foreach (var progression in progressions)
            {
                if (progression == null || string.IsNullOrWhiteSpace(progression.identifier))
                {
                    continue;
                }

                FuseProgression definition = null;
                try
                {
                    definition = ProgressionAPI.GetDefinition(progression);
                }
                catch
                {
                    definition = null;
                }

                if (definition?.Sections == null)
                {
                    continue;
                }

                foreach (var pair in definition.Sections)
                {
                    if (pair.Value == null || string.IsNullOrWhiteSpace(pair.Key))
                    {
                        continue;
                    }

                    visitor(new SectionEntry(progression.identifier + "/" + pair.Key, pair.Value));
                }
            }
        }

        private static void AddIfMatches(
            List<Impact> hits,
            HashSet<string> candidates,
            string sourceKind,
            string sourceId,
            string effect,
            FuseStringPatch targets,
            string state = null)
        {
            // Use EffectiveAdditions — the impact lookup is asking
            // "does this patch source advertise any of the candidate
            // ids in its add-list?" The patch dict's false (removal)
            // entries aren't an "impact" in this sense, so they're
            // intentionally excluded.
            var ids = targets?.EffectiveAdditions;
            if (ids == null || ids.Length == 0 || candidates.Count == 0)
            {
                return;
            }

            for (var index = 0; index < ids.Length; index++)
            {
                var target = ids[index];
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                if (MatchesAny(candidates, target))
                {
                    hits.Add(new Impact(sourceKind, sourceId, effect, state, target));
                    return;
                }
            }
        }

        private static string FormatMapFeatureState(MapFeature feature)
        {
            if (feature == null)
            {
                return null;
            }

            var parts = new List<string>();
            if (ProgressionAPI.TryGetMapFeatureEnabledState(feature, out var enabled))
            {
                parts.Add("enabled=" + BoolText(enabled));
            }

            var unlocked = TryReadMapFeatureUnlocked(feature);
            if (unlocked.HasValue)
            {
                parts.Add("unlocked=" + BoolText(unlocked.Value));
            }

            parts.Add("defaultSandbox=" + BoolText(feature.defaultEnableInSandbox));
            return string.Join(", ", parts.ToArray());
        }

        private static bool? TryReadMapFeatureUnlocked(MapFeature feature)
        {
            if (feature == null)
            {
                return null;
            }

            try
            {
                var propertyValue = MapFeatureUnlockedProperty?.GetValue(feature, null);
                if (propertyValue is bool propertyBool)
                {
                    return propertyBool;
                }
            }
            catch
            {
                // Fall through to the backing field probe below.
            }

            try
            {
                var fieldValue = MapFeatureUnlockedField?.GetValue(feature);
                if (fieldValue is bool fieldBool)
                {
                    return fieldBool;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static string BoolText(bool value)
        {
            return value ? "true" : "false";
        }

        private static bool MatchesAny(HashSet<string> candidates, string target)
        {
            if (candidates.Contains(target))
            {
                return true;
            }

            // Allow leaf-name matches when the candidate is a full path and the target is a leaf,
            // or vice versa. Cheap heuristic that catches the common scene-path / id mismatch.
            var targetLeaf = LeafName(target);
            if (!string.IsNullOrWhiteSpace(targetLeaf) && candidates.Contains(targetLeaf))
            {
                return true;
            }

            foreach (var candidate in candidates)
            {
                var candidateLeaf = LeafName(candidate);
                if (!string.IsNullOrWhiteSpace(candidateLeaf) &&
                    string.Equals(candidateLeaf, targetLeaf, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (candidate.EndsWith("/" + target, StringComparison.OrdinalIgnoreCase) ||
                    target.EndsWith("/" + candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string LeafName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var lastSlash = path.LastIndexOf('/');
            return lastSlash >= 0 && lastSlash + 1 < path.Length
                ? path.Substring(lastSlash + 1)
                : path;
        }

        private readonly struct MapFeatureEntry
        {
            public MapFeatureEntry(string identifier, FuseMapFeature definition, string state)
            {
                Identifier = identifier;
                Definition = definition;
                State = state;
            }

            public string Identifier { get; }
            public FuseMapFeature Definition { get; }
            public string State { get; }
        }

        private readonly struct SectionEntry
        {
            public SectionEntry(string qualifiedId, FuseSection definition)
            {
                QualifiedId = qualifiedId;
                Definition = definition;
            }

            public string QualifiedId { get; }
            public FuseSection Definition { get; }
        }
    }
}
