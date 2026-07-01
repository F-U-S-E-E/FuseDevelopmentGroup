using FUSE.Infrastructure;
using RLD;
using System;
using UnityEngine;

namespace FUSE.Editor.Gizmos
{
    /// <summary>
    /// Handles scale gizmo interactions for multiple handlers simultaneously.
    /// Scales all handlers together while maintaining their relative scales.
    /// </summary>
    public class FuseMultiScaleGizmoHandler : FuseMultiGizmoHandler
    {
        /// <summary>
        /// Called when the scale operation completes with the final scale.
        /// </summary>
        public event Action<Vector3> OnScaleCompleted;

        protected override ObjectTransformGizmo CreateGizmo()
        {
            var engine = MonoSingleton<RTGizmosEngine>.Get;
            if (engine == null)
            {
                FuseLog.Error("FUSE multi-scale gizmo: RTGizmosEngine singleton not available.");
                return null;
            }

            return engine.CreateObjectScaleGizmo();
        }

        protected override void OnGizmoCompleted(Vector3 finalPosition, Quaternion finalRotation, Vector3 finalScale)
        {
            // For scale gizmos, we only care about scale changes
            OnScaleCompleted?.Invoke(finalScale);
        }

        /// <summary>
        /// Sets whether to use uniform scaling (all axes together) or per-axis scaling.
        /// </summary>
        public void SetUniformScaling(bool uniform)
        {
            // Note: The exact API for configuring uniform scaling depends on the RLD version
            // This is a placeholder for future implementation if needed
            FuseLog.Info($"FUSE multi-scale gizmo: Uniform scaling configuration not yet implemented (requested: {uniform})");
        }
    }
}
