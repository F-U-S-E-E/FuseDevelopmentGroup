using FUSE.Infrastructure;
using Helpers;
using RLD;
using System;
using UnityEngine;

namespace FUSE.Editor.Gizmos
{
    /// <summary>
    /// Handles move gizmo interactions for multiple handlers simultaneously.
    /// Moves all handlers together while maintaining their relative positions.
    /// </summary>
    public class FuseMultiMoveGizmoHandler : FuseMultiGizmoHandler
    {
        /// <summary>
        /// Called when the move operation completes with the final position.
        /// </summary>
        public event Action<Vector3> OnMoveCompleted;

        public override GizmoSpace TransformSpace => GizmoSpace.Local;

        protected override ObjectTransformGizmo CreateGizmo()
        {
            var engine = MonoSingleton<RTGizmosEngine>.Get;
            if (engine == null)
            {
                FuseLog.Error("FUSE multi-move gizmo: RTGizmosEngine singleton not available.");
                return null;
            }

            return engine.CreateObjectMoveGizmo();
        }

        protected override void OnGizmoCompleted(Vector3 finalPosition, Quaternion finalRotation, Vector3 finalScale)
        {
            // For move gizmos, we only care about position changes
            OnMoveCompleted?.Invoke(finalPosition);
        }

        protected override void OnGizmoDragUpdate(Gizmo gizmo, int handleId)
        {
            // Move all handlers together while maintaining their relative positions
            ApplyTransformToAllHandlers(gizmo.TotalDragOffset, gizmo.TotalDragRotation, gizmo.TotalDragScale, false);
        }
    }
}
