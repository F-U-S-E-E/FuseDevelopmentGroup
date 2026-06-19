# Editable Properties Panel

## Overview

The FuseEditorPropertiesPanel now supports editing basic property types directly in the editor UI. When you select a single entity, its editable properties are displayed in the properties panel with input fields.

## Supported Property Types

- **String** — Text input field
- **Int** — Integer-only text field (validates input)
- **Float** — Decimal number input field (validates input)
- **Bool** — Boolean toggle
- **Vector2** — Separate X and Y axis input fields displayed on one row
- **Vector3** — Separate X, Y, and Z axis input fields displayed on one row
- **FuseVector3** — Same as Vector3 with separate axis inputs

Read-only properties are displayed as labels and cannot be edited.

## How It Works

1. **Property Discovery** — When you select an entity, the panel inspects its type using reflection
2. **Buffer Management** — Each editable property's value is stored in a text buffer
3. **Live Editing** — Changes are applied immediately to the entity instance
4. **Type Validation** — Input is validated against the property's expected type
5. **Error Handling** — Invalid input is silently rejected and the previous value is retained

## Usage

### Editing Properties

1. Select an entity in the editor (single selection only)
2. View its properties in the Properties panel on the right
3. Click on any editable field and type a new value
4. Changes are applied as you type (with validation)

### Vector Input

Vectors are displayed with separate input fields for each axis on a single row:

```
Position    X [1.5]  Y [2.3]  Z [3.1]
Velocity    X [-0.5] Y [0.0]  Z [1.2]
```

Each axis can be edited independently:
- Type a new value in the axis field
- Valid floating-point numbers are accepted
- Invalid input is silently rejected
- Changes are applied immediately to the entity

### Examples

**Vector2 editing:**
```
Scale       X [1.0]  Y [1.0]
```

**Vector3 editing:**
```
Position    X [0]    Y [10]   Z [5.2]
```

## Limitations

- **Single Selection Only** — Multi-selection doesn't support editing (shows "Bulk editing coming soon")
- **Direct Values Only** — Collections, nested objects, and complex types are not editable
- **No Undo/Redo** — Changes are applied directly without undo support (future enhancement)
- **No Validation UI** — Invalid input is silently rejected without visual feedback

## Technical Details

### Property Filtering

Properties are considered editable if they match these criteria:

```csharp
var propType = property.PropertyType;
return propType == typeof(string) ||
       propType == typeof(int) || 
       propType == typeof(float) ||
       propType == typeof(bool) ||
       propType == typeof(Vector3) ||
       propType == typeof(Vector2) ||
       propType == typeof(FuseVector3) ||
       !property.CanWrite; // Read-only properties are ok (shown as labels)
```

### Input Validation

- **String**: No validation, accepts any text
- **Int/Float**: Text must parse successfully using `int.TryParse()` / `float.TryParse()`
- **Bool**: Toggle button for boolean input
- **Vector2**: Each axis must be a valid float
- **Vector3/FuseVector3**: Each axis must be a valid float

### Vector Rendering

Vectors are rendered with separate axis fields:

1. Each axis (X, Y, Z) gets its own text input field
2. Fields are arranged horizontally on a single row
3. Changes to any axis are applied immediately
4. All three axes are updated when any single axis changes

### Property Application

When a valid value is entered:

1. For vector types, the individual axis values are read from their input fields
2. A new vector is constructed from the axis values
3. For **FuseVector3**, conversion from **Vector3** is performed
4. The updated vector is written via `PropertyInfo.SetValue()`
5. The entity is modified directly in memory

## Future Enhancements

- [ ] Undo/Redo support for property changes
- [ ] Visual feedback for invalid input (red highlight)
- [ ] Numeric spinner controls (up/down arrows for each axis)
- [ ] Enum type support
- [ ] Collections editing (limited UI)
- [ ] Change notification/persistence
- [ ] Multi-selection bulk editing
- [ ] Quaternion/Rotation support

## Example: Adding New Types

To add support for another property type:

1. Update `IsEditableProperty()` to include your type
2. For simple scalar types: add logic in `DrawEditablePropertyField()` and `ApplyPropertyChangeInternal()`
3. For complex types: create a new `DrawXxxField()` method similar to `DrawVector3Field()`
4. Add to the condition in `DrawEntityProperties()` to route to the appropriate renderer

```csharp
// In DrawEntityProperties:
else if (propType == typeof(MyType))
{
    DrawMyTypeField(new Rect(...), property, entity, property.Name, propType, y);
    y += RowHeight; // Adjust if your type uses multiple rows
}

// Create renderer:
private void DrawMyTypeField(Rect rect, PropertyInfo property, object entity, string propName, Type propType, float y)
{
    // Implementation similar to DrawVector3Field
}
```

## Debugging

If properties don't appear in the panel:

1. Check that the entity type is registered in `FuseEditor.EntityTypeMap`
2. Verify properties are public with a getter
3. Look for exceptions in the console (wrapped in try-catch)
4. Use `FuseLog` to enable detailed logging

If edits aren't being applied:

1. Verify the property has a setter (`CanWrite == true`)
2. Check that the input format is correct for the property type
3. Look for warnings in the console about failed property application

