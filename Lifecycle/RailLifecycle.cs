using System;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using RAIL.Cache;
using RAIL.Console;
using RAIL.Events;
using RAIL.Infrastructure;
using RAIL.Loading;

namespace RAIL.Lifecycle
{
    internal sealed class RailLifecycle
    {
        internal void Register()
        {
            try
            {
                Messenger.Default.Register<MapDidLoadEvent>(this, OnMapDidLoad);
                Messenger.Default.Register<GraphDidRebuildCollections>(this, OnGraphDidRebuildCollections);
                Messenger.Default.Register<MapWillUnloadEvent>(this, OnMapWillUnload);
                RailEarlyLoader.Initialize();
                RailLog.Info("RAIL lifecycle registered.");
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL lifecycle registration failed", ex);
                throw;
            }
        }

        internal void Unregister()
        {
            try
            {
                RailEarlyLoader.Shutdown();
                Messenger.Default.Unregister(this);
                RailLog.Info("RAIL lifecycle unregistered.");
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL lifecycle unregister failed", ex);
            }
        }

        private void OnMapDidLoad(MapDidLoadEvent message)
        {
            var loadedCount = 0;
            var appliedCount = 0;
            var pipelineCompleted = false;
            RailLoadReport.ResetMapLoad();

            try
            {
                RailCacheRegistry.RebuildAll();
                loadedCount = RailDataPackageDiscovery.LoadPackagesFromDisk(false);
                appliedCount = RailDataPackageDiscovery.ApplyLoadedPackages("map load");
                RailEarlyLoader.ApplyFallbackAfterMapLoad();
                RailLog.Info($"RAIL map-load package pipeline completed: loadedFromDisk={loadedCount}, appliedToRuntime={appliedCount}.");
                pipelineCompleted = true;
            }
            catch (Exception ex)
            {
                RailLoadReport.RecordNotice("Map-load package pipeline failed: " + ex.Message);
                RailLog.Exception("RAIL map-load handling failed", ex);
            }

            // Console handler is created during scene activation, so we re-attempt
            // registration here even if the early Load attempt missed it.
            try
            {
                RailConsoleRegistrar.TryRegisterAll();
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL console registration on map-load failed.", ex);
            }

            RailLoadReport.PublishMapLoadReport(
                pipelineCompleted ? "map load" : "map load failed",
                loadedCount,
                appliedCount);
        }

        private void OnGraphDidRebuildCollections(GraphDidRebuildCollections message)
        {
            try
            {
                RailCacheRegistry.RebuildAll();
                RailWorldSuppressor.ApplyTrackGroupSuppressionsAfterGraphLoad("graph rebuild");
                RailEvents.RaiseGraphRebuilt();
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL graph-rebuild lifecycle handling failed", ex);
            }
        }

        private void OnMapWillUnload(MapWillUnloadEvent message)
        {
            try
            {
                RailWorldSuppressor.RestoreAll("map unload");
                RailEarlyLoader.RestoreOnMapUnload();
                RailModLoader.UnloadAll();
                RailMapTileRegistry.ClearAll();
                RailCacheRegistry.ClearAll();
                RailRuntimeRebindService.ResetUnknownKindLog();
                RailLog.Info("RAIL cleared runtime state for map unload.");
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL map-unload handling failed", ex);
            }
        }
    }
}
