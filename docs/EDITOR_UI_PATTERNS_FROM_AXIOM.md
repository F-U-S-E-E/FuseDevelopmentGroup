# UI patterns learned from Axiom (Moulberry, Fabric 1.21.x)

Source: `D:\Railroader Modding\The Forbidden Zone\Axiom-5.4.2-for-MC1.21.11.jar`,
decompiled with Vineflower 1.12.0 into
`D:\Railroader Modding\The Forbidden Zone\axiom-decompiled\`.

License note: Axiom is LGPL-3.0. We're using it as **inspiration only** — no
source is being copied into FUSE. The patterns below are documented in our
own terms.

## TL;DR

Axiom is a Dear ImGui application bolted into Minecraft via a custom
GLFW + OpenGL backend. Every visible panel is an ImGui window, the
layout is docking-based, and a small number of strong patterns make a
sprawling editor (40+ windows, 30+ tools) feel coherent.

The patterns that translate cleanly to FUSE.Editor, ranked by ROI:

| Pattern | What it gives us | FUSE.Editor effort |
|---|---|---|
| **`EditorWindowType` enum-registry** | Single source of truth for every panel | Low — fits Unity IMGUI |
| **`ImGuiHelper` domain wrapper** | Stop sprinkling raw IMGUI calls | Low — write incrementally |
| **i18n key + `.description` companion** | Tooltip-driven UX with localization-ready labels | Low — start with English |
| **View bookmarks** (saved camera positions) | "Jump to siding 4" affordance during editing | Medium — needs persistence |
| **Permission-gated disabled items + tooltip** | Clear UX for "feature exists, you can't use it right now" | Low |
| **Keybind registry as first-class data** | Reassignable hotkeys, no scattered `Input.GetKey` | Medium |
| **Style/Theme manager** | Consistent Railroader-flavored look across panels | Medium |
| **Tools as a registry with `iconChar` + permissions** | Add a tool by adding an entry, not editing 5 files | Low |
| **Dear ImGui itself (with docking)** | Full editor-class UI primitives (docks, splits, drag-drop) | **HIGH** — separate workstream |

## Layout: docking with a baked-in default

`EditorUI.init()` writes a default `imgui.ini` on first launch describing the
dock layout. The user can rearrange afterwards, but the first-run experience
is curated:

```
DockSpace
├─ Left column (300 px wide)
│  ├─ Tools (top, 250 px)
│  └─ Tool Options (bottom, 750 px)
├─ Center (CentralNode — the 3D viewport gap)
└─ Right column (300 px wide)
   ├─ Clipboard / TargetInfo (tabs, 200 px)
   ├─ Palette (200 px)
   ├─ Active Block (100 px)
   ├─ History (300 px)
   └─ World Properties (200 px)
