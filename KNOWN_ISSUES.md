# Known Issues

## Deferred Systems

- Signals are intentionally deferred until the rest of the legacy route surface is stable.
- The public in-game editor workflow is not a beta blocker yet.
- Rolling stock and locomotive/car mods are out of beta scope unless they are audio-only horn, whistle, or bell packs.

## Game Limitations

- Railroader does not support normal three-way switches. Legacy content that authors a three-way switch shape should be treated as an authoring issue, not a FUSE graph bug.
- Some legacy packages contain broken passenger stop or depot definitions even under the original legacy stack. FUSE should report those clearly and avoid making them worse.

## Experimental Features

- Early scene-path suppression is experimental and disabled by default.
- Runtime authoring mutations are experimental.
- `/fuse.reapply` and `/fuse.restore` are experimental recovery/testing commands.

## External Mod Conflicts

- Do not load a legacy route and its converted FUSE route at the same time unless testing conflicts.
- Legacy loaders, AMM, the legacy custom content framework, and the legacy mod loader may create duplicate objects when used with converted packages for the same route.
- FUSE can load custom industry components only when the owning component assembly is installed and loaded.

## Multiplayer

- FUSE beta uses legacy-style multiplayer compatibility mode: every player must have the same FUSE build, enabled package list, and load order installed locally.
- FUSE does not negotiate host/client package mismatches. A mismatched client can desync visually or operationally even though FUSE will warn on first non-host runtime apply.
- Strict non-host client blocking is available through `Settings.BlockNonHostMultiplayerClientWorldApply`, but it is disabled by default so private multiplayer tests can behave like the legacy mod loader.

## Current Verification Notes

- The current supported beta test stack reaches `73 loaded | faults 0 | conflicts 0 | assets 0 | graph 0 | transfers 0 | suppressions 0` in `FUSE.log`.
- Asset packs should be installed as real asset packs; FUSE should not alias missing assets to unrelated names when the correct asset pack exists.
- Location ordering, progression visibility, and map mask visuals still need repeated human visual checks across the full route stack before beta sign-off.
