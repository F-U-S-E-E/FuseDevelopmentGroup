using FUSE.Authoring.Data;
using FUSE.Editor.Overlays;
using FUSE.Editor.Resources;
using FUSE.Infrastructure;
using Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Track;
using UnityEngine;

namespace FUSE.Editor.EditorHandler
{
    public class TrackSpanEditorHandler : EditorHandlerBase
    {
        private Vector3 _previewPosition;
        private Quaternion _previewRotation;
        private Vector3 _previewScale = Vector3.one;
        private static Material _renderMaterial;
        private Mesh _previewMesh;
        private bool _isVisible = true;
        private OverlaySelectionArea[] _selectionAreas;

        float oldLength = 0f;

        MethodInfo UpdatePointsMethod = typeof(TrackSpan).GetMethod("UpdateCachedPointsIfNeeded", BindingFlags.NonPublic | BindingFlags.Instance);

        private TrackSpan TrackSpan => Entity as TrackSpan;
        private FuseSpan FuseSpanData => FuseData as FuseSpan;

        public override bool IsVisible
        {
            get => _isVisible;
            set => _isVisible = value;
        }

        public override OverlaySelectionArea[] SelectionAreas
        {
            get
            {
                if (_selectionAreas == null)
                {
                    // Create a simple box-based selection area
                    _selectionAreas = new OverlaySelectionArea[]
                    {
                        new OverlaySelectionArea()
                        {
                            AreaId = "Segment",
                            PreviewId = ID,
                            Bounds = new Bounds(Vector3.zero, Vector3.one * 0.5f),
                            Transform = Matrix4x4.TRS(WorldTransformer.GameToWorld(GetPosition()), Quaternion.identity, Vector3.one),
                            IsSelectable = true,
                            SelectionData = "TrackSegment"
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

        public TrackSpanEditorHandler(object entity)
        {
            if (!CanHandleEntity(entity)) return;
            Entity = entity;
            ID = TrackSpan.id;
            FuseData = FUSE.Runtime.API.TrackAPI.CloneSpanDefinition(FUSE.Runtime.API.TrackAPI.GetDefinition(TrackSpan));

            InitializeRenderingResources();

            UpdateMeshIfNeeded();
            _previewPosition = Vector3.zero;
            _previewRotation = Quaternion.identity;
            _previewScale = Vector3.one;
        }

        private void UpdateSelectionAreas()
        {
            if (_selectionAreas != null && _selectionAreas.Length > 0)
            {
                _selectionAreas[0].Transform = Matrix4x4.TRS(WorldTransformer.GameToWorld(GetPosition()), Quaternion.identity, Vector3.one);
            }
        }

        public override bool CanHandleEntity(object entity)
        {
            return CanHandleEntityStatic(entity);
        }

        /// <summary>
        /// Static method to determine if this handler type can handle a TrackSpan entity.
        /// Called by the EditorHandlerRegistry via reflection without instantiation.
        /// </summary>
        public static bool CanHandleEntityStatic(object entity)
        {
            return entity is TrackSpan;
        }

        private void InitializeRenderingResources()
        {
            if (_renderMaterial != null)
            {
                return;
            }
            try
            {
                // Find or create wireframe material
                var shader = Shader.Find("Universal Render Pipeline/Lit");

                if (shader != null)
                {
                    _renderMaterial = new Material(shader)
                    {
                        name = "TrackSegmentOverlay"
                    };

                    if (_renderMaterial.HasProperty("_BaseColor"))
                    {
                        _renderMaterial.SetColor("_BaseColor", new Color(0.5f, 1f, 0.5f, 1f));
                    }
                    else if (_renderMaterial.HasProperty("_Color"))
                    {
                        _renderMaterial.SetColor("_Color", new Color(0.5f, 1f, 0.5f, 1f));
                    }

                    _renderMaterial.renderQueue = 4000;
                }
            }
            catch (System.Exception ex)
            {
                FuseLog.Error($"TrackSegmentEditorHandler: Error initializing rendering resources: {ex.Message}");
            }
        }

        public void UpdateMeshIfNeeded()
        {
            if (oldLength == TrackSpan.Length && _previewMesh != null)
            {
                return;
            }

            //UpdateCachedPointsIfNeeded(warnInvalid: false);
            UpdatePointsMethod.Invoke(TrackSpan, new object[] { false });

            Vector3 centerpoint = TrackSpan.GetCenterPoint();

            Vector3[] points = TrackSpan.GetPoints().ToArray();

            var cubicMesh = MeshGenerator.CreateCubicMeshAlongPath(points, centerpoint);
            var Begining = MeshGenerator.CreateSolidTriangleMesh(points[0] - centerpoint, Quaternion.LookRotation(points[1] - points[0]), 0.5f);
            var Middle = MeshGenerator.CreateSolidTriangleMesh(Vector3.zero, Quaternion.LookRotation(points[points.Length/2+1] - points[points.Length/2]), 0.75f);
            var End = MeshGenerator.CreateSolidTriangleMesh(points[points.Length-1] - centerpoint,Quaternion.LookRotation(points[points.Length-1] - points[points.Length-2]), 0.5f);

            _previewMesh = MeshGenerator.CombineMeshesWithOffsetAndRotation(cubicMesh, Begining, Vector3.zero, Quaternion.identity);
            _previewMesh = MeshGenerator.CombineMeshesWithOffsetAndRotation(_previewMesh, Middle, Vector3.zero, Quaternion.identity);
            _previewMesh = MeshGenerator.CombineMeshesWithOffsetAndRotation(_previewMesh, End, Vector3.zero, Quaternion.identity);

            oldLength = TrackSpan.Length;
        }

        

        public override void Render(Camera camera)
        {
            UpdateMeshIfNeeded();
            if (_renderMaterial == null || _previewMesh == null)
            {
                return;
            }

            try
            {
                var matrix = Matrix4x4.TRS(WorldTransformer.GameToWorld(GetPosition()), Quaternion.identity, Vector3.one);

                Color color = Color.red;

                if (FuseEditor.Instance.EntitySelection.SelectedIds.Contains(ID))
                {
                    color = Color.green;
                }


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
                FuseLog.Error($"TrackSegmentEditorHandler: Error rendering preview: {ex.Message}");
            }
        }

        public override Vector3 GetPosition() => TrackSpan.GetCenterPoint();

        public override Quaternion GetRotation() => Quaternion.identity;

        public override Vector3 GetScale() => Vector3.one;

        public override void SetPosition(Vector3 position, bool createUndoRedo = true) => _previewPosition = position;

        public override void SetRotation(Quaternion rotation, bool createUndoRedo = true) => _previewRotation = rotation;

        public override void SetScale(Vector3 scale, bool createUndoRedo = true) => _previewScale = scale;

        public override object GetObject() => Entity;

        public override object GetData() => FuseData;

        public override void SetData(object data, bool createUndoRedo = true)
        {
            if (data == null || data.GetType() != typeof(FuseSpan))
            {
                return;
            }

            FuseSpan oldSegment = FUSE.Runtime.API.TrackAPI.CloneSpanDefinition(FuseSpanData);

            FuseData = data;

            if (createUndoRedo)
            {
                FuseEditorChangeHandler.Instance.QueueChange(this, oldSegment);
            }
        }

        public override void ApplyData()
        {
            FUSE.Runtime.API.TrackAPI.UpdateSpan(ID, FUSE.Runtime.API.TrackAPI.CloneSpanDefinition(FuseSpanData));
        }

        public override void SaveData(FuseModDefinition mod)
        {
            mod.Tracks.Spans[ID] = FUSE.Runtime.API.TrackAPI.CloneSpanDefinition(FuseSpanData);
        }

        public override Dictionary<string, (Type type, object value)> GetProperties()
        {
            var props = new Dictionary<string, (Type type, object value)>();

            props[nameof(FuseSpan.Upper)] = (typeof(Location), FUSE.Runtime.API.TrackAPI.MakeLocation(Graph.Shared, FuseSpanData.Upper));
            props[nameof(FuseSpan.Lower)] = (typeof(Location), FUSE.Runtime.API.TrackAPI.MakeLocation(Graph.Shared, FuseSpanData.Lower));

            return props;
        }

        public override void UpdateProperties(Dictionary<string, object> properties, bool createUndoRedo = true)
        {
            FuseSpan oldSpan = FUSE.Runtime.API.TrackAPI.CloneSpanDefinition(FuseSpanData);

            foreach (var kvp in properties)
            {
                UpdateProperty(kvp.Key, kvp.Value, false);
            }

            if (createUndoRedo)
            {
                FuseEditorChangeHandler.Instance.QueueChange(this, oldSpan);
            }
        }

        public override void UpdateProperty(string propertyName, object value, bool createUndoRedo = true)
        {
            FuseSpan oldSpan = null;

            if (createUndoRedo)
            {
                oldSpan = FUSE.Runtime.API.TrackAPI.CloneSpanDefinition(FuseSpanData);
            }



            if (createUndoRedo)
            {
                FuseEditorChangeHandler.Instance.QueueChange(this, oldSpan);
            }
        }

        public override string GetTooltip()
        {
            return $"TrackSpan: {ID}";
        }

        public override bool Equals(object handler)
        {
            if (handler is TrackSpanEditorHandler other)
            {
                return ID == other.ID && Entity == other.Entity;
            }
            return false;
        }
    }
}
