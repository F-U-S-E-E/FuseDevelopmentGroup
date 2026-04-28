using System.Collections.Generic;
using UnityEngine;

namespace RAIL.Data
{
    public sealed class RailWorldDefinition
    {
        public Dictionary<string, RailScenery> Scenery { get; set; } = new Dictionary<string, RailScenery>();
        public Dictionary<string, RailSpliney> Splineys { get; set; } = new Dictionary<string, RailSpliney>();
        public Dictionary<string, RailTelegraphPoles> TelegraphPoles { get; set; } = new Dictionary<string, RailTelegraphPoles>();
        public Dictionary<string, RailMapLabel> MapLabels { get; set; } = new Dictionary<string, RailMapLabel>();
        public Dictionary<string, RailMapMask> MapMasks { get; set; } = new Dictionary<string, RailMapMask>();
        public Dictionary<string, RailMapTileSource> MapTiles { get; set; } = new Dictionary<string, RailMapTileSource>();
        public Dictionary<string, RailSceneClone> SceneClones { get; set; } = new Dictionary<string, RailSceneClone>();
    }

    public sealed class RailScenery
    {
        public string Model { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public Vector3 Scale { get; set; } = Vector3.one;
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
