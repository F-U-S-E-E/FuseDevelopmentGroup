using FUSE.Authoring.Data.Common;
using UnityEngine;

namespace FUSE.Authoring.Data
{
    /// <summary>
    /// Serializable node snapshot used to restore base-game track removed by a
    /// FUSE package. Preserved fields: id, transform, switch stand flip, thrown
    /// state, diamond-crossing state, and public CTC flags. Lossy fields: turntable links, private CTC
    /// display state, event delegates, and runtime cache state.
    /// </summary>
    public sealed class FuseRemovedNodeSnapshot
    {
        public string Id { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public bool FlipSwitchStand { get; set; }
        public bool IsDiamond { get; set; }
        public bool IsThrown { get; set; }
        public bool IsCtcSwitch { get; set; }
        public bool IsCtcSwitchUnlocked { get; set; }
    }

    /// <summary>
    /// Serializable segment snapshot used to restore base-game track removed by a
    /// FUSE package. Preserved fields: id, endpoints, style/structure flags, class, group,
    /// priority, speed limit, availability flags, endpoint transforms, and
    /// measured length. Lossy fields: private Bezier caches, turntable links,
    /// editor gizmo state, and generated mesh state.
    /// </summary>
    public sealed class FuseRemovedSegmentSnapshot
    {
        public string Id { get; set; }
        public string StartNodeId { get; set; }
        public string EndNodeId { get; set; }
        public string Style { get; set; }
        public int StructureFlags { get; set; }
        public string TrackClass { get; set; }
        public string GroupId { get; set; }
        public int Priority { get; set; }
        public int SpeedLimit { get; set; }
        public bool Available { get; set; }
        public bool GroupEnabled { get; set; }
        public float Length { get; set; }
        public Vector3 StartNodePosition { get; set; }
        public Vector3 StartNodeRotation { get; set; }
        public Vector3 EndNodePosition { get; set; }
        public Vector3 EndNodeRotation { get; set; }
    }

    /// <summary>
    /// Serializable span snapshot used to restore base-game track spans removed
    /// by a FUSE package. Preserved fields: id plus upper/lower segment ids,
    /// ends, and distances. Lossy fields: cached route points, cached segment
    /// lists, collider meshes, and graph references rebuilt by Railroader.
    /// </summary>
    public sealed class FuseRemovedSpanSnapshot
    {
        public string Id { get; set; }
        public FuseTrackLocation Upper { get; set; }
        public FuseTrackLocation Lower { get; set; }
    }
}
