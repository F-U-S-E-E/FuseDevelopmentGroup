using FUSE.Editor.EditorHandler;
using FUSE.Infrastructure;
using Helpers;
using RLD;
using System;
using UnityEngine;

namespace FUSE.Editor.Gizmos
{
    /// <summary>
    /// Base class for handling RLD gizmo interactions. Manages initialization,
    /// movement tracking, and provides callbacks when the gizmo operation completes.
    /// 
    /// Creates a proxy GameObject for gizmo manipulation to decouple the gizmo
    /// from the actual game object (which may not exist for abstract handlers).
    /// </summary>
    public abstract class FuseGizmoHandler : IDisposable
    {
        /// <summary>
        /// The RLD ObjectTransformGizmo instance being managed.
        /// </summary>
        protected ObjectTransformGizmo TransformGizmo { get; private set; }

        /// <summary>
        /// The target EditorHandlerBase being manipulated by the gizmo.
        /// </summary>
        protected EditorHandlerBase Handler { get; private set; }

        /// <summary>
        /// Temporary proxy GameObject that serves as the gizmo target.
        /// Synced to the handler's transform at initialization and its changes
        /// are applied back to the handler when the gizmo completes.
        /// </summary>
        protected GameObject GizmoTarget { get; private set; }

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

        public GizmoSpace TransformSpace { get; private set; } = GizmoSpace.Global;

        public FuseGizmoManager.GizmoOrigin GizmoOrigin { get; private set; } = FuseGizmoManager.GizmoOrigin.Object;

        /// <summary>
        /// Initializes the gizmo handler with a target EditorHandlerBase.
        /// </summary>
        /// <param name="handler">The EditorHandlerBase to manipulate with the gizmo.</param>
        /// <returns>True if initialization succeeded, false otherwise.</returns>
        public virtual bool Initialize(EditorHandlerBase handler)
        {
            if (handler == null)
            {
                FuseLog.Error("FUSE gizmo: Cannot initialize with null handler.");
                return false;
            }

            Handler = handler;
            InitialPosition = WorldTransformer.GameToWorld(handler.GetPosition());
            InitialRotation = handler.GetRotation();
            InitialScale = handler.GetScale();

            // Create a proxy GameObject for the gizmo to manipulate
            GizmoTarget = CreateGizmoTargetObject();
            if (GizmoTarget == null)
            {
                FuseLog.Error("FUSE gizmo: Failed to create gizmo target object.");
                return false;
            }

            // Set the proxy to match the handler's current transform
            GizmoTarget.transform.position = InitialPosition;
            GizmoTarget.transform.rotation = InitialRotation;
            GizmoTarget.transform.localScale = InitialScale;

            // Create the appropriate gizmo type
            TransformGizmo = CreateGizmo();
            if (TransformGizmo == null)
            {
                FuseLog.Error("FUSE gizmo: Failed to create gizmo instance.");
                CleanupGizmoTarget();
                return false;
            }

            // Configure the gizmo
            ConfigureGizmo();

            // Set the proxy as the gizmo target
            TransformGizmo.SetTargetObject(GizmoTarget);
            TransformGizmo.RefreshPositionAndRotation();

            // Register for gizmo events
            RegisterGizmoEvents();

            IsActive = true;
            return true;
        }

        public void SetTransformSpace(GizmoSpace space)
        {
            TransformSpace = space;
            if (TransformGizmo != null)
            {
                TransformGizmo.SetTransformSpace(space);
            }
        }

        public void SetGizmoOrigin(FuseGizmoManager.GizmoOrigin origin)
        {
            GizmoOrigin = origin;
        }

        /// <summary>
        /// Creates a temporary proxy GameObject for the gizmo to manipulate.
        /// </summary>
        /// <returns>A new GameObject, or null if creation failed.</returns>
        protected virtual GameObject CreateGizmoTargetObject()
        {
            var proxyObject = new GameObject("FUSE_GizmoTarget");
            if (proxyObject == null)
            {
                FuseLog.Error("FUSE gizmo: Failed to instantiate gizmo target GameObject.");
                return null;
            }

            // Ensure the proxy doesn't show up in the scene
            proxyObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable;
            return proxyObject;
        }

        /// <summary>
        /// Cleans up the temporary gizmo target object.
        /// </summary>
        protected virtual void CleanupGizmoTarget()
        {
            if (GizmoTarget != null)
            {
                try
                {
                    UnityEngine.Object.DestroyImmediate(GizmoTarget);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE gizmo: Failed to clean up gizmo target object", ex);
                }
                GizmoTarget = null;
            }
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
            TransformGizmo.SetTransformSpace(TransformSpace);
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
            if (Handler == null || GizmoTarget == null)
            {
                return;
            }

            // Capture final transform state from the gizmo target proxy
            var finalPosition = GizmoTarget.transform.position;
            var finalRotation = GizmoTarget.transform.rotation;
            var finalScale = GizmoTarget.transform.localScale;

            // Apply the changes through the handler
            Handler.SetPosition(WorldTransformer.WorldToGame(finalPosition), createUndoRedo: true);
            Handler.SetRotation(finalRotation, createUndoRedo: true);
            Handler.SetScale(finalScale, createUndoRedo: true);

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
            if (Handler != null)
            {
                Handler.SetPosition(WorldTransformer.WorldToGame(InitialPosition), createUndoRedo: false);
                Handler.SetRotation(InitialRotation, createUndoRedo: false);
                Handler.SetScale(InitialScale, createUndoRedo: false);
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

            CleanupGizmoTarget();
            Handler = null;
        }
    }
}
