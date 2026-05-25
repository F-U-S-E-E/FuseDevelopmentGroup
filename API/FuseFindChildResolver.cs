using System.Collections.Generic;

namespace FUSE.API
{
    /// <summary>
    /// Pure disambiguation logic for
    /// <see cref="FusePrefabResolver"/>'s child-by-name lookup. Lives
    /// here so the rule can be unit-tested without spinning up Unity
    /// — the real <see cref="FusePrefabResolver"/> walks the live
    /// <c>Transform</c> tree, classifies each matching child as exact
    /// vs case-insensitive and with-content vs without, then calls
    /// <see cref="SelectWinningIndex"/> for the final pick.
    ///
    /// The rule exists because Unity's built-in <c>Transform.Find</c>
    /// returns the first hierarchy match by name, which is wrong when
    /// the parent has duplicate-named siblings of different shape —
    /// the canonical example is the vanilla <c>World/Large Scenery/Bryson</c>
    /// container, which can end up with both the real Freight House
    /// GameObject (SceneryAssetInstance + renderers in its subtree)
    /// AND an empty placeholder of the same name created by an
    /// upstream mod. Picking the wrong sibling here cascades into
    /// FUSE marking metadata onto an empty wrapper while the real
    /// building keeps its base-game behaviour — exactly the regression
    /// we shipped the day before this resolver was extracted.
    /// </summary>
    internal static class FuseFindChildResolver
    {
        /// <summary>How well a candidate's name matched the requested string.</summary>
        public enum MatchKind
        {
            /// <summary>The candidate's name matched the request character-for-character (case-sensitive ordinal).</summary>
            Exact = 0,

            /// <summary>The candidate's name matched only after a case-insensitive ordinal comparison.</summary>
            CaseInsensitive = 1
        }

        /// <summary>
        /// One classified sibling-match. Tests construct these by hand;
        /// the real <see cref="FusePrefabResolver"/> builds one per
        /// matching <c>Transform</c> child.
        /// </summary>
        public readonly struct Candidate
        {
            public Candidate(MatchKind kind, bool hasContent, int originalIndex)
            {
                Kind = kind;
                HasContent = hasContent;
                OriginalIndex = originalIndex;
            }

            /// <summary>Whether the name match was exact or case-insensitive.</summary>
            public MatchKind Kind { get; }

            /// <summary>
            /// Whether the candidate carries scenery content
            /// (<c>SceneryAssetInstance</c> or any <c>Renderer</c> in
            /// its descendant tree). The real resolver computes this
            /// via <see cref="UnityEngine.GameObject.GetComponentInChildren{T}(bool)"/>;
            /// tests provide it directly.
            /// </summary>
            public bool HasContent { get; }

            /// <summary>
            /// The candidate's original index in the parent's child
            /// list, so the executor can <c>parent.GetChild(i)</c> to
            /// fetch the actual Transform once a winner is picked.
            /// </summary>
            public int OriginalIndex { get; }
        }

        /// <summary>
        /// Picks the best candidate from a list, applying the
        /// four-tier priority that <see cref="FusePrefabResolver.FindChild"/>
        /// needs to honour:
        ///
        /// <list type="number">
        ///   <item><description>Exact-name match WITH content</description></item>
        ///   <item><description>Exact-name match without content (a placeholder we still want over case-insensitive matches)</description></item>
        ///   <item><description>Case-insensitive match WITH content</description></item>
        ///   <item><description>Case-insensitive match without content</description></item>
        /// </list>
        ///
        /// Within each tier, the FIRST candidate in <paramref name="candidates"/>
        /// order wins (which matches Unity's stable child-order
        /// iteration and the prior <c>Transform.Find</c> contract).
        /// </summary>
        /// <returns>The index INTO <paramref name="candidates"/> of the
        /// winner, or <c>null</c> if the input list is empty.</returns>
        public static int? SelectWinningIndex(IReadOnlyList<Candidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            int? exactWithContent = null;
            int? exactFirst = null;
            int? caseInsensitiveWithContent = null;
            int? caseInsensitiveFirst = null;

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                switch (candidate.Kind)
                {
                    case MatchKind.Exact:
                        if (exactFirst == null)
                        {
                            exactFirst = index;
                        }
                        if (exactWithContent == null && candidate.HasContent)
                        {
                            exactWithContent = index;
                        }
                        break;
                    case MatchKind.CaseInsensitive:
                        if (caseInsensitiveFirst == null)
                        {
                            caseInsensitiveFirst = index;
                        }
                        if (caseInsensitiveWithContent == null && candidate.HasContent)
                        {
                            caseInsensitiveWithContent = index;
                        }
                        break;
                }
            }

            return exactWithContent ?? exactFirst ?? caseInsensitiveWithContent ?? caseInsensitiveFirst;
        }
    }
}
