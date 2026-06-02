# FUSE Editor Icons

Drop 16×16 (or larger; renderer scales) PNG icons into this folder
to replace the editor's Unicode-glyph fallbacks.

## Naming

The file name must match a `FuseEditorIconKind` enum member exactly,
case-sensitive:

```
FUSE.Editor/Screen/UI/FuseEditorIcons.cs:FuseEditorIconKind { ... }
```

Examples:
- `Save.png` → toolbar Save button
- `Track.png` → F1 entity-kind selector
- `Play.png` → bottom-bar PLAY MOD CTA

If a PNG isn't present, the editor paints the matching Unicode glyph
(`↓`, `☲`, `▶`, etc.) so the layout is never empty.

## Where the icons go at runtime

`FuseEditorIcons.ResolveIconsFolder()` reads files from the directory
next to `FUSE.Editor.dll`. The build's deploy target copies this
folder into `<GameDir>/Mods/FUSE/Icons/` — an artist can replace any
file in place and the change takes effect on the next editor session.

## Sources for placeholder art

[Lucide icons](https://lucide.dev) (ISC license, MIT-compatible).
Suggested matches (download the SVG, convert to 16×16 PNG):

| Icon kind | Lucide name      |
| --------- | ---------------- |
| New       | `file-plus`      |
| Open      | `folder-open`    |
| Save      | `save`           |
| Undo      | `undo-2`         |
| Redo      | `redo-2`         |
| Select    | `mouse-pointer-2`|
| Move      | `move-3d`        |
| Rotate    | `rotate-3d`      |
| Scale     | `scale-3d`       |
| Place     | `plus-square`    |
| Grid      | `grid-2x2`       |
| Camera    | `video`          |
| Track     | `route`          |
| Switch    | `merge`          |
| Scenery   | `tree-pine`      |
| Mandela   | `box`            |
| Play      | `play`           |
| Trash     | `trash-2`        |
| Plus      | `plus`           |
| Close     | `x`              |
| ChevronDown / ChevronRight | `chevron-down`, `chevron-right` |

Recommended export settings: 16×16, transparent background,
white foreground at full opacity. The editor's icon renderer tints
the glyph by palette role (active = orange, disabled = gray); a
white source PNG composites cleanly under those tints.

## Adding a new icon kind

1. Add the member to `FuseEditorIconKind` (preserve enum order — F-key
   bindings are positional).
2. Add a glyph fallback to the `Glyphs` map in `FuseEditorIcons.cs`.
3. Drop a `<NewKind>.png` into this folder.

The icon is now reachable via `FuseEditorIcons.Get(FuseEditorIconKind.NewKind)`
and `FuseEditorIcons.Draw(rect, FuseEditorIconKind.NewKind)` from any
UI code.
