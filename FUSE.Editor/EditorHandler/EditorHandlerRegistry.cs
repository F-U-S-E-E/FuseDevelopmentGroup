using FUSE.Infrastructure;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace FUSE.Editor.EditorHandler
{
    /// <summary>
    /// Registry for managing EditorHandler types and instantiating appropriate handlers for entities.
    /// Automatically discovers and registers all EditorHandler implementations,
    /// and provides factory methods to create handlers based on entity type.
    /// </summary>
    public static class EditorHandlerRegistry
    {
        private static List<Type> _registeredHandlerTypes;
        private static bool _initialized;

        /// <summary>
        /// Initializes the registry by discovering all EditorHandler implementations.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _registeredHandlerTypes = new List<Type>();
            DiscoverHandlerTypes();
            _initialized = true;

            FuseLog.Info($"EditorHandlerRegistry: Initialized with {_registeredHandlerTypes.Count} handler types");
        }

        /// <summary>
        /// Discovers all EditorHandler implementations in the current assembly.
        /// </summary>
        private static void DiscoverHandlerTypes()
        {
            try
            {
                var assembly = typeof(EditorHandlerBase).Assembly;
                var baseHandlerType = typeof(EditorHandlerBase);

                foreach (var type in assembly.GetTypes())
                {
                    // Skip abstract classes and the base EditorHandler class itself
                    if (type.IsAbstract || type == baseHandlerType)
                    {
                        continue;
                    }

                    // Check if the type inherits from EditorHandler
                    if (baseHandlerType.IsAssignableFrom(type))
                    {
                        _registeredHandlerTypes.Add(type);
                        FuseLog.Info($"EditorHandlerRegistry: Registered handler type: {type.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Error($"EditorHandlerRegistry: Failed to discover handler types: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to create an appropriate handler for the given entity.
        /// Iterates through registered handler types and uses the first one where CanHandleEntity returns true.
        /// </summary>
        /// <param name="entity">The entity to create a handler for.</param>
        /// <returns>A new EditorHandler instance, or null if no handler can handle the entity.</returns>
        public static EditorHandlerBase CreateHandler(object entity)
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (entity == null)
            {
                FuseLog.Warning("EditorHandlerRegistry: Cannot create handler for null entity");
                return null;
            }

            // Try each registered handler type
            foreach (var handlerType in _registeredHandlerTypes)
            {
                try
                {
                    // Use reflection to call the static CanHandleEntity method
                    if (CanHandleEntityWithStaticMethod(handlerType, entity))
                    {
                        // If static method says it can handle, create the handler
                        var handler = (EditorHandlerBase)Activator.CreateInstance(handlerType, entity);

                        if (handler != null)
                        {
                            FuseLog.Info($"EditorHandlerRegistry: Created {handlerType.Name} for entity {entity.GetType().Name}: {handler.ID}");
                            return handler;
                        }
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Info($"EditorHandlerRegistry: Failed to create {handlerType.Name}: {ex.Message}");
                    // Continue to the next handler type
                }
            }

            FuseLog.Warning($"EditorHandlerRegistry: No handler found for entity type {entity.GetType().Name}");
            return null;
        }

        /// <summary>
        /// Calls the static CanHandleEntity method on a handler type via reflection.
        /// Looks for a static method named "CanHandleEntityStatic" that takes an object parameter.
        /// </summary>
        private static bool CanHandleEntityWithStaticMethod(Type handlerType, object entity)
        {
            try
            {
                // Look for the static CanHandleEntityStatic method
                var method = handlerType.GetMethod(
                    "CanHandleEntityStatic",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null,
                    new[] { typeof(object) },
                    null);

                if (method != null && method.ReturnType == typeof(bool))
                {
                    // Call the static method
                    var result = method.Invoke(null, new[] { entity });
                    return (bool)result;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"EditorHandlerRegistry: Failed to call CanHandleEntityStatic on {handlerType.Name}", ex);
            }

            return false;
        }

        /// <summary>
        /// Registers a custom handler type.
        /// </summary>
        /// <param name="handlerType">The handler type to register (must inherit from EditorHandler).</param>
        public static void RegisterHandlerType(Type handlerType)
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (!typeof(EditorHandlerBase).IsAssignableFrom(handlerType))
            {
                FuseLog.Error($"EditorHandlerRegistry: Type {handlerType.Name} does not inherit from EditorHandler");
                return;
            }

            if (_registeredHandlerTypes.Contains(handlerType))
            {
                FuseLog.Warning($"EditorHandlerRegistry: Handler type {handlerType.Name} is already registered");
                return;
            }

            _registeredHandlerTypes.Add(handlerType);
            FuseLog.Info($"EditorHandlerRegistry: Registered custom handler type: {handlerType.Name}");
        }

        /// <summary>
        /// Unregisters a handler type.
        /// </summary>
        /// <param name="handlerType">The handler type to unregister.</param>
        public static void UnregisterHandlerType(Type handlerType)
        {
            if (!_initialized)
            {
                return;
            }

            if (_registeredHandlerTypes.Remove(handlerType))
            {
                FuseLog.Info($"EditorHandlerRegistry: Unregistered handler type: {handlerType.Name}");
            }
        }

        /// <summary>
        /// Gets the list of registered handler types.
        /// </summary>
        public static IReadOnlyList<Type> GetRegisteredHandlerTypes()
        {
            if (!_initialized)
            {
                Initialize();
            }

            return _registeredHandlerTypes.AsReadOnly();
        }

        /// <summary>
        /// Clears all registered handler types and resets the registry.
        /// </summary>
        public static void Reset()
        {
            _registeredHandlerTypes?.Clear();
            _initialized = false;
        }
    }
}
