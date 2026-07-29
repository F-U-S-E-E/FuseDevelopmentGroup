# FUSE Profiler

An in-game performance profiler for Railroader. Opens with **F11** (or the
`/profiler` console command) and shows, live, where frame time is going:
per-system tabs for the game's hot paths (culling, scenery streaming, track
rebuilds, ops/AI, UI and event dispatch, train physics), a search box to
profile any method or any mod's whole assembly on the fly, and a per-mod
rollup that attributes the cost of every Harmony patch in the session to the
mod that installed it.

Measurement is Harmony-based and only active while the profiler window is
open; patches are removed and buffers freed shortly after it closes, so an
installed-but-closed profiler costs nothing.

## Design credit

The architecture — category entries over Harmony-instrumented method sets,
per-frame/per-tick ring-buffer sampling with off-thread stat rollups, and
Harmony-patch cost attribution — follows the excellent design of
[Dubs Performance Analyzer](https://github.com/Dubwise56/Dubs-Performance-Analyzer)
for RimWorld, by Dubwise and simplyWiri. This is an independent
reimplementation for Railroader: no code is shared with that project.

## Usage notes

- The profiler measures **inclusive wall-clock time** per method, bucketed per
  frame (or per physics step for the Physics tab). Percentages are relative to
  the measured whole-frame time.
- Profiling adds overhead to the methods being measured. Compare numbers
  against each other, not against an unprofiled session.
- The **Mods** tab shows other mods' Harmony patch cost attributed by patch
  owner. A mod with expensive patches shows up immediately; a mod that is slow
  in its own MonoBehaviours instead shows up via **assembly profiling** from
  the search panel.

## Known limitations

- Probes measure **main-thread** calls only; instrumented methods invoked on
  worker threads (async continuations, thread pools) are ignored rather than
  corrupting the buffers.
- Tiny Harmony patch methods the Mono JIT has inlined into their targets
  cannot be intercepted — the Mods tab undercounts those. Whole-assembly
  profiling of the suspect mod covers the gap.
- Transpiler-inserted code is not isolated (the transpiled method's total is
  attributed to the game method, not the transpiling mod).
- Async method bodies count only their synchronous slice; coroutine targets
  are measured at their real bodies via the iterator's MoveNext.
- The window blocks clicks over UI beneath it via an invisible raycast
  blocker, but game systems that poll input directly may still react to
  clicks made over the profiler.
