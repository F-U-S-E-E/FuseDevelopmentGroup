using RAIL.Data.Common;
using RAIL.Validation;
using UnityEngine;

namespace RAIL.Authoring
{
    public abstract class RailTrackEntity : RailAuthoringEntity
    {
        protected RailTrackEntity(string id = null, string packageId = null)
            : base(id, packageId)
        {
        }

        public override string EntityKind => "track";

        [RailEditable("Group Id", Group = "Track", Order = 10)]
        public string GroupId { get; set; }
    }

    public abstract class RailWorldEntity : RailAuthoringEntity
    {
        protected RailWorldEntity(string id = null, string packageId = null)
            : base(id, packageId)
        {
        }

        public override string EntityKind => "world";

        [RailEditable("Position", Group = "Transform", Order = 10)]
        public Vector3 Position { get; set; }

        [RailEditable("Rotation", Group = "Transform", Order = 20)]
        public Vector3 Rotation { get; set; }

        [RailEditable("Scale", Group = "Transform", Order = 30)]
        public Vector3 Scale { get; set; } = Vector3.one;
    }

    public abstract class RailOperationsEntity : RailAuthoringEntity
    {
        protected RailOperationsEntity(string id = null, string packageId = null)
            : base(id, packageId)
        {
        }

        public override string EntityKind => "operations";
    }

    public abstract class RailSplineEntity : RailWorldEntity
    {
        protected RailSplineEntity(string id = null, string packageId = null)
            : base(id, packageId)
        {
        }

        public override string EntityKind => "spline";

        [RailEditable("Spline Type", Group = "Spline", Order = 10)]
        [RailDropdown("road", "river", "trestle", AllowCustomValue = false)]
        public string SplineType { get; set; } = "road";

        [RailEditable("Profile", Group = "Spline", Order = 20)]
        public string Profile { get; set; }

        [RailEditable("Style", Group = "Spline", Order = 30)]
        public string Style { get; set; }
    }

    public abstract class RailTrackBoundEntity : RailOperationsEntity
    {
        protected RailTrackBoundEntity(string id = null, string packageId = null)
            : base(id, packageId)
        {
        }

        [RailEditable("Track Span Ids", Group = "Track Binding", Order = 10)]
        [RailReference("track-span", AllowNull = true)]
        public string[] TrackSpanIds { get; set; } = new string[0];

        [RailHidden]
        public RailTrackLocation[] RuntimeTrackLocations { get; set; } = new RailTrackLocation[0];
    }

    public class RailConfigurableStructureEntity : RailWorldEntity
    {
        public RailConfigurableStructureEntity(string id = null, string packageId = null)
            : base(id, packageId)
        {
        }

        public override string EntityKind => "configurable-structure";

        [RailEditable("Target Path", Group = "Structure", Order = 10)]
        [RailReference("scene-path", AllowNull = false)]
        public string TargetPath { get; set; }

        [RailEditable("Source Prefab", Group = "Structure", Order = 20)]
        [RailReference("prefab", AllowNull = true)]
        public string Source { get; set; }

        [RailEditable("Enabled", Group = "Structure", Order = 30)]
        public bool Enabled { get; set; } = true;

        [RailHidden]
        public GameObject RuntimeStructure { get; private set; }

        public void LoadDefinition(RAIL.Data.RailSceneClone definition)
        {
            if (definition == null)
            {
                return;
            }

            TargetPath = definition.TargetPath;
            Source = definition.Source;
            Enabled = definition.Enabled ?? true;
            if (definition.LocalPosition.HasValue)
            {
                Position = definition.LocalPosition.Value;
            }

            if (definition.LocalRotation.HasValue)
            {
                Rotation = definition.LocalRotation.Value;
            }

            if (definition.LocalScale.HasValue)
            {
                Scale = definition.LocalScale.Value;
            }

            ClearDirty();
        }

        public RAIL.Data.RailSceneClone ToDefinition()
        {
            return (RAIL.Data.RailSceneClone)BuildRuntimeData();
        }

        public override ValidationResult Validate()
        {
            var result = base.Validate();
            if (string.IsNullOrWhiteSpace(TargetPath))
            {
                result.AddError(nameof(TargetPath), "Configurable structure target path is required.", "rail.authoring.structure.target.required");
            }

            LastValidation = result;
            return result;
        }

        public override object BuildRuntimeData()
        {
            return new RAIL.Data.RailSceneClone
            {
                TargetPath = TargetPath,
                Source = Source,
                Enabled = Enabled,
                LocalPosition = Position,
                LocalRotation = Rotation,
                LocalScale = Scale
            };
        }

        public override bool SaveToDefinition(RAIL.Data.RailModDefinition definition)
        {
            if (definition?.World?.SceneClones == null)
            {
                return false;
            }

            definition.World.SceneClones[Id] = ToDefinition();
            return true;
        }

        public override void ApplyToRuntime()
        {
            var definition = (RAIL.Data.RailSceneClone)BuildRuntimeData();
            RuntimeStructure = RAIL.API.SceneCloneAPI.GetSceneClone(Id);
            if (RuntimeStructure == null)
            {
                RuntimeStructure = RAIL.API.SceneCloneAPI.AddSceneClone(Id, definition);
            }
            else
            {
                RAIL.API.SceneCloneAPI.UpdateSceneClone(Id, definition);
                RuntimeStructure = RAIL.API.SceneCloneAPI.GetSceneClone(Id);
            }

            BindRuntime(RuntimeStructure);
        }

        public override void CaptureFromRuntime()
        {
            var runtime = RuntimeStructure ?? RAIL.API.SceneCloneAPI.GetSceneClone(Id);
            if (runtime == null)
            {
                return;
            }

            Position = runtime.transform.localPosition;
            Rotation = runtime.transform.localEulerAngles;
            Scale = runtime.transform.localScale;
            Enabled = runtime.activeSelf;
            BindRuntime(runtime);
            MarkDirty("captured configurable structure from runtime");
        }
    }

    public abstract class RailIndustryComponentEntity : RailTrackBoundEntity
    {
        protected RailIndustryComponentEntity(string id = null, string packageId = null)
            : base(id, packageId)
        {
        }

        public override string EntityKind => "industry-component";

        [RailEditable("Industry Id", Group = "Industry", Order = 10)]
        [RailReference("industry", AllowNull = false)]
        public string IndustryId { get; set; }

        [RailEditable("Component Type", Group = "Industry", Order = 20)]
        public string ComponentType { get; set; }

        [RailEditable("Load Id", Group = "Industry", Order = 30)]
        [RailReference("load", AllowNull = true)]
        public string LoadId { get; set; }
    }
}
