using System;
using System.Collections.Generic;
using System.Linq;

namespace FUSE.Infrastructure
{
    internal sealed class FuseLiveLogEntry
    {
        public long Sequence { get; set; }
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public string FormatLine() =>
            $"[{Timestamp:HH:mm:ss.fff}] [{Level}] {Message}";
    }

    /// <summary>
    /// A bounded, thread-safe copy of the current session's FUSE log. The disk
    /// writer remains the durable source; this buffer exists so the Tools page
    /// and optional live console never need to repeatedly reread FUSE.log.
    /// </summary>
    internal static class FuseLiveLogBuffer
    {
        internal const int Capacity = 1000;

        private static readonly object Gate = new object();
        private static readonly Queue<FuseLiveLogEntry> Entries =
            new Queue<FuseLiveLogEntry>(Capacity);

        private static long _nextSequence;

        internal static void Append(DateTime timestamp, string level, string message)
        {
            lock (Gate)
            {
                while (Entries.Count >= Capacity)
                {
                    Entries.Dequeue();
                }

                Entries.Enqueue(new FuseLiveLogEntry
                {
                    Sequence = ++_nextSequence,
                    Timestamp = timestamp,
                    Level = NormalizeLevel(level),
                    Message = message ?? string.Empty
                });
            }
        }

        internal static FuseLiveLogEntry[] Snapshot(string levelFilter, string search, int maximum)
        {
            maximum = Math.Max(1, maximum);
            var normalizedLevel = (levelFilter ?? string.Empty).Trim();
            var normalizedSearch = (search ?? string.Empty).Trim();

            lock (Gate)
            {
                return Entries
                    .Where(entry => MatchesLevel(entry.Level, normalizedLevel))
                    .Where(entry => string.IsNullOrEmpty(normalizedSearch) ||
                                    entry.Message.IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    entry.Level.IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Reverse()
                    .Take(maximum)
                    .Reverse()
                    .Select(Clone)
                    .ToArray();
            }
        }

        internal static int Count
        {
            get
            {
                lock (Gate)
                {
                    return Entries.Count;
                }
            }
        }

        private static FuseLiveLogEntry Clone(FuseLiveLogEntry entry) =>
            new FuseLiveLogEntry
            {
                Sequence = entry.Sequence,
                Timestamp = entry.Timestamp,
                Level = entry.Level,
                Message = entry.Message
            };

        private static bool MatchesLevel(string actual, string filter)
        {
            if (string.IsNullOrEmpty(filter) || string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(filter, "Warnings + Errors", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(actual, "WARN", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(actual, "ERROR", StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(filter, "Errors", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(actual, "ERROR", StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(actual, filter, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeLevel(string level)
        {
            var value = (level ?? string.Empty).Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(value) ? "INFO" : value;
        }

        internal static void ResetForTests()
        {
            lock (Gate)
            {
                Entries.Clear();
                _nextSequence = 0;
            }
        }
    }
}
