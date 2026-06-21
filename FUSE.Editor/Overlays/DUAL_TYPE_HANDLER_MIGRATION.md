# Dual-Type Handler Migration Guide

## Overview

The overlay handler system has been refactored to support **dual-type handlers** where the overlayed entity and preview/pending-edit data are **separate types**.

### Previous Pattern (Single-Parameter)
```csharp
// Old: Entity type contains everything
IOverlayHandler<TrackNode>
{
    ExtractPreviewTransform(TrackNode entity, ...)
}
```

### New Pattern (Dual-Parameter)
```csharp
// New: Entity and preview data are separate
IOverlayHandler<TrackNode, FuseNode>
{
    ExtractPreviewTransform(TrackNode entity, FuseNode previewData, ...)
}
```

## Key Changes

### 1. OverlayPreviewData Constructor

**Old:**
```csharp
var preview = new OverlayPreviewData(
    gameObject, 
    previewId, 
    position, 
    rotation, 
    scale);
```

**New:**
```csharp
var preview = new OverlayPreviewData(
    gameObject,           // Original object
    previewData,          // FuseNode or similar
    previewId);           // Unique ID

// Set transforms separately:
preview.PreviewPosition = position;
preview.PreviewRotation = rotation;
preview.PreviewScale = scale;
```

### 2. OverlayPreviewData Properties

| Old | New | Purpose |
|-----|-----|---------|
| `ObjectId` | `PreviewId` | Unique identifier for the preview |
| N/A | `FuseData` | The preview/pending-edit data object |
| N/A | `PreviewPosition/Rotation/Scale` | Transform values for the preview |
| N/A | `GetPreviewMatrix()` | Helper to get world matrix of preview |
| N/A | `GetOriginalMatrix()` | Helper to get world matrix of original |

### 3. Handler Interface Updates

All handler methods that previously took only the entity now can take both entity and preview data:

```csharp
// Old handler methods
public interface IOverlayHandler<T>
{
    void ExtractPreviewTransform(T entity, out Vector3 pos, ...);
    IOverlayRenderable GetRenderable(T entity);
    Color? GetPreviewTint(T entity);
    OverlaySelectionArea[] GetSelectionAreas(T entity, ...);
}

// New handler methods
public interface IOverlayHandler<TEntity, TPreviewData>
{
    void ExtractPreviewTransform(TEntity entity, TPreviewData previewData, out Vector3 pos, ...);
    IOverlayRenderable GetRenderable(TEntity entity, TPreviewData previewData);
    Color? GetPreviewTint(TEntity entity, TPreviewData previewData);
    OverlaySelectionArea[] GetSelectionAreas(TEntity entity, TPreviewData previewData, ...);
}
```

### 4. Registering a Dual-Type Handler

**Old pattern:**
```csharp
FuseOverlayManager.Instance.HandlerRegistry.RegisterHandler(
    new TrackNodeOverlayHandler());
```

**New pattern (still available for backward compatibility):**
```csharp
// Old single-type API still works if your handler doesn't separate data
var registry = FuseOverlayManager.Instance.HandlerRegistry;
registry.RegisterHandler(handler); // IOverlayHandler<TrackNode>
```

**New dual-type pattern:**
```csharp
// Use the new registry for dual-type handlers
var registry2 = FuseOverlayManager.Instance.HandlerRegistry2;
registry2.RegisterHandler<TrackNode, FuseNode>(handler);
// IOverlayHandler<TrackNode, FuseNode>
```

### 5. Creating a Preview with Dual-Type Data

**Old approach:**
```csharp
var preview = _overlayManager.ApplyPreview(trackNode);
```

**New approach (with separate preview data):**
```csharp
// Create the preview/pending-edit object
var fuseNode = new FuseNode
{
    Position = new FuseVector3(...),
    Rotation = new FuseVector3(...),
};

// Register with both the entity and preview data
var preview = _overlayManager.RegisterPreview(
    objectId: trackNode.id,
    originalObject: trackNode.gameObject,
    fuseData: fuseNode,
    renderable: adapter);
```

## Migration Checklist

