namespace FUSE.Registry
{
    /// <summary>
    /// Kinds of resource a FUSE package can claim ownership of.
    /// Exclusive kinds permit at most one owner per id.
    /// Shared kinds reference-count owners and remain active while at least one remains.
    /// Mergeable kinds track a primary owner but accept secondary claims; the apply
    /// path merges secondary definitions onto the existing primary one (legacy
    /// StrangeCustoms-style mixinto behavior).
    /// </summary>
    public enum FuseClaimKind
    {
        // Exclusive graph/control objects
        Node,
        Segment,
        Span,
        Loader,
        Station,
        Turntable,

        // Shared/cumulative patch objects
        Industry,
        Scenery,

        // Shared/refcounted suppressions
        SuppressedScenePath,
        SuppressedTrackGroup,
        SuppressedArea
    }

    internal static class FuseClaimKindPolicy
    {
        // IsShared and IsMergeable are mutually exclusive at the registry level
        // (TryClaim checks IsShared first). Industry uses the mergeable path
        // because the apply pipeline forces MergeComponents=true on secondary
        // claims; Scenery stays on the shared path until an equivalent apply-side
        // merge mechanism is wired for it.
        public static bool IsShared(FuseClaimKind kind)
        {
            switch (kind)
            {
                case FuseClaimKind.Scenery:
                case FuseClaimKind.SuppressedScenePath:
                case FuseClaimKind.SuppressedTrackGroup:
                case FuseClaimKind.SuppressedArea:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsMergeable(FuseClaimKind kind)
        {
            switch (kind)
            {
                case FuseClaimKind.Industry:
                    return true;
                default:
                    return false;
            }
        }
    }
}
