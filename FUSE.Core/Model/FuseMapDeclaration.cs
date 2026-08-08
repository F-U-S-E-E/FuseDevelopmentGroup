namespace Fuse.Core.Model
{
    /// <summary>
    /// Declares that the package provides a selectable map. The map's identity
    /// is the package id — one map per package. <see cref="MapFolder"/> is a
    /// package-relative folder shaped exactly like the game's
    /// StreamingAssets/Maps/&lt;name&gt; directories: a Map.json (origin
    /// lat/lon, tileDimension, tile list) plus tile_XXX_YYY.data heightmap
    /// tiles. When the map is the active session map, FUSE redirects
    /// MapStore.Load to this folder, so the pack's Map.json wholesale replaces
    /// the stock origin and tile set for that session.
    /// </summary>
    public sealed class FuseMapDeclaration
    {
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string MapFolder { get; set; }
        public bool SuppressBaseWorld { get; set; } = true;
    }
}
