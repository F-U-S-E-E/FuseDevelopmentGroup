using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Port of <c>meta()</c> from the Python converter. Reads
    /// Definition.json (Railloader manifest) or Info.json (UMM
    /// manifest) and pulls out the id / name / version / author
    /// fields. Falls back to the folder name + sensible defaults
    /// when neither manifest exists or is unreadable.
    /// </summary>
    internal static class LegacyManifestReader
    {
        public struct LegacyManifest
        {
            public string Id;
            public string Name;
            public string Version;
            public string Author;
        }

        public static LegacyManifest Read(string modFolder)
        {
            var result = new LegacyManifest
            {
                Id = Path.GetFileName(modFolder),
                Name = Path.GetFileName(modFolder),
                Version = "1.0.0",
                Author = string.Empty,
            };

            foreach (var manifestName in new[] { "Definition.json", "Info.json" })
            {
                var path = Path.Combine(modFolder, manifestName);
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    // Tolerant read so manifests with JSONC comments
                    // / trailing commas (legacy Railloader convention)
                    // still load.
                    var json = LegacyJsonReader.ReadJson(path) as JObject;
                    if (json != null)
                    {
                        result.Id = FirstNonEmpty(json.Value<string>("id"), json.Value<string>("Id"), result.Id);
                        result.Name = FirstNonEmpty(json.Value<string>("name"), json.Value<string>("DisplayName"), result.Name);
                        result.Version = FirstNonEmpty(json.Value<string>("version"), json.Value<string>("Version"), result.Version);
                        result.Author = FirstNonEmpty(json.Value<string>("author"), json.Value<string>("Author"), result.Author);
                        return result;
                    }
                }
                catch (Exception)
                {
                    // Try the next manifest; the conversion report will
                    // surface a clearer warning at a higher level.
                }
            }

            return result;
        }

        private static string FirstNonEmpty(params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }
    }
}
