using System;
using System.Collections.Generic;
using UnityEngine;

namespace FUSE.Data
{
    public sealed class FuseWorldDefinition
    {
        public Dictionary<string, FuseScenery> Scenery { get; set; } = new Dictionary<string, FuseScenery>();
        public FuseSpawnPoint[] SpawnPoints { get; set; } = Array.Empty<FuseSpawnPoint>();
        public Dictionary<string, FuseSpliney> Splineys { get; set; } = new Dictionary<string, FuseSpliney>();
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

        public bool ShouldSerializeSuppressScenePaths()
        {
            return false;
        }

        public bool ShouldSerializeSuppressGroups()
        {
            return false;
        }

        public bool ShouldSerializeSuppressAreas()
        {
            return false;
        }
    }

    public sealed class FuseWorldRemovals
    {
        public string[] Scenery { get; set; } = new string[0];
        public string[] Splineys { get; set; } = new string[0];
        public string[] TelegraphPoles { get; set; } = new string[0];
        public string[] MapLabels { get; set; } = new string[0];
        public string[] MapMasks { get; set; } = new string[0];
        public string[] SceneClones { get; set; } = new string[0];
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

        public bool ShouldSerializeDefinitionIdentifier()
        {
            return false;
        }

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
        public FuseSplineyPoint[] Points { get; set; }
    }

    public sealed class FuseSplineyPoint
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public float? Width { get; set; }
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
