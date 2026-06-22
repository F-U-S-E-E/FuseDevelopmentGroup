using FUSE.Authoring.Data;
using FUSE.Editor.Overlays;
using FUSE.Infrastructure;
using Helpers;
using System.Collections.Generic;
using Track;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FUSE.Editor.Track.Overlays
{
    /// <summary>
    /// Handler that converts TrackSegment entities into overlay preview data.
    /// Works with TrackSegment as the entity and FuseSegment as the preview data.
    /// This handler encapsulates all TrackSegment-specific logic, allowing the overlay system
    /// to work with any entity type without code duplication.
    /// </summary>
    public class TrackSegmentOverlayHandler : IOverlayHandler<TrackSegment, FuseSegment>
    {
        public string HandlerName => "TrackSegment";

        private Dictionary<string, TrackSegmentOverlayAdapter> _renderableCache = new Dictionary<string, TrackSegmentOverlayAdapter>();

        public bool CanHandle(TrackSegment entity)
        {
            // Validate the entity is in a valid state
            return entity != null;
        }

        public string GetEntityId(TrackSegment entity)
        {
            // Use the segment's instance ID for uniqueness
            return entity.id;
        }

        public GameObject GetTargetGameObject(TrackSegment entity)
        {
            // Return the TrackSegment's GameObject
            return entity.a.gameObject;
        }

        public void ExtractPreviewTransform(
            TrackSegment entity,
            FuseSegment previewData,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            position = entity.a.transform.localPosition;

            rotation = Quaternion.identity;

            scale = Vector3.one;
        }

        public IOverlayRenderable GetRenderable(TrackSegment entity, FuseSegment previewData)
        {
            // Use the TrackSegmentOverlayAdapter for custom rendering
            // This provides a tube/cylinder mesh representation of the track segment
            if (!_renderableCache.TryGetValue(entity.id, out var renderable))
            {
                renderable = new TrackSegmentOverlayAdapter();
                _renderableCache[entity.id] = renderable;
            }
            return renderable;
        }

        public string GetObjectType(TrackSegment entity) => "TrackSegment";

        public Color? GetPreviewTint(TrackSegment entity, FuseSegment previewData)
        {
            // Optional: Use different colors for different segment styles or track classes
            // Return null to use the default white color
            return null;
        }

        public OverlaySelectionArea[] GetSelectionAreas(
            TrackSegment entity,
            FuseSegment previewData,
            Vector3 previewPosition,
            Quaternion previewRotation,
            Vector3 previewScale)
        {
            // Create a selectable cylinder/tube along the segment length

            // Create a cylinder bounds centered on the segment

            TrackSegmentOverlayAdapter renderable = GetRenderable(entity, previewData) as TrackSegmentOverlayAdapter;
            var midpoint = renderable.Curve.GetPoint(0.5f);
            var segmentRotation = renderable.Curve.GetRotation(0.5f);

            var area1 = new OverlaySelectionArea
            {
                AreaId = $"segment_{entity.GetInstanceID()}",
                PreviewId = GetEntityId(entity),
                Bounds = new Bounds(Vector3.zero, Vector3.one * 0.75f),
                Transform = Matrix4x4.TRS(WorldTransformer.GameToWorld(midpoint), segmentRotation, previewScale),
                IsSelectable = true,
                SelectionData = entity
            };

            return new[] { area1 };
        }

        public void OnPreviewSelected(TrackSegment entity, FuseSegment previewData, OverlaySelectionArea selectionArea)
        {
            // This callback is invoked when a user clicks on this preview's selection area
            // Perform editor-specific action: select the track segment in the editor
            FuseLog.Info($"TrackSegment preview selected: {entity.GetInstanceID()}");

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
