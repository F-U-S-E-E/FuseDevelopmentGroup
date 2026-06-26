using Core;
using FUSE.Authoring.Data;
using FUSE.Editor.Overlays;
using FUSE.Editor.Resources;
using FUSE.Infrastructure;
using Helpers;
using System;
using System.Collections.Generic;
using Track;
using UnityEngine;

namespace FUSE.Editor.EditorHandler
{
    /// <summary>
    /// Concrete EditorHandler for TrackSegment entities.
    /// Manages rendering, transform, and data operations for track segments in the editor.
    /// </summary>
    public class TrackSegmentEditorHandler : EditorHandlerBase
    {
        private Vector3 _previewPosition;
        private Quaternion _previewRotation;
        private Vector3 _previewScale = Vector3.one;
        private static Material _renderMaterial;
        private Mesh _previewMesh;
        private bool _isVisible = true;
        private OverlaySelectionArea[] _selectionAreas;

        private TrackSegment TrackSegment => Entity as TrackSegment;
        private FuseSegment FuseSegmentData => FuseData as FuseSegment;

        private Vector3 nodeAPos;
        private Vector3 nodeARot;

        private Vector3 nodeBPos;
        private Vector3 nodeBRot;

        public BezierCurve? Curve;

        public Vector3 MidPoint => Curve.HasValue ? Curve.Value.GetPoint(0.5f) : Vector3.zero;

