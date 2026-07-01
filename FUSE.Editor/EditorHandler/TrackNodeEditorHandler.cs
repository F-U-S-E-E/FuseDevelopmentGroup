using FUSE.Authoring.Data;
using FUSE.Editor.Overlays;
using FUSE.Editor.Resources;
using FUSE.Editor.Screen;
using FUSE.Infrastructure;
using Helpers;
using RLD;
using System;
using System.Collections.Generic;
using Track;
using UnityEngine;

namespace FUSE.Editor.EditorHandler
{
    /// <summary>
    /// Concrete EditorHandler for TrackNode entities.
    /// Manages rendering, transform, and data operations for track nodes in the editor.
    /// </summary>
    public class TrackNodeEditorHandler : EditorHandlerBase
    {
        private static Material _renderMaterial;
        private static Mesh _previewMesh;
        private bool _isVisible = true;
        private OverlaySelectionArea[] _selectionAreas;

        private TrackNode TrackNode => Entity as TrackNode;
        private FuseNode FuseNodeData => FuseData as FuseNode;

        /// <summary>
        /// Creates a new TrackNodeEditorHandler.
        /// </summary>
        /// <param name="id">Unique identifier for this handler.</param>
        /// <param name="entity">The TrackNode entity being edited.</param>
        /// <param name="previewData">The preview data (e.g., FuseNode).</param>
        public TrackNodeEditorHandler(object entity)
        {
            Entity = entity;
            ID = TrackNode.id;
            FuseData = FUSE.Runtime.API.TrackAPI.CloneNodeDefinition(FUSE.Runtime.API.TrackAPI.GetDefinition(TrackNode));

            InitializeRenderingResources();
        }

        /// <summary>
        /// Whether this handler's preview is currently visible.
        /// </summary>
        public override bool IsVisible
        {
            get => _isVisible;
            set => _isVisible = value;
        }

        /// <summary>
        /// The selectable areas within this preview for click-based selection.
        /// </summary>
        public override OverlaySelectionArea[] SelectionAreas
        {
            get
            {
                if (_selectionAreas == null)
                {
                    // Create a simple sphere-based selection area
                    _selectionAreas = new OverlaySelectionArea[]
                    {
                        new OverlaySelectionArea()
                        {
                            AreaId = "Node",
                            PreviewId = ID,
                            Bounds = new Bounds(Vector3.zero, Vector3.one * 0.6f),
                            Transform = Matrix4x4.TRS(WorldTransformer.GameToWorld(GetPosition()), GetRotation(), GetScale()),
                            IsSelectable = true,
                            SelectionData = "TrackNode"
                        }
                    };
                }
                else
                {
                    UpdateSelectionAreas();
                }
                return _selectionAreas;
            }
        }

        private void UpdateSelectionAreas()
        {
            if (_selectionAreas != null && _selectionAreas.Length > 0)
            {
                _selectionAreas[0].Transform = Matrix4x4.TRS(WorldTransformer.GameToWorld(GetPosition()), GetRotation(), GetScale());
            }
        }

        public override bool CanHandleEntity(object entity)
        {
            return CanHandleEntityStatic(entity);
        }

        /// <summary>
        /// Static method to determine if this handler type can handle a TrackNode entity.
        /// Called by the EditorHandlerRegistry via reflection without instantiation.
        /// </summary>
        public static bool CanHandleEntityStatic(object entity)
        {
            return entity is TrackNode;
        }

        /// <summary>
        /// Initializes materials and meshes for rendering.
        /// </summary>
        private void InitializeRenderingResources()
        {
            if (_renderMaterial != null && _previewMesh != null)
            {
                return; // Already initialized
            }
        
            try
            {
                // Find or create wireframe material
                var shader = Shader.Find("Universal Render Pipeline/Lit");

                if (shader != null)
                {
                    _renderMaterial = new Material(shader)
                    {
                        name = "TrackNodeOverlay"
                    };

                    if (_renderMaterial.HasProperty("_BaseColor"))
                    {
                        _renderMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, 1f));
                    }
                    else if (_renderMaterial.HasProperty("_Color"))
                    {
                        _renderMaterial.SetColor("_Color", new Color(1f, 1f, 1f, 1f));
                    }

                    _renderMaterial.renderQueue = 4000;
                }

