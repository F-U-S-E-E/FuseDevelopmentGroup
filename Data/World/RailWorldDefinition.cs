using System;
using System.Collections.Generic;
using UnityEngine;

namespace RAIL.Data
{
    public sealed class RailWorldDefinition
    {
        public Dictionary<string, RailScenery> Scenery { get; set; } = new Dictionary<string, RailScenery>();
        public RailSpawnPoint[] SpawnPoints { get; set; } = Array.Empty<RailSpawnPoint>();
        public Dictionary<string, RailSpliney> Splineys { get; set; } = new Dictionary<string, RailSpliney>();
        public Dictionary<string, RailTelegraphPoles> TelegraphPoles { get; set; } = new Dictionary<string, RailTelegraphPoles>();
        public RailTelegraphPoleMovement[] TelegraphPoleMovements { get; set; } = Array.Empty<RailTelegraphPoleMovement>();
        public Dictionary<string, RailMapLabel> MapLabels { get; set; } = new Dictionary<string, RailMapLabel>();
        public Dictionary<string, RailMapMask> MapMasks { get; set; } = new Dictionary<string, RailMapMask>();
        public Dictionary<string, RailMapTileSource> MapTiles { get; set; } = new Dictionary<string, RailMapTileSource>();
        public Dictionary<string, RailSceneClone> SceneClones { get; set; } = new Dictionary<string, RailSceneClone>();
        public string[] SuppressBaseScenePaths { get; set; } = Array.Empty<string>();
        public string[] SuppressBaseTrackGroups { get; set; } = Array.Empty<string>();
        public string[] SuppressBaseAreas { get; set; } = Array.Empty<string>();
        public string[] SuppressScenePaths { get; set; }
        public string[] SuppressGroups { get; set; }
        public string[] SuppressAreas { get; set; }
        public RailWorldRemovals Removals { get; set; } = new RailWorldRemovals();

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

    public sealed class RailWorldRemovals
    {
        public string[] Scenery { get; set; } = new string[0];
        public string[] Splineys { get; set; } = new string[0];
        public string[] TelegraphPoles { get; set; } = new string[0];
        public string[] MapLabels { get; set; } = new string[0];
        public string[] MapMasks { get; set; } = new string[0];
        public string[] SceneClones { get; set; } = new string[0];
    }

    public sealed class RailScenery
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

    public sealed class RailSpawnPoint
    {
        public string Name { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public float? Radius { get; set; }
        public int? Priority { get; set; }
    }

    public sealed class RailSpliney
    {
        public string Type { get; set; }
        public string Profile { get; set; }
        public string Style { get; set; }
        public float OffsetY { get; set; }
        public string HeadStyle { get; set; }
        public string TailStyle { get; set; }
        public RailSplineyPoint[] Points { get; set; }
    }

    public sealed class RailSplineyPoint
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public float? Width { get; set; }
    }

    public sealed class RailTelegraphPoles
    {
        public string Profile { get; set; }
        public string PolePrefab { get; set; }
        public string WirePrefab { get; set; }
        public float? Spacing { get; set; }
        public Vector3[] Points { get; set; }
    }

    public sealed class RailTelegraphPoleMovement
    {
        public int[] PoleIndices { get; set; } = Array.Empty<int>();
        public Vector3 Offset { get; set; }
    }

    public sealed class RailMapLabel
    {
        public string Text { get; set; }
        public string Style { get; set; }
        public int? SpeedLimitMph { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public float? Size { get; set; }
        public string Color { get; set; }
    }

    public sealed class RailMapMask
    {
        public string Type { get; set; }
        public Vector3 Center { get; set; }
        public Vector3 Rotation { get; set; }
        public float? Radius { get; set; }
        public Vector3? Size { get; set; }
        public float? Width { get; set; }
        public Vector3[] Points { get; set; }
    }

    public sealed class RailMapTileSource
    {
        public string Directory { get; set; }
        public string SourceFolder { get; set; }
        public int Priority { get; set; }
    }

    public sealed class RailSceneClone
    {
        public string TargetPath { get; set; }
        public string Source { get; set; }
        public bool? Enabled { get; set; }
        public Vector3? LocalPosition { get; set; }
        public Vector3? LocalRotation { get; set; }
        public Vector3? LocalScale { get; set; }
    }
}
