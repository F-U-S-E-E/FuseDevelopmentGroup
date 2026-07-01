using FUSE.Editor.EditorHandler;
using FUSE.Infrastructure;
using Helpers;
using RLD;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FUSE.Editor.Gizmos
{
    /// <summary>
    /// Base class for handling RLD gizmo interactions with multiple handlers simultaneously.
    /// Manages initialization, movement tracking, and applies transformations to all handlers.
    /// 
    /// Creates a proxy GameObject for gizmo manipulation to decouple the gizmo
    /// from actual game objects (which may not exist for abstract handlers).
    /// </summary>
    public abstract class FuseMultiGizmoHandler : IDisposable
    {
        /// <summary>
        /// The RLD ObjectTransformGizmo instance being managed.
        /// </summary>
        protected ObjectTransformGizmo TransformGizmo { get; private set; }

        /// <summary>
        /// The collection of target EditorHandlerBase instances being manipulated by the gizmo.
        /// </summary>
        protected List<EditorHandlerBase> Handlers { get; private set; }

        /// <summary>
        /// The primary handler (used as the gizmo's visual anchor).
        /// </summary>
        protected EditorHandlerBase PrimaryHandler { get; private set; }

        /// <summary>
        /// Temporary proxy GameObject that serves as the gizmo target.
        /// Synced to the primary handler's transform at initialization and its changes
        /// are applied back to all handlers when the gizmo completes.
        /// </summary>
        protected GameObject GizmoTarget { get; private set; }

        /// <summary>
        /// Initial positions of all handlers when the gizmo was activated.
        /// </summary>
        protected List<Vector3> InitialPositions { get; private set; }

        protected Vector3 InitialGizmoPosition { get; private set; }

        /// <summary>
        /// Initial rotations of all handlers when the gizmo was activated.
        /// </summary>
        protected List<Quaternion> InitialRotations { get; private set; }

        protected Quaternion InitialGizmoRotation { get; private set; }

        /// <summary>
        /// Initial scales of all handlers when the gizmo was activated.
        /// </summary>
        protected List<Vector3> InitialScales { get; private set; }

        protected Vector3 InitialGizmoScale { get; private set; }

        /// <summary>
        /// Whether the gizmo is currently active and tracking changes.
        /// </summary>
        public bool IsActive { get; private set; }

        public virtual GizmoSpace TransformSpace { get; private set; } = GizmoSpace.Local;

        public FuseGizmoManager.GizmoOrigin GizmoOrigin { get; private set; } = FuseGizmoManager.GizmoOrigin.Object;

        /// <summary>
        /// Initializes the gizmo handler with multiple target EditorHandlerBase instances.
        /// </summary>
        /// <param name="handlers">The EditorHandlerBase instances to manipulate with the gizmo.</param>
        /// <returns>True if initialization succeeded, false otherwise.</returns>
        public virtual bool Initialize(IEnumerable<EditorHandlerBase> handlers)
        {
            var handlerList = new List<EditorHandlerBase>(handlers);

            if (handlerList.Count == 0)
            {
                FuseLog.Error("FUSE multi-gizmo: Cannot initialize with empty handler collection.");
                return false;
            }

            if (handlerList[0] == null)
            {
                FuseLog.Error("FUSE multi-gizmo: First handler in collection is null.");
                return false;
            }

            Handlers = handlerList;
            PrimaryHandler = Handlers[0];

            // Capture initial state for all handlers
            InitialPositions = new List<Vector3>();
            InitialRotations = new List<Quaternion>();
            InitialScales = new List<Vector3>();

            foreach (var handler in Handlers)
            {
                if (handler == null)
                {
                    FuseLog.Error("FUSE multi-gizmo: Null handler in collection.");
                    return false;
                }

                InitialPositions.Add(WorldTransformer.GameToWorld(handler.GetPosition()));
                InitialRotations.Add(handler.GetRotation());
                InitialScales.Add(handler.GetScale());
            }

            // Create a proxy GameObject for the gizmo to manipulate
            GizmoTarget = CreateGizmoTargetObject();
            if (GizmoTarget == null)
            {
                FuseLog.Error("FUSE multi-gizmo: Failed to create gizmo target object.");
                return false;
            }

            if (GizmoOrigin == FuseGizmoManager.GizmoOrigin.Object)
            {
                InitialGizmoPosition = InitialPositions[0];
                InitialGizmoRotation = InitialRotations[0];
                InitialGizmoScale = InitialScales[0];
            }
            else if (GizmoOrigin == FuseGizmoManager.GizmoOrigin.Group)
            {
                // Calculate the average position, rotation, and scale of all handlers
                InitialGizmoPosition = Vector3.zero;
                InitialGizmoRotation = Quaternion.identity;
                InitialGizmoScale = Vector3.one;
                foreach (var pos in InitialPositions)
                {
                    InitialGizmoPosition += pos;
                }
                InitialGizmoPosition /= InitialPositions.Count;
                // For rotation, we can use a simple average for demonstration purposes
                foreach (var rot in InitialRotations)
                {
                    InitialGizmoRotation *= rot;
                }
                InitialGizmoRotation = Quaternion.Slerp(Quaternion.identity, InitialGizmoRotation, 1.0f / InitialRotations.Count);
                // For scale, we can also average the scales
                foreach (var scale in InitialScales)
                {
                    InitialGizmoScale += scale;
                }
                InitialGizmoScale /= InitialScales.Count;
            }

            // Set the proxy to match the primary handler's current transform
            GizmoTarget.transform.position = InitialGizmoPosition;
            GizmoTarget.transform.rotation = InitialGizmoRotation;
            GizmoTarget.transform.localScale = InitialGizmoScale;

            // Create the appropriate gizmo type
            TransformGizmo = CreateGizmo();
            if (TransformGizmo == null)
            {
                FuseLog.Error("FUSE multi-gizmo: Failed to create gizmo instance.");
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

        public virtual void SetTransformSpace(GizmoSpace space)
        {
            TransformSpace = space;
            if (TransformGizmo != null)
            {
                TransformGizmo.SetTransformSpace(TransformSpace);
            }
        }

        public virtual void SetGizmoOrigin(FuseGizmoManager.GizmoOrigin origin)
        {
            GizmoOrigin = origin;
        }

        /// <summary>
        /// Creates a temporary proxy GameObject for the gizmo to manipulate.
        /// </summary>
        /// <returns>A new GameObject, or null if creation failed.</returns>
        protected virtual GameObject CreateGizmoTargetObject()
        {
            var proxyObject = new GameObject("FUSE_MultiGizmoTarget");
            if (proxyObject == null)
            {
                FuseLog.Error("FUSE multi-gizmo: Failed to instantiate gizmo target GameObject.");
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
                    FuseLog.Exception("FUSE multi-gizmo: Failed to clean up gizmo target object", ex);
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
        ///

        private void OnGizmoDragEnd(Gizmo gizmo, int handleId)
        {
            if (PrimaryHandler == null || Handlers == null || Handlers.Count == 0 || GizmoTarget == null)
            {
                return;
            }

            // Capture final transform state from the gizmo target proxy
            var finalPosition = GizmoTarget.transform.position;
            var finalRotation = GizmoTarget.transform.rotation;
            var finalScale = GizmoTarget.transform.localScale;

            // Apply the changes to all handlers
            ApplyTransformToAllHandlers(gizmo.TotalDragOffset, gizmo.TotalDragRotation, gizmo.TotalDragScale, true);

            // Invoke the appropriate completion callback
            OnGizmoCompleted(finalPosition, finalRotation, finalScale);
        }

        /// <summary>
        /// Applies the primary handler's final transform to all handlers while maintaining relative offsets.
        /// </summary>
        protected virtual void ApplyTransformToAllHandlers(Vector3 totalOffset, Quaternion totalRotation, Vector3 totalScale, bool createUndoRedo)
        {
            if (PrimaryHandler == null || Handlers == null || InitialPositions == null)
            {
                return;
            }

            // Apply the delta to all handlers
            for (int i = 0; i < Handlers.Count; i++)
            {
                var handler = Handlers[i];
                if (handler == null) continue;

                if (TransformSpace == GizmoSpace.Local)
                {

                    // Apply position delta
                    var newPosition = InitialPositions[i] + totalOffset;
                    handler.SetPosition(WorldTransformer.WorldToGame(newPosition), createUndoRedo); // Undo only for first

                    // Apply rotation delta
                    var newRotation = totalRotation * InitialRotations[i];
                    handler.SetRotation(newRotation, createUndoRedo);

                    // Apply scale delta
                    var newScale = new Vector3(
                        InitialScales[i].x * totalScale.x,
                        InitialScales[i].y * totalScale.y,
                        InitialScales[i].z * totalScale.z);
                    handler.SetScale(newScale, createUndoRedo);
                }
                else // World space
                {
                    // Apply position delta
                    RotateAroundPivot(InitialPositions[i] + totalOffset,
                        InitialRotations[i],
                        InitialGizmoPosition,
                        totalRotation,
                        out Vector3 newPosition,
                        out Quaternion newRotation);

                    handler.SetPosition(WorldTransformer.WorldToGame(newPosition), createUndoRedo); // Undo only for first
                    // Apply rotation delta
                    handler.SetRotation(newRotation, createUndoRedo);
                    // Apply scale delta
                    var newScale = new Vector3(
                        InitialScales[i].x * totalScale.x,
                        InitialScales[i].y * totalScale.y,
                        InitialScales[i].z * totalScale.z);
                    handler.SetScale(newScale, createUndoRedo);
                }
            }
        }

        public static void RotateAroundPivot(
        Vector3 position,
        Quaternion rotation,
        Vector3 pivot,
        Quaternion pivotRotation,
        out Vector3 newPosition,
        out Quaternion newRotation)
        {
            // Offset from pivot
            Vector3 offset = position - pivot;

            // Rotate the offset around the pivot
            offset = pivotRotation * offset;

            // New world position
            newPosition = pivot + offset;

            // Apply rotation to the object itself
            newRotation = pivotRotation * rotation;
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
        /// Cancels the current gizmo operation and restores the initial transforms.
        /// </summary>
        public void Cancel()
        {
            if (Handlers != null && InitialPositions != null)
            {
                for (int i = 0; i < Handlers.Count; i++)
                {
                    var handler = Handlers[i];
                    if (handler == null) continue;

                    handler.SetPosition(WorldTransformer.WorldToGame(InitialPositions[i]), createUndoRedo: false);
                    handler.SetRotation(InitialRotations[i], createUndoRedo: false);
                    handler.SetScale(InitialScales[i], createUndoRedo: false);
                }
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
                    FuseLog.Exception("FUSE multi-gizmo: Failed to remove gizmo from RTGizmosEngine", ex);
                }
                TransformGizmo = null;
            }

            CleanupGizmoTarget();
            Handlers = null;
            PrimaryHandler = null;
            InitialPositions = null;
            InitialRotations = null;
            InitialScales = null;
        }
    }
}
