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
                var feature = GetMapFeature(id);
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

            var normalized = NormalizeScenePath(path);
            var transforms = UnityEngine.Object.FindObjectsOfType<Transform>(true);
            var exact = transforms.FirstOrDefault(transform =>
                string.Equals(transform.name, path, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeScenePath(GetScenePath(transform)), normalized, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact.gameObject;
            }

            if (normalized.IndexOf('/') < 0)
            {
                return null;
            }

            var suffix = "/" + normalized.TrimStart('/');
            var suffixMatches = transforms
                .Where(transform => NormalizeScenePath(GetScenePath(transform)).EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            if (suffixMatches.Length == 1)
            {
                FuseLog.Info(
                    $"FUSE resolved shortened scene path '{path}' to '{GetScenePath(suffixMatches[0])}'.");
                return suffixMatches[0].gameObject;
            }

            if (suffixMatches.Length > 1)
            {
                FuseLog.Warning(
                    $"FUSE could not resolve shortened scene path '{path}' because multiple scene objects match that suffix.");
            }

            return null;
        }

        private static string NormalizeScenePath(string path)
        {
            return (path ?? string.Empty)
                .Trim()
                .Replace('\\', '/')
                .Trim('/');
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

        private static Section GetSection(string id)
        {
            if (FuseSectionRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (Section)cached;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<Section>(true).FirstOrDefault(section =>
                    string.Equals(section.identifier, id, StringComparison.OrdinalIgnoreCase))
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
    }
}
