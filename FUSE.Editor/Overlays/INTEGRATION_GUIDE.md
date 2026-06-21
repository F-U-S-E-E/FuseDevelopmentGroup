# FUSE Editor Overlay System - Integration Guide

## Quick Start

### 1. Basic Setup

The overlay system is a singleton that auto-initializes when first accessed:

```csharp
var overlay = FuseOverlayManager.Instance;
```

### 2. Register a Preview

```csharp
// For a TrackNode
var node = /* your TrackNode */;
var adapter = new TrackNodeOverlayAdapter(node);

var preview = overlay.RegisterPreview(
    objectId: node.id,
    originalObject: node.gameObject,
    previewPosition: newPosition,      // Position from pending edits
    previewRotation: newRotation,      // Rotation from pending edits
    previewScale: Vector3.one,
    renderable: adapter);

// Customize the preview
if (preview != null)
{
    preview.ObjectType = "TrackNode";
    preview.Tint = Color.yellow;
}
```

### 3. Update as User Edits

```csharp
// Update preview as position changes
overlay.UpdatePreview(nodeId, updatedPosition, updatedRotation, Vector3.one);

// Or from FuseNode directly
overlay.UpdatePreviewFromFuseNode(nodeId, pendingFuseNodeEdits);
```

### 4. Confirm or Cancel

```csharp
// Confirm: Apply edits to actual object
node.transform.position = preview.PreviewPosition;
overlay.UnregisterPreview(nodeId);  // Clear preview

// Or cancel: Just remove preview (original untouched)
overlay.UnregisterPreview(nodeId);
```

---

## Integration Patterns

### Pattern 1: Standalone Overlay Preview
*Use when you want to preview edits without gizmo control*

```csharp
// Start editing
var preview = overlay.RegisterPreview(nodeId, nodeObject, position, rotation, scale);

// Update while editing (user input, UI sliders, etc.)
overlay.UpdatePreview(nodeId, newPosition, newRotation, Vector3.one);

// Confirm when ready
node.transform.position = newPosition;  // Apply
overlay.UnregisterPreview(nodeId);      // Clear
```

### Pattern 2: Gizmo + Overlay Preview
*Use when you want gizmo control with visual preview*

See `TrackNodeGizmoOverlayIntegration.cs` for complete example:

```csharp
// 1. Register overlay preview
var preview = overlay.RegisterPreview(nodeId, nodeObject, 
    initialPosition, initialRotation, Vector3.one);

// 2. Start gizmo on temporary target
var gizmoTarget = new GameObject();
gizmoTarget.transform.position = initialPosition;
gizmoManager.BeginMove(gizmoTarget, finalPosition =>
{
    // 3. Update preview as gizmo moves (in Update loop)
    overlay.UpdatePreview(nodeId, currentGizmoPos, currentGizmoRot, Vector3.one);

    // 4. On completion, apply to actual node and clear preview
    node.transform.position = finalPosition;
    overlay.UnregisterPreview(nodeId);
});
```

### Pattern 3: Multi-Object Preview
*Use when editing multiple objects with previews*

```csharp
// Register previews for multiple objects
foreach (var node in selectedNodes)
{
    overlay.RegisterPreview(node.id, node.gameObject,
        GetPendingPosition(node),
        GetPendingRotation(node),
        Vector3.one);
}

// Update all at once
foreach (var node in selectedNodes)
{
    overlay.UpdatePreview(node.id, newPositions[node.id], rotations[node.id], Vector3.one);
}

// Clear all when done
overlay.ClearAllPreviews();
```

### Pattern 4: Type-Specific Rendering
*Use IOverlayRenderable for custom preview appearance*

```csharp
public class BuildingOverlayAdapter : IOverlayRenderable
{
    private Building _building;

    public Mesh GetOverlayMesh()
    {
        // Return custom mesh, e.g., bounding box wireframe
        return CreateBoundingBoxMesh(_building.bounds);
    }

    public Material GetOverlayMaterial()
    {
        // Use custom material for buildings (e.g., red outline)
        return Resources.Load<Material>("Materials/OverlayBuilding");
    }

    // ... implement other interface methods
}

// Use it
var adapter = new BuildingOverlayAdapter(building);
overlay.RegisterPreview(buildingId, buildingObj, pos, rot, scale, adapter);
```

