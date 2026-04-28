using System;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using RAIL.Cache;
using RAIL.Events;
using RAIL.Infrastructure;
using RAIL.Loading;

namespace RAIL.Lifecycle
{
    internal sealed class RailLifecycle
    {
        internal void Register()
        {
            Messenger.Default.Register<MapDidLoadEvent>(this, OnMapDidLoad);
            Messenger.Default.Register<GraphDidRebuildCollections>(this, OnGraphDidRebuildCollections);
            Messenger.Default.Register<MapWillUnloadEvent>(this, OnMapWillUnload);
            RailLog.Info("RAIL lifecycle registered.");
        }

        internal void Unregister()
        {
            Messenger.Default.Unregister(this);
        }

        private void OnMapDidLoad(MapDidLoadEvent message)
        {
            try
            {
                RailCacheRegistry.RebuildAll();
                RailDataPackageDiscovery.LoadAllAvailablePackages();
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL map-load handling failed", ex);
            }
        }

        private void OnGraphDidRebuildCollections(GraphDidRebuildCollections message)
        {
            RailCacheRegistry.RebuildAll();
            RailEvents.RaiseGraphRebuilt();
        }

        private void OnMapWillUnload(MapWillUnloadEvent message)
        {
            RailModLoader.UnloadAll();
            RailMapTileRegistry.ClearAll();
            RailCacheRegistry.ClearAll();
        }
    }
}
