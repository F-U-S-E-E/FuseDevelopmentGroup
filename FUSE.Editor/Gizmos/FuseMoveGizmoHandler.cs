using FUSE.Infrastructure;
using RLD;
using System;
using UnityEngine;

namespace FUSE.Editor.Gizmos
{
    /// <summary>
    /// Handles move gizmo interactions. Invokes a callback with the new position
    /// when the gizmo is released.
    /// </summary>
    public class FuseMoveGizmoHandler : FuseGizmoHandler
    {
        /// <summary>
        /// Called when the move operation completes with the final position.
        /// </summary>
        public event Action<Vector3> OnMoveCompleted;

        protected override ObjectTransformGizmo CreateGizmo()
        {
            var engine = MonoSingleton<RTGizmosEngine>.Get;
            if (engine == null)
            {
                FuseLog.Error("FUSE move gizmo: RTGizmosEngine singleton not available.");
                return null;
            }

            return engine.CreateObjectMoveGizmo();
        }

        protected override void OnGizmoCompleted(Vector3 finalPosition, Quaternion finalRotation, Vector3 finalScale)
        {
            // For move gizmos, we only care about position changes
            OnMoveCompleted?.Invoke(finalPosition);
        }

        /// <summary>
        /// Sets the transform space for the move gizmo (Global or Local).
        /// </summary>
        public void SetTransformSpace(GizmoSpace space)
        {
            if (TransformGizmo != null)
            {
                TransformGizmo.SetTransformSpace(space);
            }
        }
    }
}
