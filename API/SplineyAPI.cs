using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AutoTrestle;
using Helpers;
using Map.Runtime.MaskComponents;
using RAIL.Cache;
using RAIL.Data;
using RAIL.Infrastructure;
using UnityEngine;

namespace RAIL.API
{
    public static class SplineyAPI
    {
        private static readonly FieldInfo RiverBuilderSplineProfileField = typeof(RiverBuilder).GetField("splineProfile", BindingFlags.Instance | BindingFlags.NonPublic);
        private static Transform _fallbackRiverRoot;
        private static Transform _fallbackTrestleRoot;

        public static GameObject AddSpliney(string id, RailSpliney definition)
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
            RailSplineyRuntimeIndex.Instance.Set(id, root);
            RailApiPersistence.RecordDefinition(RailDefinitionKind.Spliney, id, definition);
            return root;
        }

        public static void UpdateSpliney(string id, RailSpliney definition)
        {
            var root = RequireSpliney(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyDefinition(root, id, definition, true);
            RailSplineyRuntimeIndex.Instance.Set(id, root);
            RailApiPersistence.RecordDefinition(RailDefinitionKind.Spliney, id, definition);
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
                RailLog.Warning($"RAIL world removal skipped missing spliney '{id}'.");
                return false;
            }

            var path = GetTransformPath(root.transform);
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
            RailSplineyRuntimeIndex.Instance.Remove(id);
            RailRuntimeDefinitionCache.Remove(RailDefinitionKind.Spliney, id);
            RailLog.Info($"RAIL removed spliney '{id}' from '{path}'.");
            return true;
        }

        public static GameObject GetSpliney(string id)
        {
            if (RailSplineyRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (GameObject)cached;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<RailSplineyMarker>(true)
                    .FirstOrDefault(marker => string.Equals(marker.Id, id, StringComparison.OrdinalIgnoreCase))
                    ?.gameObject
                : null;
        }

        public static IEnumerable<GameObject> GetAllSplineys()
        {
            return UnityEngine.Object.FindObjectsOfType<RailSplineyMarker>(true).Select(marker => marker.gameObject);
        }

        public static RailSpliney GetSplineyDefinition(string id)
        {
            return GetDefinition(GetSpliney(id));
        }

        public static RailSpliney GetDefinition(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            var marker = root.GetComponent<RailSplineyMarker>();
            var id = marker != null ? marker.Id : root.name;
            RailRuntimeDefinitionCache.TryGet(RailDefinitionKind.Spliney, id, out RailSpliney definition);
            definition = definition ?? new RailSpliney();

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

                definition.Points = riverPath.TransformedPoints.Select(point => new RailSplineyPoint
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
                definition.Points = trestle.controlPoints.Select(point => new RailSplineyPoint
                {
                    Position = root.transform.TransformPoint(point.position),
                    Rotation = (root.transform.rotation * point.rotation).eulerAngles
                }).ToArray();
            }

            return definition;
        }

        private static void ApplyDefinition(GameObject root, string id, RailSpliney definition, bool rebuildRuntime)
        {
            var points = definition.Points ?? Array.Empty<RailSplineyPoint>();
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
                    ConfigureFlowy(root, definition, kind, points);
                    break;
                case SplineyKind.Trestle:
                    ConfigureTrestle(root, definition, points);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            root.name = id;
            var marker = root.GetComponent<RailSplineyMarker>() ?? root.AddComponent<RailSplineyMarker>();
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

        private static void ConfigureFlowy(GameObject root, RailSpliney definition, SplineyKind kind, IReadOnlyList<RailSplineyPoint> points)
        {
            root.transform.SetParent(GetRiverRoot(), false);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var center = AveragePosition(points);
            root.transform.localPosition = center;

            var riverPath = root.GetComponent<RiverPath>() ?? root.AddComponent<RiverPath>();
            riverPath.style = kind == SplineyKind.River ? RiverPath.RiverPathStyle.River : RiverPath.RiverPathStyle.Road;
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

        private static void ConfigureTrestle(GameObject root, RailSpliney definition, IReadOnlyList<RailSplineyPoint> points)
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
            var profiles = Resources.FindObjectsOfTypeAll<SplineProfile>()
                .Where(profile => profile != null)
                .ToList();

            profiles.AddRange(UnityEngine.Object.FindObjectsOfType<RiverBuilder>(true)
                .Select(builder => RiverBuilderSplineProfileField?.GetValue(builder) as SplineProfile)
                .Where(profile => profile != null));

            return ResolveNamedObject(
                profiles,
                profileName,
                kind == SplineyKind.River ? "river" : "road");
        }

        private static AutoTrestleProfile ResolveAutoTrestleProfile(string profileName)
        {
            var profiles = Resources.FindObjectsOfTypeAll<AutoTrestleProfile>()
                .Where(profile => profile != null)
                .ToList();

            profiles.AddRange(UnityEngine.Object.FindObjectsOfType<AutoTrestle.AutoTrestle>(true)
                .Select(trestle => trestle != null ? trestle.profile : null)
                .Where(profile => profile != null));

            return ResolveNamedObject(profiles, profileName, "trestle");
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
                _fallbackRiverRoot = new GameObject("RAIL Rivers").transform;
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
                _fallbackTrestleRoot = new GameObject("RAIL Trestles").transform;
                UnityEngine.Object.DontDestroyOnLoad(_fallbackTrestleRoot.gameObject);
            }

            return _fallbackTrestleRoot;
        }

        private static Vector3 AveragePosition(IReadOnlyList<RailSplineyPoint> points)
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

            if (string.Equals(value, "river", StringComparison.OrdinalIgnoreCase))
            {
                return SplineyKind.River;
            }

            return SplineyKind.Road;
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

            return GetSpliney(id) ?? RailPrefabResolver.ResolveScenePath(id) ?? GameObject.Find(id);
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
            Trestle
        }
    }

    public sealed class RailSplineyMarker : MonoBehaviour
    {
        public string Id;
        public string Kind;
    }
}
