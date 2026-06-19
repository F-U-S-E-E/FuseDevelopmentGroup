using System;
using FUSE.Infrastructure;
using RLD;
using TMPro;
using Track;
using UnityEngine;

namespace FUSE.Editor.Track
{
    /// <summary>
    /// Sphere visualizer attached to a TrackNode while the FUSE editor is
    /// active. Handles selection (click), draws a floating label, and owns
    /// the move/rotate gizmos that mutate the node transform.
    /// </summary>
    internal sealed class FuseNodeMarker : MonoBehaviour
    {
        public TrackNode Node;
        public string ModId;

        private GameObject _textObj;
        private GameObject _gizmoTarget;
        private ObjectTransformGizmo _activeGizmo;
        private int _mode;
        private bool _isDirty;
        private Color? _baselineColor;
        private bool _isVisible = true;
        private MeshRenderer _meshRenderer;
        private TextMeshPro _label;

        // Selected markers shift to a warm yellow so the active
        // selection is visible at a glance even when the Select tool
        // is active (where no gizmo would otherwise indicate selection).
        private static readonly Color SelectedTint = new Color(1f, 0.86f, 0.32f, 0.95f);

        private const int ModeIdle = 0;
        private const int ModeMove = 1;
        private const int ModeRotate = 2;

        private void Start()
        {
            _meshRenderer = GetComponent<MeshRenderer>();

            _textObj = new GameObject("FuseNodeMarker.Label");
            _textObj.transform.SetParent(transform, worldPositionStays: false);
            _textObj.transform.localPosition = Vector3.zero;

            _label = _textObj.AddComponent<TextMeshPro>();
            _label.text = Node != null ? Node.id : "<null>";
            _label.fontSize = 8;
            _label.horizontalAlignment = HorizontalAlignmentOptions.Center;
            _label.verticalAlignment = VerticalAlignmentOptions.Top;

            // Apply initial visibility state
            ApplyVisibility();
        }

        private void OnMouseDown()
        {
            if (_mode != ModeIdle)
            {
                return;
            }

            FuseNodeEditorController.SelectMarker(this);

            // Let the active tool engage on selection — Move attaches a
            // translate gizmo, Rotate attaches a rotate gizmo, Select
            // and Place no-op. Routing through the registry keeps marker
            // code free of tool-specific knowledge.
            Screen.UI.FuseEditorToolRegistry.Active?.OnNodeSelected(this);
        }

        /// <summary>
        /// Tints the marker yellow when selected and restores its
        /// original color when deselected. Called by
        /// <see cref="FuseNodeEditorController.SelectMarker"/> +
        /// <see cref="FuseNodeEditorController.DeselectCurrent"/> so
        /// the marker doesn't have to know about controller-level
        /// selection state.
        /// </summary>
        public void SetSelected(bool selected)
        {
            if (_meshRenderer == null)
            {
                _meshRenderer = GetComponent<MeshRenderer>();
            }

            if (_meshRenderer == null || _meshRenderer.material == null)
            {
                return;
            }

            if (!_baselineColor.HasValue)
            {
                _baselineColor = _meshRenderer.material.color;
            }

            _meshRenderer.material.color = selected ? SelectedTint : _baselineColor.Value;
        }

        /// <summary>
        /// Sets whether this marker should be visible. Used for camera-based
        /// culling to avoid rendering/updating distant markers.
        /// </summary>
        public void SetVisibility(bool visible)
        {
            if (_isVisible == visible)
            {
                return;
            }

            _isVisible = visible;
            ApplyVisibility();
        }

        /// <summary>
        /// Applies the current visibility state to the marker's renderer and label.
        /// </summary>
        private void ApplyVisibility()
        {
            // Don't hide if we're in an active gizmo mode (moving/rotating)
            if (_mode != ModeIdle)
            {
                return;
            }

            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = _isVisible;
            }

            if (_textObj != null)
            {
                _textObj.SetActive(_isVisible);
            }
        }

        private void LateUpdate()
        {
            // Skip updates if marker is not visible (culled)
            if (!_isVisible || _textObj == null || Camera.main == null)
            {
                return;
            }

            _textObj.transform.LookAt(transform.position + Camera.main.transform.forward);
        }

        public void BeginMove()
        {
            BeginGizmo(ModeMove);
        }

        public void BeginRotate()
        {
            BeginGizmo(ModeRotate);
        }

        private void BeginGizmo(int mode)
        {
            if (Node == null)
            {
                return;
            }

            _mode = mode;

            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = false;
            }

            DestroyGizmo();

            _gizmoTarget = new GameObject("FuseNodeMarker.GizmoTarget");
            _gizmoTarget.transform.position = Node.transform.position;
            _gizmoTarget.transform.rotation = Node.transform.rotation;

