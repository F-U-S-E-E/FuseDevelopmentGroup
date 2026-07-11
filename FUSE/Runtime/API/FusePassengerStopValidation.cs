using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Infrastructure;
using FUSE.Loading;
using Model.Ops;
using Track;

namespace FUSE.Runtime.API
{
    /// <summary>
    /// Surfaces passenger-stop graph conflicts that the game tolerates silently
    /// but that break passenger service at runtime.
    ///
    /// The game's stop-to-stop machinery assumes each platform belongs to exactly
    /// one stop and every stop is wired into the neighbor graph. Modded content
    /// can violate both: a pack that ships two stops bound to the same track span
    /// makes both stops service the same parked passenger cars — each service
    /// tick flips the cars' last-stop markers against the other stop — and a stop
    /// with an empty neighbor list is unreachable by the game's stop-graph path
    /// search, so every one of those marker flips logs a failed
    /// "Path from A to B not found" search, forever. In the field (Aspen,
    /// 2026-07-07) one leftover duplicate depot produced ~72 such errors per
    /// minute for entire sessions while /fuse.report said "graph 0, conflicts 0".
    ///
    /// Findings are recorded as graph post-bind issues so they surface in the
    /// health report, the /fuse.report console output, and the report JSON with
    /// no new report plumbing. Validation is detection-only by design: which
    /// duplicate stop is the "wrong" one is a content decision the pack author
    /// has to make.
    /// </summary>
    internal static class FusePassengerStopValidation
    {
        private static bool _dirty;

        /// <summary>
        /// Flags that stops changed since the last validation pass; the next
        /// report snapshot revalidates. Called from the FUSE passenger-stop
        /// refresh path (definition apply / graph rebuild).
        /// </summary>
        internal static void MarkDirty()
        {
            _dirty = true;
        }

        /// <summary>
        /// Revalidates only when a stop refresh happened since the last pass.
        /// Called from the load-report snapshot so on-demand report renders stay
        /// current without paying for a scan per render.
        /// </summary>
        internal static void RunIfDirty(string reason)
        {
            if (!_dirty)
            {
                return;
            }

            Run(reason);
        }

        /// <summary>
        /// Scans all active passenger stops and records one graph post-bind issue
        /// per finding. Main-thread only (walks live stop components).
        /// </summary>
        internal static void Run(string reason)
        {
            _dirty = false;
            try
            {
                var issues = Analyze(CollectStopInfos());
                foreach (var issue in issues)
                {
                    FuseLoadReport.RecordGraphPostBindIssue("<world>", "passenger stop", issue.ObjectId, issue.Reason);
                }

                if (issues.Count > 0)
                {
                    FuseLog.Warning(
                        $"FUSE passenger stop validation reason='{reason ?? string.Empty}' " +
                        $"flagged {issues.Count} issue(s); details in /fuse.report (graph post-bind issues).");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE passenger stop validation failed", ex);
                // Stay dirty: this validation exists to stop /fuse.report from
                // silently under-reporting, so a failed scan must retry on the
                // next snapshot instead of skipping until an unrelated refresh.
                _dirty = true;
            }
        }

        private static List<StopInfo> CollectStopInfos()
        {
            var stops = new List<StopInfo>();
            foreach (var stop in PassengerStop.FindAll())
            {
                // Only stops that can actually service cars right now can
                // conflict; progression-hidden or disabled duplicates are the
                // game's intended way of parking alternate content.
                if (stop == null || !stop.isActiveAndEnabled || stop.ProgressionDisabled)
                {
                    continue;
                }

                var spanKeys = new List<string>();
                foreach (var span in stop.TrackSpans ?? Enumerable.Empty<TrackSpan>())
                {
                    if (span == null)
                    {
                        continue;
                    }

                    // Authored span ids are the shared handle two stops can both
                    // bind; unnamed spans fall back to instance identity so they
                    // only ever match when the stops share the same object.
                    spanKeys.Add(string.IsNullOrWhiteSpace(span.id)
                        ? "instance:" + span.GetInstanceID()
                        : span.id);
                }

                var neighborCount = stop.neighbors?.Count(neighbor => neighbor != null) ?? 0;
                stops.Add(new StopInfo(stop.identifier ?? string.Empty, spanKeys, neighborCount));
            }

            return stops;
        }

        /// <summary>
        /// Pure analysis over collected stop shapes (unit-testable without
        /// Unity). Coordinates one helper per rule; new rules slot in as new
        /// Find* helpers. Issue order (duplicate ids, shared spans, isolated
        /// stops — each alphabetical within its rule) is part of the tested
        /// contract: the load report dedupes recorded lines by exact text and
        /// tests assert deterministic output.
        /// </summary>
        internal static List<Issue> Analyze(IReadOnlyList<StopInfo> stops)
        {
            var issues = new List<Issue>();
            if (stops == null || stops.Count == 0)
            {
                return issues;
            }

            issues.AddRange(FindDuplicateIdentifierIssues(stops));
            issues.AddRange(FindSharedSpanIssues(stops));
            issues.AddRange(FindIsolatedStopIssues(stops));
            return issues;
        }

        // Duplicate identifiers: the stop registry, save state, and pathing
        // all key on the identifier, so two live instances sharing one id
        // shadow each other unpredictably.
        private static IEnumerable<Issue> FindDuplicateIdentifierIssues(IReadOnlyList<StopInfo> stops)
        {
            foreach (var group in stops
                         .Where(stop => !string.IsNullOrWhiteSpace(stop.Id))
                         .GroupBy(stop => stop.Id, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1)
                         .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                yield return new Issue(
                    group.Key,
                    $"{group.Count()} live passenger stop instances share identifier '{group.Key}' — " +
                    "stop registry lookups, save state, and passenger pathing assume identifiers are unique");
            }
        }

        // Shared spans: every span key bound by two or more distinct stop
        // INSTANCES. Ownership is tracked by stop index, not id: two stops
        // sharing an identifier (or both missing one) are exactly the
        // duplicate-content case this check exists to expose, and an
        // id-based dedupe would collapse them into a single owner and hide
        // the conflict. Each stop contributes each span key at most once
        // (per-stop Distinct below), so the owner lists stay instance-unique.
        private static IEnumerable<Issue> FindSharedSpanIssues(IReadOnlyList<StopInfo> stops)
        {
            var stopsBySpan = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < stops.Count; index++)
            {
                foreach (var spanKey in stops[index].SpanKeys.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!stopsBySpan.TryGetValue(spanKey, out var ownerIndexes))
                    {
                        ownerIndexes = new List<int>();
                        stopsBySpan[spanKey] = ownerIndexes;
                    }

                    ownerIndexes.Add(index);
                }
            }

