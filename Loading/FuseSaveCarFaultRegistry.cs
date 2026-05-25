using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Data;
using FUSE.Infrastructure;

namespace FUSE.Loading
{
    /// <summary>
    /// In-memory record of every car instance the save load could
    /// not restore due to an unresolved prototype identifier. Cleared
    /// at the start of every save load (so we don't carry stale
    /// records across reloads) and populated as the game's
    /// <c>TrainController.AddCarInternal</c> raises
    /// <c>PrefabStore.UnknownIdentifierException</c> per car.
    /// Surfaced through the FUSE Health UI for the user to inspect
    /// and (in a follow-up flow) replace the broken cars with
    /// compatible working types so the save's interchange / waybill
    /// / consist references stay coherent.
    /// </summary>
    internal static class FuseSaveCarFaultRegistry
    {
        private static readonly object Sync = new object();
        private static readonly List<FuseSaveCarFault> Faults = new List<FuseSaveCarFault>();

        /// <summary>
        /// Snapshot count of currently-recorded faults. Cheap to call
        /// from the UI rebuild loop.
        /// </summary>
        public static int Count
        {
            get
            {
                lock (Sync)
                {
                    return Faults.Count;
                }
            }
        }

        /// <summary>
        /// Returns a sorted snapshot of all recorded faults. Sorted by
        /// (missing prototype id, then display name) so cars sharing
        /// the same broken type group together in the UI.
        /// </summary>
        public static IReadOnlyList<FuseSaveCarFault> GetAll()
        {
            lock (Sync)
            {
                return Faults
                    .OrderBy(f => f.MissingPrototypeId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        /// <summary>
        /// Drops every recorded fault. Called when a new save load
        /// begins so the registry reflects only the current session's
        /// failed cars — without this, switching saves would leave
        /// the previous save's broken cars listed forever.
        /// </summary>
        public static void Reset()
        {
            lock (Sync)
            {
                Faults.Clear();
            }
        }

        /// <summary>
        /// Removes the recorded fault for <paramref name="carId"/>,
        /// regardless of the missing-prototype value. Called after a
        /// replacement spawn succeeds so the UI no longer reports the
        /// car as orphaned. Returns true when a record was removed,
        /// false when no matching record was found (idempotent on
        /// repeat calls).
        /// </summary>
        public static bool RemoveByCarId(string carId)
        {
            if (string.IsNullOrEmpty(carId))
            {
                return false;
            }

            lock (Sync)
            {
                for (var index = 0; index < Faults.Count; index++)
                {
                    if (string.Equals(Faults[index].CarId, carId, StringComparison.Ordinal))
                    {
                        Faults.RemoveAt(index);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Records a fault. Idempotent on (carId, missingPrototypeId):
        /// repeated calls for the same car (e.g. if save load retries
        /// or multiple message handlers see the same exception) do
        /// not produce duplicate entries. Returns true if a new
        /// record was added, false if it was already present.
        /// </summary>
        public static bool Record(
            string carId,
            string reportingMark,
            string roadNumber,
            string missingPrototypeId,
            string locationSegmentId,
            float locationDistance,
            bool locationEndIsA,
            string reason,
            object originalSnapshotCar = null,
            object originalSnapshotProperties = null,
            int originalSnapshotVersion = 1)
        {
            if (string.IsNullOrEmpty(carId) && string.IsNullOrEmpty(missingPrototypeId))
            {
                // Nothing identifies this fault — drop it.
                return false;
            }

            lock (Sync)
            {
                for (var index = 0; index < Faults.Count; index++)
                {
                    var existing = Faults[index];
                    if (string.Equals(existing.CarId, carId ?? string.Empty, StringComparison.Ordinal) &&
                        string.Equals(existing.MissingPrototypeId, missingPrototypeId ?? string.Empty, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                Faults.Add(new FuseSaveCarFault(
                    carId,
                    reportingMark,
                    roadNumber,
                    missingPrototypeId,
                    locationSegmentId,
                    locationDistance,
                    locationEndIsA,
                    reason,
                    originalSnapshotCar,
                    originalSnapshotProperties,
                    originalSnapshotVersion));
            }

            try
            {
                FuseLog.Warning(
                    $"FUSE save-car fault recorded: car='{(string.IsNullOrEmpty(reportingMark) ? carId : reportingMark + " " + roadNumber)}' " +
                    $"id='{carId}' missingPrototype='{missingPrototypeId}' reason='{reason}'.");
            }
            catch
            {
                // Logging is best-effort; the registry is the
                // authoritative store.
            }
            return true;
        }
    }
}
