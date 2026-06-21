# FUSE Editor Overlay System

A lightweight, display-only preview system for visualizing uncommitted edits in the FUSE editor.

## Overview

The overlay system allows you to show previews (ghosts/wireframes) of objects with pending edits **without modifying the actual game objects**. This is useful for:

- Previewing node position/rotation changes before confirmation
- Showing building placement previews
- Visualizing Bezier curve control point changes
- Any object where you want to preview edits before committing them

**New in this version**: The overlay system now supports interactive selection. Users can click on overlay previews to trigger handler-defined selection callbacks, enabling editor integration.

## Architecture

### Core Components

#### **FuseOverlayRenderer**
Low-level renderer that manages preview data and draws them using `Graphics.DrawMesh()`.

- Stores preview data keyed by object ID
- Handles mesh/material lookup
- Renders previews in `OnPostRender()` (engine hook)
- Uses a dedicated rendering layer to avoid collider interference
- Manages the `OverlaySelectionSystem` for click interactions

#### **FuseOverlayManager**
Singleton MonoBehaviour that wraps the renderer and provides a convenient API.

- Manages lifecycle
- Prevents multiple instances
- Emits events when previews are added/updated/removed
- Can be enabled/disabled globally
- Provides `SelectionSystem` property for click handling

#### **OverlayPreviewData**
Data structure holding all preview state.

- Stores original object reference (not modified)
- Stores preview transform (position, rotation, scale)
- Supports custom IOverlayRenderable or falls back to mesh
- Can be tinted and toggled visible/invisible
- **New**: Stores selection areas and entity reference

#### **IOverlayRenderable**
Interface for objects that need custom overlay rendering.

- Provide custom mesh for the preview
- Provide custom material
- Return original transform values
- Return bounds for culling

#### **OverlaySelectionSystem** (NEW)
Manages click/raycast interactions with overlay previews.

- Performs raycasting against selection areas
- Tracks hover state for UI feedback
- Emits selection/hover events
- Dispatches clicks to entity handlers

#### **IOverlayHandler<T>** (Enhanced)
Generic handler for entity-specific overlay logic.

- **New**: `GetSelectionAreas()` method to define clickable regions
- **New**: `OnPreviewSelected()` callback when clicked

### Adapters

#### **TrackNodeOverlayAdapter**
Makes `TrackNode` compatible with the overlay system.

- Generates a simple sphere mesh for visualization
- Uses wireframe material by default
- Can be customized with your own material

## Usage

### Basic Workflow (Handler-Based)

```csharp
using FUSE.Editor.Overlays;

// Register handler for TrackNode
var handler = new TrackNodeOverlayHandler();
FuseOverlayManager.Instance.HandlerRegistry.RegisterHandler<TrackNode>(handler);

// Create a preview using the generic API
var node = /* your TrackNode */;
var previewData = FuseOverlayManager.Instance.ApplyPreview(node);

// To enable selection, set the selection camera
FuseOverlayManager.Instance.SetSelectionCamera(SceneView.lastActiveSceneView.camera);

// In your input handler/OnGUI:
if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
{
    FuseOverlayManager.Instance.TrySelectPreviewAtMouse(Event.current.mousePosition);
}
```

### Creating a Handler with Selection

```csharp
public class MyEntityHandler : IOverlayHandler<MyEntity>
{
    // ... existing methods ...

    public OverlaySelectionArea[] GetSelectionAreas(
        MyEntity entity,
        Vector3 previewPosition,
        Quaternion previewRotation,
        Vector3 previewScale)
    {
        var area = new OverlaySelectionArea
        {
            AreaId = $"entity_{entity.GetInstanceID()}",
            PreviewId = GetEntityId(entity),
            Bounds = new Bounds(Vector3.zero, Vector3.one * 2f),
            Transform = Matrix4x4.TRS(previewPosition, previewRotation, previewScale),
            IsSelectable = true,
            SelectionData = entity
        };

        return new[] { area };
    }

    public void OnPreviewSelected(MyEntity entity, OverlaySelectionArea selectionArea)
    {
        // Handle selection - update inspector, highlight, etc.
        FuseLog.Info($"Selected entity {entity.Name}");
    }
}
```

### Integration with FuseNode (Legacy)

```csharp
// If you have pending edits in a FuseNode object
var pendingEdits = new FuseNode
{
    Position = new Vector3(10, 5, 20),
    Rotation = new Vector3(0, 90, 0),
    FlipSwitchStand = true
};

// Update preview directly from FuseNode
overlay.UpdatePreviewFromFuseNode(nodeId, pendingEdits);
```

### Custom Rendering

Implement `IOverlayRenderable` for custom preview appearance:

```csharp
public class MyCustomOverlayAdapter : IOverlayRenderable
{
    private readonly GameObject _obj;
    private Material _customMaterial;

    public MyCustomOverlayAdapter(GameObject obj, Material material)
    {
        _obj = obj;
        _customMaterial = material;
    }

    public Mesh GetOverlayMesh()
    {
        // Return custom mesh, or use original
        var filter = _obj.GetComponent<MeshFilter>();
        return filter?.sharedMesh;
    }

    public Material GetOverlayMaterial()
    {
        // Return your custom overlay material
        return _customMaterial;
    }

    public Vector3 GetOriginalPosition() => _obj.transform.position;
    public Quaternion GetOriginalRotation() => _obj.transform.rotation;
    public Vector3 GetOriginalScale() => _obj.transform.lossyScale;
    public Bounds GetObjectBounds() => _obj.GetComponent<Renderer>()?.bounds ?? new Bounds();
}
```