- [ ] Create your preview/pending-edit data type (e.g., `FuseNode`)
- [ ] Update handler to implement `IOverlayHandler<TEntity, TPreviewData>`
- [ ] Update all handler methods to accept both entity and preview data parameters
- [ ] Update preview extraction logic to read from preview data instead of entity
- [ ] Register the new dual-type handler
- [ ] Update all preview creation calls to pass the preview data object
- [ ] Update any selection callbacks to handle both entity and preview data
- [ ] Test that renders and selection work correctly

## Example: TrackNode Handler Migration

### Before
```csharp
public class TrackNodeOverlayHandler : IOverlayHandler<TrackNode>
{
    public void ExtractPreviewTransform(TrackNode entity, out Vector3 position, out Quaternion rotation, out Vector3 scale)
    {
        position = entity.transform.position;
        rotation = entity.transform.rotation;
        scale = Vector3.one;
    }

    public Color? GetPreviewTint(TrackNode entity)
    {
        return Color.yellow;
    }
}
```

### After
```csharp
public class TrackNodeOverlayHandler : IOverlayHandler<TrackNode, FuseNode>
{
    public void ExtractPreviewTransform(TrackNode entity, FuseNode previewData, out Vector3 position, out Quaternion rotation, out Vector3 scale)
    {
        // Extract from preview data, not the entity
        position = previewData.Position.ToVector3();
        rotation = Quaternion.Euler(previewData.Rotation.ToVector3());
        scale = Vector3.one;
    }

    public Color? GetPreviewTint(TrackNode entity, FuseNode previewData)
    {
        // Can use both entity and preview data for color logic
        return Color.yellow;
    }
}
```

## Backward Compatibility

The old single-parameter handler interface (`IOverlayHandler<T>`) is still available and functional. The system supports:

1. **Old registry**: `OverlayHandlerRegistry` (single-parameter handlers)
2. **New registry**: `OverlayHandlerRegistry2` (dual-parameter handlers)

Both can coexist, though mixing them in the same overlay session may cause confusion in handler dispatch.

## When to Use Dual-Type Handlers

Use dual-type handlers when:
- You have edit/pending-edit state in a separate object (FuseNode, FuseBuilding, etc.)
- Overlays need to visualize both the original and the pending state
- Different rendering/selection behavior depending on pending edits
- You want clear separation between the source object and preview state

Use single-type handlers when:
- The entity contains all necessary preview information
- You're not separating edit state into a different object
- You need simpler, more straightforward handler logic

## Files Modified

- `FUSE.Editor/Overlays/IOverlayHandler2.cs` - New dual-type handler interface
- `FUSE.Editor/Overlays/OverlayHandlerRegistry2.cs` - New dual-type registry
- `FUSE.Editor/Overlays/OverlayPreviewData.cs` - Updated to store both entity and preview data
- `FUSE.Editor/Overlays/FuseOverlayRenderer.cs` - Updated RegisterPreview signature
- `FUSE.Editor/Overlays/FuseOverlayManager.cs` - Updated RegisterPreview signature
- `FUSE.Editor/Track/Overlays/TrackNodeOverlayExample.cs` - Updated to use FuseNode
- `FUSE.Editor/Track/Overlays/TrackNodeGizmoOverlayIntegration.cs` - Updated to use FuseNode

## Troubleshooting

### "Handler not found for type X"
- Make sure you registered the handler with the correct registry and type parameters
- `HandlerRegistry` for old single-type handlers
- `HandlerRegistry2` for new dual-type handlers

### "PreviewId is null"
- Your handler's `GetEntityId()` is returning null or empty string
- Ensure the preview data is being instantiated correctly

### Selection callbacks not firing
- Verify `OnPreviewSelected()` is passing all required parameters
- Check that selection areas are being returned by `GetSelectionAreas()`

## Next Steps

1. Migrate your existing handlers to dual-type versions
2. Update handler registration code
3. Create preview/pending-edit data objects (FuseNode, FuseBuilding, etc.)
4. Update preview creation and update logic
5. Test selection and rendering with the new pattern
