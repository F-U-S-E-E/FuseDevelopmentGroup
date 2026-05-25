using FUSE.Authoring.Data.Common;
using FUSE.Authoring.Validation;
using UnityEngine;

namespace FUSE.Authoring.Entities
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

        // Track whether the source JSON definition (or a capture from a
        // live GameObject) explicitly specified a local transform value.
        // The base <see cref="FuseWorldEntity"/> declares Position /
        // Rotation / Scale as non-nullable Vector3 properties that
        // default to Vector3.zero / Vector3.one; without these flags we
        // cannot tell "the author set localPosition to the origin" apart
        // from "the author did not specify a position at all". The
        // distinction matters: <see cref="BuildRuntimeData"/> writes
        // FuseSceneClone.LocalPosition (a nullable Vector3) and the apply
        // path in <see cref="FUSE.Runtime.API.SceneCloneAPI.ApplyDefinition"/>
        // ONLY rewrites the live transform.localPosition when
        // LocalPosition.HasValue is true. Without the flags, every
        // scene-clone definition that omitted localPosition would silently
        // zero the live transform on apply — which is how a
        // <c>{ "enabled": true }</c> mandela on the vanilla
        // <c>World/Large Scenery/Bryson/Freight House</c> path was
        // collapsing the building's local (202.36, 1.0, 210.45) to
        // (0, 0, 0), teleporting it from its intended spot by the
        // Bryson freight house track onto the parent Bryson container's
        // origin (which happens to overlap Lego's Scrappalachia yard).
        private bool _hasLocalPosition;
        private bool _hasLocalRotation;
        private bool _hasLocalScale;

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

        public void LoadDefinition(FUSE.Authoring.Data.FuseSceneClone definition)
        {
            if (definition == null)
            {
                return;
            }

            TargetPath = definition.TargetPath;
            Source = definition.Source;
            Enabled = definition.Enabled ?? true;
            // Track each transform component independently — a definition
            // can specify any subset (e.g. only LocalRotation) and we must
            // preserve that subset through the round-trip so apply does
            // not zero the unspecified ones. See the
            // _hasLocalPosition/Rotation/Scale field comments above.
            _hasLocalPosition = definition.LocalPosition.HasValue;
            _hasLocalRotation = definition.LocalRotation.HasValue;
            _hasLocalScale = definition.LocalScale.HasValue;
            if (_hasLocalPosition)
            {
                Position = definition.LocalPosition.Value;
            }

            if (_hasLocalRotation)
            {
                Rotation = definition.LocalRotation.Value;
            }

            if (_hasLocalScale)
            {
                Scale = definition.LocalScale.Value;
            }

            ClearDirty();
        }

        public FUSE.Authoring.Data.FuseSceneClone ToDefinition()
        {
            return (FUSE.Authoring.Data.FuseSceneClone)BuildRuntimeData();
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
            // Only emit a non-null LocalPosition / LocalRotation / LocalScale
            // when the source definition (JSON) or a runtime capture
            // actually provided one. The apply path treats
            // <c>LocalPosition.HasValue == true</c> as "force the live
            // transform to this value" — and the inherited
            // <see cref="FuseWorldEntity.Position"/> property's default
            // of <c>Vector3.zero</c> would otherwise be indistinguishable
            // from an authored origin, silently teleporting the bound
            // GameObject to its parent's origin on every apply.
            return new FUSE.Authoring.Data.FuseSceneClone
            {
                TargetPath = TargetPath,
                Source = Source,
                Enabled = Enabled,
                LocalPosition = _hasLocalPosition ? (Vector3?)Position : null,
                LocalRotation = _hasLocalRotation ? (Vector3?)Rotation : null,
                LocalScale = _hasLocalScale ? (Vector3?)Scale : null
            };
        }

        public override bool SaveToDefinition(FUSE.Authoring.Data.FuseModDefinition definition)
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
            var definition = (FUSE.Authoring.Data.FuseSceneClone)BuildRuntimeData();
            RuntimeStructure = FUSE.Runtime.API.SceneCloneAPI.GetSceneClone(Id);
            if (RuntimeStructure == null)
            {
                RuntimeStructure = FUSE.Runtime.API.SceneCloneAPI.AddSceneClone(Id, definition);
            }
            else
            {
                FUSE.Runtime.API.SceneCloneAPI.UpdateSceneClone(Id, definition);
                RuntimeStructure = FUSE.Runtime.API.SceneCloneAPI.GetSceneClone(Id);
            }

            BindRuntime(RuntimeStructure);
        }

        public override void CaptureFromRuntime()
        {
            var runtime = RuntimeStructure ?? FUSE.Runtime.API.SceneCloneAPI.GetSceneClone(Id);
            if (runtime == null)
            {
                return;
            }

            Position = runtime.transform.localPosition;
            Rotation = runtime.transform.localEulerAngles;
            Scale = runtime.transform.localScale;
            // A capture is an explicit "snapshot the live transform" act,
            // so promote all three components to "specified" — the user's
            // intent is that BuildRuntimeData round-trips these values
            // back into the definition rather than silently dropping them.
            _hasLocalPosition = true;
            _hasLocalRotation = true;
            _hasLocalScale = true;
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