---

## Common Scenarios

### Scenario: Editing a Track Node Position

```csharp
public class NodeEditorUI : MonoBehaviour
{
    private TrackNode _editingNode;
    private FuseOverlayManager _overlay;

    public void StartEditingNode(TrackNode node)
    {
        _editingNode = node;
        _overlay = FuseOverlayManager.Instance;

        // Show preview at current position
        var adapter = new TrackNodeOverlayAdapter(node);
        _overlay.RegisterPreview(
            node.id, node.gameObject,
            node.transform.position,
            node.transform.rotation,
            Vector3.one,
            adapter);
    }

    public void OnPositionInputChanged(Vector3 newPosition)
    {
        // Update preview as user types position
        _overlay.UpdatePreview(_editingNode.id, newPosition, 
            _editingNode.transform.rotation, Vector3.one);
    }

    public void OnConfirmEdit()
    {
        var newPos = _overlay.GetPreview(_editingNode.id).PreviewPosition;
        _editingNode.transform.position = newPos;
        _overlay.UnregisterPreview(_editingNode.id);
    }
}
```

### Scenario: Bezier Curve Control Point Preview

```csharp
public class BezierControlPointOverlay
{
    public void ShowControlPointPreview(BezierSpline spline, int pointIndex, Vector3 newPosition)
    {
        var overlay = FuseOverlayManager.Instance;
        var previewId = $"{spline.id}_point_{pointIndex}";

        // For Bezier points, you might create a small sphere mesh
        var adapter = new BezierPointOverlayAdapter(spline, pointIndex);

        overlay.RegisterPreview(previewId, spline.gameObject,
            newPosition, Quaternion.identity, Vector3.one,
            adapter);
    }

    public void UpdateControlPointPreview(string previewId, Vector3 newPosition)
    {
        FuseOverlayManager.Instance.UpdatePreview(
            previewId, newPosition, Quaternion.identity, Vector3.one);
    }

    public void ConfirmControlPointEdit(string previewId, BezierSpline spline, int index)
    {
        var preview = FuseOverlayManager.Instance.GetPreview(previewId);
        spline.SetControlPoint(index, preview.PreviewPosition);
        FuseOverlayManager.Instance.UnregisterPreview(previewId);
    }
}
```

### Scenario: Building Placement Preview

```csharp
public class BuildingPlacementUI : MonoBehaviour
{
    private GameObject _previewBuilding;
    private FuseOverlayManager _overlay;

    public void BeginPlacingBuilding(Building buildingPrefab)
    {
        _overlay = FuseOverlayManager.Instance;

        // Instantiate invisible preview building
        _previewBuilding = Instantiate(buildingPrefab.gameObject);
        _previewBuilding.SetActive(false);

        var adapter = new BuildingOverlayAdapter(buildingPrefab);

        _overlay.RegisterPreview(
            "preview_building",
            _previewBuilding,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one,
            adapter);
    }

    public void OnMouseMove(Vector3 worldPosition)
    {
        // Update building preview position as mouse moves
        _overlay.UpdatePreview("preview_building", worldPosition, Quaternion.identity, Vector3.one);
    }

    public void OnPlaceBuilding()
    {
        var preview = _overlay.GetPreview("preview_building");
        var finalPos = preview.PreviewPosition;

        // Actually place the building
        var building = Instantiate(_previewBuilding, finalPos, Quaternion.identity);

        // Clean up
        _overlay.UnregisterPreview("preview_building");
        Destroy(_previewBuilding);
    }
}
```

---

## Performance Tips

### 1. Visibility Culling
```csharp
var preview = overlay.GetPreview(nodeId);
if (preview != null)
{
    // Disable rendering without unregistering
    preview.IsVisible = false;
    // Re-enable later
    preview.IsVisible = true;
}
```

