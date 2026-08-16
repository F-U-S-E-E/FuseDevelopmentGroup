# Architecture

An orientation for contributors: what each project is, how a package gets from
disk into the running game, and where the seams are.

For build setup, see [../CONTRIBUTING.md](../CONTRIBUTING.md).

## Projects

The solution splits along one main axis: **does this code need the game?**

### Game-side (net48, Unity + UMM)

| Project | Role |
| --- | --- |
| `FUSE` | The mod itself. Loads packages, applies them to the running world, owns the runtime UI and console commands. |
| `FUSE.Editor` | In-game editor. Also the entry project CI builds — it pulls in `FUSE`. |
| `FUSE.LiveBridge` | Optional in-game half of the hot-reload bridge. Ships as its own zip. |
| `FUSE.TestBridge` | In-game hooks for the golden-master test harness. |
| `FUSE.Tests` | xUnit 2.x tests. net48, pinned there deliberately. |

These target `net48` because that is what the Unity runtime hosts. They resolve
Unity and Railroader types from the game install, or from the checked-in
`lib/refs` reference assemblies when no game is present.

### Game-free (net10.0)

| Project | Role |
| --- | --- |
| `FUSE.Core` | The shared model, schema, validation, serialization, migrations, and geometry. Multi-targets `net48;net10.0` so both worlds use one implementation. |
| `FUSE.Converter` | Legacy → FUSE conversion. Also multi-targets. |
| `FUSE.ConverterCli` | The `fuse-convert` binary: convert → validate → report. |
| `FUSE.ExternalEditor` | Standalone Avalonia desktop editor. |
| `FUSE.LiveHarness` / `FUSE.TestCli` | Test tooling. |
| `FUSE.Core.Tests`, `FUSE.LiveHarness.Tests`, `FUSE.ExternalEditor.UiTests` | Tests for the game-free half. |

`FUSE.Core`'s multi-targeting is the load-bearing decision here. Validation and
schema logic run identically in the game, in the converter, and in the external
editor because they are literally the same code, not three implementations that
drift.

## Where The Testable Code Lives

Anything reachable without `UnityEngine` can be unit tested: validation,
serialization, dependency resolution, registry conflict logic, pure data
conversion. That is most of `FUSE.Core` and `FUSE.Converter`.

Anything touching game or Unity types cannot, and is verified by the in-game
golden-master harness instead. When adding logic, consider whether it can live on
the testable side of that line — usually it can.

## The Loading Pipeline

`FUSE/Loading` holds most of this.

### 1. Discovery

`FuseDataPackageDiscovery` walks `Railroader/Mods` for FUSE packages;
`FuseDefinitionFileDiscovery` finds the `*.fuse.json` files inside each one.
`FuseEarlyLoader` handles work that must happen before the game gets far into
startup.

UMM's enabled checkbox is honored here — a package UMM can see and has disabled is
marked disabled and its data files are not read.

### 2. Legacy conversion

`FuseLegacyDataConverter` (split across `.World`, `.Progression`, `.Components`,
`.Audio`, `.Json`, `.StringPatches`) translates legacy formats in memory.
`FuseLegacyJsonPatch` and `FuseLegacyContainerMixintoRegistry` handle legacy
patch and mixinto semantics.

This is the same conceptual work as the standalone converter, applied at load time
for compatibility rather than producing files on disk.

### 3. Validation and requirements

`FuseModLoader.Validation` runs the `FUSE.Core` validator.
`FuseModRequirementResolver` resolves dependencies and load order, honoring
`RailLoadPriority`, `RailLoadAfter`, and `RailLoadBefore`.

### 4. Apply

`FuseModLoader.Apply` writes definitions into the live world;
`FuseModLoader.TrackMerging` merges track graph changes into the existing graph.

`FuseApplyTransaction` and `FuseRegistryTransaction` make this recoverable rather
than a partial mutation on failure.

### 5. Ownership and conflicts

`FuseRegistry` records which package claims which object, keyed by
`FuseClaimKind`. Two packages claiming the same id produces a
`FuseRegistryConflict` — what `/fuse.conflicts` reports.

This is the mechanism behind the "never run a legacy route and its converted
equivalent together" rule: both claim the same ids.

### 6. Reporting

`FuseLoadReport` accumulates the outcome; `FusePackageFaultRegistry` records
per-package faults. This is what `/fuse.report` and the startup toast render.

A faulted package is isolated — `PackageApplyOutcome` carries the per-package
result so one failure does not take down unrelated packages.

## Design Rules

Three conventions explain most of what looks unusual in this codebase.

### Isolate third-party failure

One broken package must not crash the host. Broad `catch (Exception)` in the
loading path is intentional design, which is why CA1031 is excluded from the
analyzer set repo-wide. Catch, record against the package, keep going.

`FusePatchResilience` applies the same idea to Harmony patches: a patch that fails
to apply is recorded and skipped, not fatal. `/fuse.patches` lists both outcomes.

### Report, do not hide

FUSE repairs what it can safely repair and reports everything else. Unsupported
graph shapes, missing hard dependencies, invalid spans, and missing component
assemblies get reported rather than silently dropped. The converter's outcome
classification (`converted` / `repaired` / `preserved` / `dependency-required` /
`unresolved` / `unsupported` / `error`) is the same principle in the offline tool.

### Diagnostics name the package

A warning should identify the package id, operation, object id, and field. The
console commands and the various registries exist so that a user-reported problem
can be traced to a specific package and object without a debugger.

## Runtime Services

`FUSE/Runtime/Lifecycle` holds the services that run during play rather than at
load: deferred scenery activation, terrain rebaking, texture memory policy under
constrained VRAM, unused asset reclamation, frame spike and hitch detection, and
the runtime reload/rebind services behind the experimental console commands.

`FUSE/Infrastructure` holds cross-cutting concerns — logging, settings,
multiplayer guards, UMM injection, exception attribution to the owning mod, and
performance metrics.

## UI

`FUSE/Interface/MenuWindow` builds the in-game FUSE menu (Status and Tools tabs);
`FUSE/Interface/Console` registers the `/fuse.*` commands via Railroader's
`ConsoleCommandAttribute`. Adding a command means adding a class with that
attribute — and an entry in
[CONSOLE_COMMANDS.md](CONSOLE_COMMANDS.md).

## Related

- [../CONTRIBUTING.md](../CONTRIBUTING.md) — build and contribution process
- [EDITOR_ARCHITECTURE.md](EDITOR_ARCHITECTURE.md) — editor design and target shape
- [`../schemas/FUSE_JSON_SCHEMA.md`](../schemas/FUSE_JSON_SCHEMA.md) — the data contract
- [RELEASING.md](RELEASING.md) — how builds ship
