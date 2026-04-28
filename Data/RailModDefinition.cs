using System.Collections.Generic;

namespace RAIL.Data
{
    public sealed class RailModDefinition
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string ModVersion { get; set; } = "1.0.0";
        public string RailroaderVersion { get; set; }
        public string Description { get; set; }
        public string CoordinateSpace { get; set; } = "world";
        public RailTrackDefinition Tracks { get; set; } = new RailTrackDefinition();
        public RailOperationsDefinition Operations { get; set; } = new RailOperationsDefinition();
        public RailWorldDefinition World { get; set; } = new RailWorldDefinition();
        public RailProgressionRoot Progression { get; set; } = new RailProgressionRoot();
        public RailEditorState Editor { get; set; }
        public Dictionary<string, object> Extensions { get; set; } = new Dictionary<string, object>();
    }
}
