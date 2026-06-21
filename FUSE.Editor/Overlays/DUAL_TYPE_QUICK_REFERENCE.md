# Dual-Type Handler Quick Reference

## Registration

```csharp
// Create handler
var handler = new TrackNodeDualTypeHandler();

// Register with new registry (handles IOverlayHandler<TEntity, TPreviewData>)
FuseOverlayManager.Instance.HandlerRegistry2.RegisterHandler<TrackNode, FuseNode>(handler);
```

## Creating a Preview

```csharp
// 1. Create preview data
var fuseNode = new FuseNode
{
    Position = new FuseVector3(x, y, z),
    Rotation = new FuseVector3(rx, ry, rz),
};

// 2. Create optional renderable
var adapter = new TrackNodeOverlayAdapter(trackNode);

// 3. Register preview with both entity and preview data
var preview = FuseOverlayManager.Instance.RegisterPreview(
    objectId: trackNode.id,
    originalObject: trackNode.gameObject,
    fuseData: fuseNode,      // <-- Separate preview data
    renderable: adapter);
```

## Updating a Preview

```csharp
// Update preview data
fuseNode.Position = new FuseVector3(newX, newY, newZ);

// Update the displayed preview
FuseOverlayManager.Instance.UpdatePreview(
    trackNode.id,
    fuseNode.Position.ToVector3(),
    Quaternion.Identity,
    Vector3.one);
```

## Handler Implementation

```csharp
public class MyHandler<TEntity, TPreviewData> : IOverlayHandler<TEntity, TPreviewData>
{
    public string HandlerName => "My Handler";

    public bool CanHandle(TEntity entity) => entity != null;

    public string GetEntityId(TEntity entity) => /* get unique id */;

    public GameObject GetTargetGameObject(TEntity entity) => /* get GameObject */;

    public void ExtractPreviewTransform(
        TEntity entity,
        TPreviewData previewData,
        out Vector3 position,
        out Quaternion rotation,
        out Vector3 scale)
    {
        // Extract from previewData, not entity!
        // ...
    }

    public IOverlayRenderable GetRenderable(TEntity entity, TPreviewData previewData)
    {
        // Return custom renderable or null for default
    }

    public string GetObjectType(TEntity entity) => "EntityType";

    public Color? GetPreviewTint(TEntity entity, TPreviewData previewData)
    {
        // Return null for no tint
    }

    public OverlaySelectionArea[] GetSelectionAreas(
        TEntity entity,
        TPreviewData previewData,
        Vector3 previewPosition,
        Quaternion previewRotation,
        Vector3 previewScale)
    {
        // Return array of selectable areas
    }

    public void OnPreviewSelected(
        TEntity entity,
        TPreviewData previewData,
        OverlaySelectionArea selectionArea)
    {
        // Handle selection
    }
}
```

## Key Points

### Entity vs Preview Data
- **Entity**: The original GameObject (TrackNode, Building, etc.)
  - Used for: Getting renderable, determining colors, context
  - Read-only in handler

- **Preview Data**: The pending-edit object (FuseNode, FuseBuilding, etc.)
  - Used for: Transform extraction, displaying edit state
  - Updated by your code as user edits

### Handler Methods

| Method | Purpose | Reads From |
|--------|---------|-----------|
| `CanHandle()` | Can this handler process this entity? | Entity |
| `GetEntityId()` | Get unique preview ID | Entity |
| `GetTargetGameObject()` | Get GameObject to track | Entity |
| `ExtractPreviewTransform()` | Get preview position/rotation/scale | **Preview Data** |
| `GetRenderable()` | Custom mesh/material provider | Both |
| `GetObjectType()` | Classification tag | Entity |
| `GetPreviewTint()` | Color tint for preview | Both |
| `GetSelectionAreas()` | Define clickable areas | Both |
| `OnPreviewSelected()` | Selection callback | Both |

### Preview Data Lifetime

```
Create FuseNode
    ↓
RegisterPreview(entity, fuseData)
    ↓
[Editing...]
UpdatePreview() when FuseData changes
    ↓
CommitEdits() / UnregisterPreview()
    ↓
Clean up FuseNode
```

## Common Patterns

