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

        // Selected markers shift to a warm yellow so the active
        // selection is visible at a glance even when the Select tool
        // is active (where no gizmo would otherwise indicate selection).
        private static readonly Color SelectedTint = new Color(1f, 0.86f, 0.32f, 0.95f);

        private const int ModeIdle = 0;
        private const int ModeMove = 1;
        private const int ModeRotate = 2;

        private void Start()
        {
            _textObj = new GameObject("FuseNodeMarker.Label");
            _textObj.transform.SetParent(transform, worldPositionStays: false);
            _textObj.transform.localPosition = Vector3.zero;

            var label = _textObj.AddComponent<TextMeshPro>();
            label.text = Node != null ? Node.id : "<null>";
            label.fontSize = 8;
            label.horizontalAlignment = HorizontalAlignmentOptions.Center;
            label.verticalAlignment = VerticalAlignmentOptions.Top;
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
            var renderer = GetComponent<MeshRenderer>();
            if (renderer == null || renderer.material == null)
            {
                return;
            }

            if (!_baselineColor.HasValue)
            {
                _baselineColor = renderer.material.color;
            }

            renderer.material.color = selected ? SelectedTint : _baselineColor.Value;
        }

        private void LateUpdate()
        {
            if (_textObj == null || Camera.main == null)
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

            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }

            DestroyGizmo();

            _gizmoTarget = new GameObject("FuseNodeMarker.GizmoTarget");
            _gizmoTarget.transform.position = Node.transform.position;
            _gizmoTarget.transform.rotation = Node.transform.rotation;

            var engine = MonoSingleton<RTGizmosEngine>.Get;
            _activeGizmo = mode == ModeMove
                ? engine.CreateObjectMoveGizmo()
                : engine.CreateObjectRotationGizmo();

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

            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = true;
            }

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
