# FuseEditorScreen Selection API

## Overview

The `FuseEditorScreen` now supports multi-selection of entities through a list-based selection system. This allows users to select, add, and remove multiple entities of different types simultaneously.

## Architecture

### Storage
- **`_selectedEntityKinds`**: `List<string>` - Stores entity kinds in parallel with IDs
- **`_selectedEntityIds`**: `List<string>` - Stores entity IDs in parallel with kinds
- Parallel arrays ensure kind[i] always corresponds to id[i]

### Backward Compatibility
- **`_selectedEntityKind`**: Read-only property returning first selected kind or "Node"
- **`_selectedEntityId`**: Read-only property returning first selected ID or null
- Legacy code accessing these properties continues to work

## Public API

### Single Selection

```csharp
// Replace entire selection with one entity
void SetSelectedEntity(string entityKind, string entityId)

// Add one entity to current selection (no duplicates)
void AddToSelection(string entityKind, string entityId)

// Remove one entity from selection
bool RemoveFromSelection(string entityKind, string entityId)

// Toggle selection state of one entity
bool ToggleSelection(string entityKind, string entityId)
```

### Bulk Selection

```csharp
// Replace entire selection with multiple entities
void SetSelectedEntities(IList<string> entityKinds, IList<string> entityIds)

// Add multiple entities to current selection
void AddToSelection(IList<string> entityKinds, IList<string> entityIds)

// Remove multiple entities from selection
int RemoveFromSelection(IList<string> entityKinds, IList<string> entityIds)
```

### Query API

```csharp
// Check if an entity is selected
bool IsEntitySelected(string entityKind, string entityId)

// Get selection count
int SelectionCount { get; }

// Access selected entities (read-only)
IReadOnlyList<string> SelectedEntityKinds { get; }
IReadOnlyList<string> SelectedEntityIds { get; }

// Clear all selections
void ClearSelection()
```

## UI Behavior

### Entity Tree Row Clicks

- **Normal Click**: Replaces selection with clicked entity (`SetSelectedEntity`)
- **Ctrl+Click / Cmd+Click**: Toggles selection of clicked entity (`ToggleSelection`)
- **Shift+Click**: Adds clicked entity to selection (`AddToSelection`)

### Properties Panel

- **No Selection**: Shows empty hint message
- **Single Selection**: Shows full property editor for the entity
- **Multi-Selection**: Shows summary grouped by entity kind with "Clear Selection" button

## Usage Examples

```csharp
// Select multiple nodes at once
screen.SetSelectedEntities(
    new[] { "Node", "Node", "Node" },
    new[] { "node-001", "node-002", "node-003" }
);

// Add a segment to existing node selection
screen.AddToSelection("Segment", "segment-xyz");

// Check if a specific node is selected
if (screen.IsEntitySelected("Node", "node-001"))
{
    // Handle selected node
}

// Remove specific entities
screen.RemoveFromSelection(
    new[] { "Node", "Segment" },
    new[] { "node-001", "segment-xyz" }
);

// Clear entire selection
screen.ClearSelection();

// Iterate all selected entities
for (int i = 0; i < screen.SelectionCount; i++)
{
    var kind = screen.SelectedEntityKinds[i];
    var id = screen.SelectedEntityIds[i];
    Debug.Log($"Selected: {kind}/{id}");
}
```

## Integration Points

### External Consumers

- **`FuseNodeEditorController.SelectMarker`**: Uses `SetSelectedEntity` (single-click behavior)
- **Entity Tree**: Uses contextual selection methods based on modifier keys
- **Properties Panel**: Uses `SelectionCount` and read-only collections for display

### Future Enhancements

- Range selection (Shift+Click with proper range calculation)
- Rectangular marquee selection in viewport
- Selection filters (select all of type, invert selection, etc.)
- Bulk property editing for multi-selection
