using Fuse.Core.Validation.Help;

namespace Fuse.Core.Validation
{
    public enum FuseDiagnosticSeverity
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// One rendered-ready diagnostic row: a validation issue (or conversion
    /// report line) plus the resolved fix-hint catalog entry. The CLI and
    /// the editors bind the same list — there is intentionally no second
    /// presentation model anywhere else.
    /// </summary>
    public sealed class FuseDiagnostic
    {
        public FuseDiagnostic(
            FuseDiagnosticSeverity severity,
            string code,
            string field,
            string message,
            string source,
            object value = null,
            FuseValidationHelpEntry help = null)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Field = field ?? string.Empty;
            Message = message ?? string.Empty;
            Source = source ?? string.Empty;
            Value = value;
            Help = help;
        }

        public FuseDiagnosticSeverity Severity { get; }
        public string Code { get; }
        public string Field { get; }
        public string Message { get; }

        /// <summary>Where the diagnostic came from, e.g. a fragment file name.</summary>
        public string Source { get; }

        /// <summary>The offending value, when the issue captured one.</summary>
        public object Value { get; }

        /// <summary>Resolved fix-hint, or null when the code has no catalog entry.</summary>
        public FuseValidationHelpEntry Help { get; }
    }
}
