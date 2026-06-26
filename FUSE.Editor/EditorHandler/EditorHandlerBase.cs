using FUSE.Authoring.Data;
using FUSE.Editor.Overlays;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FUSE.Editor.EditorHandler
{
    /// <summary>
    /// Abstract base class for editor handlers that manage rendering and editing of game objects.
    /// Each handler encapsulates rendering logic, transform management, and data serialization.
    /// </summary>
    public abstract class EditorHandlerBase
    {
        /// <summary>
        /// Unique identifier for this handler instance.
        /// </summary>
        public string ID { get; protected set; }

        /// <summary>
        /// The original entity/object being edited.
        /// </summary>
        public object Entity { get; protected set; }

        /// <summary>
        /// The preview/pending-edit data.
        /// </summary>
        public object FuseData { get; protected set; }

        /// <summary>
        /// Whether this handler's preview is currently visible.
        /// </summary>
        public abstract bool IsVisible { get; set; }

        /// <summary>
        /// Static method that determines if this handler type can handle the given entity.
        /// Each concrete handler should implement this as:
        /// public static bool CanHandleEntity(object entity) { ... }
        /// 
        /// This is called by the registry via reflection without instantiating the handler.
        /// </summary>
        /// <remarks>
        /// Concrete implementations should follow this pattern:
        /// <code>
        /// public static bool CanHandleEntity(object entity)
        /// {
        ///     return entity is TrackNode;
        /// }
        /// </code>
        /// </remarks>
        public abstract bool CanHandleEntity(object entity);

        /// <summary>
        /// The selectable areas within this preview for click-based selection.
        /// </summary>
        public abstract OverlaySelectionArea[] SelectionAreas { get; }

        /// <summary>
        /// Renders this handler's preview overlay using the provided camera.
        /// </summary>
        /// <param name="camera">The camera to render with.</param>
        public abstract void Render(Camera camera);

        /// <summary>
        /// Gets the preview position.
        /// </summary>
        /// <returns>The preview position in world space.</returns>
        public abstract Vector3 GetPosition();

        /// <summary>
        /// Gets the preview rotation.
        /// </summary>
        /// <returns>The preview rotation.</returns>
        public abstract Quaternion GetRotation();

        /// <summary>
        /// Gets the preview scale.
        /// </summary>
        /// <returns>The preview scale.</returns>
        public abstract Vector3 GetScale();

        /// <summary>
        /// Sets the preview position.
        /// </summary>
        /// <param name="position">The new position.</param>
        /// <param name="createUndoRedo">Whether to create an undo/redo entry for this change.</param>
        public abstract void SetPosition(Vector3 position, bool createUndoRedo = true);

        /// <summary>
        /// Sets the preview rotation.
        /// </summary>
        /// <param name="rotation">The new rotation.</param>
        /// <param name="createUndoRedo">Whether to create an undo/redo entry for this change.</param>
        public abstract void SetRotation(Quaternion rotation, bool createUndoRedo = true);

        /// <summary>
        /// Sets the preview scale.
        /// </summary>
        /// <param name="scale">The new scale.</param>
        /// <param name="createUndoRedo">Whether to create an undo/redo entry for this change.</param>
        public abstract void SetScale(Vector3 scale, bool createUndoRedo = true);

        /// <summary>
        /// Gets the underlying entity/object being edited.
        /// </summary>
        /// <returns>The entity object.</returns>
        public abstract object GetObject();

        /// <summary>
        /// Gets the preview/pending-edit data.
        /// </summary>
        /// <returns>The preview data.</returns>
        public abstract object GetData();

        /// <summary>
        /// Sets the preview/pending-edit data.
        /// </summary>
        /// <param name="data">The new preview data.</param>
        public abstract void SetData(object data, bool createUndoRedo = true);

        /// <summary>
        /// Applies the pending preview data to the original entity.
        /// </summary>
        public abstract void ApplyData();

        /// <summary>
        /// Saves the preview data to a mod definition.
        /// </summary>
        /// <param name="mod">The mod definition to save to.</param>
        public abstract void SaveData(FuseModDefinition mod);

        /// <summary>
        /// Gets all editable properties exposed by this handler.
        /// </summary>
        /// <returns>Dictionary of property names to (type, value) tuples.</returns>
        public abstract Dictionary<string, (Type type, object value)> GetProperties();

        /// <summary>
        /// Updates multiple properties at once.
        /// </summary>
        /// <param name="properties">Dictionary of property names to values.</param>
        public abstract void UpdateProperties(Dictionary<string, object> properties, bool createUndoRedo = true);

        /// <summary>
        /// Updates a single property.
        /// </summary>
        /// <param name="propertyName">The name of the property to update.</param>
        /// <param name="value">The new value.</param>
        /// <param name="createUndoRedo">Whether to create an undo/redo entry for this change.</param>
        public abstract void UpdateProperty(string propertyName, object value, bool createUndoRedo = true);

        /// <summary>
        /// Gets a tooltip or description of this handler's object.
        /// </summary>
        /// <returns>A string description.</returns>
        public abstract string GetTooltip();

        /// <summary>
        /// Checks equality with another handler.
        /// </summary>
        /// <param name="handler">The handler to compare with.</param>
        /// <returns>True if the handlers represent the same object.</returns>
        public abstract override bool Equals(object handler);
    }
}
