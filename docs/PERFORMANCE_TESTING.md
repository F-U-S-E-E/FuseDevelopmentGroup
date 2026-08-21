# Performance Acceptance Testing

Use this procedure for FUSE, Tile Editor, and Toolshed performance reports. It
separates load-screen work, ordinary gameplay, streaming, editor work, and
third-party exceptions so one visible pause is not assigned to the wrong mod.

## Before The Run

1. Restart Railroader after installing the candidate DLLs. Do not hot-replace a
   DLL in a running process.
2. Record CPU, GPU, RAM, storage type, display resolution, and the exact enabled
   profile. Include a lower-end machine when release acceptance is the goal.
3. Enable **FUSE Settings > Frame Spike Diagnostics**. Keep ordinary debug world
   labels and overlays off unless that overlay is the feature under test.
4. Use the same save, camera start, and profile for the before and after runs.

## Required Scenarios

### Load And Track Apply

- Start at the main menu, load the large-mod-set save, and do not move the
  camera until player control is released.
- Record package discovery, map apply, track rebuild, scenery activation, and
  total load times from `FUSE.log`.
- Fail the run if player control begins while camera-near FUSE scenery still
  appears in a visible activation wave.

### Equipment Roster And Buy Menu

- Find the `equipment catalog warm-up completed` line. It reports total work,
  slow-store count, worst store, and Lego containers skipped by the fast path.
- Open the company equipment roster and the buy menu once before completion and
  once after completion. Record click time and whether locomotives, railcars,
  tender swaps, Lego clones, and customization controls are present.
- A menu that does not open, loses content, or stalls for seconds after warm-up
  fails even when the frame-rate average looks healthy.

### Recurring Gameplay Stutter

- Stand still for two minutes, then run or drive through a scenery-heavy area
  for two minutes. Include at least one teleport/camera turn if that reproduced
  the original report.
- Any repeating 2–4 second pause must align to a timestamped frame-spike line.
  Record GC deltas, queue depths, and `fusePumpWorstPhase`.
- `none measured` is a valid result: it means the expensive phase was outside
  FUSE's runtime pump and the surrounding Player/FUSE log lines must be used.

### Tile Editor

- Enable whole-map grade labels and compare idle, pan/zoom, node dragging,
  terrain painting, vegetation painting, save, and reload.
- Leave the desktop editor idle beside Railroader, then minimize it. Confirm it
  returns to 60 FPS during input while using the documented 15/5 FPS idle caps.
- Save and reload terrain and vegetation; unchanged data must not trigger a
  redraw, heartbeat-write backlog, or reverted mask.

### Toolshed

- Exercise one service facility, selective interchange, link-and-pin car, and
  storage/particle animation.
- Leave the game idle for at least one minute after every definition binds. A
  two-second scene-scan rhythm must not remain.
- Change maps/scenes and confirm facilities rebind without waiting for the
  maximum retry delay.

## Evidence Bundle

Attach:

- `FUSE.log` and `Player.log`
- the FUSE health, audit, and debug-bundle JSON exports
- `/fuse.report json`, `/fuse.loaded`, and `/fuse.conflicts`
- the exact mod profile/manifest and hardware details
- click-to-open timings and a short video for visible pop-in or repeating stalls

Do not close a performance checklist item from an automated suite alone. Unit
tests protect correctness; this run supplies the frame-time, main-thread, queue,
memory/GC, menu, and visual evidence required for release acceptance.
