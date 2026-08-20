using System;
using System.Collections.Generic;
using UnityEngine;

namespace FUSE.Authoring.Data
{
    public sealed class FuseWorldDefinition
    {
        public Dictionary<string, FuseScenery> Scenery { get; set; } = new Dictionary<string, FuseScenery>();
        public FuseSpawnPoint[] SpawnPoints { get; set; } = Array.Empty<FuseSpawnPoint>();
        public Dictionary<string, FuseSpliney> Splineys { get; set; } = new Dictionary<string, FuseSpliney>();
        public Dictionary<string, FuseWaterSurface> WaterSurfaces { get; set; } = new Dictionary<string, FuseWaterSurface>();
        public Dictionary<string, FuseTelegraphPoles> TelegraphPoles { get; set; } = new Dictionary<string, FuseTelegraphPoles>();
        public FuseTelegraphPoleMovement[] TelegraphPoleMovements { get; set; } = Array.Empty<FuseTelegraphPoleMovement>();
        public Dictionary<string, FuseMapLabel> MapLabels { get; set; } = new Dictionary<string, FuseMapLabel>();
        public Dictionary<string, FuseMapMask> MapMasks { get; set; } = new Dictionary<string, FuseMapMask>();
        public Dictionary<string, FuseMapTileSource> MapTiles { get; set; } = new Dictionary<string, FuseMapTileSource>();
        public Dictionary<string, FuseSceneClone> SceneClones { get; set; } = new Dictionary<string, FuseSceneClone>();
        public string[] SuppressBaseScenePaths { get; set; } = Array.Empty<string>();
        public string[] SuppressBaseTrackGroups { get; set; } = Array.Empty<string>();
        public string[] SuppressBaseAreas { get; set; } = Array.Empty<string>();
        public string[] SuppressScenePaths { get; set; }
        public string[] SuppressGroups { get; set; }
        public string[] SuppressAreas { get; set; }
        public FuseWorldRemovals Removals { get; set; } = new FuseWorldRemovals();

        // ShouldSerializeXxx methods are Json.NET convention — they MUST be
        // instance methods for the serializer to discover them by reflection.
#pragma warning disable CA1822 // Mark members as static
        public bool ShouldSerializeSuppressScenePaths() => false;
        public bool ShouldSerializeSuppressGroups() => false;
        public bool ShouldSerializeSuppressAreas() => false;
#pragma warning restore CA1822
    }

    public sealed class FuseWorldRemovals
    {
        public string[] Scenery { get; set; } = Array.Empty<string>();
        public string[] Splineys { get; set; } = Array.Empty<string>();
        public string[] WaterSurfaces { get; set; } = Array.Empty<string>();
        public string[] TelegraphPoles { get; set; } = Array.Empty<string>();
        public string[] MapLabels { get; set; } = Array.Empty<string>();
        public string[] MapMasks { get; set; } = Array.Empty<string>();
        public string[] SceneClones { get; set; } = Array.Empty<string>();
    }

    public sealed class FuseScenery
    {
        /// <summary>
        /// Display/label field. Never used as a PrefabStore asset key.
        /// May contain user-facing names like "Camp 1" or "Mess Hall".
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// PrefabStore / SceneryAssetManager asset identifier (a.k.a. modelIdentifier).
        /// This is the only field that may be passed to SceneryAssetInstance.identifier
        /// or to SceneryAssetManager.LoadScenery.
        /// </summary>
        public string AssetIdentifier { get; set; }

        /// <summary>
        /// Alias kept for forward-compat with authoring tooling that uses
        /// "definition identifier" terminology. Mirrors AssetIdentifier.
        /// </summary>
        public string DefinitionIdentifier
        {
            get { return AssetIdentifier; }
            set { AssetIdentifier = value; }
        }

        // Json.NET convention — must be instance for the serializer to discover it.
#pragma warning disable CA1822 // Mark members as static
        public bool ShouldSerializeDefinitionIdentifier() => false;
#pragma warning restore CA1822

        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public Vector3 Scale { get; set; } = Vector3.one;
        public string[] AnchorSpanIds { get; set; } = Array.Empty<string>();
    }

    public sealed class FuseSpawnPoint
    {
        public string Name { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public float? Radius { get; set; }
        public int? Priority { get; set; }
    }

