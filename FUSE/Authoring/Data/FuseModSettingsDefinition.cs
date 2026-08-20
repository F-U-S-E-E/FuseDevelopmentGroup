using System;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FUSE.Authoring.Data
{
    public sealed class FuseModSettingDefinition
    {
        public string Type { get; set; } = "text";
        public string Label { get; set; }
        public string Description { get; set; }
        public string Scope { get; set; } = "user";
        [JsonProperty("default")]
        public JToken Default { get; set; }
        public string[] Values { get; set; } = Array.Empty<string>();
        public double? Min { get; set; }
        public double? Max { get; set; }
        public double? Step { get; set; }
        public bool Advanced { get; set; }
        public bool ReloadRequired { get; set; }
    }

    /// <summary>
    /// Conditionally includes a named set of authored objects based on one
    /// package setting. Rules are evaluated after schema validation and before
    /// the definition becomes resident; changing the setting therefore takes
    /// effect on the next map/package reload.
    /// </summary>
    public sealed class FuseFeatureRule
    {
        public string Setting { get; set; }
        public string Operator { get; set; } = "equals";
        public JToken Value { get; set; }
        public FuseFeatureTargets Targets { get; set; } = new FuseFeatureTargets();
    }

    public sealed class FuseFeatureTargets
    {
        public string[] TrackNodes { get; set; } = Array.Empty<string>();
        public string[] TrackSegments { get; set; } = Array.Empty<string>();
        public string[] TrackSpans { get; set; } = Array.Empty<string>();
        public string[] TrackAreas { get; set; } = Array.Empty<string>();
        public string[] Loads { get; set; } = Array.Empty<string>();
        public string[] Industries { get; set; } = Array.Empty<string>();
        public string[] IndustryComponents { get; set; } = Array.Empty<string>();
        public string[] Loaders { get; set; } = Array.Empty<string>();
        public string[] Turntables { get; set; } = Array.Empty<string>();
        public string[] Stations { get; set; } = Array.Empty<string>();
        public string[] Scenery { get; set; } = Array.Empty<string>();
        public string[] Splineys { get; set; } = Array.Empty<string>();
        public string[] WaterSurfaces { get; set; } = Array.Empty<string>();
        public string[] TelegraphPoles { get; set; } = Array.Empty<string>();
        public string[] MapLabels { get; set; } = Array.Empty<string>();
        public string[] MapMasks { get; set; } = Array.Empty<string>();
        public string[] MapTiles { get; set; } = Array.Empty<string>();
        public string[] SceneClones { get; set; } = Array.Empty<string>();
        public string[] Progressions { get; set; } = Array.Empty<string>();
        public string[] MapFeatures { get; set; } = Array.Empty<string>();
        public string[] Whistles { get; set; } = Array.Empty<string>();
        public string[] Horns { get; set; } = Array.Empty<string>();
        public string[] Bells { get; set; } = Array.Empty<string>();
    }
}
