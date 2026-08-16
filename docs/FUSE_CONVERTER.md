# FUSE Converter

Official drag-and-drop converter for legacy Railroader data mods.

## Download

Prebuilt binaries are published on the GitHub Releases page rather than tracked in this repo:

- Windows exe: [FUSE-Converter.exe](https://github.com/F-U-S-E-E/FuseDevelopmentGroup/releases/latest/download/FUSE-Converter.exe)
- Portable Python zipapp: [FUSEConvertFolder.pyz](https://github.com/F-U-S-E-E/FuseDevelopmentGroup/releases/latest/download/FUSEConvertFolder.pyz)

All releases live at <https://github.com/F-U-S-E-E/FuseDevelopmentGroup/releases>.

Both tools are attached to the mod release (`mod-v*` tag), so `latest/download`
resolves to the converter built alongside the FUSE build you are running.

## Quick Use

Drag any of these onto `ConvertToFUSE.cmd` at the repo root:

- a legacy route / track mod folder
- a legacy route / track mod `.zip`
- a legacy horn / whistle / bell folder or `.zip`
- a legacy asset-pack folder or `.zip`
- a single legacy JSON data file

`ConvertToFUSE.cmd` launches the official FUSE converter.

The repository also includes the FUSE converter icon at:

`tools/assets/fuse_converter.ico`

## Building The EXE

From the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build_converter_exe.ps1 -InstallPyInstaller
```

That builds:

`dist/FUSE-Converter.exe`

The `dist/` folder is gitignored — locally-built binaries stay on your machine. Published builds are uploaded to GitHub Releases.

The generated exe uses the FUSE logo from `tools/assets/fuse_converter.ico`.
If PyInstaller is already installed, you can omit `-InstallPyInstaller`.

By default the converter writes to:

`C:\Steam\steamapps\common\Railroader\Mods`

If that folder does not exist, it writes to `converted` under the current working directory.

## Command Line

```powershell
python tools\fuse_converter.py "C:\Path\To\LegacyMod" --out "C:\Steam\steamapps\common\Railroader\Mods" --clean
```

## fuse-convert (.NET CLI)

`FUSE.ConverterCli` builds the `fuse-convert` binary: the C# converter
(`FUSE.Converter`) plus the FUSE.Core validator in one convert → validate →
report pipeline. Every validation finding is rendered with a fix-hint from the
catalog embedded in FUSE.Core — what's wrong, why it matters, and exactly how
to fix it.

```powershell
dotnet run --project FUSE.ConverterCli -- "C:\Path\To\LegacyMod" --out "C:\Steam\steamapps\common\Railroader\Mods" --clean
```

```text
fuse-convert <inputs...> [--out <dir>] [--kind auto|route|audio|asset] [--clean] [--batch]
             [--no-validate] [--strict] [--format console|json|markdown|all] [--quiet]
```

| Option | Meaning |
| --- | --- |
| `--out <dir>` | Output root; each mod converts into `<dir>\<ModFolder>.FUSE`. Default: `.\FUSEConverted`. |
| `--kind <kind>` | Force a package kind instead of auto-detection. |
| `--clean` | Replace an existing `.FUSE` output folder. |
| `--batch` | Treat each input as a container and convert every recognized child folder. |
| `--no-validate` | Skip validation (it is on by default). |
| `--strict` | Validation warnings also fail the run (exit 2), not just errors. |
| `--format <fmt>` | `console` (default) prints to stdout; `json` / `markdown` also write `conversion-report.json` / `conversion-report.md` into each output folder; `all` writes both. |
| `--quiet` | Suppress per-diagnostic output; print only summaries. |

Exit codes (CI can gate conversion and validation separately):

| Code | Meaning |
| --- | --- |
| `0` | Success — validation warnings allowed unless `--strict`. |
| `1` | Conversion failed. |
| `2` | Validation errors (or `--strict` with validation warnings). |
| `64` | Usage error. |

Unlike the Python tool, `fuse-convert` takes mod **folders** only — extract a
zip (or place a bare JSON in a mod folder) before converting.

## Linux / Folder Batch Mode

For Linux or anyone who wants to convert a whole folder at once:

```bash
python3 tools/fuse_converter.py --batch "/path/to/legacy-mod-folder"
```

That recursively scans the folder for recognized legacy mod folders, zip files,
and data JSON files, then writes output to:

`/path/to/legacy-mod-folder/FUSEConverted`

Or invoke the folder converter directly:

```bash
python3 tools/fuse_convert_folder.py "/path/to/legacy-mod-folder"
```

To build a single portable Python archive:

```bash
python3 tools/build_folder_converter_pyz.py
```

That creates:

`dist/FUSEConvertFolder.pyz`

Drop `FUSEConvertFolder.pyz` into a folder of legacy mods and run:

```bash
python3 FUSEConvertFolder.pyz
```

It converts every recognized child folder, zip, and data JSON into
`FUSEConverted`. Wrapper folders such as `Mods` are traversed automatically, and
zip files that contain multiple mods produce one `.FUSE` output per mod.

Useful options:

| Option | Meaning |
| --- | --- |
| `--out <folder>` | Output root for generated `.FUSE` folders. |
| `--clean` | Replace an existing generated `.FUSE` output folder under the output root. |
| `--kind route` | Force route/track/data conversion. |
| `--kind audio` | Force horn/whistle/bell conversion. |
| `--kind asset` | Force asset-pack wrapper conversion. |
| `--batch` | Recursively convert every recognized child mod, zip, or JSON in the input folder. |

## Outputs

Each converted package gets:

- `Info.json`
- one or more `*.fuse.json` files, or `audio.fuse.json`
- copied audio files or asset pack folders when needed
- `conversion-report.json`
- `conversion-report.md`

The report is the important bit. It records:

- detected package type
- generated files
- object counts
- warnings for unsupported or lossy legacy concepts
- errors if conversion failed

## Supported Inputs

| Legacy input | Output |
| --- | --- |
| Track / route JSON folders | One FUSE data file per source JSON, preserving file-per-concern structure. |
| Single route JSON file | One standalone FUSE package fragment. |
| Horn / whistle / bell packs | FUSE audio package with copied audio files. |
| Strange Customs asset packs | FUSE asset wrapper with `FuseAssetPacks` in `Info.json`. |
| Zip files | Extracted to a temporary folder, detected, converted, then cleaned up. |

## Known Lossy Areas

The converter warns when it sees concepts that still need manual verification:

- legacy formulaic `formula`
- progression `interchangeTransfers`
- unknown spline/object handlers
- unknown industry component types
- script binaries such as `.dll` / `.pdb`

Warnings do not always mean the package is unusable. They mean the conversion needs a look before calling it verified.

## Unsupported-Feature Policy

The converter should classify unsupported content instead of silently dropping it.

| Outcome | Meaning |
| --- | --- |
| `converted` | The source concept maps directly to supported FUSE schema/runtime behavior. |
| `repaired` | The converter changed invalid legacy data into valid equivalent FUSE data and reported the repair. |
| `preserved` | The source data is kept in `extensions` or package files for future/manual work, but does not have full runtime behavior yet. |
| `dependency-required` | The source entry can work when another package/asset/component dependency is installed. |
| `unresolved` | FUSE could not resolve a reference from the source data; the output keeps enough context for manual repair where possible. |
| `unsupported` | FUSE intentionally does not support the legacy behavior. The report must name the source file and concept. |
| `error` | Conversion failed for that input and the output should not be treated as usable. |

Current intentional unsupported/deferred areas:

- signals
- normal three-way switch authoring, because Railroader does not support it as a standard graph switch
- arbitrary legacy script logic from `.dll` files
- rolling stock/car/locomotive packages outside FUSE audio definitions
- unknown custom component types when their owning assembly is not installed or not converted to a FUSE component package

Unsupported entries must remain visible in `conversion-report.json` and `conversion-report.md`. A clean conversion is allowed to have `info` entries, but a supported package should not rely on hidden unsupported runtime behavior.
