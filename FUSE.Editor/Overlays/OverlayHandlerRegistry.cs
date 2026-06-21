using FUSE.Infrastructure;
using System;
using System.Collections.Generic;

namespace FUSE.Editor.Overlays
{
    /// <summary>
    /// Registry for overlay handlers that work with entity + preview-data pairs.
    /// Supports handlers of type IOverlayHandler&lt;TEntity, TPreviewData&gt;.
    /// </summary>
    public class OverlayHandlerRegistry
    {
        private readonly Dictionary<Type, object> _handlers = new Dictionary<Type, object>();

        /// <summary>
        /// Registers a handler for a specific entity type with separate preview data type.
        /// </summary>
        /// <typeparam name="TEntity">The entity type (e.g., TrackNode).</typeparam>
        /// <typeparam name="TPreviewData">The preview data type (e.g., FuseNode).</typeparam>
        /// <param name="handler">The handler instance.</param>
        public void RegisterHandler<TEntity, TPreviewData>(IOverlayHandler<TEntity, TPreviewData> handler)
        {
            if (handler == null)
            {
                FuseLog.Error("OverlayHandlerRegistry: Cannot register null handler.");
                return;
            }

            var key = typeof(TEntity);
            if (_handlers.ContainsKey(key))
            {
                FuseLog.Warning($"OverlayHandlerRegistry: Handler for type '{key.Name}' already registered. Replacing.");
            }

            _handlers[key] = handler;
            FuseLog.Info($"OverlayHandlerRegistry: Registered handler '{handler.HandlerName}' for type '{key.Name}'.");
        }

        /// <summary>
        /// Gets a handler for the given entity type, cast to the appropriate generic type.
        /// </summary>
        /// <typeparam name="TEntity">The entity type.</typeparam>
        /// <typeparam name="TPreviewData">The preview data type.</typeparam>
        /// <returns>The handler, or null if not registered.</returns>
        public IOverlayHandler<TEntity, TPreviewData> GetHandler<TEntity, TPreviewData>()
        {
            var key = typeof(TEntity);
            if (_handlers.TryGetValue(key, out var handler))
            {
                return handler as IOverlayHandler<TEntity, TPreviewData>;
            }

            return null;
        }

        /// <summary>
        /// Checks if a handler is registered for the given type.
        /// </summary>
        /// <typeparam name="TEntity">The entity type to check.</typeparam>
        /// <returns>True if a handler is registered.</returns>
        public bool HasHandler<TEntity>()
        {
            return _handlers.ContainsKey(typeof(TEntity));
        }

        /// <summary>
        /// Unregisters a handler for the given type.
        /// </summary>
        /// <typeparam name="TEntity">The entity type.</typeparam>
        public void UnregisterHandler<TEntity>()
        {
            var key = typeof(TEntity);
            if (_handlers.Remove(key))
            {
                FuseLog.Info($"OverlayHandlerRegistry: Unregistered handler for type '{key.Name}'.");
            }
        }

        /// <summary>
        /// Applies a preview for an entity using its registered handler.
        /// </summary>
        /// <typeparam name="TEntity">The entity type.</typeparam>
        /// <typeparam name="TPreviewData">The preview data type.</typeparam>
        /// <param name="entity">The entity to create a preview for.</param>
        /// <param name="previewData">The preview/pending-edit data.</param>
        /// <param name="previewId">Output: the ID of the created preview.</param>
        /// <returns>The preview data, or null if the handler is not registered or fails.</returns>
        public OverlayPreviewData ApplyPreview<TEntity, TPreviewData>(
            TEntity entity,
            TPreviewData previewData,
            out string previewId)
        {
            previewId = null;

            var handler = GetHandler<TEntity, TPreviewData>();
            if (handler == null)
            {
                FuseLog.Error($"OverlayHandlerRegistry: No handler registered for type '{typeof(TEntity).Name}'.");
                return null;
            }

            if (entity == null)
            {
                FuseLog.Error($"OverlayHandlerRegistry: Cannot apply preview for null entity of type '{typeof(TEntity).Name}'.");
                return null;
            }

            if (!handler.CanHandle(entity))
            {
                FuseLog.Error($"OverlayHandlerRegistry: Handler '{handler.HandlerName}' cannot handle the provided entity.");
                return null;
            }

            previewId = handler.GetEntityId(entity);
            if (string.IsNullOrWhiteSpace(previewId))
            {
                FuseLog.Error($"OverlayHandlerRegistry: Handler '{handler.HandlerName}' returned null/empty entity ID.");
                return null;
            }

            var gameObject = handler.GetTargetGameObject(entity);
            if (gameObject == null)
            {
                FuseLog.Warning($"OverlayHandlerRegistry: Handler '{handler.HandlerName}' returned null GameObject for preview '{previewId}'.");
                return null;
            }

            handler.ExtractPreviewTransform(entity, previewData, out var position, out var rotation, out var scale);
            var renderable = handler.GetRenderable(entity, previewData);
            var objectType = handler.GetObjectType(entity);
            var tint = handler.GetPreviewTint(entity, previewData);
            var selectionAreas = handler.GetSelectionAreas(entity, previewData, position, rotation, scale);

            var previewDataObj = new OverlayPreviewData(gameObject, previewData, previewId)
            {
                Renderable = renderable,
                ObjectType = objectType,
                Tint = tint,
                SelectionAreas = selectionAreas,
                Entity = entity,
                PreviewPosition = position,
                PreviewRotation = rotation,
                PreviewScale = scale
            };

            return previewDataObj;
        }

        /// <summary>
        /// Invokes the OnPreviewSelected callback for a given entity with its handler.
        /// </summary>
        /// <typeparam name="TEntity">The entity type.</typeparam>
        /// <typeparam name="TPreviewData">The preview data type.</typeparam>
        /// <param name="entity">The entity that was selected.</param>
        /// <param name="previewData">The preview data.</param>
        /// <param name="selectionArea">The selection area that was clicked.</param>
        public void InvokeSelectionCallback<TEntity, TPreviewData>(
            TEntity entity,
            TPreviewData previewData,
            OverlaySelectionArea selectionArea)
        {
            var handler = GetHandler<TEntity, TPreviewData>();
            if (handler != null)
            {
                handler.OnPreviewSelected(entity, previewData, selectionArea);
            }
        }

        /// <summary>
        /// Clears all registered handlers.
        /// </summary>
        public void ClearAllHandlers()
        {
            _handlers.Clear();
            FuseLog.Info("OverlayHandlerRegistry: All handlers cleared.");
        }

        /// <summary>
        /// Gets the count of registered handlers.
        /// </summary>
        public int GetHandlerCount() => _handlers.Count;
    }
}
