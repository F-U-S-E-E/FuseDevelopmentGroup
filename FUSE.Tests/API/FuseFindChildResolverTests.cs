using System.Collections.Generic;
using FUSE.API;
using Xunit;
using static FUSE.API.FuseFindChildResolver;

namespace FUSE.Tests.API
{
    /// <summary>
    /// Pins the four-tier priority that
    /// <see cref="FuseFindChildResolver.SelectWinningIndex"/> applies
    /// when <see cref="FUSE.API.FusePrefabResolver"/> needs to pick
    /// among siblings whose names match a requested string. The rule
    /// was added to fix the Bryson Freight House regression where
    /// Unity's built-in <c>Transform.Find</c> would land on an empty
    /// placeholder sibling instead of the real prefab-backed
    /// GameObject; these tests lock the priority order so future
    /// changes to the resolver fail CI before they ship.
    ///
    /// The resolver itself is intentionally Unity-free — the real
    /// <see cref="FusePrefabResolver.FindChild"/> walks the live
    /// <c>Transform</c> tree, classifies each child by
    /// name-match-kind + has-content, and delegates here. Tests
    /// construct the classified candidate list directly.
    /// </summary>
    public class FuseFindChildResolverTests
    {
        private static Candidate Exact(int index, bool hasContent = false) =>
            new Candidate(MatchKind.Exact, hasContent, index);

        private static Candidate Insensitive(int index, bool hasContent = false) =>
            new Candidate(MatchKind.CaseInsensitive, hasContent, index);

        public class EmptyInput
        {
            [Fact]
            public void NullList_ReturnsNull()
            {
                Assert.Null(SelectWinningIndex(null));
            }

            [Fact]
            public void EmptyList_ReturnsNull()
            {
                Assert.Null(SelectWinningIndex(new List<Candidate>()));
            }
        }

        public class SingleCandidate
        {
            [Fact]
            public void OneExactWithContent_Wins()
            {
                var list = new List<Candidate> { Exact(0, hasContent: true) };
                Assert.Equal(0, SelectWinningIndex(list));
            }

            [Fact]
            public void OneExactWithoutContent_Wins()
            {
                var list = new List<Candidate> { Exact(0, hasContent: false) };
                Assert.Equal(0, SelectWinningIndex(list));
            }

            [Fact]
            public void OneInsensitiveWithContent_Wins()
            {
                var list = new List<Candidate> { Insensitive(0, hasContent: true) };
                Assert.Equal(0, SelectWinningIndex(list));
            }

            [Fact]
            public void OneInsensitiveWithoutContent_Wins()
            {
                var list = new List<Candidate> { Insensitive(0, hasContent: false) };
                Assert.Equal(0, SelectWinningIndex(list));
            }
        }

        public class ContentBeatsEmpty
        {
            [Fact]
            public void ExactPlaceholderBeforeExactWithContent_ContentWins()
            {
                // The Bryson Freight House scenario: an empty wrapper
                // appears first in the child list, the real prefab-backed
                // one second. Resolver MUST pick the content-bearing one.
                var list = new List<Candidate>
                {
                    Exact(0, hasContent: false),
                    Exact(1, hasContent: true)
                };

                Assert.Equal(1, SelectWinningIndex(list));
            }

            [Fact]
            public void ExactContentBeforePlaceholder_ContentWins()
            {
                // Symmetric: order doesn't matter, content does.
                var list = new List<Candidate>
                {
                    Exact(0, hasContent: true),
                    Exact(1, hasContent: false)
                };

                Assert.Equal(0, SelectWinningIndex(list));
            }

            [Fact]
            public void MultiplePlaceholdersThenOneContent_ContentWins()
            {
                var list = new List<Candidate>
                {
                    Exact(0, hasContent: false),
                    Exact(1, hasContent: false),
                    Exact(2, hasContent: false),
                    Exact(3, hasContent: true)
                };

                Assert.Equal(3, SelectWinningIndex(list));
            }
        }

        public class TieBreakWithinSameTier
        {
            [Fact]
            public void TwoExactWithContent_FirstWins()
            {
                // Within a tier, stable child-order wins. This matches
                // Unity's prior Transform.Find contract and avoids us
                // picking a different sibling between two consecutive
                // reapplies (which would thrash the FUSE marker).
                var list = new List<Candidate>
                {
                    Exact(0, hasContent: true),
                    Exact(1, hasContent: true)
                };

                Assert.Equal(0, SelectWinningIndex(list));
            }