### 2. Batch Updates
```csharp
// Bad: Multiple GetPreview calls
foreach (var id in nodeIds)
{
    overlay.UpdatePreview(id, GetNewPos(id), GetNewRot(id), scale);
}

// Better: Direct access if you own the preview data
foreach (var id in nodeIds)
{
    overlay.UpdatePreview(id, positions[id], rotations[id], scale);
}
```

### 3. Clear Unused Previews
```csharp
// Good: Remove previews when no longer needed
overlay.UnregisterPreview(nodeId);

// Better for bulk clearing
overlay.ClearAllPreviews();
```

### 4. Mesh Caching
In your IOverlayRenderable implementation:
```csharp
private Mesh _cachedMesh;

public Mesh GetOverlayMesh()
{
    if (_cachedMesh == null)
    {
        _cachedMesh = GenerateMesh();  // Expensive operation once
    }
    return _cachedMesh;
}
```

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| Preview not visible | `IsVisible` false or no mesh | Check `preview.IsVisible` and verify mesh exists |
| Wrong position | Using local instead of world positions | Ensure using world coords in `RegisterPreview` |
| Preview overlaps objects | Rendering layer conflict | Adjust layer 30 in FuseOverlayRenderer |
| Performance drop | Too many active previews | Use visibility culling or clear unused previews |
| Mesh is null | No MeshFilter or IOverlayRenderable | Ensure object has MeshFilter or implement adapter |

---

## API Reference

### FuseOverlayManager (Singleton)

```csharp
// Lifecycle
FuseOverlayManager.Instance                    // Get singleton
.IsEnabled                                      // Enable/disable rendering

// Registration
.RegisterPreview(id, obj, pos, rot, scale, renderable?)    // Add preview
.UnregisterPreview(id)                          // Remove preview
.ClearAllPreviews()                             // Remove all

// Queries
.HasPreview(id)                                 // Check existence
.GetPreview(id)                                 // Get preview data
.GetActivePreviewCount()                        // Count previews
.GetActivePreviewIds()                          // List all IDs

// Updates
.UpdatePreview(id, pos, rot, scale)             // Update transform
.UpdatePreviewFromFuseNode(id, fuseNode)        // Update from FuseNode

// Events
.OnPreviewAdded                                 // Fired when registered
.OnPreviewRemoved                               // Fired when unregistered
.OnPreviewUpdated                               // Fired when updated
```

### OverlayPreviewData

```csharp
.OriginalObject                                 // Original game object
.PreviewPosition                                // Current preview position
.PreviewRotation                                // Current preview rotation
.PreviewScale                                   // Current preview scale
.IsVisible                                      // Render this preview?
.Tint                                           // Color tint (nullable)
.ObjectType                                     // User tag (string)
.ObjectId                                       // Unique ID

// Methods
.UpdatePreviewTransform(pos, rot, scale)        // Update all at once
.GetPreviewMatrix()                             // Get Matrix4x4
.GetOriginalMatrix()                            // Original transform matrix
```

### IOverlayRenderable (Interface)

```csharp
Mesh GetOverlayMesh()                           // Mesh to render
Material GetOverlayMaterial()                   // Material to use
Vector3 GetOriginalPosition()                   // Original world position
Quaternion GetOriginalRotation()                // Original rotation
Vector3 GetOriginalScale()                      // Original scale
Bounds GetObjectBounds()                        // For culling
```

---

## Files Overview

| File | Purpose |
|------|---------|
| `IOverlayRenderable.cs` | Interface for custom renderable objects |
| `OverlayPreviewData.cs` | Preview state data structure |
| `FuseOverlayRenderer.cs` | Core rendering engine |
| `FuseOverlayManager.cs` | Singleton manager & public API |
| `TrackNodeOverlayAdapter.cs` | Adapter for `TrackNode` |
| `TrackNodeOverlayExample.cs` | Simple usage example |
| `TrackNodeGizmoOverlayIntegration.cs` | Advanced gizmo + overlay pattern |
| `README.md` | System overview |
| `INTEGRATION_GUIDE.md` | This file |
