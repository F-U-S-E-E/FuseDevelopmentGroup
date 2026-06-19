using FUSE.Loading;
using Fuse.Core.Model;

namespace FUSE.Editor.Screen
{
    /// <summary>
    /// Default entity resolver for built-in FUSE entity types (Nodes, Segments, Spans, Areas,
    /// Scenery, Splineys, MapLabels, Telegraphs, Industries, Loads, Stations, Turntables, Loaders).
    /// This resolver handles all core FUSE data classes defined in the Authoring.Data namespace.
    /// </summary>
    internal sealed class DefaultEntityResolver : IEntityResolver
    {
        /// <summary>
        /// Checks if this resolver handles the given entity kind.
        /// Only returns true for built-in FUSE entity types.
        /// </summary>
        public bool CanResolve(string entityKind)
        {
            return entityKind is
                // Track entities
                "Node" or "Segment" or "Span" or "Area" or
                // World entities
                "Scenery" or "Spliney" or "MapLabel" or "Telegraph" or
                // Operations entities
                "Industry" or "Load" or "Station" or "Turntable" or "Loader";
        }

        /// <summary>
        /// Resolves built-in FUSE entities from the mod definition.
        /// </summary>
        public object TryResolveEntity(FuseLoadedMod mod, string entityKind, string entityId)
        {
            if (mod?.Definition == null)
            {
                return null;
            }

            var definition = mod.Definition;

            // Use switch expression for cleaner dispatch
            return entityKind switch
            {
                // Track entities
                "Node" => FUSE.Runtime.API.TrackAPI.GetNodeDefinition(entityId),
                "Segment" => FUSE.Runtime.API.TrackAPI.GetSegmentDefinition(entityId),
                "Span" => FUSE.Runtime.API.TrackAPI.GetSpanDefinition(entityId),
                "Area" => FUSE.Runtime.API.TrackAPI.GetAreaDefinition(entityId),

                // World entities
                "Scenery" => FUSE.Runtime.API.SceneryAPI.GetSceneryDefinition(entityId),
                "Spliney" => FUSE.Runtime.API.SplineyAPI.GetSplineyDefinition(entityId),
                "MapLabel" => FUSE.Runtime.API.MapAPI.GetMapLabelDefinition(entityId),
                "Telegraph" => FUSE.Runtime.API.MapAPI.GetTelegraphPolesDefinition(entityId),

                // Operations entities
                "Industry" => FUSE.Runtime.API.IndustryAPI.GetIndustryDefinition(entityId),
                "Load" => FUSE.Runtime.API.LoadAPI.GetLoadDefinition(entityId),
                "Station" => FUSE.Runtime.API.StationAPI.GetStationDefinition(entityId),
                "Turntable" => FUSE.Runtime.API.TurntableAPI.GetTurntableDefinition(entityId),
                "Loader" => FUSE.Runtime.API.LoaderAPI.GetLoaderDefinition(entityId),

                _ => null
            };
        }

        /// <summary>
        /// Resolves the mod definition for built-in FUSE entity types.
        /// Returns the FuseModDefinition which is the root for all built-in entities.
        /// </summary>
        public object TryResolveModDefinition(FuseLoadedMod mod, string entityKind)
        {
            if (mod?.Definition == null)
            {
                return null;
            }

            // For built-in FUSE types, all entities are under mod.Definition (FuseModDefinition)
            if (CanResolve(entityKind))
            {
                return mod.Definition;
            }

            return null;
        }
    }
}
