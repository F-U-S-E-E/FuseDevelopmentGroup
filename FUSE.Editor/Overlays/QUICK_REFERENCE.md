# FUSE Editor Overlay System - Quick Reference

## 30-Second Start

### Using Handler-Based API (Recommended)

```csharp
// Register a handler for your entity type
var handler = new MyEntityHandler();
FuseOverlayManager.Instance.HandlerRegistry.RegisterHandler<MyEntity>(handler);

// Create a preview using generic API
var entity = /* your entity */;
var preview = FuseOverlayManager.Instance.ApplyPreview(entity);

// Setup selection (once)
FuseOverlayManager.Instance.SetSelectionCamera(SceneView.lastActiveSceneView.camera);

// Handle clicks
var mousePos = Event.current.mousePosition;
FuseOverlayManager.Instance.TrySelectPreviewAtMouse(mousePos);
```

### Legacy DirectAPI

```csharp
// Get the overlay manager (singleton)
var overlay = FuseOverlayManager.Instance;

// Register a preview
var preview = overlay.RegisterPreview(
    "node-1",                                    // ID
    nodeGameObject,                              // Original object
    newPosition,                                 // Preview position
    newRotation,                                 // Preview rotation
    Vector3.one,                                 // Preview scale
    new TrackNodeOverlayAdapter(nodeGameObject)  // Renderer
);

// Update preview
overlay.UpdatePreview("node-1", updatedPos, updatedRot, Vector3.one);

// Apply changes
node.transform.position = preview.PreviewPosition;
overlay.UnregisterPreview("node-1");
```

## API Cheat Sheet

### Handlers (New)
```csharp
// Register handler
FuseOverlayManager.Instance.HandlerRegistry.RegisterHandler<T>(handler)

// Create preview via handler
FuseOverlayManager.Instance.ApplyPreview<T>(entity)

// Update preview from entity
FuseOverlayManager.Instance.UpdatePreviewFromEntity<T>(previewId, entity)
```

### Selection (New)
```csharp
// Setup camera for raycasting
FuseOverlayManager.Instance.SetSelectionCamera(camera)

// Try select from mouse
FuseOverlayManager.Instance.TrySelectPreviewAtMouse(mousePosition)

// Get selection system (advanced)
var selectionSystem = FuseOverlayManager.Instance.SelectionSystem

// Subscribe to events
selectionSystem.OnPreviewSelectionChanged += (id, area) => { }
selectionSystem.OnPreviewHovered += (id, area) => { }
selectionSystem.OnPreviewUnhovered += () => { }
```

### Registration
```csharp
// Register preview
overlay.RegisterPreview(id, obj, pos, rot, scale, renderable?)

// Unregister preview
overlay.UnregisterPreview(id)

// Clear all
overlay.ClearAllPreviews()
```

### Updates
```csharp
// Update position/rotation/scale
overlay.UpdatePreview(id, position, rotation, scale)

// Update from FuseNode
overlay.UpdatePreviewFromFuseNode(id, fuseNode)
```

### Queries
```csharp
// Check if exists
overlay.HasPreview(id)

// Get preview data
var preview = overlay.GetPreview(id)

// Count/list previews
int count = overlay.GetActivePreviewCount()
var ids = overlay.GetActivePreviewIds()

// Get (underlying renderer (advanced)
var renderer = overlay.GetRenderer()
```

### Customization
```csharp
// Change visibility
preview.IsVisible = false

// Change color
preview.Tint = Color.yellow

// Add type tag
preview.ObjectType = "TrackNode"
```

### Lifecycle
```csharp
// Enable/disable rendering
overlay.IsEnabled = false

// Subscribe to events
overlay.OnPreviewAdded += (id) => Debug.Log($"Added {id}");
overlay.OnPreviewUpdated += (id) => Debug.Log($"Updated {id}");
overlay.OnPreviewRemoved += (id) => Debug.Log($"Removed {id}");
```

## Common Workflows

### Workflow 1: Edit & Confirm
```csharp
var preview = overlay.RegisterPreview(id, obj, pos, rot, scale, adapter);
// ...user edits...
overlay.UpdatePreview(id, newPos, newRot, scale);
// ...user confirms...
obj.transform.position = preview.PreviewPosition;
overlay.UnregisterPreview(id);
```

