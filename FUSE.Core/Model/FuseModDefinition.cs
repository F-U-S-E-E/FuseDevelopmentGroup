using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using Fuse.Core.Serialization.Converters;

namespace Fuse.Core.Model
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
        public string[] Tags { get; set; } = Array.Empty<string>();
        public string CoordinateSpace { get; set; } = "world";
        public FuseMapDeclaration Map { get; set; }
        public FuseMixintoDefinition Mixinto { get; set; }
        public FuseTrackDefinition Tracks { get; set; } = new FuseTrackDefinition();
        public FuseOperationsDefinition Operations { get; set; } = new FuseOperationsDefinition();
        public FuseWorldDefinition World { get; set; } = new FuseWorldDefinition();
        public FuseAudioRoot Audio { get; set; } = new FuseAudioRoot();
        public FuseProgressionRoot Progression { get; set; } = new FuseProgressionRoot();
        public Dictionary<string, FuseModSettingDefinition> Settings { get; set; } = new Dictionary<string, FuseModSettingDefinition>();
        public Dictionary<string, FuseFeatureRule> FeatureRules { get; set; } = new Dictionary<string, FuseFeatureRule>();
        public FuseEditorState Editor { get; set; }
        public Dictionary<string, object> Extensions { get; set; } = new Dictionary<string, object>();
    }

    public sealed class FuseMixintoDefinition
    {
        /// <summary>
        /// Legacy mixinto target name, such as game-graph or progressions.
        /// FUSE keeps this as provenance; runtime behavior is driven by the
        /// fragment's normal FUSE sections plus Requires.
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// Legacy source file that produced this FUSE fragment.
        /// </summary>
        public string SourceFile { get; set; }

        /// <summary>
        /// Optional mod requirements mirroring legacy conditional mixintos.
        /// If any requirement is not satisfied, FUSE skips this fragment
        /// instead of treating it as a package fault.
        /// </summary>
        public FuseModRequirement[] Requires { get; set; }

        /// <summary>
        /// Optional legacy incompatibility references. If any matching package
        /// is enabled, only this conditional fragment is skipped.
        /// </summary>
        public FuseModRequirement[] ConflictsWith { get; set; }
    }

    public sealed class FuseModRequirement
    {
        public string Id { get; set; }
        public string NotBefore { get; set; }
        public string NotAfter { get; set; }
    }
}
