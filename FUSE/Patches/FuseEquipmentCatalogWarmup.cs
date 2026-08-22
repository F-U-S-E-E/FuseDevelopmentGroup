using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using AssetPack.Runtime;
using FUSE.Infrastructure;
using HarmonyLib;
using Model.Database;

namespace FUSE.Patches
{
    /// <summary>
    /// Spreads the Equipment catalogue's cold Definitions.json work over
    /// multiple frames. AssetPackRuntimeStore caches Container(), so touching
    /// one store per frame performs exactly the same deserialization and legacy
    /// Harmony edits the stock EquipmentWindow would trigger, without doing the
    /// entire mounted set synchronously when the player clicks Buy.
    /// </summary>
    internal static class FuseEquipmentCatalogWarmup
    {
        private static readonly FieldInfo StoresField =
            AccessTools.Field(typeof(PrefabStore), "_stores");

        private static PrefabStore _owner;
        private static AssetPackRuntimeStore[] _stores = Array.Empty<AssetPackRuntimeStore>();
        private static int _nextStore;
        private static int _failedStores;
        private static long _elapsedTicks;
        private static long _worstStoreTicks;
        private static string _worstStoreIdentifier = string.Empty;
        private static int _slowStores;

        private const double SlowStoreMilliseconds = 25d;

        internal static int PendingStoreCount => Math.Max(0, _stores.Length - _nextStore);

        internal static void Schedule(PrefabStore owner)
        {
            FusePrefabStoreAllCarDefinitionInfosFilterPatch.InvalidateCache();
            _owner = owner;
            _stores = SnapshotStores(owner);
            _nextStore = 0;
            _failedStores = 0;
            _elapsedTicks = 0;
            _worstStoreTicks = 0;
            _worstStoreIdentifier = string.Empty;
            _slowStores = 0;

            if (_stores.Length > 0)
            {
                FuseLog.Info(
                    $"FUSE scheduled Equipment catalogue warm-up stores={_stores.Length}; " +
                    "one cold definition store will be prepared per frame.");
            }
        }

        internal static void Update()
        {
            if (_owner == null || _nextStore >= _stores.Length)
            {
                return;
            }

            var currentStoreCount = ReadStoreCount(_owner);
            if (currentStoreCount >= 0 && currentStoreCount != _stores.Length)
            {
                Schedule(_owner);
                return;
            }

            var store = _stores[_nextStore++];
            if (store != null)
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    store.Container();
                }
                catch (Exception ex)
                {
                    _failedStores++;
                    FuseLog.Warning(
                        $"FUSE Equipment catalogue warm-up skipped store='{store.Identifier ?? "<unknown>"}' " +
                        $"reason='{ex.GetBaseException().Message}'. The remaining stores will still be prepared.");
                }
                finally
                {
                    var elapsedTicks = Stopwatch.GetTimestamp() - started;
                    _elapsedTicks += elapsedTicks;
                    if (elapsedTicks > _worstStoreTicks)
                    {
                        _worstStoreTicks = elapsedTicks;
                        _worstStoreIdentifier = store.Identifier ?? "<unknown>";
                    }

                    if (elapsedTicks * 1000d / Stopwatch.Frequency >= SlowStoreMilliseconds)
                    {
                        _slowStores++;
                    }
                }
            }

            if (_nextStore != _stores.Length)
            {
                return;
            }

            var elapsedMs = _elapsedTicks * 1000d / Stopwatch.Frequency;
            var worstStoreMs = _worstStoreTicks * 1000d / Stopwatch.Frequency;
            FuseLog.Info(
                $"FUSE Equipment catalogue warm-up completed stores={_stores.Length} " +
                $"failed={_failedStores} cumulativeWorkMs={elapsedMs:0.0} " +
                $"slowStores={_slowStores} slowThresholdMs={SlowStoreMilliseconds:0} " +
                $"worstStore='{_worstStoreIdentifier}' worstStoreMs={worstStoreMs:0.0} " +
                $"legoUnrelatedContainersSkipped={FuseLegosLibraryCompatibility.ContainersSkippedByFastPath}. " +
                "Opening the buy menu will reuse the prepared containers.");
        }

        private static AssetPackRuntimeStore[] SnapshotStores(PrefabStore owner)
        {
            if (owner == null || StoresField == null)
            {
                return Array.Empty<AssetPackRuntimeStore>();
            }

            try
            {
                if (!(StoresField.GetValue(owner) is IEnumerable<AssetPackRuntimeStore> stores))
                {
                    return Array.Empty<AssetPackRuntimeStore>();
                }

                return new List<AssetPackRuntimeStore>(stores).ToArray();
            }
            catch
            {
                return Array.Empty<AssetPackRuntimeStore>();
            }
        }

        private static int ReadStoreCount(PrefabStore owner)
        {
            if (owner == null || StoresField == null)
            {
                return -1;
            }

            try
            {
                return StoresField.GetValue(owner) is System.Collections.ICollection stores
                    ? stores.Count
                    : -1;
            }
            catch
            {
                return -1;
            }
        }
    }
}
