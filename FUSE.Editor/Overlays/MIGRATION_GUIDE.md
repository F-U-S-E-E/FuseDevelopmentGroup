# Migration Guide: Type-Specific to Handler-Based API

## Overview

The overlay system has evolved from a **type-specific API** to a **generic handler-based API**. This guide explains the changes and how to migrate existing code.

---

## Key Changes

### Before: Type-Specific API

```csharp
// Old approach: Caller had to manually handle TrackNode specifics
var preview = FuseOverlayManager.Instance.RegisterPreview(
    objectId: $"node_{trackNode.Id}",
    originalObject: trackNodeGameObject,
    previewPosition: new Vector3(trackNode.Position.x, trackNode.Position.y, trackNode.Position.z),
    previewRotation: Quaternion.Euler(trackNode.Rotation.x, trackNode.Rotation.y, trackNode.Rotation.z),
    previewScale: Vector3.one,
    renderable: new TrackNodeOverlayAdapter(trackNode)
);

// Update was also manual
preview.PreviewPosition = newPosition;
FuseOverlayManager.Instance.UpdatePreview(previewId, newPosition, rotation, scale);
```

**Problems:**
- ❌ Repeats for every entity type (TrackNode, Building, BezierSpan, etc.)
- ❌ Type-specific conversion logic scattered across codebase
- ❌ Error-prone manual ID generation
- ❌ Hard to maintain consistency

### After: Generic Handler-Based API

```csharp
// New approach: Single line!
var preview = FuseOverlayManager.Instance.ApplyPreview(trackNode);

// Update is equally simple
FuseOverlayManager.Instance.UpdatePreviewFromEntity(previewId, trackNode);
```

**Benefits:**
- ✅ Single unified API for all entity types
- ✅ Type-specific logic centralized in handlers
- ✅ Less code, easier to maintain
- ✅ Compile-time type safety

---

## Migration Steps

### Step 1: Create a Handler

For each entity type, implement `IOverlayHandler<T>`:

```csharp
public class TrackNodeOverlayHandler : IOverlayHandler<TrackNode>
{
    public string HandlerName => "TrackNode";

    public bool CanHandle(TrackNode entity) => entity != null && entity.transform != null;

    public string GetEntityId(TrackNode entity)
        => $"track_node_{entity.GetInstanceID()}";

    public GameObject GetTargetGameObject(TrackNode entity)
        => entity.gameObject;

    public void ExtractPreviewTransform(
        TrackNode entity,
        out Vector3 position,
        out Quaternion rotation,
        out Vector3 scale)
    {
        var transform = entity.transform;
        position = transform.position;
        rotation = transform.rotation;
        scale = transform.lossyScale;
    }

    public IOverlayRenderable GetRenderable(TrackNode entity)
        => new TrackNodeOverlayAdapter(entity);

    public string GetObjectType(TrackNode entity) => "TrackNode";

    public Color? GetPreviewTint(TrackNode entity) => null;
}
```

### Step 2: Register the Handler

```csharp
// Do this once at editor startup
var registry = FuseOverlayManager.Instance.HandlerRegistry;
registry.RegisterHandler(new TrackNodeOverlayHandler());
```

### Step 3: Replace API Calls

#### Creating a Preview

**Old:**
```csharp
var preview = FuseOverlayManager.Instance.RegisterPreview(
    objectId: GetNodeId(trackNode),
    originalObject: GetNodeGameObject(trackNode),
    previewPosition: ExtractPosition(trackNode),
    previewRotation: ExtractRotation(trackNode),
    previewScale: Vector3.one,
    renderable: new TrackNodeOverlayAdapter(trackNode)
);
var previewId = preview.ObjectId;
```

**New:**
```csharp
var preview = FuseOverlayManager.Instance.ApplyPreview(trackNode, out var previewId);
```

#### Updating a Preview

**Old:**
```csharp
// Manual transform extraction
var position = new Vector3(trackNode.Position.x, trackNode.Position.y, trackNode.Position.z);
var rotation = Quaternion.Euler(trackNode.Rotation.x, trackNode.Rotation.y, trackNode.Rotation.z);
FuseOverlayManager.Instance.UpdatePreview(previewId, position, rotation, Vector3.one);
```

**New:**
```csharp
// Handler extracts transform automatically
FuseOverlayManager.Instance.UpdatePreviewFromEntity(previewId, trackNode);
```

#### Batch Operations

**Old:**
```csharp
foreach (var node in nodes)
{
    var preview = RegisterTrackNodePreview(node);
    previews.Add(preview);
}
```

**New:**
```csharp
var previews = FuseOverlayManager.Instance.GetRenderer()
    .ApplyPreviewBatch(nodes);
```

---

## Complete Code Comparison

### Editing a Single Node

#### Old Approach
```csharp
public class NodeEditor
{
    private TrackNode _node;
    private string _previewId;

    public void BeginEdit(TrackNode node)
    {
        _node = node;

        // Manual setup
        var preview = FuseOverlayManager.Instance.RegisterPreview(
            objectId: $"node_{node.GetInstanceID()}",
            originalObject: node.gameObject,
            previewPosition: node.transform.position,
            previewRotation: node.transform.rotation,
            previewScale: node.transform.lossyScale,
            renderable: new TrackNodeOverlayAdapter(node)
        );

        _previewId = preview?.ObjectId;
    }

    public void UpdatePosition(Vector3 newPos)
    {
        if (_node == null) return;
        _node.transform.position = newPos;

        // Manual update
        FuseOverlayManager.Instance.UpdatePreview(
            _previewId,
            _node.transform.position,
            _node.transform.rotation,
            _node.transform.lossyScale
        );
    }

    public void ApplyEdit()
    {
        if (_previewId != null)
            FuseOverlayManager.Instance.UnregisterPreview(_previewId);
        _node = null;
    }
}
```

