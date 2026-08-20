namespace Fuse.ConverterCli;

/// <summary>
/// Parsed `fuse-convert` arguments. Mirrors the legacy Python converter's
/// surface (inputs, --out, --kind, --clean, --batch) plus the validation
/// flags from the convert→validate→report pipeline.
/// </summary>
internal sealed class CliOptions
{
    public List<string> Inputs { get; } = new();
    public string? OutputRoot { get; set; }
    public string Kind { get; set; } = "auto";
    public bool Clean { get; set; }
    public bool Batch { get; set; }
    public bool Validate { get; set; } = true;
    public bool Strict { get; set; }
    public string Format { get; set; } = "console";
    public bool Quiet { get; set; }
    public bool ShowHelp { get; set; }

    private static readonly string[] Kinds = { "auto", "route", "audio" };
    private static readonly string[] Formats = { "console", "json", "markdown", "all" };

    public bool WriteJsonReport => Format is "json" or "all";
    public bool WriteMarkdownReport => Format is "markdown" or "all";

    /// <summary>Returns the parsed options, or null with an error message on a usage problem.</summary>
    public static CliOptions? Parse(string[] args, out string? error)
    {
        error = null;
        var options = new CliOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    return options;

                case "--out":
                    if (++index >= args.Length)
                    {
                        error = "--out requires a directory path.";
                        return null;
                    }

                    options.OutputRoot = args[index];
                    break;

                case "--kind":
                    if (++index >= args.Length)
                    {
                        error = "--kind requires a value (auto, route, or audio).";
                        return null;
                    }

                    if (!Kinds.Contains(args[index]))
                    {
                        error = $"--kind: invalid value '{args[index]}'. Allowed: auto, route, audio.";
                        return null;
                    }

                    options.Kind = args[index];
                    break;

                case "--format":
                    if (++index >= args.Length)
                    {
                        error = "--format requires a value (console, json, markdown, or all).";
                        return null;
                    }

                    if (!Formats.Contains(args[index]))
                    {
                        error = $"--format: invalid value '{args[index]}'. Allowed: console, json, markdown, all.";
                        return null;
                    }

                    options.Format = args[index];
                    break;

                case "--clean":
                    options.Clean = true;
                    break;

                case "--batch":
                    options.Batch = true;
                    break;

                case "--validate":
                    options.Validate = true;
                    break;

                case "--no-validate":
                    options.Validate = false;
                    break;

                case "--strict":
                    options.Strict = true;
                    break;

                case "--quiet":
                    options.Quiet = true;
                    break;

                default:
                    if (arg.StartsWith('-'))
                    {
                        error = $"Unknown option: {arg}";
                        return null;
                    }

                    options.Inputs.Add(arg);
                    break;
            }
        }

        if (options.Inputs.Count == 0)
        {
            error = "At least one input folder is required.";
            return null;
        }

        return options;
    }

    public const string Usage = @"fuse-convert — convert legacy Railroader mods to FUSE packages and validate the result.

USAGE:
  fuse-convert <inputs...> [--out <dir>] [--kind <kind>] [--clean] [--batch]
               [--no-validate] [--strict] [--format <fmt>] [--quiet]

ARGUMENTS:
  <inputs...>     One or more legacy mod folders. With --batch, container
                  folders whose child folders are converted individually.
                  Zip archives and bare JSON files are not supported here;
                  extract the zip (or place the JSON in a mod folder) first.

OPTIONS:
  --out <dir>     Output root. Each mod converts into '<dir>\<ModFolder>.FUSE'.
                  Default: '.\FUSEConverted'.
  --kind <kind>   Force a JSON package kind: auto | route | audio. Default: auto.
  --clean         Replace an existing '.FUSE' output folder for each converted mod.
  --batch         Treat each input as a container of mods. JSON packages are
                  converted; code/assets/tiles/native packages are reported
                  with their correct install guidance.
  --validate      Validate converted fragments and print fix-hints (default: on).
  --no-validate   Skip validation.
  --strict        Exit with code 2 when validation produces warnings, not just errors.
  --format <fmt>  console (default) | json | markdown | all. json writes
                  conversion-report.json and markdown writes conversion-report.md
                  into successful output packages. Failed/unsupported inputs
                  write under '<out>\_conversion-reports'; all writes both.
  --quiet         Suppress per-diagnostic console output; print only summaries.
  -h, --help      Show this help.

EXIT CODES:
  0   success (validation warnings allowed unless --strict)
  1   conversion failed
  2   validation errors (or --strict and validation warnings)
  64  usage error";
}
