using System;
using System.Diagnostics;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using FUSE.API;
using FUSE.Cache;
using FUSE.Console;
using FUSE.Events;
using FUSE.Infrastructure;
using FUSE.Loading;

namespace FUSE.Lifecycle
{
    internal sealed class FuseLifecycle
    {
        internal void Register()
        {
            try
            {
                Messenger.Default.Register<MapDidLoadEvent>(this, OnMapDidLoad);
                Messenger.Default.Register<GraphDidRebuildCollections>(this, OnGraphDidRebuildCollections);
                Messenger.Default.Register<MapWillUnloadEvent>(this, OnMapWillUnload);
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

        private void OnMapDidLoad(MapDidLoadEvent message)
        {
            var mapLoadStopwatch = Stopwatch.StartNew();
            var loadedCount = 0;
            var appliedCount = 0;
            var pipelineCompleted = false;
            var canMutateWorld = FuseMultiplayerGuard.CanApplyWorldMutations("map load");
            FuseLoadReport.ResetMapLoad();

            try
            {
                FuseLegacyAssemblyHost.LoadAllAvailableAssemblies("map load fallback");
                var cacheStopwatch = Stopwatch.StartNew();
                FuseCacheRegistry.RebuildAll();
                FusePerformanceMetrics.RecordTiming("cache rebuild before map load apply", cacheStopwatch.ElapsedMilliseconds);
                FuseLog.Info($"FUSE load timing phase='cache rebuild before map load apply' elapsedMs={cacheStopwatch.ElapsedMilliseconds}.");
                TrackAPI.CaptureBaseGraphSnapshot("map load before FUSE package apply");
                loadedCount = FuseDataPackageDiscovery.LoadPackagesFromDisk(false);
                if (canMutateWorld)
                {
                    appliedCount = FuseDataPackageDiscovery.ApplyLoadedPackages("map load");
                    TrackAPI.RemoveInvalidTrackSpans("map load after FUSE package apply");
                    TrackAPI.ScrubCtcSignalReferences("map load after FUSE package apply");
                    IndustryAPI.ScrubIndustryComponentCaches("map load after FUSE package apply");
                    IndustryAPI.DisableOrphanedBaseGameIndustries("map load after FUSE package apply");
                    TrackAPI.DisableInvalidTrackMarkers("map load after FUSE package apply");
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
            if (canMutateWorld)
            {
                var mapRebuildStopwatch = Stopwatch.StartNew();
                FuseRuntimeReloadService.ReloadTerrain("map-load map-mask rebuild");
                FusePerformanceMetrics.RecordTiming("map mask rebuild", mapRebuildStopwatch.ElapsedMilliseconds);
                FuseLog.Info($"FUSE load timing phase='map mask rebuild' elapsedMs={mapRebuildStopwatch.ElapsedMilliseconds}.");
            }
            else
            {
                FuseLog.Info("FUSE skipped map mask rebuild on non-host multiplayer client.");
            }

            // Console handler is created during scene activation, so we re-attempt
            // registration here even if the early Load attempt missed it.
            try
            {
                var consoleStopwatch = Stopwatch.StartNew();
                FuseConsoleRegistrar.TryRegisterAll();
                FuseLegacyAssemblyHost.RetryPendingConsoleCommands();
                FusePerformanceMetrics.RecordTiming("console registration", consoleStopwatch.ElapsedMilliseconds);
                FuseLog.Info($"FUSE load timing phase='console registration' elapsedMs={consoleStopwatch.ElapsedMilliseconds}.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE console registration on map-load failed.", ex);
            }

            FuseLoadReport.PublishMapLoadReport(
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
                FuseCacheRegistry.RebuildAll();
                FuseWorldSuppressor.ApplyTrackGroupSuppressionsAfterGraphLoad("graph rebuild");
                TrackAPI.ScrubCtcSignalReferences("graph rebuild");
                FuseEvents.RaiseGraphRebuilt();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE graph-rebuild lifecycle handling failed", ex);
            }
        }

        private void OnMapWillUnload(MapWillUnloadEvent message)
        {
            try
            {
                FuseWorldSuppressor.RestoreAll("map unload");
                FuseEarlyLoader.RestoreOnMapUnload();
                FuseModLoader.UnloadAll(resetDiscovery: true, restoreTrackSnapshots: false);
                FuseMapTileRegistry.ClearAll();
                TrackAPI.ClearBaseGraphSnapshot();
                FuseCacheRegistry.ClearAll();
                FuseRuntimeRebindService.ResetUnknownKindLog();
                FuseLog.Info("FUSE cleared runtime state for map unload.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE map-unload handling failed", ex);
            }
        }

    }
}
