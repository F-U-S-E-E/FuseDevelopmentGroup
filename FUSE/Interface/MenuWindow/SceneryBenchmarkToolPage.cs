using FUSE.Infrastructure;
using System;
using UI.Builder;
using UI.Common;
using static FUSE.Interface.InterfaceUtils;

namespace FUSE.Interface.MenuWindow
{
    internal struct SceneryBenchmarkToolPage
    {
        public static void Build(UIPanelBuilder builder)
        {
            builder.AddTitle("Scenery Load Benchmark", "");

            builder.AddLabel("Reproducible culling/streaming tests (issue #76). Be in the overview camera with a map loaded.");

            builder.Spacer(8f);

            builder.AddSection("Scenarios");
            AddWrappedLabel(
                builder,
                "CORRIDOR teleports between Bryson and Sylva a few times, then drives the camera up and down the " +
                "track between them at a set pace. SWEEP is the quick local test (oscillates across the cull " +
                "boundary at your current view).",
                64f);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Run Corridor", () => StartRun(builder, "corridor", FuseSceneryBenchmark.RunCorridor));
                row.AddButtonCompact("Run Sweep", () => StartRun(builder, "sweep", FuseSceneryBenchmark.RunSweep));
            }, 6f).Height(32f);

            builder.Spacer(8f);

            builder.AddSection("Throttle A/B");
            AddWrappedLabel(
                builder,
                "Each A/B run plays the scenario twice, toggling the per-frame load cap off vs on (batch-load " +
                "stall) — compare minFps and maxLoadMs between the passes.",
                48f);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Corridor Throttle A/B", () => StartRun(builder, "corridor throttle A/B", FuseSceneryBenchmark.RunCorridorThrottleAb));
                row.AddButtonCompact("Sweep Throttle A/B", () => StartRun(builder, "sweep throttle A/B", FuseSceneryBenchmark.RunSweepThrottleAb));
            }, 6f).Height(32f);

            builder.Spacer(8f);

            builder.AddSection("Status");
            builder.AddField("Benchmark", () => FuseSceneryBenchmark.Status, UIPanelBuilder.Frequency.Fast).Height(26f);
            AddWrappedLabel(
                builder,
                "Each run appends a summary to FUSE-scenery-benchmark.json and writes a per-frame CSV " +
                "(FUSE-bench-*.csv: FPS, object counts, churn, defer/release, load latency, memory); live " +
                "progress prints to FUSE.log.",
                64f);
        }

        private static void StartRun(UIPanelBuilder builder, string scenarioName, Func<string> start)
        {
            string message;
            try
            {
                message = start();
            }
            catch (Exception ex)
            {
                message = $"FUSE {scenarioName} benchmark failed to start: {ex.GetBaseException().Message}";
                FuseLog.Exception($"FUSE tools page benchmark start failed scenario='{scenarioName}'", ex);
            }

            Toast.Present(message);
            builder.Rebuild();
        }
    }
}
