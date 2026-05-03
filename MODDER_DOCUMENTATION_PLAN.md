# Modder Documentation Plan

Goal: Make FUSE usable by modders who do not already know the current codebase, the legacy stack, or the debugging history.

## Documentation Set

| Document | Status | Priority | Audience | Purpose |
| --- | --- | --- | --- | --- |
| Quickstart: First FUSE Data Package | MISSING | Critical | New FUSE modders | Create a minimal UMM data package and see it load in game. |
| Package Layout And `Info.json` | PARTIAL | Critical | All modders | Explain `FuseDataFile`, `FuseDataFiles`, `FuseAssetPacks`, dependencies, disabled packages, and load priority. |
| Schema Reference | PARTIAL | High | Data authors and tool authors | Expand `schemas/FUSE_JSON_SCHEMA.md` with all current fields and examples. |
| Track Graph Authoring | MISSING | Critical | Route authors | Explain nodes, segments, spans, areas, groups, removals, and graph IDs. |
| Span Authoring Diagrams | MISSING | Critical | Route authors | Explain upper/lower, Start/A, End/B, distance, normalized, and invalid crossings. |
| Operations And Industries | MISSING | Critical | Route authors | Explain loads, industries, areas, component types, loaders, stations, and ordering. |
| World Objects | MISSING | High | Route/scenery authors | Explain scenery, scene clones, splineys, map labels, map masks, map tiles, spawn points, and telegraph poles. |
| Asset And Prefab URIs | MISSING | High | Asset pack authors | Explain `vanilla://`, `scenery://`, `path://scene`, `empty://`, and future/reserved schemes. |
| Audio Packages | MISSING | High | Sound mod authors | Explain whistles, horns, bells, audio files, asset-pack wrappers, and customization UI behavior. |
| Progression And Unlocks | MISSING | Medium | Narrative route authors | Explain sections, delivery phases, map features, unlock fields, and current limitations. |
| World Suppression | PARTIAL | High | Route authors | Explain group, area, and experimental scene-path suppression with risks. |
| Authoring/Editor API | MISSING | High | Tool authors | Explain authoring entities, editable attributes, registry, persistence, save, capture, and rebuild. |
| Runtime API Reference | MISSING | High | C# mod authors | List public API methods, call timing, errors, and examples. |
| Console Commands | MISSING | Medium | Users and testers | Explain `/fuse.report`, `/fuse.loaded`, `/fuse.conflicts`, `/fuse.patches`, `/fuse.reapply`, `/fuse.restore`, and related commands. |
| Troubleshooting | MISSING | Critical | Everyone | Diagnose faulted packages, unknown assets, missing spans, dependency cycles, patch failures, and map-load reports. |
| Legacy Migration Guide | PARTIAL | Critical | Legacy mod authors | Explain conversion steps, unsupported features, and manual fix points. |

## Recommended Writing Order

1. Quickstart: First FUSE Data Package.
2. Package Layout And `Info.json`.
3. Track Graph Authoring plus Span Authoring Diagrams.
4. Operations And Industries.
5. World Objects.
6. Troubleshooting And Console Commands.
7. Legacy Migration Guide.
8. Audio Packages.
9. Progression And Suppression.
10. Runtime API Reference.
11. Authoring/Editor API.

## Required Examples

Each example should be small enough to paste into a package and test.

- Minimal package with only metadata.
- Add one map label.
- Add one scenery object.
- Add one node, segment, and span.
- Remove one base-game track segment by real graph ID.
- Add one load and one simple industry.
- Add one passenger stop and station.
- Add one turntable with generated pit nodes.
- Add one road spliney, one river spliney, and one trestle spliney.
- Add one map tile overlay source.
- Add one whistle, horn, and bell.
- Add one progression section with a delivery phase.
- Suppress one track group and one area.

## Topics That Need Extra Clarity

### Track IDs

Base-game removals must use actual graph IDs. Modders should not write friendly names like `Old Spur` or `Sold-siding` unless those are real graph IDs in the live graph. FUSE-authored objects can use descriptive package-owned IDs.

### Spans

A span is not a siding name. It is two measured locations on one or more track segments. On the same segment, the endpoints must face each other: one from Start/A, one from End/B. Distances must not exceed segment length.

### Legacy Names

Docs should teach FUSE-native names. Legacy names can be documented as converter input only:

- `AlinasMapMod.PaxStationComponent` -> `passengerStop`
- Strange Customs `FlowyThingBuilder` -> `road` or `river` based on style/profile
- Legacy `mandelas` -> FUSE `world.sceneClones`

### Experimental Features

Docs must mark the following as experimental or alpha-risk:

- Early scene-path suppression.
- Runtime authoring mutations.
- `/fuse.reapply`.
- `/fuse.restore`.

### Conversion Troubleshooting

Every conversion guide should explain how to read:

- The in-game map-load toast.
- `/fuse.report`.
- `/fuse.loaded`.
- `/fuse.conflicts`.
- `Player.log` for stack traces only after the FUSE report points at a package.

## Documentation Folder Proposal

```text
docs/
  quickstart.md
  package-layout.md
  track-authoring.md
  spans.md
  operations-industries.md
  world-objects.md
  assets-prefabs.md
  audio.md
  progression.md
  suppression.md
  authoring-editor.md
  runtime-api.md
  console-commands.md
  troubleshooting.md
  legacy-migration.md
examples/
  HelloFuse/
  TrackBasics/
  IndustryBasics/
  WorldBasics/
  AudioBasics/
  ProgressionBasics/
```

## Definition Of Done For Public Alpha Docs

- A new modder can install FUSE, copy an example package, and see an object in game.
- A route author can understand spans without reading C#.
- A converter user can tell whether a problem is a schema error, runtime missing asset, dependency issue, or unsupported legacy feature.
- A C# modder can tell which APIs are stable and when to call them.
- Known unsupported legacy features are named directly.

