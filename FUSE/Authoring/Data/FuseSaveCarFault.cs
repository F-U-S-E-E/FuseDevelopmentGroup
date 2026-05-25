using System;

namespace FUSE.Data
{
    /// <summary>
    /// A record of one car instance the save could not restore because
    /// the car's prototype identifier did not resolve to any usable
    /// definition at load time. Distinct from
    /// <see cref="FUSE.Loading.FusePackageFault"/>, which is about a
    /// MOD/PACKAGE failing to load — this is about a CAR INSTANCE
    /// inside the save whose declared type is orphaned (typically
    /// because the modern bundle for that prefab now ships under a
    /// different identifier, or the legacy definition lived in a
    /// loser SCAssetPacks pack whose bundle conflicts with the modern
    /// root sibling). The game cleans these cars up at load time;
    /// this record exists so a user-facing UI can offer to replace
    /// them with a compatible working car type instead of just losing
    /// the instance permanently.
    /// </summary>
    public sealed class FuseSaveCarFault
    {
        public FuseSaveCarFault(
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
            CarId = carId ?? string.Empty;
            ReportingMark = reportingMark ?? string.Empty;
            RoadNumber = roadNumber ?? string.Empty;
            MissingPrototypeId = missingPrototypeId ?? string.Empty;
            LocationSegmentId = locationSegmentId ?? string.Empty;
            LocationDistance = locationDistance;
            LocationEndIsA = locationEndIsA;
            Reason = reason ?? string.Empty;
            TimestampUtc = DateTime.UtcNow;
            OriginalSnapshotCar = originalSnapshotCar;
            OriginalSnapshotProperties = originalSnapshotProperties;
            OriginalSnapshotVersion = originalSnapshotVersion;
        }

        /// <summary>
        /// Internal car id (e.g. "Cyy2"). Stable across saves; used
        /// as the key for replacement operations so the new car
        /// reuses the same id and the rest of the save (waybills,
        /// trains, etc.) keeps referring to it correctly.
        /// </summary>
        public string CarId { get; }

        /// <summary>Two-to-four-character railroad mark (e.g. "NYO&amp;W").</summary>
        public string ReportingMark { get; }

        /// <summary>Numeric road number (e.g. "22701").</summary>
        public string RoadNumber { get; }

        /// <summary>
        /// The unresolvable prototype identifier the save declared
        /// (e.g. "spinecar1"). This is what the user-facing UI can
        /// search the legacy/modern alias maps with to suggest a
        /// compatible replacement.
        /// </summary>
        public string MissingPrototypeId { get; }

        public string LocationSegmentId { get; }
        public float LocationDistance { get; }
        public bool LocationEndIsA { get; }

        /// <summary>
        /// Short human-readable reason for surfacing this fault
        /// (e.g. "filtered by FUSE because the only definition lives
        /// in a duplicate-leaf-name SCAssetPacks pack").
        /// </summary>
        public string Reason { get; }

        public DateTime TimestampUtc { get; }

        /// <summary>
        /// The boxed <c>Snapshot.Car</c> struct as the game originally
        /// passed it to <c>TrainController.AddCarInternal</c>. Held as
        /// <c>object</c> because the type lives in the game assembly
        /// and we don't want the data model to take a direct compile-
        /// time reference. When a replacement is applied, the
        /// replacement flow reads/writes <c>prototypeId</c> on this
        /// box via reflection and re-invokes the game's loader, which
        /// preserves every other field (id, road number, location,
        /// reporting mark, etc.). Null when the registry was populated
        /// without snapshot capture (e.g. unit-test recordings).
        /// </summary>
        public object OriginalSnapshotCar { get; }

        /// <summary>
        /// The per-car properties dictionary the game's loader was
        /// going to attach to this instance (waybill, load contents,
        /// content description, etc.). Held as <c>object</c> for the
        /// same compile-time-decoupling reason as
        /// <see cref="OriginalSnapshotCar"/>. Passed back into the
        /// loader unchanged when a replacement is applied so the
        /// resulting car keeps the waybill that was driving it
        /// somewhere.
        /// </summary>
        public object OriginalSnapshotProperties { get; }

        /// <summary>Snapshot version the game used when handing the
        /// car to the loader; needed for the re-invocation to use the
        /// same migration path.</summary>
        public int OriginalSnapshotVersion { get; }

        /// <summary>True when this fault record has the data needed
        /// to attempt an in-place replacement.</summary>
        public bool CanReplace => OriginalSnapshotCar != null;

        public string DisplayName =>
            string.IsNullOrEmpty(ReportingMark) && string.IsNullOrEmpty(RoadNumber)
                ? CarId
                : (ReportingMark + " " + RoadNumber).Trim();

        public override string ToString()
        {
            return $"car='{DisplayName}' id='{CarId}' missingPrototype='{MissingPrototypeId}' reason='{Reason}'";
        }
    }
}
