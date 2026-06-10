using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fuse.Core.Validation.Help;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Fuse.Core.Validation
{
    /// <summary>
    /// Converts validation results into unified <see cref="FuseDiagnostic"/>
    /// rows (resolving each issue's fix-hint from the embedded catalog) and
    /// renders diagnostic lists for the console, Markdown reports, and JSON
    /// reports. Conversion reports flow through the same shape via the
    /// adapter in FUSE.Converter, so every front end shares one renderer.
    /// </summary>
    public static class FuseValidationRenderer
    {
        public static List<FuseDiagnostic> FromValidation(string source, ValidationResult result)
        {
            var diagnostics = new List<FuseDiagnostic>();
            if (result == null)
            {
                return diagnostics;
            }

            foreach (var issue in result.Errors)
            {
                diagnostics.Add(ToDiagnostic(FuseDiagnosticSeverity.Error, source, issue));
            }

            foreach (var issue in result.Warnings)
            {
                diagnostics.Add(ToDiagnostic(FuseDiagnosticSeverity.Warning, source, issue));
            }

            return diagnostics;
        }

        private static FuseDiagnostic ToDiagnostic(FuseDiagnosticSeverity severity, string source, ValidationIssue issue)
        {
            return new FuseDiagnostic(
                severity,
                issue.Code,
                issue.Field,
                issue.Message,
                source,
                issue.Value,
                FuseValidationHelp.For(issue.Code));
        }

        public static string ToConsole(IReadOnlyList<FuseDiagnostic> diagnostics)
        {
            var builder = new StringBuilder();
            foreach (var diagnostic in diagnostics ?? (IReadOnlyList<FuseDiagnostic>)new FuseDiagnostic[0])
            {
                builder.Append(diagnostic.Severity.ToString().ToLowerInvariant());
                if (!string.IsNullOrEmpty(diagnostic.Code))
                {
                    builder.Append(' ').Append(diagnostic.Code);
                }

                builder.AppendLine();

                var location = JoinLocation(diagnostic);
                if (location.Length > 0)
                {
                    builder.Append("  at:      ").AppendLine(location);
                }

                builder.Append("  problem: ").AppendLine(diagnostic.Message);
                if (diagnostic.Value != null)
                {
                    builder.Append("  value:   ").AppendLine(diagnostic.Value.ToString());
                }

                if (diagnostic.Help != null)
                {
                    if (!string.IsNullOrEmpty(diagnostic.Help.Why))
                    {
                        builder.Append("  why:     ").AppendLine(diagnostic.Help.Why);
                    }

                    if (!string.IsNullOrEmpty(diagnostic.Help.Fix))
                    {
                        builder.Append("  fix:     ").AppendLine(diagnostic.Help.Fix);
                    }

                    if (!string.IsNullOrEmpty(diagnostic.Help.Example))
                    {
                        builder.Append("  example: ").AppendLine(diagnostic.Help.Example);
                    }
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }

        public static string ToMarkdown(IReadOnlyList<FuseDiagnostic> diagnostics)
        {
            var builder = new StringBuilder();
            foreach (var diagnostic in diagnostics ?? (IReadOnlyList<FuseDiagnostic>)new FuseDiagnostic[0])
            {
                var title = diagnostic.Help != null && !string.IsNullOrEmpty(diagnostic.Help.Title)
                    ? diagnostic.Help.Title
                    : diagnostic.Message;
                builder.Append("- **").Append(diagnostic.Severity.ToString().ToLowerInvariant()).Append("** ");
                if (!string.IsNullOrEmpty(diagnostic.Code))
                {
                    builder.Append('`').Append(diagnostic.Code).Append("` ");
                }

                builder.AppendLine(title);

                var location = JoinLocation(diagnostic);
                if (location.Length > 0)
                {
                    builder.Append("  - At: `").Append(location).AppendLine("`");
                }

                builder.Append("  - Problem: ").AppendLine(diagnostic.Message);
                if (diagnostic.Help != null)
                {
                    if (!string.IsNullOrEmpty(diagnostic.Help.Why))
                    {
                        builder.Append("  - Why: ").AppendLine(diagnostic.Help.Why);
                    }

                    if (!string.IsNullOrEmpty(diagnostic.Help.Fix))
                    {
                        builder.Append("  - Fix: ").AppendLine(diagnostic.Help.Fix);
                    }

                    if (!string.IsNullOrEmpty(diagnostic.Help.Example))
                    {
                        builder.Append("  - Example: `").Append(diagnostic.Help.Example).AppendLine("`");
                    }
                }
            }

            return builder.ToString();
        }

        public static string ToJson(IReadOnlyList<FuseDiagnostic> diagnostics)
        {
            return ToJsonArray(diagnostics).ToString(Formatting.Indented);
        }

        public static JArray ToJsonArray(IReadOnlyList<FuseDiagnostic> diagnostics)
        {
            var rows = new JArray();
            foreach (var diagnostic in diagnostics ?? (IReadOnlyList<FuseDiagnostic>)new FuseDiagnostic[0])
            {
                var row = new JObject
                {
                    ["severity"] = diagnostic.Severity.ToString().ToLowerInvariant(),
                    ["code"] = diagnostic.Code,
                    ["field"] = diagnostic.Field,
                    ["message"] = diagnostic.Message,
                    ["source"] = diagnostic.Source,
                };

                if (diagnostic.Value != null)
                {
                    row["value"] = JToken.FromObject(diagnostic.Value);
                }

                if (diagnostic.Help != null)
                {
                    row["help"] = new JObject
                    {
                        ["title"] = diagnostic.Help.Title,
                        ["why"] = diagnostic.Help.Why,
                        ["fix"] = diagnostic.Help.Fix,
                        ["example"] = diagnostic.Help.Example,
                    };
                }

                rows.Add(row);
            }

            return rows;
        }

        public static int CountErrors(IEnumerable<FuseDiagnostic> diagnostics)
        {
            return diagnostics?.Count(diagnostic => diagnostic.Severity == FuseDiagnosticSeverity.Error) ?? 0;
        }

        public static int CountWarnings(IEnumerable<FuseDiagnostic> diagnostics)
        {
            return diagnostics?.Count(diagnostic => diagnostic.Severity == FuseDiagnosticSeverity.Warning) ?? 0;
        }

        private static string JoinLocation(FuseDiagnostic diagnostic)
        {
            if (string.IsNullOrEmpty(diagnostic.Source))
            {
                return diagnostic.Field ?? string.Empty;
            }

            if (string.IsNullOrEmpty(diagnostic.Field))
            {
                return diagnostic.Source;
            }

            return diagnostic.Source + " :: " + diagnostic.Field;
        }
    }
}
