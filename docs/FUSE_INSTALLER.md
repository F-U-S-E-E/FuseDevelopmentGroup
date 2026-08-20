# FUSE Installer

`fuse_installer.py` installs mod packages from zip files into the base folder's
`Mods` directory. It is designed for a bundled `FUSE-Installer.exe`, so users can
drop zip files beside the executable and run it from the base folder.

The published `FUSE-Installer.exe` carries the FUSE framework inside it, so it
works two ways:

- **Double-click it (no arguments): it installs FUSE.** This is the easiest way
  for a player to get FUSE running.
- **Drag one or more mod `.zip` files onto it: it installs those mods.** A
  drag-and-drop run installs exactly the mods you dropped and does not touch
  FUSE.

A no-argument run also installs any loose `.zip` files sitting beside the exe (in
addition to FUSE), so the "drop several zips in the folder and run once" workflow
still works. Existing mod folders are backed up and updated by default. Pass
`--skip-existing` to leave them alone, or `--no-fuse` to process only the loose
zips without installing the bundled FUSE package.

## Download

The published `FUSE-Installer.exe` is distributed via GitHub Releases:
<https://github.com/F-U-S-E-E/FuseDevelopmentGroup/releases>. The `dist/` folder is gitignored,
so building locally produces a private copy at `dist\FUSE-Installer.exe`.

The installer inspects zip structure and manifest JSON only. It does not import,
execute, or depend on any package code.

When FUSE is installed, the installer also creates a data-only UMM entry with
the historical id `AssetLoader`. This entry contains no DLL and runs no patches;
it exists so older mod manifests that require `AssetLoader` still pass UMM's
dependency check before their content is loaded through FUSE.

UMM code mods are reflected before their entry method runs. The installer also
checks each installed code DLL (without executing it) for references to the
RailLoader Interchange/Injector or Strange Customs contracts that FUSE replaces.
When found, it adds `FUSE` to that mod's `Requirements` and `LoadAfter` fields so
FUSE's compatibility resolver is active before UMM reflects the DLL. The
original `Info.json` is copied to a dated
`Mods\ModBackups\FUSEInstaller\CompatibilityManifests-*` folder first. This is
what allows separately installed UMM packages such as Alina Utilities to start
with the old loader DLLs absent. RailLoader-hosted data/code packages such as
Signals Everywhere follow the separate hosted-package path described below.

## Supported zips

- UMM packages with `Info.json`.
- FUSE data packages with `Info.json` and FUSE data markers such as
  `FuseDataFiles`, `FuseAssetPacks`, or a FUSE requirement.
- RailLoader packages with `Definition.json`, including data packages and
  code-only hosted plugins such as Signals Everywhere.
- Multi-package zips laid out as `Mods/PackageA/...` and `Mods/PackageB/...`.

Each archive is inspected and staged before anything replaces an installed mod.
One bad archive does not stop the remaining archives. The final table lists every
package as `INSTALLED`, `UPDATED`, `SKIPPED`, or `FAILED`.

Before writing any package, installer 0.7 scans the complete batch and checks:

- unsafe, absolute, parent-traversing, duplicate, or case-colliding ZIP paths;
- ambiguous layouts where one package manifest contains another package;
- duplicate package ids across all selected ZIPs (including `.FUSE` aliases);
- `Requirements`, `FuseRequires`, and RailLoader `requires`, including
  `NotBefore`/`NotAfter` version bounds;
- dependencies already installed or supplied later in the same batch; and
- dependency failure propagation, so a package that fails preflight cannot
  satisfy another package just by being present in the batch.

Missing dependency results name the requester, dependency id, version bounds,
and corrective action. FUSE satisfies only the legacy contracts it explicitly
replaces (for example Alina's Map Mod, Strange Customs, Confusing Supplements,
RailLoader Injector/Interchange, and AssetLoader); it does not broadly waive
unrelated ZAMU or third-party requirements. Retired legacy version bounds are
ignored only for those explicit FUSE replacement contracts. If an installed
dependency has no readable version, the installer keeps the package eligible
but records that the bound could not be verified.

## User flow

To install FUSE:

1. Install Unity Mod Manager for Railroader.
2. Double-click `FUSE-Installer.exe`. It searches registered Steam libraries for
   Railroader; use **Browse** if you have more than one copy or a non-Steam
   install.
3. Leave **Install/update the bundled FUSE framework** checked.
4. Add any mod zips you also want to install, then click **Install**.
5. Read the package-by-package result list before closing the window.

