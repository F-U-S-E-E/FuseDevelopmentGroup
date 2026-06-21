# Overlay Selection System - Implementation Summary

## What Was Added

A complete interactive selection system for overlay previews has been implemented. Users can now click on overlay previews to trigger handler-defined selection callbacks, enabling seamless editor integration.

## New Files Created

### Core Selection Infrastructure

1. **OverlaySelectionSystem.cs**
   - Manages raycasting against preview selection areas
   - Tracks hover state for UI feedback
   - Emits selection/hover events
   - Requires a camera for raycasting

2. **OverlaySelectionArea.cs** (Previously created)
   - Represents a clickable region for a preview
   - Contains bounds, transform, and raycast mesh
   - Stores selection metadata and highlight color
   - Implements `Raycast()` for hit detection

3. **SELECTION_SYSTEM.md**
   - Comprehensive guide to the selection feature
   - Usage patterns and best practices
   - Performance considerations
   - Troubleshooting guide

## Modified Files

### Interface Extensions

**IOverlayHandler.cs**
- Added `GetSelectionAreas(T entity, Vector3 position, Quaternion rotation, Vector3 scale)`: Returns clickable regions for an entity
- Added `OnPreviewSelected(T entity, OverlaySelectionArea selectionArea)`: Callback when a selection area is clicked

**OverlayPreviewData.cs**
- Added `SelectionAreas`: Stores selection areas for the preview
- Added `IsSelected`: Tracks selection state
- Added `Entity`: Stores the original entity for handler callbacks

### Core Renderer/Manager

**FuseOverlayRenderer.cs**
- Added `_selectionSystem` field and property
- Added `SetSelectionCamera(Camera camera)` method
- Integrated selection system into constructor

**FuseOverlayManager.cs**
- Added `SelectionSystem` property
- Added `SetSelectionCamera(Camera camera)` method
- Added `TrySelectPreviewAtMouse(Vector2 mousePosition)` method
- Added `InvokeSelectionCallback()` dispatcher

**OverlayHandlerRegistry.cs**
- Updated `ApplyPreview<T>()` to populate `SelectionAreas` and `Entity` on preview data
- Added `InvokeSelectionCallback<T>(T entity, OverlaySelectionArea selectionArea)` generic dispatcher

### Entity Handler Implementation

**TrackNodeOverlayHandler.cs**
- Implemented `GetSelectionAreas()`: Creates a 2m radius sphere selection area
- Implemented `OnPreviewSelected()`: Handles selection callback (currently logs selection)

### Documentation

- Updated **README.md** with selection feature overview
- Updated **QUICK_REFERENCE.md** with handler-based and selection examples
- Created **SELECTION_SYSTEM.md** as comprehensive selection guide

## Architecture Flow

```
User clicks on preview
        ↓
Input handler calls TrySelectPreviewAtMouse(mousePos)
        ↓
OverlaySelectionSystem.TrySelect() performs raycast
        ↓
Ray tested against all visible previews' selection areas
        ↓
Closest hit determines selected area
        ↓
FuseOverlayManager.InvokeSelectionCallback() dispatcher
        ↓
Appropriate IOverlayHandler<T>.OnPreviewSelected() invoked
        ↓
Handler performs editor-specific selection action
```

## Key Design Decisions

1. **Handler-Owned Selection Logic**: Each entity handler defines what "selection" means. The overlay system just handles raycasting and dispatch.

2. **Generic Dispatch**: Uses `OverlayHandlerRegistry.InvokeSelectionCallback<T>()` to avoid reflection overhead in the hot path.

3. **Separate System**: `OverlaySelectionSystem` is independent from rendering, allowing both to evolve separately.

4. **Event-Based Feedback**: Emits hover/selection events so UI layers can provide visual feedback.

5. **Entity Reference Storage**: `OverlayPreviewData` stores the original entity, enabling handler invocation without ID duplication.

## Usage Example

### Setup
```csharp
// Register handler
var handler = new MyEntityHandler();
FuseOverlayManager.Instance.HandlerRegistry.RegisterHandler<MyEntity>(handler);

// Set selection camera
FuseOverlayManager.Instance.SetSelectionCamera(SceneView.lastActiveSceneView.camera);
```

### Create Preview
```csharp
var entity = /* your entity */;
var preview = FuseOverlayManager.Instance.ApplyPreview(entity);
```

### Handle Clicks
```csharp
if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
{
    if (FuseOverlayManager.Instance.TrySelectPreviewAtMouse(Event.current.mousePosition))
    {
        Event.current.Use();
    }
}
```

### Handler Implementation
```csharp
public class MyEntityHandler : IOverlayHandler<MyEntity>
{
    public OverlaySelectionArea[] GetSelectionAreas(
        MyEntity entity, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        return new[] { new OverlaySelectionArea
        {
            AreaId = "main",
            PreviewId = GetEntityId(entity),
            Bounds = new Bounds(Vector3.zero, Vector3.one * 2f),
            Transform = Matrix4x4.TRS(pos, rot, scale),
            IsSelectable = true,
            SelectionData = entity
        }};
    }

    public void OnPreviewSelected(MyEntity entity, OverlaySelectionArea area)
    {
        // Do something with the selection
        FuseLog.Info($"Selected entity");
    }
}
```

## Integration Points

1. **Input Handler**: Call `TrySelectPreviewAtMouse()` from your OnGUI/input code
2. **Handler Callback**: Implement `OnPreviewSelected()` to handle selection semantics
3. **Event Subscription**: Subscribe to `SelectionSystem.OnPreviewHovered` for UI feedback
4. **Camera Setup**: Call `SetSelectionCamera()` once with your scene camera

## Benefits

✅ **Clean Separation**: Selection logic stays with entity handlers  
✅ **Editor Integration**: Handler callbacks can register objects, update UI, etc.  
✅ **Hover Feedback**: Events enable visual feedback during hovering  
✅ **Multiple Areas**: Support for multi-part selections (control points, etc.)  
✅ **Type-Safe**: Generic architecture eliminates casting  
✅ **Performance**: Efficient raycasting with early exit on closest hit  

## Next Steps for Integration

1. **Add Editor Window Integration**: Wire clicks from your editor windows to `TrySelectPreviewAtMouse()`
2. **Implement Handler Callbacks**: Populate `OnPreviewSelected()` to register in your selection system
3. **Visual Feedback**: Subscribe to hover events to highlight areas or change cursor
4. **Advanced Areas**: Define multiple selection areas for complex entities (Bezier control points, etc.)

## Build Status

✅ **Build: SUCCESSFUL**

All changes compile cleanly. The selection system is ready for integration into editor tools.
