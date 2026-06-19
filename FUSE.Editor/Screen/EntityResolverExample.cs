using FUSE.Editor.Screen;
using FUSE.Loading;

namespace FUSE.Editor.Examples
{
    /*
     * EXAMPLE: How to extend the FUSE Editor Properties Panel with custom entity types
     * 
     * This example demonstrates how an external mod can register custom entity resolvers
     * to support properties editing for their own entity types.
     * 
     * STEP 1: Create your entity resolver by implementing IEntityResolver
     */

    /// <summary>
    /// Example resolver for a custom ExampleMod that defines custom entity types.
    /// This would typically be defined in your mod's code.
    /// </summary>
    public class ExampleModEntityResolver : IEntityResolver
    {
        /// <summary>
        /// Returns true if this resolver handles the given entity kind.
        /// In this example, we handle "CustomTrackObjects" and "CustomSignals".
        /// </summary>
        public bool CanResolve(string entityKind)
        {
            return entityKind is "CustomTrackObjects" or "CustomSignals";
        }

        /// <summary>
        /// Resolves custom entities from the mod definition.
        /// </summary>
        public object TryResolveEntity(FuseLoadedMod mod, string entityKind, string entityId)
        {
            // Your mod's definition would likely have custom collections
            // Cast the definition to your mod's definition type if needed

            if (mod?.Definition == null)
            {
                return null;
            }

            // OPTION A: If your mod definition extends FuseDefinition
            // var customDef = mod.Definition as YourModDefinition;
            // return entityKind switch
            // {
            //     "CustomTrackObjects" => customDef?.CustomTrackObjects?.GetValueOrDefault(entityId),
            //     "CustomSignals" => customDef?.CustomSignals?.GetValueOrDefault(entityId),
            //     _ => null
            // };

            // OPTION B: If your entities are stored separately
            // return GetCustomEntity(entityKind, entityId);

            return null; // Replace with your actual implementation
        }

        /// <summary>
        /// Resolves the mod definition for custom entity types.
        /// Returns the mod definition object that contains this entity kind's collection.
        /// </summary>
        public object TryResolveModDefinition(FuseLoadedMod mod, string entityKind)
        {
            if (mod?.Definition == null)
            {
                return null;
            }

            // For custom entity types, you would typically return your custom mod definition
            // if (CanResolve(entityKind))
            // {
            //     return mod.Definition as YourModDefinition;
            // }

            return null; // Replace with your actual implementation
        }
    }

    /*
     * STEP 2: Register your resolver during mod initialization
     */

    public class ExampleModLoader
    {
        /// <summary>
        /// Called when the mod is loaded. Register custom entity resolvers here.
        /// </summary>
        public static void OnModLoad()
        {
            // Register your custom entity resolver with the properties panel
            FuseEditorPropertiesPanel.RegisterEntityResolver(
                new ExampleModEntityResolver()
            );
        }
    }

    /*
     * STEP 3: Update your entity tree or selection system to include your custom entity kinds
     * 
     * When the FUSE editor's selection system encounters your custom entity kind,
     * the properties panel will automatically use your resolver to look it up and
     * display its properties.
     * 
     * USAGE EXAMPLE from mod code:
     * -----
     * // In your entity tree:
     * public void SelectCustomEntity(string entityId)
     * {
     *     editor.SetSelectedEntity("CustomTrackObjects", entityId);
     * }
     * 
     * // The properties panel will:
     * // 1. See "CustomTrackObjects" as the entity kind
     * // 2. Ask each resolver if it can handle it
     * // 3. Your ExampleModEntityResolver.CanResolve returns true
     * // 4. Call TryResolveEntity to get the entity instance
     * // 5. Dynamically generate properties for your custom entity type
     * -----
     * 
     * REGISTERING MULTIPLE RESOLVERS:
     * You can register multiple resolvers if your mod handles several custom entity types.
     * Each resolver is tried in registration order, so:
     * - First resolver that returns true from CanResolve is used
     * - DefaultEntityResolver is always registered first (lowest priority)
     * - Your resolvers are checked in registration order (highest priority)
     * 
     * BEST PRACTICES:
     * 1. One resolver per logical grouping of entity types
     * 2. Make CanResolve fast (simple string checks, not expensive lookups)
     * 3. Return null from TryResolveEntity if entity not found (don't throw)
     * 4. Document which entity kinds your resolver handles
     */
}
