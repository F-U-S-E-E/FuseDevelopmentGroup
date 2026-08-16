using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Loading;
using Game.State;

namespace FUSE.Patches
{
    internal sealed class FuseNewGameMapOption
    {
        internal const string StockMapName = "Railroader Base Map";
        internal const string StockProgressionName = "East Whittier Start";
        internal const string StockProgressionId = "ewh";
        internal const string SelectionMarkerPrefix = "fuse-map:";
        private const string OriginalSetupSeparator = "|setup:";

        private FuseNewGameMapOption(string mapId, string displayName)
        {
            MapId = mapId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
        }

        internal string MapId { get; }
        internal string DisplayName { get; }
        internal bool IsStock => string.IsNullOrEmpty(MapId);

        internal static IReadOnlyList<FuseNewGameMapOption> Build(IEnumerable<FuseRegisteredMap> maps)
        {
            var options = new List<FuseNewGameMapOption>
            {
                new FuseNewGameMapOption(string.Empty, StockMapName),
            };

            var seenMapIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var map in (maps ?? Enumerable.Empty<FuseRegisteredMap>())
                         .Where(candidate => candidate != null &&
                                             candidate.IsValid &&
                                             !string.IsNullOrWhiteSpace(candidate.MapId))
                         .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(candidate => candidate.MapId, StringComparer.OrdinalIgnoreCase))
            {
                if (!seenMapIds.Add(map.MapId))
                {
                    continue;
                }

                options.Add(new FuseNewGameMapOption(map.MapId, map.DisplayName));
            }

            return options;
        }

        internal static string CreateSelectionMarker(
            string mapId,
            string originalSetupId = null)
        {
            var normalized = (mapId ?? string.Empty).Trim();
            return string.IsNullOrEmpty(normalized)
                ? string.Empty
                : SelectionMarkerPrefix + normalized +
                  OriginalSetupSeparator + (originalSetupId ?? string.Empty);
        }

        internal static bool TryParseSelectionMarker(
            string setupId,
            out string mapId)
        {
            return TryParseSelectionMarker(setupId, out mapId, out _);
        }

        internal static bool TryParseSelectionMarker(
            string setupId,
            out string mapId,
            out string originalSetupId)
        {
            mapId = string.Empty;
            originalSetupId = null;
            if (string.IsNullOrWhiteSpace(setupId) ||
                !setupId.StartsWith(SelectionMarkerPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var payload = setupId.Substring(SelectionMarkerPrefix.Length);
            var separatorIndex = payload.IndexOf(
                OriginalSetupSeparator,
                StringComparison.Ordinal);
            if (separatorIndex >= 0)
            {
                mapId = payload.Substring(0, separatorIndex).Trim();
                originalSetupId = payload.Substring(
                    separatorIndex + OriginalSetupSeparator.Length);
                if (string.IsNullOrEmpty(originalSetupId))
                {
                    originalSetupId = null;
                }
            }
            else
            {
                mapId = payload.Trim();
            }

            return !string.IsNullOrEmpty(mapId);
        }

        internal static NewGameSetup MarkSelection(NewGameSetup setup, string mapId)
        {
            return new NewGameSetup(
                setup.RailroadName,
                setup.ReportingMark,
                setup.Mode,
                setup.ProgressionId,
                CreateSelectionMarker(mapId, setup.SetupId));
        }

        internal static NewGameSetup ClearSelectionMarker(NewGameSetup setup)
        {
            var originalSetupId = setup.SetupId;
            if (TryParseSelectionMarker(
                    setup.SetupId,
                    out _,
                    out var parsedSetupId))
            {
                originalSetupId = parsedSetupId;
            }

            return new NewGameSetup(
                setup.RailroadName,
                setup.ReportingMark,
                setup.Mode,
                setup.ProgressionId,
                originalSetupId);
        }
    }

    internal sealed class FuseNewGameProgressionOption
    {
        internal const string NoProgressionName = "None (map default)";

        private FuseNewGameProgressionOption(
            string displayName,
            string progressionId)
        {
            DisplayName = displayName ?? string.Empty;
            ProgressionId = progressionId;
        }

        internal string DisplayName { get; }
        internal string ProgressionId { get; }

        internal static IReadOnlyList<FuseNewGameProgressionOption> Build(
            FuseRegisteredMap map)
        {
            var progressionIds = map?.ProgressionIds ?? Array.Empty<string>();
            if (progressionIds.Count == 0)
            {
                return new[]
                {
                    new FuseNewGameProgressionOption(NoProgressionName, null),
                };
            }

            return progressionIds
                .Select(id => new FuseNewGameProgressionOption(id, id))
                .ToArray();
        }
    }
}
