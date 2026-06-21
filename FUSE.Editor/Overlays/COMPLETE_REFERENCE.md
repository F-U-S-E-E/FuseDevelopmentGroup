# Complete Overlay System Reference

## System Architecture

### Layers

```
┌─────────────────────────────────────────────────────┐
│ Editor Tools (e.g., Gizmo Integration)              │
└─────────────────────────────────────────────────────┘
                         ↓
┌─────────────────────────────────────────────────────┐
│ FuseOverlayManager (Singleton Entry Point)          │
│ - ApplyPreview<T>()                                 │
│ - RegisterPreview()                                 │
│ - UpdatePreview()                                   │
│ - UnregisterPreview()                               │
└─────────────────────────────────────────────────────┘
           ↓                              ↓
    ┌──────────────┐            ┌────────────────────┐
    │   Handler    │            │ FuseOverlayRenderer│
    │  Registry 2  │            │  & Selection System│
    │ (Dual-Type)  │            │                    │
    └──────────────┘            └────────────────────┘
           ↓                              ↓
    ┌──────────────┐            ┌────────────────────┐
    │  Concrete    │            │ OverlayPreviewData │
    │  Handler     │            │ (Entity + Preview) │
    │  (Per Type)  │            │                    │
    └──────────────┘            └────────────────────┘
           ↓                              ↓
    [TrackNode + FuseNode]    [Rendered Overlays]
```

## Type System

### Handler Types

```csharp
// Old (Single Type)
IOverlayHandler<EntityType>

// New (Dual Type)
IOverlayHandler<EntityType, PreviewDataType>
```

### Data Types

```csharp
class OverlayPreviewData
{
    // What you're rendering FROM
    public GameObject OriginalObject { get; set; }

    // What you're renderings BASED ON
    public object FuseData { get; set; }

    // Where to render it
    public Vector3 PreviewPosition { get; set; }
    public Quaternion PreviewRotation { get; set; }
    public Vector3 PreviewScale { get; set; }

    // How to render it
    public IOverlayRenderable Renderable { get; set; }
    public Color? Tint { get; set; }

    // Interaction
    public OverlaySelectionArea[] SelectionAreas { get; set; }
    public bool IsSelected { get; set; }
}
```

## API Methods

### FuseOverlayManager

| Method | Purpose | Parameters |
|--------|---------|-----------|
| `RegisterPreview()` | Create new overlay preview | id, gameObject, fuseData, renderable |
| `UpdatePreview()` | Update existing preview | id, position, rotation, scale |
| `UnregisterPreview()` | Remove preview | id |
| `HasPreview()` | Check if preview exists | id |
| `GetPreview()` | Get preview data | id |
| `ApplyPreview<T>()` | Generic preview application | entity |
| `SetSelectionCamera()` | Camera for selection raycasting | camera |
| `TrySelectPreviewAtMouse()` | Select at mouse position | mousePos |
| `IsEnabled` (property) | Enable/disable overlay system | true/false |

### IOverlayHandler<TEntity, TPreviewData>

| Method | Purpose | Returns |
|--------|---------|---------|
| `CanHandle()` | Can process this entity? | bool |
| `GetEntityId()` | Unique ID for preview | string |
| `GetTargetGameObject()` | GameObject to track | GameObject |
| `ExtractPreviewTransform()` | Get position/rotation/scale | out parameters |
| `GetRenderable()` | Custom mesh/material provider | IOverlayRenderable |
| `GetObjectType()` | Classification tag | string |
| `GetPreviewTint()` | Color tint for preview | Color? |
| `GetSelectionAreas()` | Clickable areas | OverlaySelectionArea[] |
| `OnPreviewSelected()` | Selection callback | void |

## Data Flows

### Creating a Preview

```
1. You create preview data (e.g., FuseNode)
2. You call RegisterPreview(entity, gameObject, fuseData)
3. Renderer creates OverlayPreviewData
4. Preview added to registry
5. Selection system notified
```

### Rendering a Preview

```
Each frame:
1. Renderer iterates active previews
2. For each preview:
   - Gets mesh from preview.Renderable
   - Gets matrix from preview.GetPreviewMatrix()
   - Gets tint from preview.Tint
   - Calls Graphics.DrawMesh()
3. Result: Preview visible at pending position
```

### Handling Selection

```
1. User clicks in viewport
2. Selection system raycasts
3. For each preview with hit:
   - Handler.OnPreviewSelected() called
   - Passes entity, previewData, selectionArea
4. Handler can then:
   - Register with selection system
   - Apply edits immediately
   - Show edit UI
   - etc.
```

## Common Implementation Patterns

### Pattern 1: Position Preview from Edit Data

```csharp
public void ExtractPreviewTransform(Entity entity, EditData data,
    out Vector3 pos, out Quaternion rot, out Vector3 scale)
{
    // Read from edit data, not entity
    pos = data.PendingPosition ?? entity.transform.position;
    rot = data.PendingRotation ?? entity.transform.rotation;
    scale = Vector3.one;
}
```

### Pattern 2: Renderable Based on Entity Type

```csharp
public IOverlayRenderable GetRenderable(Entity entity, EditData data)
{
    // Choose renderable based on entity type
    if (entity is TrackNode tn)
        return new TrackNodeOverlayAdapter(tn);
    if (entity is Building b)
        return new BuildingOverlayAdapter(b);
    return null; // Use default
}
```

### Pattern 3: Color Based on Edit State

```csharp
public Color? GetPreviewTint(Entity entity, EditData data)
{
    // Color shows edit status
    if (!data.HasChanges)
        return null; // No tint, use default
    if (data.IsConflicted)
        return Color.red;
    if (data.IsModified)
        return Color.yellow;
    return Color.green;
}
```

