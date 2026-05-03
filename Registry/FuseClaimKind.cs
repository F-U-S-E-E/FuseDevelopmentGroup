namespace FUSE.Registry
{
    /// <summary>
    /// Kinds of resource a FUSE package can claim ownership of.
    /// Exclusive kinds permit at most one owner per id.
    /// Shared kinds reference-count owners and remain active while at least one remains.
    /// </summary>
    public enum FuseClaimKind
    {
        // Exclusive
        Node,
        Segment,
        Span,
        Industry,
        Loader,
        Station,
        Scenery,
        Turntable,

        // Shared (refcounted)
        SuppressedScenePath,
        SuppressedTrackGroup,
        SuppressedArea
    }

    internal static class FuseClaimKindPolicy
    {
        public static bool IsShared(FuseClaimKind kind)
        {
            switch (kind)
            {
                case FuseClaimKind.SuppressedScenePath:
                case FuseClaimKind.SuppressedTrackGroup:
                case FuseClaimKind.SuppressedArea:
                    return true;
                default:
                    return false;
            }
        }
    }
}
