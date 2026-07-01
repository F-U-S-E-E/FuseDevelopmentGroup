# Dropdown System Implementation Guide

## Architecture Overview

The dropdown system is built as a separate, reusable component that can be integrated into any toolbar or UI region. It follows the same IMGUI patterns used throughout the FUSE editor.

## Component Separation

```
FuseEditorToolbarDropdown (Reusable)
├── Manages dropdown state
├── Handles rendering
├── Processes click events
└── Translates labels

FuseEditorScreen (Consumer)
├── Creates dropdown instances
├── Controls drawing layout
└── Manages integration with tools
```

## Drawing Pipeline

1. **FuseEditorScreen.OnGUI()** (main frame)
   - Draws toolbar via `_toolbar.Draw()`
   - Draws dropdowns via `DrawToolbarDropdowns()`

2. **FuseEditorIconToolbar.Draw()** 
   - Returns X coordinate of last drawn element
   - Enables dropdowns to position themselves to the right

3. **FuseEditorToolbarDropdown.Draw()**
   - Draws button background and text
   - Draws dropdown indicator (triangle)
   - Renders open menu if `_isOpen == true`
   - Handles button click to toggle menu

4. **DrawDropdownMenu()**
   - Calculates menu bounds
   - Clamps to screen edges
   - Draws semi-transparent background panel
   - Renders each option as a clickable button
   - Closes on outside click

## State Management

### Dropdown State
- `_selectedOptionId` - Currently selected option ID
- `_isOpen` - Whether the dropdown menu is visible
- `_openPosition` - Screen coordinates where menu should appear

### Option Data
Each option contains:
- `Id` - Unique identifier (e.g., "object", "group")
- `LabelKey` - Localization key for display text
- `OnSelected` - Optional callback invoked when selected

## Event Handling

### Click to Open
```csharp
if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
{
    _isOpen = !_isOpen;
    _openPosition = new Vector2(rect.x, rect.yMax);
}
```

### Menu Item Selection
```csharp
if (GUI.Button(itemRect, label, style))
{
    _selectedOptionId = option.Id;
    _isOpen = false;
    option.OnSelected?.Invoke();
}
```

### Close on Outside Click
```csharp
if (Event.current.type == EventType.MouseDown && !menuRect.Contains(Event.current.mousePosition))
{
    _isOpen = false;
}
```

## Layout Calculation

### Width Calculation
1. Measure all option labels using `GUIStyle.CalcSize()`
2. Find the maximum width
3. Add padding and indicator width
4. Ensure minimum width of 80px

### Position Calculation
```csharp
// Toolbar positioning
var originRect = new Rect(rect.x, rect.y + yPadding, dropdownWidth, dropdownHeight);
var transformRect = new Rect(originRect.xMax + dropdownSpacing, ...);

// Menu positioning (clamped to screen)
if (menuRect.yMax > screenHeight)
    menuRect.y = buttonRect.y - menuHeight;  // Open upward
if (menuRect.xMax > screenWidth)
    menuRect.x = screenWidth - menuRect.width;  // Clamp right edge
```

## Styling System

### Theme Integration
Dropdowns use theme styles created in `FuseEditorTheme.cs`:

1. **ToolbarDropdownLabel** - Text label styling
   - Font size: 12px
   - Color: TextPrimary
   - Alignment: MiddleLeft

2. **ToolbarDropdownItem** - Unselected menu items
   - Hover background: HighlightHover
   - Normal text: TextPrimary

3. **ToolbarDropdownItemActive** - Selected menu item
   - Background: HighlightSelected (orange)
   - Text: TextAccent (gold)
   - Font: Bold

### Lazy Initialization
Styles are created on-demand using the `Ensure()` pattern:
```csharp
public static GUIStyle ToolbarDropdownLabel 
    => Ensure(ref _toolbarDropdownLabel, CreateToolbarDropdownLabelStyle);
```

This defers style creation until the editor first renders.

## Integration with FuseEditorScreen

### Initialization (BuildToolbar)
```csharp
_toolOriginDropdown = new FuseEditorToolbarDropdown(
    id: "tool_origin",
    labelKey: "fuse.editor.toolbar.origin",
    options: new[]
    {
        new FuseEditorToolbarDropdown.Option("object", "fuse.editor.toolbar.origin.object"),
        new FuseEditorToolbarDropdown.Option("group", "fuse.editor.toolbar.origin.group")
    },
    initialSelectedId: "object");
```

### Rendering (OnGUI)
```csharp
var toolbarRect = new Rect(0f, MenuBarHeight, screenRect.width, ToolbarHeight);
float toolbarEndX = _toolbar.Draw(toolbarRect);  // Get end position

// Draw dropdowns immediately after toolbar
DrawToolbarDropdowns(new Rect(toolbarEndX + 12f, MenuBarHeight, ...));
```

### Helper Method (DrawToolbarDropdowns)
```csharp
private void DrawToolbarDropdowns(Rect rect)
{
    // Position first dropdown
    var originRect = new Rect(rect.x, rect.y + yPadding, dropdownWidth, dropdownHeight);
    _toolOriginDropdown.Draw(originRect);

    // Position second dropdown with spacing
    var transformRect = new Rect(originRect.xMax + dropdownSpacing, ...);
    _toolTransformDropdown.Draw(transformRect);
}
```

## Future Enhancement Points

The dropdown system is designed to be extended:

1. **Option Groups** - Group related options (e.g., "Basic" vs "Advanced")
2. **Icons** - Add small icons next to option labels
3. **Callbacks** - Execute actions when values change
4. **Persistence** - Save/load user preferences
5. **Keyboard Navigation** - Arrow keys to open/navigate
6. **Search** - Filter options in large dropdowns
7. **Multi-select** - Allow multiple selections (checkboxes)
8. **Custom Renderers** - Allow custom drawing for options

## Performance Considerations

- **Label Width Caching**: Could cache max label width to avoid recalculation every frame
- **GUIStyle Pooling**: Styles are cached in theme, not recreated per draw
- **Option List Copying**: Uses `new List<Option>()` in constructor to avoid external mutation
- **Screen Bounds**: Checks once per draw, not per option

## Common Usage Patterns

### Adding a Callback
```csharp
new FuseEditorToolbarDropdown.Option(
    id: "option_id",
    labelKey: "label.key",
    onSelected: () => { /* Handle selection */ })
```

### Checking Selected Value
```csharp
if (_toolOriginDropdown.SelectedOptionId == "group")
{
    // Apply group-centered transforms
}
```

### Changing Selection Programmatically
```csharp
_toolTransformDropdown.SelectedOptionId = "global";
```

### Open/Close State
```csharp
if (_toolOriginDropdown.IsOpen)
{
    // Menu is currently displayed
}
```

## Testing Considerations

- **Mock FuseEditorUiHelper** - Tests need label translation
- **Screen Bounds** - Use fixed screen size in tests
- **Event Handling** - Simulate click events via Event.current
- **Theme Initialization** - Ensure theme styles are created before tests
