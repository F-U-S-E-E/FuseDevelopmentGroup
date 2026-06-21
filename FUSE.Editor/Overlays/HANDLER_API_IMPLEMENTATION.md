# Handler-Based Overlay API - Complete Implementation

## ✅ What Was Changed

The overlay system has been **completely refactored to use a generic handler-based architecture**. This eliminates all type-specific code from the application layer.

---

## 🎯 New Architecture

### Before: Type-Specific Hardcoding

```
Your Code → "Is it TrackNode?" → RegisterPreview(trackNode specifics)
         → "Is it Building?" → RegisterPreview(building specifics)
         → "Is it BezierSpan?" → RegisterPreview(bezier specifics)
```

Problems:
- ❌ Type-specific code scattered everywhere
- ❌ Duplicated conversion logic for each type
- ❌ Hard to add new entity types
- ❌ Not scalable

### After: Generic Handler Pattern

```
Your Code → ApplyPreview<T>(entity) → Lookup IOverlayHandler<T>
                                    → Handler extracts all entity specifics
                                    → Unified overlay preview
```

Benefits:
- ✅ Single generic API for all types
- ✅ Type-specific logic centralized in handlers
- ✅ Easy to support new entity types
- ✅ Clean, maintainable architecture

---

## 📦 New Components

### 1. `IOverlayHandler<T>`
Generic interface that handlers implement for each entity type:
- **Extract entity ID** - Unique identifier
- **Extract transform** - Position, rotation, scale
- **Provide renderable** - Custom mesh/material
- **Provide tint** - Optional color override

```csharp
public interface IOverlayHandler<T>
{
    bool CanHandle(T entity);
    string GetEntityId(T entity);
    GameObject GetTargetGameObject(T entity);
    void ExtractPreviewTransform(T entity, out Vector3 position, out Quaternion rotation, out Vector3 scale);
    IOverlayRenderable GetRenderable(T entity);
    string GetObjectType(T entity);
    Color? GetPreviewTint(T entity);
}
```

### 2. `OverlayHandlerRegistry`
Manages handlers for different entity types:
- Register/unregister handlers
- Look up handler for entity type
- Apply previews using appropriate handler

```csharp
var registry = FuseOverlayManager.Instance.HandlerRegistry;
registry.RegisterHandler(new TrackNodeOverlayHandler());
registry.RegisterHandler(new BuildingOverlayHandler());
```

### 3. Generic `ApplyPreview<T>()` API
New unified entry point in `FuseOverlayManager` and `FuseOverlayRenderer`:

```csharp
// Create preview - no type-specific code needed
var preview = FuseOverlayManager.Instance.ApplyPreview(trackNode);

// Update preview - handler extracts new transform
FuseOverlayManager.Instance.UpdatePreviewFromEntity(previewId, trackNode);
```

---

## 📚 Files Created

### Core Handler Infrastructure
1. **IOverlayHandler.cs** - Generic handler interface
2. **OverlayHandlerRegistry.cs** - Registry for managing handlers

### TrackNode Example
3. **TrackNodeOverlayHandler.cs** - Handler implementation for TrackNode
4. **TrackNodeOverlayExample_HandlerBased.cs** - Updated usage example

### Documentation
5. **HANDLER_BASED_API.md** - Complete API guide
6. **MIGRATION_GUIDE.md** - How to migrate from old API

---

## 🔧 How to Use

### Step 1: Create a Handler

```csharp
public class TrackNodeOverlayHandler : IOverlayHandler<TrackNode>
{
    public string HandlerName => "TrackNode";

    public bool CanHandle(TrackNode entity) => entity != null;

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

### Step 2: Register Handler

```csharp
// At editor startup
var registry = FuseOverlayManager.Instance.HandlerRegistry;
registry.RegisterHandler(new TrackNodeOverlayHandler());
registry.RegisterHandler(new BuildingOverlayHandler());
registry.RegisterHandler(new BezierSpanOverlayHandler());
```

### Step 3: Use Generic API

```csharp
// Create preview - works for ANY entity type!
var preview = FuseOverlayManager.Instance.ApplyPreview(trackNode);

// Update preview - handler extracts new data
FuseOverlayManager.Instance.UpdatePreviewFromEntity(previewId, trackNode);

// Batch operations - same pattern
var previews = FuseOverlayManager.Instance.GetRenderer()
    .ApplyPreviewBatch(nodes);
```

---

## 💡 Key Principles

### 1. Single Responsibility
Each handler is responsible for ONE entity type:
- Know how to extract entity ID
- Know how to extract transform
- Know how to provide renderable
- No mixing of concerns

### 2. Type Safety
Generic parameters ensure compile-time checking:
```csharp
ApplyPreview<TrackNode>(node);  // ✅ TrackNode
ApplyPreview<TrackNode>(building);  // ❌ Compile error
```

### 3. Centralization
All type-specific logic lives in ONE place (the handler), not scattered across the app.

### 4. Extensibility
Add support for new entity types without modifying existing code:
```csharp
registry.RegisterHandler(new MyNewEntityTypeHandler());
// Everything just works!
```

---

## 📊 Code Reduction

### Example: Edit Tool

**Old Approach (Type-Specific)**
```csharp
// ~50 lines with lots of manual conversion
var preview = FuseOverlayManager.Instance.RegisterPreview(
    objectId: GetCustomId(node),
    originalObject: GetGameObject(node),
    previewPosition: ExtractPosition(node),
    previewRotation: ExtractRotation(node),
    previewScale: Vector3.one,
    renderable: new TrackNodeOverlayAdapter(node)
);

