using FUSE.Loading;
using Fuse.Core.Model;

namespace FUSE.Editor.Screen
{
    /// <summary>
    /// Resolves entity instances from mod definitions based on entity kind.
    /// Allows external mods to extend the properties editor with custom entity types.
    /// </summary>
    public interface IEntityResolver
    {
        /// <summary>
        /// Returns true if this resolver can handle the given entity kind.
        /// </summary>
        /// <param name="entityKind">The entity kind string (e.g., "Nodes", "Segments", custom types)</param>
        /// <returns>True if this resolver can resolve the entity kind, false otherwise</returns>
        bool CanResolve(string entityKind);

        /// <summary>
        /// Attempts to retrieve the entity instance from the mod definition.
        /// </summary>
        /// <param name="mod">The loaded mod containing the definition</param>
        /// <param name="entityKind">The entity kind</param>
        /// <param name="entityId">The unique identifier of the entity</param>
        /// <returns>The entity instance if found, null otherwise</returns>
        object TryResolveEntity(FuseLoadedMod mod, string entityKind, string entityId);

        /// <summary>
        /// Resolves the mod definition object that contains this entity kind's collection.
        /// Used for persisting changes back to the mod definition.
        /// </summary>
        /// <param name="mod">The loaded mod</param>
        /// <param name="entityKind">The entity kind</param>
        /// <returns>The mod definition object (usually FuseModDefinition or custom mod definition), or null if not found</returns>
        object TryResolveModDefinition(FuseLoadedMod mod, string entityKind);
    }
}