### Conditional Rendering Based on Both Entity and Preview State

```csharp
public Color? GetPreviewTint(TrackNode entity, FuseNode previewData)
{
    // Check entity type
    if (entity.flipSwitchStand)
        return Color.gold;

    // Check preview state
    if (previewData.HasUnsavedChanges)
        return Color.red;

    return Color.yellow;
}
```

### Transform from Preview Data, Renderable from Entity

```csharp
public void ExtractPreviewTransform(TrackNode entity, FuseNode previewData,
    out Vector3 position, out Quaternion rotation, out Vector3 scale)
{
    position = previewData.Position.ToVector3();
    rotation = Quaternion.Euler(previewData.Rotation.ToVector3());
    scale = Vector3.one;
}

public IOverlayRenderable GetRenderable(TrackNode entity, FuseNode previewData)
{
    // Use entity to determine what to render
    return new TrackNodeOverlayAdapter(entity);
}
```

### Multiple Selection Areas with Context

```csharp
public OverlaySelectionArea[] GetSelectionAreas(TrackNode entity, FuseNode previewData,
    Vector3 position, Quaternion rotation, Vector3 scale)
{
    var areas = new List<OverlaySelectionArea>();

    // Main area
    areas.Add(new OverlaySelectionArea
    {
        AreaId = $"{entity.id}_main",
        PreviewId = entity.id,
        Bounds = new Bounds(position, Vector3.one * 0.5f),
        IsSelectable = true,
        SelectionData = "main"
    });

    // Secondary areas based on entity type
    if (entity.flipSwitchStand)
    {
        areas.Add(new OverlaySelectionArea
        {
            AreaId = $"{entity.id}_stand",
            PreviewId = entity.id,
            Bounds = new Bounds(position + Vector3.up * 0.3f, Vector3.one * 0.3f),
            IsSelectable = true,
            SelectionData = "stand"
        });
    }

    return areas.ToArray();
}
```

## Checklist: Setting Up a New Entity Type

- [ ] Create preview data class (FuseMyEntity)
- [ ] Create handler class (MyEntityDualTypeHandler)
- [ ] Implement all IOverlayHandler<MyEntity, FuseMyEntity> methods
- [ ] Create optional renderable adapter (IOverlayRenderable)
- [ ] Register handler at startup: `HandlerRegistry2.RegisterHandler<MyEntity, FuseMyEntity>(handler)`
- [ ] In edit code: create FuseMyEntity, call RegisterPreview with both objects
- [ ] Test rendering at preview position
- [ ] Test selection and callbacks
- [ ] Test updates as preview data changes

## API Reference

### FuseOverlayManager

```csharp
// Register handler
HandlerRegistry2.RegisterHandler<TEntity, TPreviewData>(handler);

// Create preview
RegisterPreview(id, gameObject, previewData, renderable);

// Update preview
UpdatePreview(id, position, rotation, scale);

// Remove preview
UnregisterPreview(id);

// Check if preview exists
HasPreview(id);

// Get preview data
GetPreview(id);

// Enable/disable overlay system
IsEnabled = true/false;

// Set selection camera
SetSelectionCamera(camera);

// Try selecting at mouse position
TrySelectPreviewAtMouse(mousePos);
```

### OverlayPreviewData

```csharp
// Properties
string PreviewId                    // Unique ID
object FuseData                     // Preview/pending-edit data
GameObject OriginalObject           // Source GameObject
Vector3 PreviewPosition             // Render position
Quaternion PreviewRotation          // Render rotation
Vector3 PreviewScale                // Render scale
IOverlayRenderable Renderable       // Custom mesh/material provider
string ObjectType                   // Classification tag
Color? Tint                         // Color tint
OverlaySelectionArea[] SelectionAreas  // Clickable areas
object Entity                       // Original entity reference
bool IsSelected                     // Current selection state

// Methods
Matrix4x4 GetPreviewMatrix()        // World matrix at preview transform
Matrix4x4 GetOriginalMatrix()       // World matrix at original transform
void UpdatePreviewTransform(Vector3 pos, Quaternion rot, Vector3 scale)
```

---

**See DUAL_TYPE_HANDLER_MIGRATION.md for detailed migration guide**
