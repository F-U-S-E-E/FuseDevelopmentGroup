using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Progression;
using Game.State;
using KeyValue.Runtime;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Loading;
using Track;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static partial class ProgressionAPI
    {
        private static readonly FieldInfo ManagerFeaturesField = typeof(MapFeatureManager).GetField("_features", BindingFlags.Instance | BindingFlags.NonPublic);
        // TrackSpan's cached segments + recompute method are private; we read them
        // by reflection for the diagnostic MapEnhancer-simulation dump so we can
        // see exactly which TrackSegments Map Enhancer would classify as industrial
        // when it walks IndustryComponent.TrackSpans in its IndustryTrackClassPatch.
        private static readonly FieldInfo TrackSpanCachedSegmentsField = typeof(TrackSpan).GetField("_cachedSegments", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo TrackSpanUpdateCachedPointsMethod = typeof(TrackSpan).GetMethod("UpdateCachedPointsIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ManagerProgressionsField = typeof(ProgressionManager).GetField("_progressions", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ManagerCurrentProgressionField = typeof(ProgressionManager).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo ManagerFeatureEnablesProperty = typeof(MapFeatureManager).GetProperty("FeatureEnables", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ManagerHandleFeatureEnablesChangedMethod = typeof(MapFeatureManager).GetMethod("HandleFeatureEnablesChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ProgressionSectionsField = typeof(Progression).GetField("<Sections>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        // Progression.enableFeaturesAtStart is a private [SerializeField] MapFeature[]
        // (FormerlySerializedAs "enableAtStart"). Progression.Configure calls
        // mapFeatureManager.SetFeatureEnabled(feature, true) for each entry when
        // hosting, on every load — the base career's own start-feature lever.
        private static readonly FieldInfo ProgressionEnableFeaturesAtStartField = typeof(Progression).GetField("enableFeaturesAtStart", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ProgressionUpdateSectionStatesMethod = typeof(Progression).GetMethod("UpdateSectionStates", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SectionInterchangeTransfersField = typeof(Section).GetField("<InterchangeTransfers>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo InterchangeTransferFromField = typeof(InterchangeTransfer).GetField("from", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo InterchangeTransferToField = typeof(InterchangeTransfer).GetField("to", BindingFlags.Instance | BindingFlags.NonPublic);
        private const string FuseInterchangeTransferPrefix = "FUSE Interchange Transfer ";

        public static MapFeature AddMapFeature(string id, FuseMapFeature definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetMapFeature(id) != null)
            {
                throw new InvalidOperationException($"Map feature '{id}' already exists.");
            }

            var manager = MapFeatureManager.Shared;
            if (manager == null)
            {
                throw new InvalidOperationException("MapFeatureManager was not found.");
            }

            var gameObject = new GameObject(id);
            gameObject.transform.SetParent(manager.transform, false);
            var feature = gameObject.AddComponent<MapFeature>();
            feature.identifier = id;
            ApplyMapFeatureDefinition(feature, definition);
            FuseMapFeatureRuntimeIndex.Instance.Set(id, feature);
            RefreshMapFeatureManager(manager);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.MapFeature, id, definition);
            return feature;
        }

        public static void UpdateMapFeature(string id, FuseMapFeature definition)
        {
            var feature = RequireMapFeature(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyMapFeatureDefinition(feature, definition);
            FuseMapFeatureRuntimeIndex.Instance.Set(id, feature);
            if (MapFeatureManager.Shared != null)
            {
                RefreshMapFeatureManager(MapFeatureManager.Shared);
            }
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.MapFeature, id, definition);
        }

        public static void RemoveMapFeature(string id)
        {
            var feature = RequireMapFeature(id);
            feature.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(feature.gameObject);
            FuseMapFeatureRuntimeIndex.Instance.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.MapFeature, id);
            if (MapFeatureManager.Shared != null)
            {
                RefreshMapFeatureManager(MapFeatureManager.Shared);
            }
        }

        public static MapFeature GetMapFeature(string id)
        {
            if (FuseMapFeatureRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (MapFeature)cached;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<MapFeature>(true).FirstOrDefault(feature =>
                    string.Equals(feature.identifier, id, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        public static IEnumerable<MapFeature> GetAllMapFeatures()
        {
            return UnityEngine.Object.FindObjectsOfType<MapFeature>(true);
        }

        internal static bool TryGetMapFeatureEnabledState(MapFeature feature, out bool enabled)
        {
            enabled = false;
            if (feature == null || string.IsNullOrWhiteSpace(feature.identifier))
            {
                return false;
            }

            var manager = MapFeatureManager.Shared;
            if (manager == null)
            {
                return false;
            }

            enabled = IsFeatureEnabled(feature, ReadFeatureEnables(manager));
            return true;
        }

        public static FuseMapFeature GetMapFeatureDefinition(string id)
        {
            return GetDefinition(GetMapFeature(id));
        }

        public static FuseMapFeature GetDefinition(MapFeature feature)
        {
            if (feature == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.MapFeature, feature.identifier, out FuseMapFeature definition);
            definition = definition ?? new FuseMapFeature();
            definition.DisplayName = feature.displayName;
            definition.Description = feature.description;
            definition.InitiallyEnabled = feature.defaultEnableInSandbox;
            // When we serialize a live feature back out as a FuseMapFeature
            // we emit the replacement-set shape (FromSet), because the snapshot
            // is meant to round-trip the EXACT current state of the runtime
            // feature, not a per-id patch on top of something else.
            definition.GroupIds = FuseStringPatch.FromSet(feature.trackGroupsEnableOnUnlock ?? feature.trackGroupsAvailableOnUnlock);
            definition.PrerequisiteFeatureIds = FuseStringPatch.FromSet(ToFeatureIds(feature.prerequisites));
            definition.TrackGroupsEnableOnUnlock = FuseStringPatch.FromSet(feature.trackGroupsEnableOnUnlock);
            definition.TrackGroupsAvailableOnUnlock = FuseStringPatch.FromSet(feature.trackGroupsAvailableOnUnlock);
            definition.AreasEnableOnUnlock = FuseStringPatch.FromSet(ToAreaIds(feature.areasEnableOnUnlock));
            definition.GameObjectsEnableOnUnlock = FuseStringPatch.FromSet(ToGameObjectPaths(feature.gameObjectsEnableOnUnlock));
            definition.UnlockIncludeIndustries = FuseStringPatch.FromSet(ToIndustryIds(feature.unlockIncludeIndustries));
            definition.UnlockExcludeIndustries = FuseStringPatch.FromSet(ToIndustryIds(feature.unlockExcludeIndustries));
            definition.UnlockIncludeIndustryComponents = FuseStringPatch.FromSet(ToIndustryComponentIds(feature.unlockIncludeIndustryComponents));
            return definition;
        }

        public static Progression AddProgression(string id, FuseProgression definition)
        {
            return AddProgression(id, definition, null);
        }

        internal static Progression AddProgression(string id, FuseProgression definition, string packageId)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetProgression(id) != null)
            {
                throw new InvalidOperationException($"Progression '{id}' already exists.");
            }

            var root = GameObject.Find("Progressions") ?? new GameObject("Progressions");
            var gameObject = new GameObject(id);
            gameObject.transform.SetParent(root.transform, false);
            var progression = gameObject.AddComponent<Progression>();
            progression.identifier = id;
            progression.mapFeatureManager = MapFeatureManager.Shared;

            ApplyProgressionDefinition(progression, definition, packageId);
            FuseProgressionRuntimeIndex.Instance.Set(id, progression);
            RefreshProgressionManager();
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Progression, id, definition);
            return progression;
        }

        public static void UpdateProgression(string id, FuseProgression definition)
        {
            UpdateProgression(id, definition, null);
        }

        internal static void UpdateProgression(string id, FuseProgression definition, string packageId)
        {
            var progression = RequireProgression(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyProgressionDefinition(progression, definition, packageId);
            FuseProgressionRuntimeIndex.Instance.Set(id, progression);
            RefreshProgressionManager();
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Progression, id, definition);
        }

        public static void RemoveProgression(string id)
        {
            var progression = RequireProgression(id);
            progression.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(progression.gameObject);
            FuseProgressionRuntimeIndex.Instance.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.Progression, id);
            RefreshProgressionManager();
        }

        public static Progression GetProgression(string id)
        {
            if (FuseProgressionRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (Progression)cached;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<Progression>(true).FirstOrDefault(progression =>
                    string.Equals(progression.identifier, id, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        public static IEnumerable<Progression> GetAllProgressions()
        {
            return UnityEngine.Object.FindObjectsOfType<Progression>(true);
        }

        public static FuseProgression GetProgressionDefinition(string id)
        {
            return GetDefinition(GetProgression(id));
        }

        public static FuseProgression GetDefinition(Progression progression)
        {
            if (progression == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.Progression, progression.identifier, out FuseProgression definition);
            definition = definition ?? new FuseProgression();
            definition.Sections = definition.Sections ?? new Dictionary<string, FuseSection>();

            // Snapshot the live start-feature list the same way the section
            // fields below are snapshotted: FromSet, because the snapshot is
            // the exact current runtime state, not a merge patch. Guarded so
            // a game build without the field simply leaves the cached value.
            if (ProgressionEnableFeaturesAtStartField != null)
            {
                var startFeatures = ProgressionEnableFeaturesAtStartField.GetValue(progression) as MapFeature[];
                definition.EnableFeaturesAtStart = FuseStringPatch.FromSet(ToFeatureIds(startFeatures));
            }

            var sections = ProgressionSectionsField?.GetValue(progression) as Section[] ??
                           progression.GetComponentsInChildren<Section>(true);
            foreach (var section in sections.Where(section => section != null && !string.IsNullOrWhiteSpace(section.identifier)))
            {
                definition.Sections.TryGetValue(section.identifier, out var existingSection);
                // Snapshot the live section back out to a FuseSection. The
                // sectional unlock-fan-out fields (industries/areas/...) are
                // not directly mirrored on the runtime Section object — they
                // live on the synthesized section-unlock MapFeature instead —
                // so we copy whatever the previous serialized definition had
                // for them through unchanged. Section-shaped fields
                // (prereqs, enableFeaturesOnUnlock, etc.) are reflected from
                // the runtime state via FromSet because the snapshot
                // describes the exact current state, not a merge patch.
                definition.Sections[section.identifier] = new FuseSection
                {
                    Id = section.identifier,
                    ProgressionId = progression.identifier,
                    DisplayName = section.displayName,
                    Description = section.description,
                    PrerequisiteSectionIds = FuseStringPatch.FromSet(ToSectionIds(section.prerequisiteSections)),
                    EnableFeaturesOnUnlock = FuseStringPatch.FromSet(ToFeatureIds(section.enableFeaturesOnUnlock)),
                    DisableFeaturesOnUnlock = FuseStringPatch.FromSet(ToFeatureIds(section.disableFeaturesOnUnlock)),
                    EnableFeaturesOnAvailable = FuseStringPatch.FromSet(ToFeatureIds(section.enableFeaturesOnAvailable)),
                    UnlockIncludeIndustries = existingSection?.UnlockIncludeIndustries,
                    UnlockExcludeIndustries = existingSection?.UnlockExcludeIndustries,
                    UnlockIncludeIndustryComponents = existingSection?.UnlockIncludeIndustryComponents,
                    AreasEnableOnUnlock = existingSection?.AreasEnableOnUnlock,
                    GameObjectsEnableOnUnlock = existingSection?.GameObjectsEnableOnUnlock,
                    TrackGroupsEnableOnUnlock = existingSection?.TrackGroupsEnableOnUnlock,
                    TrackGroupsAvailableOnUnlock = existingSection?.TrackGroupsAvailableOnUnlock,
                    InterchangeTransfers = ToInterchangeTransfers(section.InterchangeTransfers) ?? existingSection?.InterchangeTransfers,
                    DeliveryPhases = ToDeliveryPhases(section.deliveryPhases)
                };
            }

            return definition;
        }

        public static void SetFeatureEnabled(string id, bool enabled)
        {
            var manager = MapFeatureManager.Shared;
            if (manager == null)
            {
                throw new InvalidOperationException("MapFeatureManager was not found.");
            }

            manager.SetFeatureEnabled(id, enabled);
        }

        private static void RequireId(string id, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("ID is required.", parameterName);
            }
        }
    }
}
