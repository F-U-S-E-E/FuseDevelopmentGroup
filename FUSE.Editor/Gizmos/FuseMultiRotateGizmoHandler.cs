using FUSE.Infrastructure;
using RLD;
using System;
using UnityEngine;

namespace FUSE.Editor.Gizmos
{
    /// <summary>
    /// Handles rotate gizmo interactions for multiple handlers simultaneously.
    /// Rotates all handlers together around the primary handler's position.
    /// </summary>
    public class FuseMultiRotateGizmoHandler : FuseMultiGizmoHandler
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
                FuseLog.Error("FUSE multi-rotate gizmo: RTGizmosEngine singleton not available.");
                return null;
            }

            return engine.CreateObjectRotationGizmo();
        }

        protected override void OnGizmoCompleted(Vector3 finalPosition, Quaternion finalRotation, Vector3 finalScale)
        {
            // For rotate gizmos, we only care about rotation changes
            OnRotateCompleted?.Invoke(finalRotation);
        }

        protected override void OnGizmoDragUpdate(Gizmo gizmo, int handleId)
        {
            ApplyTransformToAllHandlers(gizmo.TotalDragOffset, gizmo.TotalDragRotation, gizmo.TotalDragScale, false);
        }
    }
}
