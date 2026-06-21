# Overlay Selection Integration Guide

## Quick Start: Adding Selection to Your Editor Tool

This guide shows how to integrate the new overlay selection system into an existing editor window or tool.

## Step 1: Register Your Handler (Once)

```csharp
using FUSE.Editor.Overlays;
using FUSE.Editor.Track.Overlays;
using UnityEditor;

public class MyEditorWindow : EditorWindow
{
    private void OnEnable()
    {
        // Register the handler once when the window opens
        var handler = new TrackNodeOverlayHandler();
        FuseOverlayManager.Instance.HandlerRegistry.RegisterHandler<TrackNode>(handler);

        // Set the selection camera for raycasting
        if (SceneView.lastActiveSceneView != null)
        {
            FuseOverlayManager.Instance.SetSelectionCamera(SceneView.lastActiveSceneView.camera);
        }
    }
}
```

## Step 2: Add Selection Input Handling

### Option A: In SceneGUI

```csharp
private void OnSceneGUI(SceneView sceneView)
{
    HandleSceneInput();
}

private void HandleSceneInput()
{
    Event evt = Event.current;

    if (evt.type == EventType.MouseDown && evt.button == 0)
    {
        // Try to select a preview
        if (FuseOverlayManager.Instance.TrySelectPreviewAtMouse(evt.mousePosition))
        {
            // Selection was handled by the overlay system
            evt.Use();
        }
    }
}
```

### Option B: In EditorWindow OnGUI

```csharp
public class MyEditorWindow : EditorWindow
{
    private void OnGUI()
    {
        // Your UI code here...

        HandleWindowInput();
    }

    private void HandleWindowInput()
    {
        Event evt = Event.current;

        if (evt.type == EventType.MouseDown && evt.button == 0)
        {
            var mousePos = evt.mousePosition;

            if (FuseOverlayManager.Instance.TrySelectPreviewAtMouse(mousePos))
            {
                evt.Use();
            }
        }
    }
}
```

## Step 3: Create Previews When Editing

```csharp
private TrackNode _editingNode;
private string _currentPreviewId;

public void BeginEditingNode(TrackNode node)
{
    _editingNode = node;

    // Create an overlay preview of the node
    var preview = FuseOverlayManager.Instance.ApplyPreview(node);
    if (preview != null)
    {
        _currentPreviewId = preview.PreviewId;
        preview.Tint = Color.yellow; // Show it in a different color
    }
}

public void UpdateNodePreview(Vector3 newPosition)
{
    if (_editingNode != null && !string.IsNullOrEmpty(_currentPreviewId))
    {
        // Move the preview as the user edits
        FuseOverlayManager.Instance.UpdatePreviewFromEntity(_currentPreviewId, _editingNode);
    }
}

public void ConfirmEdit()
{
    if (_editingNode != null && !string.IsNullOrEmpty(_currentPreviewId))
    {
        // Apply the preview position to the actual node
        _editingNode.transform.position = _editingNode.transform.position; // Use your edit value

        // Remove the preview
        FuseOverlayManager.Instance.UnregisterPreview(_currentPreviewId);
        _editingNode = null;
        _currentPreviewId = null;
    }
}
```

## Step 4: Handle Selection Callbacks (In Your Handler)

```csharp
public class TrackNodeOverlayHandler : IOverlayHandler<TrackNode>
{
    // ... existing methods ...

    public void OnPreviewSelected(TrackNode entity, OverlaySelectionArea selectionArea)
    {
        // This is called when a user clicks on the preview

        // Example: Select the node in the scene
        FuseLog.Info($"TrackNode selected: {entity.gameObject.name}");

        // Example: Update your editor UI
        // YourEditorWindow.SelectNode(entity);

        // Example: Perform an action
        // BeginEditingNode(entity);
    }
}
```

## Complete Example: TrackNode Editor Window

