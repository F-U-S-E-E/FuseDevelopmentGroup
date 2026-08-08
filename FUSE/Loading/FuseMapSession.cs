using System;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;

namespace FUSE.Loading
{
    /// <summary>
    /// Which registered map (if any) the current or pending game session runs
    /// on. Set by the map launcher right before the session launches and
    /// cleared when the main menu comes back up. While a map is active:
    /// MapStore.Load is redirected to the pack's map folder, and packages
    /// declaring a different map are skipped by the apply pipeline. With no
    /// active map, the stock map loads untouched and every map package stays
    /// dormant.
    /// </summary>
    public static class FuseMapSession
    {
        internal const string InactiveSkipReasonPrefix = "map package inactive";

        private static readonly object Sync = new object();
        private static string _activeMapId = string.Empty;

        /// <summary>Empty when the session is (or will be) on the stock map.</summary>
        public static string ActiveMapId
        {
            get
            {
                lock (Sync)
                {
                    return _activeMapId;
                }
            }
        }

        public static bool HasActiveMap => !string.IsNullOrEmpty(ActiveMapId);

        internal static void Activate(string mapId)
        {
            var normalized = (mapId ?? string.Empty).Trim();
            lock (Sync)
            {
                _activeMapId = normalized;
            }

            FuseLog.Info($"FUSE map session activated map='{normalized}'.");
        }

        internal static void Deactivate(string reason)
        {
            string previous;
            lock (Sync)
            {
                previous = _activeMapId;
                _activeMapId = string.Empty;
            }

            if (!string.IsNullOrEmpty(previous))
            {
                FuseLog.Info($"FUSE map session deactivated map='{previous}' reason='{reason ?? "unspecified"}'.");
            }
        }

        internal static bool ShouldApplyDefinition(FuseModDefinition definition)
        {
            return ShouldApply(definition, ActiveMapId);
        }

        /// <summary>
        /// A package with no map declaration always applies. A map package
        /// applies only while its map is the active session map.
        /// </summary>
        internal static bool ShouldApply(FuseModDefinition definition, string activeMapId)
        {
            if (definition?.Map == null)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(activeMapId) &&
                   string.Equals(definition.Id, activeMapId.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        internal static string InactiveSkipReason(string activeMapId)
        {
            return string.IsNullOrWhiteSpace(activeMapId)
                ? $"{InactiveSkipReasonPrefix} (no map selected; stock map session)"
                : $"{InactiveSkipReasonPrefix} (active map='{activeMapId}')";
        }
    }
}
