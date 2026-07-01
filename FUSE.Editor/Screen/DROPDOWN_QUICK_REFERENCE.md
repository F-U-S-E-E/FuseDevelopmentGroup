# Toolbar Dropdowns - Quick Reference

## What Was Added

Two dropdown menus now appear in the FuseEditorScreen toolbar:

```
┌─────────────────────────────────────────────────────────────────────┐
│ File History Gizmo View    [Tool Origin▼]  [Tool Transform▼]       │ Toolbar
└─────────────────────────────────────────────────────────────────────┘
```

## Dropdowns

### Tool Origin
- **Purpose**: Choose between Object-centered or Group-centered transforms
- **Options**:
  - Object (default)
  - Group
- **Access**: `FuseEditorScreen._toolOriginDropdown.SelectedOptionId`

### Tool Transform  
- **Purpose**: Choose between Local or Global coordinate space
- **Options**:
  - Local (default)
  - Global
- **Access**: `FuseEditorScreen._toolTransformDropdown.SelectedOptionId`

## How to Use

### Get Current Selection
```csharp
var origin = _toolOriginDropdown.SelectedOptionId;      // "object" or "group"
var transform = _toolTransformDropdown.SelectedOptionId;  // "local" or "global"
```

### Set Selection Programmatically
```csharp
_toolOriginDropdown.SelectedOptionId = "group";
_toolTransformDropdown.SelectedOptionId = "global";
```

### Check if Dropdown is Open
```csharp
bool isOpen = _toolOriginDropdown.IsOpen;
```

## Files Created/Modified

### New Files
- `FUSE.Editor/Screen/UI/FuseEditorToolbarDropdown.cs` - Dropdown component
- `FUSE.Editor/Screen/TOOLBAR_DROPDOWNS.md` - Full documentation
- `FUSE.Editor/Screen/DROPDOWN_IMPLEMENTATION.md` - Implementation guide

### Modified Files
- `FUSE.Editor/Screen/FuseEditorScreen.cs` - Added dropdown fields, initialization, rendering
- `FUSE.Editor/Screen/UI/FuseEditorTheme.cs` - Added dropdown styles

## Localization Keys Needed

Add these to your localization system:

```
fuse.editor.toolbar.origin = "Origin"
fuse.editor.toolbar.origin.object = "Object"
fuse.editor.toolbar.origin.group = "Group"

fuse.editor.toolbar.transform = "Transform"  
fuse.editor.toolbar.transform.local = "Local"
fuse.editor.toolbar.transform.global = "Global"
```

## Integration Checklist

- [ ] Add localization keys to translation system
- [ ] Hook dropdown changes to gizmo behavior
- [ ] Update gizmo transform space based on `_toolTransformDropdown.SelectedOptionId`
- [ ] Update gizmo origin/pivot based on `_toolOriginDropdown.SelectedOptionId`
- [ ] (Optional) Add keyboard shortcuts for quick toggle
- [ ] (Optional) Persist user preference to EditorPrefs

## Visual Appearance

Each dropdown:
- Has a label showing the currently selected value
- Displays a small downward-pointing triangle indicator
- Styled with the EDEN dark theme colors
- Has hover and active states that match the rest of the toolbar
- Opens downward, but will flip upward near screen bottom

## Behavior

- **Click to open**: Opens menu showing all options
- **Click option**: Selects it and closes menu
- **Click outside**: Closes menu without selecting
- **Auto-sizing**: Width adjusts to fit longest option text

## Code Organization

```
FuseEditorScreen
├── _toolOriginDropdown      (FuseEditorToolbarDropdown instance)
├── _toolTransformDropdown   (FuseEditorToolbarDropdown instance)
├── BuildToolbar()           (Initialize dropdowns)
├── DrawToolbarDropdowns()   (Render dropdowns)
└── OnGUI()                  (Call drawing methods)

FuseEditorToolbarDropdown
├── Options[]                (Array of selectable options)
├── SelectedOptionId         (Current value)
├── IsOpen                   (Menu visibility)
└── Draw(rect)               (Render method)

FuseEditorTheme
├── ToolbarDropdownLabel     (Text style)
├── ToolbarDropdownItem      (Menu item style)
└── ToolbarDropdownItemActive (Selected item style)
```

## Performance Notes

- Dropdowns lazy-initialize styles (styles created on first use)
- No per-frame allocations after initialization
- Screen bounds clamping happens once per draw
- Event handling integrates with IMGUI event system

## Known Limitations

- No keyboard navigation (can be added)
- No search/filter in dropdown menu
- No icon support (can be added)
- Single-select only (multi-select can be added)

## Build Status

✅ Builds successfully  
✅ No compilation errors  
✅ Ready for integration testing
