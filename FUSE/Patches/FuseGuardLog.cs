namespace FUSE.Patches
{
    /// <summary>
    /// Shared log gate for the runtime guards: first few occurrences
    /// individually (enough to identify the offender), then a heartbeat —
    /// permanently-broken content re-triggers its guard on every cycle it
    /// gets (decal visibility updates, culling events, key-value replays),
    /// and one log line per trigger would itself become the flood the
    /// guards exist to stop.
    /// </summary>
    internal static class FuseGuardLog
    {
        internal static bool ShouldLog(long count)
        {
            return count <= 5 || count % 100 == 0;
        }
    }
}
