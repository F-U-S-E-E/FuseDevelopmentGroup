using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Progression;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using RAIL.Cache;
using RAIL.Data;
using UnityEngine;

namespace RAIL.API
{
    public static class ProgressionAPI
    {
        private static readonly FieldInfo ManagerFeaturesField = typeof(MapFeatureManager).GetField("_features", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ManagerProgressionsField = typeof(ProgressionManager).GetField("_progressions", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ProgressionSectionsField = typeof(Progression).GetField("<Sections>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        public static MapFeature AddMapFeature(string id, RailMapFeature definition)
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
            MapFeatureCache.Instance.Set(id, feature);
            RefreshMapFeatureManager(manager);
            return feature;
        }

        public static void UpdateMapFeature(string id, RailMapFeature definition)
        {
            var feature = RequireMapFeature(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyMapFeatureDefinition(feature, definition);
            MapFeatureCache.Instance.Set(id, feature);
            if (MapFeatureManager.Shared != null)
            {
                RefreshMapFeatureManager(MapFeatureManager.Shared);
            }
        }

        public static void RemoveMapFeature(string id)
        {
            var feature = RequireMapFeature(id);
            feature.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(feature.gameObject);
            MapFeatureCache.Instance.Remove(id);
            if (MapFeatureManager.Shared != null)
            {
                RefreshMapFeatureManager(MapFeatureManager.Shared);
            }
        }

        public static MapFeature GetMapFeature(string id)
        {
            if (MapFeatureCache.Instance.TryGetValue(id, out var cached))
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

        public static Progression AddProgression(string id, RailProgression definition)
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
            ProgressionCache.Instance.Set(id, progression);
            RefreshProgressionManager();
            return progression;
        }

        public static void UpdateProgression(string id, RailProgression definition)
        {
            var progression = RequireProgression(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyProgressionDefinition(progression, definition);
            ProgressionCache.Instance.Set(id, progression);
            RefreshProgressionManager();
        }

        public static void RemoveProgression(string id)
        {
            var progression = RequireProgression(id);
            progression.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(progression.gameObject);
            ProgressionCache.Instance.Remove(id);
            RefreshProgressionManager();
        }

        public static Progression GetProgression(string id)
        {
            if (ProgressionCache.Instance.TryGetValue(id, out var cached))
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

        public static void SetFeatureEnabled(string id, bool enabled)
        {
            var manager = MapFeatureManager.Shared;
            if (manager == null)
            {
                throw new InvalidOperationException("MapFeatureManager was not found.");
            }

            manager.SetFeatureEnabled(id, enabled);
        }

        private static void ApplyMapFeatureDefinition(MapFeature feature, RailMapFeature definition)
        {
            feature.displayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? feature.identifier : definition.DisplayName;
            feature.description = definition.Description ?? string.Empty;
            feature.defaultEnableInSandbox = definition.InitiallyEnabled;
            feature.trackGroupsEnableOnUnlock = definition.GroupIds ?? Array.Empty<string>();
            feature.trackGroupsAvailableOnUnlock = definition.GroupIds ?? Array.Empty<string>();
            feature.prerequisites = feature.prerequisites ?? Array.Empty<MapFeature>();
            feature.gameObjectsEnableOnUnlock = feature.gameObjectsEnableOnUnlock ?? Array.Empty<GameObject>();
            feature.areasEnableOnUnlock = feature.areasEnableOnUnlock ?? Array.Empty<Area>();
            feature.unlockExcludeIndustries = feature.unlockExcludeIndustries ?? Array.Empty<Industry>();
            feature.unlockIncludeIndustries = feature.unlockIncludeIndustries ?? Array.Empty<Industry>();
            feature.unlockIncludeIndustryComponents = feature.unlockIncludeIndustryComponents ?? Array.Empty<IndustryComponent>();
        }

        private static void ApplyProgressionDefinition(Progression progression, RailProgression definition)
        {
            if (progression.mapFeatureManager == null)
            {
                progression.mapFeatureManager = MapFeatureManager.Shared;
            }

            foreach (var sectionDefinition in definition.Sections ?? new Dictionary<string, RailSection>())
            {
                var section = GetSection(sectionDefinition.Key);
                if (section == null || section.transform.parent != progression.transform)
                {
                    var gameObject = new GameObject(sectionDefinition.Key);
                    gameObject.transform.SetParent(progression.transform, false);
                    section = gameObject.AddComponent<Section>();
                    section.identifier = sectionDefinition.Key;
                }

                ApplySectionDefinition(section, sectionDefinition.Value);
                SectionCache.Instance.Set(section.identifier, section);
            }

            ProgressionSectionsField?.SetValue(progression, progression.GetComponentsInChildren<Section>());
        }

        private static void ApplySectionDefinition(Section section, RailSection definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            section.displayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? section.identifier : definition.DisplayName;
            section.description = definition.Description ?? string.Empty;
            section.prerequisiteSections = ResolveSections(definition.PrerequisiteSectionIds);
            section.enableFeaturesOnUnlock = ResolveMapFeatures(definition.EnableFeaturesOnUnlock);
            section.enableFeaturesOnAvailable = ResolveMapFeatures(definition.EnableFeaturesOnAvailable);
            section.disableFeaturesOnUnlock = ResolveMapFeatures(definition.DisableFeaturesOnUnlock);
            section.deliveryPhases = (definition.DeliveryPhases ?? Array.Empty<RailDeliveryPhase>()).Select(CreateDeliveryPhase).ToArray();
        }

        private static Section.DeliveryPhase CreateDeliveryPhase(RailDeliveryPhase definition)
        {
            var deliveries = definition.Deliveries ?? Array.Empty<RailDelivery>();
            var phase = new Section.DeliveryPhase
            {
                cost = definition.Cost,
                deliveries = deliveries.Select(CreateDelivery).ToArray()
            };

            if (deliveries.Length > 0)
            {
                if (string.IsNullOrWhiteSpace(definition.IndustryComponentId))
                {
                    throw new InvalidOperationException("Progression delivery phases with deliveries require IndustryComponentId.");
                }

                phase.industryComponent = ResolveIndustryComponent(definition.IndustryComponentId);
            }

            return phase;
        }

        private static Section.Delivery CreateDelivery(RailDelivery definition)
        {
            return new Section.Delivery
            {
                carTypeFilter = new CarTypeFilter(definition.CarTypeFilter ?? string.Empty),
                count = definition.Count,
                load = ResolveLoad(definition.LoadId),
                direction = Section.Delivery.Direction.LoadToIndustry
            };
        }

        private static Section[] ResolveSections(string[] ids)
        {
            return ResolveObjects(ids, GetSection, "section");
        }

        private static MapFeature[] ResolveMapFeatures(string[] ids)
        {
            return ResolveObjects(ids, GetMapFeature, "map feature");
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

        private static Section GetSection(string id)
        {
            if (SectionCache.Instance.TryGetValue(id, out var cached))
            {
                return (Section)cached;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<Section>().FirstOrDefault(section => section.identifier == id)
                : null;
        }

        private static ProgressionIndustryComponent ResolveIndustryComponent(string id)
        {
            if (!IndustryComponentCache.Instance.TryGetValue(id, out var cached))
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

            LoadCache.Instance.Set(load.id, load);
            return load;
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
            ManagerFeaturesField?.SetValue(manager, manager.GetComponentsInChildren<MapFeature>());
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
