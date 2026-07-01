using FUSE.Editor.EditorHandler;
using FUSE.Infrastructure;
using RLD;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FUSE.Editor.Gizmos
{
    /// <summary>
    /// Manages active gizmo handlers and ensures only one gizmo is active at a time.
    /// Provides a simplified API for starting move/rotate/scale operations on single or multiple handlers.
    /// </summary>
    public class FuseGizmoManager : IDisposable
    {
        public enum GizmoOrigin
        {
            Object,
            Group
        }

        private GizmoOrigin origin = GizmoOrigin.Object;
        private GizmoSpace space = GizmoSpace.Local;

        private FuseGizmoHandler _activeHandler;
        private FuseMultiGizmoHandler _activeMultiHandler;

        /// <summary>
        /// The currently active gizmo handler, or null if no gizmo is active.
        /// </summary>
        public FuseGizmoHandler ActiveHandler => _activeHandler;

        /// <summary>
        /// The currently active multi-handler gizmo, or null if no multi-gizmo is active.
        /// </summary>
        public FuseMultiGizmoHandler ActiveMultiHandler => _activeMultiHandler;

        /// <summary>
        /// Whether a gizmo is currently active (single or multi).
        /// </summary>
        public bool HasActiveGizmo => (_activeHandler != null && _activeHandler.IsActive) || (_activeMultiHandler != null && _activeMultiHandler.IsActive);

        public GizmoOrigin GetGizmoOrigin()
        {
            return origin;
        }

        public void SetGizmoOrigin(GizmoOrigin newOrigin)
        {
            origin = newOrigin;

            if (_activeHandler != null)
            {
                _activeHandler.SetGizmoOrigin(origin);
            }
            if (_activeMultiHandler != null)
            {
                _activeMultiHandler.SetGizmoOrigin(origin);
            }
        }

        public void SetGizmoOriginToObject()
        {
            SetGizmoOrigin(GizmoOrigin.Object);
        }

        public void SetGizmoOriginToGroup()
        {
            SetGizmoOrigin(GizmoOrigin.Group);
        }

        public GizmoSpace GetGizmoSpace()
        {
            return space;
        }

        public void SetGizmoSpace(GizmoSpace newSpace)
        {
            space = newSpace;

            if (_activeHandler != null)
            {
                _activeHandler.SetTransformSpace(space);
            }
            if (_activeMultiHandler != null)
            {
                _activeMultiHandler.SetTransformSpace(space);
            }
        }

        public void SetGizmoSpaceToLocal()
        {
            SetGizmoSpace(GizmoSpace.Local);
        }

        public void SetGizmoSpaceToGlobal()
        {
            SetGizmoSpace(GizmoSpace.Global);
        }

        /// <summary>
        /// Starts a move operation on the target EditorHandlerBase.
        /// </summary>
        /// <param name="handler">The EditorHandlerBase to move.</param>
        /// <param name="onCompleted">Callback invoked with the final position when the move completes.</param>
        /// <returns>The move gizmo handler, or null if initialization failed.</returns>
        public FuseMoveGizmoHandler BeginMove(EditorHandlerBase handler, Action<Vector3> onCompleted = null)
        {
            if (handler == null)
            {
                FuseLog.Error("FUSE gizmo manager: Cannot begin move with null handler.");
                return null;
            }

            // Deactivate any existing gizmo
            EndCurrentGizmo();

            var moveHandler = new FuseMoveGizmoHandler();

            moveHandler.SetTransformSpace(space);
            moveHandler.SetGizmoOrigin(origin);

            if (onCompleted != null)
            {
                moveHandler.OnMoveCompleted += onCompleted;
            }

            if (!moveHandler.Initialize(handler))
            {
                moveHandler.Dispose();
                return null;
            }

            _activeHandler = moveHandler;
            return moveHandler;
        }

        /// <summary>
        /// Starts a rotate operation on the target EditorHandlerBase.
        /// </summary>
        /// <param name="handler">The EditorHandlerBase to rotate.</param>
        /// <param name="onCompleted">Callback invoked with the final rotation when the rotate completes.</param>
        /// <returns>The rotate gizmo handler, or null if initialization failed.</returns>
        public FuseRotateGizmoHandler BeginRotate(EditorHandlerBase handler, Action<Quaternion> onCompleted = null)
        {
            if (handler == null)
            {
                FuseLog.Error("FUSE gizmo manager: Cannot begin rotate with null handler.");
                return null;
            }

            // Deactivate any existing gizmo
            EndCurrentGizmo();

            var rotateHandler = new FuseRotateGizmoHandler();

            rotateHandler.SetTransformSpace(space);
            rotateHandler.SetGizmoOrigin(origin);

            if (onCompleted != null)
            {
                rotateHandler.OnRotateCompleted += onCompleted;
            }

            if (!rotateHandler.Initialize(handler))
            {
                rotateHandler.Dispose();
                return null;
            }

            _activeHandler = rotateHandler;
            return rotateHandler;
        }

        /// <summary>
        /// Starts a scale operation on the target EditorHandlerBase.
        /// </summary>
        /// <param name="handler">The EditorHandlerBase to scale.</param>
        /// <param name="onCompleted">Callback invoked with the final scale when the scale completes.</param>
        /// <returns>The scale gizmo handler, or null if initialization failed.</returns>
        public FuseScaleGizmoHandler BeginScale(EditorHandlerBase handler, Action<Vector3> onCompleted = null)
        {
            if (handler == null)
            {
                FuseLog.Error("FUSE gizmo manager: Cannot begin scale with null handler.");
                return null;
            }

            // Deactivate any existing gizmo
            EndCurrentGizmo();

            var scaleHandler = new FuseScaleGizmoHandler();

            scaleHandler.SetTransformSpace(space);
            scaleHandler.SetGizmoOrigin(origin);

            if (onCompleted != null)
            {
                scaleHandler.OnScaleCompleted += onCompleted;
            }

            if (!scaleHandler.Initialize(handler))
            {
                scaleHandler.Dispose();
                return null;
            }

            _activeHandler = scaleHandler;
            return scaleHandler;
        }

        /// <summary>
        /// Starts a move operation on multiple target EditorHandlerBase instances.
        /// All handlers are moved together while maintaining their relative positions.
        /// </summary>
        /// <param name="handlers">The EditorHandlerBase instances to move.</param>
        /// <param name="onCompleted">Callback invoked with the final position when the move completes.</param>
        /// <returns>The multi-move gizmo handler, or null if initialization failed.</returns>
        public FuseMultiMoveGizmoHandler BeginMoveMultiple(IEnumerable<EditorHandlerBase> handlers, Action<Vector3> onCompleted = null)
        {
            var handlerList = new List<EditorHandlerBase>(handlers);

            if (handlerList.Count == 0)
            {
                FuseLog.Error("FUSE gizmo manager: Cannot begin multi-move with empty handler collection.");
                return null;
            }

            // Deactivate any existing gizmo
            EndCurrentGizmo();

            var moveHandler = new FuseMultiMoveGizmoHandler();

            moveHandler.SetTransformSpace(space);
            moveHandler.SetGizmoOrigin(origin);

            if (onCompleted != null)
            {
                moveHandler.OnMoveCompleted += onCompleted;
            }

            if (!moveHandler.Initialize(handlerList))
            {
                moveHandler.Dispose();
                return null;
            }

            _activeMultiHandler = moveHandler;
            return moveHandler;
        }

        /// <summary>
        /// Starts a rotate operation on multiple target EditorHandlerBase instances.
        /// All handlers are rotated together around the primary handler's position.
        /// </summary>
        /// <param name="handlers">The EditorHandlerBase instances to rotate.</param>
        /// <param name="onCompleted">Callback invoked with the final rotation when the rotate completes.</param>
        /// <returns>The multi-rotate gizmo handler, or null if initialization failed.</returns>
        public FuseMultiRotateGizmoHandler BeginRotateMultiple(IEnumerable<EditorHandlerBase> handlers, Action<Quaternion> onCompleted = null)
        {
            var handlerList = new List<EditorHandlerBase>(handlers);

            if (handlerList.Count == 0)
            {
                FuseLog.Error("FUSE gizmo manager: Cannot begin multi-rotate with empty handler collection.");
                return null;
            }

            // Deactivate any existing gizmo
            EndCurrentGizmo();

            var rotateHandler = new FuseMultiRotateGizmoHandler();

            rotateHandler.SetTransformSpace(space);
            rotateHandler.SetGizmoOrigin(origin);

            if (onCompleted != null)
            {
                rotateHandler.OnRotateCompleted += onCompleted;
            }

            if (!rotateHandler.Initialize(handlerList))
            {
                rotateHandler.Dispose();
                return null;
            }

            _activeMultiHandler = rotateHandler;
            return rotateHandler;
        }

        /// <summary>
        /// Starts a scale operation on multiple target EditorHandlerBase instances.
        /// All handlers are scaled together while maintaining their relative scales.
        /// </summary>
        /// <param name="handlers">The EditorHandlerBase instances to scale.</param>
        /// <param name="onCompleted">Callback invoked with the final scale when the scale completes.</param>
        /// <returns>The multi-scale gizmo handler, or null if initialization failed.</returns>
        public FuseMultiScaleGizmoHandler BeginScaleMultiple(IEnumerable<EditorHandlerBase> handlers, Action<Vector3> onCompleted = null)
        {
            var handlerList = new List<EditorHandlerBase>(handlers);

            if (handlerList.Count == 0)
            {
                FuseLog.Error("FUSE gizmo manager: Cannot begin multi-scale with empty handler collection.");
                return null;
            }

            // Deactivate any existing gizmo
            EndCurrentGizmo();

            var scaleHandler = new FuseMultiScaleGizmoHandler();

            scaleHandler.SetTransformSpace(space);
            scaleHandler.SetGizmoOrigin(origin);

            if (onCompleted != null)
            {
                scaleHandler.OnScaleCompleted += onCompleted;
            }

            if (!scaleHandler.Initialize(handlerList))
            {
                scaleHandler.Dispose();
                return null;
            }

            _activeMultiHandler = scaleHandler;
            return scaleHandler;
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

            if (_activeMultiHandler != null)
            {
                _activeMultiHandler.Cancel();
                _activeMultiHandler.Dispose();
                _activeMultiHandler = null;
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

            if (_activeMultiHandler != null)
            {
                _activeMultiHandler.Deactivate();
                _activeMultiHandler.Dispose();
                _activeMultiHandler = null;
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