FUSE is written to `Mods\FUSE`.

To install mods:

1. Drag one or more `.zip` files onto `FUSE-Installer.exe`, or open the installer
   and use **Add zips**.
2. Confirm the detected Railroader folder and click **Install**.
3. Each package is written to `Mods\<package id>`. Native FUSE, UMM, and hosted
   RailLoader-format packages can be selected together.

Existing folders are moved to
`Mods\ModBackups\FUSEInstaller\<timestamp>` before the new copy is installed.
Use `--skip-existing` only when you intentionally do not want updates.
Every non-dry run writes a machine-readable result under
`Mods\FUSEInstaller\Reports` so a failed package can be diagnosed without a
screenshot of the installer window. The report's `compatibilityActions` list
records each AssetLoader backup, alias installation, verification failure, or
blocked migration with its source and destination paths. Each package record
also preserves the dependency ids and version bounds used by preflight.

## Removing the old loader safely

Before installing, the tool checks the actual game folder, verifies Unity Mod
Manager is present, and looks for known legacy loader files in
`Railroader_Data\Managed`: `Railloader.dll`, `Railloader.Injector.dll`,
`Railloader.Interchange.dll`, and `StrangeCustoms.dll`.

If any are found, the installer stops and asks permission to move only those
exact files into a dated backup under
`Mods\ModBackups\FUSEInstaller\LegacyLoader-<timestamp>`. It does not silently
delete or patch game files. For an unattended repair, pass
`--repair-legacy-loader`.

Steam's **Verify integrity of game files** is useful if a game-owned file was
modified, but it may leave extra third-party DLLs behind. Verification therefore
does not replace the installer's explicit legacy-file check.

### Replacing AssetLoader

FUSE owns all three behaviors exposed by AssetLoader 1.0.1: package-root and
child `Catalog.json` store discovery, direct mod-folder store paths, and
definitions-only child-folder overrides used by rolling-stock/tender swaps.

During a FUSE install or update, the installer checks `Mods` for:

- an old `AssetLoader` UMM folder or any immediate mod folder containing
  `AssetLoader.dll`;
- a loose `Mods\AssetLoader.dll`;
- a loose `Mods\AssetLoader.zip`.

With approval, those exact paths are moved to
`Mods\ModBackups\FUSEInstaller\AssetLoader-<timestamp>`, then the installer
creates `Mods\AssetLoader\Info.json` as the data-only dependency alias. It
verifies that no old AssetLoader runtime remains. Use
`--repair-asset-loader` for an unattended migration. Dragging the old
AssetLoader ZIP onto the FUSE installer does not reinstall its DLL.

Do not simply delete AssetLoader by hand while older packages still declare it
as a UMM requirement. Either use the FUSE installer so the alias is created, or
update those package manifests to require `FUSE` instead.

## Command examples

Install every zip in the current folder:

```powershell
.\FUSE-Installer.exe
```

Install explicit zips:

```powershell
.\FUSE-Installer.exe .\MyPackage.zip .\OtherPackage.zip
```

Force command-line mode (the published executable opens the graphical installer
by default):

```powershell
.\FUSE-Installer.exe --cli .\MyPackage.zip .\OtherPackage.zip --with-fuse
```

Inspect without writing:

```powershell
.\FUSE-Installer.exe --dry-run
```

Install from a different location:

```powershell
.\FUSE-Installer.exe --game-dir "D:\Games\BaseFolder" --inbox "D:\Downloads\Mods"
```

Archive processed zips after successful installs:

```powershell
.\FUSE-Installer.exe --archive-zips
```

## Building the exe

From the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build_installer_exe.ps1
```

If PyInstaller is not installed for the active Python environment:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build_installer_exe.ps1 -InstallPyInstaller
```

The output is `dist\FUSE-Installer.exe`.

### Bundling FUSE into the exe

Pass `-FusePayload` with the core FUSE mod zip (the `FUSE-v*.zip` produced by the
release build) to bundle FUSE inside the exe. A manual run of that exe then
installs FUSE:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build_installer_exe.ps1 -FusePayload .\FUSE-v1.0.2.zip
```

The release workflow does this automatically, passing the zip it just built. If
you build without `-FusePayload`, the exe still installs mods from dragged zips,
but a no-argument run has no FUSE to install and reports that instead. After a
bundled build, the script performs a real install into a throwaway fake game
folder and confirms `Mods\FUSE\Info.json` was written. Installer tests also
verify AssetLoader backup, DLL removal, and dependency-alias creation.