            var engine = MonoSingleton<RTGizmosEngine>.Get;
            if (engine == null)
            {
                FuseLog.Error("FUSE node editor: RTGizmosEngine singleton is not available. Gizmo cannot be created.");
                // Restore renderer visibility since we're not entering gizmo mode
                if (_meshRenderer != null)
                {
                    _meshRenderer.enabled = true;
                }
                // Clean up the gizmo target we just created
                if (_gizmoTarget != null)
                {
                    Destroy(_gizmoTarget);
                    _gizmoTarget = null;
                }
                _mode = ModeIdle;
                return;
            }

            // Ensure the engine has a camera set before creating gizmos
            if (Camera.main != null)
            {
                try
                {
                    var appField = typeof(RTGizmosEngine).GetField("_app", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (appField != null)
                    {
                        var app = appField.GetValue(engine);
                        if (app != null)
                        {
                            var focusCameraProperty = app.GetType().GetProperty("FocusCamera");
                            if (focusCameraProperty != null)
                            {
                                focusCameraProperty.SetValue(app, Camera.main);
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    FuseLog.Exception("FUSE node editor: Failed to set gizmo focus camera.", ex);
                }
            }
            else
            {
                FuseLog.Error("FUSE node editor: Camera.main is null. Cannot create gizmo.");
                // Restore renderer visibility since we're not entering gizmo mode
                if (_meshRenderer != null)
                {
                    _meshRenderer.enabled = true;
                }
                // Clean up the gizmo target
                if (_gizmoTarget != null)
                {
                    Destroy(_gizmoTarget);
                    _gizmoTarget = null;
                }
                _mode = ModeIdle;
                return;
            }

            _activeGizmo = mode == ModeMove
                ? engine.CreateObjectMoveGizmo()
                : engine.CreateObjectRotationGizmo();

            if (_activeGizmo == null)
            {
                FuseLog.Error($"FUSE node editor: Failed to create {(mode == ModeMove ? "move" : "rotate")} gizmo from RTGizmosEngine.");
                // Restore renderer visibility since we're not entering gizmo mode
                if (_meshRenderer != null)
                {
                    _meshRenderer.enabled = true;
                }
                // Clean up the gizmo target
                if (_gizmoTarget != null)
                {
                    Destroy(_gizmoTarget);
                    _gizmoTarget = null;
                }
                _mode = ModeIdle;
                return;
            }

            _activeGizmo.SetTransformSpace(GizmoSpace.Global);
            _activeGizmo.SetTargetObject(_gizmoTarget);
            _activeGizmo.SetTransformPivot(GizmoObjectTransformPivot.ObjectCenterPivot);
            _activeGizmo.RefreshPositionAndRotation();
            _activeGizmo.Gizmo.Transform.Rotate3D(_gizmoTarget.transform.rotation);
            _activeGizmo.Gizmo.Transform.Rotation3D = _gizmoTarget.transform.rotation;

            // Gizmo updates _gizmoTarget.transform directly each frame while
            // dragging; mirror that onto Node.transform per-tick and commit on
            // drag end. Kept off the TrackNode's own transform during drag to
            // avoid mid-drag rebuild churn against the live Graph.
            _activeGizmo.Gizmo.PostDragUpdate += OnGizmoDragUpdate;
            _activeGizmo.Gizmo.PostDragEnd += OnGizmoDragEnd;
        }

        private void OnGizmoDragUpdate(Gizmo gizmo, int handleId)
        {
            if (Node == null || _gizmoTarget == null)
            {
                return;
            }

            if (_mode == ModeMove)
            {
                Node.transform.position = _gizmoTarget.transform.position;
            }
            else if (_mode == ModeRotate)
            {
                Node.transform.rotation = _gizmoTarget.transform.rotation;
            }

            _isDirty = true;
        }

        private void OnGizmoDragEnd(Gizmo gizmo, int handleId)
        {
            if (_activeGizmo == null || _gizmoTarget == null)
            {
                return;
            }

            _activeGizmo.Gizmo.Transform.Rotation3D = _gizmoTarget.transform.rotation;
        }

        public void Deselect()
        {
            _mode = ModeIdle;
            DestroyGizmo();

            // Re-apply visibility state (which will restore renderer if visible)
            ApplyVisibility();

            if (_isDirty)
            {
                PersistAndRebuild();
                _isDirty = false;
            }
        }

        public void PersistAndRebuild()
        {
            if (Node == null || string.IsNullOrEmpty(ModId))
            {
                return;
            }

            try
            {
                FuseNodeEditorController.PersistNode(ModId, Node);
                global::Track.Graph.Shared.OnNodeDidChange(Node);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor failed to persist node '{Node.id}'", ex);
            }
        }

        private void DestroyGizmo()
        {
            if (_activeGizmo != null)
            {
                try
                {
                    MonoSingleton<RTGizmosEngine>.Get.RemoveGizmo(_activeGizmo.Gizmo);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE editor failed to remove RTGizmosEngine gizmo", ex);
                }
                _activeGizmo = null;
            }

            if (_gizmoTarget != null)
            {
                Destroy(_gizmoTarget);
                _gizmoTarget = null;
            }
        }

        private void OnDestroy()
        {
            DestroyGizmo();

            if (_textObj != null)
            {
                Destroy(_textObj);
                _textObj = null;
            }
        }
    }
}
