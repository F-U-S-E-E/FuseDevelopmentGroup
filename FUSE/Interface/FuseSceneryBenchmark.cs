using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FUSE.Infrastructure;
using FUSE.Patches;
using Helpers;
using Model.Ops;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;
using UnityEngine.Profiling;

namespace FUSE.Interface
{
    /// <summary>
    /// Scenario-driven, automated, reproducible camera benchmark for issue #76 and
    /// general culling/streaming debugging.
    ///
    /// A <see cref="Scenario"/> is just a named camera-movement coroutine. The
    /// generic runner wraps any scenario: it resets the FUSE churn counters, runs the
    /// movement (sampling peak in-flight loads), then records one JSON row of churn +
    /// timing to FUSE-scenery-benchmark.json. "A/B" runs the scenario twice, forcing
    /// the debounce off then on via
    /// <see cref="FuseSceneryCullingDebouncePatch.BenchmarkDebounceOverride"/> (a
    /// benchmark-only override, not a user setting) — the churn delta is the signal.
    ///
    /// Built-in scenarios:
    ///  - "sweep": jump to your current view, then oscillate the camera across the
    ///    ~1500m cull boundary (quick, local churn test).
    ///  - "corridor": teleport between Bryson and Sylva a few times, then drive the
    ///    camera up and down the track between them at a set pace (realistic streaming
    ///    exercise). Endpoints resolve from the game's PassengerStops by name; the
    ///    drive uses the public Track.Graph to walk the rails.
    ///
    /// All movement uses the proven public camera APIs (CameraSelector.JumpToPoint /
    /// MoveStrategyToPoint) — no game-internal reflection.
    /// </summary>
    internal static class FuseSceneryBenchmark
    {
        // Sweep scenario.
        private const float ColdOffsetMeters = 8000f;
        private const float SweepDistanceMeters = 2500f;
        private const float SweepCycleSeconds = 4f;
        private const float SweepDurationSeconds = 16f;
        private const float ColdTimeoutSeconds = 30f;
        private const float TargetTimeoutSeconds = 60f;
        private const float SettleConfirmSeconds = 0.5f;

        // Corridor scenario.
        private const string CorridorFromName = "Bryson";
        private const string CorridorToName = "Sylva";
        private const int TeleportCycles = 3;
        private const int RoundTrips = 2;
        private const float PaceMetersPerSec = 350f;
        private const float DwellSeconds = 3f;
        private const float DwellTimeoutSeconds = 30f;
        private const float LegTimeoutSeconds = 120f;

        private static readonly SceneryLoadStateReader LoadState = new SceneryLoadStateReader();

        private static bool _running;
        private static string _status = "idle";

        // The sampler for the run currently in progress, so a scenario's movement can
        // reset the per-run latency/load window at a measurement boundary (e.g. the
        // sweep resets after the one-time cold load). Set while a run is active.
        private static RunSampler _activeSampler;

        internal static string Status => _status;
        internal static bool IsRunning => _running;

        // ---- Entry points (Advanced panel) ----

        internal static string RunCorridor() => StartScenario(BuildCorridorScenario(), AbMode.Single);

        internal static string RunCorridorAb() => StartScenario(BuildCorridorScenario(), AbMode.Debounce);

        internal static string RunSweepAb() => StartScenario(BuildSweepScenario(), AbMode.Debounce);

        internal static string RunCorridorThrottleAb() => StartScenario(BuildCorridorScenario(), AbMode.Throttle);

        internal static string RunSweepThrottleAb() => StartScenario(BuildSweepScenario(), AbMode.Throttle);

        // Which lever (if any) an A/B run toggles between its baseline and fix passes.
        private enum AbMode
        {
            Single,
            Debounce,
            Throttle
        }

        private static string StartScenario(Scenario scenario, AbMode mode)
        {
            if (_running)
            {
                return _status = "Benchmark already running.";
            }

            if (scenario == null)
            {
                return _status; // builder already set a reason
            }

            if (!LoadState.Available)
            {
                return _status = "Cannot read SceneryAssetInstance load state (game layout changed). Benchmark unavailable.";
            }

            if (CameraSelector.shared == null)
            {
                return _status = "No camera available — load a map first.";
            }

            var routine = mode == AbMode.Single ? RunSingleRoutine(scenario) : RunAbRoutine(scenario, mode);
            BenchmarkRunner.Instance.StartCoroutine(routine);
            var suffix = mode == AbMode.Single ? string.Empty
                : mode == AbMode.Throttle ? "throttle A/B " : "A/B ";
            return _status = $"{scenario.Name} {suffix}started — watch the status line / FUSE.log.";
        }

