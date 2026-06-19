using FUSE.Infrastructure;
using RLD;
using System;
using UnityEngine;

namespace FUSE.Editor.Gizmos
{
    /// <summary>
    /// Handles rotate gizmo interactions. Invokes a callback with the new rotation
    /// when the gizmo is released.
    /// </summary>
    public class FuseRotateGizmoHandler : FuseGizmoHandler
    {
        /// <summary>
        /// Called when the rotate operation completes with the final rotation.
        /// </summary>
        public event Action<Quaternion> OnRotateCompleted;

        protected override ObjectTransformGizmo CreateGizmo()
        {
            var engine = MonoSingleton<RTGizmosEngine>.Get;
            if (engine == null)
            {
                FuseLog.Error("FUSE rotate gizmo: RTGizmosEngine singleton not available.");
                return null;
            }

            return engine.CreateObjectRotationGizmo();
        }

        protected override void OnGizmoCompleted(Vector3 finalPosition, Quaternion finalRotation, Vector3 finalScale)
        {
            // For rotate gizmos, we only care about rotation changes
            OnRotateCompleted?.Invoke(finalRotation);
        }

        /// <summary>
        /// Sets the transform space for the rotate gizmo (Global or Local).
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