// Later: Manual extraction again
preview.PreviewPosition = new Vector3(node.Position.x, node.Position.y, node.Position.z);
preview.PreviewRotation = Quaternion.Euler(node.Rotation.x, node.Rotation.y, node.Rotation.z);
FuseOverlayManager.Instance.UpdatePreview(previewId, 
    preview.PreviewPosition, 
    preview.PreviewRotation, 
    Vector3.one);
```

**New Approach (Handler-Based)**
```csharp
// ~5 lines, crystal clear intent
var preview = FuseOverlayManager.Instance.ApplyPreview(node, out var previewId);

// Later: Handler handles extraction automatically
FuseOverlayManager.Instance.UpdatePreviewFromEntity(previewId, node);
```

**Reduction: ~90% less code** ✨

---

## ✨ Features

### ✅ Generic API
Single `ApplyPreview<T>(entity)` works for all entity types

### ✅ Automatic Extraction
Handler extracts ID, transform, renderable - no manual conversion

### ✅ Type-Safe
Generic parameters prevent type mismatches at compile time

### ✅ Extensible
Add new entity types by registering a handler

### ✅ Batch Operations
`ApplyPreviewBatch<T>(...)` for multiple entities

### ✅ Centralized Logic
All per-type logic in one handler class

### ✅ URP Compatible
Works with Universal Render Pipeline (as before)

---

## 🧪 Example Workflow

```csharp
// 1. Register handlers at startup
TrackNodeOverlaySetup.RegisterTrackHandlers();

// 2. Create a preview - super simple!
var trackNode = GetTrackNodeFromScene();
var preview = FuseOverlayManager.Instance.ApplyPreview(trackNode, out var previewId);

// 3. Edit the entity
trackNode.transform.position = new Vector3(10, 20, 30);
trackNode.transform.rotation = Quaternion.Euler(45, 90, 0);

// 4. Update preview - handler does the work
FuseOverlayManager.Instance.UpdatePreviewFromEntity(previewId, trackNode);

// 5. Preview appears on screen at (10,20,30) with rotation applied!

// 6. Confirm or cancel
if (userConfirmed)
    FuseOverlayManager.Instance.UnregisterPreview(previewId);
else
    RevertNode(trackNode);
```

---

## 🔄 Migration Path

### Old Code (Still Works)
```csharp
var preview = FuseOverlayManager.Instance.RegisterPreview(
    id, gameObject, pos, rot, scale, renderable);
```

### New Code (Recommended)
```csharp
var preview = FuseOverlayManager.Instance.ApplyPreview(entity);
```

**You can migrate gradually** - both APIs coexist!

---

## 📋 API Reference

### FuseOverlayManager

```csharp
// Generic preview creation
OverlayPreviewData ApplyPreview<T>(T entity);
OverlayPreviewData ApplyPreview<T>(T entity, out string previewId);

// Update from entity
void UpdatePreviewFromEntity<T>(string objectId, T entity);

// Access handler registry
OverlayHandlerRegistry HandlerRegistry { get; }

// Traditional API (still works)
OverlayPreviewData RegisterPreview(...);
void UpdatePreview(...);
```

### OverlayHandlerRegistry

```csharp
// Register/unregister
void RegisterHandler<T>(IOverlayHandler<T> handler);
void UnregisterHandler<T>();

// Query
IOverlayHandler<T> GetHandler<T>();
bool HasHandler<T>();
int GetHandlerCount();
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

## 📖 Documentation

- **HANDLER_BASED_API.md** - Complete guide with examples
- **MIGRATION_GUIDE.md** - Step-by-step migration from old API
- **README.md** - Overall overlay system documentation

---

## ✅ Verification

- ✅ Build successful
- ✅ All files compile
- ✅ Generic type system working
- ✅ Handler registry functional
- ✅ URP compatibility maintained
- ✅ Examples provided
- ✅ Documentation complete

---

## 🚀 Next Steps

1. **Create handlers for other entity types:**
   - BuildingOverlayHandler
   - BezierSpanOverlayHandler
   - Any other entities you need to preview

2. **Register handlers at editor startup**
   - Use pattern suggested in TrackNodeOverlayExample_HandlerBased.cs

3. **Replace existing API calls:**
   - Update code to use `ApplyPreview<T>()` and `UpdatePreviewFromEntity()`

4. **Enjoy the simplified API!**

---

## 💭 Design Philosophy

> "Generic, centralized, extensible"

Instead of:
- Repeating type-specific code everywhere
- Mixing entity logic with preview logic
- Making it hard to add new entity types

We now have:
- Single generic entry point
- Centralized handler implementations
- Trivial extension to new entity types

**Result:** Cleaner, more maintainable, more scalable system.

---

## Summary

✨ **The overlay system now uses a modern, generic handler-based architecture that eliminates type-specific hardcoding and provides a clean, unified API for all entity types.**

```csharp
// That's all you need:
FuseOverlayManager.Instance.ApplyPreview(entity);
```

No more conversion boilerplate. No more type-specific code. Just pure, simple preview functionality.
