using FUSE.Infrastructure;
using System;
using UnityEngine;

namespace FUSE.Editor.Gizmos
{
    /// <summary>
    /// Manages active gizmo handlers and ensures only one gizmo is active at a time.
    /// Provides a simplified API for starting move/rotate/scale operations.
    /// </summary>
    public class FuseGizmoManager : IDisposable
    {
        private FuseGizmoHandler _activeHandler;

        /// <summary>
        /// The currently active gizmo handler, or null if no gizmo is active.
        /// </summary>
        public FuseGizmoHandler ActiveHandler => _activeHandler;

        /// <summary>
        /// Whether a gizmo is currently active.
        /// </summary>
        public bool HasActiveGizmo => _activeHandler != null && _activeHandler.IsActive;

        /// <summary>
        /// Starts a move operation on the target GameObject.
        /// </summary>
        /// <param name="target">The GameObject to move.</param>
        /// <param name="onCompleted">Callback invoked with the final position when the move completes.</param>
        /// <returns>The move gizmo handler, or null if initialization failed.</returns>
        public FuseMoveGizmoHandler BeginMove(GameObject target, Action<Vector3> onCompleted = null)
        {
            if (target == null)
            {
                FuseLog.Error("FUSE gizmo manager: Cannot begin move with null target.");
                return null;
            }

            // Deactivate any existing gizmo
            EndCurrentGizmo();

            var handler = new FuseMoveGizmoHandler();
            if (onCompleted != null)
            {
                handler.OnMoveCompleted += onCompleted;
            }

            if (!handler.Initialize(target))
            {
                handler.Dispose();
                return null;
            }

            _activeHandler = handler;
            return handler;
        }

        /// <summary>
        /// Starts a rotate operation on the target GameObject.
        /// </summary>
        /// <param name="target">The GameObject to rotate.</param>
        /// <param name="onCompleted">Callback invoked with the final rotation when the rotate completes.</param>
        /// <returns>The rotate gizmo handler, or null if initialization failed.</returns>
        public FuseRotateGizmoHandler BeginRotate(GameObject target, Action<Quaternion> onCompleted = null)
        {
            if (target == null)
            {
                FuseLog.Error("FUSE gizmo manager: Cannot begin rotate with null target.");
                return null;
            }

            // Deactivate any existing gizmo
            EndCurrentGizmo();

            var handler = new FuseRotateGizmoHandler();
            if (onCompleted != null)
            {
                handler.OnRotateCompleted += onCompleted;
            }

            if (!handler.Initialize(target))
            {
                handler.Dispose();
                return null;
            }

            _activeHandler = handler;
            return handler;
        }

        /// <summary>
        /// Starts a scale operation on the target GameObject.
        /// </summary>
        /// <param name="target">The GameObject to scale.</param>
        /// <param name="onCompleted">Callback invoked with the final scale when the scale completes.</param>
        /// <returns>The scale gizmo handler, or null if initialization failed.</returns>
        public FuseScaleGizmoHandler BeginScale(GameObject target, Action<Vector3> onCompleted = null)
        {
            if (target == null)
            {
                FuseLog.Error("FUSE gizmo manager: Cannot begin scale with null target.");
                return null;
            }

            // Deactivate any existing gizmo
            EndCurrentGizmo();

            var handler = new FuseScaleGizmoHandler();
            if (onCompleted != null)
            {
                handler.OnScaleCompleted += onCompleted;
            }

            if (!handler.Initialize(target))
            {
                handler.Dispose();
                return null;
            }

            _activeHandler = handler;
            return handler;
        }

        /// <summary>
        /// Cancels the current gizmo operation and restores the target's original transform.
        /// </summary>
        public void CancelCurrentGizmo()
        {
            if (_activeHandler != null)
            {
                _activeHandler.Cancel();
                _activeHandler.Dispose();
                _activeHandler = null;
            }
        }

        /// <summary>
        /// Ends the current gizmo operation normally (accepting any changes).
        /// </summary>
        public void EndCurrentGizmo()
        {
            if (_activeHandler != null)
            {
                _activeHandler.Deactivate();
                _activeHandler.Dispose();
                _activeHandler = null;
            }
        }

        /// <summary>
        /// Disposes of all gizmo handlers and cleans up resources.
        /// </summary>
        public void Dispose()
        {
            EndCurrentGizmo();
        }
    }
}
