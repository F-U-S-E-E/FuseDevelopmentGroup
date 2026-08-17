namespace FUSE.Runtime.API
{
    /// <summary>
    /// Which flavor of <see cref="ProgressionAPI.RefreshRuntimeStateAfterApply"/>
    /// is safe to run, given how far the game's per-load progression setup has
    /// settled.
    /// </summary>
    internal enum ProgressionRefreshProfile
    {
        /// <summary>
        /// Neither <c>Progression.Configure</c> nor the no-progression settle
        /// point has been observed for this load. <c>StateManager.IsSandbox</c>
        /// is still the pre-deserialization default and any feature-state write
        /// would use the wrong assumption — park the refresh.
        /// </summary>
        Deferred,

        /// <summary>
        /// A real <c>Progression</c> was configured (company-mode load).
        /// The full refresh — including the game's feature-state replay and
        /// track-group restore — is safe and required.
        /// </summary>
        Full,

        /// <summary>
        /// The load settled WITHOUT a configured progression (sandbox save, or
        /// a save whose progression id resolved to nothing). GameMode is now
        /// authoritative, but there is no Progression object to derive section
        /// state from, and the game's own initial feature pass already applied
        /// explicit save entries plus sandbox defaults. Only the
        /// mode-independent maintenance steps may run; replaying feature state
        /// or re-disabling feature-claimed track groups would lock mod content
        /// that sandbox convention keeps open (synthesized section-unlock
        /// features are authored defaultEnableInSandbox=false).
        /// </summary>
        NoProgression,
    }

    /// <summary>
    /// Pure decision logic for the refresh profile — kept Unity-free and
    /// separate from <see cref="ProgressionAPI"/> so it is unit-testable.
    /// </summary>
    internal static class ProgressionRefreshProfiles
    {
        /// <summary>
        /// A configured progression always wins: if <c>Progression.Configure</c>
        /// ran, the session has real section state regardless of any earlier
        /// no-progression observation, and the full refresh is the correct one.
        /// </summary>
        internal static ProgressionRefreshProfile Determine(
            bool configuredWithProgression,
            bool settledWithoutProgression)
        {
            if (configuredWithProgression)
            {
                return ProgressionRefreshProfile.Full;
            }

            return settledWithoutProgression
                ? ProgressionRefreshProfile.NoProgression
                : ProgressionRefreshProfile.Deferred;
        }
    }
}
