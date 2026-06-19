using FUSE.Infrastructure;
using RLD;
using System;
using UnityEngine;

namespace FUSE.Editor.Gizmos
{
    /// <summary>
    /// Base class for handling RLD gizmo interactions. Manages initialization,
    /// movement tracking, and provides callbacks when the gizmo operation completes.
    /// </summary>
    public abstract class FuseGizmoHandler : IDisposable
    {
        /// <summary>
        /// The RLD ObjectTransformGizmo instance being managed.
        /// </summary>
        protected ObjectTransformGizmo TransformGizmo { get; private set; }

        /// <summary>
        /// The target GameObject the gizmo is manipulating.
        /// </summary>
        protected GameObject Target { get; private set; }

        /// <summary>
        /// Initial position of the target when the gizmo was activated.
        /// </summary>
        protected Vector3 InitialPosition { get; private set; }

        /// <summary>
        /// Initial rotation of the target when the gizmo was activated.
        /// </summary>
        protected Quaternion InitialRotation { get; private set; }

        /// <summary>
        /// Initial scale of the target when the gizmo was activated.
        /// </summary>
        protected Vector3 InitialScale { get; private set; }

        /// <summary>
        /// Whether the gizmo is currently active and tracking changes.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Initializes the gizmo handler with a target GameObject.
        /// </summary>
        /// <param name="target">The GameObject to manipulate with the gizmo.</param>
        /// <returns>True if initialization succeeded, false otherwise.</returns>
        public virtual bool Initialize(GameObject target)
        {
            if (target == null)
            {
                FuseLog.Error("FUSE gizmo: Cannot initialize with null target.");
                return false;
            }

            Target = target;
            InitialPosition = target.transform.position;
            InitialRotation = target.transform.rotation;
            InitialScale = target.transform.localScale;

            // Create the appropriate gizmo type
            TransformGizmo = CreateGizmo();
            if (TransformGizmo == null)
            {
                FuseLog.Error("FUSE gizmo: Failed to create gizmo instance.");
                return false;
            }

            // Configure the gizmo
            ConfigureGizmo();

            // Set the target for manipulation
            TransformGizmo.SetTargetObject(target);
            TransformGizmo.RefreshPositionAndRotation();

            // Register for gizmo events
            RegisterGizmoEvents();

            IsActive = true;
            return true;
        }

        /// <summary>
        /// Creates the specific type of gizmo (move, rotate, scale).
        /// Must be implemented by derived classes.
        /// </summary>
        protected abstract ObjectTransformGizmo CreateGizmo();

        /// <summary>
        /// Configures gizmo-specific settings (transform space, pivot, etc.).
        /// Can be overridden by derived classes.
        /// </summary>
        protected virtual void ConfigureGizmo()
        {
            if (TransformGizmo == null) return;

            // Default to world space and center pivot
            TransformGizmo.SetTransformSpace(GizmoSpace.Global);
            TransformGizmo.SetTransformPivot(GizmoObjectTransformPivot.ObjectCenterPivot);
        }

        /// <summary>
        /// Registers for gizmo drag events to track when manipulation completes.
        /// </summary>
        private void RegisterGizmoEvents()
        {
            if (TransformGizmo == null || TransformGizmo.Gizmo == null) return;

            TransformGizmo.Gizmo.PreDragBegin += OnGizmoDragBegin;
            TransformGizmo.Gizmo.PostDragUpdate += OnGizmoDragUpdate;
            TransformGizmo.Gizmo.PostDragEnd += OnGizmoDragEnd;
        }

        /// <summary>
        /// Unregisters from gizmo drag events.
        /// </summary>
        private void UnregisterGizmoEvents()
        {
            if (TransformGizmo == null || TransformGizmo.Gizmo == null) return;

            TransformGizmo.Gizmo.PreDragBegin -= OnGizmoDragBegin;
            TransformGizmo.Gizmo.PostDragUpdate -= OnGizmoDragUpdate;
            TransformGizmo.Gizmo.PostDragEnd -= OnGizmoDragEnd;
        }

        /// <summary>
        /// Called when the user begins dragging the gizmo.
        /// </summary>
        protected virtual void OnGizmoDragBegin(Gizmo gizmo, int handleId)
        {
            // Override in derived classes if needed
        }

        /// <summary>
        /// Called continuously while the user drags the gizmo.
        /// </summary>
        protected virtual void OnGizmoDragUpdate(Gizmo gizmo, int handleId)
        {
            // Override in derived classes if needed
        }

        /// <summary>
        /// Called when the user releases the gizmo. Invokes the completion callback.
        /// </summary>
        private void OnGizmoDragEnd(Gizmo gizmo, int handleId)
        {
            if (Target == null)
            {
                return;
            }

            // Capture final transform state
            var finalPosition = Target.transform.position;
            var finalRotation = Target.transform.rotation;
            var finalScale = Target.transform.localScale;

            // Invoke the appropriate completion callback
            OnGizmoCompleted(finalPosition, finalRotation, finalScale);
        }

        /// <summary>
        /// Called when the gizmo manipulation is complete. Override to handle
        /// the final transform state.
        /// </summary>
        /// <param name="finalPosition">The final position after manipulation.</param>
        /// <param name="finalRotation">The final rotation after manipulation.</param>
        /// <param name="finalScale">The final scale after manipulation.</param>
        protected abstract void OnGizmoCompleted(Vector3 finalPosition, Quaternion finalRotation, Vector3 finalScale);

        /// <summary>
        /// Cancels the current gizmo operation and restores the initial transform.
        /// </summary>
        public void Cancel()
        {
            if (Target != null)
            {
                Target.transform.position = InitialPosition;
                Target.transform.rotation = InitialRotation;
                Target.transform.localScale = InitialScale;
            }

            Deactivate();
        }

        /// <summary>
        /// Deactivates the gizmo and cleans up resources.
        /// </summary>
        public void Deactivate()
        {
            if (!IsActive)
            {
                return;
            }

            UnregisterGizmoEvents();

            if (TransformGizmo != null)
            {
                TransformGizmo.SetEnabled(false);
            }

            IsActive = false;
        }

        /// <summary>
        /// Disposes of the gizmo handler and destroys the gizmo.
        /// </summary>
        public virtual void Dispose()
        {
            Deactivate();

            if (TransformGizmo != null && TransformGizmo.Gizmo != null)
            {
                try
                {
                    var engine = MonoSingleton<RTGizmosEngine>.Get;
                    if (engine != null)
                    {
                        engine.RemoveGizmo(TransformGizmo.Gizmo);
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE gizmo: Failed to remove gizmo from RTGizmosEngine", ex);
                }
                TransformGizmo = null;
            }

            Target = null;
        }
    }
}
