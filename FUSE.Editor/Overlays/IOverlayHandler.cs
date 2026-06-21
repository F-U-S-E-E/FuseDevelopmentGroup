using UnityEngine;

namespace FUSE.Editor.Overlays
{
    /// <summary>
    /// Generic interface for handling overlay preview creation with dual parameters:
    /// - TEntity: The original/actual object being previewed (e.g., TrackNode)
    /// - TPreviewData: The pending edit data (e.g., FuseNode)
    /// 
    /// This supports workflows where the overlay needs both the original state 
    /// and the preview/pending-edit state separately.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being previewed (e.g., TrackNode, Building).</typeparam>
    /// <typeparam name="TPreviewData">The preview/pending-edit data type (e.g., FuseNode, PendingBuildingData).</typeparam>
    public interface IOverlayHandler<TEntity, TPreviewData>
    {
        /// <summary>
        /// Gets a unique identifier for this handler (e.g., "TrackNode", "Building").
        /// Used for logging and debugging.
        /// </summary>
        string HandlerName { get; }

        /// <summary>
        /// Checks if the handler can process the given entity.
        /// </summary>
        /// <param name="entity">The entity to check.</param>
        /// <returns>True if this handler can process the entity.</returns>
        bool CanHandle(TEntity entity);

        /// <summary>
        /// Extracts a unique identifier from the entity.
        /// This ID is used to track the preview in the overlay system.
        /// </summary>
        /// <param name="entity">The entity to get the ID from.</param>
        /// <returns>A unique string identifier.</returns>
        string GetEntityId(TEntity entity);

        /// <summary>
        /// Gets the GameObject to attach the preview to.
        /// This is the original object that the preview will display alongside.
        /// </summary>
        /// <param name="entity">The entity being previewed.</param>
        /// <returns>The target GameObject, or null if not applicable.</returns>
        GameObject GetTargetGameObject(TEntity entity);

        /// <summary>
        /// Extracts preview transform data from the preview data object.
        /// These values represent the pending edits that should be visualized.
        /// </summary>
        /// <param name="entity">The original entity (for reference/context).</param>
        /// <param name="previewData">The preview/pending-edit data containing new transform values.</param>
        /// <param name="position">Output: the preview position.</param>
        /// <param name="rotation">Output: the preview rotation.</param>
        /// <param name="scale">Output: the preview scale.</param>
        void ExtractPreviewTransform(
            TEntity entity,
            TPreviewData previewData,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale);

        /// <summary>
        /// Gets the renderable interface for this entity, if custom rendering is needed.
        /// Return null to use default wireframe rendering.
        /// </summary>
        /// <param name="entity">The original entity.</param>
        /// <param name="previewData">The preview/pending-edit data.</param>
        /// <returns>An IOverlayRenderable implementation, or null for default rendering.</returns>
        IOverlayRenderable GetRenderable(TEntity entity, TPreviewData previewData);

        /// <summary>
        /// Gets the object type tag for categorizing previews.
        /// Examples: "TrackNode", "Building", "BezierSpan".
        /// </summary>
        /// <param name="entity">The entity being previewed.</param>
        /// <returns>A string tag for this object type.</returns>
        string GetObjectType(TEntity entity);

        /// <summary>
        /// Gets an optional tint color for the preview.
        /// Return null to use the default white color.
        /// </summary>
        /// <param name="entity">The original entity.</param>
        /// <param name="previewData">The preview/pending-edit data.</param>
        /// <returns>A Color for tinting, or null for no tint.</returns>
        Color? GetPreviewTint(TEntity entity, TPreviewData previewData);

        /// <summary>
        /// Gets the selectable areas for this entity's preview.
        /// Return null or empty array if the preview has no interactive areas.
        /// </summary>
        /// <param name="entity">The original entity.</param>
        /// <param name="previewData">The preview/pending-edit data.</param>
        /// <param name="previewPosition">The preview's current position.</param>
        /// <param name="previewRotation">The preview's current rotation.</param>
        /// <param name="previewScale">The preview's current scale.</param>
        /// <returns>Array of selectable areas, or null if not selectable.</returns>
        OverlaySelectionArea[] GetSelectionAreas(
            TEntity entity,
            TPreviewData previewData,
            Vector3 previewPosition,
            Quaternion previewRotation,
            Vector3 previewScale);

        /// <summary>
        /// Called when a selection area of this entity's preview is clicked.
        /// This callback handles registering the selected object/element in the editor's selection system.
        /// </summary>
        /// <param name="entity">The original entity whose preview was clicked.</param>
        /// <param name="previewData">The preview/pending-edit data.</param>
        /// <param name="selectionArea">The specific area that was clicked.</param>
        void OnPreviewSelected(TEntity entity, TPreviewData previewData, OverlaySelectionArea selectionArea);
    }
}
