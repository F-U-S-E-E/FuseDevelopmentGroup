using FUSE.Infrastructure;
using FUSE.Runtime.Lifecycle;

namespace FUSE.Loading
{
    /// <summary>
    /// Public facade the optional in-game <c>FUSE.LiveBridge</c> mod calls to
    /// hot-reload FUSE packages after an external editor rewrites them on disk.
    /// The correct reload is a disk re-read followed by
    /// <see cref="FuseRuntimeReloadService.ReloadTrackAndData"/> (rebuild caches +
    /// re-apply + cleanup) — bare <see cref="FuseDataPackageDiscovery.ReloadPackagesFromDisk"/>
    /// only re-reads and does NOT update the running world.
    /// </summary>
    public static class FuseLiveReloadApi
    {
        /// <summary>
        /// Re-read every FUSE package from disk and re-apply to the running world.
        /// Multiplayer-guarded (no-ops on a non-host client). Must be called on the
        /// Unity main thread. Returns the number of definitions applied.
        /// </summary>
        public static int ReloadAllFromDisk(string reason)
        {
            var operation = string.IsNullOrWhiteSpace(reason) ? "live-bridge reload" : reason.Trim();
            FuseDataPackageDiscovery.LoadPackagesFromDisk(true);
            return FuseRuntimeReloadService.ReloadTrackAndData(operation);
        }

        /// <summary>Whether FUSE may apply world mutations right now (false on a non-host MP client).</summary>
        public static bool CanApplyWorldMutations(string reason) =>
            FuseMultiplayerGuard.CanApplyWorldMutations(string.IsNullOrWhiteSpace(reason) ? "live-bridge" : reason);

        /// <summary>Human-readable multiplayer compatibility note for the heartbeat/status.</summary>
        public static string DescribeMultiplayer(string reason) =>
            FuseMultiplayerGuard.GetMultiplayerCompatibilityNotice(string.IsNullOrWhiteSpace(reason) ? "live-bridge" : reason);
    }
}
