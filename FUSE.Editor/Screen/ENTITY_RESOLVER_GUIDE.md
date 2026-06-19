# Entity Resolver Architecture - Implementation Guide

## Overview

The FUSE Editor Properties Panel now uses a **pluggable Entity Resolver pattern** that allows external mods to extend entity type support without modifying core FUSE code.

## Architecture

### Core Components

**1. `IEntityResolver` Interface** (`FUSE.Editor/Screen/IEntityResolver.cs`)
- Defines the contract for entity resolution
- Two methods:
  - `CanResolve(string entityKind)` - Quick check if resolver handles this entity kind
  - `TryResolveEntity(FuseLoadedMod mod, string entityKind, string entityId)` - Retrieves the entity instance

**2. `DefaultEntityResolver` Class** (`FUSE.Editor/Screen/DefaultEntityResolver.cs`)
- Handles all built-in FUSE entity types
- Automatically registered first (lowest priority)
- Uses switch expression for clean entity dispatch

**3. `FuseEditorPropertiesPanel` Class** (Updated)
- Maintains static list of resolvers
- Provides `RegisterEntityResolver(IEntityResolver)` public method
- Uses resolver chain to find and fetch entities

### Flow Diagram

```
Entity Selected (e.g., kind="CustomSignal", id="sig_001")
         ↓
FuseEditorPropertiesPanel.GetEntityInstance(mod, kind, id)
         ↓
Loop through _entityResolvers in order:
    ├─ DefaultEntityResolver.CanResolve("CustomSignal") → false
    ├─ CustomModResolver.CanResolve("CustomSignal") → true ✓
    └─ CustomModResolver.TryResolveEntity(mod, kind, id) → entity instance
         ↓
Entity found! Display properties automatically
```

## How to Extend

### Step 1: Create Your Resolver

```csharp
public class MyModEntityResolver : IEntityResolver
{
    public bool CanResolve(string entityKind)
    {
        return entityKind is "MyCustomType" or "MyOtherType";
    }

    public object TryResolveEntity(FuseLoadedMod mod, string entityKind, string entityId)
    {
        if (mod?.Definition == null) return null;

        return entityKind switch
        {
            "MyCustomType" => GetCustomEntity(mod, entityId),
            "MyOtherType" => GetOtherEntity(mod, entityId),
            _ => null
        };
    }
}
```

### Step 2: Register During Mod Load

```csharp
public class MyModLoader
{
    public static void OnModLoad()
    {
        FuseEditorPropertiesPanel.RegisterEntityResolver(
            new MyModEntityResolver()
        );
    }
}
```

### Step 3: Use in Your Entity Tree

```csharp
// In your entity tree selection code:
fuseEditorScreen.SetSelectedEntity("MyCustomType", customEntityId);

// The properties panel will automatically:
// 1. Try DefaultEntityResolver (CanResolve returns false)
// 2. Try MyModEntityResolver (CanResolve returns true)
// 3. Call TryResolveEntity to get your entity
// 4. Reflect over properties and display them
```

## Key Benefits

✅ **Open/Closed Principle** - Open for extension, closed for modification
✅ **No Core Changes Required** - External mods work independently
✅ **Priority Order** - Resolvers tried in registration order
✅ **Type Safe** - Built-in type checking, no string-based lookups
✅ **Extensible** - Multiple resolvers can coexist
✅ **Testable** - Each resolver can be tested independently

## Best Practices

1. **Make CanResolve Fast**
   - Use simple string comparisons (is operator works great)
   - Avoid expensive lookups or calculations
   - This gets called frequently

2. **One Resolver Per Cohesive Group**
   - Group related entity types in one resolver
   - Multiple resolvers are fine, keep each focused

3. **Return null, Don't Throw**
   ```csharp
   // Good:
   return collection?.GetValueOrDefault(id);

   // Bad:
   if (!collection.ContainsKey(id)) throw new Exception("Not found");
   ```

4. **Document Your Entity Kinds**
   - Clearly comment which entity kinds your resolver handles
   - Makes it easy for tool developers to know what's available

5. **Consider Null Mod Definitions**
   - Always check `mod?.Definition` before access
   - Handle the case where entity isn't found gracefully

## Internal Changes

### Modified Files

**`FuseEditorPropertiesPanel.cs`**
- Added static `_entityResolvers` list
- Added public `RegisterEntityResolver(IEntityResolver)` method
- Simplified `GetEntityInstance()` to use resolver chain
- Removed large switch statement (moved to DefaultEntityResolver)

### New Files

**`IEntityResolver.cs`** - Public interface for custom resolvers
**`DefaultEntityResolver.cs`** - Handles all built-in FUSE types (extracted from properties panel)
**`EntityResolverExample.cs`** - Comprehensive example and documentation

## Testing Your Resolver

```csharp
[TestClass]
public class MyModEntityResolverTests
{
    private MyModEntityResolver _resolver;
    private FuseLoadedMod _testMod;

    [TestInitialize]
    public void Setup()
    {
        _resolver = new MyModEntityResolver();
        // Setup test mod with test definitions
    }

    [TestMethod]
    public void CanResolve_MyCustomType_ReturnsTrue()
    {
        Assert.IsTrue(_resolver.CanResolve("MyCustomType"));
    }

    [TestMethod]
    public void TryResolveEntity_ValidEntity_ReturnsInstance()
    {
        var entity = _resolver.TryResolveEntity(_testMod, "MyCustomType", "test_id");
        Assert.IsNotNull(entity);
    }

    [TestMethod]
    public void TryResolveEntity_InvalidEntity_ReturnsNull()
    {
        var entity = _resolver.TryResolveEntity(_testMod, "MyCustomType", "missing_id");
        Assert.IsNull(entity);
    }
}
```

## Future Enhancements

- **Priority-Based Resolution**: Assign priorities to resolvers (low/medium/high)
- **Caching**: Cache resolver lookups for frequently accessed entities
- **Type-Based Filtering**: Pre-filter resolvers by entity type categories
- **Unregister Support**: Allow unregistering resolvers (useful for hot-reload scenarios)
