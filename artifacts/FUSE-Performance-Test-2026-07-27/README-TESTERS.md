# FUSE Performance Test Build - 2026-07-27

This package contains the three modified runtime mods:

- `Mods/FUSE`
- `Mods/Toolshed-v0.3.0`
- `Mods/LegosLibraryPerformancePatch`

FUSE Live Bridge and FUSE Test Bridge are development tools and are intentionally
not included. The standalone SceneryLoadRaceFix is also not included because its
load-generation fix is integrated into FUSE.

See `CHANGELOG-2026-07-27.md` for the complete change list and measured results.

## Installation

1. Close Railroader.
2. Back up the existing `Railroader/Mods/FUSE` and
   `Railroader/Mods/Toolshed-v0.3.0` folders, plus any existing
   `Railroader/Mods/LegosLibraryPerformancePatch` folder.
3. Remove or move those old folders out of `Railroader/Mods`. Also remove the
   obsolete standalone `SceneryLoadRaceFix` while testing this FUSE build. Do not
   leave duplicate `Toolshed`, `Toolshed-v*`, `FUSE`, or Lego performance-patch
   folders active.
4. Copy the contents of this package's `Mods` folder into
   `Railroader/Mods`.
5. Start Railroader normally.

The package does not include `Settings.xml` and will not overwrite an existing
Toolshed configuration. It also does not include the development bridge mods.
The Lego patch remains a separate folder so it can be removed independently if
Lego's Logos and Decorations fixes the same paths upstream. Version 0.4 limits
decoration request waves, cleans up their retained resources, preserves building
decals that have no parent car, and prevents missing optional decoration assets
from invoking Unity's synchronous crash reporter.

## What to test

Please record:

- GPU model and VRAM capacity.
- System RAM capacity.
- Time from the visible loading screen appearing until gameplay appears.
- Dedicated and shared GPU memory at the menu, immediately after loading, in
  Bryson, and after 10 minutes of driving.
- Whether the Windows busy cursor or a frozen loading screen appears.
- Whether buildings or equipment are missing.
- Whether the Equipment Purchase menu opens and cars can be purchased.
- Any stutter after loading, teleporting, or driving through Bryson.
- After teleporting, whether nearby buildings are visible immediately without
  rotating or moving the camera.
- Whether stutter repeats approximately every two seconds.

For an 8 GB GPU, leave automatic detection enabled. FUSE automatically applies
its constrained texture policy to an actual 8 GB card. The manual
`Force 8 GB VRAM Mode` switch is only needed to reproduce that behavior on a
larger GPU.

## Expected reference result

On the development system with a 16 GB GPU:

- Best validated visible loading screen: 12.7 seconds.
- FUSE map-load pipeline: approximately 7.02 seconds.
- EFA progression apply: approximately 255 ms.
- Narrow Gauge special-work analysis: approximately 1.0 second.
- Merged graph rebuild: approximately 1.7 seconds.
- All 13 Narrow Gauge special-work plans valid.
- Final track state: 960 segments, 154 switches, 93 bumpers.
- All 204 saved cars present.
- Equipment Purchase menu functional.
- Runtime-injected equipment definitions present.
- Toolshed performs its empty configuration scan once per session instead of
  rescanning the Mods tree every two seconds.
- Buildings use normal local streaming rather than global residency. The
  reference save keeps about 357 of 2,583 FUSE scenery instances in its local
  working set so the test remains valid for 8 GB GPUs.
- Teleport/world-shift culling is reconciled automatically. Nearby structures
  use a separate bounded destination lane; vegetation retains the normal
  four-task background ceiling. The live destination test settled at 71/71
  requested/loaded FUSE models with an empty queue and no camera movement.
- The final diagnostics-on pass had a 340 ms worst post-screen frame; the prior
  1.16-1.22 second exception/crash-upload stalls were absent.

## Logs to return

After reproducing an issue, send:

- `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE.log`
- `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\Player.log`
- A screenshot of Task Manager's GPU Memory and system Memory pages.

Do not install `FUSE.LiveBridge` or `FUSE.TestBridge` for this test unless a
developer specifically requests one of them.
