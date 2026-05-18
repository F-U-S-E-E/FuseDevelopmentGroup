namespace FUSE.Registry
{
    /// <summary>
    /// Kinds of resource a FUSE package can claim ownership of.
    /// Exclusive kinds permit at most one owner per id.
    /// Shared kinds reference-count owners and remain active while at least one
    /// remains; Industry/Scenery sit in this group because legacy mixinto-style
    /// packages routinely layer onto the same id, and FuseModLoader's apply
    /// path forces MergeComponents=true when an industry already exists from a
    /// prior package so the secondary apply doesn't wipe stale components.
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
        public static bool IsShared(FuseClaimKind kind)
        {
            switch (kind)
            {
                case FuseClaimKind.Industry:
                case FuseClaimKind.Scenery:
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
