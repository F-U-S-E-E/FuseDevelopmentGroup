# Handler-Based Overlay API - Quick Reference

## 🚀 TL;DR

```csharp
// 1. Create handler
public class MyEntityHandler : IOverlayHandler<MyEntity>
{
    public string HandlerName => "MyEntity";
    public bool CanHandle(MyEntity e) => e != null;
    public string GetEntityId(MyEntity e) => $"my_{e.Id}";
    public GameObject GetTargetGameObject(MyEntity e) => e.gameObject;
    public void ExtractPreviewTransform(MyEntity e, out Vector3 pos, out Quaternion rot, out Vector3 scale)
    {
        pos = e.transform.position;
        rot = e.transform.rotation;
        scale = Vector3.one;
    }
    public IOverlayRenderable GetRenderable(MyEntity e) => null; // or custom adapter
    public string GetObjectType(MyEntity e) => "MyEntity";
    public Color? GetPreviewTint(MyEntity e) => null;
}

// 2. Register handler (once, at startup)
FuseOverlayManager.Instance.HandlerRegistry.RegisterHandler(new MyEntityHandler());

// 3. Use it!
var preview = FuseOverlayManager.Instance.ApplyPreview(entity);
FuseOverlayManager.Instance.UpdatePreviewFromEntity(previewId, entity);
```

---

## Core API

### Create Preview
```csharp
// Simple
var preview = FuseOverlayManager.Instance.ApplyPreview(entity);

// Get ID too
var preview = FuseOverlayManager.Instance.ApplyPreview(entity, out var previewId);
```

### Update Preview
```csharp
FuseOverlayManager.Instance.UpdatePreviewFromEntity(previewId, entity);
```

### Remove Preview
```csharp
FuseOverlayManager.Instance.UnregisterPreview(previewId);
```

### Batch Operations
```csharp
var previews = FuseOverlayManager.Instance.GetRenderer()
    .ApplyPreviewBatch(entity1, entity2, entity3);
```

---

## Handler Interface

```csharp
public interface IOverlayHandler<T>
{
    // Required: Basic info
    string HandlerName { get; }  // "TrackNode", "Building", etc.
    bool CanHandle(T entity);    // Entity validation

    // Required: Prerequisites
    string GetEntityId(T entity);           // Unique ID for tracking
    GameObject GetTargetGameObject(T entity); // Attach preview to this

    // Required: Transform extraction
    void ExtractPreviewTransform(T entity, out Vector3 position, out Quaternion rotation, out Vector3 scale);

    // Optional: Rendering customization
    IOverlayRenderable GetRenderable(T entity); // null = default wireframe
    string GetObjectType(T entity);      // "TrackNode", "Building", etc.
    Color? GetPreviewTint(T entity);     // null = white
}
```

---

## Common Patterns

### Pattern 1: Simple Static Entity
```csharp
class SimpleEntityHandler : IOverlayHandler<SimpleEntity>
{
    public string HandlerName => "SimpleEntity";
    public bool CanHandle(SimpleEntity e) => e != null;
    public string GetEntityId(SimpleEntity e) => e.Id.ToString();
    public GameObject GetTargetGameObject(SimpleEntity e) => e.gameObject;
    public void ExtractPreviewTransform(SimpleEntity e, out Vector3 pos, out Quaternion rot, out Vector3 scale)
    {
        pos = e.Position;
        rot = Quaternion.identity;
        scale = Vector3.one;
    }
    public IOverlayRenderable GetRenderable(SimpleEntity e) => null;
    public string GetObjectType(SimpleEntity e) => "SimpleEntity";
    public Color? GetPreviewTint(SimpleEntity e) => null;
}
```

### Pattern 2: Entity with Custom Rendering
```csharp
class ComplexEntityHandler : IOverlayHandler<ComplexEntity>
{
    public string HandlerName => "ComplexEntity";
    // ... other methods ...
    public IOverlayRenderable GetRenderable(ComplexEntity e) 
        => new ComplexEntityAdapter(e); // Custom adapter
}
```

### Pattern 3: Entity with State-Based Tinting
```csharp
public Color? GetPreviewTint(MyEntity e)
{
    if (e.IsSelected) return Color.green;
    if (e.IsLoading) return Color.yellow;
    if (e.HasError) return Color.red;
    return null; // Default white
}
```

