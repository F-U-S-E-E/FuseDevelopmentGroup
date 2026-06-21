using Helpers;
using UnityEngine;

namespace FUSE.Editor.Overlays
{
    /// <summary>
    /// Represents the preview state of an object with uncommitted edits.
    /// Holds the original object, the preview/pending-edit data, and rendering parameters.
    /// 
    /// Supports dual-parameter pattern:
    /// - OriginalObject: The actual game object (e.g., TrackNode)
    /// - FuseData: The pending edit data (e.g., FuseNode)
    /// </summary>
    public class OverlayPreviewData
    {
        /// <summary>
        /// The original game object being edited. Not modified.
        /// </summary>
        public GameObject OriginalObject { get; }

        /// <summary>
        /// The preview/pending-edit data (e.g., FuseNode for TrackNode edits).
        /// Contains the new transform values and other pending changes.
        /// </summary>
        public object FuseData { get; set; }

        /// <summary>
        /// The preview position (from pending edits). May differ from original position.
        /// </summary>
        public Vector3 PreviewPosition { get; set; }

        /// <summary>
        /// The preview rotation (from pending edits). May differ from original rotation.
        /// </summary>
        public Quaternion PreviewRotation { get; set; }

        /// <summary>
        /// The preview scale (from pending edits). May differ from original scale.
        /// </summary>
        public Vector3 PreviewScale { get; set; }

        /// <summary>
        /// Optional renderable interface for the object. If null, standard mesh/material lookup is used.
        /// </summary>
        public IOverlayRenderable Renderable { get; set; }

        /// <summary>
        /// Unique identifier for this preview (e.g., node ID, building ID).
        /// Used for tracking and deduplication.
        /// </summary>
        public string PreviewId { get; }

        /// <summary>
        /// User-defined tag for categorizing previews (e.g., "TrackNode", "Building", "BezierPoint").
        /// Useful for filtering and styling.
        /// </summary>
        public string ObjectType { get; set; }

        /// <summary>
        /// Whether this preview is currently visible.
        /// </summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// Optional tint color applied to the wireframe/ghost rendering.
        /// </summary>
        public Color? Tint { get; set; }

        /// <summary>
        /// Selectable areas for this preview (for mouse interaction and selection).
        /// </summary>
        public OverlaySelectionArea[] SelectionAreas { get; set; }

        /// <summary>
        /// Whether this preview is currently selected in the editor.
        /// </summary>
        public bool IsSelected { get; set; }

        /// <summary>
        /// The original entity object associated with this preview (for handler callbacks).
        /// Stored to pass back to handler during selection.
        /// </summary>
        public object Entity { get; set; }

        /// <summary>
        /// Creates a new preview data instance.
        /// </summary>
        /// <param name="originalObject">The original game object being edited (not modified).</param>
        /// <param name="fuseData">The preview/pending-edit data (e.g., FuseNode).</param>
        /// <param name="previewId">Unique identifier for this preview.</param>
        public OverlayPreviewData(
            GameObject originalObject,
            object fuseData,
            string previewId)
        {
            OriginalObject = originalObject;
            PreviewId = previewId;
            FuseData = fuseData;
            PreviewPosition = Vector3.zero;
            PreviewRotation = Quaternion.identity;
            PreviewScale = Vector3.one;
        }

        /// <summary>
        /// Updates all preview transform values at once.
        /// </summary>
        public void UpdatePreviewTransform(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            PreviewPosition = position;
            PreviewRotation = rotation;
            PreviewScale = scale;
        }

        /// <summary>
        /// Gets the world matrix for the preview transform.
        /// </summary>
        public Matrix4x4 GetPreviewMatrix()
        {
            return Matrix4x4.TRS(WorldTransformer.GameToWorld(PreviewPosition), PreviewRotation, PreviewScale);
        }

        /// <summary>
        /// Gets the world matrix for the original object's transform.
        /// </summary>
        public Matrix4x4 GetOriginalMatrix()
        {
            if (OriginalObject == null)
                return Matrix4x4.identity;

            var t = OriginalObject.transform;
            return Matrix4x4.TRS(t.position, t.rotation, t.lossyScale);
        }
    }
}