### Workflow 2: Gizmo + Preview
```csharp
overlay.RegisterPreview(id, obj, pos, rot, scale, adapter);
gizmoMgr.BeginMove(gizmoTarget, finalPos =>
{
    overlay.UpdatePreview(id, finalPos, rot, scale);
    obj.transform.position = finalPos;
    overlay.UnregisterPreview(id);
});
```

### Workflow 3: Handler-Based with Selection
```csharp
// Setup (once)
var handler = new MyEntityHandler();
FuseOverlayManager.Instance.HandlerRegistry.RegisterHandler<MyEntity>(handler);
FuseOverlayManager.Instance.SetSelectionCamera(sceneCamera);

// Per entity
var entity = /* your entity */;
var preview = FuseOverlayManager.Instance.ApplyPreview(entity);

// On click
FuseOverlayManager.Instance.TrySelectPreviewAtMouse(mousePos);
// -> handler.OnPreviewSelected() is called
```

### Workflow 4: Multi-Select
```csharp
foreach (var node in nodes)
{
    overlay.RegisterPreview(node.id, node.obj, node.pos, node.rot, scale);
}
// ...update all...
foreach (var node in nodes)
{
    overlay.UpdatePreview(node.id, newPositions[node.id], rot, scale);
}
overlay.ClearAllPreviews();
```

## File Locations

```
FUSE.Editor/
├── Overlays/
│   ├── FuseOverlayRenderer.cs          (Core renderer)
│   ├── FuseOverlayManager.cs           (Singleton API)
│   ├── OverlayPreviewData.cs           (Preview state)
│   ├── OverlaySelectionSystem.cs       (NEW: Click handling)
│   ├── OverlaySelectionArea.cs         (NEW: Clickable regions)
│   ├── OverlayHandlerRegistry.cs       (Handler dispatch)
│   ├── IOverlayHandler.cs              (Handler interface)
│   ├── IOverlayRenderable.cs           (Custom rendering)
│   ├── README.md                        (Main docs)
│   ├── SELECTION_SYSTEM.md             (NEW: Selection guide)
│   ├── QUICK_REFERENCE.md              (This file)
│   ├── INTEGRATION_GUIDE.md            (Integration patterns)
│   └── IMPLEMENTATION_SUMMARY.md       (Architecture summary)
└── Track/Overlays/
    ├── TrackNodeOverlayHandler.cs      (TrackNode handler)
    ├── TrackNodeOverlayAdapter.cs      (TrackNode renderer)
    └── TrackNodeOverlayExample_HandlerBased.cs
```

## Key Classes

| Class | Purpose |
|-------|---------|
| `FuseOverlayManager` | Singleton API (use this) |
| `FuseOverlayRenderer` | Core rendering engine |
| `OverlayPreviewData` | Preview state container |
| `OverlaySelectionSystem` | Click/hover handling (NEW) |
| `OverlaySelectionArea` | Clickable region (NEW) |
| `IOverlayHandler<T>` | Entity-specific handler |
| `IOverlayRenderable` | Interface for custom rendering |
| `TrackNodeOverlayAdapter` | TrackNode support |

## Key Points

✅ **Display-Only**: Original objects never move  
✅ **Handler-Based**: Encapsulates entity-specific logic  
✅ **Selectable**: Click to trigger handler callbacks  
✅ **Singleton**: Access via `FuseOverlayManager.Instance`  
✅ **Extensible**: Implement `IOverlayHandler<T>` for custom entities  
✅ **Lightweight**: ~64 bytes per preview, O(n) rendering  
✅ **Safe**: Compatible with existing gizmo system  

## Troubleshooting

| Problem | Check |
|---------|-------|
| Not visible | `preview.IsVisible`, mesh exists, overlay enabled |
| Wrong position | Using world position (not local) |
| Selection not working | Camera set, preview visible, selection areas defined |
| Performance | Too many previews, use visibility culling |
| Mesh missing | Implement `IOverlayRenderable.GetOverlayMesh()` |

---

For more details, see:
- `README.md` - Full documentation
- `SELECTION_SYSTEM.md` - Selection feature guide (NEW)
- `INTEGRATION_GUIDE.md` - Detailed patterns
- `IMPLEMENTATION_SUMMARY.md` - Architecture overview
- Example files - Working code samples
