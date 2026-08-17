# FUSE Installer

`fuse_installer.py` installs mod packages from zip files into the base folder's
`Mods` directory. It is designed for a bundled `FUSE-Installer.exe`, so users can
drop zip files beside the executable and run it from the base folder.

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

1. Put `FUSE-Installer.exe` in the base folder.
2. Drop one or more `.zip` files in that same folder.
3. Run `FUSE-Installer.exe`.
4. The installer writes packages to `Mods\<package id>`.

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
