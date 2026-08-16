# FUSE Editor — Architecture

> **Status: retired.** FUSE no longer initializes or ships this in-game editor.
> The source remains as a historical design reference. Custom map loading and
> the standalone editor are separate systems and remain supported.

The FUSE editor was designed as its own top-level mode, inspired by Arma 3's
EDEN editor. This doc captures that historical target architecture.

## North Star

A FUSE mod author should be able to:

1. Launch Railroader → click **FUSE Editor** in the main menu (not inside a
   running save).
2. Pick a mod to edit (or create a new one) and a map context.
3. Land in a dedicated editor session: 3D world view + EDEN-style panels.
4. Place / move / configure FUSE entities (Nodes, Segments, Scenery,
   Industries, Stations…) directly in the world.
5. Click **Play Mod** to preview the mod inside a sandbox/company session
   with the mod's effects applied.
6. Press a return-to-editor affordance to come back to the editing view —
   the editing state must be exactly where it was, preview progress
   discarded (this is the EDEN contract: "the scenario will be in the same
   state in which you started it").

The editor is **separate** from in-game UI. It does not live as an overlay
inside a paused save. The earlier pause-menu prototype has been removed.

## Layout (mocked today; binding in follow-up)

```
+---------------------------------------------------------------------+
| FUSE Editor   •   Mod: my_mod_1.0          [Save] [Play ▶] [Exit ✕] |
+--------------------+----------------------------+-------------------+
|  Entities          |                            |  Properties       |
|  ─────────         |                            |  ─────────        |
|  ▼ Tracks          |    3D Viewport             |  Kind:  Node      |
|    ▼ Nodes (8)     |                            |  Id:    node-A0   |
|      node-A0       |    Selected entity gets    |  Position:        |
|      node-A1       |    a gizmo (move/rotate)   |    X 1234.5       |
|      …             |                            |    Y    0.0       |
|    ▶ Segments (7)  |                            |    Z -789.2       |
|    ▶ Spans  (0)    |                            |  Rotation:        |
|    ▶ Areas  (1)    |                            |    0° 90° 0°      |
|  ▼ World           |                            |  Group: main-line |
|    ▶ Scenery   (3) |                            |  Tags:  none      |
|    ▶ Splineys  (0) |                            |                   |
|    …               |                            |  [Delete]         |
|  ▼ Operations      |                            |                   |
|    ▶ Industries(2) |   [Select] [Move]          |                   |
|    ▶ Loads     (0) |   [Rotate] [Scale]         |                   |
|    …               |   [Place]                  |                   |
+--------------------+----------------------------+-------------------+
|  Editing: my_mod_1.0  •  Entities: 22  •  Last saved: 4m ago        |
+---------------------------------------------------------------------+
```

EDEN parallels:

- **Top bar** mirrors EDEN's file actions (Save) and preview affordances
  (Play). Exit Editor is FUSE-specific.
- **Left panel** mirrors EDEN's "entity overview / Layers" — a
  hierarchical, collapsible view of every entity in the document with
  per-category counts. Categories follow FUSE's authoring model
  (`FuseTrackDefinition`, `FuseWorldDefinition`, `FuseOperationsDefinition`).
- **Center 3D viewport** is the Railroader map with FUSE entities
  highlighted. Selected entity gets a gizmo.
- **Right panel** is per-selection attribute editors — EDEN's
  "Configuring Attributes" pattern.
- **Bottom bar** is mission-aware status (active mod, entity count,
  save state).

## Assemblies and lifecycle

`FUSE.dll`

- Owns the Harmony patches that touch game UI (the main-menu button).
- Holds the bridge interfaces (`IFuseEditorLifecycle`, `IFuseSelectionProvider`,
  `IFuseEditorProvider`) and the `FuseEditorBridge` static dispatcher.
- Retains the disabled `FuseEditorAssemblyLoader` and bridge contracts for
  source history; the runtime feature gate prevents bootstrap.

`FUSE.Editor.dll`

- Implements `IFuseEditorLifecycle` (via `FuseEditorBootstrap` →
  `FuseEditorLifecycle`).
- `FuseEditor` is the DontDestroyOnLoad host; `FuseEditorScreen` is the
  IMGUI overlay implementing the EDEN-inspired layout.
- Per-entity-kind editors (currently `Track/FuseNodeMarker`,
  `Track/FuseNodeEditorController`) hold real selection/gizmo/persistence
  logic. These are paused-out today (no entry path); they'll be re-summoned
  once the screen has selection plumbing.

Bridge methods used by the former flow:

| Direction         | Method                                | Use                                             |
| ----------------- | ------------------------------------- | ----------------------------------------------- |
| FUSE → FUSE.Editor | `IFuseEditorLifecycle.OnFuseLoaded`   | Bootstrap on plugin load                        |
| FUSE → FUSE.Editor | `IFuseEditorLifecycle.OnFuseUnloaded` | Teardown on plugin unload                       |
| FUSE → FUSE.Editor | `IFuseEditorLifecycle.EnterEditor`    | Main-menu button clicked                        |
| FUSE.Editor → FUSE | `FuseEditorBridge.EditorExited` event | User clicked Exit (or screen torn down)         |

## What's mocked vs implemented today

| Area | Status | Notes |
|------|--------|-------|
| Main-menu button injection                  | **Implemented** | Harmony postfix on `MainMenu.Awake`. |
| Bridge lifecycle (`EnterEditor`, `EditorExited`, `EditorSessionPending`) | **Implemented** | Single-Assembly.LoadFrom + typed dispatch; pending-session flag gates auto-spawn. |
| Scene transfer on Enter                     | **Implemented** | Main-menu click calls `GlobalGameManager.Launch` with `[Editor, BushnellWhittier, EnvironmentEnviro]` + `NewGameSetup(GameMode.Sandbox)`. |
| Suppression of built-in DefinitionEditorModeController | **Implemented** | `GameObject.Find("Definition Editor Mode Controller")?.SetActive(false)` on MapDidLoad (same pattern as AlinasMapMod). |
| Exit → return to main menu                  | **Implemented** | `EditorExited` subscriber on FUSE side calls `GlobalGameManager.ReturnToMainMenu`. |
| EDEN-style three-panel layout               | **Implemented** | IMGUI panels (top bar / left tree / right properties / bottom status) overlay the loaded world; viewport gap renders the 3D scene live. |
| Entity tree populated from active mod       | **Implemented** | Reads `FuseModDefinition.Tracks/World/Operations`. |
| `FuseEditorUiHelper` + `FuseEditorStrings` helper layer | **Implemented** | Translate-label, tooltip, disabled-with-reason, separator-with-text, end-of-frame hover tooltip pass. Axiom's `ImGuiHelper` pattern in Unity-IMGUI shape. |
| `FuseEditorWindowKind` enum + visibility registry | **Implemented** | EntityTree / Properties / ToolStrip toggleable via top-bar Windows ▾ popup; viewport stretches into hidden-panel space. |
| `IFuseEditorTool` registry + Select/Move/Rotate/Scale/Place tools | **Implemented** | Viewport tool strip data-driven from the registry; click selects + Move/Rotate engage immediately; Place creates a node at camera raycast; Scale reports unavailable with reason. |
| Per-mod camera bookmarks                    | **Implemented** | Bookmark tab bar under top bar; click teleports via `CameraSelector.shared.ZoomToPoint`; "+" captures `Camera.main`; persists to `<ActiveMod>/.fuse-editor/views.json`. |
| Selection persistence + per-attribute edits | **Implemented** | Tree click selects + camera-pans; Properties panel shows real `FuseNode` data; Position/Rotation/Group/Tags all live-edit with per-keystroke auto-save via `FuseAuthoringPersistenceService.SaveDefinitionObject`. |
| 3D viewport entity gizmos                   | **Implemented (Move + Rotate)** | `FuseNodeMarker` engages RLD move/rotate gizmos on selection while Move/Rotate tools are active. |
| Selection visual cue                        | **Implemented** | Selected marker tints yellow on `SetSelected(true)`; restores its baseline color on deselect. |
| Delete                                      | **Implemented** | Delete button on Properties panel; `FuseAuthoringPersistenceService.RemoveDefinitionObject` handles all kinds (node/segment/span/area/scenery/spliney/industry/load/station/turntable). Currently only the node path is reachable from the UI. |
| Save indicator                              | **Implemented** | Bottom bar shows "Last saved: 5s ago" via `FuseEditorSaveTracker`; resets per session. |
| Save button                                 | **Mocked (disabled)** | Persistence is per-keystroke today, so the explicit Save button is redundant. Will gain meaning once a "batch save" / "undo" flow lands. |
| Play button                                 | **Mocked (disabled)** | Will load a sandbox session with the active mod applied. |
| Exit-to-Editor return from Play             | **Deferred** | Requires preview state snapshot. |

## Roadmap

### Phase 1 — Mockup over real session (this PR)

- Main-menu button visible and functional.
- Clicking it launches a real editor session via
  `GlobalGameManager.Launch([Editor, BushnellWhittier, EnvironmentEnviro], ...)`
  + a fresh `NewGameSetup(GameMode.Sandbox)`. Same shape AlinasUtils uses
  for its autoload patch.
- On `MapDidLoadEvent`, FUSE.Editor consumes the
  `EditorSessionPending` flag, disables the built-in
  `DefinitionEditorModeController` (so the in-game car-editor IMGUI
  doesn't fight us), and spawns the EDEN-inspired IMGUI overlay over
  the loaded world.
- Entity tree pulls real categories from `FuseModDefinition` when an
  active mod is set; otherwise shows representative mock data.
- Exit Editor → `GlobalGameManager.ReturnToMainMenu()` via the FUSE-side
  `EditorExited` subscriber.
- No real editing yet — Save / Play / placement tools are visible but
  disabled.

### Phase 2 — Editing on top of the loaded session

- Selection from the left tree drives a real selection in the world
  (highlight + camera focus via `CameraSelector.shared.ZoomToPoint`).
- Per-entity-kind helpers (already in `Track/*` — `FuseNodeMarker`,
  `FuseNodeEditorController`) reattach to game-side entities and surface
  gizmos for the selected one.
- Swap the IMGUI overlay for `ProgrammaticWindowCreator`-backed Unity UI
  panels now that we have a real session with the UI infrastructure in
  place. IMGUI was a stopgap for the main-menu phase that no longer
  applies.

### Phase 3 — Attribute editors + persistence

- Right panel renders per-attribute editors driven by FUSE authoring
  metadata (`FuseEditableMember`, `FuseAuthoringRegistry`).
- Save dispatches to `FuseAuthoringPersistenceService`.
- Multi-select + group operations.

### Phase 4 — Play preview

- "Play Mod" snapshots the current editor state, then loads a sandbox
  game session with the active mod applied.
- An in-game "Return to Editor" affordance (likely a pause-menu button
  injected only when entered from the editor) reverses the transition.
- On return, the editor restores the snapshotted state — Arma's
  "scenario will be in the same state" contract.

## Open questions

- **Editor scene**: do we ship a dedicated minimal Unity scene, or reuse a
  designated sandbox map in "editor mode"? Dedicated is cleaner but
  requires asset bundling; reuse is simpler but couples to game-state
  flags.
- **Mod selection UX**: dropdown inside the editor, or a separate pre-editor
  picker screen (like Eden's "select terrain" step)?
- **Multi-mod editing**: edit one mod at a time, or support cross-mod
  references with a "current mod" focus?
- **Undo/redo**: EDEN ships per-action undo. FUSE's authoring layer is
  document-shaped; a JObject-snapshot ring buffer is probably the right
  shape.