                // Create a simple sphere mesh for the preview
                _previewMesh = MeshGenerator.CreateChevronMesh(0.55f);
            }
            catch (System.Exception ex)
            {
                FuseLog.Error($"TrackNodeEditorHandler: Error initializing rendering resources: {ex.Message}");
            }
        }

        public override void Render(Camera camera)
        {
            if (_renderMaterial == null || _previewMesh == null)
            {
                return;
            }

            try
            {
                Color color = Color.yellow;
                if (FuseEditor.Instance.EntitySelection.SelectedIds.Contains(ID))
                {
                    color = Color.green;
                }           

                var matrix = Matrix4x4.TRS(WorldTransformer.GameToWorld(GetPosition()), GetRotation(), GetScale());

                var mpb = new MaterialPropertyBlock();
                if (_renderMaterial.HasProperty("_BaseColor"))
                {
                    mpb.SetColor("_BaseColor", color);
                }
                else if (_renderMaterial.HasProperty("_Color"))
                {
                    mpb.SetColor("_Color", color);
                }

                var rp = new RenderParams(_renderMaterial)
                {
                    matProps = mpb,
                    camera = camera
                };

                Graphics.RenderMesh(rp, _previewMesh, 0, matrix);
            }
            catch (System.Exception ex)
            {
                FuseLog.Error($"TrackNodeEditorHandler: Error rendering preview: {ex.Message}");
            }
        }

        public override Vector3 GetPosition() => FuseNodeData.Position;

        public override Quaternion GetRotation() => Quaternion.Euler(FuseNodeData.Rotation);

        public override Vector3 GetScale() => Vector3.one;

        public override void SetPosition(Vector3 position, bool createUndoRedo = true)
        {
            FuseNode oldNode = FUSE.Runtime.API.TrackAPI.CloneNodeDefinition(FuseNodeData);
            FuseNodeData.Position = position;
            if (createUndoRedo)
            {
                FuseEditorChangeHandler.Instance.QueueChange(this, oldNode);
            }
            else
            {
                FuseEditorChangeHandler.Instance.QueueChange(this, null);
            }
        }

        public override void SetRotation(Quaternion rotation, bool createUndoRedo = true)
        {
            FuseNode oldNode = FUSE.Runtime.API.TrackAPI.CloneNodeDefinition(FuseNodeData);
            FuseNodeData.Rotation = rotation.eulerAngles;
            if (createUndoRedo)
            {
                FuseEditorChangeHandler.Instance.QueueChange(this, oldNode);
            }
            else
            {
                FuseEditorChangeHandler.Instance.QueueChange(this, null);
            }
        }

        public override void SetScale(Vector3 scale, bool createUndoRedo = true)
        {

        }

        public override object GetObject() => Entity;

        public override object GetData() => FuseData;

        public override void SetData(object data, bool createUndoRedo = true)
        {
            if (data == null || data.GetType() != typeof(FuseNode))
            {
                return;
            }

            FuseNode oldNode = FUSE.Runtime.API.TrackAPI.CloneNodeDefinition(FuseNodeData);

            FuseData = data;

            if (createUndoRedo)
            {
                FuseEditorChangeHandler.Instance.QueueChange(this, oldNode);
            }
        }

        public override void ApplyData()
        {
            FUSE.Runtime.API.TrackAPI.UpdateNode(ID, FUSE.Runtime.API.TrackAPI.CloneNodeDefinition(FuseNodeData));
        }

        public override void SaveData(FuseModDefinition mod)
        {
            if (Entity is TrackNode trackNode && FuseData is FuseNode fuseNode)
            {
                mod.Tracks.Nodes[ID] = FUSE.Runtime.API.TrackAPI.CloneNodeDefinition(fuseNode);
            }
        }

        public override Dictionary<string, (Type type, object value)> GetProperties()
        {
            var props = new Dictionary<string, (Type type, object value)>();

            props[nameof(FuseNode.Position)] = (typeof(Vector3), FuseNodeData.Position);
            props[nameof(FuseNode.Rotation)] = (typeof(Vector3), FuseNodeData.Rotation);
            props[nameof(FuseNode.FlipSwitchStand)] = (typeof(bool), FuseNodeData.FlipSwitchStand);

            return props;
        }

        public override void UpdateProperties(Dictionary<string, object> properties, bool createUndoRedo = true)
        {
            FuseNode oldNode = FUSE.Runtime.API.TrackAPI.CloneNodeDefinition(FuseNodeData);

            foreach (var kvp in properties)
            {
                UpdateProperty(kvp.Key, kvp.Value, false);
            }

            if (createUndoRedo)
            {
                FuseEditorChangeHandler.Instance.QueueChange(this, oldNode);
            }
            else
            {
                FuseEditorChangeHandler.Instance.QueueChange(this, null);
            }
        }

        public override void UpdateProperty(string propertyName, object value, bool createUndoRedo = true)
        {
            FuseNode oldNode = FUSE.Runtime.API.TrackAPI.CloneNodeDefinition(FuseNodeData);

            if (propertyName == nameof(FuseNode.Position))
            {
                FuseNodeData.Position = (Vector3)value;

            }
            else if (propertyName == nameof(FuseNode.Rotation))
            {
                FuseNodeData.Rotation = (Vector3)value;
            }
            else if (propertyName == nameof(FuseNode.FlipSwitchStand))
            {
                FuseNodeData.FlipSwitchStand = (bool)value;
            }

            if (createUndoRedo)
            {
                FuseEditorChangeHandler.Instance.QueueChange(this, oldNode);
            }
            else
            {
                FuseEditorChangeHandler.Instance.QueueChange(this, null);
            }
        }

        public override string GetTooltip()
        {
            if (Entity is TrackNode trackNode)
            {
                return $"TrackNode: {trackNode.id}";
            }
            return "TrackNode Preview";
        }

        public override bool Equals(object handler)
        {
            if (handler is TrackNodeEditorHandler other)
            {
                return ID == other.ID && Entity == other.Entity;
            }
            return false;
        }
    }
}
