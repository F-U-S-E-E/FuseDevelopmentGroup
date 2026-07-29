using System.Collections.Generic;
using FUSE.Profiler.Instrumentation;

namespace FUSE.Profiler.Entries
{
    /// <summary>
    /// The built-in Railroader profiling categories. Every target is a
    /// name-resolved spec so a game update that renames something degrades to
    /// a visible "target failed" row instead of a load failure; the
    /// FUSE.Tests resolution canaries assert the whole table against the
    /// installed game so drift is caught at build time.
    /// </summary>
    internal static class RailroaderEntries
    {
        internal static IEnumerable<ProfilerEntry> CreateBuiltIns()
        {
            // Note: the whole-step time (TrainController.FixedUpdate) is
            // measured by the sim-tick clock driver itself, so it is not a
            // target here — instrumenting the boundary method would race the
            // cycle close on patch-priority ties.
            yield return new ProfilerEntry(
                "physics.sim",
                "Train physics step",
                ProfilerCategory.Physics,
                () => new[]
                {
                    new TargetSpec("Model.Physics.IntegrationSet:Tick"),
                    new TargetSpec("Model.Physics.CarAirSystem:FixedUpdateAir"),
                },
                "The fixed-step train simulation: integration sets and per-car air. Buckets are per physics step, not per frame.");

            yield return new ProfilerEntry(
                "culling.managers",
                "Culling managers",
                ProfilerCategory.Culling,
                () => new[]
                {
                    new TargetSpec("Helpers.Culling.CullingManager:Update"),
                    new TargetSpec("Helpers.Culling.CullingManager:FixedUpdate"),
                    new TargetSpec("Helpers.Culling.CullingManager:CullingGroupStateChanged"),
                },
                "The shared sphere-culling managers (hoses, bridges, CTC, signals, scenery, flares).");

            yield return new ProfilerEntry(
                "culling.decals",
                "Decal culling",
                ProfilerCategory.Culling,
                () => new[]
                {
                    new TargetSpec("Effects.Decals.DecalCullingManager:Update"),
                    new TargetSpec("Effects.Decals.DecalCullingManager:FixedUpdate"),
                    new TargetSpec("Effects.Decals.DecalCullingManager:UpdateDecalVisibilityJob"),
                    new TargetSpec("Effects.Decals.DecalCullingManager:UpdateScreenSizeThreshold"),
                },
                "Car-lettering decal visibility: the per-frame Burst job schedule and threshold updates.");

            yield return new ProfilerEntry(
                "scenery.streaming",
                "Scenery streaming",
                ProfilerCategory.Scenery,
                () => new[]
                {
                    new TargetSpec("Helpers.SceneryAssetManager:LoadScenery", label: "SceneryAssetManager.LoadScenery (sync part)"),
                    new TargetSpec("Helpers.SceneryAssetInstance:SetLoaded", label: "SceneryAssetInstance.SetLoaded (sync part)"),
                    new TargetSpec("Helpers.SceneryAssetInstance:CullingSphereStateChanged"),
                    new TargetSpec("Helpers.SceneryAssetInstance:SetupComponents"),
                },
                "Scenery load/unload flips and component setup. The async load bodies count only their synchronous slice.");

            yield return new ProfilerEntry(
                "scenery.world",
                "World streaming",
                ProfilerCategory.Scenery,
                () => new[]
                {
                    new TargetSpec("WorldStreamer2.Streamer:Update"),
                },
                "Terrain/world tile streaming.");

            yield return new ProfilerEntry(
                "track.rebuild",
                "Track rebuilds",
                ProfilerCategory.Track,
                () => new[]
                {
                    new TargetSpec("Track.TrackObjectManager:Rebuild"),
                    new TargetSpec("Track.TrackObjectManager:RebuildCoroutine", coroutine: true),
                    new TargetSpec("Track.TrackRebuilder:Update"),
                    new TargetSpec("Track.TrackRebuilder:WorkBuildQueue"),
                    new TargetSpec("Track.TrackRebuilder:WorkDestroyQueue"),
                    new TargetSpec("Track.Graph:RebuildCollections"),
                },
                "Track descriptor rebuilds and the incremental mesh build/destroy queues.");

            yield return new ProfilerEntry(
                "ops.controller",
                "Ops controller",
                ProfilerCategory.Operations,
                () => new[]
                {
                    new TargetSpec("Model.Ops.OpsController:PeriodicUpdate", coroutine: true),
                    new TargetSpec("Model.Ops.OpsController:RebuildPopulations"),
                    new TargetSpec("Model.Ops.OpsController:CheckWaybills"),
                    new TargetSpec("Model.Ops.OpsController:CheckLoads"),
                    new TargetSpec("Model.Ops.OpsController:RebuildCollections"),
                },
                "Industry population/waybill sweeps.");

            yield return new ProfilerEntry(
                "ops.passengers",
                "Passenger stops",
                ProfilerCategory.Operations,
                () => new[]
                {
                    new TargetSpec("Model.Ops.PassengerStop:Loop", coroutine: true),
                    new TargetSpec("Model.Ops.PassengerStop:WorkCar", coroutine: true),
                    new TargetSpec("Model.Ops.PassengerStop:UnloadCar"),
                    new TargetSpec("Model.Ops.PassengerStop:LoadCar"),
                    new TargetSpec("Model.Ops.PassengerStop:GrowWaiting"),
                },
                "Passenger stop work loops (the family behind stop ping-pong storms).");

            yield return new ProfilerEntry(
                "ops.autoengineer",
                "Auto engineer",
                ProfilerCategory.Operations,
                () => new[]
                {
                    new TargetSpec("Model.AI.AutoEngineerPlanner:Loop", coroutine: true),
                    new TargetSpec("Model.AI.AutoEngineerPlanner:RouteLoop", coroutine: true),
                    new TargetSpec("Model.AI.AutoEngineerPlanner:UpdateTargets"),
                    new TargetSpec("Model.AI.AutoEngineerPlanner:ApplyMovement"),
                },
                "AI crew planning and movement application.");

            yield return new ProfilerEntry(
                "ui.panels",
                "UI panel rebuilds",
                ProfilerCategory.UiEvents,
                () => new[]
                {
                    new TargetSpec("UI.Builder.UIPanel:Rebuild"),
                    new TargetSpec("UI.Builder.UIPanel:InvokeOnRebuild"),
                },
                "Full panel rebuilds — event-triggered UI churn shows up here.");

            yield return new ProfilerEntry(
                "keyvalue.dispatch",
                "KeyValue writes",
                ProfilerCategory.KeyValue,
                () => new[]
                {
                    new TargetSpec("KeyValue.Runtime.KeyValueObject:Set"),
                },
                "Networked property writes including their synchronous observer callbacks.");
        }
    }
}
