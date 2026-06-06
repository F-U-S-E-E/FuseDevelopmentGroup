using FUSE.Runtime.API;
using FUSE.Authoring.Data;
using FUSE.Loading;
using FUSE.Authoring.Serialization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Loading
{
    /// <summary>
    /// End-to-end integration tests that walk every link in the
    /// mandela pipeline from legacy JSON to apply-plan, so regressions
    /// that slip through the seams between layers (legacy converter,
    /// JSON deserialization, planner) still fail CI.
    ///
    /// The unit-level test suites in
    /// <see cref="FuseLegacyMandelaConverterTests"/>,
    /// <see cref="FUSE.Tests.Authoring.FuseConfigurableStructureEntityTests"/>,
    /// and <see cref="FUSE.Tests.API.FuseSceneCloneApplyPlannerTests"/>
    /// each pin one layer in isolation. This suite stitches them
    /// together so a refactor that breaks the contract BETWEEN
    /// layers — e.g. the converter outputting a JSON shape the
    /// serializer can't read into <see cref="FuseSceneClone"/>, or
    /// the serializer reading a field the planner doesn't honour —
    /// is caught by an assertion at the actual decision boundary the
    /// executor walks.
    /// </summary>
    public class FuseMandelaEndToEndTests
    {
        private const string Target = "World/Large Scenery/Bryson/Freight House";

        private static FuseSceneClone ConvertAndDeserialize(JObject mandelas)
        {
            var manifest = new FuseLegacyPackageManifest
            {
                PackageId = "test-pkg",
                DisplayName = "Test Package",
                Author = "tester",
                Version = "1.0.0"
            };
            var root = FuseLegacyDataConverter.CreateSkeleton(manifest, "mandela-fragment");
            FuseLegacyDataConverter.ConvertSource(
                new JObject { ["mandelas"] = mandelas },
                root,
                manifest);

            // The legacy converter produces a JObject in the FUSE
            // canonical shape. Round-trip it through the FUSE
            // serializer to land at the same FuseSceneClone the
            // production apply path will see.
            var definition = FuseSerializer.FromJson(root.ToString());
            Assert.True(definition.World.SceneClones.ContainsKey(Target),
                "converter must produce a sceneClone entry at the requested path");
            return definition.World.SceneClones[Target];
        }

        [Fact]
        public void NullBrysonShape_EnabledTrueOnly_ProducesNoOpPlanForVanillaTarget()
        {
            // The exact mandela shape Stryker's nullBryson.json uses
            // for the Freight House. End-to-end: this MUST come out
            // of the planner with no transform overrides and no
            // clone-from-source. If any layer regresses, the apply
            // path will teleport the vanilla building.
            var def = ConvertAndDeserialize(new JObject
            {
                [Target] = new JObject { ["enabled"] = true }
            });

            Assert.Equal(Target, def.TargetPath);
            Assert.True(def.Enabled);
            Assert.False(def.LocalPosition.HasValue);
            Assert.False(def.LocalRotation.HasValue);
            Assert.False(def.LocalScale.HasValue);
            // Source may be null or empty depending on serializer
            // handling; both must read as "no source" to the
            // downstream planner.
            Assert.True(string.IsNullOrWhiteSpace(def.Source));

            var plan = FuseSceneCloneApplyPlanner.Compute(def, existingTargetFound: true);

            Assert.False(plan.CloneFromSource);
            Assert.False(plan.DestroyExistingTarget);
            Assert.False(plan.OverrideLocalPosition.HasValue);
            Assert.False(plan.OverrideLocalRotation.HasValue);
            Assert.False(plan.OverrideLocalScale.HasValue);
            Assert.True(plan.SetActiveState.HasValue);
            Assert.True(plan.SetActiveState.Value);
        }

        [Fact]
        public void AuthoredPosition_FlowsAllTheWayToPlan()
        {
            // The other direction: an author who DOES supply a
            // position via JSON must see it land in the plan
            // unchanged. If the legacy converter or serializer drops
            // it, this test catches it.
            var authored = new UnityEngine.Vector3(123.4f, 56.7f, -89f);
            var def = ConvertAndDeserialize(new JObject
            {
                [Target] = new JObject
                {
                    ["enabled"] = true,
                    ["localPosition"] = new JObject
                    {
                        ["x"] = authored.x,
                        ["y"] = authored.y,
                        ["z"] = authored.z
                    }
                }
            });

            Assert.True(def.LocalPosition.HasValue);
            Assert.Equal(authored.x, def.LocalPosition.Value.x, precision: 3);
            Assert.Equal(authored.y, def.LocalPosition.Value.y, precision: 3);
            Assert.Equal(authored.z, def.LocalPosition.Value.z, precision: 3);

            var plan = FuseSceneCloneApplyPlanner.Compute(def, existingTargetFound: true);

            Assert.True(plan.OverrideLocalPosition.HasValue);
            Assert.Equal(authored.x, plan.OverrideLocalPosition.Value.x, precision: 3);
            Assert.Equal(authored.y, plan.OverrideLocalPosition.Value.y, precision: 3);
            Assert.Equal(authored.z, plan.OverrideLocalPosition.Value.z, precision: 3);
        }

        [Fact]
        public void InstantiateFromShape_FlowsAllTheWayToClonePlan()
        {
            // The third mandela mode — instantiate from a source
            // prefab. End-to-end check that the source path's
            // scheme prefix survives the converter, the serializer,
            // and arrives at the planner as "clone from source".
            var def = ConvertAndDeserialize(new JObject
            {
                [Target] = new JObject
                {
                    ["enabled"] = true,
                    ["instantiateFrom"] = "World/Large Scenery/Dillsboro/Freight House"
                }
            });

            Assert.Equal(
                "path://scene/World/Large Scenery/Dillsboro/Freight House",
                def.Source);

            var plan = FuseSceneCloneApplyPlanner.Compute(def, existingTargetFound: true);

            Assert.True(plan.CloneFromSource);
            Assert.True(plan.DestroyExistingTarget);
            Assert.True(plan.RunPrefabSanitizer);
            Assert.True(plan.ForceRenderable);
        }

        [Fact]
        public void EnabledFalseSuppression_DoesNotEvenReachThePlanner()
        {
            // A pure-suppression mandela should NOT land in
            // world.sceneClones at all — it goes into
            // world.suppressBaseScenePaths instead. The planner is
            // never invoked for it. This pins the dispatch decision
            // end-to-end.
            var manifest = new FuseLegacyPackageManifest
            {
                PackageId = "test-pkg",
                DisplayName = "Test Package",
                Author = "tester",
                Version = "1.0.0"
            };
            var root = FuseLegacyDataConverter.CreateSkeleton(manifest, "mandela-fragment");
            FuseLegacyDataConverter.ConvertSource(
                new JObject
                {
                    ["mandelas"] = new JObject
                    {
                        [Target] = new JObject { ["enabled"] = false }
                    }
                },
                root,
                manifest);

            var definition = FuseSerializer.FromJson(root.ToString());

            Assert.False(definition.World.SceneClones.ContainsKey(Target),
                "an enabled:false (no source) mandela must NOT be a sceneClone");
            Assert.Contains(Target, definition.World.SuppressBaseScenePaths);
        }
    }
}
