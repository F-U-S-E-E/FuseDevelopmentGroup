## EditorHandlerRegistry - Static Method Pattern

The `EditorHandlerRegistry` uses a static method pattern to check if a handler can handle an entity **without instantiating it**. This is more efficient than creating an instance just to check compatibility.

### How It Works

1. **Discovery**: When the registry initializes, it scans for all classes that inherit from `EditorHandler`
2. **Static Check**: For each entity, the registry uses **reflection** to call the static `CanHandleEntityStatic` method on each handler type
3. **Instantiation**: Only when a handler's static method returns `true` does the registry create an instance

### Creating a New Handler

When creating a new `EditorHandler` subclass, implement the static method pattern like this:

```csharp
public class MyCustomEditorHandler : EditorHandler
{
    public MyCustomEditorHandler(object entity)
    {
        Entity = entity;
        // Initialize handler...
    }

    /// <summary>
    /// Static method called by the registry via reflection.
    /// This is checked BEFORE instantiation, so keep it lightweight.
    /// </summary>
    public static bool CanHandleEntityStatic(object entity)
    {
        return entity is MyCustomEntity;
    }

    /// <summary>
    /// Instance method - delegates to the static method.
    /// </summary>
    public override bool CanHandleEntity(object entity)
    {
        return CanHandleEntityStatic(entity);
    }

    // Implement other abstract methods...
}
```

### Key Points

- **Static Method Name**: Must be exactly `CanHandleEntityStatic`
- **Signature**: `public static bool CanHandleEntityStatic(object entity)`
- **Return Type**: Must return `bool`
- **No Instantiation**: The registry calls this via reflection without creating an instance
- **For Complex Types**: If you need to check multiple conditions, consider type checking first:

```csharp
public static bool CanHandleEntityStatic(object entity)
{
    // Type check first (fast)
    if (!(entity is GameObject go))
        return false;

    // Component check (slower, but only if type matches)
    return go.GetComponent<MyCustomComponent>() != null;
}
```

### Usage

The registry automatically discovers and manages all handlers:

```csharp
// Initialize (called once, typically at startup)
EditorHandlerRegistry.Initialize();

// Create a handler for an entity
var entity = GetMyEntity();
var handler = EditorHandlerRegistry.CreateHandler(entity);

if (handler != null)
{
    // Use the handler for rendering, editing, etc.
    handler.Render(camera);
}
```

### Registry Methods

- **Initialize()**: Discover all handler types
- **CreateHandler(object entity)**: Create the appropriate handler for an entity
- **RegisterHandlerType(Type)**: Add a custom handler type at runtime
- **UnregisterHandlerType(Type)**: Remove a handler type
- **GetRegisteredHandlerTypes()**: View all registered handlers
- **Reset()**: Clear the registry

### Performance Benefit

By using static methods with reflection:
- ❌ **Old Way**: Create instance → Check CanHandle → Discard if false → Create real instance = **2 instantiations**
- ✅ **New Way**: Check static method → Create instance if true = **1 instantiation max**

This is especially important when:
- You have many handler types
- Instantiation is expensive
- There are many entities to process per frame
