using System.Collections.Generic;
using Helpers;
using FUSE.Runtime.API;
using FUSE.Authoring.Data;
using FUSE.Authoring.Validation;
using UnityEngine;

namespace FUSE.Authoring.Entities
{
    public sealed class FuseSceneryEntity : FuseWorldEntity
    {
        public FuseSceneryEntity(string id = null, string packageId = null)
            : base(id, packageId)
        {
        }

        public override string EntityKind => "scenery";

        /// <summary>
        /// User-facing display label. Never sent to PrefabStore / SceneryAssetManager.
        /// </summary>
        [FuseEditable("Model", Group = "Scenery", Order = 10)]
        public string Model { get; set; }

        /// <summary>
        /// PrefabStore asset identifier. The only value passed to
        /// SceneryAssetInstance.identifier or SceneryAssetManager.LoadScenery.
        /// </summary>
        [FuseEditable("Asset Identifier", Group = "Scenery", Order = 11)]
        [FuseDropdown(SourceMember = nameof(GetAvailableModels), AllowCustomValue = true)]
        [FuseReference("scenery-model", AllowNull = false)]
        public string AssetIdentifier { get; set; }

        [FuseHidden]
        public SceneryAssetInstance RuntimeScenery { get; private set; }

        [FuseHidden]
        public Bounds RuntimeBounds { get; private set; }

        [FuseEditable("Anchor Spans", Group = "Scenery", Order = 12)]
        public string[] AnchorSpanIds { get; set; } = System.Array.Empty<string>();

        public void LoadDefinition(FuseScenery definition)
        {
            if (definition == null)
            {
                return;
            }

            Model = definition.Model;
            // Legacy packages stored the asset identifier in Model. If AssetIdentifier
            // is missing we mirror Model so the entity can still apply.
            AssetIdentifier = !string.IsNullOrWhiteSpace(definition.AssetIdentifier)
                ? definition.AssetIdentifier
                : definition.Model;
            Position = definition.Position;
            Rotation = definition.Rotation;
            Scale = definition.Scale == default ? Vector3.one : definition.Scale;
            AnchorSpanIds = definition.AnchorSpanIds ?? System.Array.Empty<string>();
            ClearDirty();
        }

        public FuseScenery ToDefinition()
        {
            return (FuseScenery)BuildRuntimeData();
        }

        public override ValidationResult Validate()
        {
            var result = base.Validate();
            if (string.IsNullOrWhiteSpace(AssetIdentifier) && string.IsNullOrWhiteSpace(Model))
            {
                result.AddError(nameof(AssetIdentifier), "Scenery requires an AssetIdentifier (or legacy Model).", "fuse.authoring.scenery.assetIdentifier.required");
            }

            LastValidation = result;
            return result;
        }

        public override object BuildRuntimeData()
        {
            return new FuseScenery
            {
                Model = Model,
                AssetIdentifier = AssetIdentifier,
                Position = Position,
                Rotation = Rotation,
                Scale = Scale == default ? Vector3.one : Scale,
                AnchorSpanIds = AnchorSpanIds ?? System.Array.Empty<string>()
            };
        }

        public override bool SaveToDefinition(FuseModDefinition definition)
        {
            if (definition?.World?.Scenery == null)
            {
                return false;
            }

            definition.World.Scenery[Id] = ToDefinition();
            return true;
        }

        public override void ApplyToRuntime()
        {
            var definition = ToDefinition();
            RuntimeScenery = SceneryAPI.GetScenery(Id);
            if (RuntimeScenery == null)
            {
                RuntimeScenery = SceneryAPI.AddScenery(Id, definition, RefreshRuntimeBoundsAfterDeferredActivation);
            }
            else
            {
                SceneryAPI.UpdateScenery(Id, definition);
                RuntimeScenery = SceneryAPI.GetScenery(Id);
            }

            UpdateRuntimeBounds();
            BindRuntime(RuntimeScenery != null ? RuntimeScenery.gameObject : null, RuntimeScenery);
        }

        public override void CaptureFromRuntime()
        {
            var runtime = RuntimeScenery ?? SceneryAPI.GetScenery(Id);
            if (runtime == null)
            {
                return;
            }

            // runtime.identifier is the asset identifier (e.g. PrefabStore key),
            // never the display label.
            AssetIdentifier = runtime.identifier;
            if (string.IsNullOrWhiteSpace(Model))
            {
                Model = runtime.identifier;
            }

            Position = runtime.transform.localPosition;
            Rotation = runtime.transform.localEulerAngles;
            Scale = runtime.transform.localScale;
            RuntimeScenery = runtime;
            UpdateRuntimeBounds();
            BindRuntime(runtime.gameObject, runtime);
            MarkDirty("captured scenery from runtime");
        }

        public static IEnumerable<string> GetAvailableModels()
        {
            return SceneryAPI.GetAvailableSceneryModels();
        }

        /// <summary>
        /// Re-computes runtime bounds after the deferred scenery activator finally
        /// activates this scenery; its renderers do not exist until activation. No-op
        /// for scenery that was activated inline (bounds were already computed).
        /// </summary>
        internal void RefreshRuntimeBoundsAfterDeferredActivation()
        {
            UpdateRuntimeBounds();
        }

        private void UpdateRuntimeBounds()
        {
            RuntimeBounds = default;
            var runtime = RuntimeScenery;
            if (runtime == null)
            {
                return;
            }

            var renderers = runtime.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            RuntimeBounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                RuntimeBounds.Encapsulate(renderers[index].bounds);
            }
        }
    }
}
