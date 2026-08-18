# Settings Reference

FUSE has 29 settings. Most players never need to change any of them — the
defaults are the supported configuration, and the majority of these switches are
diagnostic tools rather than features.

## Where Settings Live

Settings resolve from three layers, each overriding the one before it:

1. **Built-in defaults** — compiled into FUSE. Used when nothing else specifies a
   value.
2. **`Info.json`** — the `Settings` object in `Railroader/Mods/FUSE/Info.json`.
   The shipped file lists 14 of the 29; any setting absent from the file falls back
   to its built-in default, and you can add a missing one by name to set it.
3. **User settings** — `settings.json`, written by the in-game FUSE menu.

The user settings file wins over `Info.json`. It lives at:

```
%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE\settings.json
```

Changing a setting through the FUSE menu writes it to that file, so an edit made
in-game survives a mod update that replaces `Info.json`. Conversely, editing
`Info.json` has no visible effect if the same key was already set in-game — clear
it from `settings.json` first.

If `Info.json` is missing or fails to parse, FUSE logs the problem and falls back
to defaults for every setting rather than loading a partial configuration.

FUSE writes the fully resolved setting values to `FUSE.log` at startup, so the log
is the authoritative record of what was actually in effect for a given session.

## Player Settings

The handful of settings that change how the game plays or looks.

### `EnableEnhancedLoadingScreen`

**Default: `true`** — the only setting that ships enabled.

Replaces the stock "Loading…" screen with staged progress and a current-step
label that stays up until FUSE's post-load pipeline finishes. Turning it off falls
the whole feature back to the untouched stock screen.

### `DecoupleVisualConditionLimits`

**Default: `false`**

Controls the per-car Visual Condition slider. When off, the slider can only make a
car look *more* worn than its mechanical condition. When on, the visual override
applies verbatim, so a mechanically worn car can be made to look fresh.

### `RandomizeVisualConditionOnSpawn`

**Default: `false`**

Randomizes each car's visual condition when it spawns, within the range below.

### `RandomVisualConditionMin` / `RandomVisualConditionMax`

**Defaults: `0.6` and `1.0`**, clamped to the range 0–1.

The spawn randomization range, used only when `RandomizeVisualConditionOnSpawn` is
on. The default span — mostly presentable cars up to factory-fresh — mirrors the
legacy behavior players already expect.

### `ShowLegacyModsInUmm`

**Default: `false`**

Adds a synthetic UMM row for every legacy data package, putting them in UMM's
global mod list. Kept opt-in for a performance reason: UMM walks that list from
several per-frame callbacks. FUSE's own Mods page is the primary place to inspect
legacy packages.

## Update Check

### `EnableUpdateCheck`

**Default: `true`**

On startup, FUSE asks GitHub — which holds the canonical versioning — whether a
newer *stable* release exists. If one does, it shows a non-blocking notice: a
one-time toast on the next map load, plus an "Update available" line with a
download button on the FUSE window's Status page. It never blocks play, and a
build is only ever flagged when a newer stable release is confirmed.

Only a public list of release versions is fetched. The request carries no FUSE
account, save, or mod-list data; GitHub sees only the normal metadata of any
HTTPS request, such as your IP address. Release candidates do not trigger the
notice, and a local or development build (version `0.0.0`) skips the check
entirely. The download link points at Nexus for a Nexus-installed copy and at
GitHub otherwise.

Turn this off to stop the automatic startup check from making any network
request. The manual `/fuse.update` command still checks on demand — run it to see
the current status or force a re-check.

## Multiplayer

### `DirectStoreNativeDeserialize`

**Default: `true`**

Advanced. When FUSE mounts a mod asset pack directly (the normal case without
AssetLoader), the pack's first deserialization each map load goes through the
game's own `ContainerSerialization.Deserialize` entry point — exactly what the
game does for a natively loaded pack — so old-loader Harmony patches on that
method apply to mod packs too. LegosLibraryOfStuff injects its clone
definitions (repaint liveries, LLW tender swaps) there; without this, clones of
mod-pack cars never existed and saves referencing them showed "orphaned cars".

Set to `false` to fall back to FUSE's Newtonsoft-only loader for the cold load
(the pre-1.1 behaviour). Only useful as a diagnostic escape hatch if an
old-loader patch misbehaves; expect LegosLibraryOfStuff variants of mod cars to
disappear while it is off.

### `BlockNonHostMultiplayerClientWorldApply`

**Default: `false`**

When on, non-host clients are blocked from applying runtime world changes
entirely. Off by default so private multiplayer tests behave like RailLoader,
where every player is expected to have the same mods installed locally.

