using System.Collections.Generic;

namespace Fuse.Core.Authoring
{
    /// <summary>
    /// Shared <c>prefix_NNNN</c> id minting for the authoring ops helpers
    /// (<see cref="TrackOps"/>, <see cref="WorldOps"/>, <see cref="OperationsOps"/>).
    /// Fills the first free slot (gaps included) and widens past 9999 once the D4
    /// range is exhausted. The cursor overload lets a bulk caller mint many ids in
    /// one pass without rebuilding the taken-set and rescanning from 1 per id.
    /// </summary>
    internal static class AuthoringIds
    {
        /// <summary>
        /// Single-shot: the first free <c>prefix_NNNN</c> slot not already present in
        /// <paramref name="existing"/>. Builds a one-off set, so repeated calls are O(n) each.
        /// </summary>
        internal static string UniqueId(IEnumerable<string> existing, string prefix)
        {
            var set = new HashSet<string>(existing);
            var i = 1;
            return UniqueId(set, prefix, ref i);
        }

        // Ids are only added while a batch runs, so the first free slot never moves backwards
        // and the cursor can resume where the previous call stopped instead of rescanning from 1.
        internal static string UniqueId(ISet<string> takenIds, string prefix, ref int nextIndex)
        {
            if (nextIndex < 1)
            {
                nextIndex = 1;
            }

            var id = $"{prefix}_{nextIndex:D4}";
            while (!takenIds.Add(id))
            {
                nextIndex++;
                id = $"{prefix}_{nextIndex:D4}";
            }

            // Leave the cursor past the minted slot so the next call doesn't re-test it.
            nextIndex++;
            return id;
        }
    }
}
