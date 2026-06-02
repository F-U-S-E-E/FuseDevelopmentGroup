using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Port of <c>skeleton()</c> from the Python converter. Each
    /// legacy source JSON file becomes one FUSE fragment in the output
    /// package; the skeleton seeds the empty document with the
    /// canonical section shape so converters can populate it without
    /// re-creating the structure.
    /// </summary>
    internal static class FuseFragmentBuilder
    {
        public const string FuseSchemaVersion = "1.0";

        public static JObject Build(string modId, string modName, string modVersion, string author, string fragmentName)
        {
            return new JObject
            {
                ["$schema"] = ".\\schemas\\fuse-mod.schema.json",
                ["schemaVersion"] = FuseSchemaVersion,
                ["id"] = $"{modId}.{fragmentName}",
                ["name"] = $"{modName} ({fragmentName})",
                ["author"] = author ?? string.Empty,
                ["modVersion"] = modVersion,
                ["coordinateSpace"] = "world",
                ["tracks"] = new JObject
                {
                    ["nodes"] = new JObject(),
                    ["segments"] = new JObject(),
                    ["spans"] = new JObject(),
                    ["areas"] = new JObject(),
                    ["removals"] = new JObject
                    {
                        ["nodes"] = new JArray(),
                        ["segments"] = new JArray(),
                        ["spans"] = new JArray(),
                    },
                },
                ["operations"] = new JObject
                {
                    ["loads"] = new JObject(),
                    ["industries"] = new JObject(),
                    ["loaders"] = new JObject(),
                    ["turntables"] = new JObject(),
                    ["stations"] = new JObject(),
                },
                ["world"] = new JObject
                {
                    ["scenery"] = new JObject(),
                    ["spawnPoints"] = new JArray(),
                    ["splineys"] = new JObject(),
                    ["telegraphPoles"] = new JObject(),
                    ["telegraphPoleMovements"] = new JArray(),
                    ["mapLabels"] = new JObject(),
                    ["mapMasks"] = new JObject(),
                    ["mapTiles"] = new JObject(),
                    ["sceneClones"] = new JObject(),
                    ["removals"] = new JObject
                    {
                        ["scenery"] = new JArray(),
                        ["splineys"] = new JArray(),
                        ["telegraphPoles"] = new JArray(),
                        ["mapLabels"] = new JArray(),
                        ["mapMasks"] = new JArray(),
                        ["sceneClones"] = new JArray(),
                    },
                },
                ["progression"] = new JObject
                {
                    ["sections"] = new JArray(),
                    ["progressions"] = new JObject(),
                    ["mapFeatures"] = new JObject(),
                },
                ["extensions"] = new JObject(),
            };
        }
    }
}
