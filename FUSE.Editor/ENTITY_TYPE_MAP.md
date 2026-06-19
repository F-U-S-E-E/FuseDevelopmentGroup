# FuseEditor Entity Type Mapping

## Overview

The `FuseEditor` class now includes a bidirectional mapping system between string entity type names (as used in the entity tree UI) and their corresponding Fuse data class types. This enables reflection-based operations, type-safe serialization helpers, and dynamic property editing.

## Entity Type Map

### Public API

```csharp
// Forward mapping: String name → Type
public static readonly IReadOnlyDictionary<string, Type> EntityTypeMap

// Reverse mapping: Type → String name
public static IReadOnlyDictionary<Type, string> EntityTypeReverseMap { get; }

// Helper methods
public static bool TryGetEntityType(string entityTypeName, out Type type)
public static bool TryGetEntityTypeName(Type type, out string entityTypeName)
```

### Supported Entity Types

#### Track Entities
- **"Node"** → `FuseNode`
- **"Segment"** → `FuseSegment`
- **"Span"** → `FuseSpan`
- **"Area"** → `FuseArea`

#### World Entities
- **"Scenery"** → `FuseScenery`
- **"Spliney"** → `FuseSpliney`
- **"MapLabel"** → `FuseMapLabel`
- **"Telegraph"** → `FuseTelegraphPoles`

#### Operations Entities
- **"Industry"** → `FuseIndustry`
- **"Load"** → `FuseLoad`
- **"Station"** → `FuseStation`
- **"Turntable"** → `FuseTurntable`
- **"Loader"** → `FuseLoader`

## Usage Examples

### Getting Type for Entity Name

```csharp
// Direct dictionary access
if (FuseEditor.EntityTypeMap.TryGetValue("Nodes", out Type nodeType))
{
    // nodeType == typeof(FuseNode)
    Console.WriteLine($"Node type: {nodeType.FullName}");
}

// Helper method
if (FuseEditor.TryGetEntityType("Segments", out Type segmentType))
{
    // segmentType == typeof(FuseSegment)
    var properties = segmentType.GetProperties();
    foreach (var prop in properties)
    {
        Console.WriteLine($"Property: {prop.Name} ({prop.PropertyType})");
    }
}
```

### Getting Entity Name for Type

```csharp
// Get entity name from type
var type = typeof(FuseIndustry);
if (FuseEditor.TryGetEntityTypeName(type, out string entityName))
{
    // entityName == "Industries"
    Console.WriteLine($"Entity type name: {entityName}");
}
```

### Reflection-Based Property Access

```csharp
// Generic property accessor based on entity type
public object GetEntityProperty(string entityTypeName, object entityInstance, string propertyName)
{
    if (!FuseEditor.TryGetEntityType(entityTypeName, out Type entityType))
    {
        throw new ArgumentException($"Unknown entity type: {entityTypeName}");
    }

    var property = entityType.GetProperty(propertyName);
    if (property == null)
    {
        throw new ArgumentException($"Property {propertyName} not found on {entityType.Name}");
    }

    return property.GetValue(entityInstance);
}

// Usage
var node = new FuseNode { Position = new FuseVector3(100, 0, 200) };
var position = GetEntityProperty("Nodes", node, "Position");
Console.WriteLine($"Node position: {position}");
```

### Multi-Selection Property Editing

```csharp
// Apply property change to multiple selected entities of the same type
public void ApplyPropertyToSelection(string entityTypeName, string propertyName, object value)
{
    if (!FuseEditor.TryGetEntityType(entityTypeName, out Type entityType))
    {
        return;
    }

    var property = entityType.GetProperty(propertyName);
    if (property == null || !property.CanWrite)
    {
        return;
    }

    var screen = FuseEditor.Instance?.Screen;
    if (screen == null)
    {
        return;
    }

    // Get all selected entities of this type
    for (int i = 0; i < screen.SelectionCount; i++)
    {
        if (screen.SelectedEntityKinds[i] == entityTypeName)
        {
            var entityId = screen.SelectedEntityIds[i];
            var entity = GetEntityById(entityTypeName, entityId);
            if (entity != null)
            {
                property.SetValue(entity, value);
            }
        }
    }
}
```

### Serialization Helpers

```csharp
// Generic serializer that uses the type map
public string SerializeEntity(string entityTypeName, string entityId, object entity)
{
    if (!FuseEditor.TryGetEntityType(entityTypeName, out Type entityType))
    {
        throw new ArgumentException($"Unknown entity type: {entityTypeName}");
    }

    // Validate that entity is of the correct type
    if (entity != null && !entityType.IsInstanceOfType(entity))
    {
        throw new ArgumentException(
            $"Entity is not of type {entityType.Name}. Got: {entity.GetType().Name}");
    }

    // Use type information for custom serialization
    return JsonConvert.SerializeObject(entity, Formatting.Indented);
}
```

### Dynamic Property Inspector

```csharp
// Build property inspector UI based on type
public void ShowPropertyInspector(string entityTypeName, object entity)
{
    if (!FuseEditor.TryGetEntityType(entityTypeName, out Type entityType))
    {
        return;
    }

    Console.WriteLine($"Properties of {entityTypeName}:");
    Console.WriteLine(new string('-', 50));

    foreach (var property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        var value = property.GetValue(entity);
        var valueStr = value?.ToString() ?? "<null>";
        Console.WriteLine($"{property.Name} ({property.PropertyType.Name}): {valueStr}");
    }
}
```

## Implementation Details

### Forward Mapping
The `EntityTypeMap` dictionary is initialized as a static readonly field, providing O(1) lookup from string names to types.

### Reverse Mapping
The `EntityTypeReverseMap` is built lazily on first access by inverting the forward map. This avoids startup overhead if the reverse map is never needed.

### Thread Safety
Both mappings are immutable after initialization and safe for concurrent read access. The lazy initialization of the reverse map uses a simple null check, which is safe because dictionary assignment is atomic in .NET.

## Integration Points

### Current Consumers
Currently used internally by `FuseEditor` for type resolution and reflection-based property access.

### Potential Future Uses
- **Generic Property Editors**: Build dynamic property panels based on type reflection
- **Serialization Framework**: Type-safe serialization/deserialization with validation
- **Multi-Entity Operations**: Bulk operations across selected entities of different types
- **Plugin System**: Allow external code to query and manipulate entities by type
- **Undo/Redo System**: Store type information with change records for proper restoration
- **Validation Framework**: Type-based rule validation across entity collections

## Notes

- All types in the map are from the `Fuse.Core.Model` namespace
- The mapping is complete for all entity types currently supported by the FUSE editor
- Entity type names match the bucket names used in `FuseEditorScreen.BuildSnapshot()`
- The reverse map is cached after first access for performance
