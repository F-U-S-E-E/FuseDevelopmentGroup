namespace Fuse.LiveHarness.Fixtures;

/// <summary>
/// A live-harness fixture manifest (fixture.json): which save to load, the expected game version
/// (a mismatch skips rather than fails), and which captures to golden-master.
/// </summary>
public sealed class FixtureManifest
{
    public string Id { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Expected game version; if set and the live game differs, the run is skipped (baselines are version-pinned).</summary>
    public string GameVersion { get; set; } = string.Empty;

    /// <summary>The save to load into the running session before capturing. Empty = use whatever is loaded.</summary>
    public string SaveName { get; set; } = string.Empty;

    public string MultiplayerRole { get; set; } = "host";

    /// <summary>Reason string passed to reload (kept fixed so it does not perturb the captured report).</summary>
    public string Reason { get; set; } = "fixture run";

    /// <summary>Capture and golden-master the <c>/fuse.report json</c> output.</summary>
    public bool CaptureReport { get; set; } = true;

    /// <summary>Dump captures to golden-master. Valid: graph, runtimegraph, mandelas, progression.</summary>
    public string[] Dumps { get; set; } = { "runtimegraph", "mandelas" };
}
