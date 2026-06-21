# Overlay Selection System

## Overview

The overlay selection system enables editor interactions with overlay previews. When users click on a preview's selection area, the system performs a raycast, identifies the hit area, and invokes the entity handler's selection callback.

This architecture keeps selection logic handler-specific: the overlay infrastructure handles raycasting and dispatch, while each entity handler defines what "selection" means for that entity type.

## Architecture

### Components

1. **OverlaySelectionSystem**
   - Manages raycasting against all active preview selection areas
   - Provides hover state tracking for UI feedback
   - Emits `OnPreviewSelectionChanged`, `OnPreviewHovered`, `OnPreviewUnhovered` events
   - Requires a camera for raycasting (set via `SetCamera()`)

2. **OverlaySelectionArea**
   - Represents a clickable region for a preview
   - Contains bounds, raycast mesh, transform, and selection metadata
   - Implements `Raycast(Ray, out distance)` for hit detection
   - Stores `SelectionData` for handler-specific context

3. **IOverlayHandler<T> Extensions**
   - `GetSelectionAreas(T entity, Vector3 position, Quaternion rotation, Vector3 scale)`: Entity handlers return their selection areas
   - `OnPreviewSelected(T entity, OverlaySelectionArea selectionArea)`: Handler callback when a selection area is clicked

4. **FuseOverlayManager Public API**
   - `SetSelectionCamera(Camera camera)`: Configure the camera for raycasting
   - `TrySelectPreviewAtMouse(Vector2 mousePosition)`: Perform selection from mouse coordinates
   - `SelectionSystem` property: Direct access to the selection system for advanced usage

## Usage

### Basic Integration

```csharp
// In your editor tool/window OnGUI
void OnSceneGUI(SceneView sceneView)
{
    if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
    {
        // Try to select a preview at the mouse position
        if (FuseOverlayManager.Instance.TrySelectPreviewAtMouse(Event.current.mousePosition))
        {
            Event.current.Use(); // Consume the event
        }
    }
}
```

### Entity Handler Implementation

Implement selection area generation in your handler:

```csharp
public class MyEntityOverlayHandler : IOverlayHandler<MyEntity>
{
    public OverlaySelectionArea[] GetSelectionAreas(
        MyEntity entity,
        Vector3 previewPosition,
        Quaternion previewRotation,
        Vector3 previewScale)
    {
        // Define one or more clickable areas around the preview
        var area = new OverlaySelectionArea
        {
            AreaId = $"entity_{entity.GetInstanceID()}",
            PreviewId = GetEntityId(entity),
            Bounds = new Bounds(Vector3.zero, Vector3.one * 2f), // 2m diameter sphere
            Transform = Matrix4x4.TRS(previewPosition, previewRotation, previewScale),
            IsSelectable = true,
            SelectionData = entity,
            HighlightColor = Color.cyan
        };

        return new[] { area };
    }

    public void OnPreviewSelected(MyEntity entity, OverlaySelectionArea selectionArea)
    {
        // Handle selection: register in editor, highlight, etc.
        FuseLog.Info($"Selected {entity.Name} (area: {selectionArea.AreaId})");
        // Update your selection state, UI, inspector, etc.
    }
}
```

### Multiple Selection Areas

If an entity preview should have multiple clickable regions (e.g., control points on a Bezier curve):

```csharp
public OverlaySelectionArea[] GetSelectionAreas(
    MyEntity entity,
    Vector3 previewPosition,
    Quaternion previewRotation,
    Vector3 previewScale)
{
    var areas = new List<OverlaySelectionArea>();

    // Control point 1
    areas.Add(new OverlaySelectionArea
    {
        AreaId = "control_point_0",
        PreviewId = GetEntityId(entity),
        Bounds = new Bounds(entity.ControlPoint0 - previewPosition, Vector3.one * 0.5f),
        Transform = Matrix4x4.TRS(previewPosition, previewRotation, previewScale),
        IsSelectable = true,
        SelectionData = new { ControlPointIndex = 0 },
        HighlightColor = Color.red
    });

    // Control point 2
    areas.Add(new OverlaySelectionArea
    {
        AreaId = "control_point_1",
        PreviewId = GetEntityId(entity),
        Bounds = new Bounds(entity.ControlPoint1 - previewPosition, Vector3.one * 0.5f),
        Transform = Matrix4x4.TRS(previewPosition, previewRotation, previewScale),
        IsSelectable = true,
        SelectionData = new { ControlPointIndex = 1 },
        HighlightColor = Color.green
    });

    return areas.ToArray();
}

public void OnPreviewSelected(MyEntity entity, OverlaySelectionArea selectionArea)
{
    var data = selectionArea.SelectionData as dynamic;
    int controlPointIndex = data.ControlPointIndex;
    FuseLog.Info($"Selected control point {controlPointIndex}");
}
```

