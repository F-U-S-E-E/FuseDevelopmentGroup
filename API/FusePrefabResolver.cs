using System;
using FUSE.Infrastructure;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FUSE.API
{
    internal static class FusePrefabResolver
    {
        private const float RoundhouseOutlierDistance = 85f;
        private const float RoundhouseOutlierHeight = 45f;
        private const float RoundhouseOutlierSize = 95f;

        public static GameObject Resolve(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                throw new ArgumentException("Prefab URI is required.", nameof(uri));
            }

            var marker = uri.IndexOf("://", StringComparison.Ordinal);
            if (marker < 0)
            {
                throw new ArgumentException($"Invalid prefab URI '{uri}'.");
            }

            var scheme = uri.Substring(0, marker);
            var path = uri.Substring(marker + 3);
            if (string.Equals(scheme, "empty", StringComparison.OrdinalIgnoreCase))
            {
                return new GameObject("empty");
            }

            if (string.Equals(scheme, "path", StringComparison.OrdinalIgnoreCase))
            {
                return ResolvePath(path);
            }

            if (string.Equals(scheme, "scenery", StringComparison.OrdinalIgnoreCase))
            {
                var scenery = SceneryAPI.GetScenery(path);
                return scenery != null ? scenery.gameObject : null;
            }

            if (string.Equals(scheme, "vanilla", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveVanilla(path);
            }

            throw new ArgumentException($"Unknown prefab URI scheme '{scheme}'.");
        }

        private static GameObject ResolvePath(string path)
        {
            const string scenePrefix = "scene/";
            if (path.StartsWith(scenePrefix, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(scenePrefix.Length);
            }

            return ResolveScenePath(path);
        }

        public static GameObject ResolveScenePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            var root = FindRootObject(segments[0]);

            if (root == null)
            {
                return GameObject.Find(path);
            }

            var current = root.transform;
            for (var index = 1; index < segments.Length; index++)
            {
                current = FindChild(current, segments[index]);
                if (current == null)
                {
                    return null;
                }
            }

            return current.gameObject;
        }

        private static GameObject FindRootObject(string name)
        {
            GameObject caseInsensitiveMatch = null;
            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                var roots = scene.GetRootGameObjects();
                for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                {
                    var root = roots[rootIndex];
                    if (root == null)
                    {
                        continue;
                    }

                    if (string.Equals(root.name, name, StringComparison.Ordinal))
                    {
                        return root;
                    }

                    if (caseInsensitiveMatch == null && string.Equals(root.name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        caseInsensitiveMatch = root;
                    }
                }
            }

            return caseInsensitiveMatch;
        }

        private static Transform FindChild(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            var exact = parent.Find(name);
            if (exact != null)
            {
                return exact;
            }

            Transform caseInsensitiveMatch = null;
            for (var index = 0; index < parent.childCount; index++)
            {
                var child = parent.GetChild(index);
                if (child == null)
                {
                    continue;
                }

                if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    caseInsensitiveMatch = child;
                    break;
                }
            }

            return caseInsensitiveMatch;
        }

        private static GameObject ResolveVanilla(string key)
        {
            switch (key)
            {
                case "coalConveyor":
                    return ResolvePath("scene/World/Large Scenery/Whittier/Coal Conveyor");
                case "coalTower":
                    return ResolvePath("scene/World/Large Scenery/Bryson/Bryson Coaling Tower");
                case "dieselFuelingStand":
                    return ResolvePath("scene/World/Large Scenery/Whittier/East Whittier Diesel Fueling Stand");
                case "waterColumn":
                    return ResolvePath("scene/World/Large Scenery/Whittier/Water Column");
                case "waterTower":
                    return ResolvePath("scene/World/Large Scenery/Whittier Water Tower");
                case "brysonDepot":
                    return ResolvePath("scene/World/Large Scenery/Bryson/Bryson Depot");
                case "dillsboroDepot":
                    return ResolvePath("scene/World/Large Scenery/Dillsboro/Dillsboro Depot");
                case "flagStopStation":
                    return ResolvePath("scene/World/Large Scenery/Ela/flagstopstation");
                case "roundhouseEnd":
                    return CreateRoundhouseEndPrefab(true);
                case "roundhouseStart":
                    return CreateRoundhouseEndPrefab(false);
                case "roundhouseStall":
                    return CreateRoundhouseStallPrefab();
                case "southernCombinationDepot":
                    return ResolvePath("scene/World/Large Scenery/Whittier/Southern Combination Depot");
                default:
                    throw new ArgumentException($"Unknown vanilla prefab '{key}'.");
            }
        }

        private static GameObject CreateRoundhouseStallPrefab()
        {
            var root = new GameObject("Roundhouse Stall");
            CloneToRoot(root, "scene/World/Large Scenery/Bryson/Bryson Turntable/Roundhouse/Stall", Vector3.zero, Vector3.zero, Vector3.one);
            CloneToRoot(root, "scene/World/Large Scenery/Bryson/Bryson Turntable/Roundhouse/Roundhouse Modular A Between", Vector3.zero, new Vector3(270f, 174.375f, 0f), Vector3.one, false);
            return root;
        }

        private static GameObject CreateRoundhouseEndPrefab(bool start)
        {
            var root = new GameObject("Roundhouse End");
            CloneToRoot(root, "scene/World/Large Scenery/Bryson/Bryson Turntable/Roundhouse/Stall", Vector3.zero, Vector3.zero, Vector3.one);
            CloneToRoot(root, "scene/World/Large Scenery/Bryson/Bryson Turntable/Roundhouse/Roundhouse Modular A Side", Vector3.zero, new Vector3(0f, 180f, 0f), new Vector3(start ? 1f : -1f, 1f, 1f));
            if (!start)
            {
                CloneToRoot(root, "scene/World/Large Scenery/Bryson/Bryson Turntable/Roundhouse/Roundhouse Modular A Between", Vector3.zero, new Vector3(270f, 185.625f, 0f), Vector3.one, false);
            }

            return root;
        }

        private static void CloneToRoot(GameObject root, string path, Vector3 localPosition, Vector3 localEulerAngles, Vector3 localScale, bool applyLocalScale = true)
        {
            var source = ResolvePath(path);
            if (source == null)
            {
                throw new InvalidOperationException($"Prefab source '{path}' was not found.");
            }

            var clone = UnityEngine.Object.Instantiate(source, root.transform);
            clone.transform.localPosition = localPosition;
            clone.transform.localEulerAngles = localEulerAngles;
            if (applyLocalScale)
            {
                clone.transform.localScale = localScale;
            }

            HideOutlyingRoundhouseRenderers(clone, path);
        }

        private static void HideOutlyingRoundhouseRenderers(GameObject clone, string sourcePath)
        {
            if (clone == null)
            {
                return;
            }

            var disabledCount = 0;
            var renderers = clone.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                var boundsRoot = clone.transform.parent != null ? clone.transform.parent : clone.transform;
                var localCenter = boundsRoot.InverseTransformPoint(renderer.bounds.center);
                var boundsSize = renderer.bounds.size;
                var maxSize = Mathf.Max(boundsSize.x, Mathf.Max(boundsSize.y, boundsSize.z));
                var outlyingCenter =
                    Mathf.Abs(localCenter.x) > RoundhouseOutlierDistance ||
                    Mathf.Abs(localCenter.z) > RoundhouseOutlierDistance ||
                    Mathf.Abs(localCenter.y) > RoundhouseOutlierHeight;
                var outlyingSize = maxSize > RoundhouseOutlierSize;
                if (!outlyingCenter && !outlyingSize)
                {
                    continue;
                }

                renderer.enabled = false;
                renderer.forceRenderingOff = true;
                renderer.gameObject.SetActive(false);
                disabledCount++;
                FuseLog.Warning($"FUSE hid outlying roundhouse renderer '{GetTransformPath(clone.transform, renderer.transform)}' from '{sourcePath}' rootLocalCenter={localCenter}, boundsSize={renderer.bounds.size}, cloneLocalScale={clone.transform.localScale}.");
            }

            if (disabledCount > 0)
            {
                FuseLog.Warning($"FUSE hid {disabledCount} outlying roundhouse renderer(s) cloned from '{sourcePath}'.");
            }
        }

        private static string GetTransformPath(Transform root, Transform current)
        {
            if (current == null)
            {
                return string.Empty;
            }

            var names = new System.Collections.Generic.Stack<string>();
            var cursor = current;
            while (cursor != null)
            {
                names.Push(cursor.name);
                if (cursor == root)
                {
                    break;
                }

                cursor = cursor.parent;
            }

            return string.Join("/", names.ToArray());
        }
    }
}
