using System.Collections.Generic;

namespace FUSE.Converter.Models
{
    /// <summary>
    /// Outcome of a single <c>FuseLegacyConverter.ConvertMod</c>
    /// invocation. <c>Success</c> is the gate: the rest of the fields
    /// always carry whatever the converter managed to compute (so a
    /// partial conversion shows the modder the work that DID complete
    /// alongside the error that stopped it).
    /// </summary>
    internal sealed class FuseConversionResult
    {
        public bool Success { get; set; }

        /// <summary>Absolute path to the output folder produced.</summary>
        public string OutputFolderPath { get; set; }

        /// <summary>Mod metadata pulled from Definition.json / Info.json.</summary>
        public string ModId { get; set; }
        public string ModName { get; set; }
        public string ModVersion { get; set; }
        public string Author { get; set; }

        /// <summary>Names of the *.fuse.json files written (relative to OutputFolderPath).</summary>
        public List<string> WrittenFragments { get; } = new List<string>();

        /// <summary>Per-fragment counts: fragment file → counts dictionary (e.g. "nodes" → 27).</summary>
        public Dictionary<string, Dictionary<string, int>> FragmentCounts { get; } = new Dictionary<string, Dictionary<string, int>>();

        /// <summary>Aggregated info / warning / error lines emitted during the conversion.</summary>
        public List<FuseConversionReportEntry> Report { get; } = new List<FuseConversionReportEntry>();
    }
}
