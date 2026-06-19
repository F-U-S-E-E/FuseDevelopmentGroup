# Properties Panel Layout - Full Width Input Fields

## Overview

The properties panel now stretches all value input fields to fill the maximum available width in the window, providing a more spacious and modern UI.

## Layout Changes

### Scalar Property Fields (String, Int, Float, Bool)

**Before:**
```
Label (96px)  |  Input Field (constrained)
```

**After:**
```
Label (96px)  |  Input Field (fills entire remaining width)
```

The scalar property input fields now stretch to fill all available width minus the label and right padding.

### Vector2 Property Fields

**Before:**
```
Label  X [field1]  Y [field2]
       (constrained widths)
```

**After:**
```
Label  X [-----field1-----]  Y [-----field2-----]
       (equal distribution across full width)
```

- X and Y axis fields are equally distributed
- Together they fill the entire width available after the property label
- Each occupies: `(availableWidth - axisLabels - spacing) / 2`

### Vector3 Property Fields

**Before:**
```
Label  X [field]  Y [field]  Z [field]
       (constrained widths)
```

**After:**
```
Label  X [-----field-----]  Y [-----field-----]  Z [-----field-----]
       (equal distribution across full width)
```

- X, Y, and Z axis fields are equally distributed
- Together they fill the entire width available after the property label
- Each occupies: `(availableWidth - axisLabels - spacing) / 2 / 3`

## Technical Implementation

### Key Changes

1. **Scalar Fields** — `inputRect` width changed from `rect.width - LabelWidth` to `rect.width - LabelWidth - Padding`
   - This adds right padding and ensures fields don't extend beyond the panel boundary

2. **Vector Fields** — Responsive width calculation:
   ```csharp
   float availableWidth = rect.width - LabelWidth - spacing;
   float fieldWidth = (availableWidth - axisLabelWidth * numAxes - spacing * (numAxes - 1)) / numAxes;
   ```
   - Calculates total available width after label
   - Subtracts space needed for axis labels (X, Y, Z)
   - Subtracts spaces between fields
   - Divides remaining width equally among axis fields

3. **Positioning** — All axis layout uses calculated `fieldWidth`:
   - Positions are computed from left to right with consistent spacing
   - Each field uses the same calculated width
   - Fields are separated by `Padding` constant (6px)

## Benefits

✅ **Better Use of Screen Space** — No wasted dead space to the right of input fields
✅ **Consistent Proportions** — Fields scale with window size (parent container)
✅ **Cleaner Appearance** — Uniform distribution across all property types
✅ **Vector Field Alignment** — Axis fields are clearly organized and evenly spaced
✅ **Responsive** — Works with any panel width and resizing

## Layout Constants

```csharp
private const float RowHeight = 22f;      // Height of each property row
private const float LabelWidth = 96f;     // Width reserved for property label
private const float Padding = 6f;         // Space between elements

// Axis labels (in vector fields)
const float axisLabelWidth = 16f;         // Width of "X", "Y", "Z" label
```

## Example Calculations

### Scalar Field with 400px window width:
```
Total rect.width: 400px
- Label width:   96px
- Padding:       6px
= Input field:   298px available
```

### Vector3 Field with 400px window width:
```
Total rect.width: 400px
Available after label & padding: 400 - 96 - 6 = 298px
- 3 axis labels (16px each):    48px
- 2 gaps between fields:        12px (6 + 6)
= Per-field width:  (298 - 48 - 12) / 3 = 79.3px each
```

Result: `X [-----79px-----]  Y [-----79px-----]  Z [-----79px-----]`

## Future Enhancements

- [ ] Adjustable label width based on longest property name
- [ ] Configurable spacing/padding preferences
- [ ] Dynamic column layout for many properties
- [ ] Property grouping/sections with resizable dividers
- [ ] Horizontal scrolling for very long property names
