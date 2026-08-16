#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property PublishAot=false
#:property AppendTargetFrameworkToOutputPath=true
#:package System.Reflection.MetadataLoadContext@8.0.0

// Validates that a built FUSE release .zip conforms to UnityModManager (UMM)
// requirements before it gets published. FUSE ships as a *pure* UMM mod (it is
// the Railloader replacement, so unlike a normal Railroader mod it carries no
// Railloader Definition.json — only a UMM Info.json), so the checks here are the
// UMM subset of the cross-repo Validate-ModPackage scripts.
//
// Run with: dotnet run scripts/Validate-ModPackage.cs -- <zip> <expected-version> [mod-id] [ref-dir]
//
// Checks:
//   - Archive layout: exactly one top-level folder named <mod-id> containing the
//     runtime DLL (FUSE.dll), Info.json, a non-empty schemas/ tree and
//     assets/fuse_icon.png. No stray files, no *.pdb.
//   - Info.json: required fields present and non-empty; Version is the version
//     CORE (MAJOR.MINOR.PATCH, the only shape UMM's System.Version parser accepts)
//     and matches the expected core; AssemblyName matches the entry DLL;
//     ManagerVersion is a valid version; HomePage (if a non-empty string) is a
//     well-formed http(s) URL.
//   - DLL: EntryMethod from Info.json resolves to a public, static method returning
//     bool and taking exactly one UnityModManagerNet.UnityModManager.ModEntry
//     parameter — inspected via System.Reflection.MetadataLoadContext, so no Unity
//     runtime, no game launch, and no managed-assembly load side effects. This is
//     the check that actually proves UMM can call into the shipped binary.
//
// ref-dir must contain UnityModManager(Net).dll so the ModEntry parameter type
// resolves. In CI this is the game's managed UnityModManager folder, obtained from
// `dotnet msbuild FUSE/FUSE.csproj -getProperty:UnityModManagerDir` so it tracks
// whatever Paths.user / GameDir the build itself resolved. Locally it falls back to
// the UnityModManagerDir / GameDir environment variables, then to lib/refs.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

const string ExpectedEntryParam = "UnityModManagerNet.UnityModManager+ModEntry";

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: dotnet run scripts/Validate-ModPackage.cs -- <zip> <expected-version> [mod-id] [ref-dir]");
    return 2;
}

string zipPath = args[0];
string expectedVersion = args[1];
string modId = args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]) ? args[2] : "FUSE";
string? refDirArg = args.Length > 3 && !string.IsNullOrWhiteSpace(args[3]) ? args[3] : null;

// The entry DLL carries the UMM EntryMethod. The retired in-game editor and its
// converter dependency are intentionally not part of the runtime mod package.
string entryDll = $"{modId}.dll";

// UMM does not accept SemVer pre-release tags in Info.json (it parses Version as
// System.Version, which only allows numeric segments), so the build stamps Info.json
// with just the version core (e.g. 0.13.0 for a 0.13.0-rc.1 tag). Validate the core.
var coreMatch = Regex.Match(expectedVersion, @"^(?<core>[0-9]+\.[0-9]+\.[0-9]+)");
if (!coreMatch.Success)
{
    Console.Error.WriteLine($"ERROR: ExpectedVersion '{expectedVersion}' must start with MAJOR.MINOR.PATCH.");
    return 2;
}
string expectedVersionCore = coreMatch.Groups["core"].Value;

// PR/push CI builds intentionally stamp the 0.0.0 core (the artifact identity lives
// in the pre-release suffix, e.g. 0.0.0-pr42.abc1234), so only treat a literal 0.0.0
// in Info.json as a stamping failure when the caller expected a real version.
bool expectPlaceholderVersion = expectedVersionCore == "0.0.0";

string refDir = ResolveRefDir(refDirArg);

if (!File.Exists(zipPath))
{
    Console.Error.WriteLine($"ERROR: ZipPath '{zipPath}' does not exist.");
    return 2;
}

var failures = new List<string>();
void Fail(string m) => failures.Add(m);
void Require(bool cond, string m) { if (!cond) failures.Add(m); }

// Verifies that a manifest field is present and has the expected JSON type. Without
// this, a payload like `"ManagerVersion": 1` would silently bypass the string checks
// below because they only run when the value is the right kind.
void RequireField(JsonElement obj, string source, string field, JsonValueKind expected, bool requireNonEmpty = false)
{
    if (!obj.TryGetProperty(field, out var el))
    {
        Fail($"{source}: '{field}' is missing.");
        return;
    }
    if (el.ValueKind != expected)
    {
        Fail($"{source}: '{field}' must be {expected.ToString().ToLowerInvariant()}; got {el.ValueKind.ToString().ToLowerInvariant()}.");
        return;
    }
    if (requireNonEmpty && expected == JsonValueKind.String && string.IsNullOrWhiteSpace(el.GetString()))
    {
        Fail($"{source}: '{field}' must be non-empty.");
    }
}