        /// <summary>
        /// Creates a new TrackSegmentEditorHandler.
        /// </summary>
        /// <param name="id">Unique identifier for this handler.</param>
        /// <param name="entity">The TrackSegment entity being edited.</param>
        /// <param name="previewData">The preview data (e.g., FuseSegment).</param>
        public TrackSegmentEditorHandler(object entity)
        {
            if (!CanHandleEntity(entity)) return;
            Entity = entity;
            ID = TrackSegment.id;
            FuseData = FUSE.Runtime.API.TrackAPI.CloneSegmentDefinition(FUSE.Runtime.API.TrackAPI.GetDefinition(TrackSegment));

            InitializeRenderingResources();

            UpdateMeshIfNeeded();
            _previewPosition = MidPoint;
            _previewRotation = Quaternion.identity;
            _previewScale = Vector3.one;            
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
                    // Create a simple box-based selection area
                    _selectionAreas = new OverlaySelectionArea[]
                    {
                        new OverlaySelectionArea()
                        {
                            AreaId = "Segment",
                            PreviewId = ID,
                            Bounds = new Bounds(Vector3.zero, Vector3.one * 0.7f),
                            Transform = Matrix4x4.TRS(WorldTransformer.GameToWorld(MidPoint), Quaternion.identity, Vector3.one),
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

        private void UpdateSelectionAreas()
        {
            if (_selectionAreas != null && _selectionAreas.Length > 0)
            {
                _selectionAreas[0].Transform = Matrix4x4.TRS(WorldTransformer.GameToWorld(MidPoint), Quaternion.identity, Vector3.one);
            }
        }

        public override bool CanHandleEntity(object entity)
        {
            return CanHandleEntityStatic(entity);
        }

        /// <summary>
        /// Static method to determine if this handler type can handle a TrackSegment entity.
        /// Called by the EditorHandlerRegistry via reflection without instantiation.
        /// </summary>
        public static bool CanHandleEntityStatic(object entity)
        {
            return entity is TrackSegment;
        }

        /// <summary>
        /// Initializes materials and meshes for rendering.
        /// </summary>
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
            if (TrackSegment == null || FuseSegmentData == null)
            {
                return;
            }

            // Get preview data if available (for pending edits)
            (Vector3 pos, Vector3 rot) nodeATransform;
            (Vector3 pos, Vector3 rot) nodeBTransform;

            if (FuseEditorChangeHandler.Instance.TryGetQueuedChange(FuseSegmentData.StartNodeId, typeof(TrackNode), out var nodeAPreview))
            {
                nodeATransform = (nodeAPreview.GetPosition(), nodeAPreview.GetRotation().eulerAngles);
            }
            else
            {
                TrackNode a = FUSE.Runtime.API.TrackAPI.GetNode(FuseSegmentData.StartNodeId);
                nodeATransform = (a.transform.localPosition, a.transform.rotation.eulerAngles);
            }

            if (FuseEditorChangeHandler.Instance.TryGetQueuedChange(FuseSegmentData.EndNodeId, typeof(TrackNode), out var nodeBPreview))
            {
                nodeBTransform = (nodeBPreview.GetPosition(), nodeBPreview.GetRotation().eulerAngles);
            }
            else
            {
                TrackNode b = FUSE.Runtime.API.TrackAPI.GetNode(FuseSegmentData.EndNodeId);
                nodeBTransform = (b.transform.localPosition, b.transform.rotation.eulerAngles);
            }


            // Regenerate mesh if nodes have changed
            if (HaveNodesChanged(nodeATransform, nodeBTransform))
            {
                // Get the Bezier curve from the segment
                Curve = CreateBezier();
                int curveResolution = Mathf.RoundToInt(Curve.Value.CalculateLength() * 8);
                curveResolution = Mathf.Max(4, curveResolution); // Ensure minimum resolution
                Mesh rail = MeshGenerator.CreateBezierRailMesh(Curve.Value, curveResolution, 1.435f, 0.07f, MidPoint, 0.0001f, Gauge.Standard.RailHeight);

                Mesh cheveron = MeshGenerator.CreateChevronMesh(0.75f);

                _previewMesh = MeshGenerator.CombineMeshesWithOffsetAndRotation(rail, cheveron, Vector3.zero, Curve.Value.GetRotation(0.5f));
            }
        }

        private BezierCurve CreateBezier()
        {
            Quaternion aRot = Quaternion.Euler(nodeARot);
            Quaternion bRot = Quaternion.Euler(nodeBRot);

            float d = (nodeAPos - nodeBPos).magnitude * BezierTangentFactorForTangents(aRot * Vector3.forward, bRot * Vector3.forward);
            Vector3 vector = TangentPointAlongSegment(nodeAPos, aRot, nodeBPos, bRot, d);
            Vector3 vector2 = TangentPointAlongSegment(nodeBPos, bRot, nodeAPos, aRot, d);
            return new BezierCurve(new Vector3[4] { nodeAPos, vector, vector2, nodeBPos }, aRot * Vector3.up, bRot * Vector3.up);
        }

        private static float BezierTangentFactorForTangents(Vector3 a, Vector3 b)
        {
            float num = Vector3.Angle(a, b);
            if (num > 90f)
            {
                num = 180f - num;
            }
            return Mathf.Lerp(0.35f, 0.41f, Mathf.InverseLerp(45f, 90f, num));
        }

        private Vector3 TangentPointAlongSegment(Vector3 thisPos, Quaternion thisRot, Vector3 otherPos, Quaternion otherRot, float d)
        {
            Vector3 vector = Transform(thisPos, thisRot, Vector3.forward);
            Vector3 vector2 = Transform(thisPos, thisRot, Vector3.back);
            float magnitude = (vector - otherPos).magnitude;
            float magnitude2 = (vector2 - otherPos).magnitude;
            float num = ((magnitude < magnitude2) ? d : (0f - d));
            return Transform(thisPos, thisRot, Vector3.forward * num);
        }

        private Vector3 Transform(Vector3 pos, Quaternion rot, Vector3 v)
        {
            return rot * v + pos;
        }

        private bool HaveNodesChanged((Vector3 pos, Vector3 rot) nodeA, (Vector3 pos, Vector3 rot) nodeB)
        {
            if (nodeA.pos != nodeAPos || nodeA.rot != nodeARot || nodeB.pos != nodeBPos || nodeB.rot != nodeBRot)
            {
                // Update cached transforms
                nodeAPos = nodeA.pos;
                nodeARot = nodeA.rot;
                nodeBPos = nodeB.pos;
                nodeBRot = nodeB.rot;
                return true;
            }

            return false;
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

                Color color = Color.cyan;

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

        public override Vector3 GetPosition() => MidPoint;

        public override Quaternion GetRotation() => Quaternion.identity;

        public override Vector3 GetScale() => Vector3.one;

        public override void SetPosition(Vector3 position, bool createUndoRedo = true) => _previewPosition = position;

        public override void SetRotation(Quaternion rotation, bool createUndoRedo = true) => _previewRotation = rotation;

        public override void SetScale(Vector3 scale, bool createUndoRedo = true) => _previewScale = scale;

        public override object GetObject() => Entity;

        public override object GetData() => FuseData;

        public override void SetData(object data, bool createUndoRedo = true)
        {
            if (data == null || data.GetType() != typeof(FuseSegment))
            {
                return;
            }

            FuseSegment oldSegment = FUSE.Runtime.API.TrackAPI.CloneSegmentDefinition(FuseSegmentData);

            FuseData = data;

            if (createUndoRedo)
            {
                FuseEditorChangeHandler.Instance.QueueChange(this, oldSegment);
            }
        }

        public override void ApplyData()
        {
            FUSE.Runtime.API.TrackAPI.UpdateSegment(ID, FUSE.Runtime.API.TrackAPI.CloneSegmentDefinition(FuseSegmentData));
        }

        public override void SaveData(FuseModDefinition mod)
        {
            mod.Tracks.Segments[ID] = FUSE.Runtime.API.TrackAPI.CloneSegmentDefinition(FuseSegmentData);
        }

        public override Dictionary<string, (Type type, object value)> GetProperties()
        {
            var props = new Dictionary<string, (Type type, object value)>();

            props[nameof(FuseSegment.StartNodeId)] = (typeof(TrackNode), FUSE.Runtime.API.TrackAPI.GetNode(FuseSegmentData.StartNodeId));
            props[nameof(FuseSegment.EndNodeId)] = (typeof(TrackNode), FUSE.Runtime.API.TrackAPI.GetNode(FuseSegmentData.EndNodeId));
            props[nameof(FuseSegment.GroupId)] = (typeof(string), FuseSegmentData.GroupId);
            if (Enum.TryParse<TrackSegment.Style>(FuseSegmentData.Style, out var style))
            {
                props[nameof(FuseSegment.Style)] = (typeof(TrackSegment.Style), style);
            }
            else
            {
                props[nameof(FuseSegment.Style)] = (typeof(TrackSegment.Style), TrackSegment.Style.Standard);
            }
            if (Enum.TryParse<TrackClass>(FuseSegmentData.TrackClass, out var trackClass))
            {
                props[nameof(FuseSegment.TrackClass)] = (typeof(TrackClass), trackClass);
            }
            else
            {
                props[nameof(FuseSegment.TrackClass)] = (typeof(TrackClass), TrackClass.Mainline);
            }
            props[nameof(FuseSegment.SpeedLimit)] = (typeof(int), FuseSegmentData.SpeedLimit);
            props[nameof(FuseSegment.Priority)] = (typeof(int), FuseSegmentData.Priority);

            return props;
        }

        public override void UpdateProperties(Dictionary<string, object> properties, bool createUndoRedo = true)
        {
            FuseSegment oldSegment = FUSE.Runtime.API.TrackAPI.CloneSegmentDefinition(FuseSegmentData);

            foreach (var kvp in properties)
            {
                UpdateProperty(kvp.Key, kvp.Value, false);
            }

            if (createUndoRedo)
            {
                FuseEditorChangeHandler.Instance.QueueChange(this, oldSegment);
            }
        }

        public override void UpdateProperty(string propertyName, object value, bool createUndoRedo = true)
        {
            FuseSegment oldSegment = null;

            if (createUndoRedo)
            {
                oldSegment = FUSE.Runtime.API.TrackAPI.CloneSegmentDefinition(FuseSegmentData);
            }

            if (propertyName == nameof(FuseSegment.StartNodeId) && value is TrackNode startNodeId)
            {
                FuseSegmentData.StartNodeId = startNodeId.id;
            }
            else if (propertyName == nameof(FuseSegment.EndNodeId) && value is TrackNode endNodeId)
            {
                FuseSegmentData.EndNodeId = endNodeId.id;
            }
            else if (propertyName == nameof(FuseSegment.GroupId) && value is string groupId)
            {
                FuseSegmentData.GroupId = groupId;
            }
            else if (propertyName == nameof(FuseSegment.Style) && value is TrackSegment.Style style)
            {
                FuseSegmentData.Style = style.ToString();
            }
            else if (propertyName == nameof(FuseSegment.TrackClass) && value is TrackClass trackClass)
            {
                FuseSegmentData.TrackClass = trackClass.ToString();
            }
            else if (propertyName == nameof(FuseSegment.SpeedLimit) && value is int speedLimit)
            {
                FuseSegmentData.SpeedLimit = speedLimit;
            }
            else if (propertyName == nameof(FuseSegment.Priority) && value is int priority)
            {
                FuseSegmentData.Priority = priority;
            }

            if (createUndoRedo)
            {
                FuseEditorChangeHandler.Instance.QueueChange(this, oldSegment);
            }
        }

        public override string GetTooltip()
        {
            return $"TrackSegment: {ID}\nStart Node: {FuseSegmentData.StartNodeId}\nEnd Node: {FuseSegmentData.EndNodeId}";
        }

        public override bool Equals(object handler)
        {
            if (handler is TrackSegmentEditorHandler other)
            {
                return ID == other.ID && Entity == other.Entity;
            }
            return false;
        }
    }
}
