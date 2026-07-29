using System.Collections.Generic;
using System.Linq;
using FUSE.Profiler.Entries;
using FUSE.Profiler.Instrumentation;
using Xunit;

namespace FUSE.Tests.Profiler
{
    /// <summary>
    /// Canary net for the built-in category tables: every target spec must
    /// resolve against the installed game assemblies. A game update that
    /// renames or removes one of these methods fails here at build time
    /// instead of silently showing a dead row in the field.
    /// </summary>
    public class RailroaderTargetResolutionTests
    {
        public static IEnumerable<object[]> AllBuiltInSpecs()
        {
            foreach (var entry in RailroaderEntries.CreateBuiltIns())
            {
                foreach (var spec in entry.TargetProvider())
                {
                    yield return new object[] { entry.Id, spec.MethodSpec, spec.Coroutine };
                }
            }
        }

        [Theory]
        [MemberData(nameof(AllBuiltInSpecs))]
        public void Every_builtin_target_resolves_against_the_game(string entryId, string methodSpec, bool coroutine)
        {
            var method = MethodResolver.Resolve(new TargetSpec(methodSpec, coroutine), out var error);
            Assert.True(method != null, $"{entryId}: {error}");
        }

        [Fact]
        public void BuiltIn_entry_ids_are_unique()
        {
            var ids = RailroaderEntries.CreateBuiltIns().Select(e => e.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [Fact]
        public void Physics_entries_use_the_sim_tick_clock_and_others_do_not()
        {
            foreach (var entry in RailroaderEntries.CreateBuiltIns())
            {
                if (entry.Category == ProfilerCategory.Physics)
                {
                    Assert.Equal(FUSE.Profiler.Engine.ProbeCadence.SimTick, entry.Cadence);
                }
                else
                {
                    Assert.Equal(FUSE.Profiler.Engine.ProbeCadence.Frame, entry.Cadence);
                }
            }
        }
    }
}
