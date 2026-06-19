# Entity Resolver Quick Reference

## Minimal Implementation (Copy & Paste Template)

```csharp
using FUSE.Editor.Screen;
using FUSE.Loading;

// 1. Create your resolver
public class YourModEntityResolver : IEntityResolver
{
    public bool CanResolve(string entityKind)
    {
        // Return true if you handle this entity kind
        return entityKind is "YourEntityType1" or "YourEntityType2";
    }

    public object TryResolveEntity(FuseLoadedMod mod, string entityKind, string entityId)
    {
        if (mod?.Definition == null) return null;

        // Return your entity instance, or null if not found
        return entityKind switch
        {
            "YourEntityType1" => mod.Definition.YourCollection1?.GetValueOrDefault(entityId),
            "YourEntityType2" => mod.Definition.YourCollection2?.GetValueOrDefault(entityId),
            _ => null
        };
    }
}

// 2. Register during mod load
public class YourModInitializer
{
    public static void Initialize()
    {
        FuseEditorPropertiesPanel.RegisterEntityResolver(new YourModEntityResolver());
    }
}
```

## Key Points

✓ CanResolve() is called frequently - keep it FAST
✓ TryResolveEntity() should return null for "not found", never throw
✓ Multiple resolvers can coexist without conflicts
✓ Resolvers are tried in registration order (DefaultResolver always first)
✓ External mods don't need to modify FUSE core code

## Common Pattern: Multiple Entity Types

```csharp
public bool CanResolve(string entityKind)
{
    return entityKind is "TrackSignal" or "RailSignal" or "WarningSign";
}

public object TryResolveEntity(FuseLoadedMod mod, string entityKind, string entityId)
{
    if (mod?.Definition == null)
        return null;

    var customDef = mod.Definition as YourModDefinition;
    if (customDef == null)
        return null;

    return entityKind switch
    {
        "TrackSignal" => customDef.TrackSignals?.GetValueOrDefault(entityId),
        "RailSignal" => customDef.RailSignals?.GetValueOrDefault(entityId),
        "WarningSign" => customDef.WarningSignsMap?.GetValueOrDefault(entityId),
        _ => null
    };
}
```

## Pattern: Multiple Separate Resolvers

```csharp
public void Initialize()
{
    // Register one resolver per cohesive group of entity types
    FuseEditorPropertiesPanel.RegisterEntityResolver(new TrackObjectResolver());
    FuseEditorPropertiesPanel.RegisterEntityResolver(new SignalResolver());
    FuseEditorPropertiesPanel.RegisterEntityResolver(new IndustryResolver());
}
```

## Pattern: Generic Resolver with Handler Map

```csharp
public class GenericCollectionResolver : IEntityResolver
{
    private readonly Dictionary<string, Func<FuseLoadedMod, string, object>> _handlers;

    public GenericCollectionResolver()
    {
        _handlers = new()
        {
            { "Type1", (mod, id) => GetType1(mod, id) },
            { "Type2", (mod, id) => GetType2(mod, id) },
        };
    }

    public bool CanResolve(string entityKind) => _handlers.ContainsKey(entityKind);

    public object TryResolveEntity(FuseLoadedMod mod, string entityKind, string entityId)
    {
        return _handlers.TryGetValue(entityKind, out var handler)
            ? handler(mod, entityId)
            : null;
    }

    private object GetType1(FuseLoadedMod mod, string id) => 
        (mod.Definition as YourModDef)?.Collection1?.GetValueOrDefault(id);

    private object GetType2(FuseLoadedMod mod, string id) => 
        (mod.Definition as YourModDef)?.Collection2?.GetValueOrDefault(id);
}
```

## Debugging Tips

**Q: Properties panel shows "Unknown entity type"**
A: Your resolver's CanResolve() returned false, or TryResolveEntity() returned null. 
   Check the entity kind string matches exactly (case-sensitive).

**Q: Entity is found but properties don't display**
A: The entity's properties might be private or complex types. 
   Check IsEditableProperty() filter in the panel.

**Q: Multiple resolver conflict**
A: Make sure CanResolve() returns false for entity kinds you don't handle.
   If two resolvers handle the same kind, the first registered wins.

**Q: Resolver registration isn't working**
A: Call RegisterEntityResolver() during mod initialization, 
   before the editor panel is drawn.

## Testing Your Resolver

```csharp
[TestClass]
public class YourModEntityResolverTests
{
    private YourModEntityResolver _resolver;
    private FuseLoadedMod _testMod;

    [TestInitialize]
    public void Setup()
    {
        _resolver = new YourModEntityResolver();
        // Setup test mod with test definitions
    }

    [TestMethod]
    public void CanResolve_YourType_ReturnsTrue()
    {
        Assert.IsTrue(_resolver.CanResolve("YourEntityType1"));
    }

    [TestMethod]
    public void TryResolveEntity_ValidEntity_ReturnsInstance()
    {
        var entity = _resolver.TryResolveEntity(_testMod, "YourEntityType1", "test_id");
        Assert.IsNotNull(entity);
    }

    [TestMethod]
    public void TryResolveEntity_InvalidEntity_ReturnsNull()
    {
        var entity = _resolver.TryResolveEntity(_testMod, "YourEntityType1", "missing_id");
        Assert.IsNull(entity);
    }
}
```

## Files to Review

1. **IEntityResolver.cs** - The interface definition
2. **DefaultEntityResolver.cs** - Built-in implementation
3. **EntityResolverExample.cs** - Complete working example
4. **ENTITY_RESOLVER_GUIDE.md** - Comprehensive architecture guide
5. **IMPLEMENTATION_SUMMARY.md** - Overview of changes
