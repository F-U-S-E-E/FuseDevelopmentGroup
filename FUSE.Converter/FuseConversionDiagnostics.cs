using System.Collections.Generic;
using Fuse.Core.Validation;
using FUSE.Converter.Models;

namespace FUSE.Converter
{
    /// <summary>
    /// Adapts a conversion report into the unified
    /// <see cref="FuseDiagnostic"/> shape so the CLI and the editors render
    /// conversion and validation findings through one path. Lives here (not
    /// in FUSE.Core) because Core must not reference the converter layer.
    /// </summary>
    public static class FuseConversionDiagnostics
    {
        public static List<FuseDiagnostic> FromConversion(FuseConversionResult result)
        {
            var diagnostics = new List<FuseDiagnostic>();
            if (result?.Report == null)
            {
                return diagnostics;
            }

            foreach (var entry in result.Report)
            {
                if (entry == null)
                {
                    continue;
                }

                diagnostics.Add(new FuseDiagnostic(
                    ToSeverity(entry.Level),
                    entry.Concept,
                    field: null,
                    entry.Message,
                    entry.SourceFile));
            }

            return diagnostics;
        }

        private static FuseDiagnosticSeverity ToSeverity(FuseConversionReportLevel level)
        {
            switch (level)
            {
                case FuseConversionReportLevel.Error:
                    return FuseDiagnosticSeverity.Error;
                case FuseConversionReportLevel.Warning:
                    return FuseDiagnosticSeverity.Warning;
                default:
                    return FuseDiagnosticSeverity.Info;
            }
        }
    }
}
