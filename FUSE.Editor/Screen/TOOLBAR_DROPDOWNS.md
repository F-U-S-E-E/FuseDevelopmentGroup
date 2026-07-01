# FuseEditorScreen Toolbar Dropdowns

## Overview
Two dropdown menus have been added to the FuseEditorScreen toolbar on the right side of the icon toolbar:
1. **Tool Origin** - Selects between "Object" and "Group" 
2. **Tool Transform** - Selects between "Local" and "Global"

## Components Added

### 1. FuseEditorToolbarDropdown.cs
**Location**: `FUSE.Editor/Screen/UI/FuseEditorToolbarDropdown.cs`

A reusable dropdown component for the toolbar.

**Key Classes**:
- `FuseEditorToolbarDropdown` - Main dropdown control
- `Option` - Represents a selectable option in the dropdown

**Features**:
- Displays currently selected option
- Opens/closes menu on button click
- Displays chevron indicator showing dropdown is interactive
- Automatically clamps menu to screen bounds
- Supports custom option callbacks via `OnSelected` action
- Translatable labels using `FuseEditorUiHelper.TranslateLabel()`

**Public API**:
```csharp
// Constructor
new FuseEditorToolbarDropdown(
    id: "dropdown_id",
    labelKey: "localization.key",
    options: new[]
    {
        new FuseEditorToolbarDropdown.Option("id", "label.key", onSelectedCallback)
    },
    initialSelectedId: "id")

// Properties
string SelectedOptionId { get; set; }
bool IsOpen { get; }

// Methods
float Draw(Rect rect)  // Returns X coordinate where drawing ended
```

### 2. FuseEditorTheme Updates
Added three new dropdown-related styles to `FuseEditorTheme.cs`:

**New Styles**:
- `ToolbarDropdownLabel` - Text styling for the selected label
- `ToolbarDropdownItem` - Regular menu item styling
- `ToolbarDropdownItemActive` - Highlighting for selected item in menu

**Style Management**:
- Lazy-initialized on first access via `Ensure()`
- Reset/destroyed in the `Reset()` method for testing
- Included in `EnsureCreated()` for upfront initialization

### 3. FuseEditorScreen Updates
**Location**: `FUSE.Editor/Screen/FuseEditorScreen.cs`

**New Fields**:
```csharp
private FuseEditorToolbarDropdown _toolOriginDropdown;
private FuseEditorToolbarDropdown _toolTransformDropdown;
```

**Modified Methods**:
- `BuildToolbar()` - Now initializes both dropdowns after creating the toolbar
- `OnGUI()` - Calls `DrawToolbarDropdowns()` to render dropdowns in the toolbar area
- `DrawToolbarDropdowns()` - New helper method that renders both dropdowns side-by-side

**Dropdown Configuration**:

**Tool Origin Dropdown**:
- ID: `"tool_origin"`
- Label Key: `"fuse.editor.toolbar.origin"`
- Options: 
  - `"object"` → `"fuse.editor.toolbar.origin.object"`
  - `"group"` → `"fuse.editor.toolbar.origin.group"`
- Default: `"object"`

**Tool Transform Dropdown**:
- ID: `"tool_transform"`
- Label Key: `"fuse.editor.toolbar.transform"`
- Options:
  - `"local"` → `"fuse.editor.toolbar.transform.local"`
  - `"global"` → `"fuse.editor.toolbar.transform.global"`
- Default: `"local"`

## Layout

The dropdowns appear in the toolbar to the right of the icon groups:

```
[File | History | Gizmo | View]  [Tool Origin▼] [Tool Transform▼]
```

Each dropdown:
- Has a minimum width of 80px
- Automatically sizes to fit its longest option text
- Maintains 6px spacing between dropdowns
- Vertically centered in the toolbar (32px height)

## Localization

The following localization keys need to be added to your localization system:

```
fuse.editor.toolbar.origin = "Origin"
fuse.editor.toolbar.origin.object = "Object"
fuse.editor.toolbar.origin.group = "Group"

fuse.editor.toolbar.transform = "Transform"
fuse.editor.toolbar.transform.local = "Local"
fuse.editor.toolbar.transform.global = "Global"
```

## Usage

Access the selected values at runtime:

```csharp
// Get current dropdown selections from FuseEditorScreen
string origin = _toolOriginDropdown.SelectedOptionId;  // "object" or "group"
string transform = _toolTransformDropdown.SelectedOptionId;  // "local" or "global"

// Change selection programmatically
_toolOriginDropdown.SelectedOptionId = "group";
_toolTransformDropdown.SelectedOptionId = "global";
```

## Styling

The dropdowns inherit styling from the theme palette:
- **Background**: `Palette.BackgroundPrimary` (toolbar button background)
- **Text**: `Palette.TextPrimary` (normal), `Palette.TextAccent` (selected)
- **Hover State**: `Palette.HighlightHover` (subtle highlight)
- **Active State**: `Palette.HighlightSelected` (orange selection band)

## Integration Points

To fully integrate the dropdowns with actual tool behavior:

1. **Hook into selection changes**: Listen to `SelectedOptionId` property changes
2. **Apply tool settings**: When a dropdown changes, update gizmo behavior:
   - Origin affects whether transformations are relative to object or group center
   - Transform affects whether gizmo uses local or global coordinate space
3. **Persist settings**: Save dropdown selections to user preferences

## Build Status
✅ All changes compile successfully with no errors.
