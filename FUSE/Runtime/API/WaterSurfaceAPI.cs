using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Runtime.Cache;
using UnityEngine;
using UnityEngine.Rendering;

namespace FUSE.Runtime.API
{
    /// <summary>
    /// Creates FUSE-owned lake polygons from native world data. The runtime
    /// uses Railroader's public LakePolygon contract and loaded water assets;
    /// it does not embed or duplicate game implementation code.
    /// </summary>
    public static class WaterSurfaceAPI
    {
        private static Transform _fallbackRoot;
        private static Material _fallbackMaterial;

        public static GameObject AddWaterSurface(string id, FuseWaterSurface definition)
        {
            RequireId(id);
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (GetWaterSurface(id) != null)
                throw new InvalidOperationException($"Water surface '{id}' already exists.");

            var root = new GameObject(id);
            try
            {
                ApplyDefinition(root, id, definition);
                FuseWaterSurfaceRuntimeIndex.Instance.Set(id, root);
                FuseApiPersistence.RecordDefinition(FuseDefinitionKind.WaterSurface, id, definition);
                return root;
            }
            catch
            {
                root.SetActive(false);
                ReleaseGeneratedMesh(root);
                UnityEngine.Object.Destroy(root);
                throw;
            }
        }

        public static void UpdateWaterSurface(string id, FuseWaterSurface definition)
        {
            var root = RequireWaterSurface(id);
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            ApplyDefinition(root, id, definition);
            FuseWaterSurfaceRuntimeIndex.Instance.Set(id, root);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.WaterSurface, id, definition);
        }

        public static GameObject GetWaterSurface(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;
            if (FuseWaterSurfaceRuntimeIndex.Instance.TryGetValue(id, out var cached))
                return cached as GameObject;
            if (FuseCacheRegistry.IsReady)
                return null;

            return UnityEngine.Object.FindObjectsOfType<FuseWaterSurfaceMarker>(true)
                .FirstOrDefault(marker => string.Equals(marker.Id, id, StringComparison.OrdinalIgnoreCase))
                ?.gameObject;
        }

        public static IEnumerable<GameObject> GetAllWaterSurfaces()
        {
            return UnityEngine.Object.FindObjectsOfType<FuseWaterSurfaceMarker>(true)
                .Where(marker => marker != null)
                .Select(marker => marker.gameObject);
        }

        public static FuseWaterSurface GetWaterSurfaceDefinition(string id)
        {
            return GetDefinition(GetWaterSurface(id));
        }

        public static FuseWaterSurface GetDefinition(GameObject root)
        {
            if (root == null)
                return null;
            var marker = root.GetComponent<FuseWaterSurfaceMarker>();
            var id = marker != null ? marker.Id : root.name;
            FuseRuntimeDefinitionCache.TryGet(
                FuseDefinitionKind.WaterSurface,
                id,
                out FuseWaterSurface definition);
            definition = definition ?? new FuseWaterSurface();

            var lake = root.GetComponent<LakePolygon>();
            if (lake != null)
            {
                definition.Points = (lake.points ?? new List<Vector3>())
                    .Select(root.transform.TransformPoint)
                    .ToArray();
                definition.LockHeight = lake.lockHeight;
                definition.SnapToTerrain = lake.snapToTerrain;
                definition.EnableCollider = root.GetComponent<MeshCollider>() != null;
                definition.UvScale = lake.uvScale;
                definition.TriangleDensity = lake.traingleDensity;
                definition.MaximumTriangleArea = lake.maximumTriangleSize;
                definition.YOffset = lake.yOffset;
            }

            if (marker != null)
            {
                definition.SourceLakePath = marker.SourceLakePath;
                definition.MaterialName = marker.MaterialName;
            }
            return definition;
        }

        public static void RemoveWaterSurface(string id)
        {
            if (!TryRemoveWaterSurface(id))
                throw new InvalidOperationException($"Water surface '{id}' was not found.");
        }

