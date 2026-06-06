using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Model.Ops;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Authoring.Data.Common;
using FUSE.Runtime.Events;
using FUSE.Infrastructure;
using Track;
using Track.Signals;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static partial class TrackAPI
    {

        public static Area AddArea(string id, FuseArea definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetArea(id) != null)
            {
                throw new InvalidOperationException($"Area '{id}' already exists.");
            }

            var displayName = string.IsNullOrWhiteSpace(definition.Name) ? id : definition.Name;
            var existingNamedArea = FindSingleAreaByDisplayName(displayName);
            if (existingNamedArea != null)
            {
                var existingAreaKey = string.IsNullOrWhiteSpace(existingNamedArea.identifier)
                    ? existingNamedArea.name
                    : existingNamedArea.identifier;
                AreaAliases[id] = existingAreaKey;
                ApplyAreaDefinition(existingNamedArea, definition);
                RememberAreaOrder(id, definition.Order);
                FuseAreaRuntimeIndex.Instance.Set(id, existingNamedArea);
                if (!string.IsNullOrWhiteSpace(existingAreaKey))
                {
                    FuseAreaRuntimeIndex.Instance.Set(existingAreaKey, existingNamedArea);
                }
                FuseLog.Info(
                    $"FUSE aliased area '{id}' name='{displayName}' to existing area id='{existingAreaKey}' " +
                    $"parent='{DescribeAreaParent(existingNamedArea.transform.parent)}' position={existingNamedArea.transform.localPosition} radius={existingNamedArea.radius}.");
                FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackArea, id, definition);
                return existingNamedArea;
            }

            var gameObject = new GameObject(displayName);
            gameObject.transform.SetParent(GetAreaRoot(), false);
            var area = gameObject.AddComponent<Area>();
            area.identifier = id;
            ApplyAreaDefinition(area, definition);
            RememberAreaOrder(id, definition.Order);
            FuseAreaRuntimeIndex.Instance.Set(id, area);
            FuseLog.Info($"FUSE created area '{id}' name='{displayName}' parent='{DescribeAreaParent(area.transform.parent)}' position={area.transform.localPosition} radius={area.radius}.");
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackArea, id, definition);
            return area;
        }

        public static void UpdateArea(string id, FuseArea definition)
        {
            var area = RequireArea(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyAreaDefinition(area, definition);
            RememberAreaOrder(id, definition.Order);
            FuseAreaRuntimeIndex.Instance.Set(id, area);
            FuseLog.Info($"FUSE updated area '{id}' name='{area.name}' position={area.transform.localPosition} radius={area.radius}.");
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackArea, id, definition);
        }

        public static Area GetArea(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            if (FuseAreaRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (Area)cached;
            }

            if (AreaAliases.TryGetValue(id, out var aliasedId) &&
                !string.IsNullOrWhiteSpace(aliasedId) &&
                !string.Equals(aliasedId, id, StringComparison.OrdinalIgnoreCase))
            {
                var aliasedArea = GetArea(aliasedId);
                if (aliasedArea != null)
                {
                    FuseAreaRuntimeIndex.Instance.Set(id, aliasedArea);
                    return aliasedArea;
                }
            }

            var controller = OpsController.Shared;
            if (controller != null)
            {
                var area = controller.Areas.FirstOrDefault(candidate =>
                    candidate != null &&
                    (string.Equals(candidate.identifier, id, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(candidate.name, id, StringComparison.OrdinalIgnoreCase)));
                if (area != null)
                {
                    FuseAreaRuntimeIndex.Instance.Set(area.identifier, area);
                    return area;
                }
            }

            return UnityEngine.Object.FindObjectsOfType<Area>(true).FirstOrDefault(candidate =>
                candidate != null &&
                (string.Equals(candidate.identifier, id, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(candidate.name, id, StringComparison.OrdinalIgnoreCase)));
        }

        public static IEnumerable<Area> GetAllAreas()
        {
            return UnityEngine.Object.FindObjectsOfType<Area>(true).Where(area => area != null);
        }

        public static FuseArea GetAreaDefinition(string id)
        {
            return GetDefinition(GetArea(id));
        }

        public static FuseArea GetDefinition(Area area)
        {
            if (area == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.TrackArea, area.identifier, out FuseArea definition);
            definition = definition ?? new FuseArea();
            definition.Name = area.name;
            definition.Position = area.transform.localPosition;
            definition.Radius = area.radius;
            definition.TagColor = new[] { area.tagColor.r, area.tagColor.g, area.tagColor.b, area.tagColor.a };
            return definition;
        }

        public static void ApplyAreaOrdering()
        {
            var areas = GetAllAreas()
                .Where(area => area != null)
                .ToArray();
            var explicitCount = areas.Count(area =>
            {
                int _;
                return TryGetAreaOrder(area, out _);
            });
            if (explicitCount > 0)
            {
                var movedCount = ApplyAreaSiblingOrdering(areas);
                FuseLog.Info(
                    $"FUSE cached area ordering for {areas.Length} area(s), explicitOrdered={explicitCount}, " +
                    $"moved={movedCount}, firstAreas='{BuildAreaOrderPreview()}'.");
            }
        }

        public static bool TryGetAreaOrder(Area area, out int order)
        {
            order = 0;
            return area != null &&
                   !string.IsNullOrWhiteSpace(area.identifier) &&
                   (AreaOrders.TryGetValue(area.identifier, out order) ||
                    TryGetAliasedAreaOrder(area.identifier, out order));
        }

        public static int GetAreaSortOrder(Area area, int fallbackSiblingIndex)
        {
            int order;
            return TryGetAreaOrder(area, out order)
                ? order
                : GetAreaFallbackOrder(area, fallbackSiblingIndex);
        }

        public static int GetSiblingAreaSortOrder(int siblingIndex)
        {
            if (siblingIndex < 0)
            {
                return int.MaxValue;
            }

            if (siblingIndex >= int.MaxValue / AreaOrderSiblingSpacing)
            {
                return int.MaxValue;
            }

            return siblingIndex * AreaOrderSiblingSpacing;
        }

        private static string BuildAreaOrderPreview()
        {
            try
            {
                var areas = OpsController.Shared != null
                    ? OpsController.Shared.Areas
                    : GetAllAreas();
                var indexedAreas = areas
                    .Where(area => area != null)
                    .Select((area, index) =>
                    {
                        int order;
                        var hasOrder = TryGetAreaOrder(area, out order);
                        return new
                        {
                            Area = area,
                            Index = index,
                            HasOrder = hasOrder,
                            Order = hasOrder ? order : GetAreaFallbackOrder(area, index)
                        };
                    });

                return string.Join(
                    " > ",
                    indexedAreas
                        .OrderBy(item => item.Order)
                        .ThenBy(item => item.Index)
                        .Take(12)
                        .Select(item => string.IsNullOrWhiteSpace(item.Area.name) ? item.Area.identifier : item.Area.name)
                        .ToArray());
            }
            catch (Exception ex)
            {
                return "unavailable: " + ex.Message;
            }
        }

        private static int ApplyAreaSiblingOrdering(IEnumerable<Area> areas)
        {
            var movedCount = 0;
            try
            {
                foreach (var parentGroup in areas
                             .Where(area => area != null && area.transform != null && area.transform.parent != null)
                             .Select(area =>
                             {
                                 int order;
                                 var hasOrder = TryGetAreaOrder(area, out order);
                                 return new
                                 {
                                     Area = area,
                                     Parent = area.transform.parent,
                                     SiblingIndex = area.transform.GetSiblingIndex(),
                                     HasOrder = hasOrder,
                                     Order = hasOrder ? order : GetAreaFallbackOrder(area, area.transform.GetSiblingIndex())
                                 };
                             })
                             .GroupBy(item => item.Parent))
                {
                    if (!parentGroup.Any(item => item.HasOrder))
                    {
                        continue;
                    }

                    var original = parentGroup
                        .OrderBy(item => item.SiblingIndex)
                        .ToArray();
                    var ordered = original
                        .OrderBy(item => item.Order)
                        .ThenBy(item => item.SiblingIndex)
                        .ToArray();

                    var baseIndex = original.Min(item => item.SiblingIndex);
                    for (var index = 0; index < ordered.Length; index++)
                    {
                        var targetIndex = baseIndex + index;
                        var area = ordered[index].Area;
                        if (area == null || area.transform == null || area.transform.GetSiblingIndex() == targetIndex)
                        {
                            continue;
                        }

                        area.transform.SetSiblingIndex(targetIndex);
                        movedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE could not apply area sibling ordering for the Locations list", ex);
            }

            return movedCount;
        }

        private static Area RequireArea(string id)
        {
            var area = GetArea(id);
            if (area == null)
            {
                throw new InvalidOperationException($"Area '{id}' was not found.");
            }

            return area;
        }

        private static void ApplyAreaDefinition(Area area, FuseArea definition)
        {
            var displayName = string.IsNullOrWhiteSpace(definition.Name) ? area.identifier : definition.Name;
            area.name = displayName;
            area.gameObject.name = displayName;
            if (definition.Position.HasValue)
            {
                area.transform.localPosition = definition.Position.Value;
            }

            if (definition.Radius.HasValue)
            {
                area.radius = definition.Radius.Value;
            }

            if (definition.TagColor != null && definition.TagColor.Length >= 3)
            {
                area.tagColor = ParseAreaColor(definition.TagColor);
            }
        }

        private static void RememberAreaOrder(string id, int? order)
        {
            if (order.HasValue)
            {
                AreaOrders[id] = order.Value;
                return;
            }

            AreaOrders.Remove(id);
        }

        private static Area FindSingleAreaByDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return null;
            }

            var matches = GetAllAreas()
                .Where(area => area != null &&
                               string.Equals(area.name, displayName, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static bool TryGetAliasedAreaOrder(string areaId, out int order)
        {
            foreach (var alias in AreaAliases)
            {
                if (string.Equals(alias.Value, areaId, StringComparison.OrdinalIgnoreCase) &&
                    AreaOrders.TryGetValue(alias.Key, out order))
                {
                    return true;
                }
            }

            order = 0;
            return false;
        }

        private static int GetAreaFallbackOrder(Area area, int fallbackSiblingIndex)
        {
            var key = GetAreaOrderKey(area);
            if (!string.IsNullOrWhiteSpace(key) && AreaFallbackOrders.TryGetValue(key, out var order))
            {
                return order;
            }

            order = GetSiblingAreaSortOrder(fallbackSiblingIndex);
            if (!string.IsNullOrWhiteSpace(key))
            {
                AreaFallbackOrders[key] = order;
            }

            return order;
        }

        private static string GetAreaOrderKey(Area area)
        {
            if (area == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(area.identifier))
            {
                return area.identifier;
            }

            return !string.IsNullOrWhiteSpace(area.name) ? area.name : null;
        }

        private static Color ParseAreaColor(float[] values)
        {
            return new Color(
                Mathf.Clamp01(values[0]),
                Mathf.Clamp01(values[1]),
                Mathf.Clamp01(values[2]),
                values.Length > 3 ? Mathf.Clamp01(values[3]) : 1f);
        }

        private static Transform GetAreaRoot()
        {
            if (OpsController.Shared != null)
            {
                return OpsController.Shared.transform;
            }

            if (_fallbackAreaRoot == null)
            {
                _fallbackAreaRoot = new GameObject("FUSE Areas").transform;
                UnityEngine.Object.DontDestroyOnLoad(_fallbackAreaRoot.gameObject);
            }

            return _fallbackAreaRoot;
        }

        private static string DescribeAreaParent(Transform parent)
        {
            if (parent == null)
            {
                return "<none>";
            }

            var ops = parent.GetComponent<OpsController>();
            return ops != null ? $"{parent.name} (OpsController)" : parent.name;
        }
    }
}
