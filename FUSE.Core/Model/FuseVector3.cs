using System;

namespace Fuse.Core.Model
{
    /// <summary>
    /// Unity-free stand-in for the subset of <c>UnityEngine.Vector3</c> the
    /// ported FUSE authoring model, serializer, validator, and migration use.
    /// Field layout (<see cref="x"/>, <see cref="y"/>, <see cref="z"/>) and the
    /// equality semantics mirror <c>UnityEngine.Vector3</c> so that ported code
    /// such as <c>scale == default ? FuseVector3.one : scale</c> behaves
    /// identically and the JSON wire shape stays byte-for-byte compatible.
    ///
    /// <para>Mirrors Unity's two distinct equality flavours: <see cref="Equals(FuseVector3)"/>
    /// performs exact per-field comparison, while the <c>==</c>/<c>!=</c>
    /// operators perform Unity's approximate comparison (squared distance below
    /// a tiny epsilon). Only the operator form is exercised by the ported code
    /// (always against <c>default</c>), but matching both keeps fidelity exact.</para>
    /// </summary>
    public struct FuseVector3 : IEquatable<FuseVector3>
    {
        // Mirror of UnityEngine.Vector3.kEpsilon used by the == operator.
        private const float Epsilon = 1E-05f;

        public float x;
        public float y;
        public float z;

        public FuseVector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static FuseVector3 zero => new FuseVector3(0f, 0f, 0f);

        public static FuseVector3 one => new FuseVector3(1f, 1f, 1f);

        public static float Distance(FuseVector3 a, FuseVector3 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            float dz = a.z - b.z;
            return (float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        public bool Equals(FuseVector3 other)
        {
            return x == other.x && y == other.y && z == other.z;
        }

        public override bool Equals(object obj)
        {
            return obj is FuseVector3 other && Equals(other);
        }

        public override int GetHashCode()
        {
            // Mirrors UnityEngine.Vector3.GetHashCode bit-mixing so hashing
            // behaviour matches the type being stood in for.
            return x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
        }

        public static bool operator ==(FuseVector3 lhs, FuseVector3 rhs)
        {
            float dx = lhs.x - rhs.x;
            float dy = lhs.y - rhs.y;
            float dz = lhs.z - rhs.z;
            float sqrMagnitude = (dx * dx) + (dy * dy) + (dz * dz);
            return sqrMagnitude < Epsilon * Epsilon;
        }

        public static bool operator !=(FuseVector3 lhs, FuseVector3 rhs)
        {
            return !(lhs == rhs);
        }

        public override string ToString()
        {
            return $"({x}, {y}, {z})";
        }
    }
}