## Selection Flow Diagram

```
User clicks on preview
         ↓
Input handler calls TrySelectPreviewAtMouse(mousePos)
         ↓
OverlaySelectionSystem.TrySelect() performs raycast
         ↓
Ray tested against all active previews' selection areas
         ↓
Closest hit distance determines selected area
         ↓
FuseOverlayManager.InvokeSelectionCallback() dispatches to handler
         ↓
Appropriate IOverlayHandler<T>.OnPreviewSelected() invoked
         ↓
Handler performs editor-specific selection registration
```

## Event System

The selection system emits events for UI feedback:

```csharp
// Subscribe to selection changes
FuseOverlayManager.Instance.SelectionSystem.OnPreviewSelectionChanged += 
    (previewId, area) =>
    {
        Debug.Log($"Preview {previewId} selected at area {area.AreaId}");
    };

// Subscribe to hover changes
FuseOverlayManager.Instance.SelectionSystem.OnPreviewHovered += 
    (previewId, area) =>
    {
        // Update cursor, highlight UI, etc.
        Debug.Log($"Hovering over {area.AreaId}");
    };

FuseOverlayManager.Instance.SelectionSystem.OnPreviewUnhovered += 
    () =>
    {
        // Reset hover state
        Debug.Log("No longer hovering");
    };
```

## Performance Considerations

- **Raycasting**: Each frame's mouse move can trigger a raycast. The selection system is optimized to test only visible previews with active selection areas.
- **Selection Area Count**: Prefer fewer, larger selection areas over many small ones. Each preview can have multiple areas, but excessive numbers will impact performance.
- **Event Callbacks**: Keep `OnPreviewSelected` and event subscribers lightweight; they may be called frequently during editor interactions.

## Example: TrackNode Selection

The built-in `TrackNodeOverlayHandler` demonstrates selection integration:

```csharp
public OverlaySelectionArea[] GetSelectionAreas(
    TrackNode entity,
    Vector3 previewPosition,
    Quaternion previewRotation,
    Vector3 previewScale)
{
    var area = new OverlaySelectionArea
    {
        AreaId = $"node_{entity.GetInstanceID()}",
        PreviewId = GetEntityId(entity),
        Bounds = new Bounds(Vector3.zero, Vector3.one * 2f),
        Transform = Matrix4x4.TRS(previewPosition, previewRotation, previewScale),
        IsSelectable = true,
        SelectionData = entity
    };

    return new[] { area };
}

public void OnPreviewSelected(TrackNode entity, OverlaySelectionArea selectionArea)
{
    FuseLog.Info($"TrackNode preview selected: {entity.GetInstanceID()}");
    // Integration layer will register the selection in the editor
}
```

## Troubleshooting

### Selection not working

1. **No camera set**: Call `SetSelectionCamera()` with the scene camera before attempting selection.
2. **Preview not visible**: Ensure `OverlayPreviewData.IsVisible` is `true`.
3. **No selection areas**: Verify handler's `GetSelectionAreas()` returns non-empty array.
4. **Selection area outside bounds**: Ensure `OverlaySelectionArea.Bounds` and `Transform` correctly represent the clickable region.

### Performance degradation

1. **Too many selection areas**: Reduce the number of areas per preview or consolidate small areas into one.
2. **Large bounds**: Use reasonably sized selection area bounds to avoid excessive raycast hits.
3. **Event subscribers**: Check that event callback subscriptions are being cleaned up.

## Integration Notes

- Selection is separate from rendering; overlays render whether or not they have selection areas.
- The selection system is decoupled from the gizmo system; both can coexist.
- Handler selection callbacks should avoid heavy operations; defer complex logic to a later update phase if needed.
- Selection registration (e.g., marking an object as "selected" in the editor) is handler-specific and not managed by the overlay system.
