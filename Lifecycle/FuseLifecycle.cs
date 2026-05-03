using System;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
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
            var loadedCount = 0;
            var appliedCount = 0;
            var pipelineCompleted = false;
            FuseLoadReport.ResetMapLoad();

            try
            {
                FuseCacheRegistry.RebuildAll();
                loadedCount = FuseDataPackageDiscovery.LoadPackagesFromDisk(false);
                appliedCount = FuseDataPackageDiscovery.ApplyLoadedPackages("map load");
                FuseEarlyLoader.ApplyFallbackAfterMapLoad();
                FuseLog.Info($"FUSE map-load package pipeline completed: loadedFromDisk={loadedCount}, appliedToRuntime={appliedCount}.");
                pipelineCompleted = true;
            }
            catch (Exception ex)
            {
                FuseLoadReport.RecordNotice("Map-load package pipeline failed: " + ex.Message);
                FuseLog.Exception("FUSE map-load handling failed", ex);
            }

            // Console handler is created during scene activation, so we re-attempt
            // registration here even if the early Load attempt missed it.
            try
            {
                FuseConsoleRegistrar.TryRegisterAll();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE console registration on map-load failed.", ex);
            }

            FuseLoadReport.PublishMapLoadReport(
                pipelineCompleted ? "map load" : "map load failed",
                loadedCount,
                appliedCount);
        }

        private void OnGraphDidRebuildCollections(GraphDidRebuildCollections message)
        {
            try
            {
                FuseCacheRegistry.RebuildAll();
                FuseWorldSuppressor.ApplyTrackGroupSuppressionsAfterGraphLoad("graph rebuild");
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
                FuseModLoader.UnloadAll();
                FuseMapTileRegistry.ClearAll();
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
