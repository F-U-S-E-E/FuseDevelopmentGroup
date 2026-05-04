using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Progression;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using FUSE.Cache;
using FUSE.Data;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.API
{
    public static class ProgressionAPI
    {
        private static readonly FieldInfo ManagerFeaturesField = typeof(MapFeatureManager).GetField("_features", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ManagerProgressionsField = typeof(ProgressionManager).GetField("_progressions", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ProgressionSectionsField = typeof(Progression).GetField("<Sections>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
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
                ? UnityEngine.Object.FindObjectsOfType<MapFeature>().FirstOrDefault(feature => feature.identifier == id)
                : null;
        }

        public static IEnumerable<MapFeature> GetAllMapFeatures()
        {
            return UnityEngine.Object.FindObjectsOfType<MapFeature>();
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

            ApplyProgressionDefinition(progression, definition);
            FuseProgressionRuntimeIndex.Instance.Set(id, progression);
            RefreshProgressionManager();
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Progression, id, definition);
            return progression;
        }

        public static void UpdateProgression(string id, FuseProgression definition)
        {
            var progression = RequireProgression(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyProgressionDefinition(progression, definition);
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
                ? UnityEngine.Object.FindObjectsOfType<Progression>().FirstOrDefault(progression => progression.identifier == id)
                : null;
        }

        public static IEnumerable<Progression> GetAllProgressions()
        {
            return UnityEngine.Object.FindObjectsOfType<Progression>();
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

        private static void ApplyProgressionDefinition(Progression progression, FuseProgression definition)
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

                ApplySectionDefinition(section, sectionDefinition.Value);
                FuseSectionRuntimeIndex.Instance.Set(section.identifier, section);
            }

            ProgressionSectionsField?.SetValue(progression, progression.GetComponentsInChildren<Section>());
        }

        private static void ApplySectionDefinition(Section section, FuseSection definition)
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
                ResolveMapFeatures(definition.EnableFeaturesOnUnlock),
                sectionUnlockFeature);
            section.enableFeaturesOnAvailable = ResolveMapFeatures(definition.EnableFeaturesOnAvailable);
            section.disableFeaturesOnUnlock = ResolveMapFeatures(definition.DisableFeaturesOnUnlock);
            section.deliveryPhases = (definition.DeliveryPhases ?? Array.Empty<FuseDeliveryPhase>()).Select(CreateDeliveryPhase).ToArray();
            ApplyInterchangeTransfers(section, definition.InterchangeTransfers);
        }

        private static void ApplyInterchangeTransfers(Section section, IDictionary<string, string> transfers)
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
                        FuseLog.Warning($"FUSE progression section '{section.identifier}' skipped interchange transfer with blank source id.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(transfer.Value))
                    {
                        continue;
                    }

                    var from = ResolveInterchange(transfer.Key);
                    var to = ResolveInterchange(transfer.Value);
                    if (from == null || to == null)
                    {
                        FuseLog.Warning($"FUSE progression section '{section.identifier}' skipped interchange transfer '{transfer.Key}' -> '{transfer.Value}' because one or both interchange components were not found.");
                        continue;
                    }

                    if (InterchangeTransferFromField == null || InterchangeTransferToField == null)
                    {
                        FuseLog.Warning($"FUSE progression section '{section.identifier}' could not bind interchange transfer '{transfer.Key}' -> '{transfer.Value}' because base game fields were not found.");
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
            return ResolveObjects(ids, GetMapFeature, "map feature");
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
            return ResolveObjects(paths, ResolveGameObject, "game object");
        }

        private static T[] ResolveObjects<T>(string[] ids, Func<string, T> resolver, string label)
            where T : class
        {
            if (ids == null || ids.Length == 0)
            {
                return Array.Empty<T>();
            }

            return ids.Select(id =>
            {
                var value = resolver(id);
                if (value == null)
                {
                    throw new InvalidOperationException($"Referenced {label} '{id}' was not found.");
                }

                return value;
            }).ToArray();
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

            return UnityEngine.Object.FindObjectsOfType<Interchange>(true)
                .FirstOrDefault(component => ComponentMatchesId(component, id));
        }

        private static GameObject ResolveGameObject(string path)
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

            return UnityEngine.Object.FindObjectsOfType<Transform>(true)
                .FirstOrDefault(transform => string.Equals(GetScenePath(transform), path, StringComparison.OrdinalIgnoreCase))
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
            }
            catch
            {
                // Some freshly cloned components have incomplete parent identity.
            }

            var industry = component.GetComponentInParent<Industry>(true);
            return industry != null &&
                   !string.IsNullOrWhiteSpace(industry.identifier) &&
                   !string.IsNullOrWhiteSpace(component.subIdentifier) &&
                   string.Equals(industry.identifier + "." + component.subIdentifier, id, StringComparison.OrdinalIgnoreCase);
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
                ? UnityEngine.Object.FindObjectsOfType<Section>().FirstOrDefault(section => section.identifier == id)
                : null;
        }

        private static ProgressionIndustryComponent ResolveIndustryComponent(string id)
        {
            if (!FuseIndustryComponentRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                cached = UnityEngine.Object.FindObjectsOfType<IndustryComponent>().FirstOrDefault(component => component.Identifier == id);
            }

            var component = cached as ProgressionIndustryComponent;
            if (component == null)
            {
                throw new InvalidOperationException($"Progression industry component '{id}' was not found.");
            }

            return component;
        }

        private static Load ResolveLoad(string loadId)
        {
            if (string.IsNullOrWhiteSpace(loadId))
            {
                return null;
            }

            var load = CarPrototypeLibrary.instance?.LoadForId(loadId);
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
