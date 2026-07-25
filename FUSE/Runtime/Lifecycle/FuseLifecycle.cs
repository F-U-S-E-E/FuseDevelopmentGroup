using System;
using System.Diagnostics;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using FUSE.Runtime.API;
using FUSE.Runtime.Cache;
using FUSE.Interface;
using FUSE.Interface.Console;
using FUSE.Runtime.Events;
using FUSE.Infrastructure;
using FUSE.Loading;

namespace FUSE.Runtime.Lifecycle
{
    internal sealed class FuseLifecycle
    {
        internal void Register()
        {
            try
            {
                Messenger.Default.Register<MapWillLoadEvent>(this, OnMapWillLoad);
                Messenger.Default.Register<MapDidLoadEvent>(this, OnMapDidLoad);
                Messenger.Default.Register<GraphDidRebuildCollections>(this, OnGraphDidRebuildCollections);
                Messenger.Default.Register<MapWillUnloadEvent>(this, OnMapWillUnload);
                Messenger.Default.Register<GameModeDidChange>(this, OnGameModeDidChange);
                FuseEarlyLoader.Initialize();
                FuseLog.Info("FUSE lifecycle registered.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE lifecycle registration failed", ex);
                throw;
            }
        }

        internal void Unregister()
        {
            try
            {
                FuseEarlyLoader.Shutdown();
                Messenger.Default.Unregister(this);
                FuseLog.Info("FUSE lifecycle unregistered.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE lifecycle unregister failed", ex);
            }
        }

        // Earliest clean "load started" signal (sent at the top of
        // GlobalGameManager._LoadMap, before any scene work). Shows the FUSE
        // enhanced loading screen so it owns the visuals for the whole load.
        private void OnMapWillLoad(MapWillLoadEvent message)
        {
            // Advance the generation before any work for this map can start.
            // Resetting in MapDidLoad erased failures observed during scene load
            // and also made current-load tasks look like late previous-map work.
            FUSE.Patches.FuseSceneryLoadFailurePatch.ResetForNewMap();
            FuseLoadReport.ResetMapLoad();

            try
            {
                FuseLoadingScreen.BeginLoad("map load");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE enhanced loading screen begin-load failed", ex);
            }
        }

        private void OnMapDidLoad(MapDidLoadEvent message)
        {
            // Run the whole FUSE post-load pipeline, then ALWAYS tell the enhanced
            // loading screen the pipeline is done — even on a throw or a non-host
            // multiplayer early-out — so the two-flag hide gate can release and the
            // player is never trapped behind the screen.
            try
            {
                RunMapDidLoadPipeline();
            }
            finally
            {
                try
                {
                    FuseLoadingScreen.NotifyFusePipelineComplete();
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE enhanced loading screen pipeline-complete signal failed", ex);
                }
            }
        }

