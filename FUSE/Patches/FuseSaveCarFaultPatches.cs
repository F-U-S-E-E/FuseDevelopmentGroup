using System;
using System.Reflection;
using HarmonyLib;
using Model;
using Model.Database;
using FUSE.Infrastructure;
using FUSE.Loading;

namespace FUSE.Patches
{
    /// <summary>
    /// Clears the save-car fault registry at the start of every
    /// snapshot load so the registry reflects only the cars that
    /// failed THIS load. Without this prefix the registry would
    /// retain faults from prior loads (a different save or a
    /// previous attempt at the same save) and the orphaned-car
    /// window and load report would mis-report counts.
    /// </summary>
    [HarmonyPatch(typeof(TrainController), "HandleSnapshotCars")]
    internal static class FuseTrainControllerSnapshotCarsResetPatch
    {
        private static void Prefix()
        {
            try
            {
                FuseSaveCarFaultRegistry.Reset();
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE save-car fault registry reset failed softly: {ex.GetBaseException().Message}");
            }
        }

        /// <summary>
        /// Pops the orphaned-car replacement window after the save's
        /// car-snapshot processing completes, exactly when the
        /// registry has been populated by every failing
        /// <c>AddCarInternal</c> call's Finalizer. Equivalent in
        /// spirit to how the game itself fires
        /// <c>LostCarPlacerWindow.ShowIfNeeded</c> after a snapshot
        /// load completes — we just route to our own window when
        /// there are FUSE-filtered orphans to surface. The actual
        /// window creation no-ops when no orphans were recorded so
        /// saves that load cleanly never pop a dialog.
        /// </summary>
        private static void Postfix()
        {
            // Flush any deferred map-load report first so its toast
            // and cached strings include this load's orphan count
            // (FuseLifecycle schedules but defers publish; this is
            // the call that actually fires it). Doing this BEFORE
            // popping the orphan window keeps the user-facing
            // ordering sensible: see the load summary first, then
            // the orphan-replacement dialog if any orphans were
            // recorded.
            try
            {
                FuseLoadReport.FlushScheduledMapLoadReport();
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE deferred map-load report flush failed softly: {ex.GetBaseException().Message}");
            }

            try
            {
                FUSE.Interface.FuseOrphanedCarWindow.ShowIfNeeded();
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE orphaned-car window show failed softly: {ex.GetBaseException().Message}");
            }
        }
    }

    /// <summary>
    /// Records a save-car fault every time
    /// <c>TrainController.AddCarInternal</c> throws
    /// <see cref="PrefabStore.UnknownIdentifierException"/>. The
    /// game's existing catch in <c>HandleSnapshotCars</c> then
    /// removes the offending car as usual; we observe the exception
    /// without altering control flow.
    ///
    /// <para>Implemented as a Harmony Finalizer so the snapshot-car
    /// instance is in scope (we need its id, road number, location)
    /// and so we can read <c>__exception</c> without changing what
    /// the caller sees. Returning the original exception keeps the
    /// game's <c>catch (PrefabStore.UnknownIdentifierException ex2)</c>
    /// block working unchanged.</para>
    ///
    /// <para>Uses reflection on the <c>Snapshot.Car</c> struct rather
    /// than a direct field reference because the struct ships in
    /// <c>Assembly-CSharp</c> and a refactor on the host side
    /// renaming a field would otherwise produce a hard
    /// <c>FieldAccessException</c> at load. Read failures degrade
    /// to recording an "unknown" placeholder so the registry still
    /// reflects that something failed.</para>
    /// </summary>
    [HarmonyPatch(typeof(TrainController), "AddCarInternal")]
    internal static class FuseTrainControllerAddCarInternalFaultPatch
    {
        private static FieldInfo _carIdField;
        private static FieldInfo _carPrototypeIdField;
        private static FieldInfo _carRoadNumberField;
        private static FieldInfo _carReportingMarkField;
        private static FieldInfo _carLocationField;
        private static FieldInfo _locationSegmentIdField;
        private static FieldInfo _locationDistanceField;
        private static FieldInfo _locationEndIsAField;
        private static bool _reflectionInitialized;

