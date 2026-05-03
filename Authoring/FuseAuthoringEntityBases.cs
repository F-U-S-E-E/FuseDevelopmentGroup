using FUSE.Data.Common;
using FUSE.Validation;
using UnityEngine;

namespace FUSE.Authoring
{
    public abstract class FuseTrackEntity : FuseAuthoringEntity
    {
        protected FuseTrackEntity(string id = null, string packageId = null)
            : base(id, packageId)
        {
        }

        public override string EntityKind => "track";

        [FuseEditable("Group Id", Group = "Track", Order = 10)]
        public string GroupId { get; set; }
    }

    public abstract class FuseWorldEntity : FuseAuthoringEntity
    {
        protected FuseWorldEntity(string id = null, string packageId = null)
            : base(id, packageId)
        {
        }

        public override string EntityKind => "world";

        [FuseEditable("Position", Group = "Transform", Order = 10)]
        public Vector3 Position { get; set; }

        [FuseEditable("Rotation", Group = "Transform", Order = 20)]
        public Vector3 Rotation { get; set; }

        [FuseEditable("Scale", Group = "Transform", Order = 30)]
        public Vector3 Scale { get; set; } = Vector3.one;
    }

    public abstract class FuseOperationsEntity : FuseAuthoringEntity
    {
        protected FuseOperationsEntity(string id = null, string packageId = null)
            : base(id, packageId)
        {
        }

        public override string EntityKind => "operations";
    }

    public abstract class FuseSplineEntity : FuseWorldEntity
    {
        protected FuseSplineEntity(string id = null, string packageId = null)
            : base(id, packageId)
        {
        }

        public override string EntityKind => "spline";

        [FuseEditable("Spline Type", Group = "Spline", Order = 10)]
        [FuseDropdown("road", "river", "trestle", AllowCustomValue = false)]
        public string SplineType { get; set; } = "road";

        [FuseEditable("Profile", Group = "Spline", Order = 20)]
        public string Profile { get; set; }

        [FuseEditable("Style", Group = "Spline", Order = 30)]
        public string Style { get; set; }
    }

    public abstract class FuseTrackBoundEntity : FuseOperationsEntity
    {
        protected FuseTrackBoundEntity(string id = null, string packageId = null)
            : base(id, packageId)
        {
        }

        [FuseEditable("Track Span Ids", Group = "Track Binding", Order = 10)]
        [FuseReference("track-span", AllowNull = true)]
        public string[] TrackSpanIds { get; set; } = new string[0];

        [FuseHidden]
        public FuseTrackLocation[] RuntimeTrackLocations { get; set; } = new FuseTrackLocation[0];
    }

    public class FuseConfigurableStructureEntity : FuseWorldEntity
    {
        public FuseConfigurableStructureEntity(string id = null, string packageId = null)
            : base(id, packageId)
        {
        }

        public override string EntityKind => "configurable-structure";

        [FuseEditable("Target Path", Group = "Structure", Order = 10)]
        [FuseReference("scene-path", AllowNull = false)]
        public string TargetPath { get; set; }

        [FuseEditable("Source Prefab", Group = "Structure", Order = 20)]
        [FuseReference("prefab", AllowNull = true)]
        public string Source { get; set; }

        [FuseEditable("Enabled", Group = "Structure", Order = 30)]
        public bool Enabled { get; set; } = true;

        [FuseHidden]
        public GameObject RuntimeStructure { get; private set; }

        public void LoadDefinition(FUSE.Data.FuseSceneClone definition)
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

        public FUSE.Data.FuseSceneClone ToDefinition()
        {
            return (FUSE.Data.FuseSceneClone)BuildRuntimeData();
        }

        public override ValidationResult Validate()
        {
            var result = base.Validate();
            if (string.IsNullOrWhiteSpace(TargetPath))
            {
                result.AddError(nameof(TargetPath), "Configurable structure target path is required.", "fuse.authoring.structure.target.required");
            }

            LastValidation = result;
            return result;
        }

        public override object BuildRuntimeData()
        {
            return new FUSE.Data.FuseSceneClone
            {
                TargetPath = TargetPath,
                Source = Source,
                Enabled = Enabled,
                LocalPosition = Position,
                LocalRotation = Rotation,
                LocalScale = Scale
            };
        }

        public override bool SaveToDefinition(FUSE.Data.FuseModDefinition definition)
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
            var definition = (FUSE.Data.FuseSceneClone)BuildRuntimeData();
            RuntimeStructure = FUSE.API.SceneCloneAPI.GetSceneClone(Id);
            if (RuntimeStructure == null)
            {
                RuntimeStructure = FUSE.API.SceneCloneAPI.AddSceneClone(Id, definition);
            }
            else
            {
                FUSE.API.SceneCloneAPI.UpdateSceneClone(Id, definition);
                RuntimeStructure = FUSE.API.SceneCloneAPI.GetSceneClone(Id);
            }

            BindRuntime(RuntimeStructure);
        }

        public override void CaptureFromRuntime()
        {
            var runtime = RuntimeStructure ?? FUSE.API.SceneCloneAPI.GetSceneClone(Id);
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

    public abstract class FuseIndustryComponentEntity : FuseTrackBoundEntity
    {
        protected FuseIndustryComponentEntity(string id = null, string packageId = null)
            : base(id, packageId)
        {
        }

        public override string EntityKind => "industry-component";

        [FuseEditable("Industry Id", Group = "Industry", Order = 10)]
        [FuseReference("industry", AllowNull = false)]
        public string IndustryId { get; set; }

        [FuseEditable("Component Type", Group = "Industry", Order = 20)]
        public string ComponentType { get; set; }

        [FuseEditable("Load Id", Group = "Industry", Order = 30)]
        [FuseReference("load", AllowNull = true)]
        public string LoadId { get; set; }
    }
}
