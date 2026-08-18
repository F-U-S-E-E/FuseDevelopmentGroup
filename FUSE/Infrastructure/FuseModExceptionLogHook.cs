using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace FUSE.Infrastructure
{
    /// <summary>
    /// The catch-everything net of the mod health monitor: a second subscriber
    /// on <c>Application.logMessageReceivedThreaded</c> that observes every
    /// exception Unity logs (world-move handlers, coroutine faults, Harmony
    /// postfix throws surfacing through MonoBehaviour updates), attributes it
    /// to a mod via <see cref="FuseModAttributionMap"/>, and feeds
    /// <see cref="FuseModExceptionRegistry"/>.
    ///
    /// Deliberately NOT folded into FuseSceneryLoadFailurePatch's existing log
    /// hook: that hook's per-map generation gating and reset semantics are
    /// wrong for a session-cumulative monitor, and a second delegate costs one
    /// extra invocation per log line. This net is disjoint from the registry's
    /// other sources by construction — exceptions FUSE itself contains
    /// (messenger isolation, legacy host) are suppressed before Unity ever
    /// logs them, so they can never be double counted here.
    ///
    /// Threading: the hook thread must never amplify an NRE-per-frame episode.
    /// Steady state (a signature already seen) is allocation-free: one enum
    /// compare, one hash over the condition plus the first stack line, one
    /// locked dictionary hit, one int increment. Only a first-seen signature
    /// allocates and enqueues; attribution parsing, registry writes, and
    /// throttled log lines all happen in <see cref="DrainPending"/> on the
    /// main thread (driven per frame by FuseRuntimePump), which also keeps the
    /// hook thread away from the UMM logger. The drain's own FuseLog lines
    /// surface as LogType.Error at most, so the LogType.Exception filter makes
    /// log recursion impossible.
    ///
    /// Never a popup or toast — the health report and Status page are the only
    /// surfaces for what this observes.
    /// </summary>
    internal static class FuseModExceptionLogHook
    {
        /// <summary>Registry bucket for exceptions no mod token matched.</summary>
        internal const string UnattributedBucket = "<unattributed>";

        // Bounded state: past these caps, occurrences are counted into
        // _overflowDropped instead of growing memory. 128 distinct signatures
        // is far beyond any observed field session (the worst offenders repeat
        // ONE signature thousands of times).
        private const int MaxPendingQueue = 128;
        private const int MaxTrackedSignatures = 128;
        private const int MaxSampleMessageLength = 200;

        // Mirrors FuseMessengerIsolationPatch.MaxRememberedOffenders: a NEW
        // offender mod is always named once even after another mod spent the
        // global first-5 budget, and the set cannot grow without limit.
        private const int MaxLoggedMods = 32;

        private static readonly object Sync = new object();
        private static readonly Dictionary<int, SignatureEntry> Signatures = new Dictionary<int, SignatureEntry>();
        private static readonly List<SignatureEntry> DirtyRepeats = new List<SignatureEntry>();
        private static readonly ConcurrentQueue<PendingException> Pending = new ConcurrentQueue<PendingException>();
        private static int _pendingCount;      // guarded by Sync
        private static long _overflowDropped;  // guarded by Sync

        private static bool _installed;
        private static volatile bool _accepting;

        // Main-thread-only drain state (never touched by the hook thread).
        private static readonly HashSet<string> LoggedMods = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, long> PerModObserved = new Dictionary<string, long>(StringComparer.Ordinal);

        private sealed class SignatureEntry
        {
            // Attribution fields are written once by the main-thread drain
            // (under Sync) and read thereafter; Resolved flips only after all
            // of them are populated.
            public bool Resolved;
            public string ModId;
            public string DisplayName;
            public string ExceptionType;
            public string TopOwnedFrame;
            public string SampleMessage;

            // Occurrences seen by the hook thread since the last drain flush
            // (guarded by Sync). The full session totals live in the registry.
            public int PendingRepeats;
        }

        private struct PendingException
        {
            public string Condition;
            public string StackTrace;
            public SignatureEntry Entry;
        }

        /// <summary>Occurrences dropped past the signature/queue caps (diagnostics).</summary>
        internal static long OverflowDropped
        {
            get
            {
                lock (Sync)
                {
                    return _overflowDropped;
                }
            }
        }

        internal static void Install()
        {
            if (_installed)
            {
                _accepting = true;
                return;
            }

            try
            {
                // The threaded variant sees exceptions logged from every
                // thread; the fast path below is already thread-safe.
                Application.logMessageReceivedThreaded += OnLogMessage;
                _installed = true;
                _accepting = true;
            }
            catch (Exception ex)
            {
                _accepting = false;
                FuseLog.Exception("FUSE mod health log hook could not install", ex);
            }
        }

        internal static void Shutdown()
        {
            _accepting = false;
            if (!_installed)
            {
                return;
            }

            try
            {
                Application.logMessageReceivedThreaded -= OnLogMessage;
                _installed = false;
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE mod health log hook could not uninstall", ex);
            }
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            // Exceptions only: mods legitimately Debug.LogError, and Error-line
            // parsing (scenery load failures) is owned by the scenery hook.
            // This compare is the whole per-line cost for non-exception logs.
            if (type != LogType.Exception)
            {
                return;
            }

            ObserveCore(condition, stackTrace);
        }

        // Runs on whatever thread logged the exception. Split from the Unity
        // callback so tests can drive it without loading UnityEngine's LogType
        // dispatch (same seam shape as ObserveGameLogMessageForTests).
        private static void ObserveCore(string condition, string stackTrace)
        {
            if (!_accepting)
            {
                return;
            }

            // Unity still writes these recoverable compatibility probes to
            // Player.log, but they are not evidence that a locomotive or the
            // session failed. In particular, Bman's shared GP38Scripts runs
            // the same preview/activation lifecycle for every locomotive it
            // supports. Missing preview audio parents and the first smoke
            // callback can throw while the final car continues initializing
            // successfully. Likewise, PlacerWindow asks for stale tender
            // identifiers while rebuilding its library and skips those rows.
            // Do not let a dense, harmless burst cross the health registry's
            // recurring-error threshold and turn the entire Status page red.
            if (IsKnownRecoverableLifecycleNoise(condition, stackTrace))
            {
                return;
            }

            var hash = ComputeSignatureHash(condition, stackTrace);
            SignatureEntry entry;
            lock (Sync)
            {
                if (Signatures.TryGetValue(hash, out entry))
                {
                    // Steady-state path for a repeating signature: count only.
                    if (entry.PendingRepeats == 0)
                    {
                        DirtyRepeats.Add(entry);
                    }

                    entry.PendingRepeats++;
                    return;
                }

                if (Signatures.Count >= MaxTrackedSignatures || _pendingCount >= MaxPendingQueue)
                {
                    _overflowDropped++;
                    return;
                }

                entry = new SignatureEntry();
                Signatures[hash] = entry;
                _pendingCount++;
            }

            // Enqueue outside the lock; the entry was registered above, so a
            // racing repeat lands on the PendingRepeats path and is flushed by
            // the drain once this item resolves.
            Pending.Enqueue(new PendingException
            {
                Condition = condition,
                StackTrace = stackTrace,
                Entry = entry
            });
        }

        /// <summary>
        /// Returns true only for exception signatures whose callers are known
        /// to recover and continue. The original Unity log entry is preserved;
        /// this predicate affects only FUSE's session-health aggregation.
        /// Kept as a pure string classifier so every supported Bman locomotive
        /// receives the same treatment without depending on asset identifiers.
        /// </summary>
        internal static bool IsKnownRecoverableLifecycleNoise(string condition, string stackTrace)
        {
            if (string.IsNullOrEmpty(condition) || string.IsNullOrEmpty(stackTrace))
            {
                return false;
            }

            if (condition.StartsWith("UnknownIdentifierException:", StringComparison.Ordinal) &&
                stackTrace.IndexOf("UI.Placer.PlacerWindow.ConfigureRow", StringComparison.Ordinal) >= 0 &&
                stackTrace.IndexOf("UI.Placer.PlacerWindow.RebuildLibrary", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            if (!condition.StartsWith("NullReferenceException:", StringComparison.Ordinal))
            {
                return false;
            }

            if (stackTrace.IndexOf("GP38Scripts.TractionMotorAudio.OnEnable", StringComparison.Ordinal) >= 0 ||
                stackTrace.IndexOf("GP38Scripts.GP38SmokeController.Start", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            var isExhaustLifecycle =
                stackTrace.IndexOf("Audio.ExhaustAudioController.StopPlaying", StringComparison.Ordinal) >= 0 ||
                stackTrace.IndexOf("Audio.ExhaustAudioController.PlayNext", StringComparison.Ordinal) >= 0;
            return isExhaustLifecycle &&
                stackTrace.IndexOf("Model.Car.HandleModelsLoaded", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Attributes queued first-occurrences, flushes repeat counts into the
        /// registry, and emits throttled log lines. Main thread only; driven
        /// every frame by <see cref="FUSE.Runtime.Lifecycle.FuseRuntimePump"/>.
        /// </summary>
        internal static void DrainPending()
        {
            // Per-frame idle cost: one lock-free queue snapshot plus one plain
            // int read (DirtyRepeats.Count is a heuristic here — the locked
            // paths below re-check under Sync).
            if (Pending.IsEmpty && DirtyRepeats.Count == 0)
            {
                return;
            }

            // First occurrences: attribution parse + registry record. Parse
            // cost is bounded by the signature cap for the whole session.
            while (Pending.TryDequeue(out var item))
            {
                lock (Sync)
                {
                    _pendingCount--;
                }

                try
                {
                    ResolveAndRecordFirst(item);
                }
                catch (Exception ex)
                {
                    FuseModExceptionRegistry.CountSelfFault();
                    FuseLog.Exception("FUSE mod health could not record an observed exception", ex);

                    // Liveness: the entry must still resolve, or its repeats
                    // ride DirtyRepeats forever and the drain never idles.
                    var entry = item.Entry;
                    lock (Sync)
                    {
                        if (!entry.Resolved)
                        {
                            entry.ModId = UnattributedBucket;
                            entry.DisplayName = UnattributedBucket;
                            entry.ExceptionType = entry.ExceptionType ?? "Exception";
                            entry.TopOwnedFrame = entry.TopOwnedFrame ?? string.Empty;
                            entry.SampleMessage = entry.SampleMessage ?? string.Empty;
                            entry.Resolved = true;
                        }
                    }
                }
            }

            List<SignatureEntry> dirty = null;
            lock (Sync)
            {
                if (DirtyRepeats.Count > 0)
                {
                    dirty = new List<SignatureEntry>(DirtyRepeats);
                    DirtyRepeats.Clear();
                }
            }

            if (dirty == null)
            {
                return;
            }

            foreach (var entry in dirty)
            {
                int repeats;
                lock (Sync)
                {
                    if (!entry.Resolved)
                    {
                        // Its first occurrence raced past the dequeue loop
                        // above (registered but not yet enqueued when we
                        // drained); keep the repeats and retry next frame.
                        DirtyRepeats.Add(entry);
                        continue;
                    }

                    repeats = entry.PendingRepeats;
                    entry.PendingRepeats = 0;
                }

                if (repeats <= 0)
                {
                    continue;
                }

                try
                {
                    // The registry coalesces episodes on its own 1s window; a
                    // frame's worth of repeats replayed here lands inside the
                    // window it occurred in (the pump drains every frame).
                    for (var i = 0; i < repeats; i++)
                    {
                        FuseModExceptionRegistry.Record(
                            "LogHook",
                            entry.ModId,
                            entry.DisplayName,
                            entry.ExceptionType,
                            entry.TopOwnedFrame,
                            entry.SampleMessage);
                    }

                    NoteRecorded(entry, repeats);
                }
                catch (Exception ex)
                {
                    FuseModExceptionRegistry.CountSelfFault();
                    FuseLog.Exception("FUSE mod health could not flush repeated exception observations", ex);
                }
            }
        }

        private static void ResolveAndRecordFirst(PendingException item)
        {
            if (!FuseModAttributionMap.TryAttributeStack(
                    item.StackTrace, out var modId, out var displayName, out var topOwnedFrame))
            {
                // Still counted: the health report's unattributed bucket is
                // how a mod outside the token map (or a game-side fault) stays
                // visible instead of silently vanishing.
                modId = UnattributedBucket;
                displayName = UnattributedBucket;
                topOwnedFrame = ExtractFirstFrame(item.StackTrace);
            }

            var entry = item.Entry;
            lock (Sync)
            {
                entry.ModId = modId;
                entry.DisplayName = displayName;
                entry.ExceptionType = ExtractExceptionType(item.Condition);
                entry.TopOwnedFrame = topOwnedFrame;
                entry.SampleMessage = Truncate(item.Condition, MaxSampleMessageLength);
                entry.Resolved = true;
            }

            FuseModExceptionRegistry.Record(
                "LogHook",
                entry.ModId,
                entry.DisplayName,
                entry.ExceptionType,
                entry.TopOwnedFrame,
                entry.SampleMessage);
            NoteRecorded(entry, 1);
        }

        // Per-mod running totals (this hook's view) drive the log throttle.
        // Main thread only.
        private static void NoteRecorded(SignatureEntry entry, int added)
        {
            var modId = entry.ModId ?? UnattributedBucket;
            PerModObserved.TryGetValue(modId, out var before);
            var after = before + added;
            PerModObserved[modId] = after;

            var newMod = LoggedMods.Count < MaxLoggedMods && LoggedMods.Add(modId);

            // FuseGuardLog.ShouldLog generalized to batched increments: the
            // drain adds a whole frame's repeats at once, so the every-100th
            // heartbeat must fire when a batch CROSSES a multiple of 100, not
            // only when it lands exactly on one.
            if (!newMod && after > 5 && after / 100 == before / 100)
            {
                return;
            }

            var frame = string.IsNullOrEmpty(entry.TopOwnedFrame) ? "<no frame>" : entry.TopOwnedFrame;
            if (newMod || after <= 5)
            {
                FuseLog.Warning(
                    $"FUSE mod health observed a logged exception attributed to '{entry.DisplayName}' " +
                    $"({modId}) #{after}: {entry.ExceptionType} at {frame} — \"{entry.SampleMessage}\". " +
                    "Counted for the health report; repeat log lines are throttled.");
            }
            else
            {
                FuseLog.Warning(
                    $"FUSE mod health: {after} logged exception(s) attributed to '{entry.DisplayName}' " +
                    $"({modId}) this session (latest: {entry.ExceptionType} at {frame}).");
            }
        }

        /// <summary>
        /// Allocation-free signature hash: the condition string's own hash
        /// combined with a char walk of the stack trace up to its first
        /// newline (no Substring). Pure; unit-tested.
        /// </summary>
        internal static int ComputeSignatureHash(string condition, string stackTrace)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (condition?.GetHashCode() ?? 0);
                if (!string.IsNullOrEmpty(stackTrace))
                {
                    for (var i = 0; i < stackTrace.Length; i++)
                    {
                        var c = stackTrace[i];
                        if (c == '\n')
                        {
                            break;
                        }

                        hash = hash * 31 + c;
                    }
                }

                return hash;
            }
        }

        /// <summary>
        /// The exception type token from a Unity exception condition line
        /// ("NullReferenceException: message" → "NullReferenceException").
        /// Pure; unit-tested.
        /// </summary>
        internal static string ExtractExceptionType(string condition)
        {
            if (string.IsNullOrEmpty(condition))
            {
                return "<unknown exception>";
            }

            var colon = condition.IndexOf(':');
            var type = colon > 0 ? condition.Substring(0, colon) : condition;
            return Truncate(type.Trim(), 100);
        }

        /// <summary>
        /// First frame of a Unity stack trace, without the argument list /
        /// source suffix ("Ns.Type.Method (args) (at f:1)" → "Ns.Type.Method").
        /// Pure; unit-tested.
        /// </summary>
        internal static string ExtractFirstFrame(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
            {
                return null;
            }

            var end = stackTrace.IndexOf('\n');
            var line = (end >= 0 ? stackTrace.Substring(0, end) : stackTrace).TrimEnd('\r').Trim();
            var parenthesis = line.IndexOf(" (", StringComparison.Ordinal);
            if (parenthesis > 0)
            {
                line = line.Substring(0, parenthesis);
            }

            return line.Length == 0 ? null : line;
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength);
        }

        /// <summary>Drives the threaded observe core without Unity's log dispatch.</summary>
        internal static void ObserveExceptionForTests(string condition, string stackTrace)
        {
            ObserveCore(condition, stackTrace);
        }

        /// <summary>Controls the producer gate without installing Unity's event hook.</summary>
        internal static void SetAcceptanceForTests(bool accept)
        {
            _accepting = accept;
        }

        /// <summary>Queued-but-undrained first occurrences (test hook).</summary>
        internal static int PendingCountForTests => Pending.Count;

        /// <summary>Distinct signatures registered this session (test hook).</summary>
        internal static int TrackedSignatureCountForTests
        {
            get
            {
                lock (Sync)
                {
                    return Signatures.Count;
                }
            }
        }

        /// <summary>Test hook — the hook state is session-cumulative by design.</summary>
        internal static void ResetForTests()
        {
            lock (Sync)
            {
                Signatures.Clear();
                DirtyRepeats.Clear();
                _pendingCount = 0;
                _overflowDropped = 0;
            }

            while (Pending.TryDequeue(out _))
            {
            }

            LoggedMods.Clear();
            PerModObserved.Clear();
            _accepting = false;
        }
    }
}