string tempRoot = Path.Combine(Path.GetTempPath(), "modpkg-validate-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

try
{
    ZipFile.ExtractToDirectory(zipPath, tempRoot);

    // 1. Layout: exactly one top-level folder, named <mod-id>.
    var rootEntries = new DirectoryInfo(tempRoot).GetFileSystemInfos();
    if (rootEntries.Length != 1)
    {
        Fail($"Zip must contain exactly one top-level entry; found {rootEntries.Length}: {string.Join(", ", rootEntries.Select(e => e.Name))}");
    }

    var modFolder = rootEntries.OfType<DirectoryInfo>().FirstOrDefault();
    if (modFolder is null)
    {
        Fail("Zip top-level entry is not a directory.");
        return Report();
    }

    Require(modFolder.Name == modId, $"Top-level folder must be '{modId}'; got '{modFolder.Name}'.");

    string infoPath = Path.Combine(modFolder.FullName, "Info.json");
    string entryDllPath = Path.Combine(modFolder.FullName, entryDll);

    bool infoExists = File.Exists(infoPath);
    bool entryDllExists = File.Exists(entryDllPath);

    Require(infoExists, "Info.json missing from mod folder.");
    Require(entryDllExists, $"{entryDll} missing from mod folder.");

    // schemas/ ships the JSON schemas FUSE validates mods against at runtime; an
    // empty or absent tree means the zip is broken.
    var schemasDir = new DirectoryInfo(Path.Combine(modFolder.FullName, "schemas"));
    Require(schemasDir.Exists, "schemas/ folder missing from mod folder.");
    if (schemasDir.Exists)
    {
        Require(schemasDir.GetFiles("*", SearchOption.AllDirectories).Length > 0,
            "schemas/ folder is present but empty.");
    }

    Require(File.Exists(Path.Combine(modFolder.FullName, "assets", "fuse_icon.png")),
        "assets/fuse_icon.png missing from mod folder.");

    // AGPL-3.0 conveys with the binary, so a zip without it is a compliance gap.
    Require(File.Exists(Path.Combine(modFolder.FullName, "LICENSE")),
        "LICENSE missing from mod folder (AGPL-3.0 requires conveying the license with the binary).");

    // No stray files. The allow-list is the known flat files plus anything under
    // schemas/ and assets/ (both are controlled, recursively-copied trees). Anything
    // else — a stray DLL, a doc XML, a .DS_Store — gets flagged. *.pdb is called out
    // separately because shipping debug symbols in a Release zip is its own smell and
    // could otherwise hide under an allowed directory.
    var allowedFlat = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Info.json",
        entryDll,
        // FUSE ships under the AGPL-3.0, which requires conveying the license
        // text along with the binary. The release workflow copies the repo-root
        // LICENSE into every mod zip.
        "LICENSE",
    };

    bool IsAllowed(string rel) =>
        allowedFlat.Contains(rel) ||
        rel.StartsWith("schemas/", StringComparison.OrdinalIgnoreCase) ||
        rel.StartsWith("assets/", StringComparison.OrdinalIgnoreCase);

    var allFiles = modFolder.GetFiles("*", SearchOption.AllDirectories)
        .Select(f => Path.GetRelativePath(modFolder.FullName, f.FullName).Replace('\\', '/'))
        .ToList();

    var extras = allFiles.Where(rel => !IsAllowed(rel)).ToList();
    if (extras.Count > 0)
    {
        // Print what this build actually saw. A stray-file failure is otherwise
        // hard to tell apart from the validator running different code than the
        // checkout (stale build cache, wrong working directory), which is exactly
        // the ambiguity that stalled the 1.0.0 dry runs.
        Console.Error.WriteLine("Stray-file check details:");
        Console.Error.WriteLine("  allow-list: " + string.Join(", ", allowedFlat.OrderBy(x => x)));
        Console.Error.WriteLine("  mod folder: " + string.Join(", ", allFiles.OrderBy(x => x)));
        Fail("Mod folder contains unexpected entries: " + string.Join(", ", extras));
    }

    var pdbs = allFiles.Where(rel => rel.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)).ToList();
    if (pdbs.Count > 0)
        Fail("Mod folder ships debug symbols (Release zip should not): " + string.Join(", ", pdbs));

    // 2. Info.json
    // Parse inside a using and Clone the root: JsonDocument pools its backing
    // buffers and must be disposed, but a cloned JsonElement is independent of the
    // document's lifetime, so the checks below can use it after the doc is gone.
    JsonElement? info = null;
    if (infoExists)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(infoPath));
            info = doc.RootElement.Clone();
        }
        catch (Exception ex) { Fail($"Info.json failed to parse as JSON: {ex.Message}"); }
    }

    if (info is JsonElement i)
    {
        // Required string fields: present, of JSON-string type, and non-empty.
        foreach (var field in new[] { "Id", "DisplayName", "Author", "Version", "AssemblyName", "EntryMethod", "ManagerVersion" })
        {
            RequireField(i, "Info.json", field, JsonValueKind.String, requireNonEmpty: true);
        }

        if (TryGetString(i, "Id", out var id))
            Require(id == modId, $"Info.json: Id should be '{modId}'; got '{id}'.");

        if (TryGetString(i, "AssemblyName", out var asmName))
            Require(asmName == entryDll, $"Info.json: AssemblyName should be '{entryDll}'; got '{asmName}'.");

        if (TryGetString(i, "Version", out var ver))
        {
            Require(Regex.IsMatch(ver, @"^[0-9]+\.[0-9]+\.[0-9]+$"),
                $"Info.json: Version '{ver}' must be MAJOR.MINOR.PATCH (UMM does not accept pre-release suffixes).");
            Require(expectPlaceholderVersion || ver != "0.0.0",
                "Info.json: Version is still placeholder '0.0.0' (InjectModVersionIntoInfoJson target failed?).");
            Require(ver == expectedVersionCore,
                $"Info.json: Version '{ver}' does not match expected core '{expectedVersionCore}' (derived from '{expectedVersion}').");
        }

        if (TryGetString(i, "ManagerVersion", out var mv))
            Require(Regex.IsMatch(mv, @"^[0-9]+\.[0-9]+(\.[0-9]+)?$"), $"Info.json: ManagerVersion '{mv}' is not a valid version string.");

        // HomePage is optional. FUSE currently ships it as an empty string, which UMM
        // treats as "unset", so only validate it as a URL when it is a non-empty string.
        // A present-but-wrong-typed value (number, object) is still a failure.
        if (i.TryGetProperty("HomePage", out var hpEl))
        {
            if (hpEl.ValueKind != JsonValueKind.String)
            {
                Fail($"Info.json: 'HomePage' must be a string when present; got {hpEl.ValueKind.ToString().ToLowerInvariant()}.");
            }
            else
            {
                var hp = hpEl.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(hp))
                {
                    // Uri.TryCreate is stricter than a "scheme + non-whitespace" regex —
                    // it rejects malformed authorities like 'http://:' while still
                    // accepting normal http(s) URLs.
                    bool wellFormed = Uri.TryCreate(hp, UriKind.Absolute, out var hpUri)
                                      && (hpUri.Scheme == Uri.UriSchemeHttp || hpUri.Scheme == Uri.UriSchemeHttps);
                    Require(wellFormed, $"Info.json: HomePage '{hp}' must be a well-formed http(s) URL.");
                }
            }
        }
    }

    // 3. DLL: EntryMethod resolves to public static bool Load(ModEntry)
    if (entryDllExists && info is JsonElement i2 && TryGetString(i2, "EntryMethod", out var entry) && !string.IsNullOrWhiteSpace(entry))
    {
        int dotIdx = entry.LastIndexOf('.');
        if (dotIdx <= 0)
        {
            Fail($"Info.json: EntryMethod '{entry}' must be a fully-qualified Type.Method name.");
        }
        else
        {
            string typeName = entry[..dotIdx];
            string methodName = entry[(dotIdx + 1)..];

            if (!Directory.Exists(refDir))
            {
                Fail($"Reference assembly directory '{refDir}' not found; cannot resolve the EntryMethod parameter type. " +
                     "Pass the game's UnityModManager folder as the 4th argument, or set UnityModManagerDir / GameDir.");
            }
            else
            {
                ValidateDll(entryDllPath, typeName, methodName, entry, refDir, failures);
            }
        }
    }
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
}

