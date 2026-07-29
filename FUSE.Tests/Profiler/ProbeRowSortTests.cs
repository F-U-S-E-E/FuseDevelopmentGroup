using System;
using System.Collections.Generic;
using FUSE.Profiler.Engine;
using Xunit;

namespace FUSE.Tests.Profiler
{
    public class ProbeRowSortTests
    {
        private static ProbeRow Row(string key, double avg, double max, long calls)
        {
            return new ProbeRow(key, key, null, ProbeCadence.Frame, avg, max, avg * 10, calls, 1, 0d);
        }

        [Fact]
        public void Sorts_descending_by_the_selected_metric()
        {
            var rows = new List<ProbeRow> { Row("a", 1, 9, 5), Row("b", 3, 1, 1), Row("c", 2, 5, 9) };

            ProbeRow.SortForDisplay(rows, ProbeSortMode.Average, null);
            Assert.Equal(new[] { "b", "c", "a" }, rows.ConvertAll(r => r.Key));

            ProbeRow.SortForDisplay(rows, ProbeSortMode.Max, null);
            Assert.Equal(new[] { "a", "c", "b" }, rows.ConvertAll(r => r.Key));

            ProbeRow.SortForDisplay(rows, ProbeSortMode.Calls, null);
            Assert.Equal(new[] { "c", "a", "b" }, rows.ConvertAll(r => r.Key));
        }

        [Fact]
        public void Pinned_rows_float_to_the_top_regardless_of_metric()
        {
            var rows = new List<ProbeRow> { Row("big", 100, 100, 100), Row("small", 1, 1, 1) };
            var pinned = new HashSet<string>(StringComparer.Ordinal) { "small" };

            ProbeRow.SortForDisplay(rows, ProbeSortMode.Average, pinned);
            Assert.Equal("small", rows[0].Key);
        }

        [Fact]
        public void Name_sort_is_case_insensitive_ascending()
        {
            var rows = new List<ProbeRow> { Row("beta", 1, 1, 1), Row("Alpha", 2, 2, 2) };
            ProbeRow.SortForDisplay(rows, ProbeSortMode.Name, null);
            Assert.Equal("Alpha", rows[0].Key);
        }
    }
}
