using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuse.Core.Bridge;
using Fuse.LiveHarness.Bridge;
using Fuse.LiveHarness.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Fuse.LiveHarness.Fixtures;

/// <summary>Outcome of golden-mastering one capture (the report, or one dump) against its baseline.</summary>
public sealed record CaptureResult(string Name, bool Ok, IReadOnlyList<JsonDelta> Deltas, string Note);

/// <summary>Outcome of a whole fixture run.</summary>
public sealed class FixtureRunResult
{
    public bool Success { get; private init; }

    public bool WasSkipped { get; private init; }

    public bool Updated { get; private init; }

    public string? Message { get; private init; }

    public IReadOnlyList<CaptureResult> Captures { get; private init; } = Array.Empty<CaptureResult>();

    public static FixtureRunResult Error(string message) => new() { Success = false, Message = message };

    public static FixtureRunResult Skipped(string message) => new() { Success = true, WasSkipped = true, Message = message };

    public static FixtureRunResult Completed(IReadOnlyList<CaptureResult> captures, bool updated) => new()
    {
        Captures = captures,
        Updated = updated,
        Success = updated || captures.All(c => c.Ok),
    };
}

/// <summary>
/// Drives one fixture against the live game: load the fixture save, wait until ready, reload FUSE
/// packages, capture the report + dumps, normalize, and either write baselines (<c>update</c>) or
/// diff against the stored baselines. The capture/normalize/diff steps reuse the CI-tested
/// <see cref="JsonNormalizer"/> and <see cref="JsonDiff"/>; only the orchestration needs the live game.
/// </summary>
public sealed class FixtureRunner
{
    private readonly BridgeClient _client;
    private readonly JsonNormalizer _normalizer;

    public FixtureRunner(BridgeClient client, JsonNormalizer? normalizer = null)
    {
        _client = client;
        _normalizer = normalizer ?? new JsonNormalizer();
    }

    public async Task<FixtureRunResult> RunAsync(string fixtureDir, bool updateBaselines, CancellationToken ct = default)
    {
        var manifestPath = Path.Combine(fixtureDir, "fixture.json");
        if (!File.Exists(manifestPath))
        {
            return FixtureRunResult.Error($"fixture.json not found in {fixtureDir}");
        }

        var manifest = JsonConvert.DeserializeObject<FixtureManifest>(File.ReadAllText(manifestPath))
                       ?? new FixtureManifest();

        var state = _client.ReadState();
        if (state is null || _client.Classify(state, DateTime.UtcNow) != BridgeConnection.Connected)
        {
            return FixtureRunResult.Error("bridge not connected — is the game running with FUSE.TestBridge enabled?");
        }

        if (!string.IsNullOrEmpty(manifest.GameVersion)
            && !string.Equals(manifest.GameVersion, state.GameVersion, StringComparison.Ordinal))
        {
            return FixtureRunResult.Skipped(
                $"game version '{state.GameVersion}' != fixture '{manifest.GameVersion}'; baselines are version-pinned.");
        }

        if (!string.IsNullOrEmpty(manifest.SaveName))
        {
            var load = await _client.SendAsync(new TestRequest { Verb = BridgeProtocol.TestVerbLoadSave, Arg = manifest.SaveName }, ct);
            if (!load.Ok)
            {
                return FixtureRunResult.Error($"failed to load save '{manifest.SaveName}': {load.Error}");
            }

            if (!await _client.WaitReadyAsync(ct: ct))
            {
                return FixtureRunResult.Error($"session not ready after loading save '{manifest.SaveName}'.");
            }
        }
        else if (!await _client.WaitReadyAsync(ct: ct))
        {
            return FixtureRunResult.Error("no map loaded — load a save first or set saveName in the fixture.");
        }

        var reload = await _client.SendAsync(new TestRequest { Verb = BridgeProtocol.TestVerbReload, Reason = manifest.Reason }, ct);
        if (!reload.Ok)
        {
            return FixtureRunResult.Error($"reload failed: {reload.Error}");
        }

        var baselineDir = Path.Combine(fixtureDir, "baselines");
        Directory.CreateDirectory(baselineDir);

        var captures = new List<CaptureResult>();

        if (manifest.CaptureReport)
        {
            var report = await _client.SendAsync(new TestRequest { Verb = BridgeProtocol.TestVerbReport, Arg = "json" }, ct);
            captures.Add(report.Ok
                ? Capture("report", report.Text ?? string.Empty, baselineDir, updateBaselines)
                : new CaptureResult("report", false, Array.Empty<JsonDelta>(), $"request failed: {report.Error}"));
        }

        foreach (var dump in manifest.Dumps ?? Array.Empty<string>())
        {
            var result = await _client.SendAsync(new TestRequest { Verb = BridgeProtocol.TestVerbDump, Arg = dump }, ct);
            if (!result.Ok)
            {
                captures.Add(new CaptureResult(dump, false, Array.Empty<JsonDelta>(), $"request failed: {result.Error}"));
                continue;
            }

            var json = result.ArtifactPath is not null && File.Exists(result.ArtifactPath)
                ? File.ReadAllText(result.ArtifactPath)
                : result.Text ?? string.Empty;
            captures.Add(Capture(dump, json, baselineDir, updateBaselines));
        }

        return FixtureRunResult.Completed(captures, updateBaselines);
    }

    private CaptureResult Capture(string name, string json, string baselineDir, bool update)
    {
        JToken current;
        try
        {
            current = _normalizer.Normalize(JToken.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json));
        }
        catch (JsonException ex)
        {
            return new CaptureResult(name, false, Array.Empty<JsonDelta>(), $"could not parse capture: {ex.Message}");
        }

        var baselinePath = Path.Combine(baselineDir, name + ".json");
        if (update || !File.Exists(baselinePath))
        {
            var existed = File.Exists(baselinePath);
            File.WriteAllText(baselinePath, current.ToString(Formatting.Indented));
            return new CaptureResult(name, true, Array.Empty<JsonDelta>(), existed ? "baseline updated" : "baseline created");
        }

        JToken baseline;
        try
        {
            baseline = _normalizer.Normalize(JToken.Parse(File.ReadAllText(baselinePath)));
        }
        catch (JsonException ex)
        {
            return new CaptureResult(name, false, Array.Empty<JsonDelta>(), $"could not parse baseline: {ex.Message}");
        }

        var deltas = JsonDiff.Compare(baseline, current);
        return new CaptureResult(name, deltas.Count == 0, deltas, deltas.Count == 0 ? "match" : $"{deltas.Count} delta(s)");
    }
}