### Events

```csharp
// Listen for overlay events
var overlay = FuseOverlayManager.Instance;

overlay.OnPreviewAdded += (id) =>
{
    Debug.Log($"Preview added for {id}");
};

overlay.OnPreviewUpdated += (id) =>
{
    Debug.Log($"Preview updated for {id}");
};

overlay.OnPreviewRemoved += (id) =>
{
    Debug.Log($"Preview removed for {id}");
};
```

### Query & Visibility

```csharp
var overlay = FuseOverlayManager.Instance;

// Check if a preview exists
if (overlay.HasPreview(nodeId))
{
    // Get the preview data
    var preview = overlay.GetPreview(nodeId);

    // Hide it temporarily
    preview.IsVisible = false;

    // Change its tint
    preview.Tint = Color.red;
}

// Get count of active previews
int count = overlay.GetActivePreviewCount();

// Get all preview IDs
foreach (var id in overlay.GetActivePreviewIds())
{
    Debug.Log($"Preview: {id}");
}

// Clear all previews at once
overlay.ClearAllPreviews();
```

### Enable/Disable Overlay System

```csharp
var overlay = FuseOverlayManager.Instance;

// Disable rendering (previews still registered, just not visible)
overlay.IsEnabled = false;

// Re-enable
overlay.IsEnabled = true;
```

## Integration Points

### With Gizmo System

If using the existing `FuseGizmoManager`, hook preview updates to gizmo callbacks:

```csharp
var gizmoManager = new FuseGizmoManager();
var overlay = FuseOverlayManager.Instance;

// Start move with preview
var handler = gizmoManager.BeginMove(targetObject, finalPosition =>
{
    // Gizmo completed - update preview then confirm
    overlay.UpdatePreview(nodeId, finalPosition, currentRotation, Vector3.one);
});

// Periodically check gizmo position and update preview in Update()
if (handler != null && handler.IsActive)
{
    var currentPos = /* get current gizmo position */;
    overlay.UpdatePreview(nodeId, currentPos, currentRotation, Vector3.one);
}
```

### With Node Marker Selection

```csharp
// In FuseNodeMarker or selection code
public void OnNodeSelected(TrackNode node)
{
    var overlay = FuseOverlayManager.Instance;
    var adapter = new TrackNodeOverlayAdapter(node);

    overlay.RegisterPreview(
        node.id,
        node.gameObject,
        node.transform.position,  // Start at current position
        node.transform.rotation,
        Vector3.one,
        adapter);
}

public void OnNodeDeselected(TrackNode node)
{
    FuseOverlayManager.Instance.UnregisterPreview(node.id);
}
```

## Design Decisions

### Display-Only Philosophy
- **No Position Changes**: The original object never moves. The overlay only shows where it *would* be.
- **Uncommitted Edits**: Previews reflect pending changes that haven't been applied yet.
- **Preview Confirmation**: User confirms edits, then you apply them to the actual object.

### Rendering Strategy
- Uses `Graphics.DrawMesh()` in `OnPostRender()` for efficient batch rendering.
- Dedicated rendering layer (30) to avoid collider and UI layer interference.
- Falls back to wireframe if no custom material provided.
- Supports tinting for visual feedback.

### Lifecycle
- Previews registered by ID (string key)
- Automatically deduplicated (re-registering replaces old preview)
- Previews cleared on UnregisterPreview() or ClearAllPreviews()
- Manager singleton with DontDestroyOnLoad

## Performance Considerations

- **Memory**: Each preview stores minimal data (object ref, 3 vectors, 1 quaternion).
- **CPU**: Graphics.DrawMesh() call per visible preview per frame (~0.1-0.3ms per 100 previews).
- **Mesh Caching**: Adapters can cache generated meshes (e.g., sphere in TrackNodeOverlayAdapter).
- **Visibility Culling**: Toggle preview.IsVisible to skip rendering.

## Limitations & Future Enhancements

### Current
- Renders using standard meshes/materials only (no custom shaders required, but possible).
- No built-in frustum culling (all rendered every frame).
- Single tint color per preview.

### Potential Enhancements
- Frustum culling by bounds
- Per-preview layer mask for selective rendering
- Outline/silhouette rendering mode
- Transparency fade for distance
- Gizmo icon/text labels
- Snap-to-grid visualization

## Troubleshooting

**Preview not visible?**
- Check `preview.IsVisible` is true
- Verify overlay manager is enabled: `FuseOverlayManager.Instance.IsEnabled`
- Check that mesh is valid (not null)
- Verify material is set or wireframe fallback is available

**Preview in wrong position?**
- Ensure you're updating with correct world position (not local)
- Check quaternion vs. euler angles conversion

**Preview rendering over UI?**
- Use layer 30 (OverlayLayer) which should be below UI layer
- Adjust layer in FuseOverlayRenderer if needed

**Performance issues?**
- Reduce number of active previews
- Use visibility culling for distant objects
- Cache meshes instead of generating each frame

## Files

- `IOverlayRenderable.cs` - Interface for custom renderable objects
- `OverlayPreviewData.cs` - Data structure for preview state
- `FuseOverlayRenderer.cs` - Core rendering system
- `FuseOverlayManager.cs` - Singleton manager & public API
- `TrackNodeOverlayAdapter.cs` - Adapter for TrackNode
- `TrackNodeOverlayExample.cs` - Complete usage example
- `README.md` - This file
