using FUSE.Editor.Gizmos;
using UnityEngine;

namespace FUSE.Editor.Track
{
    /// <summary>
    /// Example of integrating the new gizmo handler system with FuseNodeMarker.
    /// This shows how to replace the existing gizmo code with the cleaner handler approach.
    /// </summary>
    public class FuseNodeMarkerGizmoIntegrationExample : MonoBehaviour
    {
        // Shared gizmo manager for all markers
        private static FuseGizmoManager _gizmoManager = new FuseGizmoManager();

        private GameObject _gizmoTarget;
        private global::Track.TrackNode _node;
        private string _modId;

        /// <summary>
        /// Example: Start a move operation using the new gizmo system.
        /// </summary>
        public void BeginMoveWithNewSystem()
        {
            // Create a temporary target GameObject for the gizmo
            _gizmoTarget = new GameObject("NodeGizmoTarget");
            _gizmoTarget.transform.position = _node.transform.position;
            _gizmoTarget.transform.rotation = _node.transform.rotation;

            // Start the move operation with a completion callback
            var handler = _gizmoManager.BeginMove(_gizmoTarget, OnMoveCompleted);

            if (handler != null)
            {
                // Optional: configure the gizmo
                handler.SetTransformSpace(RLD.GizmoSpace.Global);
            }
            else
            {
                // Cleanup if initialization failed
                if (_gizmoTarget != null)
                {
                    Destroy(_gizmoTarget);
                    _gizmoTarget = null;
                }
            }
        }

        /// <summary>
        /// Called when the move operation completes.
        /// </summary>
        private void OnMoveCompleted(Vector3 newPosition)
        {
            // Update the actual node position
            if (_node != null)
            {
                _node.transform.position = newPosition;

                // Persist to backend
                try
                {
                    FuseNodeEditorController.PersistNode(_modId, _node);
                    global::Track.Graph.Shared.OnNodeDidChange(_node);
                }
                catch (System.Exception ex)
                {
                    Infrastructure.FuseLog.Exception($"FUSE editor failed to persist node '{_node.id}'", ex);
                }
            }

            // Clean up the temporary target
            if (_gizmoTarget != null)
            {
                Destroy(_gizmoTarget);
                _gizmoTarget = null;
            }
        }

        /// <summary>
        /// Example: Start a rotate operation using the new gizmo system.
        /// </summary>
        public void BeginRotateWithNewSystem()
        {
            _gizmoTarget = new GameObject("NodeGizmoTarget");
            _gizmoTarget.transform.position = _node.transform.position;
            _gizmoTarget.transform.rotation = _node.transform.rotation;

            var handler = _gizmoManager.BeginRotate(_gizmoTarget, OnRotateCompleted);

            if (handler != null)
            {
                handler.SetTransformSpace(RLD.GizmoSpace.Global);
            }
            else
            {
                if (_gizmoTarget != null)
                {
                    Destroy(_gizmoTarget);
                    _gizmoTarget = null;
                }
            }
        }

        /// <summary>
        /// Called when the rotate operation completes.
        /// </summary>
        private void OnRotateCompleted(Quaternion newRotation)
        {
            if (_node != null)
            {
                _node.transform.rotation = newRotation;

                try
                {
                    FuseNodeEditorController.PersistNode(_modId, _node);
                    global::Track.Graph.Shared.OnNodeDidChange(_node);
                }
                catch (System.Exception ex)
                {
                    Infrastructure.FuseLog.Exception($"FUSE editor failed to persist node '{_node.id}'", ex);
                }
            }

            if (_gizmoTarget != null)
            {
                Destroy(_gizmoTarget);
                _gizmoTarget = null;
            }
        }

        /// <summary>
        /// Example: Cancel the current gizmo operation.
        /// </summary>
        public void CancelCurrentOperation()
        {
            _gizmoManager.CancelCurrentGizmo();

            if (_gizmoTarget != null)
            {
                Destroy(_gizmoTarget);
                _gizmoTarget = null;
            }
        }

        /// <summary>
        /// Clean up when the marker is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            if (_gizmoTarget != null)
            {
                Destroy(_gizmoTarget);
                _gizmoTarget = null;
            }
        }

        // ====================================================================
        // MIGRATION NOTES
        // ====================================================================
        //
        // To migrate FuseNodeMarker to use the new gizmo system:
        //
        // 1. Replace BeginGizmo(int mode) with separate BeginMoveWithNewSystem()
        //    and BeginRotateWithNewSystem() methods.
        //
        // 2. Remove the OnGizmoDragUpdate() method - the handler tracks this
        //    internally and only calls you back when complete.
        //
        // 3. Replace OnGizmoDragEnd() with OnMoveCompleted() and
        //    OnRotateCompleted() callbacks.
        //
        // 4. Remove DestroyGizmo() - the manager handles cleanup automatically.
        //
        // 5. Simplify Deselect() to just call CancelCurrentOperation() if
        //    needed, or let the completion callbacks handle persistence.
        //
        // Benefits:
        // - Cleaner separation of concerns
        // - No manual event registration/unregistration
        // - Automatic cleanup
        // - Type-safe callbacks
        // - Easier to test and maintain
    }
}