            [Fact]
            public void TwoExactWithoutContent_FirstWins()
            {
                var list = new List<Candidate>
                {
                    Exact(0, hasContent: false),
                    Exact(1, hasContent: false)
                };

                Assert.Equal(0, SelectWinningIndex(list));
            }
        }

        public class ExactBeatsCaseInsensitive
        {
            [Fact]
            public void ExactPlaceholder_BeatsCaseInsensitiveWithContent()
            {
                // The four-tier priority puts ANY exact match above ANY
                // case-insensitive match, even when the exact one is an
                // empty placeholder. This is deliberate: an exact-name
                // match is the author's intent; case-insensitive is a
                // last-resort tolerance for typo'd JSON. Picking a
                // case-insensitive content-bearing sibling over an
                // exact-name placeholder would surprise authors who
                // followed the actual scene-path conventions.
                var list = new List<Candidate>
                {
                    Insensitive(0, hasContent: true),
                    Exact(1, hasContent: false)
                };

                Assert.Equal(1, SelectWinningIndex(list));
            }

            [Fact]
            public void TwoExactNoContent_AndOneInsensitiveWithContent_ExactStillWins()
            {
                var list = new List<Candidate>
                {
                    Exact(0, hasContent: false),
                    Insensitive(1, hasContent: true),
                    Exact(2, hasContent: false)
                };

                Assert.Equal(0, SelectWinningIndex(list));
            }
        }

        public class CaseInsensitiveFallback
        {
            [Fact]
            public void NoExactMatches_PicksInsensitiveContentOverInsensitivePlaceholder()
            {
                var list = new List<Candidate>
                {
                    Insensitive(0, hasContent: false),
                    Insensitive(1, hasContent: true)
                };

                Assert.Equal(1, SelectWinningIndex(list));
            }

            [Fact]
            public void NoExactMatches_NoContent_PicksFirstInsensitive()
            {
                var list = new List<Candidate>
                {
                    Insensitive(0, hasContent: false),
                    Insensitive(1, hasContent: false)
                };

                Assert.Equal(0, SelectWinningIndex(list));
            }
        }

        public class FullPriorityCascade
        {
            [Fact]
            public void AllFourTiersPresent_ExactContentWins()
            {
                var list = new List<Candidate>
                {
                    Insensitive(0, hasContent: false),
                    Insensitive(1, hasContent: true),
                    Exact(2, hasContent: false),
                    Exact(3, hasContent: true)
                };

                Assert.Equal(3, SelectWinningIndex(list));
            }

            [Fact]
            public void NoExactContent_ExactPlaceholderWins()
            {
                var list = new List<Candidate>
                {
                    Insensitive(0, hasContent: false),
                    Insensitive(1, hasContent: true),
                    Exact(2, hasContent: false),
                    Exact(3, hasContent: false)
                };

                Assert.Equal(2, SelectWinningIndex(list));
            }

            [Fact]
            public void NoExactAtAll_InsensitiveContentWins()
            {
                var list = new List<Candidate>
                {
                    Insensitive(0, hasContent: false),
                    Insensitive(1, hasContent: true),
                    Insensitive(2, hasContent: false)
                };

                Assert.Equal(1, SelectWinningIndex(list));
            }

            [Fact]
            public void NoExactAtAll_NoInsensitiveContent_FirstInsensitiveWins()
            {
                var list = new List<Candidate>
                {
                    Insensitive(0, hasContent: false),
                    Insensitive(1, hasContent: false)
                };

                Assert.Equal(0, SelectWinningIndex(list));
            }
        }

        public class OriginalIndexThreading
        {
            // OriginalIndex is the contract the executor uses to fetch
            // the actual Transform via parent.GetChild(i). The resolver
            // returns the index INTO the candidate list — the executor
            // then reads candidate.OriginalIndex from the picked entry.
            // These tests confirm OriginalIndex round-trips intact so
            // a non-contiguous candidate list (e.g. one built by
            // skipping non-matching children) still maps back to the
            // right child.

            [Fact]
            public void NonContiguousIndices_PreservedInCandidate()
            {
                // Simulate: parent has 10 children, only 3 of them
                // match — original indices 2, 5, 9.
                var list = new List<Candidate>
                {
                    Exact(2, hasContent: false),
                    Exact(5, hasContent: true),
                    Insensitive(9, hasContent: true)
                };

                var winnerIndex = SelectWinningIndex(list);
                Assert.True(winnerIndex.HasValue);
                Assert.Equal(1, winnerIndex.Value);
                Assert.Equal(5, list[winnerIndex.Value].OriginalIndex);
            }
        }
    }
}