        // ---- Scenario definitions ----

        private sealed class Scenario
        {
            public string Name;
            public Func<IEnumerator> Movement;
        }

        private static Scenario BuildSweepScenario()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                _status = "No active camera. Switch to the overview camera and try again.";
                return null;
            }

            var target = WorldTransformer.WorldToGame(cam.transform.position);
            var rotation = cam.transform.rotation;
            var cold = target + new Vector3(ColdOffsetMeters, 0f, 0f);
            return new Scenario { Name = "sweep", Movement = () => SweepMovement(target, cold, rotation) };
        }

        private static Scenario BuildCorridorScenario()
        {
            return new Scenario { Name = "corridor", Movement = CorridorMovement };
        }

        // ---- Generic A/B harness ----

        private static IEnumerator RunSingleRoutine(Scenario scenario)
        {
            _running = true;
            yield return RunScenarioOnce(scenario, null, null, "single", null);
            _running = false;
        }

        private static IEnumerator RunAbRoutine(Scenario scenario, AbMode mode)
        {
            _running = true;
            JObject baseline = null;
            JObject fixedRun = null;
            if (mode == AbMode.Throttle)
            {
                // Debounce stays default-on; toggle only the load throttle so the delta
                // isolates batch-load throughput from churn.
                yield return RunScenarioOnce(scenario, null, false, "baseline-no-throttle", r => baseline = r);
                yield return RunScenarioOnce(scenario, null, true, "fix-throttle", r => fixedRun = r);
            }
            else
            {
                yield return RunScenarioOnce(scenario, false, null, "baseline-no-debounce", r => baseline = r);
                yield return RunScenarioOnce(scenario, true, null, "fix-debounce", r => fixedRun = r);
            }

            _running = false;

            if (baseline != null && fixedRun != null)
            {
                if (mode == AbMode.Throttle)
                {
                    _status =
                        $"{scenario.Name} throttle A/B — minFps base={baseline.Value<double>("minFps"):0} fix={fixedRun.Value<double>("minFps"):0}; " +
                        $"peakInFlight base={baseline.Value<long>("peakInFlight")} fix={fixedRun.Value<long>("peakInFlight")}; " +
                        $"maxLoadMs base={baseline.Value<double>("maxLoadMs"):0} fix={fixedRun.Value<double>("maxLoadMs"):0} " +
                        $"(deferred {fixedRun.Value<long>("deferredLoads")}, peakQueue {fixedRun.Value<int>("peakQueueDepth")}). " +
                        $"duration base={baseline.Value<double>("durationMs"):0}ms fix={fixedRun.Value<double>("durationMs"):0}ms.";
                }
                else
                {
                    _status =
                        $"{scenario.Name} A/B — churn base {baseline.Value<long>("fuseLoads")}L/{baseline.Value<long>("fuseUnloads")}U " +
                        $"vs fix {fixedRun.Value<long>("fuseLoads")}L/{fixedRun.Value<long>("fuseUnloads")}U " +
                        $"(held {fixedRun.Value<long>("suppressedUnloads")}). " +
                        $"minFps base={baseline.Value<double>("minFps"):0} fix={fixedRun.Value<double>("minFps"):0}. " +
                        $"duration base={baseline.Value<double>("durationMs"):0}ms fix={fixedRun.Value<double>("durationMs"):0}ms.";
                }

                if (baseline.Value<bool>("inconclusive") || fixedRun.Value<bool>("inconclusive"))
                {
                    _status = "INCONCLUSIVE — scenario never engaged FUSE scenery culling; " +
                              "run it in a denser area / teleport. " + _status;
                }

                FuseLog.Info("FUSE benchmark A/B: " + _status);
            }
        }

        // try/finally (no catch around yields) guarantees the overrides are cleared.
        private static IEnumerator RunScenarioOnce(
            Scenario scenario, bool? debounceOverride, bool? throttleOverride, string label, Action<JObject> onResult)
        {
            var prevDiagnostics = FuseSettings.EnableSceneryCullingDiagnostics;
            JObject result = null;
            RunSampler sampler = null;
            try
            {
                FuseSettings.SetSceneryCullingDiagnosticsTransient(true);
                FuseSceneryCullingDebouncePatch.BenchmarkDebounceOverride = debounceOverride;
                FuseSceneryLoadThrottlePatch.BenchmarkThrottleOverride = throttleOverride;
                FuseSceneryCullingDiagnosticPatch.ResetCounters();
                FuseSceneryCullingDebouncePatch.ResetSuppressedUnloads();
                FuseSceneryLoadThrottlePatch.ResetStats();

                // Parallel per-frame sampler -> time-series CSV (FPS, counts, churn,
                // load latency, memory). Runs as its own coroutine so it samples even
                // while the movement is inside a nested wait.
                sampler = new RunSampler(scenario.Name, label);
                _activeSampler = sampler;
                BenchmarkRunner.Instance.StartCoroutine(sampler.Loop());

                var startTime = Time.realtimeSinceStartup;
                var movement = scenario.Movement();
                while (true)
                {
                    bool advanced;
                    try
                    {
                        advanced = movement.MoveNext();
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Exception($"FUSE benchmark scenario '{scenario.Name}' movement failed", ex);
                        break;
                    }

                    if (!advanced)
                    {
                        break;
                    }

                    yield return movement.Current;
                }

                var durationMs = Math.Round((Time.realtimeSinceStartup - startTime) * 1000f, 0);
                result = new JObject
                {
                    ["timestampLocal"] = DateTime.Now.ToString("O"),
                    ["scenario"] = scenario.Name,
                    ["label"] = label,
                    ["buildConfiguration"] = BuildConfiguration,
                    ["debounce"] = debounceOverride.HasValue
                        ? (debounceOverride.Value ? "forced-on" : "forced-off")
                        : "default-on",
                    ["deadbandMeters"] = FuseSceneryCullingDebouncePatch.UnloadDistance,
                    ["durationMs"] = durationMs,
                    ["peakInFlight"] = sampler.PeakInFlight,
                    ["avgFps"] = Math.Round(sampler.AvgFps, 1),
                    ["minFps"] = Math.Round(sampler.MinFps, 1),
                    ["fuseLoads"] = FuseSceneryCullingDiagnosticPatch.FuseLoads,
                    ["fuseUnloads"] = FuseSceneryCullingDiagnosticPatch.FuseUnloads,
                    ["vanillaLoads"] = FuseSceneryCullingDiagnosticPatch.VanillaLoads,
                    ["vanillaUnloads"] = FuseSceneryCullingDiagnosticPatch.VanillaUnloads,
                    ["suppressedUnloads"] = FuseSceneryCullingDebouncePatch.SuppressedUnloads,
                    ["throttle"] = throttleOverride.HasValue
                        ? (throttleOverride.Value ? "forced-on" : "forced-off")
                        : "default-on",
                    ["maxLoadsPerFrame"] = FuseSceneryLoadThrottlePatch.MaxLoadsPerFrame,
                    ["deferredLoads"] = FuseSceneryLoadThrottlePatch.DeferredLoads,
                    ["releasedLoads"] = FuseSceneryLoadThrottlePatch.ReleasedLoads,
                    ["droppedStaleLoads"] = FuseSceneryLoadThrottlePatch.DroppedStaleLoads,
                    ["peakQueueDepth"] = FuseSceneryLoadThrottlePatch.PeakQueueDepth,
                    ["avgLoadMs"] = Math.Round(sampler.AvgLoadMs, 0),
                    ["maxLoadMs"] = Math.Round(sampler.MaxLoadMs, 0),
                    ["csv"] = sampler.FileName
                };

                // A run that never engaged the FUSE scenery path can't validate
                // anything — flag it INCONCLUSIVE so a too-light scenario (all-zero
                // counters) is not mistaken for a passing regression guard.
                var engaged = FuseSceneryBenchmarkEngagement.Engaged(
                    FuseSceneryCullingDiagnosticPatch.FuseLoads,
                    FuseSceneryCullingDebouncePatch.SuppressedUnloads,
                    FuseSceneryLoadThrottlePatch.DeferredLoads,
                    FuseSceneryLoadThrottlePatch.PeakQueueDepth);
                result["engaged"] = engaged;
                result["inconclusive"] = !engaged;
                if (!engaged)
                {
                    FuseLog.Warning(
                        $"FUSE benchmark [{scenario.Name}/{label}] is INCONCLUSIVE: the scenario never engaged FUSE " +
                        "scenery culling (0 FUSE loads, 0 debounce suppressions, 0 throttle deferrals/queue). Run a " +
                        "denser area or teleport into heavily-modded scenery so the load path is actually exercised.");
                }

                AppendResult(result);
                _status =
                    $"[{scenario.Name}/{label}] done {durationMs:0}ms; loads={FuseSceneryCullingDiagnosticPatch.FuseLoads} " +
                    $"unloads={FuseSceneryCullingDiagnosticPatch.FuseUnloads} suppressed={FuseSceneryCullingDebouncePatch.SuppressedUnloads} " +
                    $"deferred={FuseSceneryLoadThrottlePatch.DeferredLoads} peakQueue={FuseSceneryLoadThrottlePatch.PeakQueueDepth} " +
                    $"minFps={sampler.MinFps:0} peak={sampler.PeakInFlight} maxLoadMs={sampler.MaxLoadMs:0}. CSV: {sampler.FileName}";
                FuseLog.Info("FUSE benchmark " + _status);
            }
            finally
            {
                sampler?.Finish();
                _activeSampler = null;
                FuseSceneryCullingDebouncePatch.BenchmarkDebounceOverride = null;
                FuseSceneryLoadThrottlePatch.BenchmarkThrottleOverride = null;
                FuseSettings.SetSceneryCullingDiagnosticsTransient(prevDiagnostics);
                onResult?.Invoke(result);
            }
        }

        // ---- Scenario: sweep ----

        private static IEnumerator SweepMovement(Vector3 target, Vector3 cold, Quaternion rotation)
        {
            _status = "[sweep] cooling — jumping away…";
            Teleport(cold, rotation);
            yield return WaitForLoadAndSettle(ColdTimeoutSeconds, null);
            for (var i = 0; i < 30; i++)
            {
                yield return null;
            }

            _status = "[sweep] cold-loading — jumping to target…";
            Teleport(target, rotation);
            yield return WaitForLoadAndSettle(TargetTimeoutSeconds, null);

            // Measure only the sweep flaps: reset ALL benchmarked counters here — churn,
            // throttle (deferred/released/peak-queue), and the sampler's per-run latency/
            // load window — so the end-snapshot and CSV reflect boundary churn, not the
            // one-time cold load that just finished.
            FuseSceneryCullingDiagnosticPatch.ResetCounters();
            FuseSceneryCullingDebouncePatch.ResetSuppressedUnloads();
            FuseSceneryLoadThrottlePatch.ResetStats();
            _activeSampler?.ResetLatencyWindow();

            _status = "[sweep] sweeping — measuring churn…";
            var sweepFar = target + new Vector3(SweepDistanceMeters, 0f, 0f);
            var start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < SweepDurationSeconds)
            {
                var elapsed = Time.realtimeSinceStartup - start;
                var phase = 0.5f * (1f - Mathf.Cos(elapsed / SweepCycleSeconds * 2f * Mathf.PI));
                MoveStrategy(Vector3.Lerp(target, sweepFar, phase), rotation);
                yield return null;
            }

            MoveStrategy(target, rotation);
        }

        // ---- Scenario: corridor ----

        private static IEnumerator CorridorMovement()
        {
            var from = ResolveStop(CorridorFromName);
            var to = ResolveStop(CorridorToName);
            if (from == null || to == null)
            {
                _status = $"corridor: could not find '{CorridorFromName}' and/or '{CorridorToName}' stations.";
                FuseLog.Warning("FUSE benchmark " + _status);
                yield break;
            }

            var fromPos = from.CenterPoint;
            var toPos = to.CenterPoint;
            var rotation = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;

            // Phase 1: teleport back and forth a few times.
            for (var cycle = 0; cycle < TeleportCycles; cycle++)
            {
                _status = $"corridor: teleport {cycle + 1}/{TeleportCycles} → {CorridorFromName}";
                Teleport(fromPos, rotation);
                yield return WaitForLoadAndSettle(DwellTimeoutSeconds, null);
                yield return new WaitForSecondsRealtime(DwellSeconds);

                _status = $"corridor: teleport {cycle + 1}/{TeleportCycles} → {CorridorToName}";
                Teleport(toPos, rotation);
                yield return WaitForLoadAndSettle(DwellTimeoutSeconds, null);
                yield return new WaitForSecondsRealtime(DwellSeconds);
            }

            // Phase 2: A* a route along the rails between the stations, then drive up
            // and down it. (LocationByMoving alone wanders into yard dead-ends at
            // switches — A* picks the correct branches.)
            var route = BuildTrackRoute(fromPos, toPos);
            if (route == null || route.Count < 2)
            {
                _status = $"corridor: no track route {CorridorFromName}→{CorridorToName} found — teleport phase only.";
                FuseLog.Warning("FUSE benchmark " + _status);
                yield break;
            }

            FuseLog.Info($"FUSE benchmark corridor route resolved: {route.Count} points.");
            var reverse = new List<Vector3>(route);
            reverse.Reverse();
            for (var trip = 0; trip < RoundTrips; trip++)
            {
                _status = $"corridor: track run {trip + 1}/{RoundTrips} → {CorridorToName}";
                yield return DrivePolyline(route, rotation);

                _status = $"corridor: track run {trip + 1}/{RoundTrips} → {CorridorFromName}";
                yield return DrivePolyline(reverse, rotation);
            }
        }

        // Builds a game-coord polyline along the rails between two points by A* over
        // the track-node graph (edge weight = segment length). null if disconnected.
        private static List<Vector3> BuildTrackRoute(Vector3 fromGamePos, Vector3 toGamePos)
        {
            var graph = Graph.Shared;
            if (graph == null)
            {
                return null;
            }

            var start = NearestNode(graph, fromGamePos);
            var goal = NearestNode(graph, toGamePos);
            if (start == null || goal == null)
            {
                return null;
            }

            // Sanity log: if these distances are large, node positions and station
            // CenterPoints are in different coordinate spaces (would need adjusting).
            FuseLog.Info(
                $"FUSE benchmark corridor A*: nearest node {(NodePos(start) - fromGamePos).magnitude:0}m from {CorridorFromName}, " +
                $"{(NodePos(goal) - toGamePos).magnitude:0}m from {CorridorToName}.");

            var nodePath = AStarNodePath(graph, start, goal);
            if (nodePath == null)
            {
                return null;
            }

            var points = new List<Vector3>(nodePath.Count + 2) { fromGamePos };
            foreach (var node in nodePath)
            {
                points.Add(NodePos(node));
            }

            points.Add(toGamePos);
            return points;
        }

        private static TrackNode NearestNode(Graph graph, Vector3 gamePos)
        {
            TrackNode best = null;
            var bestSqr = float.MaxValue;
            foreach (var node in graph.Nodes)
            {
                if (node == null)
                {
                    continue;
                }

                var sqr = (NodePos(node) - gamePos).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = node;
                }
            }

            return best;
        }

        private static Vector3 NodePos(TrackNode node) => node.transform.localPosition;

        // Standard A*: segments are edges weighted by length, heuristic is the
        // straight-line distance to the goal node.
        private static List<TrackNode> AStarNodePath(Graph graph, TrackNode start, TrackNode goal)
        {
            var goalPos = NodePos(goal);
            var open = new List<TrackNode> { start };
            var cameFrom = new Dictionary<TrackNode, TrackNode>();
            var gScore = new Dictionary<TrackNode, float> { [start] = 0f };
            var fScore = new Dictionary<TrackNode, float> { [start] = (NodePos(start) - goalPos).magnitude };
            var closed = new HashSet<TrackNode>();
            var guard = 0;

            while (open.Count > 0 && guard++ < 500000)
            {
                var currentIndex = 0;
                var current = open[0];
                for (var i = 1; i < open.Count; i++)
                {
                    if (Score(fScore, open[i]) < Score(fScore, current))
                    {
                        current = open[i];
                        currentIndex = i;
                    }
                }

                if (current == goal)
                {
                    return Reconstruct(cameFrom, current);
                }

                open.RemoveAt(currentIndex);
                closed.Add(current);

                foreach (var segment in graph.SegmentsConnectedTo(current))
                {
                    if (segment == null)
                    {
                        continue;
                    }

                    var neighbor = segment.GetOtherNode(current);
                    if (neighbor == null || neighbor == current || closed.Contains(neighbor))
                    {
                        continue;
                    }

                    var tentative = Score(gScore, current) + Mathf.Max(segment.GetLength(), 0.01f);
                    if (tentative < Score(gScore, neighbor))
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentative;
                        fScore[neighbor] = tentative + (NodePos(neighbor) - goalPos).magnitude;
                        if (!open.Contains(neighbor))
                        {
                            open.Add(neighbor);
                        }
                    }
                }
            }

            return null;
        }

        private static float Score(Dictionary<TrackNode, float> scores, TrackNode node) =>
            scores.TryGetValue(node, out var value) ? value : float.MaxValue;

        private static List<TrackNode> Reconstruct(Dictionary<TrackNode, TrackNode> cameFrom, TrackNode current)
        {
            var path = new List<TrackNode> { current };
            while (cameFrom.TryGetValue(current, out var previous))
            {
                current = previous;
                path.Add(current);
            }

            path.Reverse();
            return path;
        }

        // Glides the camera along a game-coord polyline at PaceMetersPerSec.
        private static IEnumerator DrivePolyline(List<Vector3> points, Quaternion rotation)
        {
            var legStart = Time.realtimeSinceStartup;
            for (var i = 0; i < points.Count - 1; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                var length = Vector3.Distance(a, b);
                if (length < 0.01f)
                {
                    continue;
                }

                var traveled = 0f;
                while (traveled < length)
                {
                    traveled += PaceMetersPerSec * Time.deltaTime;
                    MoveStrategy(Vector3.Lerp(a, b, Mathf.Clamp01(traveled / length)), rotation);
                    if (Time.realtimeSinceStartup - legStart > LegTimeoutSeconds)
                    {
                        yield break;
                    }

                    yield return null;
                }
            }
        }

        private static PassengerStop ResolveStop(string name)
        {
            try
            {
                foreach (var stop in PassengerStop.FindAll())
                {
                    if (stop == null)
                    {
                        continue;
                    }

                    var timetable = stop.TimetableName;
                    var id = stop.identifier;
                    if ((timetable != null && timetable.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (id != null && id.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return stop;
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE benchmark could not enumerate passenger stops", ex);
            }

            return null;
        }

        // ---- Camera ----

        // Real teleport (selects the Strategy camera + loads), for jumps.
        private static void Teleport(Vector3 gamePosition, Quaternion rotation)
        {
            try
            {
                CameraSelector.shared?.JumpToPoint(gamePosition, rotation, CameraSelector.CameraIdentifier.Strategy);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE benchmark teleport failed", ex);
            }
        }

        // Instant per-frame move of the already-selected Strategy camera, for sweeps
        // and track drives.
        private static void MoveStrategy(Vector3 gamePosition, Quaternion rotation)
        {
            try
            {
                CameraSelector.shared?.MoveStrategyToPoint(WorldTransformer.GameToWorld(gamePosition), rotation);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE benchmark camera move failed", ex);
            }
        }

        // ---- Settle detection ----

        private static IEnumerator WaitForLoadAndSettle(float timeoutSeconds, Action<int> onPoll)
        {
            var start = Time.realtimeSinceStartup;
            var loadingStarted = false;
            var firstZeroTime = -1f;
            while (Time.realtimeSinceStartup - start < timeoutSeconds)
            {
                var inFlight = CountInFlight();
                onPoll?.Invoke(inFlight);

                if (inFlight > 0)
                {
                    loadingStarted = true;
                    firstZeroTime = -1f;
                }
                else if (loadingStarted)
                {
                    if (firstZeroTime < 0f)
                    {
                        firstZeroTime = Time.realtimeSinceStartup;
                    }

                    if (Time.realtimeSinceStartup - firstZeroTime >= SettleConfirmSeconds)
                    {
                        yield break;
                    }
                }

                yield return null;
            }
        }

        private static int CountInFlight()
        {
            var instances = UnityEngine.Object.FindObjectsOfType<SceneryAssetInstance>();
            var count = 0;
            foreach (var instance in instances)
            {
                if (instance == null)
                {
                    continue;
                }

                if (!LoadState.GetWantsLoaded(instance))
                {
                    continue;
                }

                if (LoadState.GetModel(instance) == null)
                {
                    count++; // wants to be loaded but the model isn't present yet
                }
            }

            return count;
        }

        // ---- Output ----

        private static string ResultsPath => Path.Combine(Application.persistentDataPath, "FUSE-scenery-benchmark.json");

        private static void AppendResult(JObject result)
        {
            try
            {
                var history = File.Exists(ResultsPath)
                    ? JArray.Parse(File.ReadAllText(ResultsPath))
                    : new JArray();
                history.Add(result);
                File.WriteAllText(ResultsPath, history.ToString(Formatting.Indented));
                FuseLog.Info($"FUSE benchmark result appended to '{ResultsPath}'.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE benchmark could not write results", ex);
            }
        }

        private static string BuildConfiguration
        {
            get
            {
#if DEBUG
                return "Debug";
#else
                return "Release";
#endif
            }
        }

        // Small reflection holder for SceneryAssetInstance's private load-state fields
        // (used to count in-flight loads). Cached once.
        private sealed class SceneryLoadStateReader
        {
            private readonly System.Reflection.FieldInfo _wantsLoaded =
                typeof(SceneryAssetInstance).GetField("_wantsLoaded", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            private readonly System.Reflection.FieldInfo _model =
                typeof(SceneryAssetInstance).GetField("_model", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            public bool Available => _wantsLoaded != null && _model != null;

            public bool GetWantsLoaded(SceneryAssetInstance instance) => _wantsLoaded.GetValue(instance) is bool b && b;

            public GameObject GetModel(SceneryAssetInstance instance) => _model.GetValue(instance) as GameObject;
        }

        // Parallel per-frame sampler that writes a time-series performance CSV for a
        // run: FPS, frame time, scenery in-flight/loaded counts, churn deltas, sampled
        // per-object load latency, and memory — tagged with the current phase. Runs as
        // its own coroutine so it keeps sampling even while the movement is in a
        // nested wait. Sampling is throttled to 4 Hz to limit its own overhead.
        private sealed class RunSampler
        {
            private const float SampleIntervalSeconds = 0.25f;

            private readonly string _fileName;
            private readonly float _startTime;
            private readonly Dictionary<int, float> _loadStart = new Dictionary<int, float>();
            private StreamWriter _writer;
            private bool _active = true;
            private float _lastSampleTime;
            private int _framesSinceSample;
            private long _lastLoads;
            private long _lastUnloads;
            private long _lastSuppressed;
            private long _lastDeferred;
            private long _lastReleased;
            private float _fpsSum;
            private float _fpsMin = float.MaxValue;
            private int _fpsCount;
            private float _runLatencySum;
            private int _runLatencyCount;
            private float _runMaxLoadMs;

            internal int PeakInFlight { get; private set; }
            internal float AvgFps => _fpsCount > 0 ? _fpsSum / _fpsCount : 0f;
            internal float MinFps => _fpsCount > 0 ? _fpsMin : 0f;
            // Run-level per-object load latency (load start -> model present), the key
            // batch-load-throughput signal the throttle targets.
            internal float AvgLoadMs => _runLatencyCount > 0 ? _runLatencySum / _runLatencyCount : 0f;
            internal float MaxLoadMs => _runMaxLoadMs;
            internal string FileName => _fileName;

            internal RunSampler(string scenario, string label)
            {
                _startTime = Time.realtimeSinceStartup;
                _lastSampleTime = _startTime;
                _fileName = $"FUSE-bench-{Sanitize(scenario)}-{Sanitize(label)}-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
                try
                {
                    var path = Path.Combine(Application.persistentDataPath, _fileName);
                    _writer = new StreamWriter(path, false) { AutoFlush = true };
                    _writer.WriteLine(
                        "elapsedSec,fps,frameMs,inFlight,loaded,loadsDelta,unloadsDelta,suppressedDelta," +
                        "deferredDelta,releasedDelta,queueDepth,loadsCompleted,avgLoadMs,maxLoadMs,managedMB,unityMB,phase");
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE benchmark could not open CSV", ex);
                    _writer = null;
                }
            }

            internal IEnumerator Loop()
            {
                while (_active)
                {
                    try
                    {
                        Frame();
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Exception("FUSE benchmark sampler frame failed", ex);
                    }

                    yield return null;
                }
            }

            internal void Finish()
            {
                _active = false;
                try
                {
                    _writer?.Flush();
                    _writer?.Dispose();
                }
                catch
                {
                }

                _writer = null;
            }

            // Resets the per-run latency/load aggregates so a scenario can measure only
            // the phase after a boundary (the sweep's post-cold-load oscillation), not
            // the one-time cold load. CSV rows keep streaming; only the run-level
            // PeakInFlight / Avg / MaxLoadMs reported in the JSON summary restart.
            internal void ResetLatencyWindow()
            {
                _runLatencySum = 0f;
                _runLatencyCount = 0;
                _runMaxLoadMs = 0f;
                _loadStart.Clear();
                PeakInFlight = 0;

                // Rebase the per-sample delta baselines too: the caller (SweepMovement)
                // zeroes the global churn/throttle counters at this same boundary, so
                // without this the next CSV row would report large NEGATIVE deltas
                // (small post-reset counter minus the stale pre-reset baseline).
                _lastLoads = FuseSceneryCullingDiagnosticPatch.FuseLoads + FuseSceneryCullingDiagnosticPatch.VanillaLoads;
                _lastUnloads = FuseSceneryCullingDiagnosticPatch.FuseUnloads + FuseSceneryCullingDiagnosticPatch.VanillaUnloads;
                _lastSuppressed = FuseSceneryCullingDebouncePatch.SuppressedUnloads;
                _lastDeferred = FuseSceneryLoadThrottlePatch.DeferredLoads;
                _lastReleased = FuseSceneryLoadThrottlePatch.ReleasedLoads;
            }

            private void Frame()
            {
                _framesSinceSample++;
                var now = Time.realtimeSinceStartup;
                if (now - _lastSampleTime < SampleIntervalSeconds)
                {
                    return;
                }

                var intervalElapsed = Mathf.Max(now - _lastSampleTime, 0.0001f);
                var fps = _framesSinceSample / intervalElapsed;
                var frameMs = 1000f / Mathf.Max(fps, 0.01f);
                _fpsSum += fps;
                _fpsCount++;
                if (fps < _fpsMin)
                {
                    _fpsMin = fps;
                }

                int inFlight = 0, loaded = 0, completed = 0;
                float latencySum = 0f, latencyMax = 0f;
                var instances = UnityEngine.Object.FindObjectsOfType<SceneryAssetInstance>();
                foreach (var instance in instances)
                {
                    if (instance == null || !LoadState.GetWantsLoaded(instance))
                    {
                        continue;
                    }

                    var id = instance.GetInstanceID();
                    if (LoadState.GetModel(instance) == null)
                    {
                        inFlight++;
                        if (!_loadStart.ContainsKey(id))
                        {
                            _loadStart[id] = now;
                        }
                    }
                    else
                    {
                        loaded++;
                        if (_loadStart.TryGetValue(id, out var startedAt))
                        {
                            var latency = (now - startedAt) * 1000f;
                            latencySum += latency;
                            if (latency > latencyMax)
                            {
                                latencyMax = latency;
                            }

                            completed++;
                            _loadStart.Remove(id);
                        }
                    }
                }

                if (inFlight > PeakInFlight)
                {
                    PeakInFlight = inFlight;
                }

                // Run-level latency aggregates feed the JSON summary / throttle A/B.
                _runLatencySum += latencySum;
                _runLatencyCount += completed;
                if (latencyMax > _runMaxLoadMs)
                {
                    _runMaxLoadMs = latencyMax;
                }

                var totalLoads = FuseSceneryCullingDiagnosticPatch.FuseLoads + FuseSceneryCullingDiagnosticPatch.VanillaLoads;
                var totalUnloads = FuseSceneryCullingDiagnosticPatch.FuseUnloads + FuseSceneryCullingDiagnosticPatch.VanillaUnloads;
                var suppressed = FuseSceneryCullingDebouncePatch.SuppressedUnloads;
                var deferred = FuseSceneryLoadThrottlePatch.DeferredLoads;
                var released = FuseSceneryLoadThrottlePatch.ReleasedLoads;
                var loadsDelta = totalLoads - _lastLoads;
                var unloadsDelta = totalUnloads - _lastUnloads;
                var suppressedDelta = suppressed - _lastSuppressed;
                var deferredDelta = deferred - _lastDeferred;
                var releasedDelta = released - _lastReleased;
                _lastLoads = totalLoads;
                _lastUnloads = totalUnloads;
                _lastSuppressed = suppressed;
                _lastDeferred = deferred;
                _lastReleased = released;

                var managedMb = GC.GetTotalMemory(false) / 1048576f;
                var unityMb = SafeUnityMemory() / 1048576f;
                var avgLoadMs = completed > 0 ? latencySum / completed : 0f;

                _writer?.WriteLine(
                    $"{now - _startTime:0.00},{fps:0.0},{frameMs:0.0},{inFlight},{loaded}," +
                    $"{loadsDelta},{unloadsDelta},{suppressedDelta},{deferredDelta},{releasedDelta}," +
                    $"{FuseSceneryLoadThrottlePatch.QueueDepth},{completed},{avgLoadMs:0},{latencyMax:0}," +
                    $"{managedMb:0.0},{unityMb:0.0},\"{CsvPhase()}\"");

                _lastSampleTime = now;
                _framesSinceSample = 0;
            }

            private static long SafeUnityMemory()
            {
                try
                {
                    return Profiler.GetTotalAllocatedMemoryLong();
                }
                catch
                {
                    return 0L;
                }
            }

            private static string CsvPhase()
            {
                var status = _status ?? string.Empty;
                return status.Replace('"', '\'').Replace('\n', ' ').Replace('\r', ' ');
            }

            private static string Sanitize(string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return "x";
                }

                var chars = value.ToCharArray();
                for (var i = 0; i < chars.Length; i++)
                {
                    var c = chars[i];
                    if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                    {
                        chars[i] = '-';
                    }
                }

                return new string(chars);
            }
        }

        // Persistent host so the benchmark coroutine survives the Health panel being
        // rebuilt or closed mid-run.
        private sealed class BenchmarkRunner : MonoBehaviour
        {
            private static BenchmarkRunner _instance;

            internal static BenchmarkRunner Instance
            {
                get
                {
                    if (_instance == null)
                    {
                        var host = new GameObject("FUSE.SceneryBenchmark");
                        DontDestroyOnLoad(host);
                        _instance = host.AddComponent<BenchmarkRunner>();
                    }

                    return _instance;
                }
            }
        }
    }
}