        private static Exception Finalizer(
            object snapshotCar,
            object snapshotCarProperties,
            int version,
            Exception __exception)
        {
            if (__exception == null || !(__exception is PrefabStore.UnknownIdentifierException uie))
            {
                // Either no exception or an unrelated one — leave
                // the game's existing handling untouched.
                return __exception;
            }

            try
            {
                EnsureReflectionInitialized(snapshotCar?.GetType());

                var carId = ReadString(_carIdField, snapshotCar);
                var prototypeId = ReadString(_carPrototypeIdField, snapshotCar) ?? uie.Identifier;
                var roadNumber = ReadString(_carRoadNumberField, snapshotCar);
                var reportingMark = ReadString(_carReportingMarkField, snapshotCar);

                var locationSegmentId = string.Empty;
                var locationDistance = 0f;
                var locationEndIsA = false;
                if (_carLocationField != null)
                {
                    var locationBoxed = _carLocationField.GetValue(snapshotCar);
                    if (locationBoxed != null)
                    {
                        locationSegmentId = ReadString(_locationSegmentIdField, locationBoxed) ?? string.Empty;
                        var distanceBoxed = _locationDistanceField?.GetValue(locationBoxed);
                        if (distanceBoxed is float f)
                        {
                            locationDistance = f;
                        }
                        var endBoxed = _locationEndIsAField?.GetValue(locationBoxed);
                        if (endBoxed is bool b)
                        {
                            locationEndIsA = b;
                        }
                    }
                }

                FuseSaveCarFaultRegistry.Record(
                    carId,
                    reportingMark,
                    roadNumber,
                    prototypeId,
                    locationSegmentId,
                    locationDistance,
                    locationEndIsA,
                    $"prototype identifier '{prototypeId}' could not be resolved at save-load time " +
                    "(legacy SCAssetPacks definition was filtered or the prefab is missing).",
                    originalSnapshotCar: snapshotCar,
                    originalSnapshotProperties: snapshotCarProperties,
                    originalSnapshotVersion: version);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not record save-car fault for failing snapshot car (identifier='{uie.Identifier}'): {ex.GetBaseException().Message}");
            }

            return __exception;
        }

        private static void EnsureReflectionInitialized(Type snapshotCarType)
        {
            if (_reflectionInitialized || snapshotCarType == null)
            {
                return;
            }

            _carIdField = snapshotCarType.GetField("id", BindingFlags.Public | BindingFlags.Instance);
            _carPrototypeIdField = snapshotCarType.GetField("prototypeId", BindingFlags.Public | BindingFlags.Instance);
            _carRoadNumberField = snapshotCarType.GetField("roadNumber", BindingFlags.Public | BindingFlags.Instance);
            _carReportingMarkField = snapshotCarType.GetField("ReportingMark", BindingFlags.Public | BindingFlags.Instance);
            _carLocationField = snapshotCarType.GetField("Location", BindingFlags.Public | BindingFlags.Instance);

            var locationType = _carLocationField?.FieldType;
            if (locationType != null)
            {
                _locationSegmentIdField = locationType.GetField("segmentId", BindingFlags.Public | BindingFlags.Instance);
                _locationDistanceField = locationType.GetField("distance", BindingFlags.Public | BindingFlags.Instance);
                _locationEndIsAField = locationType.GetField("endIsA", BindingFlags.Public | BindingFlags.Instance);
            }

            _reflectionInitialized = true;
        }

        private static string ReadString(FieldInfo field, object target)
        {
            if (field == null || target == null)
            {
                return null;
            }
            try
            {
                return field.GetValue(target) as string;
            }
            catch
            {
                return null;
            }
        }
    }
}
