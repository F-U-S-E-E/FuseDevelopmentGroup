# Known Issues

Current limitations, unsupported content, and known interactions. For debugging a
specific symptom, see [TROUBLESHOOTING.md](TROUBLESHOOTING.md).

## Out Of Scope

Not defects — content FUSE does not aim to support in this release.

- **The full public in-game editor workflow.** The standalone external editor
  ships instead — see [EXTERNAL_EDITOR.md](EXTERNAL_EDITOR.md).
- **Rolling stock, locomotive, and car mods**, except audio-only horn, whistle, and
  bell packs, which convert.
- **Arbitrary legacy script mods.** Only data, asset, audio, and supported runtime
  component packages convert.
- **Signals.**
- **Mid-session scene-path suppression re-enable.** Once a map is loaded, the
  setting cannot be turned back on for that session.

## Game Limitations

Constraints from Railroader itself, not from FUSE.

- **Three-way switches are not supported.** Railroader does not treat them as
  normal graph switches. Legacy content authoring a three-way switch shape has an
  authoring problem that predates FUSE, and should be reported as such rather than
  as a FUSE graph bug.
- **Some legacy packages ship broken passenger stop or depot definitions** that
  were already broken under the original legacy stack. FUSE reports these clearly
  and avoids making them worse, but it cannot invent the missing data.

## Experimental Features

Off by default. Enabling one is a testing decision — back up your save first. See
[SETTINGS.md](SETTINGS.md#experimental).

- **Early scene-path suppression** (`EnableExperimentalEarlyScenePathSuppression`).
- **Targeted terrain invalidation** (`EnableTargetedTerrainInvalidation`) — a
  significant load-time win, but timing-sensitive because masks load
  asynchronously. Falls back to a full rebuild when no footprint was captured.
- **Runtime authoring mutations.**
- **`/fuse.reapply` and `/fuse.restore`** — recovery and testing commands that
  refuse to run mid-session without `--force`.

## External Mod Conflicts

- **Do not load a legacy route and its converted FUSE route at the same time**
  unless you are deliberately testing conflicts. Both claim the same object ids.
  `/fuse.conflicts` reports the collision.
- **Legacy loaders, AMM, Strange Customs, and RailLoader can create duplicate
  objects** when used alongside converted packages for the same route. FUSE warns
  when it finds a leftover `Railloader.dll`, `Railloader.Injector.dll`, or
  `Railloader.Interchange.dll`.
- **Custom industry components load only when the owning assembly is installed.**
  A package referencing a component type from an assembly you do not have reports
  the missing dependency rather than silently dropping the component.

See [MIGRATION_FROM_LEGACY.md](MIGRATION_FROM_LEGACY.md) for the migration order
that avoids most of this.

## Multiplayer

FUSE uses legacy-style multiplayer compatibility mode.

- **Every player needs the same FUSE build, enabled package list, and load
  order**, installed locally. FUSE does not sync package contents over the
  network.
- **Mismatches are not negotiated.** A mismatched client can desync visually or
  operationally. Non-host clients log a warning on their first runtime world
  apply.
- **Strict client blocking is available** through
  `BlockNonHostMultiplayerClientWorldApply`, disabled by default so private tests
  behave like RailLoader.

## Areas Needing Visual Verification

These apply correctly by every automated check available, but their *appearance*
depends on interactions that only a human looking at the map can confirm. If
something looks wrong here, it is worth reporting even when the load report is
clean:

- Location and area ordering in the company window, which depends on both
  converted area order data and base-game runtime ordering
- Progression visibility timing
- Map mask visuals — terrain flattening, tree cutting, and height masks

## Reporting

Include `FUSE.log`, `Player.log`, `/fuse.report json`, and the conversion report
for the affected package. Full checklist in
[TROUBLESHOOTING.md](TROUBLESHOOTING.md).
