using Fuse.Core.Serialization;
using Fuse.Core.Validation;
using FUSE.Converter;
using FUSE.Converter.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Fuse.ConverterCli;

/// <summary>
/// `fuse-convert`: convert legacy Railroader mods to FUSE packages, then
/// validate every written fragment and render each finding with its
/// fix-hint from the FUSE.Core catalog. Exit codes let CI gate conversion
/// failures (1) separately from validation findings (2).
/// </summary>
internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitConversionFailed = 1;
    private const int ExitValidationFailed = 2;
    private const int ExitUsage = 64;

    private static int Main(string[] args)
    {
        var options = CliOptions.Parse(args, out var usageError);
        if (options == null)
        {
            Console.Error.WriteLine($"fuse-convert: {usageError}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(CliOptions.Usage);
            return ExitUsage;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(CliOptions.Usage);
            return ExitOk;
        }

        var modFolders = ResolveModFolders(options, out var inputErrors);
        foreach (var inputError in inputErrors)
        {
            Console.Error.WriteLine($"fuse-convert: {inputError}");
        }

        var anyConversionFailed = inputErrors.Count > 0;
        var anyValidationErrors = false;
        var anyValidationWarnings = false;

        var outputRoot = Path.GetFullPath(options.OutputRoot ?? Path.Combine(".", "FUSEConverted"));
        foreach (var modFolder in modFolders)
        {
            var outputFolder = Path.Combine(outputRoot, Path.GetFileName(modFolder.TrimEnd('\\', '/')) + ".FUSE");
            if (options.Clean && Directory.Exists(outputFolder))
            {
                Directory.Delete(outputFolder, recursive: true);
            }

            var result = FuseLegacyConverter.ConvertPackage(modFolder, outputFolder, options.Kind);
            var conversionDiagnostics = FuseConversionDiagnostics.FromConversion(result);
            var validationDiagnostics = options.Validate
                ? ValidateFragments(result)
                : new List<FuseDiagnostic>();

            anyConversionFailed |= !result.Success;
            anyValidationErrors |= FuseValidationRenderer.CountErrors(validationDiagnostics) > 0;
            anyValidationWarnings |= FuseValidationRenderer.CountWarnings(validationDiagnostics) > 0;

            PrintMod(options, modFolder, result, conversionDiagnostics, validationDiagnostics);
            WriteReports(options, result, conversionDiagnostics, validationDiagnostics);
        }

        if (anyConversionFailed)
        {
            return ExitConversionFailed;
        }

        if (anyValidationErrors || (options.Strict && anyValidationWarnings))
        {
            return ExitValidationFailed;
        }

        return ExitOk;
    }

    /// <summary>
    /// Expands the raw inputs into the list of mod folders to convert.
    /// In batch mode each input is a container whose recognized child
    /// folders are converted individually.
    /// </summary>
    private static List<string> ResolveModFolders(CliOptions options, out List<string> errors)
    {
        errors = new List<string>();
        var folders = new List<string>();
        foreach (var input in options.Inputs)
        {
            if (File.Exists(input))
            {
                errors.Add($"'{input}' is a file. Zip archives and bare JSON files are not supported; extract the zip (or place the JSON in a mod folder) and pass the folder.");
                continue;
            }

            if (!Directory.Exists(input))
            {
                errors.Add($"Input folder does not exist: {input}");
                continue;
            }

            if (!options.Batch)
            {
                folders.Add(input);
                continue;
            }

            var recognized = Directory.GetDirectories(input)
                .Where(child => FuseLegacyConverter.DetectKind(child, options.Kind) != "unknown")
                .OrderBy(child => child, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (recognized.Count == 0)
            {
                errors.Add($"--batch found no recognizable mod folders under: {input}");
                continue;
            }

            folders.AddRange(recognized);
        }

        return folders;
    }

    /// <summary>
    /// Loads each written fragment back through the shared serializer
    /// (running migrations exactly as the game would) and validates it,
    /// resolving every issue's fix-hint via the renderer.
    /// </summary>
    private static List<FuseDiagnostic> ValidateFragments(FuseConversionResult result)
    {
        var diagnostics = new List<FuseDiagnostic>();
        var validator = new FuseDefinitionValidator();
        foreach (var fragment in result.WrittenFragments.Where(name => name.EndsWith(".fuse.json", StringComparison.OrdinalIgnoreCase)))
        {
            var path = Path.Combine(result.OutputFolderPath, fragment);
            try
            {
                var definition = FuseCoreSerializer.Load(path);
                diagnostics.AddRange(FuseValidationRenderer.FromValidation(fragment, validator.Validate(definition)));
            }
            catch (Exception ex)
            {
                diagnostics.Add(new FuseDiagnostic(
                    FuseDiagnosticSeverity.Error,
                    code: null,
                    field: null,
                    $"Fragment failed to load for validation: {ex.Message}",
                    fragment));
            }
        }

        return diagnostics;
    }

    private static void PrintMod(
        CliOptions options,
        string modFolder,
        FuseConversionResult result,
        IReadOnlyList<FuseDiagnostic> conversionDiagnostics,
        IReadOnlyList<FuseDiagnostic> validationDiagnostics)
    {
        var name = string.IsNullOrEmpty(result.ModName) ? Path.GetFileName(modFolder) : result.ModName;
        Console.WriteLine($"== {name} ({modFolder})");
        Console.WriteLine($"   conversion: {(result.Success ? "ok" : "FAILED")}, {result.WrittenFragments.Count} fragment(s) -> {result.OutputFolderPath}");
        if (options.Validate)
        {
            Console.WriteLine($"   validation: {FuseValidationRenderer.CountErrors(validationDiagnostics)} error(s), {FuseValidationRenderer.CountWarnings(validationDiagnostics)} warning(s)");
        }

        Console.WriteLine();

        if (options.Quiet)
        {
            return;
        }

        var conversionText = FuseValidationRenderer.ToConsole(conversionDiagnostics);
        if (conversionText.Length > 0)
        {
            Console.WriteLine(conversionText);
        }

        var validationText = FuseValidationRenderer.ToConsole(validationDiagnostics);
        if (validationText.Length > 0)
        {
            Console.WriteLine(validationText);
        }
    }

    /// <summary>
    /// Writes conversion-report.json / conversion-report.md (the legacy
    /// Python converter's report file names) into the mod's output folder.
    /// </summary>
    private static void WriteReports(
        CliOptions options,
        FuseConversionResult result,
        List<FuseDiagnostic> conversionDiagnostics,
        List<FuseDiagnostic> validationDiagnostics)
    {
        if ((!options.WriteJsonReport && !options.WriteMarkdownReport) || !Directory.Exists(result.OutputFolderPath))
        {
            return;
        }

        if (options.WriteJsonReport)
        {
            var report = new JObject
            {
                ["tool"] = "fuse-convert",
                ["modId"] = result.ModId,
                ["modName"] = result.ModName,
                ["success"] = result.Success,
                ["outputFolder"] = result.OutputFolderPath,
                ["fragments"] = JArray.FromObject(result.WrittenFragments),
                ["conversion"] = FuseValidationRenderer.ToJsonArray(conversionDiagnostics),
                ["validation"] = FuseValidationRenderer.ToJsonArray(validationDiagnostics),
            };
            File.WriteAllText(
                Path.Combine(result.OutputFolderPath, "conversion-report.json"),
                report.ToString(Formatting.Indented));
        }

        if (options.WriteMarkdownReport)
        {
            var markdown =
                $"# Conversion report: {result.ModName}\n\n" +
                $"- Source mod id: `{result.ModId}`\n" +
                $"- Conversion: {(result.Success ? "ok" : "**failed**")}\n" +
                $"- Fragments written: {result.WrittenFragments.Count}\n\n" +
                "## Conversion\n\n" +
                (conversionDiagnostics.Count > 0 ? FuseValidationRenderer.ToMarkdown(conversionDiagnostics) : "No conversion findings.\n") +
                "\n## Validation\n\n" +
                (validationDiagnostics.Count > 0 ? FuseValidationRenderer.ToMarkdown(validationDiagnostics) : "No validation findings.\n");
            File.WriteAllText(Path.Combine(result.OutputFolderPath, "conversion-report.md"), markdown);
        }
    }
}