            foreach (var entry in stopsBySpan.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (entry.Value.Count < 2)
                {
                    continue;
                }

                var owners = entry.Value
                    .Select(index => string.IsNullOrWhiteSpace(stops[index].Id) ? "<unnamed>" : stops[index].Id)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                yield return new Issue(
                    string.Join("+", owners),
                    $"passenger stops {FormatIdList(owners)} all bind track span '{entry.Key}' — " +
                    "overlapping stops service the same parked passenger cars and flip their last-stop " +
                    "markers against each other every service tick; when the stops are not neighbors this " +
                    "also logs a failed path search per flip");
            }
        }

        // Isolated stops: with no neighbors the game's stop-graph search can
        // never reach the stop, so passenger transfers involving it fail.
        // Meaningless on a world with a single stop.
        private static IEnumerable<Issue> FindIsolatedStopIssues(IReadOnlyList<StopInfo> stops)
        {
            if (stops.Count <= 1)
            {
                yield break;
            }

            foreach (var stop in stops
                         .Where(candidate => candidate.NeighborCount == 0 && !string.IsNullOrWhiteSpace(candidate.Id))
                         .OrderBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase))
            {
                yield return new Issue(
                    stop.Id,
                    $"passenger stop '{stop.Id}' declares no neighbors — the game's stop-graph search cannot " +
                    "reach it, so passenger transfers involving it fail (each attempt logs " +
                    "'Path from … not found - PassengerStopEdgeMoved will not be fired')");
            }
        }

        private static string FormatIdList(IReadOnlyList<string> ids)
        {
            return string.Join(", ", ids.Select(id => $"'{id}'"));
        }

        internal readonly struct StopInfo
        {
            internal StopInfo(string id, IReadOnlyList<string> spanKeys, int neighborCount)
            {
                Id = id ?? string.Empty;
                SpanKeys = spanKeys ?? Array.Empty<string>();
                NeighborCount = neighborCount;
            }

            internal string Id { get; }
            internal IReadOnlyList<string> SpanKeys { get; }
            internal int NeighborCount { get; }
        }

        internal readonly struct Issue
        {
            internal Issue(string objectId, string reason)
            {
                ObjectId = objectId ?? string.Empty;
                Reason = reason ?? string.Empty;
            }

            internal string ObjectId { get; }
            internal string Reason { get; }
        }
    }
}