return Report();

int Report()
{
    if (failures.Count == 0)
    {
        Console.WriteLine($"Mod package validation OK: {zipPath}");
        return 0;
    }

    Console.Error.WriteLine($"Mod package validation FAILED ({failures.Count} issue(s)):");
    foreach (var f in failures)
    {
        Console.Error.WriteLine($"  - {f}");
    }
    return 1;
}

// Resolves the directory that holds UnityModManager(Net).dll. Explicit arg wins;
// otherwise mirror the build's resolution order (env, then the conventional layout
// under GameDir), and finally the vendored lib/refs a contributor may have created.
static string ResolveRefDir(string? arg)
{
    if (!string.IsNullOrWhiteSpace(arg)) return arg;

    var umm = Environment.GetEnvironmentVariable("UnityModManagerDir");
    if (!string.IsNullOrWhiteSpace(umm)) return umm;

    var gameDir = Environment.GetEnvironmentVariable("GameDir");
    if (!string.IsNullOrWhiteSpace(gameDir))
        return Path.Combine(gameDir, "Railroader_Data", "Managed", "UnityModManager");

    return Path.Combine(Directory.GetCurrentDirectory(), "lib", "refs");
}

static bool TryGetString(JsonElement obj, string field, out string value)
{
    value = "";
    if (obj.TryGetProperty(field, out var el) && el.ValueKind == JsonValueKind.String)
    {
        value = el.GetString() ?? "";
        return true;
    }
    return false;
}

