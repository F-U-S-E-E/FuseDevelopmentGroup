namespace RAIL.Registry
{
    /// <summary>
    /// Kinds of resource a RAIL package can claim ownership of.
    /// Exclusive kinds permit at most one owner per id.
    /// Shared kinds reference-count owners and remain active while at least one remains.
    /// </summary>
    public enum RailClaimKind
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

    internal static class RailClaimKindPolicy
    {
        public static bool IsShared(RailClaimKind kind)
        {
            switch (kind)
            {
                case RailClaimKind.SuppressedScenePath:
                case RailClaimKind.SuppressedTrackGroup:
                case RailClaimKind.SuppressedArea:
                    return true;
                default:
                    return false;
            }
        }
    }
}
