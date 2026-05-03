using System.Collections.Generic;

using Newtonsoft.Json;
using FUSE.Serialization.Converters;

namespace FUSE.Data
{
    public sealed class FuseModDefinition
    {
        [JsonConverter(typeof(FuseSchemaVersionJsonConverter))]
        public string SchemaVersion { get; set; } = "1.0";
        public string Id { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string ModVersion { get; set; } = "1.0.0";
        public string RailroaderVersion { get; set; }
        public string Description { get; set; }
        public string CoordinateSpace { get; set; } = "world";
        public FuseTrackDefinition Tracks { get; set; } = new FuseTrackDefinition();
        public FuseOperationsDefinition Operations { get; set; } = new FuseOperationsDefinition();
        public FuseWorldDefinition World { get; set; } = new FuseWorldDefinition();
        public FuseAudioRoot Audio { get; set; } = new FuseAudioRoot();
        public FuseProgressionRoot Progression { get; set; } = new FuseProgressionRoot();
        public FuseEditorState Editor { get; set; }
        public Dictionary<string, object> Extensions { get; set; } = new Dictionary<string, object>();
    }
}
