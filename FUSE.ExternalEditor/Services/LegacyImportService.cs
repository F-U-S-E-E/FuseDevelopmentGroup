using System.Collections.Generic;
using System.Linq;
using FUSE.Converter;

namespace Fuse.ExternalEditor.Services;

/// <summary>Outcome of a legacy-mod conversion (app-owned DTO; hides FUSE.Converter internals).</summary>
public sealed class LegacyImportResult
{
    public LegacyImportResult(bool success, string outputFolderPath, string? modId, string? modName, IReadOnlyList<string> writtenFragments, IReadOnlyList<string> messages)
    {
        Success = success;
        OutputFolderPath = outputFolderPath;
        ModId = modId;
        ModName = modName;
        WrittenFragments = writtenFragments;
        Messages = messages;
    }

    public bool Success { get; }
    public string OutputFolderPath { get; }
    public string? ModId { get; }
    public string? ModName { get; }
    public IReadOnlyList<string> WrittenFragments { get; }
    public IReadOnlyList<string> Messages { get; }

    /// <summary>Absolute path of the first written <c>*.fuse.json</c> fragment, or null.</summary>
    public string? FirstFragmentPath => WrittenFragments.Count == 0
        ? null
        : System.IO.Path.Combine(OutputFolderPath, WrittenFragments[0]);
}

/// <summary>Converts a legacy (Strange Customs / Railloader) mod folder to a FUSE package.</summary>
public interface ILegacyImportService
{
    LegacyImportResult Convert(string modFolder, string outputFolder, string requestedKind = "auto");
}

/// <summary>
/// Wraps <see cref="FuseLegacyConverter"/> (accessible via InternalsVisibleTo) and
/// maps its internal result to a public DTO. This is the "Convert legacy mod" entry —
/// the converter writes a stamped Info.json + one <c>*.fuse.json</c> per source.
/// </summary>
public sealed class LegacyImportService : ILegacyImportService
{
    public LegacyImportResult Convert(string modFolder, string outputFolder, string requestedKind = "auto")
    {
        var result = FuseLegacyConverter.ConvertPackage(modFolder, outputFolder, requestedKind);
        return new LegacyImportResult(
            result.Success,
            result.OutputFolderPath,
            result.ModId,
            result.ModName,
            result.WrittenFragments.ToList(),
            result.Report.Select(e => $"[{e.Level}] {e.Message}").ToList());
    }
}