```

`MainMenuBar` runs separately at the top of the screen.

Each window's begin/end binds it to a DockId, so it lands in the right
slot on first show. Re-docking by the user mutates `imgui.ini`, which
persists across launches.

**For FUSE**: Unity IMGUI doesn't have docking. The closest equivalent
is the three-panel layout we already mocked in `FuseEditorScreen`. If
we ever move to real Dear ImGui (via `imgui-cs` or similar), inheriting
the docking-default-ini pattern is straight cargo-culting.

## The window registry pattern

`com.moulberry.axiom.editor.EditorWindowType` is an enum where every
editor panel is one entry:

```java
TOOLS("tools", important=true, openByDefault=true, extraFlags=8),
TOOL_OPTIONS("tool_options", true, true, 8),
PALETTE("palette", true, true),
INVENTORY("inventory", true, true, 4104),
HISTORY("history", true, true),
WORLD_PROPERTIES("world_properties", true, true),
…
FILTER_SELECTION("filter_selection", false, false, 64),
EXPAND_SELECTION("expand_selection", false, false, 64),
…
```

Each value carries:

- `nameKey` — i18n key (`axiom.editorui.window.<nameKey>`)
- `important` — drives menu grouping (sticky vs ephemeral panels)
- `openByDefault` — initial visibility
- `extraFlags` — ImGui window flags
- `ImBoolean open` — the live visibility bool (passed to ImGui for the
  close button)
- Per-instance cross-frame state: `justOpened`, `docked`, `focused`,
  `disabled`, `disabledDimBg`

Each window's `render()` does:

```java
public static void render() {
    if (!EditorWindowType.TOOLS.isOpen()) return;
    if (EditorWindowType.TOOLS.begin("###Tools", useFlags: true)) {
        // … draw window content …
    }
    EditorWindowType.TOOLS.end();
}
```

`begin()` handles:

- Centering the window the first time it opens (when not docked)
- Tutorial-disabled state with a dimmed overlay
- Whether to show the X close button (config-dependent)
- The `ImBoolean` close handle

The Settings menu can show "Open/Close <window>" as a checkbox list by
iterating the enum and toggling each `open`. New window = new enum
entry + new `XxxWindow.render()` class. Two files to touch.

**FUSE.Editor adoption**: a direct port. Create
`FuseEditorWindowKind` enum with entries for every panel we eventually
ship (Tracks / Properties / History / Tools / Palette / Mod Info /
Save / Play). Each has nameKey + visibility bool + a tiny `begin/end`
helper. The Settings menu auto-builds "Show window X" toggles.

## Helper layer: domain-specific IMGUI wrappers

`ImGuiHelper` is the most copyable file in the project. It collects all
the "do the right thing" wrappers around raw ImGui calls:

- `translateLabel(key)` — returns `{ key, title=i18n(key), description=i18n(key+".description") }`.
  Everywhere a label is needed, you pass this struct, not a raw string.
- `helpMarker(text)` — the `(?)` icon next to a control that shows a
  tooltip on hover. Cheap to add, vastly improves discoverability.
- `tooltip(text)` / `tooltip(text, hoverFlags)` — wraps the
  `isItemHovered → beginTooltip → text → endTooltip` dance.
- `disabledMenuItem(label, reasonText)` — grayed item with a tooltip
  explaining why it's grayed. Critical for permission-gated UIs.
- `separatorWithText(text)` — labeled section break.
- `inputInt(label, value)` / `inputFloat(...)` — wraps the array-based
  ImGui input idioms so callers pass simple values.
- `combo(label, currentItem, values, flags)` — dropdown with sane defaults.
- `radio(label, currentItem, values)` — radio group as a single call.
- `buttons(label1, label2, …)` — equal-width button row, returns the
  index clicked.
- `elementList(name, elements, width, minLines, maxLines, framed, onClick)` —
  scrollable list with add/remove affordances.
- `blockStateButton` / `blockStateDragDropSource` / `blockStateDragDropTarget` —
  domain-specific widgets for block manipulation. The FUSE analog is
  per-entity-kind widgets: `nodePickerButton`, `segmentDragDropTarget`,
  etc.
- `pushStyleColor(...)` / `popAllStyleColors()` / `pushStyleVar(...)` /
  `popAllStyleVars()` — counted style stack helpers so cleanup is
  bulletproof on early returns.
- `calcLabelPosition(Label... labels)` — pre-computes the widest label
  in a set so columns align across rows.

**FUSE.Editor adoption**: start a `FuseEditor.Screen.UI` static class
with the same shape. Even a tiny subset
(`translateLabel + helpMarker + tooltip + disabledMenuItem + buttons +
separatorWithText`) replaces ~70% of the ad-hoc IMGUI in
`FuseEditorScreen`.

## i18n: key + `.description` companion

Every user-facing string has a key. Tooltips use the same key suffixed
with `.description`. Example from `en_us.json`:

```
"axiom.editorui.mainmenu.file"                          → "File"
"axiom.editorui.mainmenu.file.import_schematic"         → "Import schematic…"
"axiom.editorui.mainmenu.help.debug_info"               → "Debug info"
"axiom.editorui.mainmenu.help.debug_info.description"   → "Show technical
                                                           details useful for
                                                           reporting bugs"