#### New Approach
```csharp
public class NodeEditor
{
    private TrackNode _node;
    private string _previewId;

    public void BeginEdit(TrackNode node)
    {
        _node = node;
        var preview = FuseOverlayManager.Instance.ApplyPreview(node, out _previewId);
    }

    public void UpdatePosition(Vector3 newPos)
    {
        if (_node == null) return;
        _node.transform.position = newPos;
        FuseOverlayManager.Instance.UpdatePreviewFromEntity(_previewId, _node);
    }

    public void ApplyEdit()
    {
        if (_previewId != null)
            FuseOverlayManager.Instance.UnregisterPreview(_previewId);
        _node = null;
    }
}
```

**Result:** ~40% less code, much clearer intent

---

## Handler Registration Patterns

### Pattern 1: Static Initialization

```csharp
public static class OverlaySetup
{
    [RuntimeInitializeOnLoadMethod]
    public static void InitializeOverlayHandlers()
    {
        var registry = FuseOverlayManager.Instance.HandlerRegistry;
        registry.RegisterHandler(new TrackNodeOverlayHandler());
        registry.RegisterHandler(new BuildingOverlayHandler());
        registry.RegisterHandler(new BezierSpanOverlayHandler());
    }
}
```

### Pattern 2: Editor Window Initialization

```csharp
public class TrackEditorWindow : EditorWindow
{
    private void OnEnable()
    {
        // Register handlers when window opens
        var registry = FuseOverlayManager.Instance.HandlerRegistry;
        registry.RegisterHandler(new TrackNodeOverlayHandler());
    }

    private void OnDisable()
    {
        // Optional: Unregister when window closes
        FuseOverlayManager.Instance.HandlerRegistry.UnregisterHandler<TrackNode>();
    }
}
```

### Pattern 3: Lazy Registration

```csharp
public class OverlayService
{
    private static bool _handlersRegistered = false;

    private static void EnsureHandlersRegistered()
    {
        if (_handlersRegistered) return;

        var registry = FuseOverlayManager.Instance.HandlerRegistry;
        registry.RegisterHandler(new TrackNodeOverlayHandler());
        registry.RegisterHandler(new BuildingOverlayHandler());

        _handlersRegistered = true;
    }

    public static OverlayPreviewData CreatePreview<T>(T entity)
    {
        EnsureHandlersRegistered();
        return FuseOverlayManager.Instance.ApplyPreview(entity);
    }
}
```

---

## Old API Still Works

The old low-level API hasn't been removed - you can still use it:

```csharp
// Still valid - direct registration
var preview = FuseOverlayManager.Instance.RegisterPreview(
    "custom_id",
    gameObject,
    position,
    rotation,
    scale
);

// Still valid - direct update
FuseOverlayManager.Instance.UpdatePreview(previewId, newPos, newRot, newScale);
```

**When to use:**
- For one-off, non-entity previews
- When you have completely custom data
- For experimental/prototype code

**When NOT to use:**
- Entity-based code (use handlers instead)
- Recurring implementations (consolidate in a handler)

---

## Handling Entity-Specific State

### Example: Different Tints for Different Node States

**Old:**
```csharp
var tint = node.IsModified ? Color.yellow : null;
// Manually set tint before or after registration
preview.Tint = tint;
```

**New:**
```csharp
// Handler encapsulates the logic
public class TrackNodeOverlayHandler : IOverlayHandler<TrackNode>
{
    public Color? GetPreviewTint(TrackNode entity)
    {
        return entity.IsModified ? Color.yellow : null;
    }
}
```

---

## Troubleshooting Migration

### Issue: "Handler not registered"

**Cause:** Handler wasn't registered before calling `ApplyPreview`

**Solution:** Call registration before using the API

```csharp
var registry = FuseOverlayManager.Instance.HandlerRegistry;
registry.RegisterHandler(new TrackNodeOverlayHandler());

var preview = FuseOverlayManager.Instance.ApplyPreview(trackNode);
```

### Issue: "No handler registered for type 'TrackNode'"

**Cause:** Handler wasn't registered, or type name mismatch

**Solution:** Verify handler is registered for the exact type

```csharp
// Check if handler exists
bool exists = FuseOverlayManager.Instance.HandlerRegistry.HasHandler<TrackNode>();
Debug.Log($"Handler exists: {exists}");
```

### Issue: "Handler returned null GameObject"

**Cause:** `GetTargetGameObject()` returned null

**Solution:** Implement `GetTargetGameObject()` correctly

```csharp
public GameObject GetTargetGameObject(TrackNode entity)
{
    if (entity == null) return null;
    return entity.gameObject; // ← Must not be null
}
```

---

## Summary

| Aspect | Old API | New API |
|--------|---------|---------|
| Creation | `RegisterPreview(...)` | `ApplyPreview<T>(entity)` |
| Update | `UpdatePreview(id, pos, rot, scale)` | `UpdatePreviewFromEntity(id, entity)` |
| Setup | Type-specific | Handler-based |
| Extensibility | Add type-specific code | Register new handler |
| Maintainability | Scattered logic | Centralized handlers |
| Boilerplate | High | Low |

**Migration is straightforward:**
1. Create handlers for your entity types
2. Register handlers at startup
3. Replace API calls with generic versions
4. Remove type-specific code

**Old API still works** - migrate at your own pace, but new code should use handlers.