### Pattern 4: ID Generation from Different Sources
```csharp
public string GetEntityId(MyEntity e)
{
    // Option A: Internal ID
    return e.UniqueId;

    // Option B: Instance ID
    return e.GetInstanceID().ToString();

    // Option C: Compound key
    return $"{e.ParentId}_{e.LocalId}";
}
```

---

## Registration Patterns

### Pattern 1: Static Initialization
```csharp
public static class OverlayInit
{
    [RuntimeInitializeOnLoadMethod]
    public static void Init()
    {
        var reg = FuseOverlayManager.Instance.HandlerRegistry;
        reg.RegisterHandler(new TrackNodeHandler());
        reg.RegisterHandler(new BuildingHandler());
    }
}
```

### Pattern 2: Editor Window
```csharp
class MyEditorWindow : EditorWindow
{
    void OnEnable() => 
        FuseOverlayManager.Instance.HandlerRegistry
            .RegisterHandler(new MyEntityHandler());
}
```

### Pattern 3: Lazy Loading
```csharp
class OverlayService
{
    static bool _init = false;
    static void Init()
    {
        if (_init) return;
        FuseOverlayManager.Instance.HandlerRegistry
            .RegisterHandler(new MyEntityHandler());
        _init = true;
    }

    public static void CreatePreview<T>(T e)
    {
        Init();
        return FuseOverlayManager.Instance.ApplyPreview(e);
    }
}
```

---

## Debugging

```csharp
// Check handler registered
var hasHandler = FuseOverlayManager.Instance.HandlerRegistry.HasHandler<TrackNode>();
Debug.Log($"Handler exists: {hasHandler}");

// Get handler count
var count = FuseOverlayManager.Instance.HandlerRegistry.GetHandlerCount();
Debug.Log($"Total handlers: {count}");

// Try with error handling
var preview = FuseOverlayManager.Instance.ApplyPreview(entity);
if (preview == null)
    Debug.LogError("Preview creation failed");
else
    Debug.Log($"Created: {preview.ObjectId}");

// Check preview registered
bool exists = FuseOverlayManager.Instance.HasPreview(previewId);
Debug.Log($"Preview exists: {exists}");
```

---

## Old vs New

| Old | New |
|-----|-----|
| `RegisterPreview(id, go, pos, rot, scale, renderable)` | `ApplyPreview(entity)` |
| Manual ID generation | Handler provides ID |
| Manual transform extraction | Handler extracts automatically |
| Type-specific code everywhere | Centralized in handler |
| 50+ lines for each type | 5 lines max |

---

## Checklist: Creating Handler

- [ ] Implement `IOverlayHandler<T>`
- [ ] Implement `HandlerName` property
- [ ] Implement `CanHandle()` validation
- [ ] Implement `GetEntityId()` - **MUST return same ID consistently**
- [ ] Implement `GetTargetGameObject()` - **MUST not return null**
- [ ] Implement `ExtractPreviewTransform()` - extract pos/rot/scale
- [ ] Implement `GetRenderable()` - null for default
- [ ] Implement `GetObjectType()` - "TypeName"
- [ ] Implement `GetPreviewTint()` - null for white
- [ ] Register handler before (runtime/editor startup)
- [ ] Test with `ApplyPreview<T>(entity)`

---

## Files

| File | Purpose |
|------|---------|
| `IOverlayHandler.cs` | Handler interface |
| `OverlayHandlerRegistry.cs` | Registry for handlers |
| `FuseOverlayManager.cs` | Public API (updated) |
| `FuseOverlayRenderer.cs` | Renderer (updated) |
| `TrackNodeOverlayHandler.cs` | Example handler |
| `HANDLER_BASED_API.md` | Full documentation |
| `MIGRATION_GUIDE.md` | How to migrate |

---

## Tips

✨ **Always return valid IDs from `GetEntityId()`** - don't generate new ones each call!

✨ **Return null from `GetRenderable()`** - default wireframe works great and is fast

✨ **Use tinting sparingly** - only when it conveys important state

✨ **Fail gracefully** in `CanHandle()` - return false for invalid entities

✨ **Test your handlers** - create a unit test for extraction logic

---

## Examples

See:
- `TrackNodeOverlayHandler.cs` - Basic handler
- `TrackNodeOverlayExample_HandlerBased.cs` - Usage examples
- Documentation files for more patterns

---

## Support

Check:
- `HANDLER_BASED_API.md` - Full guide
- `MIGRATION_GUIDE.md` - From old API
- `HANDLER_API_IMPLEMENTATION.md` - Design overview
