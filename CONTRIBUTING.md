# Contributing to FUSE

Thanks for your interest in FUSE. This page covers getting a build running, the
conventions the codebase follows, and how changes get merged.

## Licensing

FUSE is licensed under the **GNU Affero General Public License v3.0**. By
contributing, you agree that your contributions are licensed under the AGPL-3.0 as
well. See [LICENSE](LICENSE).

## Getting Set Up

### Requirements

- **Visual Studio** with the .NET Framework 4.8 SDK and C# support, or another
  editor plus the .NET SDK.
- A **.NET 10 SDK** — several projects in the solution target `net10.0`.
- **Railroader** — optional. See below.

### Build

```bash
git clone https://github.com/F-U-S-E-E/FuseDevelopmentGroup.git
cd FuseDevelopmentGroup
cp Paths.user.example Paths.user
```

Edit `Paths.user` to point at your local Railroader install:

- `GameDir` — the directory containing `Railroader.exe`
- `FuseLogPath` / `PlayerLogPath` — the two logs, both under
  `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader`
- `EnableModDeploy` — `true` by default, which deploys each build straight into
  your `Railroader\Mods` folder

Then build:

```bash
dotnet build FUSE.Editor/FUSE.Editor.csproj -c Debug -p:ModVersion=0.0.1-dev
```

That is the entry project CI builds, and it pulls in the rest of the mod.

### Building Without Railroader

You do not need the game installed to build or to contribute. When neither
`GameDir` nor `UnityManagedDir` is set, the build falls back to the checked-in
reference assemblies under `lib/refs` and still resolves the Unity and Railroader
types.

This is how the game-free projects — `FUSE.ExternalEditor`, `FUSE.Core.Tests`,
`FUSE.ConverterCli` — build on ordinary CI runners.

## Project Layout

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for what each project does and
how the loading pipeline fits together.

## Testing

```bash
dotnet test FUSE.Tests/FUSE.Tests.csproj -c Debug
```

**`FUSE.Tests` targets net48 and uses xUnit 2.x. Do not migrate it to xUnit v3** —
the v3 runner requires .NET 8+ and cannot host net48 test assemblies that reference
`FUSE.dll` and the Unity managed types.

Automated coverage is limited to code with no `UnityEngine` dependency:
validation, serialization, dependency resolution, registry conflict logic, and
pure data conversion. Anything touching game or Unity types belongs in the in-game
golden-master harness, which does not run under `dotnet test`.

Write tests for the layer you are changing. A change to validation or conversion
logic should come with tests; a change to runtime world application generally
cannot, and is verified in-game instead.

## Code Conventions

Match the surrounding code. Beyond that:

- **Analyzers are on.** `AnalysisLevel` is `latest-minimum` with
  `EnforceCodeStyleInBuild`. Keep the build warning-clean.
- **CA1031 (catch-all `Exception`) is deliberately excluded.** Broad catches
  throughout the mod-loading path are intentional design: one broken third-party
  mod must not crash the host. Follow that pattern in loading code, and log what
  you caught.
- **net48 and `LangVersion` limit some modern C#.** Favor patterns that actually
  compile for the target.
- **Diagnostics should name the package.** A warning that says which package id,
  operation, object id, and field triggered it is worth several that do not.
- **Report authoring errors; do not hide them.** FUSE repairs what it safely can
  and reports the rest. Silently dropping invalid data is not an acceptable fix.

## Making Changes

1. Branch off `main`.
2. Keep the change focused — one concern per pull request.
3. Update the docs in the same PR when behavior changes. A new setting needs an
   entry in [docs/SETTINGS.md](docs/SETTINGS.md); a new console command needs one
   in [docs/CONSOLE_COMMANDS.md](docs/CONSOLE_COMMANDS.md).
4. Confirm the build is clean and tests pass.
5. Open a pull request against `main` using the PR template.

Changes that touch world application, the track graph, or progression need
in-game verification, not just a green test run. Say what you tested and on which
route.

### Commit Messages

The history uses conventional-commit prefixes — `feat:`, `fix:`, `perf:`, `docs:`,
`ci:`, with an optional scope such as `fix(settings):`. Follow that.

## Reporting Bugs

Use [GitHub Issues](https://github.com/F-U-S-E-E/FuseDevelopmentGroup/issues) and
the bug report template. Include `FUSE.log`, `Player.log`, and `/fuse.report json`
output — see the checklist in [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md).

Security issues go through [SECURITY.md](SECURITY.md) instead, not the public
tracker.

## Releasing

Maintainers only — see [docs/RELEASING.md](docs/RELEASING.md). Releases are driven
entirely by pushing a `mod-v*` or `externaleditor-v*` tag.
