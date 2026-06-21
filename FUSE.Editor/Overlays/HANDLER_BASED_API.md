# Handler-Based Overlay API Guide

## Overview

The overlay system now uses a **generic handler-based architecture** that eliminates type-specific code. Application-layer code no longer needs to know about TrackNode, Building, BezierSpan, etc. — handlers encapsulate all the conversion logic.

### Key Concepts

- **Handler**: An `IOverlayHandler<T>` implementation that converts entity type `T` into overlay preview data.
- **Generic API**: A single `ApplyPreview<T>(entity)` method replaces all type-specific calls.
- **Handler Registry**: Centralized registry (`OverlayHandlerRegistry`) that manages handlers and provides uniform preview application.

---

## Architecture Diagram

```
Entity (TrackNode, Building, BezierSpan, ...)
    ↓
Application Layer: ApplyPreview<T>(entity)
    ↓
OverlayHandlerRegistry: Lookup handler for T
    ↓
IOverlayHandler<T>: Extract all preview data
    ↓
OverlayPreviewData: Unified preview representation
    ↓
FuseOverlayRenderer: Register & render
    ↓
Screen: Preview displayed
```

---

## Creating a Handler

### 1. Implement `IOverlayHandler<T>`

For each entity type, create a handler that implements:

```csharp
using FUSE.Editor.Overlays;
using UnityEngine;
using Fuse.Core.Model; // Example entity type

public class TrackNodeOverlayHandler : IOverlayHandler<TrackNode>
{
    public string HandlerName => "TrackNode";

    public bool CanHandle(TrackNode entity)
    {
        // Validate the entity is in a valid state
        return entity != null && entity.Position != null;
    }

    public string GetEntityId(TrackNode entity)
    {
        // Return a unique ID (e.g., from your data model)
        return $"track_node_{entity.Id}";
    }

    public GameObject GetTargetGameObject(TrackNode entity)
    {
        // Return the GameObject this preview attaches to
        // Often a GameObject in your scene hierarchy
        return GetNodeGameObject(entity.Id);
    }

    public void ExtractPreviewTransform(
        TrackNode entity,
        out Vector3 position,
        out Quaternion rotation,
        out Vector3 scale)
    {
        // Extract pending edit values
        position = new Vector3(entity.Position.x, entity.Position.y, entity.Position.z);
        rotation = Quaternion.Euler(
            entity.Rotation.x,
            entity.Rotation.y,
            entity.Rotation.z);
        scale = Vector3.one;
    }

    public IOverlayRenderable GetRenderable(TrackNode entity)
    {
        // Return custom rendering logic, or null for default
        return new TrackNodeOverlayAdapter(entity);
    }

    public string GetObjectType(TrackNode entity) => "TrackNode";

    public Color? GetPreviewTint(TrackNode entity)
    {
        // Optional: Color the preview based on entity state
        if (entity.IsSelected) return Color.green;
        if (entity.IsStationaryNode) return Color.blue;
        return null; // Use default white
    }
}
```

### 2. Register the Handler

In your editor initialization or setup code:

```csharp
// Get the overlay manager
var overlayMgr = FuseOverlayManager.Instance;

// Register handlers for your entity types
overlayMgr.HandlerRegistry.RegisterHandler(new TrackNodeOverlayHandler());
overlayMgr.HandlerRegistry.RegisterHandler(new BuildingOverlayHandler());
overlayMgr.HandlerRegistry.RegisterHandler(new BezierSpanOverlayHandler());
```

