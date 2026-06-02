using System;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Tracks the most recent successful persistence event so the
    /// status bar can show a live "Last saved: 5s ago" indicator.
    /// The persist helpers (ApplyNodePositionEdit, PersistNodeEdit,
    /// etc.) call <see cref="MarkSaved"/> after
    /// <c>FuseAuthoringPersistenceService.SaveDefinitionObject</c>
    /// returns.
    /// </summary>
    /// <remarks>
    /// Static state mirrors the rest of the editor's pattern; tests
    /// reset via <see cref="Reset"/>. The
    /// <see cref="FormatElapsed"/> helper is pure so it's xUnit-tested;
    /// the tracker itself is trivially tested via MarkSaved /
    /// LastSaveAt round-trip.
    /// </remarks>
    internal static class FuseEditorSaveTracker
    {
        /// <summary>
        /// UTC timestamp of the most recent successful save, or
        /// <c>null</c> if nothing has saved this session.
        /// </summary>
        public static DateTime? LastSaveAt { get; private set; }

        public static void MarkSaved()
        {
            LastSaveAt = DateTime.UtcNow;
        }

        public static void Reset()
        {
            LastSaveAt = null;
        }

        /// <summary>
        /// Formats <paramref name="elapsed"/> into a compact "X ago"
        /// label suitable for a status bar. Empty <paramref name="elapsed"/>
        /// (or zero / negative) returns <c>"just now"</c>; clamps to
        /// minute resolution above 60s, hour resolution above 60m, etc.
        /// </summary>
        public static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed <= TimeSpan.Zero || elapsed.TotalSeconds < 1)
            {
                return "just now";
            }

            if (elapsed.TotalSeconds < 60)
            {
                return $"{(int)elapsed.TotalSeconds}s ago";
            }

            if (elapsed.TotalMinutes < 60)
            {
                return $"{(int)elapsed.TotalMinutes}m ago";
            }

            if (elapsed.TotalHours < 24)
            {
                return $"{(int)elapsed.TotalHours}h ago";
            }

            return $"{(int)elapsed.TotalDays}d ago";
        }

        /// <summary>
        /// Convenience that combines <see cref="LastSaveAt"/> with
        /// <see cref="FormatElapsed"/> using <see cref="DateTime.UtcNow"/>
        /// as the reference. Returns <c>"—"</c> when nothing's saved
        /// yet so callers can render it directly.
        /// </summary>
        public static string GetDisplayString()
        {
            if (!LastSaveAt.HasValue)
            {
                return "—";
            }

            return FormatElapsed(DateTime.UtcNow - LastSaveAt.Value);
        }
    }
}
