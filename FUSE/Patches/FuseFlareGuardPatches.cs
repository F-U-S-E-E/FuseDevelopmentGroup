using System;
using FUSE.Infrastructure;
using FUSE.Loading;
using Game;
using HarmonyLib;

namespace FUSE.Patches
{
    /// <summary>
    /// Turns a saved flare standing on since-removed track from silent observer
    /// spam into a counted, surfaced skip.
    ///
    /// Flares persist in the save's key-value store under a
    /// "{segmentId}-{distance}-{a|b}" key. <c>FlareManager.HandleAddUpdateFlare</c>
    /// replays each entry on map load (and on any later value change) by resolving
    /// the stored location against the live track graph and asking for its world
    /// position. When a track mod has removed or replaced the segment the flare was
    /// standing on, the location resolves with a null segment and the position
    /// lookup throws <c>Track.InvalidLocationException</c>. The key-value observer
    /// wrapper already catches that throw, so vanilla behavior is "flare silently
    /// fails to appear + one cryptic 'Exception from observer action of
    /// &lt;flare-key&gt;' per replay" — the user never learns their save carries a
    /// stale flare, and each replay uploads a crash report.
    ///
    /// The finalizer suppresses the throw (same end state: no flare instance) and
    /// records a load-report notice naming the flare key so the health report says
    /// what happened and which segment is gone. The save entry itself is left
    /// untouched: deleting key-value state is host-only and destructive, and the
    /// segment may legitimately come back when the responsible track mod returns.
    /// </summary>
    [HarmonyPatch(typeof(FlareManager), "HandleAddUpdateFlare")]
    internal static class FuseFlareDeadTrackGuardPatch
    {
        private static long _suppressed;

        /// <summary>Stale-flare exceptions suppressed since startup (diagnostics).</summary>
        internal static long SuppressedExceptions => _suppressed;

        private static Exception Finalizer(Exception __exception, string key)
        {
            if (__exception == null)
            {
                return null;
            }

            _suppressed++;
            var flareKey = string.IsNullOrWhiteSpace(key) ? "<unknown>" : key;
            var reason = __exception.GetBaseException().Message;
            if (ShouldLog(_suppressed))
            {
                FuseLog.Warning(
                    $"FUSE suppressed stale-flare exception #{_suppressed} for flare '{flareKey}': {reason}. " +
                    "The flare's saved location references track that no longer exists (usually a track mod " +
                    "removed or replaced the segment it was standing on), so it cannot appear in the world.");
            }

            FuseLoadReport.RecordNotice(
                $"Saved flare '{flareKey}' references track that no longer exists ({reason}) and was skipped. " +
                "It stays in the save and reappears if the removed track segment comes back.");

            return null;
        }

        // First few occurrences individually, then a heartbeat — a stale flare
        // re-triggers on every key-value replay of the same save entry.
        private static bool ShouldLog(long count)
        {
            return count <= 5 || count % 100 == 0;
        }
    }
}
