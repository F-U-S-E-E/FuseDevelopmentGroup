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
still works. FUSE is skipped by default if it is already installed; pass
`--replace` to back up and reinstall it. Pass `--no-fuse` to process only the
loose zips without (re)installing FUSE.

## Download

The published `FUSE-Installer.exe` is distributed via GitHub Releases:
<https://github.com/F-U-S-E-E/FuseDevelopmentGroup/releases>. The `dist/` folder is gitignored,
so building locally produces a private copy at `dist\FUSE-Installer.exe`.

The installer inspects zip structure and manifest JSON only. It does not import,
execute, or depend on any package code.

## Supported zips

- UMM packages with `Info.json`.
- FUSE data packages with `Info.json` and FUSE data markers such as
  `FuseDataFiles`, `FuseAssetPacks`, or a FUSE requirement.
- Supported legacy data packages with `Definition.json` plus data JSON containing
  world, track, operations, or progression entries.
- Multi-package zips laid out as `Mods/PackageA/...` and `Mods/PackageB/...`.

## User flow

To install FUSE:

1. Put `FUSE-Installer.exe` in the base folder.
2. Double-click it.
3. FUSE is written to `Mods\FUSE`.

To install a mod:

1. Drag its `.zip` onto `FUSE-Installer.exe` (or drop several at once).
2. Each package is written to `Mods\<package id>`.

Existing folders are skipped by default. Run with `--replace` to move an existing
folder into `Mods\ModBackups\FUSEInstaller\<timestamp>` before installing the new
copy.

## Command examples

Install every zip in the current folder:

```powershell
.\FUSE-Installer.exe
```

Install explicit zips:

```powershell
.\FUSE-Installer.exe .\MyPackage.zip .\OtherPackage.zip
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
bundled build, the script runs a `--dry-run` self-check to confirm a manual run
would install FUSE.
