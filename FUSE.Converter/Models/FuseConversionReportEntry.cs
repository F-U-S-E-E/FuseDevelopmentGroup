namespace FUSE.Converter.Models
{
    /// <summary>
    /// Severity bucket for a conversion report line. The CLI converter
    /// promotes errors to a non-zero exit, while the editor's Convert
    /// button surfaces warnings in the status panel so the modder can
    /// see what's missing without the conversion outright failing.
    /// </summary>
    internal enum FuseConversionReportLevel
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// One line in the conversion report. Mirrors the Python
    /// <c>ReportEntry</c> dataclass: level + message + optional
    /// originating file + originating concept.
    /// </summary>
    internal sealed class FuseConversionReportEntry
    {
        public FuseConversionReportLevel Level { get; set; }
        public string Message { get; set; }
        public string SourceFile { get; set; }
        public string Concept { get; set; }
    }
}
