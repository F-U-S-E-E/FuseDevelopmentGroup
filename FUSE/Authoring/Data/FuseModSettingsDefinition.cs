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
}
