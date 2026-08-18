using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AutoTrestle;
using Helpers;
using Map.Runtime.MaskComponents;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static class SplineyAPI
    {
        private static readonly FieldInfo RiverBuilderSplineProfileField = typeof(RiverBuilder).GetField("splineProfile", BindingFlags.Instance | BindingFlags.NonPublic);
        private static Transform _fallbackRiverRoot;
        private static Transform _fallbackTrestleRoot;
        private static List<SplineProfile> _splineProfiles;
        private static List<AutoTrestleProfile> _autoTrestleProfiles;

        public static GameObject AddSpliney(string id, FuseSpliney definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetSpliney(id) != null)
            {
                throw new InvalidOperationException($"Spliney '{id}' already exists.");
            }

            var root = new GameObject(id);
            ApplyDefinition(root, id, definition, false);
            FuseSplineyRuntimeIndex.Instance.Set(id, root);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Spliney, id, definition);
            return root;
        }

        public static void UpdateSpliney(string id, FuseSpliney definition)
        {
            var root = RequireSpliney(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (!TryApplyScenePathSplineyPatch(root, id, definition))
            {
                ApplyDefinition(root, id, definition, true);
            }
            FuseSplineyRuntimeIndex.Instance.Set(id, root);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Spliney, id, definition);
        }

        public static void RemoveSpliney(string id)
        {
            if (!TryRemoveSpliney(id))
            {
                throw new InvalidOperationException($"Spliney '{id}' was not found.");
            }
        }

        public static bool TryRemoveSpliney(string id)
        {
            var root = FindRemovableSplineyObject(id);
            if (root == null)
            {
                FuseLog.Warning($"FUSE world removal skipped missing spliney '{id}'.");
                return false;
            }

            var path = GetTransformPath(root.transform);
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
            FuseSplineyRuntimeIndex.Instance.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.Spliney, id);
            FuseLog.Info($"FUSE removed spliney '{id}' from '{path}'.");
            return true;
        }

        public static GameObject GetSpliney(string id)
        {
            if (FuseSplineyRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (GameObject)cached;
            }

            // Strange Customs accepts a scene hierarchy path as a spliney id
            // when a package patches an existing base-game road/river. FUSE
            // previously looked only for its own markers, so a legacy
            // `points: { "$replace": [...] }` patch created a second spline
            // over the original instead of updating it. Resolve path-shaped
            // ids before the ready-cache early-out and admit only actual
            // spline roots so an arbitrary scene object cannot be captured.
            if (TryResolveScenePathSpliney(id, out var sceneSpliney))
            {
                FuseSplineyRuntimeIndex.Instance.Set(id, sceneSpliney);
                return sceneSpliney;
            }

            if (FuseCacheRegistry.IsReady)
            {
                return null;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<FuseSplineyMarker>(true)
                    .FirstOrDefault(marker => string.Equals(marker.Id, id, StringComparison.OrdinalIgnoreCase))
                    ?.gameObject
                : null;
        }

        public static IEnumerable<GameObject> GetAllSplineys()
        {
            return UnityEngine.Object.FindObjectsOfType<FuseSplineyMarker>(true).Select(marker => marker.gameObject);
        }

        public static FuseSpliney GetSplineyDefinition(string id)
        {
            return GetDefinition(GetSpliney(id));
        }

        public static FuseSpliney GetDefinition(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            var marker = root.GetComponent<FuseSplineyMarker>();
            var id = marker != null ? marker.Id : root.name;
            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.Spliney, id, out FuseSpliney definition);
            definition = definition ?? new FuseSpliney();

            var riverPath = root.GetComponent<RiverPath>();
            if (riverPath != null)
            {
                definition.Type = riverPath.style == RiverPath.RiverPathStyle.River ? "river" : "road";
                definition.OffsetY = riverPath.yOffset;
                var builder = root.GetComponent<RiverBuilder>();
                var profile = builder != null ? RiverBuilderSplineProfileField?.GetValue(builder) as SplineProfile : null;
                if (profile != null)
                {
                    definition.Profile = profile.name;
                }

                definition.Points = riverPath.TransformedPoints.Select(point => new FuseSplineyPoint
                {
                    Position = point.position,
                    Rotation = point.eulerAngles,
                    Width = point.width
                }).ToArray();
                return definition;
            }

            var trestle = root.GetComponent<AutoTrestle.AutoTrestle>();
            if (trestle != null)
            {
                definition.Type = "trestle";
                definition.Profile = trestle.profile != null ? trestle.profile.name : definition.Profile;
                definition.HeadStyle = trestle.headStyle.ToString();
                definition.TailStyle = trestle.tailStyle.ToString();
                definition.Points = trestle.controlPoints.Select(point => new FuseSplineyPoint
                {
                    Position = root.transform.TransformPoint(point.position),
                    Rotation = (root.transform.rotation * point.rotation).eulerAngles
                }).ToArray();
            }

            return definition;
        }

        private static void ApplyDefinition(GameObject root, string id, FuseSpliney definition, bool rebuildRuntime)
        {
            var points = definition.Points ?? Array.Empty<FuseSplineyPoint>();
            if (points.Length < 2)
            {
                throw new InvalidOperationException($"Spliney '{id}' requires at least two points.");
            }

            var kind = ParseKind(definition.Type);
            root.SetActive(false);

            switch (kind)
            {
                case SplineyKind.Road:
                case SplineyKind.River:
                case SplineyKind.TerrainRoad:
                case SplineyKind.Waterfall:
                    ConfigureFlowy(root, definition, kind, points);
                    break;
                case SplineyKind.Trestle:
                    ConfigureTrestle(root, definition, points);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported spliney kind '{kind}'.");
            }

            root.name = id;
            var marker = root.GetComponent<FuseSplineyMarker>() ?? root.AddComponent<FuseSplineyMarker>();
            marker.Id = id;
            marker.Kind = kind.ToString();

            root.SetActive(true);

            if (!rebuildRuntime)
            {
                return;
            }

            if (kind == SplineyKind.Trestle)
            {
                var trestle = root.GetComponent<AutoTrestle.AutoTrestle>();
                trestle?.Generate();
                return;
            }

            var builder = root.GetComponent<RiverBuilder>();
            builder?.BuildSpline();
        }

        private static bool TryApplyScenePathSplineyPatch(GameObject root, string id, FuseSpliney definition)
        {
            if (root == null || definition == null)
            {
                return false;
            }

            var marker = root.GetComponent<FuseSplineyMarker>();
            var isScenePathTarget = marker != null && marker.PreserveExistingSceneObject;
            if (!isScenePathTarget)
            {
                isScenePathTarget = IsScenePathIdentifier(id) &&
                    ReferenceEquals(FusePrefabResolver.ResolveScenePath(id), root);
            }

            if (!isScenePathTarget)
            {
                return false;
            }

            // A legacy partial patch inherits the base spliney's profile,
            // style, offset, transform, and parent. Only replace the authored
            // control points unless the fragment explicitly names a style,
            // profile, or end style. This keeps roads such as Sylva's Chipper
            // Curve asphalt instead of rebuilding them with FUSE's first
            // generic road profile (the pale overlay seen in the field
            // report), and keeps base-game trestles on their own profile,
            // parent, and transform instead of letting ApplyDefinition
            // reparent and re-center them.
            var riverPath = root.GetComponent<RiverPath>();
            if (riverPath != null)
            {
                PatchScenePathRiverPath(root, riverPath, marker, id, definition);
                return true;
            }

            var trestle = root.GetComponent<AutoTrestle.AutoTrestle>();
            if (trestle != null)
            {
                PatchScenePathTrestle(root, trestle, marker, id, definition);
                return true;
            }

            return false;
        }

        private static void PatchScenePathRiverPath(GameObject root, RiverPath riverPath, FuseSplineyMarker marker, string id, FuseSpliney definition)
        {
            var points = RequirePatchPoints(id, definition);

            // Resolve and validate everything that can fail before the scene
            // object is deactivated or mutated, so a bad profile name leaves
            // the base-game spline exactly as it was.
            var builder = root.GetComponent<RiverBuilder>();
            SplineProfile profile = null;
            if (builder != null && !string.IsNullOrWhiteSpace(definition.Profile))
            {
                profile = ResolveSplineProfile(definition.Profile, ParseKind(definition.Type));
                if (profile == null)
                {
                    throw new InvalidOperationException($"Spline profile '{definition.Profile}' was not found for '{root.name}'.");
                }

                if (RiverBuilderSplineProfileField == null)
                {
                    throw new InvalidOperationException("RiverBuilder.splineProfile field was not found.");
                }
            }

            RiverPath.RiverPathStyle? style = null;
            if (!string.IsNullOrWhiteSpace(definition.Style))
            {
                style = string.Equals(definition.Style, "river", StringComparison.OrdinalIgnoreCase)
                    ? RiverPath.RiverPathStyle.River
                    : RiverPath.RiverPathStyle.Road;
            }
            else if (!string.IsNullOrWhiteSpace(definition.Type) &&
                     !string.Equals(definition.Type, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                style = IsWaterSpline(ParseKind(definition.Type))
                    ? RiverPath.RiverPathStyle.River
                    : RiverPath.RiverPathStyle.Road;
            }

            var localPoints = points.Select(point => new RiverPath.Point(
                root.transform.InverseTransformPoint(point.Position),
                (Quaternion.Inverse(root.transform.rotation) * Quaternion.Euler(point.Rotation)).eulerAngles,
                point.Width ?? 3.5f)).ToList();

            var wasActive = root.activeSelf;
            root.SetActive(false);
            try
            {
                riverPath.points = localPoints;
                if (style.HasValue)
                {
                    riverPath.style = style.Value;
                }

                if (profile != null)
                {
                    RiverBuilderSplineProfileField.SetValue(builder, profile);
                }

                marker = marker ?? root.AddComponent<FuseSplineyMarker>();
                marker.Id = id;
                marker.Kind = riverPath.style == RiverPath.RiverPathStyle.River ? "River" : "Road";
                marker.PreserveExistingSceneObject = true;
            }
            finally
            {
                root.SetActive(wasActive);
            }

            if (wasActive)
            {
                builder?.BuildSpline();
            }

            FuseLog.Info($"FUSE replaced control points in existing scene spliney '{id}' while preserving its base profile and hierarchy.");
        }

        private static void PatchScenePathTrestle(GameObject root, AutoTrestle.AutoTrestle trestle, FuseSplineyMarker marker, string id, FuseSpliney definition)
        {
            var points = RequirePatchPoints(id, definition);

            AutoTrestleProfile profile = null;
            if (!string.IsNullOrWhiteSpace(definition.Profile))
            {
                profile = ResolveAutoTrestleProfile(definition.Profile);
                if (profile == null)
                {
                    throw new InvalidOperationException($"Auto trestle profile '{definition.Profile}' was not found for '{root.name}'.");
                }
            }

            // AutoTrestle control points are authored in the trestle's local
            // space (see GetDefinition), so map the world-space patch points
            // back through the existing transform instead of re-centering the
            // object the way ConfigureTrestle does for FUSE-owned trestles.
            var localPoints = points.Select(point => new AutoTrestle.AutoTrestle.ControlPoint
            {
                position = root.transform.InverseTransformPoint(point.Position),
                rotation = Quaternion.Inverse(root.transform.rotation) * Quaternion.Euler(point.Rotation)
            }).ToList();

            var wasActive = root.activeSelf;
            root.SetActive(false);
            try
            {
                trestle.controlPoints = localPoints;
                if (!string.IsNullOrWhiteSpace(definition.HeadStyle))
                {
                    trestle.headStyle = ParseEndStyle(definition.HeadStyle);
                }

                if (!string.IsNullOrWhiteSpace(definition.TailStyle))
                {
                    trestle.tailStyle = ParseEndStyle(definition.TailStyle);
                }

                if (profile != null)
                {
                    trestle.profile = profile;
                }

                marker = marker ?? root.AddComponent<FuseSplineyMarker>();
                marker.Id = id;
                marker.Kind = SplineyKind.Trestle.ToString();
                marker.PreserveExistingSceneObject = true;
            }
            finally
            {
                root.SetActive(wasActive);
            }

            if (wasActive)
            {
                trestle.Generate();
            }

            FuseLog.Info($"FUSE replaced control points in existing scene trestle '{id}' while preserving its base profile and hierarchy.");
        }

        private static FuseSplineyPoint[] RequirePatchPoints(string id, FuseSpliney definition)
        {
            var points = definition.Points ?? Array.Empty<FuseSplineyPoint>();
            if (points.Length < 2)
            {
                throw new InvalidOperationException($"Spliney '{id}' requires at least two points.");
            }

            return points;
        }

        private static bool TryResolveScenePathSpliney(string id, out GameObject spliney)
        {
            spliney = null;
            if (!IsScenePathIdentifier(id))
            {
                return false;
            }

            var candidate = FusePrefabResolver.ResolveScenePath(id);
            if (candidate == null ||
                (candidate.GetComponent<RiverPath>() == null && candidate.GetComponent<AutoTrestle.AutoTrestle>() == null))
            {
                return false;
            }

            spliney = candidate;
            return true;
        }

        private static bool IsScenePathIdentifier(string id)
        {
            return !string.IsNullOrWhiteSpace(id) &&
                   id.IndexOf('/') >= 0 &&
                   id.IndexOf("://", StringComparison.Ordinal) < 0;
        }

        private static void ConfigureFlowy(GameObject root, FuseSpliney definition, SplineyKind kind, IReadOnlyList<FuseSplineyPoint> points)
        {
            root.transform.SetParent(GetRiverRoot(), false);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var center = AveragePosition(points);
            root.transform.localPosition = center;

            var riverPath = root.GetComponent<RiverPath>() ?? root.AddComponent<RiverPath>();
            riverPath.style = IsWaterSpline(kind) ? RiverPath.RiverPathStyle.River : RiverPath.RiverPathStyle.Road;
            riverPath.yOffset = definition.OffsetY;
            riverPath.points = points.Select(point => new RiverPath.Point(
                point.Position - center,
                point.Rotation,
                point.Width ?? 3.5f)).ToList();

            var builder = root.GetComponent<RiverBuilder>() ?? root.AddComponent<RiverBuilder>();
            var profile = ResolveSplineProfile(definition.Profile, kind);
            if (profile == null)
            {
                throw new InvalidOperationException($"Spline profile '{definition.Profile}' was not found for '{root.name}'.");
            }

            if (RiverBuilderSplineProfileField == null)
            {
                throw new InvalidOperationException("RiverBuilder.splineProfile field was not found.");
            }

            RiverBuilderSplineProfileField.SetValue(builder, profile);
        }

        private static void ConfigureTrestle(GameObject root, FuseSpliney definition, IReadOnlyList<FuseSplineyPoint> points)
        {
            root.transform.SetParent(GetTrestleRoot(), false);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var center = AveragePosition(points);
            root.transform.localPosition = center;

            var trestle = root.GetComponent<AutoTrestle.AutoTrestle>() ?? root.AddComponent<AutoTrestle.AutoTrestle>();
            trestle.controlPoints = points.Select(point => new AutoTrestle.AutoTrestle.ControlPoint
            {
                position = point.Position - center,
                rotation = Quaternion.Euler(point.Rotation)
            }).ToList();
            trestle.headStyle = ParseEndStyle(definition.HeadStyle);
            trestle.tailStyle = ParseEndStyle(definition.TailStyle);
            trestle.profile = ResolveAutoTrestleProfile(definition.Profile);
            if (trestle.profile == null)
            {
                throw new InvalidOperationException($"Auto trestle profile was not found for '{root.name}'.");
            }
        }

        private static SplineProfile ResolveSplineProfile(string profileName, SplineyKind kind)
        {
            return ResolveNamedObject(
                GetSplineProfiles(),
                profileName,
                GetSplineProfileFallbackHint(kind));
        }

        private static AutoTrestleProfile ResolveAutoTrestleProfile(string profileName)
        {
            return ResolveNamedObject(GetAutoTrestleProfiles(), profileName, "trestle");
        }

        private static List<SplineProfile> GetSplineProfiles()
        {
            if (_splineProfiles != null && _splineProfiles.Any(profile => profile != null))
            {
                return _splineProfiles;
            }

            _splineProfiles = Resources.FindObjectsOfTypeAll<SplineProfile>()
                .Where(profile => profile != null)
                .ToList();

            _splineProfiles.AddRange(UnityEngine.Object.FindObjectsOfType<RiverBuilder>(true)
                .Select(builder => RiverBuilderSplineProfileField?.GetValue(builder) as SplineProfile)
                .Where(profile => profile != null));

            _splineProfiles = _splineProfiles
                .Where(profile => profile != null)
                .GroupBy(profile => profile.GetInstanceID())
                .Select(group => group.First())
                .ToList();

            return _splineProfiles;
        }

        private static List<AutoTrestleProfile> GetAutoTrestleProfiles()
        {
            if (_autoTrestleProfiles != null && _autoTrestleProfiles.Any(profile => profile != null))
            {
                return _autoTrestleProfiles;
            }

            _autoTrestleProfiles = Resources.FindObjectsOfTypeAll<AutoTrestleProfile>()
                .Where(profile => profile != null)
                .ToList();

            _autoTrestleProfiles.AddRange(UnityEngine.Object.FindObjectsOfType<AutoTrestle.AutoTrestle>(true)
                .Select(trestle => trestle != null ? trestle.profile : null)
                .Where(profile => profile != null));

            _autoTrestleProfiles = _autoTrestleProfiles
                .Where(profile => profile != null)
                .GroupBy(profile => profile.GetInstanceID())
                .Select(group => group.First())
                .ToList();

            return _autoTrestleProfiles;
        }

        private static T ResolveNamedObject<T>(IEnumerable<T> candidates, string preferredName, string fallbackHint) where T : UnityEngine.Object
        {
            var distinct = candidates
                .Where(candidate => candidate != null)
                .GroupBy(candidate => candidate.GetInstanceID())
                .Select(group => group.First())
                .ToList();

            if (!string.IsNullOrWhiteSpace(preferredName))
            {
                var exact = distinct.FirstOrDefault(candidate => string.Equals(candidate.name, preferredName, StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                {
                    return exact;
                }

                var contains = distinct.FirstOrDefault(candidate => candidate.name.IndexOf(preferredName, StringComparison.OrdinalIgnoreCase) >= 0);
                if (contains != null)
                {
                    return contains;
                }
            }

            if (!string.IsNullOrWhiteSpace(fallbackHint))
            {
                var hintMatch = distinct.FirstOrDefault(candidate => candidate.name.IndexOf(fallbackHint, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hintMatch != null)
                {
                    return hintMatch;
                }
            }

            return distinct.FirstOrDefault();
        }

        private static Transform GetRiverRoot()
        {
            var existing = GameObject.Find("World/Rivers");
            if (existing != null)
            {
                return existing.transform;
            }

            var world = GameObject.Find("World");
            if (world != null)
            {
                var rivers = new GameObject("Rivers");
                rivers.transform.SetParent(world.transform, false);
                return rivers.transform;
            }

            if (_fallbackRiverRoot == null)
            {
                _fallbackRiverRoot = new GameObject("FUSE Rivers").transform;
                UnityEngine.Object.DontDestroyOnLoad(_fallbackRiverRoot.gameObject);
            }

            return _fallbackRiverRoot;
        }

        private static Transform GetTrestleRoot()
        {
            var world = GameObject.Find("World") ?? GameObject.Find("Large Scenery");
            if (world != null)
            {
                return world.transform;
            }

            if (_fallbackTrestleRoot == null)
            {
                _fallbackTrestleRoot = new GameObject("FUSE Trestles").transform;
                UnityEngine.Object.DontDestroyOnLoad(_fallbackTrestleRoot.gameObject);
            }

            return _fallbackTrestleRoot;
        }

        private static Vector3 AveragePosition(IReadOnlyList<FuseSplineyPoint> points)
        {
            var center = Vector3.zero;
            for (var index = 0; index < points.Count; index++)
            {
                center += points[index].Position;
            }

            return center / points.Count;
        }

        private static SplineyKind ParseKind(string value)
        {
            if (string.Equals(value, "trestle", StringComparison.OrdinalIgnoreCase))
            {
                return SplineyKind.Trestle;
            }

            if (string.Equals(value, "terrainRoad", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "terrain-road", StringComparison.OrdinalIgnoreCase))
            {
                return SplineyKind.TerrainRoad;
            }

            if (string.Equals(value, "waterfall", StringComparison.OrdinalIgnoreCase))
            {
                return SplineyKind.Waterfall;
            }

            if (string.Equals(value, "river", StringComparison.OrdinalIgnoreCase))
            {
                return SplineyKind.River;
            }

            return SplineyKind.Road;
        }

        private static bool IsWaterSpline(SplineyKind kind)
        {
            return kind == SplineyKind.River || kind == SplineyKind.Waterfall;
        }

        private static string GetSplineProfileFallbackHint(SplineyKind kind)
        {
            switch (kind)
            {
                case SplineyKind.River:
                    return "river";
                case SplineyKind.Waterfall:
                    return "waterfall";
                case SplineyKind.TerrainRoad:
                    return "road";
                default:
                    return "road";
            }
        }

        private static AutoTrestle.AutoTrestle.EndStyle ParseEndStyle(string value)
        {
            return string.Equals(value, "bent", StringComparison.OrdinalIgnoreCase)
                ? AutoTrestle.AutoTrestle.EndStyle.Bent
                : AutoTrestle.AutoTrestle.EndStyle.Block;
        }

        private static GameObject RequireSpliney(string id)
        {
            var root = GetSpliney(id);
            if (root == null)
            {
                throw new InvalidOperationException($"Spliney '{id}' was not found.");
            }

            return root;
        }

        private static GameObject FindRemovableSplineyObject(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return GetSpliney(id) ?? FusePrefabResolver.ResolveScenePath(id) ?? GameObject.Find(id);
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var names = new Stack<string>();
            var cursor = transform;
            while (cursor != null)
            {
                names.Push(cursor.name);
                cursor = cursor.parent;
            }

            return string.Join("/", names.ToArray());
        }

        private static void RequireId(string id, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("ID is required.", parameterName);
            }
        }

        private enum SplineyKind
        {
            Road,
            River,
            TerrainRoad,
            Waterfall,
            Trestle
        }
    }

    public sealed class FuseSplineyMarker : MonoBehaviour
    {
        public string Id;
        public string Kind;
        public bool PreserveExistingSceneObject;
    }
}