### Pattern 4: Context-Aware Selection

```csharp
public void OnPreviewSelected(Entity entity, EditData data, 
    OverlaySelectionArea area)
{
    // Use both entity and edit data
    var newValue = data.GetValueFor(area.SelectionData as string);
    EditSystem.ApplyEdit(entity, newValue);
}
```

## Integration Checklist

- [ ] Create concrete handler class implementing `IOverlayHandler<YourEntity, YourEditData>`
- [ ] Implement all required methods
- [ ] Create edit data class (e.g., FuseNode for TrackNode edits)
- [ ] Register handler at startup: `HandlerRegistry2.RegisterHandler<YourEntity, YourEditData>(handler)`
- [ ] In edit code: create edit data, call `RegisterPreview(id, gameObject, editData)`
- [ ] Test that overlay renders at correct position
- [ ] Test that overlay updates when edit data changes
- [ ] Test that selection callbacks fire
- [ ] Test that selection data applies edits correctly

## Performance Considerations

### Rendering
- **Complexity**: O(n) where n = number of active previews
- **Per-preview cost**: 1 Graphics.DrawMesh() call
- **Matrix calculation**: Cached in preview data
- **Typical limit**: 100+ simultaneous previews @ 60fps

### Selection
- **Raycast complexity**: O(n*m) where n = previews, m = areas per preview
- **Optimization**: Could add spatial hashing
- **Typical limit**: Hundreds of areas @ 60fps

### Memory
- **Per preview**: ~500 bytes (OverlayPreviewData + references)
- **Typical load**: Negligible for 100 previews

## Troubleshooting Guide

### Preview Not Rendering

**Check:**
1. Is `IsEnabled` true? `FuseOverlayManager.Instance.IsEnabled`
2. Does handler `CanHandle()` return true?
3. Are preview transforms valid (not NaN)?
4. Is renderable non-null?
5. Is main camera set? `SetSelectionCamera(Camera.main)`

**Fix:**
```csharp
// Verify preview exists and has data
var preview = manager.GetPreview(id);
if (preview == null)
{
    FuseLog.Error("Preview not found");
    return;
}

// Check transforms
if (float.IsNaN(preview.PreviewPosition.x))
{
    FuseLog.Error("Invalid preview position: NaN");
    return;
}
```

### Selection Not Working

**Check:**
1. Does `GetSelectionAreas()` return non-empty array?
2. Are areas selectable? `IsSelectable = true`
3. Is camera set? `SetSelectionCamera()`
4. Is handler's `OnPreviewSelected()` implemented?
5. Are you calling `TrySelectPreviewAtMouse()`?

**Fix:**
```csharp
// Debug selection areas
var areas = handler.GetSelectionAreas(entity, data, pos, rot, scale);
FuseLog.Info($"Selection areas: {areas?.Length ?? 0}");
foreach (var area in areas ?? Array.Empty<OverlaySelectionArea>())
{
    FuseLog.Info($"  Area {area.AreaId}: selectable={area.IsSelectable}");
}
```

### Handler Not Found

**Check:**
1. Did you use `HandlerRegistry2` not `HandlerRegistry`?
2. Did you register with correct types?
3. Are generic types spelled correctly?

**Fix:**
```csharp
// Register with explicit types
var registry2 = manager.HandlerRegistry2;
registry2.RegisterHandler<TrackNode, FuseNode>(handler);

// Verify registration
if (!registry2.HasHandler<TrackNode>())
{
    FuseLog.Error("TrackNode handler not registered");
}
```

## Migration Path

### Phase 1: Coexistence (Current)
- Old single-type handlers still work
- New dual-type handlers available
- Use both simultaneously

### Phase 2: Gradual Migration
- Migrate high-priority entity types to dual-type
- Leave legacy handlers in place
- Use compatibility layer if needed

### Phase 3: Complete Migration (Future)
- All handlers on dual-type system
- Deprecate old `IOverlayHandler<T>`
- Single registry for all handlers

## Files Reference

### Entry Points
- `FuseOverlayManager.cs` - Main API
- `OverlayHandlerRegistry2.cs` - Handler registry
- `OverlayPreviewData.cs` - Preview state

### Handlers
- `IOverlayHandler2.cs` - Dual-type interface
- Implement in: `Track/Overlays/YourEntityHandler.cs`

### Supporting Systems
- `OverlaySelectionSystem.cs` - Click handling
- `OverlaySelectionArea.cs` - Hit test data
- `IOverlayRenderable.cs` - Custom rendering

### Examples
- `TrackNodeOverlayExample.cs` - Usage example
- `TrackNodeGizmoOverlayIntegration.cs` - Integration example

## Quick Commands

```csharp
// Register handler
manager.HandlerRegistry2.RegisterHandler<TrackNode, FuseNode>(handler);

// Create preview
manager.RegisterPreview(id, gameObject, fuseData);

// Update preview
manager.UpdatePreview(id, newPos, newRot, newScale);

// Remove preview
manager.UnregisterPreview(id);

// Check if exists
if (manager.HasPreview(id)) { }

// Get preview data
var preview = manager.GetPreview(id);

// Enable/disable
manager.IsEnabled = false;

// Set selection camera
manager.SetSelectionCamera(myCamera);

// Try selecting
manager.TrySelectPreviewAtMouse(Input.mousePosition);
```

---

**For specific tasks, see DUAL_TYPE_QUICK_REFERENCE.md**

**For architecture details, see DUAL_TYPE_IMPLEMENTATION_SUMMARY.md**
