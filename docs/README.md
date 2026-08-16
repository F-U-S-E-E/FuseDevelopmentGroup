# FUSE Documentation

FUSE is a Unity Mod Manager modding layer for Railroader. Start with the section
that matches what you are doing.

## Players

New to FUSE, or installing it on a fresh setup.

| Doc | What it covers |
| --- | --- |
| [Getting Started](GETTING_STARTED.md) | Install FUSE and your first packages, verify the load, update, uninstall |
| [FAQ](FAQ.md) | Common questions — legacy mods, multiplayer, saves, performance |
| [Settings](SETTINGS.md) | All 28 settings, their defaults, and where they live |
| [Console Commands](CONSOLE_COMMANDS.md) | Every `/fuse.*` command |
| [Troubleshooting](TROUBLESHOOTING.md) | Symptom → diagnostic → what to attach to a report |
| [Known Issues](KNOWN_ISSUES.md) | Current limitations and unsupported content |
| [Installer](FUSE_INSTALLER.md) | Installing packages from zips with `FUSE-Installer.exe` |

**Coming from Railloader, Strange Customs, or another legacy stack?** Read
[Migrating From Legacy Mods](MIGRATION_FROM_LEGACY.md) before installing anything.

## Package Authors

Building or converting FUSE packages.

| Doc | What it covers |
| --- | --- |
| [Package Author Guide](PACKAGE_AUTHOR_GUIDE.md) | The authoring contract — ids, dependencies, optional references |
| [JSON Schema Reference](../schemas/FUSE_JSON_SCHEMA.md) | The full data contract |
| [Converter](FUSE_CONVERTER.md) | Drag-and-drop, batch, and `fuse-convert` CLI conversion |
| [External Editor](EXTERNAL_EDITOR.md) | The standalone desktop editor |
| [Migration From Legacy](MIGRATION_FROM_LEGACY.md) | Converting legacy mods and reading the conversion report |

The machine-readable schema is at
[`schemas/fuse-mod.schema.json`](../schemas/fuse-mod.schema.json), with a worked
example in [`schemas/fuse-mod.example.json`](../schemas/fuse-mod.example.json).

## Contributors

Working on FUSE itself.

| Doc | What it covers |
| --- | --- |
| [Contributing](../CONTRIBUTING.md) | Build setup, testing policy, conventions, PR process |
| [Architecture](ARCHITECTURE.md) | Project layout, the loading pipeline, design rules |
| [Editor Architecture](EDITOR_ARCHITECTURE.md) | Editor design and target shape |
| [Editor UI Patterns](EDITOR_UI_PATTERNS_FROM_AXIOM.md) | UI reference notes |
| [Releasing](RELEASING.md) | Tag-driven release lanes (maintainers) |
| [Changelog](CHANGELOG.md) | Version history |
| [Security Policy](../SECURITY.md) | Reporting vulnerabilities |

## Offline Manuals

Printable PDF builds of the docs above:

- [FUSE User Manual](pdf/FUSE-User-Manual.pdf) — install, settings, commands, troubleshooting
- [FUSE Package Author Guide](pdf/FUSE-Package-Author-Guide.pdf) — authoring, schema, converter, editor

Rebuild them with `scripts/Build-Pdfs.py` after changing the markdown.

## Quick Answers

**Where are my logs?**
`%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE.log`

**Is it working?** Load a map and run `/fuse.report`. Zero faults and zero
conflicts is a clean load.

**A package isn't loading.** `/fuse.loaded` — not listed means undiscovered,
listed as faulted means it failed to apply.

**Filing a bug?** Attach `FUSE.log`, `Player.log`, `/fuse.report json` output, and
the package's `conversion-report.json`.

## Project

- **Repository:** <https://github.com/F-U-S-E-E/FuseDevelopmentGroup>
- **Releases:** <https://github.com/F-U-S-E-E/FuseDevelopmentGroup/releases>
- **Issues:** <https://github.com/F-U-S-E-E/FuseDevelopmentGroup/issues>
- **License:** [AGPL-3.0](../LICENSE)
