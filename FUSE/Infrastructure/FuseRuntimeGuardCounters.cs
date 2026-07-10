namespace FUSE.Infrastructure
{
    /// <summary>
    /// Session-cumulative counters for every runtime guard FUSE keeps around
    /// broken content (decal culling, car-decal helpers, curve-mesh culling,
    /// scenery decal machinery, scenery asset loads, stale flares) plus the
    /// frame-spike diagnostic.
    ///
    /// Lives in Infrastructure so both ends of the dependency rule work: the
    /// guard patches (FUSE.Patches) write here, and the load report
    /// (FUSE.Loading) and Health UI read here without a compile-time
    /// dependency on any patch class. That is what puts guard status into
    /// /fuse.report — the thing users actually paste when something feels
    /// wrong — instead of leaving it visible only to whoever opens the
    /// Advanced tab on the affected machine. All counters are main-thread
    /// plain longs: rendering them costs a handful of static reads.
    /// </summary>
    internal static class FuseRuntimeGuardCounters
    {
        internal static long DecalRegistryScrubbed { get; private set; }
        internal static long DecalVisibilitySuppressed { get; private set; }
        internal static long DecalHelperEnableSuppressed { get; private set; }
        internal static long DecalHelperDisableSuppressed { get; private set; }
        internal static long DecalRegistrationsRejected { get; private set; }
        internal static long CurveMeshSuppressed { get; private set; }
        internal static long SceneryDecalComponentsDisabled { get; private set; }
        internal static long SceneryLoadFailures { get; private set; }
        internal static long SceneryPlacementsQuarantined { get; private set; }
        internal static long FlareSuppressed { get; private set; }

        internal static long FrameSpikes { get; private set; }
        internal static float FrameSpikeWorstMs { get; private set; }

        // Liveness proof, not a fault: how many scenery load tasks the
        // load-failure watcher attached to. Zero in a session with scenery
        // activity means the watch postfix is not seeing loads at all (e.g. a
        // third-party loader bypassing the patched path) — the exact
        // field-debugging question a pasted report should answer.
        internal static long SceneryLoadWatchAttached { get; private set; }

        /// <summary>
        /// Contained guard events this session (frame spikes excluded — they
        /// are measurements, not contained faults). Zero means no broken
        /// content needed containing.
        /// </summary>
        internal static long GuardTotal =>
            DecalRegistryScrubbed +
            DecalVisibilitySuppressed +
            DecalHelperEnableSuppressed +
            DecalHelperDisableSuppressed +
            DecalRegistrationsRejected +
            CurveMeshSuppressed +
            SceneryDecalComponentsDisabled +
            SceneryLoadFailures +
            SceneryPlacementsQuarantined +
            FlareSuppressed;

        internal static bool AllIdle => GuardTotal == 0;

        internal static long RecordDecalRegistryScrubbed() => ++DecalRegistryScrubbed;

        internal static long RecordDecalVisibilitySuppressed() => ++DecalVisibilitySuppressed;

        internal static long RecordDecalHelperEnableSuppressed() => ++DecalHelperEnableSuppressed;

        internal static long RecordDecalHelperDisableSuppressed() => ++DecalHelperDisableSuppressed;

        internal static long RecordDecalRegistrationRejected() => ++DecalRegistrationsRejected;

        internal static long RecordCurveMeshSuppressed() => ++CurveMeshSuppressed;

        internal static long RecordSceneryDecalComponentDisabled() => ++SceneryDecalComponentsDisabled;

        internal static long RecordSceneryLoadFailure() => ++SceneryLoadFailures;

        internal static long RecordSceneryPlacementQuarantined() => ++SceneryPlacementsQuarantined;

        internal static long RecordFlareSuppressed() => ++FlareSuppressed;

        internal static long RecordFrameSpike(float frameMs)
        {
            FrameSpikes++;
            if (frameMs > FrameSpikeWorstMs)
            {
                FrameSpikeWorstMs = frameMs;
            }

            return FrameSpikes;
        }

        internal static long RecordSceneryLoadWatchAttached() => ++SceneryLoadWatchAttached;

        /// <summary>
        /// One-line breakdown used by the load report and the Health tab.
        /// Session-cumulative; all-zero means the guards sat idle.
        /// </summary>
        internal static string FormatSummary()
        {
            var spikes = FrameSpikes == 0
                ? "frameSpikes=0"
                : $"frameSpikes={FrameSpikes} (worst {FrameSpikeWorstMs:F0}ms)";
            return
                $"decalScrubbed={DecalRegistryScrubbed} decalVisibility={DecalVisibilitySuppressed} " +
                $"decalHelperEnable={DecalHelperEnableSuppressed} decalHelperDisable={DecalHelperDisableSuppressed} " +
                $"decalRegistrationsRejected={DecalRegistrationsRejected} " +
                $"curveMesh={CurveMeshSuppressed} sceneryCarDecalsDisabled={SceneryDecalComponentsDisabled} " +
                $"sceneryLoadFailures={SceneryLoadFailures} sceneryQuarantined={SceneryPlacementsQuarantined} " +
                $"sceneryLoadWatch={SceneryLoadWatchAttached} " +
                $"flares={FlareSuppressed} " +
                spikes;
        }

        /// <summary>Test hook — the counters are session-cumulative by design.</summary>
        internal static void ResetForTests()
        {
            DecalRegistryScrubbed = 0;
            DecalVisibilitySuppressed = 0;
            DecalHelperEnableSuppressed = 0;
            DecalHelperDisableSuppressed = 0;
            DecalRegistrationsRejected = 0;
            CurveMeshSuppressed = 0;
            SceneryDecalComponentsDisabled = 0;
            SceneryLoadFailures = 0;
            SceneryPlacementsQuarantined = 0;
            FlareSuppressed = 0;
            FrameSpikes = 0;
            FrameSpikeWorstMs = 0f;
            SceneryLoadWatchAttached = 0;
        }
    }
}