static void ValidateDll(string dllPath, string typeName, string methodName, string entryDisplay, string refDir, List<string> failures)
{
    var bclDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
    var bclDlls = Directory.GetFiles(bclDir, "*.dll");
    var refDlls = Directory.GetFiles(refDir, "*.dll");

    // PathAssemblyResolver throws if two inputs share an assembly simple name. The
    // BCL and the UMM folder don't normally collide, but dedupe defensively (keeping
    // the BCL copy) so a stray duplicate in the game folder can't crash the validator.
    var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var path in bclDlls.Concat(refDlls).Concat(new[] { dllPath }))
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (!byName.ContainsKey(name)) byName[name] = path;
    }

    Assembly asm;
    MetadataLoadContext ctx;
    try
    {
        var resolver = new PathAssemblyResolver(byName.Values);
        ctx = new MetadataLoadContext(resolver);
        asm = ctx.LoadFromAssemblyPath(dllPath);
    }
    catch (Exception ex)
    {
        failures.Add($"DLL: failed to load '{Path.GetFileName(dllPath)}' for inspection: {ex.Message}");
        return;
    }

    using (ctx)
    {
        Type? type;
        try { type = asm.GetType(typeName, throwOnError: false, ignoreCase: false); }
        catch (Exception ex)
        {
            failures.Add($"DLL: failed to resolve type '{typeName}' (from EntryMethod '{entryDisplay}'): {ex.Message}");
            return;
        }

        if (type is null)
        {
            failures.Add($"DLL: type '{typeName}' (from EntryMethod '{entryDisplay}') not found in '{Path.GetFileName(dllPath)}'.");
            return;
        }

        // GetMethods(name, flags) throws AmbiguousMatchException on overloads, and the
        // base-type/parameter-type walk can throw if a referenced assembly is missing
        // from refDir. Enumerate + inspect inside a try so the validator emits a
        // structured error (e.g. "UnityModManager.dll not in ref-dir") instead of crashing.
        try
        {
            var candidates = type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static | BindingFlags.Instance)
                .Where(m => m.Name == methodName)
                .ToList();
            if (candidates.Count > 1)
            {
                failures.Add($"DLL: EntryMethod '{entryDisplay}' is ambiguous; '{typeName}' declares {candidates.Count} methods named '{methodName}'.");
                return;
            }

            var method = candidates.FirstOrDefault();
            if (method is null)
            {
                failures.Add($"DLL: method '{methodName}' not found on type '{typeName}'.");
                return;
            }

            if (!method.IsPublic) failures.Add($"DLL: EntryMethod '{entryDisplay}' must be public.");
            if (!method.IsStatic) failures.Add($"DLL: EntryMethod '{entryDisplay}' must be static.");
            if (method.ReturnType.FullName != "System.Boolean")
                failures.Add($"DLL: EntryMethod '{entryDisplay}' must return bool; returns '{method.ReturnType.FullName}'.");

            var ps = method.GetParameters();
            if (ps.Length != 1)
            {
                failures.Add($"DLL: EntryMethod '{entryDisplay}' must take exactly 1 parameter; got {ps.Length}.");
            }
            else if (ps[0].ParameterType.FullName != ExpectedEntryParam)
            {
                failures.Add($"DLL: EntryMethod '{entryDisplay}' parameter must be '{ExpectedEntryParam}'; got '{ps[0].ParameterType.FullName}'.");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"DLL: failed to inspect EntryMethod '{entryDisplay}': {ex.Message} " +
                         "(is UnityModManager(Net).dll present in the reference directory?)");
        }
    }
}
