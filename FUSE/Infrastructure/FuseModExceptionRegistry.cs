using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace FUSE.Infrastructure
{
    /// <summary>
    /// Report-facing snapshot of one mod's observed exceptions this session.
    /// Materialized copies only — the live registry state never escapes the
    /// registry lock.
    /// </summary>
    internal sealed class FuseModExceptionSnapshot
    {
        public string ModId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public long Count { get; set; }
        public long Episodes { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public FuseModExceptionSignatureSnapshot[] Signatures { get; set; } =
            Array.Empty<FuseModExceptionSignatureSnapshot>();
    }

    /// <summary>One distinct exception signature within a mod's snapshot.</summary>
    internal sealed class FuseModExceptionSignatureSnapshot
    {
        public string ExceptionType { get; set; } = string.Empty;
        public string TopOwnedFrame { get; set; } = string.Empty;
        public string SampleMessage { get; set; } = string.Empty;
        public long Count { get; set; }
        public long Episodes { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    /// <summary>
    /// Session-cumulative registry of third-party mod exceptions observed by
    /// the legacy-mod health monitor. Three producers write here — the game
    /// log hook ("LogHook"), the messenger listener isolation ("Messenger"),
    /// and the legacy assembly host ("LegacyHost") — and the load report /
    /// Status page read here. Like <see cref="FuseRuntimeGuardCounters"/> it
    /// lives in Infrastructure so patches write and Loading/UI read without
    /// compile-time coupling, and it is never reset on map load: first/last
    /// seen timestamps plus episode counts give per-session visibility
    /// without the per-map generation gating the scenery patch needs.
    ///
    /// Everything runs under one lock because the log hook records from
    /// Unity's logging thread while the containment sites record from the
    /// main thread. The hot path (a repeat of an already-known signature) is
    /// one lock, two dictionary lookups, and a handful of field writes — no
    /// allocation beyond the value-tuple key hash — so an exception-per-frame
    /// episode costs microseconds and cannot amplify itself.
    ///
    /// Bounds: at most 32 named mods (later mods coalesce into the
    /// "&lt;other&gt;" bucket), at most 8 distinct signatures per mod (later
    /// signatures bump per-mod totals only). Occurrences of one signature
    /// within a 1-second window coalesce into a single "episode", so a dense
    /// per-frame burst counts as one episode — episodes approximate
    /// trigger events (e.g. world moves), not frames.
    /// </summary>
    internal static class FuseModExceptionRegistry
    {
        /// <summary>Bucket id for exceptions no mod could be attributed to.</summary>
        internal const string UnattributedModId = "<unattributed>";

        /// <summary>Bucket id for mods beyond the tracked-mod cap.</summary>
        internal const string OverflowModId = "<other>";

        private const string UnattributedDisplayName = "(unattributed)";
        private const string OverflowDisplayName = "(other mods)";

        private const int MaxTrackedMods = 32;
        private const int MaxSignaturesPerMod = 8;
        private const long EpisodeCoalesceWindowMs = 1000;
        private const int SampleMessageMaxLength = 200;

        private static readonly object Gate = new object();
        private static readonly Stopwatch SessionClock = Stopwatch.StartNew();
        private static readonly Dictionary<string, ModRecord> Mods =
            new Dictionary<string, ModRecord>(StringComparer.OrdinalIgnoreCase);

        private static long _totalObserved;
        private static long _totalUnattributed;
        private static long _signatureOverflowDropped;
        private static int _namedModCount;

        /// <summary>
        /// Monotonic millisecond clock used for episode coalescing. Injectable
        /// so tests can drive the window deterministically; the default is a
        /// session stopwatch (wall-clock adjustments must not split or merge
        /// episodes).
        /// </summary>
        internal static Func<long> TickSource = () => SessionClock.ElapsedMilliseconds;

        /// <summary>UTC timestamp source for first/last-seen; injectable for tests.</summary>
        internal static Func<DateTime> UtcNowSource = () => DateTime.UtcNow;

        private sealed class ModRecord
        {
            public string ModId;
            public string DisplayName;
            public long Total;
            public DateTime FirstSeenUtc;
            public DateTime LastSeenUtc;

            public readonly Dictionary<(string source, string exceptionType, string frame), SignatureRecord> Signatures =
                new Dictionary<(string, string, string), SignatureRecord>();

            // Signatures past the per-mod cap bump these instead of creating
            // new records; the episode window still applies so overflow spam
            // cannot inflate the mod's episode count per-frame.
            public long OverflowCount;
            public long OverflowEpisodes;
            public long OverflowLastTick;
            public bool HasOverflow;
        }

        private sealed class SignatureRecord
        {
            public string Source;
            public string ExceptionType;
            public string TopOwnedFrame;
            public string SampleMessage;
            public long Count;
            public long Episodes;
            public long LastTick;
            public DateTime FirstSeenUtc;
            public DateTime LastSeenUtc;
        }

        /// <summary>Total exceptions observed this session, attributed or not.</summary>
        internal static long GrandTotal
        {
            get { lock (Gate) { return _totalObserved; } }
        }

        /// <summary>True when nothing has been observed — the healthy state.</summary>
        internal static bool AllIdle => GrandTotal == 0;

        /// <summary>Observed exceptions no mod token/assembly could be attributed to.</summary>
        internal static long TotalUnattributed
        {
            get { lock (Gate) { return _totalUnattributed; } }
        }

        /// <summary>Signature-cap overflow occurrences (diagnostics/tests).</summary>
        internal static long SignatureOverflowDropped
        {
            get { lock (Gate) { return _signatureOverflowDropped; } }
        }

        /// <summary>
        /// Record one observed exception occurrence. A null/empty
        /// <paramref name="modId"/> (or the sentinel itself) lands in the
        /// unattributed bucket. Signature dedupe, episode coalescing, and all
        /// caps are handled here; callers just describe what they saw.
        /// Safe from any thread; never throws past its own boundary.
        /// </summary>
        internal static void Record(
            string source,
            string modId,
            string displayName,
            string exceptionType,
            string topOwnedFrame,
            string sampleMessage)
        {
            try
            {
                lock (Gate)
                {
                    RecordLocked(source, modId, displayName, exceptionType, topOwnedFrame, sampleMessage);
                }
            }
            catch
            {
                // The monitor must never become the fault it exists to count;
                // several callers sit inside the log pipeline being observed,
                // where logging from here could recurse.
                CountSelfFault();
            }
        }

        /// <summary>
        /// Record an exception FUSE contained on behalf of a listener,
        /// attributing by the recipient's type → assembly (exact — no stack
        /// parsing). Used by the messenger listener isolation.
        /// </summary>
        internal static void RecordContained(Exception ex, Type recipientType, string context)
        {
            if (ex == null)
            {
                return;
            }

            string modId = null;
            string displayName = null;
            try
            {
                FuseModAttributionMap.TryAttributeType(recipientType, out modId, out displayName);
            }
            catch
            {
                // Attribution failure degrades to the unattributed bucket.
                CountSelfFault();
            }

            var frame = SafeTypeName(recipientType);
            if (!string.IsNullOrEmpty(context))
            {
                frame = frame + " [" + context + "]";
            }

            Record("Messenger", modId, displayName, SafeExceptionTypeName(ex), frame, SafeMessage(ex));
        }

        /// <summary>
        /// Record an exception FUSE contained for a legacy hosted plugin whose
        /// identity is already known (the manifest id).
        /// </summary>
        internal static void RecordContained(Exception ex, string modId, string context)
        {
            if (ex == null)
            {
                return;
            }

            Record("LegacyHost", modId, modId, SafeExceptionTypeName(ex), context ?? string.Empty, SafeMessage(ex));
        }

        // Faults swallowed inside the monitor's own fail-open catches (never
        // logged from those sites — several run inside the log pipeline the
        // monitor observes, where logging could recurse). Non-zero means the
        // monitor itself misbehaved; surfaced in FormatSummary only then.
        private static long _selfFaults;

        internal static long SelfFaults => System.Threading.Interlocked.Read(ref _selfFaults);

        internal static void CountSelfFault() => System.Threading.Interlocked.Increment(ref _selfFaults);

        /// <summary>
        /// One-line breakdown for the load report summary, in the guard-counter
        /// style. mods counts attributed mods only (the unattributed and
        /// overflow buckets are excluded — overflow means the count is a floor).
        /// </summary>
        internal static string FormatSummary()
        {
            var selfFaults = SelfFaults;
            lock (Gate)
            {
                return ComposeSummaryLocked(selfFaults);
            }
        }

        private static string ComposeSummaryLocked(long selfFaults)
        {
            var summary = $"modErrors={_totalObserved} unattributed={_totalUnattributed} mods={_namedModCount}";
            return selfFaults == 0 ? summary : summary + $" selfFaults={selfFaults}";
        }

        /// <summary>
        /// Everything a report/UI render needs, captured under one lock so the
        /// summary line, totals, and per-mod rows all describe the same
        /// instant even while the log hook records on other threads.
        /// </summary>
        internal static FuseModExceptionReportState CaptureReportState()
        {
            var selfFaults = SelfFaults;
            lock (Gate)
            {
                var mods = Mods.Values
                    .OrderByDescending(record => record.Total)
                    .ThenBy(record => record.ModId, StringComparer.OrdinalIgnoreCase)
                    .Select(SnapshotRecordLocked)
                    .ToArray();
                return new FuseModExceptionReportState(
                    mods, _totalObserved, _totalUnattributed, ComposeSummaryLocked(selfFaults));
            }
        }

        /// <summary>
        /// Materialized copy of every tracked bucket, worst first. Sentinel
        /// buckets sort by their counts like any other row. Prefer
        /// <see cref="CaptureReportState"/> when totals are rendered beside the
        /// rows — this overload cannot guarantee they agree.
        /// </summary>
        internal static FuseModExceptionSnapshot[] SnapshotForReport() => CaptureReportState().Mods;

        /// <summary>Test hook — the registry is session-cumulative by design.</summary>
        internal static void ResetForTests()
        {
            lock (Gate)
            {
                Mods.Clear();
                _totalObserved = 0;
                _totalUnattributed = 0;
                _signatureOverflowDropped = 0;
                _namedModCount = 0;
                System.Threading.Interlocked.Exchange(ref _selfFaults, 0);
                TickSource = () => SessionClock.ElapsedMilliseconds;
                UtcNowSource = () => DateTime.UtcNow;
            }
        }

        private static void RecordLocked(
            string source,
            string modId,
            string displayName,
            string exceptionType,
            string topOwnedFrame,
            string sampleMessage)
        {
            _totalObserved++;

            var attributed = !string.IsNullOrWhiteSpace(modId) &&
                !string.Equals(modId, UnattributedModId, StringComparison.OrdinalIgnoreCase);
            if (!attributed)
            {
                _totalUnattributed++;
                modId = UnattributedModId;
                displayName = UnattributedDisplayName;
            }

            var nowUtc = SafeUtcNow();
            var record = GetOrCreateRecordLocked(modId, displayName, nowUtc);
            record.Total++;
            record.LastSeenUtc = nowUtc;

            var nowTick = SafeTick();
            var key = (source ?? string.Empty, exceptionType ?? string.Empty, topOwnedFrame ?? string.Empty);
            if (record.Signatures.TryGetValue(key, out var signature))
            {
                signature.Count++;
                if (nowTick - signature.LastTick > EpisodeCoalesceWindowMs)
                {
                    signature.Episodes++;
                }

                signature.LastTick = nowTick;
                signature.LastSeenUtc = nowUtc;
                return;
            }

            if (record.Signatures.Count >= MaxSignaturesPerMod)
            {
                _signatureOverflowDropped++;
                record.OverflowCount++;
                if (!record.HasOverflow || nowTick - record.OverflowLastTick > EpisodeCoalesceWindowMs)
                {
                    record.OverflowEpisodes++;
                }

                record.HasOverflow = true;
                record.OverflowLastTick = nowTick;
                return;
            }

            record.Signatures.Add(key, new SignatureRecord
            {
                Source = source ?? string.Empty,
                ExceptionType = exceptionType ?? string.Empty,
                TopOwnedFrame = topOwnedFrame ?? string.Empty,
                SampleMessage = Truncate(sampleMessage, SampleMessageMaxLength),
                Count = 1,
                Episodes = 1,
                LastTick = nowTick,
                FirstSeenUtc = nowUtc,
                LastSeenUtc = nowUtc
            });
        }

        private static ModRecord GetOrCreateRecordLocked(string modId, string displayName, DateTime nowUtc)
        {
            if (Mods.TryGetValue(modId, out var existing))
            {
                if (string.IsNullOrWhiteSpace(existing.DisplayName) && !string.IsNullOrWhiteSpace(displayName))
                {
                    existing.DisplayName = displayName;
                }

                return existing;
            }

            var isSentinel = string.Equals(modId, UnattributedModId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(modId, OverflowModId, StringComparison.OrdinalIgnoreCase);
            if (!isSentinel && _namedModCount >= MaxTrackedMods)
            {
                // Tracked-mod cap: fold this mod (and any further new mods)
                // into the shared overflow bucket instead of growing unbounded
                // when many mods misbehave at once.
                if (!Mods.TryGetValue(OverflowModId, out var overflow))
                {
                    overflow = new ModRecord
                    {
                        ModId = OverflowModId,
                        DisplayName = OverflowDisplayName,
                        FirstSeenUtc = nowUtc,
                        LastSeenUtc = nowUtc
                    };
                    Mods.Add(OverflowModId, overflow);
                }

                return overflow;
            }

            var created = new ModRecord
            {
                ModId = modId,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? modId : displayName,
                FirstSeenUtc = nowUtc,
                LastSeenUtc = nowUtc
            };
            Mods.Add(modId, created);
            if (!isSentinel)
            {
                _namedModCount++;
            }

            return created;
        }

        private static FuseModExceptionSnapshot SnapshotRecordLocked(ModRecord record)
        {
            var signatures = record.Signatures.Values
                .OrderByDescending(signature => signature.Count)
                .ThenBy(signature => signature.ExceptionType, StringComparer.Ordinal)
                .Select(signature => new FuseModExceptionSignatureSnapshot
                {
                    ExceptionType = signature.ExceptionType,
                    TopOwnedFrame = signature.TopOwnedFrame,
                    SampleMessage = signature.SampleMessage,
                    Count = signature.Count,
                    Episodes = signature.Episodes,
                    Source = signature.Source
                })
                .ToArray();

            var episodes = record.OverflowEpisodes;
            foreach (var signature in record.Signatures.Values)
            {
                episodes += signature.Episodes;
            }

            return new FuseModExceptionSnapshot
            {
                ModId = record.ModId,
                DisplayName = record.DisplayName,
                Count = record.Total,
                Episodes = episodes,
                FirstSeenUtc = record.FirstSeenUtc,
                LastSeenUtc = record.LastSeenUtc,
                Signatures = signatures
            };
        }

        private static long SafeTick()
        {
            try
            {
                var source = TickSource;
                return source != null ? source() : SessionClock.ElapsedMilliseconds;
            }
            catch
            {
                return SessionClock.ElapsedMilliseconds;
            }
        }

        private static DateTime SafeUtcNow()
        {
            try
            {
                var source = UtcNowSource;
                return source != null ? source() : DateTime.UtcNow;
            }
            catch
            {
                return DateTime.UtcNow;
            }
        }

        private static string SafeExceptionTypeName(Exception ex)
        {
            try
            {
                return ex.GetType().Name;
            }
            catch
            {
                return "Exception";
            }
        }

        private static string SafeTypeName(Type type)
        {
            try
            {
                return type != null ? (type.FullName ?? type.Name) : "(unknown recipient)";
            }
            catch
            {
                return "(unknown recipient)";
            }
        }

        private static string SafeMessage(Exception ex)
        {
            try
            {
                return ex.Message ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }

    /// <summary>
    /// One coherent registry observation: rows, totals, and the summary line
    /// captured under the same lock so no consumer can render totals that
    /// disagree with the rows beside them.
    /// </summary>
    internal sealed class FuseModExceptionReportState
    {
        public FuseModExceptionReportState(
            FuseModExceptionSnapshot[] mods, long total, long unattributed, string summaryLine)
        {
            Mods = mods ?? Array.Empty<FuseModExceptionSnapshot>();
            Total = total;
            Unattributed = unattributed;
            SummaryLine = summaryLine ?? string.Empty;
        }

        public FuseModExceptionSnapshot[] Mods { get; }
        public long Total { get; }
        public long Unattributed { get; }
        public string SummaryLine { get; }
    }
}
