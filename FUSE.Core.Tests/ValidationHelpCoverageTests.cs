using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Fuse.Core.Validation.Help;
using Xunit;

namespace Fuse.Core.Tests
{
    /// <summary>
    /// Keeps the fix-hint catalog (<c>fuse-validation-help.json</c>) in
    /// lockstep with the codes the validators emit. Source-scan based: every
    /// <c>"fuse.*"</c> string literal in either validator source file counts
    /// as an emitted code, so the test needs no churn when rules move between
    /// single-line and multi-line calls or pass codes through helpers.
    /// </summary>
    public class ValidationHelpCoverageTests
    {
        private static readonly Regex CodePattern = new Regex("\"(fuse\\.\\w[\\w.]*)\"", RegexOptions.Compiled);

        private static readonly string[] ValidatorSourceResources =
        {
            "ValidatorSource.Core.cs",
            "ValidatorSource.Game.cs",
        };

        private static HashSet<string> EmittedCodes()
        {
            var codes = new HashSet<string>();
            foreach (var resource in ValidatorSourceResources)
            {
                using (var stream = typeof(ValidationHelpCoverageTests).Assembly.GetManifestResourceStream(resource))
                {
                    Assert.True(stream != null, $"Embedded validator source '{resource}' is missing.");
                    using (var reader = new StreamReader(stream!))
                    {
                        foreach (Match match in CodePattern.Matches(reader.ReadToEnd()))
                        {
                            codes.Add(match.Groups[1].Value);
                        }
                    }
                }
            }

            return codes;
        }

        [Fact]
        public void Every_Emitted_Code_Has_A_Catalog_Entry()
        {
            var missing = EmittedCodes()
                .Where(code => !FuseValidationHelp.TryGet(code, out _))
                .OrderBy(code => code)
                .ToList();

            Assert.True(
                missing.Count == 0,
                "Validation codes without a fix-hint catalog entry (add them to FUSE.Core/Validation/Help/fuse-validation-help.json): " +
                string.Join(", ", missing));
        }

        [Fact]
        public void Every_Catalog_Entry_Is_An_Emitted_Code()
        {
            var emitted = EmittedCodes();
            var orphans = FuseValidationHelp.AllCodes
                .Where(code => !emitted.Contains(code))
                .OrderBy(code => code)
                .ToList();

            Assert.True(
                orphans.Count == 0,
                "Catalog entries whose code no validator emits (stale key or typo in fuse-validation-help.json): " +
                string.Join(", ", orphans));
        }

        [Fact]
        public void Catalog_Entries_Have_Title_Why_And_Fix()
        {
            var incomplete = FuseValidationHelp.AllCodes
                .Where(code =>
                {
                    FuseValidationHelpEntry? entry;
                    FuseValidationHelp.TryGet(code, out entry);
                    return entry == null ||
                           string.IsNullOrWhiteSpace(entry.Title) ||
                           string.IsNullOrWhiteSpace(entry.Why) ||
                           string.IsNullOrWhiteSpace(entry.Fix);
                })
                .OrderBy(code => code)
                .ToList();

            Assert.True(
                incomplete.Count == 0,
                "Catalog entries with an empty title/why/fix: " + string.Join(", ", incomplete));
        }

        [Fact]
        public void Unknown_Code_Degrades_To_No_Help()
        {
            Assert.False(FuseValidationHelp.TryGet("fuse.not.a.real.code", out _));
            Assert.Null(FuseValidationHelp.For("fuse.not.a.real.code"));
            Assert.Null(FuseValidationHelp.For(null));
        }
    }
}
