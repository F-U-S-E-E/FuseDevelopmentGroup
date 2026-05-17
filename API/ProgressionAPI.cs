using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Progression;
using Game.State;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using FUSE.Cache;
using FUSE.Data;
using FUSE.Infrastructure;
using FUSE.Loading;
using Track;
using UnityEngine;

namespace FUSE.API
{
    public static class ProgressionAPI
    {
        private static readonly FieldInfo ManagerFeaturesField = typeof(MapFeatureManager).GetField("_features", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ManagerProgressionsField = typeof(ProgressionManager).GetField("_progressions", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ManagerCurrentProgressionField = typeof(ProgressionManager).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo ManagerFeatureEnablesProperty = typeof(MapFeatureManager).GetProperty("FeatureEnables", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ManagerHandleFeatureEnablesChangedMethod = typeof(MapFeatureManager).GetMethod("HandleFeatureEnablesChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ProgressionSectionsField = typeof(Progression).GetField("<Sections>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ProgressionUpdateSectionStatesMethod = typeof(Progression).GetMethod("UpdateSectionStates", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SectionInterchangeTransfersField = typeof(Section).GetField("<InterchangeTransfers>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo InterchangeTransferFromField = typeof(InterchangeTransfer).GetField("from", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo InterchangeTransferToField = typeof(InterchangeTransfer).GetField("to", BindingFlags.Instance | BindingFlags.NonPublic);
        private const string FuseInterchangeTransferPrefix = "FUSE Interchange Transfer ";
        private static readonly HashSet<string> PlaceholderMapFeatureIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            PlaceholderMapFeatureIds.Remove(id);
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
                ? UnityEngine.Object.FindObjectsOfType<MapFeature>(true).FirstOrDefault(feature => feature.identifier == id)
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
            definition.GroupIds = feature.trackGroupsEnableOnUnlock ?? feature.trackGroupsAvailableOnUnlock;
            definition.PrerequisiteFeatureIds = ToFeatureIds(feature.prerequisites);
            definition.TrackGroupsEnableOnUnlock = feature.trackGroupsEnableOnUnlock;
            definition.TrackGroupsAvailableOnUnlock = feature.trackGroupsAvailableOnUnlock;
            definition.AreasEnableOnUnlock = ToAreaIds(feature.areasEnableOnUnlock);
            definition.GameObjectsEnableOnUnlock = ToGameObjectPaths(feature.gameObjectsEnableOnUnlock);
            definition.UnlockIncludeIndustries = ToIndustryIds(feature.unlockIncludeIndustries);
            definition.UnlockExcludeIndustries = ToIndustryIds(feature.unlockExcludeIndustries);
            definition.UnlockIncludeIndustryComponents = ToIndustryComponentIds(feature.unlockIncludeIndustryComponents);
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
                ? UnityEngine.Object.FindObjectsOfType<Progression>(true).FirstOrDefault(progression => progression.identifier == id)
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

            var sections = ProgressionSectionsField?.GetValue(progression) as Section[] ??
                           progression.GetComponentsInChildren<Section>(true);
            foreach (var section in sections.Where(section => section != null && !string.IsNullOrWhiteSpace(section.identifier)))
            {
                definition.Sections.TryGetValue(section.identifier, out var existingSection);
                definition.Sections[section.identifier] = new FuseSection
                {
                    Id = section.identifier,
                    ProgressionId = progression.identifier,
                    DisplayName = section.displayName,
                    Description = section.description,
                    PrerequisiteSectionIds = ToSectionIds(section.prerequisiteSections),
                    EnableFeaturesOnUnlock = ToFeatureIds(section.enableFeaturesOnUnlock),
                    DisableFeaturesOnUnlock = ToFeatureIds(section.disableFeaturesOnUnlock),
                    EnableFeaturesOnAvailable = ToFeatureIds(section.enableFeaturesOnAvailable),
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

        public static void RefreshRuntimeStateAfterApply(string reason)
        {
            var manager = MapFeatureManager.Shared;
            if (manager == null)
            {
                FuseLog.Warning(
                    $"FUSE progression refresh skipped package='<all>' operation='refresh progression state' " +
                    $"kind='map feature manager' id='<shared>' reason='{reason ?? "unspecified"}' message='MapFeatureManager.Shared was not available'.");
                return;
            }

            RefreshMapFeatureManager(manager);
            RefreshProgressionManager();

            var invokedCurrentProgression = false;
            try
            {
                var progressionManager = UnityEngine.Object.FindObjectOfType<ProgressionManager>();
                var current = progressionManager != null
                    ? ManagerCurrentProgressionField?.GetValue(progressionManager) as Progression
                    : null;
                if (current != null && ProgressionUpdateSectionStatesMethod != null)
                {
                    ProgressionUpdateSectionStatesMethod.Invoke(current, Array.Empty<object>());
                    invokedCurrentProgression = true;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE progression refresh package='<all>' operation='refresh progression state' " +
                    $"kind='progression' id='<current>' reason='{reason ?? "unspecified"}' message='{ex.Message}'.");
            }

            var initialized = InitializeMissingMapFeatureStates(manager);
            var forcedFeatureState = ForceApplyCurrentMapFeatureState(manager, reason);
            var restoredTrackGroups = RestoreDisabledTrackGroups(manager, reason);
            FuseLog.Info(
                $"FUSE refreshed progression runtime state package='<all>' operation='refresh progression state' " +
                $"kind='map features' id='<all>' reason='{reason ?? "unspecified"}' " +
                $"currentProgressionRefreshed={invokedCurrentProgression} initializedFeatureStates={initialized} " +
                $"forcedFeatureState={forcedFeatureState} restoredDisabledTrackGroups={restoredTrackGroups}.");
        }

        private static void ApplyMapFeatureDefinition(MapFeature feature, FuseMapFeature definition)
        {
            feature.displayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? feature.identifier : definition.DisplayName;
            feature.description = definition.Description ?? string.Empty;
            feature.defaultEnableInSandbox = definition.InitiallyEnabled;
            feature.trackGroupsEnableOnUnlock = PreferExplicit(definition.TrackGroupsEnableOnUnlock, definition.GroupIds);
            feature.trackGroupsAvailableOnUnlock = PreferExplicit(definition.TrackGroupsAvailableOnUnlock, definition.GroupIds);
            feature.prerequisites = ResolveMapFeatures(definition.PrerequisiteFeatureIds);
            feature.gameObjectsEnableOnUnlock = ResolveGameObjects(definition.GameObjectsEnableOnUnlock);
            feature.areasEnableOnUnlock = ResolveAreas(definition.AreasEnableOnUnlock);
            feature.unlockExcludeIndustries = ResolveIndustries(definition.UnlockExcludeIndustries);
            feature.unlockIncludeIndustries = ResolveIndustries(definition.UnlockIncludeIndustries);
            feature.unlockIncludeIndustryComponents = ResolveIndustryComponents(definition.UnlockIncludeIndustryComponents);
            SanitizeMapFeature(feature);
        }

        private static void ApplyProgressionDefinition(Progression progression, FuseProgression definition, string packageId)
        {
            if (progression.mapFeatureManager == null)
            {
                progression.mapFeatureManager = MapFeatureManager.Shared;
            }

            var sectionDefinitions = definition.Sections ?? new Dictionary<string, FuseSection>();
            foreach (var sectionDefinition in sectionDefinitions)
            {
                var section = GetSection(sectionDefinition.Key);
                if (section == null || section.transform.parent != progression.transform)
                {
                    var gameObject = new GameObject(sectionDefinition.Key);
                    gameObject.transform.SetParent(progression.transform, false);
                    section = gameObject.AddComponent<Section>();
                    section.identifier = sectionDefinition.Key;
                }

                FuseSectionRuntimeIndex.Instance.Set(section.identifier, section);
            }

            foreach (var sectionDefinition in sectionDefinitions)
            {
                var section = GetSection(sectionDefinition.Key);
                if (section == null)
                {
                    throw new InvalidOperationException($"Progression section '{sectionDefinition.Key}' could not be created.");
                }

                ApplySectionDefinition(section, sectionDefinition.Value, packageId);
                FuseSectionRuntimeIndex.Instance.Set(section.identifier, section);
            }

            ProgressionSectionsField?.SetValue(progression, progression.GetComponentsInChildren<Section>());
        }

        private static void ApplySectionDefinition(Section section, FuseSection definition, string packageId)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            section.displayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? section.identifier : definition.DisplayName;
            section.description = definition.Description ?? string.Empty;
            var sectionUnlockFeature = EnsureSectionUnlockFeature(section, definition);

            section.prerequisiteSections = ResolveSections(definition.PrerequisiteSectionIds);
            section.enableFeaturesOnUnlock = AppendFeature(
                AppendFeature(
                    ResolveMapFeatures(definition.EnableFeaturesOnUnlock),
                    sectionUnlockFeature),
                GetPlaceholderMapFeature(section.identifier));
            section.enableFeaturesOnAvailable = ResolveMapFeatures(definition.EnableFeaturesOnAvailable);
            section.disableFeaturesOnUnlock = ResolveMapFeatures(definition.DisableFeaturesOnUnlock);
            section.deliveryPhases = (definition.DeliveryPhases ?? Array.Empty<FuseDeliveryPhase>()).Select(CreateDeliveryPhase).ToArray();
            ApplyInterchangeTransfers(section, definition.InterchangeTransfers, packageId);
        }

        private static void ApplyInterchangeTransfers(Section section, IDictionary<string, string> transfers, string packageId)
        {
            var preserved = (section.GetComponentsInChildren<InterchangeTransfer>(true) ?? Array.Empty<InterchangeTransfer>())
                .Where(transfer => transfer != null && !IsFuseInterchangeTransfer(transfer))
                .ToList();

            foreach (var transfer in section.GetComponentsInChildren<InterchangeTransfer>(true) ?? Array.Empty<InterchangeTransfer>())
            {
                if (transfer == null || !IsFuseInterchangeTransfer(transfer))
                {
                    continue;
                }

                UnityEngine.Object.Destroy(transfer.gameObject);
            }

            var created = new List<InterchangeTransfer>();
            if (transfers != null && transfers.Count > 0)
            {
                foreach (var transfer in transfers)
                {
                    if (string.IsNullOrWhiteSpace(transfer.Key))
                    {
                        FuseLoadReport.RecordProgressionTransferSkip(
                            packageId,
                            section.identifier,
                            transfer.Key,
                            transfer.Value,
                            "blank source id");
                        FuseLog.Warning(
                            $"FUSE progression transfer skipped package='{packageId ?? string.Empty}' " +
                            $"operation='apply progression' phase='interchange transfers' kind='interchange transfer' " +
                            $"id='{section.identifier ?? string.Empty}' source='{transfer.Key ?? string.Empty}' " +
                            $"target='{transfer.Value ?? string.Empty}' reason='blank source id'.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(transfer.Value))
                    {
                        FuseLoadReport.RecordProgressionTransferSkip(
                            packageId,
                            section.identifier,
                            transfer.Key,
                            transfer.Value,
                            "blank target id");
                        FuseLog.Warning(
                            $"FUSE progression transfer skipped package='{packageId ?? string.Empty}' " +
                            $"operation='apply progression' phase='interchange transfers' kind='interchange transfer' " +
                            $"id='{section.identifier ?? string.Empty}' source='{transfer.Key ?? string.Empty}' " +
                            $"target='{transfer.Value ?? string.Empty}' reason='blank target id'.");
                        continue;
                    }

                    var from = ResolveInterchange(transfer.Key);
                    var to = ResolveInterchange(transfer.Value);
                    if (from == null || to == null)
                    {
                        FuseLoadReport.RecordProgressionTransferSkip(
                            packageId,
                            section.identifier,
                            transfer.Key,
                            transfer.Value,
                            "one or both interchange components were not found");
                        FuseLog.Warning(
                            $"FUSE progression transfer skipped package='{packageId ?? string.Empty}' " +
                            $"operation='apply progression' phase='interchange transfers' kind='interchange transfer' " +
                            $"id='{section.identifier ?? string.Empty}' source='{transfer.Key ?? string.Empty}' " +
                            $"target='{transfer.Value ?? string.Empty}' reason='one or both interchange components were not found'.");
                        continue;
                    }

                    if (InterchangeTransferFromField == null || InterchangeTransferToField == null)
                    {
                        FuseLoadReport.RecordProgressionTransferSkip(
                            packageId,
                            section.identifier,
                            transfer.Key,
                            transfer.Value,
                            "base game fields were not found");
                        FuseLog.Warning(
                            $"FUSE progression transfer skipped package='{packageId ?? string.Empty}' " +
                            $"operation='apply progression' phase='interchange transfers' kind='interchange transfer' " +
                            $"id='{section.identifier ?? string.Empty}' source='{transfer.Key ?? string.Empty}' " +
                            $"target='{transfer.Value ?? string.Empty}' reason='base game fields were not found'.");
                        continue;
                    }

                    var gameObject = new GameObject(FuseInterchangeTransferPrefix + SanitizeObjectName(transfer.Key));
                    gameObject.transform.SetParent(section.transform, false);
                    var component = gameObject.AddComponent<InterchangeTransfer>();
                    InterchangeTransferFromField.SetValue(component, from);
                    InterchangeTransferToField.SetValue(component, to);
                    created.Add(component);
                    FuseLog.Info($"FUSE progression section '{section.identifier}' added interchange transfer '{transfer.Key}' -> '{transfer.Value}'.");
                }
            }

            RefreshSectionInterchangeTransfers(section, preserved.Concat(created).ToArray());
        }

        private static bool IsFuseInterchangeTransfer(InterchangeTransfer transfer)
        {
            return transfer != null &&
                   transfer.gameObject != null &&
                   transfer.gameObject.name.StartsWith(FuseInterchangeTransferPrefix, StringComparison.Ordinal);
        }

        private static void RefreshSectionInterchangeTransfers(Section section, InterchangeTransfer[] transfers)
        {
            if (section == null)
            {
                return;
            }

            SectionInterchangeTransfersField?.SetValue(section, transfers ?? Array.Empty<InterchangeTransfer>());
        }

        private static string SanitizeObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unnamed";
            }

            var chars = value.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-' ? ch : '_').ToArray();
            return new string(chars);
        }

        private static MapFeature EnsureSectionUnlockFeature(Section section, FuseSection definition)
        {
            if (section == null || definition == null || !HasSectionUnlockFeaturePayload(definition))
            {
                return null;
            }

            var featureId = GetSectionUnlockFeatureId(section.identifier);
            var featureDefinition = new FuseMapFeature
            {
                DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? section.identifier : definition.DisplayName,
                Description = definition.Description,
                InitiallyEnabled = false,
                TrackGroupsEnableOnUnlock = definition.TrackGroupsEnableOnUnlock,
                TrackGroupsAvailableOnUnlock = definition.TrackGroupsAvailableOnUnlock,
                AreasEnableOnUnlock = definition.AreasEnableOnUnlock,
                GameObjectsEnableOnUnlock = definition.GameObjectsEnableOnUnlock,
                UnlockIncludeIndustries = definition.UnlockIncludeIndustries,
                UnlockExcludeIndustries = definition.UnlockExcludeIndustries,
                UnlockIncludeIndustryComponents = definition.UnlockIncludeIndustryComponents
            };

            var existing = GetMapFeature(featureId);
            if (existing != null)
            {
                ApplyMapFeatureDefinition(existing, featureDefinition);
                FuseMapFeatureRuntimeIndex.Instance.Set(featureId, existing);
                if (MapFeatureManager.Shared != null)
                {
                    RefreshMapFeatureManager(MapFeatureManager.Shared);
                }

                FuseApiPersistence.RecordDefinition(FuseDefinitionKind.MapFeature, featureId, featureDefinition);
                FuseLog.Info($"FUSE refreshed progression section unlock feature '{featureId}' for section '{section.identifier}'.");
                return existing;
            }

            var created = AddMapFeature(featureId, featureDefinition);
            FuseLog.Info($"FUSE created progression section unlock feature '{featureId}' for section '{section.identifier}'.");
            return created;
        }

        private static bool HasSectionUnlockFeaturePayload(FuseSection definition)
        {
            return HasAny(definition.TrackGroupsEnableOnUnlock) ||
                   HasAny(definition.TrackGroupsAvailableOnUnlock) ||
                   HasAny(definition.AreasEnableOnUnlock) ||
                   HasAny(definition.GameObjectsEnableOnUnlock) ||
                   HasAny(definition.UnlockIncludeIndustries) ||
                   HasAny(definition.UnlockExcludeIndustries) ||
                   HasAny(definition.UnlockIncludeIndustryComponents);
        }

        private static string GetSectionUnlockFeatureId(string sectionId)
        {
            return "fuse.progression.section." + (sectionId ?? string.Empty) + ".unlock";
        }

        private static MapFeature[] AppendFeature(MapFeature[] features, MapFeature feature)
        {
            if (feature == null)
            {
                return features ?? Array.Empty<MapFeature>();
            }

            return (features ?? Array.Empty<MapFeature>())
                .Concat(new[] { feature })
                .Where(candidate => candidate != null)
                .GroupBy(candidate => candidate.identifier ?? candidate.name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }

        private static Section.DeliveryPhase CreateDeliveryPhase(FuseDeliveryPhase definition)
        {
            var deliveries = definition.Deliveries ?? Array.Empty<FuseDelivery>();
            var phase = new Section.DeliveryPhase
            {
                cost = definition.Cost,
                deliveries = deliveries.Select(CreateDelivery).ToArray()
            };

            if (deliveries.Length > 0)
            {
                phase.industryComponent = !string.IsNullOrWhiteSpace(definition.IndustryComponentId)
                    ? ResolveIndustryComponent(definition.IndustryComponentId)
                    : ResolveDeliveryPhaseIndustryComponent(definition);
            }

            return phase;
        }

        private static Section.Delivery CreateDelivery(FuseDelivery definition)
        {
            return new Section.Delivery
            {
                carTypeFilter = new CarTypeFilter(definition.CarTypeFilter ?? string.Empty),
                count = definition.Count,
                load = ResolveLoad(definition.LoadId),
                direction = ParseDeliveryDirection(definition.Direction)
            };
        }

        private static ProgressionIndustryComponent ResolveDeliveryPhaseIndustryComponent(FuseDeliveryPhase definition)
        {
            var destinationIds = (definition.Deliveries ?? Array.Empty<FuseDelivery>())
                .Select(delivery => delivery?.DestinationIndustryId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (destinationIds.Length != 1)
            {
                throw new InvalidOperationException("Progression delivery phases with deliveries require industryComponentId, or a single destinationIndustryId that resolves to one ProgressionIndustryComponent.");
            }

            var industry = ResolveIndustry(destinationIds[0]);
            if (industry == null)
            {
                throw new InvalidOperationException($"Progression delivery destination industry '{destinationIds[0]}' was not found.");
            }

            var candidates = industry.GetComponentsInChildren<ProgressionIndustryComponent>(true)
                .Where(component => component != null)
                .ToArray();
            if (candidates.Length == 1)
            {
                return candidates[0];
            }

            if (candidates.Length == 0)
            {
                throw new InvalidOperationException($"Progression delivery destination industry '{destinationIds[0]}' has no ProgressionIndustryComponent. Set industryComponentId explicitly.");
            }

            throw new InvalidOperationException($"Progression delivery destination industry '{destinationIds[0]}' has {candidates.Length} ProgressionIndustryComponent entries. Set industryComponentId explicitly.");
        }

        private static Section.Delivery.Direction ParseDeliveryDirection(string direction)
        {
            if (string.IsNullOrWhiteSpace(direction))
            {
                return Section.Delivery.Direction.LoadToIndustry;
            }

            switch (direction.Trim().ToLowerInvariant())
            {
                case "1":
                case "loadfromindustry":
                case "fromindustry":
                case "from":
                case "export":
                    return Section.Delivery.Direction.LoadFromIndustry;
                case "0":
                case "loadtoindustry":
                case "toindustry":
                case "to":
                case "import":
                    return Section.Delivery.Direction.LoadToIndustry;
                default:
                    return Section.Delivery.Direction.LoadToIndustry;
            }
        }

        private static Section[] ResolveSections(string[] ids)
        {
            return ResolveObjects(ids, GetSection, "section");
        }

        private static MapFeature[] ResolveMapFeatures(string[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                return Array.Empty<MapFeature>();
            }

            var resolved = new List<MapFeature>();
            foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                var feature = GetMapFeature(id) ?? EnsurePlaceholderMapFeature(id);
                if (feature == null)
                {
                    FuseLog.Warning($"FUSE progression skipped unresolved map feature reference '{id}'.");
                    continue;
                }

                resolved.Add(feature);
            }

            return resolved
                .GroupBy(feature => feature.identifier ?? feature.name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }

        private static MapFeature GetPlaceholderMapFeature(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !PlaceholderMapFeatureIds.Contains(id))
            {
                return null;
            }

            return GetMapFeature(id);
        }

        private static MapFeature EnsurePlaceholderMapFeature(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            var existing = GetMapFeature(id);
            if (existing != null)
            {
                return existing;
            }

            try
            {
                var feature = AddMapFeature(id, new FuseMapFeature
                {
                    DisplayName = id,
                    Description = "FUSE placeholder for a legacy forward reference. A later package or progression section may replace or enable this feature.",
                    InitiallyEnabled = false
                });

                PlaceholderMapFeatureIds.Add(id);
                FuseLog.Info($"FUSE created placeholder map feature '{id}' for a legacy forward reference. If a later definition or matching progression section exists, it will bind normally.");
                return feature;
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE could not create placeholder map feature '{id}' for a legacy forward reference: {ex.Message}");
                return null;
            }
        }

        private static Area[] ResolveAreas(string[] ids)
        {
            return ResolveObjects(ids, ResolveArea, "area");
        }

        private static Industry[] ResolveIndustries(string[] ids)
        {
            return ResolveObjects(ids, ResolveIndustry, "industry");
        }

        private static IndustryComponent[] ResolveIndustryComponents(string[] ids)
        {
            return ResolveObjects(ids, ResolveAnyIndustryComponent, "industry component");
        }

        private static GameObject[] ResolveGameObjects(string[] paths)
        {
            return ResolveOptionalObjects(paths, ResolveGameObject, "game object");
        }

        private static T[] ResolveObjects<T>(string[] ids, Func<string, T> resolver, string label)
            where T : class
        {
            if (ids == null || ids.Length == 0)
            {
                return Array.Empty<T>();
            }

            var resolved = new List<T>();
            foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                var value = resolver(id);
                if (value == null)
                {
                    FuseLog.Warning($"FUSE progression skipped unresolved {label} reference '{id}'.");
                    continue;
                }

                resolved.Add(value);
            }

            return resolved.ToArray();
        }

        private static T[] ResolveOptionalObjects<T>(string[] ids, Func<string, T> resolver, string label)
            where T : class
        {
            if (ids == null || ids.Length == 0)
            {
                return Array.Empty<T>();
            }

            var resolved = new List<T>();
            foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                var value = resolver(id);
                if (value == null)
                {
                    FuseLog.Warning($"FUSE progression skipped unresolved optional {label} reference '{id}'.");
                    continue;
                }

                resolved.Add(value);
            }

            return resolved.ToArray();
        }

        private static Area ResolveArea(string id)
        {
            var area = TrackAPI.GetArea(id);
            if (area != null)
            {
                return area;
            }

            return UnityEngine.Object.FindObjectsOfType<Area>(true).FirstOrDefault(candidate =>
                candidate != null &&
                (string.Equals(candidate.identifier, id, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(candidate.name, id, StringComparison.OrdinalIgnoreCase)));
        }

        private static Industry ResolveIndustry(string id)
        {
            var industry = IndustryAPI.GetIndustry(id);
            if (industry != null)
            {
                return industry;
            }

            return UnityEngine.Object.FindObjectsOfType<Industry>(true).FirstOrDefault(candidate =>
                candidate != null &&
                (string.Equals(candidate.identifier, id, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(candidate.name, id, StringComparison.OrdinalIgnoreCase)));
        }

        private static IndustryComponent ResolveAnyIndustryComponent(string id)
        {
            if (FuseIndustryComponentRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return cached as IndustryComponent;
            }

            return UnityEngine.Object.FindObjectsOfType<IndustryComponent>(true)
                .FirstOrDefault(component => ComponentMatchesId(component, id));
        }

        private static Interchange ResolveInterchange(string id)
        {
            var cached = ResolveAnyIndustryComponent(id) as Interchange;
            if (cached != null)
            {
                return cached;
            }

            var sceneMatch = UnityEngine.Object.FindObjectsOfType<Interchange>(true)
                .FirstOrDefault(component => ComponentMatchesId(component, id));
            if (sceneMatch != null)
            {
                return sceneMatch;
            }

            var industryMatch = ResolveInterchangeFromLegacyIndustryComponentId(id);
            if (industryMatch != null)
            {
                return industryMatch;
            }

            return null;
        }

        private static Interchange ResolveInterchangeFromLegacyIndustryComponentId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            var dot = id.LastIndexOf('.');
            if (dot <= 0 || dot >= id.Length - 1)
            {
                return null;
            }

            var industryId = id.Substring(0, dot);
            var legacySubId = id.Substring(dot + 1);
            var industry = ResolveIndustry(industryId);
            if (industry == null)
            {
                return null;
            }

            var interchanges = industry.GetComponentsInChildren<Interchange>(true)
                .Where(component => component != null)
                .ToArray();
            if (interchanges.Length == 0)
            {
                return null;
            }

            var exactSubId = interchanges.FirstOrDefault(component =>
                string.Equals(component.subIdentifier, legacySubId, StringComparison.OrdinalIgnoreCase));
            if (exactSubId != null)
            {
                return exactSubId;
            }

            var canonicalSubId = interchanges.FirstOrDefault(component =>
                string.Equals(component.subIdentifier, "interchange", StringComparison.OrdinalIgnoreCase));
            if (canonicalSubId != null &&
                (string.Equals(legacySubId, "t1", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(legacySubId, "interchange", StringComparison.OrdinalIgnoreCase)))
            {
                FuseLog.Info($"FUSE resolved legacy interchange transfer id '{id}' to '{industry.identifier}.{canonicalSubId.subIdentifier}'.");
                return canonicalSubId;
            }

            if (interchanges.Length == 1)
            {
                FuseLog.Info($"FUSE resolved legacy interchange transfer id '{id}' to only interchange component '{industry.identifier}.{interchanges[0].subIdentifier}'.");
                return interchanges[0];
            }

            return null;
        }

        private static GameObject ResolveGameObject(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var marker = path.IndexOf("://", StringComparison.Ordinal);
            if (marker >= 0)
            {
                var scheme = path.Substring(0, marker);
                var value = path.Substring(marker + 3);
                if (string.Equals(scheme, "scenery", StringComparison.OrdinalIgnoreCase))
                {
                    var scenery = SceneryAPI.GetScenery(value);
                    if (scenery != null)
                    {
                        return scenery.gameObject;
                    }

                    return ResolveGameObjectPath(value);
                }

                if (string.Equals(scheme, "sceneClone", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scheme, "sceneclone", StringComparison.OrdinalIgnoreCase))
                {
                    return SceneCloneAPI.GetSceneClone(value) ?? ResolveGameObjectPath(value);
                }

                if (string.Equals(scheme, "path", StringComparison.OrdinalIgnoreCase))
                {
                    const string scenePrefix = "scene/";
                    if (value.StartsWith(scenePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        value = value.Substring(scenePrefix.Length);
                    }

                    return ResolveGameObjectPath(value) ?? ResolveAuthoredWorldObject(value);
                }

                if (string.Equals(scheme, "scene", StringComparison.OrdinalIgnoreCase))
                {
                    return ResolveGameObjectPath(value) ?? ResolveAuthoredWorldObject(value);
                }
            }

            return ResolveGameObjectPath(path) ?? ResolveAuthoredWorldObject(path);
        }

        private static GameObject ResolveAuthoredWorldObject(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var id = value.Trim();
            var scenery = SceneryAPI.GetScenery(id) ?? SceneryAPI.GetScenery(GetPathLeaf(id));
            if (scenery != null)
            {
                return scenery.gameObject;
            }

            return SceneCloneAPI.GetSceneClone(id) ?? SceneCloneAPI.GetSceneClone(GetPathLeaf(id));
        }

        private static string GetPathLeaf(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var normalized = value.Trim().Replace('\\', '/');
            var slash = normalized.LastIndexOf('/');
            return slash >= 0 && slash < normalized.Length - 1
                ? normalized.Substring(slash + 1)
                : normalized;
        }

        private static GameObject ResolveGameObjectPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var direct = GameObject.Find(path);
            if (direct != null)
            {
                return direct;
            }

            var resolved = FusePrefabResolver.ResolveScenePath(path);
            if (resolved != null)
            {
                return resolved;
            }

            return UnityEngine.Object.FindObjectsOfType<Transform>(true)
                .FirstOrDefault(transform =>
                    string.Equals(transform.name, path, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(GetScenePath(transform), path, StringComparison.OrdinalIgnoreCase))
                ?.gameObject;
        }

        private static bool ComponentMatchesId(IndustryComponent component, string id)
        {
            if (component == null || string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            try
            {
                if (string.Equals(component.Identifier, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (LooseIdEquals(component.Identifier, id))
                {
                    return true;
                }
            }
            catch
            {
                // Some freshly cloned components have incomplete parent identity.
            }

            var industry = component.GetComponentInParent<Industry>(true);
            if (industry == null ||
                string.IsNullOrWhiteSpace(industry.identifier) ||
                string.IsNullOrWhiteSpace(component.subIdentifier))
            {
                return false;
            }

            var fullId = industry.identifier + "." + component.subIdentifier;
            return string.Equals(fullId, id, StringComparison.OrdinalIgnoreCase) ||
                   LooseIdEquals(fullId, id);
        }

        private static bool LooseIdEquals(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(NormalizeLooseId(left), NormalizeLooseId(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeLooseId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return string.Empty;
            }

            return new string(id
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private static string[] ToSectionIds(IEnumerable<Section> sections)
        {
            return sections?.Where(section => section != null && !string.IsNullOrWhiteSpace(section.identifier))
                .Select(section => section.identifier)
                .ToArray();
        }

        private static string[] ToFeatureIds(IEnumerable<MapFeature> features)
        {
            return features?.Where(feature => feature != null && !string.IsNullOrWhiteSpace(feature.identifier))
                .Select(feature => feature.identifier)
                .ToArray();
        }

        private static string[] ToAreaIds(IEnumerable<Area> areas)
        {
            return areas?.Where(area => area != null && !string.IsNullOrWhiteSpace(area.identifier))
                .Select(area => area.identifier)
                .ToArray();
        }

        private static string[] ToIndustryIds(IEnumerable<Industry> industries)
        {
            return industries?.Where(industry => industry != null && !string.IsNullOrWhiteSpace(industry.identifier))
                .Select(industry => industry.identifier)
                .ToArray();
        }

        private static string[] ToIndustryComponentIds(IEnumerable<IndustryComponent> components)
        {
            return components?.Where(component => component != null)
                .Select(SafeIndustryComponentId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();
        }

        private static string[] ToGameObjectPaths(IEnumerable<GameObject> gameObjects)
        {
            return gameObjects?.Where(gameObject => gameObject != null)
                .Select(gameObject => GetScenePath(gameObject.transform))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
        }

        private static Dictionary<string, string> ToInterchangeTransfers(IEnumerable<InterchangeTransfer> transfers)
        {
            if (transfers == null)
            {
                return null;
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var transfer in transfers.Where(transfer => transfer != null))
            {
                var from = InterchangeTransferFromField?.GetValue(transfer) as Interchange;
                if (from == null)
                {
                    continue;
                }

                var fromId = SafeIndustryComponentId(from);
                if (string.IsNullOrWhiteSpace(fromId))
                {
                    continue;
                }

                var to = InterchangeTransferToField?.GetValue(transfer) as Interchange;
                result[fromId] = to != null ? SafeIndustryComponentId(to) : null;
            }

            return result.Count > 0 ? result : null;
        }

        private static FuseDeliveryPhase[] ToDeliveryPhases(IEnumerable<Section.DeliveryPhase> phases)
        {
            return phases?.Where(phase => phase != null)
                .Select(phase => new FuseDeliveryPhase
                {
                    Cost = phase.cost,
                    IndustryComponentId = phase.industryComponent != null ? phase.industryComponent.Identifier : null,
                    Deliveries = ToDeliveries(phase.deliveries)
                })
                .ToArray();
        }

        private static FuseDelivery[] ToDeliveries(IEnumerable<Section.Delivery> deliveries)
        {
            return deliveries?.Where(delivery => delivery != null)
                .Select(delivery => new FuseDelivery
                {
                    CarTypeFilter = delivery.carTypeFilter.ToString(),
                    LoadId = delivery.load != null ? delivery.load.id : null,
                    Count = delivery.count,
                    Direction = delivery.direction == Section.Delivery.Direction.LoadFromIndustry ? "loadFromIndustry" : "loadToIndustry"
                })
                .ToArray();
        }

        private static Section GetSection(string id)
        {
            if (FuseSectionRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (Section)cached;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<Section>(true).FirstOrDefault(section => section.identifier == id)
                : null;
        }

        private static ProgressionIndustryComponent ResolveIndustryComponent(string id)
        {
            if (!FuseIndustryComponentRuntimeIndex.Instance.TryGetValue(id, out var cached) || cached == null)
            {
                cached = UnityEngine.Object.FindObjectsOfType<IndustryComponent>(true)
                    .FirstOrDefault(component => ComponentMatchesId(component, id));
            }

            var component = cached as ProgressionIndustryComponent;
            if (component == null)
            {
                component = ResolveProgressionIndustryComponentFromIndustry(id);
            }

            if (component == null)
            {
                throw new InvalidOperationException($"Progression industry component '{id}' was not found.");
            }

            FuseIndustryComponentRuntimeIndex.Instance.Set(id, component);
            return component;
        }

        private static ProgressionIndustryComponent ResolveProgressionIndustryComponentFromIndustry(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            var splitIndex = id.LastIndexOf('.');
            if (splitIndex <= 0 || splitIndex >= id.Length - 1)
            {
                return null;
            }

            var industryId = id.Substring(0, splitIndex);
            var componentId = id.Substring(splitIndex + 1);
            var industry = IndustryAPI.GetIndustry(industryId);
            if (industry == null)
            {
                return null;
            }

            return industry.GetComponentsInChildren<ProgressionIndustryComponent>(true)
                .FirstOrDefault(component =>
                    component != null &&
                    (string.Equals(component.subIdentifier, componentId, StringComparison.OrdinalIgnoreCase) ||
                     LooseIdEquals(component.subIdentifier, componentId) ||
                     ComponentMatchesId(component, id)));
        }

        private static Load ResolveLoad(string loadId)
        {
            if (string.IsNullOrWhiteSpace(loadId))
            {
                return null;
            }

            var load = LoadAPI.GetLoad(loadId) ??
                       LoadAPI.GetOrCreatePlaceholderLoad(loadId, "progression delivery references a load id that is not defined by any loaded package");
            if (load == null)
            {
                throw new InvalidOperationException($"Load '{loadId}' was not found.");
            }

            FuseLoadRuntimeIndex.Instance.Set(load.id, load);
            return load;
        }

        private static string[] PreferExplicit(string[] explicitValues, string[] fallbackValues)
        {
            return HasAny(explicitValues) ? explicitValues : (fallbackValues ?? Array.Empty<string>());
        }

        private static bool HasAny(string[] values)
        {
            return values != null && values.Any(value => !string.IsNullOrWhiteSpace(value));
        }

        private static string SafeIndustryComponentId(IndustryComponent component)
        {
            if (component == null)
            {
                return null;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(component.Identifier))
                {
                    return component.Identifier;
                }
            }
            catch
            {
                // Incomplete cloned components can throw while their parent industry identity is being rebuilt.
            }

            var industry = component.GetComponentInParent<Industry>(true);
            return industry != null &&
                   !string.IsNullOrWhiteSpace(industry.identifier) &&
                   !string.IsNullOrWhiteSpace(component.subIdentifier)
                ? industry.identifier + "." + component.subIdentifier
                : null;
        }

        private static string GetScenePath(Transform transform)
        {
            if (transform == null)
            {
                return null;
            }

            var names = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }

        private static MapFeature RequireMapFeature(string id)
        {
            var feature = GetMapFeature(id);
            if (feature == null)
            {
                throw new InvalidOperationException($"Map feature '{id}' was not found.");
            }

            return feature;
        }

        private static Progression RequireProgression(string id)
        {
            var progression = GetProgression(id);
            if (progression == null)
            {
                throw new InvalidOperationException($"Progression '{id}' was not found.");
            }

            return progression;
        }

        private static void RefreshMapFeatureManager(MapFeatureManager manager)
        {
            var features = manager.GetComponentsInChildren<MapFeature>();
            foreach (var feature in features)
            {
                SanitizeMapFeature(feature);
            }

            ManagerFeaturesField?.SetValue(manager, features);
        }

        private static int InitializeMissingMapFeatureStates(MapFeatureManager manager)
        {
            if (manager == null)
            {
                return 0;
            }

            var features = manager.AvailableFeatures
                .Where(feature => feature != null && !string.IsNullOrWhiteSpace(feature.identifier))
                .ToArray();
            if (features.Length == 0)
            {
                return 0;
            }

            var existing = ReadFeatureEnables(manager);
            var defaults = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var feature in features)
            {
                if (existing.ContainsKey(feature.identifier))
                {
                    continue;
                }

                defaults[feature.identifier] = feature.defaultEnableInSandbox && StateManager.IsSandbox;
            }

            if (defaults.Count == 0)
            {
                return 0;
            }

            try
            {
                manager.SetFeatureEnables(defaults);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE progression refresh package='<all>' operation='initialize map feature state' " +
                    $"kind='map features' id='<all>' message='{ex.Message}'.");
                return 0;
            }

            return defaults.Count;
        }

        private static bool ForceApplyCurrentMapFeatureState(MapFeatureManager manager, string reason)
        {
            if (manager == null || ManagerHandleFeatureEnablesChangedMethod == null)
            {
                return false;
            }

            try
            {
                var current = ReadFeatureEnables(manager);
                ManagerHandleFeatureEnablesChangedMethod.Invoke(
                    manager,
                    new object[]
                    {
                        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                        current,
                        true
                    });
                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE progression refresh package='<all>' operation='force apply map feature state' " +
                    $"kind='map features' id='<all>' reason='{reason ?? "unspecified"}' message='{ex.Message}'.");
                return false;
            }
        }

        private static int RestoreDisabledTrackGroups(MapFeatureManager manager, string reason)
        {
            var graph = Graph.Shared;
            if (manager == null || graph == null)
            {
                return 0;
            }

            var states = ReadFeatureEnables(manager);
            var enabledGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var disabledGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var feature in manager.AvailableFeatures ?? Enumerable.Empty<MapFeature>())
            {
                if (feature == null || string.IsNullOrWhiteSpace(feature.identifier))
                {
                    continue;
                }

                var groups = FeatureTrackGroups(feature).ToArray();
                if (groups.Length == 0)
                {
                    continue;
                }

                var enabled = IsFeatureEnabled(feature, states);
                var target = enabled ? enabledGroups : disabledGroups;
                foreach (var group in groups)
                {
                    target.Add(group);
                }
            }

            disabledGroups.ExceptWith(enabledGroups);

            var changed = 0;
            foreach (var group in disabledGroups)
            {
                try
                {
                    if (graph.SetGroupEnabled(group, false))
                    {
                        changed++;
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE progression refresh package='<all>' operation='restore locked track groups' " +
                        $"kind='track group' id='{group}' reason='{reason ?? "unspecified"}' message='{ex.Message}'.");
                }
            }

            return changed;
        }

        private static bool IsFeatureEnabled(MapFeature feature, IDictionary<string, bool> states)
        {
            if (feature == null || string.IsNullOrWhiteSpace(feature.identifier))
            {
                return false;
            }

            return states != null && states.TryGetValue(feature.identifier, out var enabled)
                ? enabled
                : feature.defaultEnableInSandbox && StateManager.IsSandbox;
        }

        private static IEnumerable<string> FeatureTrackGroups(MapFeature feature)
        {
            if (feature == null)
            {
                yield break;
            }

            foreach (var group in feature.trackGroupsEnableOnUnlock ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(group))
                {
                    yield return group;
                }
            }

            foreach (var group in feature.trackGroupsAvailableOnUnlock ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(group))
                {
                    yield return group;
                }
            }
        }

        private static Dictionary<string, bool> ReadFeatureEnables(MapFeatureManager manager)
        {
            if (manager == null || ManagerFeatureEnablesProperty == null)
            {
                return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                return ManagerFeatureEnablesProperty.GetValue(manager, null) as Dictionary<string, bool> ??
                       new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE progression refresh package='<all>' operation='read map feature state' " +
                    $"kind='map features' id='<all>' message='{ex.Message}'.");
                return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void SanitizeMapFeature(MapFeature feature)
        {
            if (feature == null)
            {
                return;
            }

            feature.prerequisites = feature.prerequisites ?? Array.Empty<MapFeature>();
            feature.trackGroupsEnableOnUnlock = feature.trackGroupsEnableOnUnlock ?? Array.Empty<string>();
            feature.trackGroupsAvailableOnUnlock = feature.trackGroupsAvailableOnUnlock ?? Array.Empty<string>();
            feature.gameObjectsEnableOnUnlock = feature.gameObjectsEnableOnUnlock ?? Array.Empty<GameObject>();
            feature.areasEnableOnUnlock = feature.areasEnableOnUnlock ?? Array.Empty<Area>();
            feature.unlockExcludeIndustries = feature.unlockExcludeIndustries ?? Array.Empty<Industry>();
            feature.unlockIncludeIndustries = feature.unlockIncludeIndustries ?? Array.Empty<Industry>();
            feature.unlockIncludeIndustryComponents = feature.unlockIncludeIndustryComponents ?? Array.Empty<IndustryComponent>();
        }

        private static void RefreshProgressionManager()
        {
            var manager = UnityEngine.Object.FindObjectOfType<ProgressionManager>();
            if (manager != null)
            {
                ManagerProgressionsField?.SetValue(manager, UnityEngine.Object.FindObjectsOfType<Progression>());
            }
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
