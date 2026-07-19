using FUSE.Runtime.API;
using Xunit;

namespace FUSE.Tests.API
{
    /// <summary>
    /// Pins the refresh-profile decision that gates
    /// ProgressionAPI.RefreshRuntimeStateAfterApply. The semantics matter for
    /// data integrity: Deferred protects the stale-IsSandbox load window (the
    /// Ela-bridge corruption class), NoProgression keeps sandbox sessions from
    /// waiting forever on a Progression.Configure that never comes, and a
    /// configured progression must always win over a stale no-progression
    /// observation.
    /// </summary>
    public class ProgressionRefreshProfileTests
    {
        // Expected values are passed as the enum's underlying int because the
        // profile enum is internal (InternalsVisibleTo) and xUnit test methods
        // must stay public.
        [Theory]
        [InlineData(false, false, (int)ProgressionRefreshProfile.Deferred)]
        [InlineData(true, false, (int)ProgressionRefreshProfile.Full)]
        [InlineData(false, true, (int)ProgressionRefreshProfile.NoProgression)]
        public void Determine_maps_settle_state_to_profile(
            bool configuredWithProgression,
            bool settledWithoutProgression,
            int expected)
        {
            Assert.Equal(
                (ProgressionRefreshProfile)expected,
                ProgressionRefreshProfiles.Determine(configuredWithProgression, settledWithoutProgression));
        }

        [Fact]
        public void Determine_configured_progression_wins_over_no_progression_settle()
        {
            // Defensive precedence: if both flags are somehow set, the session
            // has a real Progression and the full refresh is the correct one —
            // the reduced profile must never demote a configured load.
            Assert.Equal(
                ProgressionRefreshProfile.Full,
                ProgressionRefreshProfiles.Determine(
                    configuredWithProgression: true,
                    settledWithoutProgression: true));
        }
    }
}