**Best Practice**: Register all handlers once during editor initialization (e.g., in an Editor window's OnEnable or a static initialization method).

---

## Using the Generic API

### Simple Preview Creation

```csharp
var trackNode = GetTrackNodeFromEditor();
var preview = FuseOverlayManager.Instance.ApplyPreview(trackNode);

if (preview != null)
{
    Debug.Log($"Preview created: {preview.ObjectId}");
}
```

### Get the Preview ID

```csharp
var previewData = FuseOverlayManager.Instance.ApplyPreview(entity, out var previewId);
Debug.Log($"Preview ID: {previewId}");
```

### Update a Preview

When the entity changes, update its preview:

```csharp
// Entity was modified (e.g., user dragged it in the gizmo)
entity.Position.x += 1.0f;

// Update the overlay preview
FuseOverlayManager.Instance.UpdatePreviewFromEntity(previewId, entity);
```

### Batch Operations

Create previews for multiple entities:

```csharp
var trackNodes = new[] { node1, node2, node3 };
var previews = FuseOverlayManager.Instance.GetRenderer()
    .ApplyPreviewBatch(trackNodes);

Debug.Log($"Created {previews.Count} previews");
```

---

## Complete Workflow Example

### Scenario: Edit a TrackNode with Preview

```csharp
using FUSE.Editor.Overlays;
using Fuse.Core.Model;
using UnityEngine;

public class TrackNodeEditTool
{
    private TrackNode _editingNode;
    private string _previewId;

    public void BeginEdit(TrackNode node)
    {
        _editingNode = node;

        // Create preview using generic handler-based API
        var preview = FuseOverlayManager.Instance.ApplyPreview(node, out _previewId);

        if (preview == null)
        {
            Debug.LogError("Failed to create overlay preview for node");
            return;
        }

        Debug.Log($"Editing node {_previewId}, preview active");
    }

    public void UpdateEdit(Vector3 newPosition)
    {
        if (_editingNode == null)
            return;

        // Apply changes to the entity
        _editingNode.Position.x = newPosition.x;
        _editingNode.Position.y = newPosition.y;
        _editingNode.Position.z = newPosition.z;

        // Update the overlay preview using generic API
        FuseOverlayManager.Instance.UpdatePreviewFromEntity(_previewId, _editingNode);
    }

    public void ApplyEdit()
    {
        if (_editingNode == null)
            return;

        // Apply changes to the actual scene/model
        SaveNodeChanges(_editingNode);

        // Remove the preview
        FuseOverlayManager.Instance.UnregisterPreview(_previewId);

        _editingNode = null;
        _previewId = null;
    }

    public void CancelEdit()
    {
        if (_editingNode == null)
            return;

        // Discard changes
        ReloadNodeState(_editingNode);

        // Remove the preview
        FuseOverlayManager.Instance.UnregisterPreview(_previewId);

        _editingNode = null;
        _previewId = null;
    }
}
```

---

## Handler Tips & Best Practices

### 1. ID Uniqueness
Ensure `GetEntityId()` always returns the same ID for the same entity across multiple calls.

```csharp
✅ Good
return $"track_node_{entity.Id}";

❌ Bad
return entity.ToString(); // May change every time
```

### 2. Null Safety
Always check for null inputs in handler methods:

```csharp
public bool CanHandle(TrackNode entity) => entity != null && entity.Position != null;
```

### 3. Error Handling
Handlers should gracefully fail and return safe defaults:

```csharp
public GameObject GetTargetGameObject(TrackNode entity)
{
    if (entity == null) return null;
    var obj = FindObjectWithTrackNodeId(entity.Id);
    if (obj == null)
        FuseLog.Warning($"Could not find GameObject for TrackNode {entity.Id}");
    return obj;
}
```

### 4. Custom Rendering
Implement `IOverlayRenderable` for complex visualization:

```csharp
public IOverlayRenderable GetRenderable(TrackNode entity)
{
    // Return custom adapter for advanced rendering
    return new TrackNodeRenderAdapter(entity);
}
```

Return `null` for default wireframe rendering.

### 5. Tinting Strategy
Use tint colors to convey state information:

```csharp
public Color? GetPreviewTint(TrackNode entity)
{
    return entity.IsModified ? Color.yellow : null;
}
```

---

## Comparison: Old vs New API

### Old Approach (Type-Specific)

```csharp
// Old: Caller must know about TrackNode specifics
var trackNode = GetTrackNode();
var preview = FuseOverlayManager.Instance.RegisterPreview(
    objectId: $"node_{trackNode.Id}",
    originalObject: trackNodeGameObject,
    previewPosition: new Vector3(trackNode.Position.x, trackNode.Position.y, trackNode.Position.z),
    previewRotation: Quaternion.Euler(trackNode.Rotation.x, trackNode.Rotation.y, trackNode.Rotation.z),
    previewScale: Vector3.one,
    renderable: new TrackNodeOverlayAdapter(trackNode)
);
```

### New Approach (Generic Handler-Based)

```csharp
// New: Handler encapsulates all TrackNode-specific logic
var trackNode = GetTrackNode();
var preview = FuseOverlayManager.Instance.ApplyPreview(trackNode);
```

**Advantages:**
- ✅ No type-specific code in application layer
- ✅ Consistent with other entity types
- ✅ Easy to add new entity types (just add a handler)
- ✅ Centralized entity conversion logic

---

## Error Handling

### Common Issues

**Issue**: "No handler registered for type 'TrackNode'"
```
Solution: Register the handler before calling ApplyPreview
```

**Issue**: "Handler returned null GameObject"
```
Solution: Ensure GetTargetGameObject() returns a valid GameObject
```

**Issue**: "Cannot apply preview for null entity"
```
Solution: Verify entity is not null before calling ApplyPreview
```

### Debugging

Enable detailed logging to see what handlers are doing:

```csharp
// Check if handler is registered
bool hasHandler = FuseOverlayManager.Instance.HandlerRegistry.HasHandler<TrackNode>();
Debug.Log($"TrackNode handler registered: {hasHandler}");

// Get handler count
int count = FuseOverlayManager.Instance.HandlerRegistry.GetHandlerCount();
Debug.Log($"Total handlers: {count}");

// Try to apply preview and check result
var preview = FuseOverlayManager.Instance.ApplyPreview(entity);
if (preview == null)
    Debug.LogError("Preview creation failed");
else
    Debug.Log($"Preview created: {preview.ObjectId}");
```

---

## Advanced: Custom Handler Registry

For specialized behavior, subclass or extend the registry:

```csharp
public class ExtendedHandlerRegistry : OverlayHandlerRegistry
{
    public void RegisterDefaultHandlers()
    {
        RegisterHandler(new TrackNodeOverlayHandler());
        RegisterHandler(new BuildingOverlayHandler());
        RegisterHandler(new BezierSpanOverlayHandler());
    }
}

// Use it
var renderer = FuseOverlayManager.Instance.GetRenderer();
renderer.HandlerRegistry.RegisterDefaultHandlers();
```

---

## API Reference

### FuseOverlayManager

```csharp
// Generic preview creation
OverlayPreviewData ApplyPreview<T>(T entity);
OverlayPreviewData ApplyPreview<T>(T entity, out string previewId);

// Update existing preview
void UpdatePreviewFromEntity<T>(string objectId, T entity);

// Access handler registry
OverlayHandlerRegistry HandlerRegistry { get; }
```

### OverlayHandlerRegistry

```csharp
// Register/unregister handlers
void RegisterHandler<T>(IOverlayHandler<T> handler);
void UnregisterHandler<T>();

// Query handlers
IOverlayHandler<T> GetHandler<T>();
bool HasHandler<T>();
int GetHandlerCount();

// Apply preview using handler
OverlayPreviewData ApplyPreview<T>(T entity, out string previewId);

// Maintenance
void ClearAllHandlers();
```

### IOverlayHandler<T>

```csharp
string HandlerName { get; }
bool CanHandle(T entity);
string GetEntityId(T entity);
GameObject GetTargetGameObject(T entity);
void ExtractPreviewTransform(T entity, out Vector3 position, out Quaternion rotation, out Vector3 scale);
IOverlayRenderable GetRenderable(T entity);
string GetObjectType(T entity);
Color? GetPreviewTint(T entity);
```

---

## Summary

The handler-based API provides:

✅ **Generic** - Single `ApplyPreview<T>()` for all entity types  
✅ **Extensible** - Add new entity types just by registering a handler  
✅ **Decoupled** - Application layer doesn't care about entity details  
✅ **Consistent** - Same API for TrackNode, Building, BezierSpan, etc.  
✅ **Type-Safe** - Generic typing ensures compile-time type checking

**Next Steps:**
1. Implement `IOverlayHandler<T>` for your entity types
2. Register handlers at editor startup
3. Use `ApplyPreview<T>()` throughout your editor tools
