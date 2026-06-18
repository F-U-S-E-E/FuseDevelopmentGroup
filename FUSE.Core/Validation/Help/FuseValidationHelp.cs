using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Fuse.Core.Validation.Help
{
    /// <summary>
    /// One fix-hint catalog entry: the "what's wrong &amp; how to fix it"
    /// guidance rendered next to a flagged validation issue.
    /// </summary>
    public sealed class FuseValidationHelpEntry
    {
        public string Title { get; set; }
        public string Why { get; set; }
        public string Fix { get; set; }
        public string Example { get; set; }
    }

    /// <summary>
    /// Lookup over the fix-hint catalog embedded in FUSE.Core
    /// (<c>Validation/Help/fuse-validation-help.json</c>), keyed by
    /// validation code. The catalog is data, not code, so docs authors can
    /// extend it without touching the validators; the coverage test in
    /// FUSE.Core.Tests keeps it in lockstep with the codes the validators
    /// actually emit. Unknown codes return null so formatters degrade to
    /// message-only output.
    /// </summary>
    public static class FuseValidationHelp
    {
        private const string ResourceName = "Fuse.Core.Validation.Help.fuse-validation-help.json";

        private static readonly object Gate = new object();
        private static Dictionary<string, FuseValidationHelpEntry> _entries;

        public static FuseValidationHelpEntry For(string code)
        {
            FuseValidationHelpEntry entry;
            return TryGet(code, out entry) ? entry : null;
        }

        public static bool TryGet(string code, out FuseValidationHelpEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(code))
            {
                return false;
            }

            EnsureLoaded();
            return _entries.TryGetValue(code, out entry) && entry != null;
        }

        public static IReadOnlyCollection<string> AllCodes
        {
            get
            {
                EnsureLoaded();
                return _entries.Keys;
            }
        }

        private static void EnsureLoaded()
        {
            if (_entries != null)
            {
                return;
            }

            lock (Gate)
            {
                if (_entries != null)
                {
                    return;
                }

                using (var stream = typeof(FuseValidationHelp).Assembly.GetManifestResourceStream(ResourceName))
                {
                    if (stream == null)
                    {
                        _entries = new Dictionary<string, FuseValidationHelpEntry>();
                        return;
                    }

                    using (var reader = new StreamReader(stream))
                    {
                        _entries = JsonConvert.DeserializeObject<Dictionary<string, FuseValidationHelpEntry>>(reader.ReadToEnd())
                                   ?? new Dictionary<string, FuseValidationHelpEntry>();
                    }
                }
            }
        }
    }
}
