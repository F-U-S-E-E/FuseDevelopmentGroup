using Newtonsoft.Json;
using UnityEngine;

namespace FUSE.Editor.Bookmarks
{
    /// <summary>
    /// One saved camera position the user can teleport to. Modelled
    /// after Axiom's <c>View</c> type: name + position + optional
    /// rotation. Pinning flags (<c>pinLocation</c>, <c>pinLevel</c>)
    /// from Axiom are deferred — Railroader has a single world, so
    /// the level-pin distinction doesn't apply, and live-tracking the
    /// current camera into a "Main" bookmark is something we can layer
    /// on after the basics ship.
    /// </summary>
    /// <remarks>
    /// Position is captured in world space using
    /// <see cref="Camera.main"/>'s transform; the camera-restore path
    /// passes <c>Position</c> straight into <c>CameraSelector.shared.ZoomToPoint</c>.
    /// Rotation is captured as a <see cref="Quaternion"/> serialised
    /// component-wise; Railroader's <c>ZoomToPoint</c> only restores
    /// position today, so rotation is round-tripped but not yet applied.
    /// </remarks>
    internal sealed class FuseEditorBookmark
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("position")]
        public SerializableVector3 Position { get; set; }

        [JsonProperty("rotation")]
        public SerializableQuaternion Rotation { get; set; }

        // [JsonIgnore] on both accessors: Newtonsoft otherwise tries to
        // serialise the Unity Vector3 / Quaternion structs whole, which
        // triggers a self-referencing loop on Vector3.normalized (it's a
        // computed property that returns a Vector3, recursing forever).
        // Serialised state lives only in Position + Rotation above.
        [JsonIgnore] public Vector3 PositionVector => Position;
        [JsonIgnore] public Quaternion RotationQuaternion => Rotation;

        public static FuseEditorBookmark FromCamera(string name, Camera camera)
        {
            if (camera == null)
            {
                return null;
            }

            return new FuseEditorBookmark
            {
                Name = name,
                Position = camera.transform.position,
                Rotation = camera.transform.rotation,
            };
        }
    }

    /// <summary>
    /// JSON-friendly Vector3. Newtonsoft can serialize Unity's
    /// <see cref="Vector3"/> directly but the resulting blob is a
    /// noisy <c>normalized</c>/<c>magnitude</c>/etc dump; this struct
    /// is three floats and nothing else.
    /// </summary>
    internal struct SerializableVector3
    {
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;
        [JsonProperty("z")] public float Z;

        public static implicit operator Vector3(SerializableVector3 v) => new Vector3(v.X, v.Y, v.Z);
        public static implicit operator SerializableVector3(Vector3 v) => new SerializableVector3 { X = v.x, Y = v.y, Z = v.z };
    }

    /// <summary>Same idea as <see cref="SerializableVector3"/> for quaternions.</summary>
    internal struct SerializableQuaternion
    {
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;
        [JsonProperty("z")] public float Z;
        [JsonProperty("w")] public float W;

        public static implicit operator Quaternion(SerializableQuaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);
        public static implicit operator SerializableQuaternion(Quaternion q) => new SerializableQuaternion { X = q.x, Y = q.y, Z = q.z, W = q.w };
    }
}
