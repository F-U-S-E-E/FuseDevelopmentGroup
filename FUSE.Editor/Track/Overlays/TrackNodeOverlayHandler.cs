using FUSE.Authoring.Data;
using FUSE.Editor.Overlays;
using FUSE.Infrastructure;
using Helpers;
using Track;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FUSE.Editor.Track.Overlays
{
    /// <summary>
    /// Handler that converts TrackNode entities into overlay preview data.
    /// Works with TrackNode as the entity and generic object as the preview data.
    /// This handler encapsulates all TrackNode-specific logic, allowing the overlay system
    /// to work with any entity type without code duplication.
    /// </summary>
    public class TrackNodeOverlayHandler : IOverlayHandler<TrackNode, FuseNode>
    {
        public string HandlerName => "TrackNode";

        public bool CanHandle(TrackNode entity)
        {
            // Validate the entity is in a valid state
            return entity != null && entity.transform != null;
        }

        public string GetEntityId(TrackNode entity)
        {
            // Use the node's instance ID for uniqueness
            return entity.id;
        }

        public GameObject GetTargetGameObject(TrackNode entity)
        {
            // Return the TrackNode's GameObject
            return entity.gameObject;
        }

        public void ExtractPreviewTransform(
            TrackNode entity,
            FuseNode previewData,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            // If no preview data is provided, use the current entity transform
            // This is a fallback for cases where preview data is not available
            if (previewData == null)
            {
                var transform = entity.transform;
                position = transform.position;
                rotation = transform.rotation;
                scale = transform.lossyScale;
            }
            else
            {
                // For TrackNode, preview data would be a FuseNode or similar object
                // This assumes the preview data has Position, Rotation, Scale properties
                // For now, use a reflection-based approach or a cast if the type is known
                position = previewData.Position;
                rotation = Quaternion.Euler(previewData.Rotation);
                scale = Vector3.one;
            }
        }

        public IOverlayRenderable GetRenderable(TrackNode entity, FuseNode previewData)
        {
            // Use the TrackNodeOverlayAdapter for custom rendering
            // This provides a sphere mesh representation of the track node
            return new TrackNodeOverlayAdapter();
        }

        public string GetObjectType(TrackNode entity) => "TrackNode";

        public Color? GetPreviewTint(TrackNode entity, FuseNode previewData)
        {
            // Optional: Use different colors for different node types
            // Return null to use the default white color
            return null;
        }

        public OverlaySelectionArea[] GetSelectionAreas(
            TrackNode entity,
            FuseNode previewData,
            Vector3 previewPosition,
            Quaternion previewRotation,
            Vector3 previewScale)
        {
            // Create a single selectable sphere around the preview position
            var area = new OverlaySelectionArea
            {
                AreaId = $"node_{entity.GetInstanceID()}",
                PreviewId = GetEntityId(entity),
                Bounds = new Bounds(Vector3.zero, Vector3.one * 1f), // 1m radius sphere
                Transform = Matrix4x4.TRS(WorldTransformer.GameToWorld(previewPosition), previewRotation, previewScale),
                IsSelectable = true,
                SelectionData = entity
            };

            return new[] { area };
        }

        public void OnPreviewSelected(TrackNode entity, FuseNode previewData, OverlaySelectionArea selectionArea)
        {
            // This callback is invoked when a user clicks on this preview's selection area
            // Perform editor-specific action: select the track node in the editor
            // The actual selection registration is handled by the integration layer
            FuseLog.Info($"TrackNode preview selected: {entity.GetInstanceID()}");

            if (Keyboard.current.shiftKey.isPressed)
            {
                FuseEditor.Instance.EntitySelection.ToggleSelection(previewData, entity.id);
            }
            else
            {
                FuseEditor.Instance.EntitySelection.ClearSelection();
                FuseEditor.Instance.EntitySelection.AddToSelection(previewData, entity.id);
            }
        }
    }
}