        public static bool TryRemoveWaterSurface(string id)
        {
            var root = GetWaterSurface(id);
            if (root == null)
            {
                FuseLog.Warning($"FUSE world removal skipped missing water surface '{id}'.");
                return false;
            }

            root.SetActive(false);
            ReleaseGeneratedMesh(root);
            UnityEngine.Object.Destroy(root);
            FuseWaterSurfaceRuntimeIndex.Instance.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.WaterSurface, id);
            FuseLog.Info($"FUSE removed water surface '{id}'.");
            return true;
        }

        private static void ApplyDefinition(GameObject root, string id, FuseWaterSurface definition)
        {
            var points = definition.Points ?? Array.Empty<Vector3>();
            if (points.Length < 3)
                throw new InvalidOperationException($"Water surface '{id}' requires at least three boundary points.");
            if (definition.TriangleDensity <= 0f || definition.TriangleDensity > 1f)
                throw new InvalidOperationException($"Water surface '{id}' triangleDensity must be greater than 0 and at most 1.");
            if (definition.MaximumTriangleArea <= 0f)
                throw new InvalidOperationException($"Water surface '{id}' maximumTriangleArea must be greater than 0.");

            var source = ResolveSourceLake(definition.SourceLakePath);
            var material = ResolveMaterial(source, definition.MaterialName);
            root.SetActive(false);
            try
            {
                root.name = id;
                root.transform.SetParent(GetWaterRoot(), false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                var waterLayer = LayerMask.NameToLayer("Water");
                if (waterLayer >= 0)
                    root.layer = waterLayer;

                var lake = root.GetComponent<LakePolygon>() ?? root.AddComponent<LakePolygon>();
                var renderer = root.GetComponent<MeshRenderer>() ?? root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;

                ApplySourceLakeProfile(source, lake);
                lake.points = points.ToList();
                lake.lockHeight = definition.LockHeight;
                lake.snapToTerrain = definition.SnapToTerrain;
                lake.uvScale = definition.UvScale;
                lake.traingleDensity = definition.TriangleDensity;
                lake.maximumTriangleSize = definition.MaximumTriangleArea;
                lake.yOffset = definition.YOffset;

                var collider = root.GetComponent<MeshCollider>();
                if (definition.EnableCollider && collider == null)
                    collider = root.AddComponent<MeshCollider>();
                else if (!definition.EnableCollider && collider != null)
                    UnityEngine.Object.Destroy(collider);

                var previousMesh = root.GetComponent<MeshFilter>()?.sharedMesh;
                // A source lake's painted per-vertex flow map belongs to its old
                // triangulation. Generate a fresh automatic map for this polygon
                // while retaining the source profile's flow speed and noise.
                lake.overrideFlowMap = false;
                lake.colorsFlowMap.Clear();
                lake.GeneratePolygon(false);
                if (collider != null)
                    collider.sharedMesh = root.GetComponent<MeshFilter>()?.sharedMesh;
                ReleaseReplacedMesh(previousMesh, lake.currentMesh);

                var marker = root.GetComponent<FuseWaterSurfaceMarker>()
                             ?? root.AddComponent<FuseWaterSurfaceMarker>();
                marker.Id = id;
                marker.SourceLakePath = definition.SourceLakePath;
                marker.MaterialName = definition.MaterialName;
            }
            finally
            {
                root.SetActive(true);
            }
        }

        private static void ApplySourceLakeProfile(LakePolygon source, LakePolygon target)
        {
            if (ReferenceEquals(target, null))
                throw new ArgumentNullException(nameof(target));

            if (ReferenceEquals(source, null))
            {
                target.currentProfile = null;
                target.receiveShadows = false;
                target.shadowCastingMode = ShadowCastingMode.Off;
                return;
            }

            // These are public LakePolygon rendering/profile controls. Copying
            // them preserves the loaded map's water appearance without copying
            // the game's mesh-generation implementation or terrain mutations.
            target.currentProfile = source.currentProfile;
            target.distSmooth = source.distSmooth;
            target.terrainSmoothMultiplier = source.terrainSmoothMultiplier;
            target.overrideLakeRender = source.overrideLakeRender;
            target.receiveShadows = source.receiveShadows;
            target.shadowCastingMode = source.shadowCastingMode;
            target.automaticFlowMapScale = source.automaticFlowMapScale;
            target.noiseflowMap = source.noiseflowMap;
            target.noiseMultiplierflowMap = source.noiseMultiplierflowMap;
            target.noiseSizeXflowMap = source.noiseSizeXflowMap;
            target.noiseSizeZflowMap = source.noiseSizeZflowMap;
            target.floatSpeed = source.floatSpeed;
            target.flowSpeed = source.flowSpeed;
            target.flowDirection = source.flowDirection;
            target.normalFromRaycast = source.normalFromRaycast;
            target.snapMask = source.snapMask;
        }

        private static void ReleaseReplacedMesh(Mesh previous, Mesh current)
        {
            if (previous != null && previous != current)
                UnityEngine.Object.Destroy(previous);
        }

        private static void ReleaseGeneratedMesh(GameObject root)
        {
            if (root == null)
                return;

            var lake = root.GetComponent<LakePolygon>();
            var filter = root.GetComponent<MeshFilter>();
            var collider = root.GetComponent<MeshCollider>();
            var mesh = lake?.currentMesh ?? filter?.sharedMesh;
            if (collider != null && collider.sharedMesh == mesh)
                collider.sharedMesh = null;
            if (filter != null && filter.sharedMesh == mesh)
                filter.sharedMesh = null;
            if (lake != null)
            {
                lake.currentMesh = null;
                lake.meshfilter = null;
            }
            if (mesh != null)
                UnityEngine.Object.Destroy(mesh);
        }

        private static LakePolygon ResolveSourceLake(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                var sourceObject = FusePrefabResolver.ResolveScenePath(path.Trim());
                var source = sourceObject != null
                    ? sourceObject.GetComponent<LakePolygon>()
                      ?? sourceObject.GetComponentInChildren<LakePolygon>(true)
                    : null;
                if (source == null)
                    throw new InvalidOperationException($"Source lake path '{path}' was not found or has no LakePolygon component.");
                return source;
            }

            return Resources.FindObjectsOfTypeAll<LakePolygon>()
                .FirstOrDefault(candidate => candidate != null
                    && candidate.GetComponent<FuseWaterSurfaceMarker>() == null
                    && candidate.GetComponent<MeshRenderer>()?.sharedMaterial != null);
        }

        private static Material ResolveMaterial(LakePolygon source, string materialName)
        {
            if (!string.IsNullOrWhiteSpace(materialName))
            {
                var material = Resources.FindObjectsOfTypeAll<Material>()
                    .FirstOrDefault(candidate => candidate != null
                        && string.Equals(candidate.name, materialName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (material == null)
                    throw new InvalidOperationException($"Water material '{materialName}' was not found among loaded game assets.");
                return material;
            }

            var sourceMaterial = source?.GetComponent<MeshRenderer>()?.sharedMaterial;
            if (sourceMaterial != null)
                return sourceMaterial;

            var loadedWater = Resources.FindObjectsOfTypeAll<Material>()
                .FirstOrDefault(candidate => candidate != null
                    && (candidate.name.IndexOf("lake", StringComparison.OrdinalIgnoreCase) >= 0
                        || candidate.name.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0));
            if (loadedWater != null)
                return loadedWater;

            if (_fallbackMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader == null)
                    throw new InvalidOperationException("No loaded lake material or fallback render shader was available.");
                _fallbackMaterial = new Material(shader)
                {
                    name = "FUSE Fallback Water",
                    color = new Color(0.08f, 0.32f, 0.48f, 0.82f),
                    hideFlags = HideFlags.HideAndDontSave,
                };
                FuseLog.Warning("FUSE could not find a stock lake material; using the simple fallback water material.");
            }
            return _fallbackMaterial;
        }

        private static Transform GetWaterRoot()
        {
            var existing = GameObject.Find("World/Water Surfaces");
            if (existing != null)
                return existing.transform;
            var world = GameObject.Find("World");
            if (world != null)
            {
                var root = new GameObject("Water Surfaces");
                root.transform.SetParent(world.transform, false);
                return root.transform;
            }
            if (_fallbackRoot == null)
            {
                _fallbackRoot = new GameObject("FUSE Water Surfaces").transform;
                UnityEngine.Object.DontDestroyOnLoad(_fallbackRoot.gameObject);
            }
            return _fallbackRoot;
        }

        private static GameObject RequireWaterSurface(string id)
        {
            var root = GetWaterSurface(id);
            if (root == null)
                throw new InvalidOperationException($"Water surface '{id}' was not found.");
            return root;
        }

        private static void RequireId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("ID is required.", nameof(id));
        }
    }

    public sealed class FuseWaterSurfaceMarker : MonoBehaviour
    {
        public string Id;
        public string SourceLakePath;
        public string MaterialName;
    }
}