```csharp
using FUSE.Editor.Overlays;
using FUSE.Editor.Track.Overlays;
using UnityEditor;
using UnityEngine;
using Track;

public class TrackNodeEditorWindow : EditorWindow
{
    private TrackNode _selectedNode;
    private string _currentPreviewId;
    private Vector3 _previewPosition;

    [MenuItem("FUSE/Track Node Editor")]
    public static void ShowWindow()
    {
        GetWindow<TrackNodeEditorWindow>("Track Node Editor");
    }

    private void OnEnable()
    {
        // Setup overlay selection system
        var handler = new TrackNodeOverlayHandler();
        FuseOverlayManager.Instance.HandlerRegistry.RegisterHandler<TrackNode>(handler);

        // Subscribe to selection events
        FuseOverlayManager.Instance.SelectionSystem.OnPreviewSelectionChanged += OnPreviewSelected;
        FuseOverlayManager.Instance.SelectionSystem.OnPreviewHovered += OnPreviewHovered;
        FuseOverlayManager.Instance.SelectionSystem.OnPreviewUnhovered += OnPreviewUnhovered;
    }

    private void OnDisable()
    {
        // Cleanup
        FuseOverlayManager.Instance.SelectionSystem.OnPreviewSelectionChanged -= OnPreviewSelected;
        FuseOverlayManager.Instance.SelectionSystem.OnPreviewHovered -= OnPreviewHovered;
        FuseOverlayManager.Instance.SelectionSystem.OnPreviewUnhovered -= OnPreviewUnhovered;

        // Clear any active preview
        if (!string.IsNullOrEmpty(_currentPreviewId))
        {
            FuseOverlayManager.Instance.UnregisterPreview(_currentPreviewId);
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("Track Node Editor", EditorStyles.boldLabel);

        if (_selectedNode == null)
        {
            GUILayout.Label("Click a node in the scene to select it", EditorStyles.helpBox);
        }
        else
        {
            GUILayout.Label($"Editing: {_selectedNode.gameObject.name}");

            // Position editing
            _previewPosition = EditorGUILayout.Vector3Field("Position", _previewPosition);

            if (GUILayout.Button("Apply"))
            {
                _selectedNode.transform.position = _previewPosition;
                FuseOverlayManager.Instance.UnregisterPreview(_currentPreviewId);
                _selectedNode = null;
            }

            if (GUILayout.Button("Cancel"))
            {
                FuseOverlayManager.Instance.UnregisterPreview(_currentPreviewId);
                _selectedNode = null;
            }
        }
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        // Setup camera for selection
        FuseOverlayManager.Instance.SetSelectionCamera(sceneView.camera);

        // Handle input
        Event evt = Event.current;
        if (evt.type == EventType.MouseDown && evt.button == 0)
        {
            if (FuseOverlayManager.Instance.TrySelectPreviewAtMouse(evt.mousePosition))
            {
                evt.Use();
            }
        }

        // Update hover state
        if (evt.type == EventType.MouseMove)
        {
            FuseOverlayManager.Instance.SelectionSystem.UpdateHoverFromMouse(evt.mousePosition);
        }
    }

    private void OnPreviewSelected(string previewId, OverlaySelectionArea area)
    {
        // Get the preview data
        var preview = FuseOverlayManager.Instance.GetRenderer().GetPreview(previewId);
        if (preview?.Entity is TrackNode node)
        {
            _selectedNode = node;
            _currentPreviewId = previewId;
            _previewPosition = node.transform.position;

            // Visual feedback
            preview.Tint = Color.cyan;

            Repaint();
        }
    }

    private void OnPreviewHovered(string previewId, OverlaySelectionArea area)
    {
        // Optional: Change cursor or highlight
        EditorGUIUtility.AddCursorRect(new Rect(0, 0, Screen.width, Screen.height), MouseCursor.Link);
    }

    private void OnPreviewUnhovered()
    {
        // Optional: Reset cursor
    }
}
```

## Integration Patterns

### Pattern 1: Direct Click-to-Edit

```csharp
public void OnPreviewSelected(TrackNode entity, OverlaySelectionArea area)
{
    // Begin editing immediately on click
    BeginEditingNode(entity);
}
```

### Pattern 2: Click-to-Highlight

```csharp
public void OnPreviewSelected(TrackNode entity, OverlaySelectionArea area)
{
    // Just select/highlight, don't edit
    Selection.activeGameObject = entity.gameObject;
}
```

### Pattern 3: Click-to-MultiSelect

```csharp
private HashSet<TrackNode> _selectedNodes = new();

public void OnPreviewSelected(TrackNode entity, OverlaySelectionArea area)
{
    bool alreadySelected = _selectedNodes.Contains(entity);
    bool isMultiSelect = Event.current.control || Event.current.command;

    if (!isMultiSelect && !alreadySelected)
    {
        _selectedNodes.Clear();
    }

    if (_selectedNodes.Contains(entity))
    {
        _selectedNodes.Remove(entity);
    }
    else
    {
        _selectedNodes.Add(entity);
    }
}
```

### Pattern 4: Click-to-ShowContextMenu

```csharp
public void OnPreviewSelected(TrackNode entity, OverlaySelectionArea area)
{
    var menu = new GenericMenu();
    menu.AddItem(new GUIContent("Edit"), false, () => BeginEditingNode(entity));
    menu.AddItem(new GUIContent("Delete"), false, () => DeleteNode(entity));
    menu.AddSeparator("");
    menu.AddItem(new GUIContent("Properties"), false, () => ShowPropertiesWindow(entity));
    menu.ShowAsContext();
}
```

## Performance Tips

1. **Cache the Handler**: Store the handler reference instead of looking it up every frame
2. **Limit Selection Areas**: Prefer fewer, larger areas over many tiny ones
3. **Update Only on Changes**: Don't call `UpdatePreviewFromEntity()` every frame if nothing changed
4. **Clear Previews**: Remove previews when done editing to avoid raycasting overhead
5. **Use Visibility**: Set `preview.IsVisible = false` instead of unregistering if you might reuse it

## Troubleshooting

### Selection not working

**Symptom**: Clicking on previews doesn't trigger callbacks

**Solutions**:
1. Did you call `SetSelectionCamera()`?
2. Is the preview visible? Check `preview.IsVisible`
3. Does the handler return selection areas? Check `GetSelectionAreas()`
4. Are you calling `TrySelectPreviewAtMouse()` with the correct mouse position?

### Raycast hitting wrong preview

**Symptom**: Clicking on one preview selects a different one

**Solutions**:
1. Check selection area bounds - they may overlap
2. Verify transform matrices are correct
3. Selection uses closest distance - farther previews won't be hit

### Handler callback not invoked

**Symptom**: `OnPreviewSelected()` is never called

**Solutions**:
1. Is the handler registered? Use `HandlerRegistry.HasHandler<T>()`
2. Does the entity match the handler type?
3. Is the camera set for raycasting?
4. Check console for error messages

## See Also

- `SELECTION_SYSTEM.md` - Detailed selection system documentation
- `README.md` - Main overlay system documentation
- `QUICK_REFERENCE.md` - Quick API reference
