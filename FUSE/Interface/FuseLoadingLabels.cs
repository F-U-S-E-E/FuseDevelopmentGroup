using System;
using System.Globalization;

namespace FUSE.Interface
{
    // Maps the game's loading-progress flavor strings to FUSE's friendly
    // "current step" labels for the enhanced loading screen (issue #83). Pure and
    // Unity-free so the mapping table is unit-testable.
    //
    // The game emits only a handful of distinct flavor strings during a load (the
    // text passed to PersistentLoader.ShowProgress): one constant string for the
    // whole async scene-load phase and another at the ~95% hand-off to the
    // synchronous save restore, plus an unload string. We match on a stable
    // substring rather than the exact text so trailing punctuation changes don't
    // matter, and an unknown/renamed string degrades to a generic-but-honest label
    // (raw text as detail) instead of breaking — if the game renames these, FUSE
    // still drives step labels from the snapshot/pipeline patches.
    internal static class FuseLoadingLabels
    {
        internal readonly struct Step
        {
            internal Step(string title, string detail, bool syncHandoff = false)
            {
                Title = title;
                Detail = detail;
                SyncHandoff = syncHandoff;
            }

            internal string Title { get; }

            internal string Detail { get; }

            // True for the game's ~95% hand-off into the synchronous save restore.
            // The controller marks the sync phase from here so the determinate bar
            // switches to its static band going into the freeze instead of sitting
            // as a partial fill that reads as "stuck at 95%".
            internal bool SyncHandoff { get; }
        }

        internal static Step MapProgressFlavor(string gameText, float fraction)
        {
            var text = gameText == null ? string.Empty : gameText.Trim();

            // ~95% hand-off marker, emitted immediately before the synchronous
            // ApplyGameSetup save restore.
            if (Contains(text, "Half a car"))
            {
                return new Step("Restoring saved game", "Reading world snapshot", syncHandoff: true);
            }

            // Return-to-menu unload. FUSE does not own the unload screen (it aborts
            // on MapWillUnloadEvent), but the mapping is kept honest for completeness.
            if (Contains(text, "Tyin'") || Contains(text, "Tying"))
            {
                return new Step("Returning to main menu", null);
            }

            // The constant string across the whole async scene-load phase. Split by
            // progress so the early menu/UI hand-off reads differently from the long
            // terrain/environment stream.
            if (Contains(text, "Two cars"))
            {
                return fraction < 0.2f
                    ? new Step("Loading world", "Preparing interface")
                    : new Step("Loading terrain", "Streaming map & environment");
            }

            // Unknown / renamed flavor string: stay honest and resilient.
            return new Step("Loading world", string.IsNullOrEmpty(text) ? null : text);
        }

        // Formats a snapshot-element count into a step detail line, e.g.
        // (1432, "car", "cars") -> "1,432 cars". A negative count (the sentinel for
        // "couldn't read the collection") yields null so the caller shows the title
        // alone rather than a wrong number.
        internal static string DescribeCount(int count, string singular, string plural)
        {
            if (count < 0)
            {
                return null;
            }

            var noun = count == 1 ? singular : plural;
            return count.ToString("N0", CultureInfo.InvariantCulture) + " " + noun;
        }

        private static bool Contains(string haystack, string needle)
        {
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