```

`translateLabel("axiom.editorui.mainmenu.help.debug_info")` returns a
struct with both strings. Code that renders the menu item doesn't have
to know whether a description exists — `helpMarker` checks and
short-circuits cleanly.

Axiom's `en_us.json` is 77 KB / **1,218 keys**. Heavily localized — they
ship Cyrillic / CJK / RTL fallback fonts for the non-Latin glyph ranges
(`addRanges(rangesBuilder, fonts.getGlyphRangesChineseFull())`, etc.).

**FUSE.Editor adoption**: skip the runtime translation layer for now,
but adopt the `key + .description` shape from day one. Even hardcoded
English strings can live in a `FuseEditorStrings` static map so we can
swap in a real loader later without rewriting call sites.

## View bookmarks (camera positions)

`ViewManager` and `View` aren't UI panels — they're **named saved
camera positions** persisted per-server (per-mod for us). The active
view is a tab at the top of the main editor window. Clicking a tab
teleports the player to that position; clicking `+` saves the current
position as a new view.

Two pinning flags per view:

- `pinLocation` — the position is locked (won't update when the camera
  moves)
- `pinLevel` — the dimension (overworld / nether) is locked

If neither is pinned, the view tracks the live camera position, so
"Main" updates as you fly around — the next time you switch back to
some other view and come back, you're where you left off.

**FUSE.Editor adoption**: textbook fit. We can serialize per-mod views
alongside the mod definition: `Mods/<mod>/.fuse-editor/views.json`.
"Roundhouse", "Yard ladder", "Junction at MP 12" — let the modder
bookmark spots they keep revisiting. Persist mode + position + dimension
(if Railroader has dimensions); pinning bits the same way.

## Permission-gated disabled items

Throughout the menu code:

```java
if (!AxiomClient.hasPermission(AxiomPermission.CAN_IMPORT_BLOCKS)) {
    ImGuiHelper.disabledMenuItem(label, "Server has disallowed importing");
} else if (ImGui.menuItem(label)) {
    // … actually import …
}
```

The disabled menu item shows the label grayed out and, on hover,
displays the reason string. The user never wonders "why can't I click
this?".

The same pattern wraps tool buttons (`Server hasn't given you
permission to use this tool`) and big actions like Save / Play
(future).

**FUSE.Editor adoption**: drop-in. Replace `if (cond) menuItem(label)`
with `disabledMenuItem(label, reason)` for the negative case. The
disabled Play/Save buttons in `FuseEditorScreen` should already be
adopting this shape — they currently render disabled with no tooltip,
which is a regression.

## Keybinds as data

`com.moulberry.axiom.editor.keybinds.Keybinds` is a registry of named
key actions: `COPY`, `PASTE`, `UNDO`, `REDO`, tool-switching keys,
view-switching keys, … . Each `Keybind` knows:

- Its default key
- Its current user-rebound key
- The category it belongs to (`KeybindCategory`)
- How to render a tooltip suffix like " (Ctrl-Z)"

`KeybindsWindow` renders an editable list grouped by category. While
the user is binding a key, the helper steals input and writes the
chosen scancode back to the keybind.

`ToolManager.keybindMap` links each tool to a keybind, so the
`ToolsWindow` button can append " (T)" to the tooltip automatically.

**FUSE.Editor adoption**: when the editor grows beyond a handful of
actions, lift hotkeys into a `FuseEditorKeybindRegistry` rather than
binding `<Keyboard>/n` (etc.) in scattered places. The `n` hotkey we
already removed wasn't worth saving, but Move/Rotate/Scale/Toggle
gizmo modes will be.

## Style/theme manager

`StyleManager` + `BuiltinStyles` keep the visual style versionable:

- `BuiltinStyles.IMGUI_DARK` / `IMGUI_LIGHT` — two base styles
- `StyleManager.getBaseStyle()` — currently selected base
- `StyleHelper.calcModifiedStyleValues(base, current)` — diffs the
  user's tweaks against the base, so saved themes are
  *deltas-from-base* not full snapshots
- `StyleHelper.Theme.convertFromBase64(...)` — themes serialize as
  base64 blobs (shareable in chat / forum posts)

The `StyleEditorWindow` exposes every ImGui style variable as a slider,
shows the live result, and a Save button persists the theme to user
config.

**FUSE.Editor adoption**: probably not in v1. But once the editor is
real, factoring out a `FuseEditorTheme` struct + a single point of
style application makes future re-skinning trivial. The cost of doing
this from the start is near zero.

## Custom GLFW/GL3 backend

`CustomImGuiImplGlfw` (1,800 LoC decompiled) and `CustomImGuiImplGl3`
(450 LoC) are Axiom's forks of imgui-java's stock backends. They had
to fork because:

- Minecraft's GL state machine clobbers ImGui state if you let it; the
  custom backend stashes and restores between frames.
- Multi-context ImGui — Minecraft sometimes has its own ImGui context
  (other mods); Axiom swaps to its own context on every frame and
  restores afterwards.
- DPI scaling: the editor needs its own `contentScale` separate from
  Minecraft's GUI scale.
- Multi-viewport (popping out windows into native OS windows) — the
  backend implements all the
  `Create/Destroy/RenderWindow/SwapBuffers/SetWindowPos/...` callbacks
  ImGui multi-viewport needs.

**For FUSE**: if we ever move to Dear ImGui, the Unity side has
`imgui-cs` (managed) backed by `cimgui`. Unity has its own GL pipeline
quirks — a custom backend will likely be necessary too. Plan for it as
a multi-day workstream, not an afternoon.

## Tools as a registry

`ToolManager.getTools()` is a `List<Tool>`. Each `Tool` is a subclass
that knows:

- `iconChar()` — a glyph from `axiomicons.ttf` that renders as the
  toolbar icon
- `requiredPermissions()` — server-side permission gates
- `name()` — i18n-translated label
- `renderAdjustment(...)` — the bottom-of-viewport adjustment HUD
  (radius, falloff, etc.) when the user holds the modifier key
- … plus the actual tool logic (mouse handling, world mutation)

`ToolsWindow.render(icons)` iterates the list and produces the button
grid automatically — no per-tool wiring in the window code. Adding a
new tool: subclass `Tool`, register it once, done.

Tool list from the decompile gives a sense of scope:
`biome_painter, blend, box_select, distort, elevation, extrude,
floodfill, fluidball, freehand, generator, gradient_painter,
lasso_select, magic_select, modelling, modify, noise_painter, painter,
path, rock, roughen, ruler, script_brush, sculpt_draw, shape, shatter,
slope, smooth, stamp, text, weld`. **30 tools**, each its own folder
under `tools/`.

**FUSE.Editor adoption**: we already have one tool in flight
(`FuseNodeMarker` / `FuseNodeEditorController` — select + gizmo for
nodes). Lift the seams: `IFuseEditorTool` interface with `IconChar`,
`Name`, `RequiredCapability`, `Render(viewport)`, `OnMouseDown`,
`OnDrag`, etc. Register each in a `FuseEditorToolRegistry`. The Tools
panel becomes a few lines of iteration.

## Drag-drop payloads

`DragDropPayloads` declares typed payload classes:

```java
DragDropPayloads.PaletteBlock          // dragging a block from the palette
DragDropPayloads.NoisePainterBlock     // dragging a block onto a noise weight
```

ImGui's drag-drop API uses string type IDs as a registry; this class
gives each one a typed Java class so payload consumers
(`blockStateDragDropTarget`) can do `getPayload(PaletteBlock.class)`
without type casts.

**FUSE.Editor adoption**: when we wire drag-drop (e.g. drag a node
from the tree into a segment definition), follow this shape — small
typed payload classes, not string keys.

## Layout idea worth stealing immediately: a Views tab bar

Right under the main menu bar, Axiom renders a one-line ImGui tab bar:

```
Main  |  Roundhouse  |  Yard ladder  |  +
```

Active view's tab is selected. Clicking a tab teleports. Clicking `+`
saves current. Right-click context menu on a tab → rename, delete,
pin location, pin dimension.

In our world: same UX, persisted to `Mods/<mod>/.fuse-editor/views.json`.
First feature to add once the editor is past mockup.

## Patterns NOT worth porting (yet)

- **The schematic / clipboard subsystem** — Axiom's lifeblood, but
  FUSE's "clipboard" is mod-definition JSON, an entirely different
  problem.
- **Async file dialogs (`AsyncFileDialogs`)** — Axiom uses
  `tinyfd_*` natives bundled in the jar. Unity Editor has its own
  EditorUtility.OpenFilePanel, but in-game we don't have a system
  file dialog. We'd need a custom file-browser panel — punt until
  there's a concrete need (import schematic from disk, etc.).
- **Multi-viewport (windows pop out of the game)** — Beautiful but
  every layer of complexity it added would be wasted on a v1.
- **`ExpressionEvaluator`** — Axiom lets you type math expressions in
  any number field (`32 * 4 + sin(pi)`). Slick. Defer.

## Concrete next steps for FUSE.Editor

Ordered by ROI per hour-of-effort:

1. **`FuseEditorWindowKind` enum** with one entry per current panel
   (currently: just the screen, but soon: Tracks list, Properties,
   Tools, Mod Info, History). Each entry has `OpenByDefault`,
   `OpenState`, `Render` method group. Replaces the ad-hoc switch in
   `FuseEditorScreen.Populate`.
2. **`FuseEditorUiHelper` static class** with `Label(key)`, `Tooltip`,
   `DisabledItem`, `HelpMarker`, `SeparatorWithText`, `Buttons`,
   `InputInt`, `InputFloat`. Even Unity IMGUI versions of these are
   immediately useful.
3. **Disabled affordances on Save / Play buttons** — they currently
   render disabled with no tooltip. Add a tooltip explaining the
   pending phase.
4. **Tools registry** — `IFuseEditorTool` + a small registry. Slot
   `FuseNodeMarker`'s select/move/rotate as the first three entries.
5. **View bookmarks** — pre-persistence: just an in-memory list of
   `{ name, position, rotation }` and a tab bar. Persistence to
   `Mods/<mod>/.fuse-editor/views.json` once the editor session model
   stabilizes.
6. **Keybind registry** — defer until tool count >= 5; not worth the
   infra for 2.
7. **Theme system** — defer until visual polish becomes the bottleneck.

## File map (decompiled, for follow-up reading)

All under
`D:\Railroader Modding\The Forbidden Zone\axiom-decompiled\editor\`:

- `EditorUI.java` — orchestrator (read 200–400 to see frame structure)
- `EditorWindowType.java` — the window registry enum
- `ImGuiHelper.java` — the helper layer (most copyable shape)
- `views/View.java`, `views/ViewManager.java` — view bookmarks
- `styles/StyleManager.java`, `styles/BuiltinStyles.java` — theming
- `windows/MainMenuBar.java` — menu construction patterns
- `windows/ToolsWindow.java` — clean small-window example
- `windows/PaletteWindow.java` — block-palette UX (drag-drop, search,
  recents)
- `keybinds/Keybind.java`, `keybinds/Keybinds.java` — keybind registry
- `widgets/SearchableCombo.java` — filterable dropdown
- `widgets/PresetWidget.java` — save/load named presets

Tooling: `D:\Railroader Modding\The Forbidden Zone\_tools\vineflower-1.12.0.jar`
re-decompiles the jar with `java -jar vineflower.jar <input.class-or-dir> <output-dir>`.