    public sealed class FuseSpliney
    {
        public string Type { get; set; }
        public string Profile { get; set; }
        public string Style { get; set; }
        public float OffsetY { get; set; }
        public string HeadStyle { get; set; }
        public string TailStyle { get; set; }
        public string AssetIdentifier { get; set; }
        public string Prefab { get; set; }
        public float Spacing { get; set; } = 5f;
        public Vector3 InstanceScale { get; set; } = Vector3.one;
        public Vector3 RotationOffset { get; set; }
        public float LateralOffset { get; set; }
        public float VerticalOffset { get; set; }
        public bool SnapToTerrain { get; set; }
        public bool AlignToSlope { get; set; }
        public bool PlaceAtEnd { get; set; } = true;
        public int MaximumInstances { get; set; } = 1024;
        public FuseSplineyPoint[] Points { get; set; }
    }

    public sealed class FuseSplineyPoint
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public float? Width { get; set; }
    }

    /// <summary>
    /// A flat or terrain-following lake polygon. Points are authored in world
    /// coordinates. FUSE reuses a loaded Railroader water material/profile so
    /// native map packages do not need to copy game assets.
    /// </summary>
    public sealed class FuseWaterSurface
    {
        public Vector3[] Points { get; set; } = Array.Empty<Vector3>();
        public string SourceLakePath { get; set; }
        public string MaterialName { get; set; }
        public bool LockHeight { get; set; } = true;
        public bool SnapToTerrain { get; set; }
        public bool EnableCollider { get; set; } = true;
        public float UvScale { get; set; } = 1f;
        public float TriangleDensity { get; set; } = 0.2f;
        public float MaximumTriangleArea { get; set; } = 50f;
        public float YOffset { get; set; }
    }

    public sealed class FuseTelegraphPoles
    {
        public string Profile { get; set; }
        public string PolePrefab { get; set; }
        public string WirePrefab { get; set; }
        public float? Spacing { get; set; }
        public Vector3[] Points { get; set; }
    }

    public sealed class FuseTelegraphPoleMovement
    {
        public int[] PoleIndices { get; set; } = Array.Empty<int>();
        public Vector3 Offset { get; set; }
    }

    public sealed class FuseMapLabel
    {
        public string Text { get; set; }
        public string Style { get; set; }
        public int? SpeedLimitMph { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public float? Size { get; set; }
        public string Color { get; set; }
    }

    public sealed class FuseMapMask
    {
        public string Type { get; set; }
        public Vector3 Center { get; set; }
        public Vector3 Rotation { get; set; }
        public float? Radius { get; set; }
        public Vector3? Size { get; set; }
        public float? Width { get; set; }
        public Vector3[] Points { get; set; }

        // Restored from AlinasMapMod — these were configurable in the old API
        // but were dropped and hardcoded in the initial FUSE implementation.
        /// <summary>How far the mask edge blends into surrounding terrain. Defaults to 0 if not set.</summary>
        public float? Falloff { get; set; }
        /// <summary>Whether the mask flattens terrain to a fixed height. Defaults to false if not set.</summary>
        public bool? EnableSetHeight { get; set; }
        /// <summary>Whether the mask clears trees. Defaults to true if not set.</summary>
        public bool? EnableCutTrees { get; set; }
        /// <summary>Whether the mask modifier is active. Defaults to true if not set.</summary>
        public bool? EnableMaskModifier { get; set; }
        /// <summary>Which named mask layer this belongs to. Defaults to MaskName.Object if not set.</summary>
        public MaskName? MaskName { get; set; }
        /// <summary>Evaluation order among masks. Defaults to 0 if not set.</summary>
        public int? Order { get; set; }
    }

    public sealed class FuseMapTileSource
    {
        public string Directory { get; set; }
        public string SourceFolder { get; set; }
        public int Priority { get; set; }
    }

    public sealed class FuseSceneClone
    {
        public string TargetPath { get; set; }
        public string Source { get; set; }
        public bool? Enabled { get; set; }
        public Vector3? LocalPosition { get; set; }
        public Vector3? LocalRotation { get; set; }
        public Vector3? LocalScale { get; set; }
    }

    /// <summary>
    /// Named mask layers used by FuseMapMask.MaskName.
    /// Add or adjust members to match the rest of your codebase if other names are required.
    /// </summary>
    public enum MaskName
    {
        Object,
        Terrain,
        Road,
        // Add other named layers here as needed
    }
}