        private static void RunMapDidLoadPipeline()
        {
            var mapLoadStopwatch = Stopwatch.StartNew();
            var loadedCount = 0;
            var appliedCount = 0;
            var pipelineCompleted = false;
            var canMutateWorld = FuseMultiplayerGuard.CanApplyWorldMutations("map load");

            // Defer per-object map-mask refresh across the whole apply: the single
            // trailing terrain rebuild (below) re-evaluates every live mask at once,
            // so the per-object GetComponentsInChildren + modifier churn during apply
            // is redundant. Also accumulates the footprint FUSE touched for the opt-in
            // targeted invalidation. Closed in the finally around the terrain rebuild.
            IDisposable terrainScope = null;

            try
            {
                terrainScope = FuseTerrainRefreshScope.Begin();
                FuseLegacyAssemblyHost.LoadAllAvailableAssemblies("map load fallback");
                var cacheStopwatch = Stopwatch.StartNew();
                FuseCacheRegistry.RebuildAll();
                FusePerformanceMetrics.RecordTiming("cache rebuild before map load apply", cacheStopwatch.ElapsedMilliseconds);
                FuseLog.Info($"FUSE load timing phase='cache rebuild before map load apply' elapsedMs={cacheStopwatch.ElapsedMilliseconds}.");
                TrackAPI.CaptureBaseGraphSnapshot("map load before FUSE package apply");
                FuseLoadingScreen.SetStep("Applying mods", "Loading mod packages");
                loadedCount = FuseDataPackageDiscovery.LoadPackagesFromDisk(false);
                if (canMutateWorld)
                {
                    // Open the deferred-scenery window so eligible static scenery
                    // created during apply is queued for post-load activation instead
                    // of being activated inline on the loading-screen critical path.
                    FuseDeferredSceneryActivator.OpenInitialMapLoadWave();
                    FuseLoadingScreen.SetStep("Applying mods", "Applying definitions");
                    appliedCount = FuseDataPackageDiscovery.ApplyLoadedPackages("map load");
                    // Run the cleanup cluster inside one batch so the rebuild
                    // RemoveInvalidTrackSpans requests folds together with any
                    // rebuild industry/marker cleanup may also request. Without
                    // this, RemoveInvalidTrackSpans fires its own full rebuild
                    // while the rest of the cleanup is still running.
                    FuseLoadingScreen.SetStep("Rebuilding track graph");
                    TrackAPI.BeginBatch();
                    try
                    {
                        TrackAPI.RemoveInvalidTrackSpans("map load after FUSE package apply");
                        TrackAPI.ScrubCtcSignalReferences("map load after FUSE package apply");
                        IndustryAPI.ScrubIndustryComponentCaches("map load after FUSE package apply");
                        IndustryAPI.DisableOrphanedBaseGameIndustries("map load after FUSE package apply");
                        TrackAPI.DisableInvalidTrackMarkers("map load after FUSE package apply");
                        // Wholesale invalidate every segment's cached
                        // BezierCurve so the rebuild that EndBatch(true)
                        // fires below computes fresh curves against the
                        // post-migration node transforms. Without this,
                        // segments whose endpoint node positions or
                        // rotations were mutated by a later legacy
                        // mixinto migration (e.g. Foxy's KaterRepair-migration
                        // moving a Bryson Tweaks switch node) keep the
                        // stale curve baked in at first-access, and
                        // <c>SwitchGeometry.Calculate</c> in
                        // <c>TrackObjectManager.BuildDescriptors</c>
                        // throws "Switch tracks do not intersect" —
                        // silently dropping the switch and every segment
                        // attached to it from the mesh build.
                        TrackAPI.InvalidateAllCurves("map load after FUSE package apply");
                    }
                    finally
                    {
                        TrackAPI.EndBatch(true);
                    }
                    var earlyLoaderStopwatch = Stopwatch.StartNew();
                    FuseEarlyLoader.ApplyFallbackAfterMapLoad();
                    FusePerformanceMetrics.RecordTiming("early-loader fallback after map load", earlyLoaderStopwatch.ElapsedMilliseconds);
                    FuseLog.Info($"FUSE load timing phase='early-loader fallback after map load' elapsedMs={earlyLoaderStopwatch.ElapsedMilliseconds}.");
                }
                else
                {
                    FuseLoadReport.RecordNotice(FuseMultiplayerGuard.GetWorldMutationBlockReason("map load"));
                    FuseLog.Info("FUSE skipped map-load runtime apply, invalid track-marker cleanup, and early-loader fallback on non-host multiplayer client.");
                }

                FuseLog.Info($"FUSE map-load package pipeline completed: loadedFromDisk={loadedCount}, appliedToRuntime={appliedCount}.");
                pipelineCompleted = true;
            }
            catch (Exception ex)
            {
                FuseLoadReport.RecordNotice("Map-load package pipeline failed: " + ex.Message);
                FuseLog.Exception("FUSE map-load handling failed", ex);
            }

            // Baked MapMask components (e.g. CLB_Plate-style scenery prefabs with
            // kind:"MapMask" in their asset-pack Definitions.json) are added to
            // GameObjects during scenery apply, but the terrain SDF bake that cuts
            // the terrain mask has already run by MapDidLoadEvent time. Without an
            // explicit rebuild the new RectangleMapMask components sit on their
            // GameObjects unused and the terrain shows dark uncut patches under
            // every placed object that relies on a baked mask (turntables, wending
            // houses, bridge piers, sawmills, etc.).
            // Calling MapManager.RebuildAll() here mirrors what AlinasMapMod's
            // "Rebuild Map" button does and forces the terrain to re-bake with the
            // now-live mask components.
            try
            {
                if (canMutateWorld)
                {
                    FuseLoadingScreen.SetStep("Rebaking terrain");
                    var mapRebuildStopwatch = Stopwatch.StartNew();
                    FuseRuntimeReloadService.ReloadTerrain("map-load map-mask rebuild");
                    FusePerformanceMetrics.RecordTiming("map mask rebuild", mapRebuildStopwatch.ElapsedMilliseconds);
                    FuseLog.Info($"FUSE load timing phase='map mask rebuild' elapsedMs={mapRebuildStopwatch.ElapsedMilliseconds}.");

                    // The terrain bake has now run with all eager (mask-bearing)
                    // scenery live, so start the post-load activation wave for the
                    // deferred static scenery. It activates over subsequent frames,
                    // nearest the player first, off the loading-screen critical path.
                    FuseDeferredSceneryActivator.BeginDrain("map load");
                }
                else
                {
                    FuseLog.Info("FUSE skipped map mask rebuild on non-host multiplayer client.");
                }
            }
            finally
            {
                // Close the deferral scope only AFTER the terrain rebuild has read the
                // accumulated footprint. Report how much per-object refresh work the
                // single trailing rebuild absorbed (measurement for the #3 win).
                var deferredRefreshes = FuseTerrainRefreshScope.DeferredRefreshCalls;
                terrainScope?.Dispose();
                if (deferredRefreshes > 0)
                {
                    FusePerformanceMetrics.RecordCount("map-load deferred mask refreshes", deferredRefreshes);
                    FuseLog.Info(
                        $"FUSE map-load deferred {deferredRefreshes} per-object map-mask refresh call(s); " +
                        "the single trailing terrain rebuild covered them.");
                }
            }

            // Replay FUSE's industrial-segment push to Map Enhancer now that the
            // load-time apply AND the trailing track-graph rebuild (EndBatch(true)
            // above) have settled. The inline RefreshIndustry in
            // AddOrUpdateComponents runs DURING apply — before that rebuild — so a
            // FUSE-created industry's TrackSpans have no resolvable cached segments
            // yet and nothing lands in Map Enhancer's industrial-segment cache
            // (observed as componentsRefreshed=0 for FUSE-built industries like the
            // Whittier sawmill, whose R1 log dropoff then paints as plain track).
            // Map Enhancer's own IndustryComponent.Start postfix misses them for the
            // same timing reason, so this post-rebuild pass is the only point where
            // the segments are resolvable. It no-ops when Map Enhancer isn't
            // installed and re-affirms already-registered base-game segments harmlessly.
            try
            {
                if (canMutateWorld)
                {
                    FUSE.Interface.FuseMapEnhancerCompat.RefreshAllIndustries("map load post-rebuild");
                }
                else
                {
                    FuseLog.Info("FUSE skipped Map Enhancer post-rebuild backfill on non-host multiplayer client.");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE Map Enhancer post-rebuild backfill failed", ex);
            }

            // Console handler is created during scene activation, so we re-attempt
            // registration here even if the early Load attempt missed it.
            try
            {
                FuseLoadingScreen.SetStep("Finishing up", "Registering console");
                var consoleStopwatch = Stopwatch.StartNew();
                FuseConsoleRegistrar.TryRegisterAll();
                FuseLegacyAssemblyHost.RetryPendingConsoleCommands();
                // Second attempt for the third-party guards: FUSE loads before
                // MapEnhancer and the rebill mod in UMM's order, so the
                // plugin-load attempt resolves neither ("idle (not present)")
                // and the guards never engaged in the field. By map load every
                // mod assembly is up; the installer re-resolves absent targets
                // and latches anything already installed.
                FUSE.Patches.FuseThirdPartyGuardInstaller.EnsureInstalled();
                FusePerformanceMetrics.RecordTiming("console registration", consoleStopwatch.ElapsedMilliseconds);
                FuseLog.Info($"FUSE load timing phase='console registration' elapsedMs={consoleStopwatch.ElapsedMilliseconds}.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE console registration on map-load failed.", ex);
            }

            // Defer the actual publish (toast + log summary +
            // cached strings) until the game's
            // TrainController.HandleSnapshotCars Postfix flushes it.
            // That hook fires AFTER every snapshot car has been
            // attempted, which is the only point where the orphan-
            // car registry is fully populated for this load. If we
            // published inline here the toast would say "orphans 0"
            // even when broken legacy car instances are about to be
            // recorded a few seconds later.
            // Stops were refreshed during the apply above; validate the resulting
            // stop graph (shared spans, isolated stops) before the report goes out
            // so the toast's "graph" count reflects this load.
            FusePassengerStopValidation.Run(pipelineCompleted ? "map load" : "map load failed");
            FuseLoadReport.ScheduleMapLoadReport(
                pipelineCompleted ? "map load" : "map load failed",
                loadedCount,
                appliedCount);
            FusePerformanceMetrics.RecordTiming("map load total", mapLoadStopwatch.ElapsedMilliseconds);
            FuseLog.Info($"FUSE load timing phase='map load total' elapsedMs={mapLoadStopwatch.ElapsedMilliseconds} loadedFromDisk={loadedCount} appliedToRuntime={appliedCount} completed={pipelineCompleted}.");
        }

        private void OnGraphDidRebuildCollections(GraphDidRebuildCollections message)
        {
            try
            {
                // A graph rebuild changes track topology only — the scene-scanning
                // indexes (industry, scenery, station, …) are kept current
                // incrementally by the APIs that mutate them, so refresh just the
                // graph-derived indexes (node/segment/span) here instead of re-running
                // all ~13 FindObjectsOfType scans on every graph rebuild. Falls back to
                // a full rebuild automatically if the caches were never built.
                FuseCacheRegistry.RebuildGraphIndexes();
                FuseWorldSuppressor.ApplyTrackGroupSuppressionsAfterGraphLoad("graph rebuild");
                TrackAPI.ScrubCtcSignalReferences("graph rebuild");
                FuseEvents.RaiseGraphRebuilt();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE graph-rebuild lifecycle handling failed", ex);
            }
        }

        // A mid-session game-mode change (the host-only '/mode' console
        // command) does not reload progression: the game keeps the session
        // exactly as it loaded — a progression-less session stays
        // progression-less, feature gating and track-group state stay put —
        // until the save is reloaded. FUSE deliberately matches that. No
        // correct target state is even computable here (after a
        // sandbox→company flip there is no Progression object to derive
        // section state from), and re-gating mod content alone would
        // desynchronize it from the vanilla content beside it. Surface the
        // situation instead of mutating world state.
        private void OnGameModeDidChange(GameModeDidChange message)
        {
            try
            {
                var mode = Game.State.StateManager.Shared?.GameMode.ToString() ?? "<unknown>";
                var text =
                    $"Game mode changed mid-session (now {mode}). Progression and track gating keep their " +
                    "as-loaded state until the save is reloaded — this matches the game's own behavior. " +
                    $"Save and reload to apply {mode}-mode gating.";
                FuseLog.Warning("FUSE observed a mid-session game-mode change: " + text);
                FuseLoadReport.RecordNotice(text);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE game-mode-change handling failed", ex);
            }
        }

        private void OnMapWillUnload(MapWillUnloadEvent message)
        {
            try
            {
                // FUSE does not own the unload screen (the stock "Tyin' down…"
                // progress is fine), and the post-load pipeline never runs on an
                // unload — so hide our screen immediately rather than letting the
                // two-flag gate wait on a pipeline-complete signal that never comes.
                FuseLoadingScreen.Abort("map unload");
                // Cancel any in-flight deferred scenery wave before the scenery
                // GameObjects it references are destroyed below.
                FuseDeferredSceneryActivator.CancelAndClear("map unload");
                // Drop any settling decoupled-mask re-bake: its bounds belong to the map being
                // torn down, and firing during/after unload would invalidate (or full-rebuild)
                // a half-initialized MapManager on the next load.
                FuseDecoupledMaskTerrainRebaker.Clear("map unload");
                FuseWorldSuppressor.RestoreAll("map unload");
                FuseEarlyLoader.RestoreOnMapUnload();
                FuseModLoader.UnloadAll(resetDiscovery: true, restoreTrackSnapshots: false);
                FuseMapTileRegistry.ClearAll();
                TrackAPI.ClearBaseGraphSnapshot();
                ProgressionAPI.ClearRememberedReferenceIds();
                // Unconditional settle-state reset: sandbox sessions never
                // configure a Progression, so the Unconfigure postfix never
                // fires for them — without this, a sandbox session's settled
                // flag would leak into the next load and let its staged
                // refresh run inside the stale-IsSandbox window.
                ProgressionAPI.NotifyMapUnloading();
                FuseCacheRegistry.ClearAll();
                FuseRuntimeRebindService.ResetUnknownKindLog();
                FuseSplineyPluginHost.Reset();
                FuseLog.Info("FUSE cleared runtime state for map unload.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE map-unload handling failed", ex);
            }
        }

    }
}