FUSE does not sync package contents over the network in either mode. Turning this
on is a strict guard for servers that would rather refuse a mismatched client than
let it desync. See [FAQ.md](FAQ.md#multiplayer) for what FUSE does and does not
guarantee in multiplayer.

## Logging And Reports

### `MirrorInfoToPlayerLog`

**Default: `false`** · *not in the shipped `Info.json`*

Mirrors FUSE's info-level log lines into Unity's `Player.log` in addition to
`FUSE.log`. Useful when correlating FUSE activity against base-game or other-mod
log output on one timeline.

### `VerboseApplyReportDetails`

**Default: `false`**

Expands the per-package detail in the apply report. Turn this on before
reproducing an issue for a bug report — the extra detail usually identifies the
offending object without a second reproduction.

### `MirrorAssetPacksToLocalLow`

**Default: `false`**

Mirrors discovered asset packs into the LocalLow folder. A compatibility aid for
setups where asset packs are not found in their normal location.

## Diagnostic Overlays

Author and debugging tools. All default to off, and none of them are intended to
be left on during normal play — the overlays draw every frame.

### `ShowAdvancedHealthDetails`

**Default: `false`**

Expands the FUSE menu's health display with advanced detail.

### `ShowTrackDebugOverlay` / `ShowTrackDebugSpanPaths`

**Default: `false`** · *neither is in the shipped `Info.json`*

Draws the track debug overlay, and within it the span paths. `ShowTrackDebugSpanPaths`
is a sub-toggle — it needs the overlay on to show anything.

### `ShowSceneryDebugOverlay` / `ShowSceneryDebugAdvanced`

**Default: `false`** · *neither is in the shipped `Info.json`*

The scenery equivalent: an overlay plus its advanced-detail sub-toggle.

### `ShowWorldLabelsOverlay`

**Default: `false`** · *not in the shipped `Info.json`*

The master switch for in-world labels on FUSE objects. The five per-kind toggles
below only matter when this is on.

### World Label Kinds

| Setting | Default |
| --- | --- |
| `WorldLabelsShowScenery` | `true` |
| `WorldLabelsShowSceneClones` | `true` |
| `WorldLabelsShowIndustries` | `true` |
| `WorldLabelsShowTrackNodes` | `false` |
| `WorldLabelsShowTrackSegments` | `false` |

None appear in the shipped `Info.json`.

The per-kind defaults are deliberately asymmetric: the three that authors usually
want are on, so flipping the master switch shows something useful immediately,
while the two dense kinds stay off. A typical map has over a thousand track nodes,
and enabling those labels paves the screen. Opt into them individually when you
actually need them.

## Performance Diagnostics

### `EnableFrameSpikeDiagnostics`

**Default: `false`**

Logs a line whenever a frame takes longer than the threshold below. This is a
measurement tool for attributing stutter, not a fix — it does not make anything
faster. Takes effect immediately, without a restart.

### `FrameSpikeThresholdMs`

**Default: `100`**, clamped to **20–500**.

The frame duration that counts as a spike. 100 ms is roughly a clearly felt hitch
at any refresh rate without flagging ordinary frame-time noise.

The bounds are enforced on every path that writes this value, so no source can set
one outside the range: below 20 ms, ordinary frames at low fps would log as
spikes; above 500 ms you are measuring stalls, not spikes. A non-numeric or `NaN`
value degrades to the default rather than escaping the clamp.

### `EnableSceneryCullingDiagnostics`

**Default: `false`**

Logs scenery culling decisions. Useful when scenery pops in late or fails to
appear.

### `ForceConstrainedVramMode`

**Default: `false`**

Forces the constrained-VRAM scenery policy on hardware that would not otherwise
trigger it — a test override for reproducing low-VRAM behavior on a larger card.

**Restart the game before collecting a comparison capture.** The setting persists,
but the policy it selects is chosen during startup.

### `EnableNativeLeakStackTraces`

**Default: `false`**

Enables Unity's native-allocation leak stack traces. These are process-wide and
expensive, so keep them off unless actively chasing a native leak. FUSE restores
the host's prior mode when it unloads.

## Experimental

Off by default and not part of a supported configuration. Enabling one is a
testing decision — back up your save first.

### `EnableExperimentalEarlyScenePathSuppression`

**Default: `false`**

Suppresses base-game scene paths early in load, with an 8-second timeout. Cannot
be re-enabled mid-session once a map is loaded.

### `EnableTargetedTerrainInvalidation`

**Default: `false`** · *not in the shipped `Info.json`*

Narrows the post-apply terrain rebuild to only the tiles FUSE actually touched,
instead of a full teardown and reload. A significant load-time win, but
timing-sensitive because masks load asynchronously, so it stays off until
validated in-game. It falls back to the full rebuild whenever no footprint was
captured.

## Settings Not In The Shipped `Info.json`

These 15 resolve to their built-in defaults unless you add them to the `Settings`
object by name or set them through the FUSE menu:

`MirrorInfoToPlayerLog`, `EnableTargetedTerrainInvalidation`,
`ShowTrackDebugOverlay`, `ShowTrackDebugSpanPaths`, `ShowSceneryDebugOverlay`,
`ShowSceneryDebugAdvanced`, `ShowWorldLabelsOverlay`, `WorldLabelsShowScenery`,
`WorldLabelsShowSceneClones`, `WorldLabelsShowIndustries`,
`WorldLabelsShowTrackNodes`, `WorldLabelsShowTrackSegments`,
`RandomizeVisualConditionOnSpawn`, `RandomVisualConditionMin`,
`RandomVisualConditionMax`.

## Related

- [CONSOLE_COMMANDS.md](CONSOLE_COMMANDS.md) — the commands these overlays complement
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) — which diagnostic to enable for which symptom
