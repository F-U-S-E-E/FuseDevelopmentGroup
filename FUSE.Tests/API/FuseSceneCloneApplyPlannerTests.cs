using FUSE.Runtime.API;
using FUSE.Authoring.Data;
using UnityEngine;
using Xunit;

namespace FUSE.Tests.API
{
    /// <summary>
    /// Pins every decision <see cref="FuseSceneCloneApplyPlanner.Compute"/>
    /// makes for every shape of <see cref="FuseSceneClone"/> the legacy
    /// converter and authoring pipeline can produce. The executor in
    /// <see cref="SceneCloneAPI"/> walks the resulting plan verbatim, so
    /// any plan computed wrong here cascades into the live transform —
    /// which is how the Bryson Freight House zeroing regression
    /// (<c>{ "enabled": true }</c> on a vanilla scenery path silently
    /// teleporting the building to its parent's origin) shipped without a
    /// failing test.
    ///
    /// Every assertion in this file maps to a contract the apply path
    /// MUST honour:
    ///
    /// <list type="bullet">
    ///   <item><description>Source omitted → never destroy the existing
    ///   target, never strip components, never force renderers, never
    ///   sanitize. Bind to whatever's at the path and leave it alone.</description></item>
    ///   <item><description>Source supplied → destroy any existing
    ///   target, instantiate, reset its local transform to identity
    ///   (so authoring positions are authoritative), then apply
    ///   overrides.</description></item>
    ///   <item><description>OverrideLocal* nullables: HasValue==null
    ///   means the executor MUST NOT touch that axis. The default
    ///   <c>Vector3.zero</c> is NEVER an implicit override.</description></item>
    ///   <item><description>Enabled==false: skip post-bind renderer
    ///   validation (don't log spurious "no renderers" warnings against
    ///   a deliberately hidden target) and skip ForceRenderable.</description></item>
    /// </list>
    /// </summary>
    public class FuseSceneCloneApplyPlannerTests
    {
        private const string Target = "World/Large Scenery/Bryson/Freight House";

        public class NullDefinition
        {
            [Fact]
            public void NullIn_NoOpPlanOut()
            {
                // Defensive: if a future caller forgets to null-check
                // before invoking the planner, we want a no-op plan
                // rather than an NRE that crashes the apply pipeline.
                var plan = FuseSceneCloneApplyPlanner.Compute(null, existingTargetFound: true);

                Assert.False(plan.CloneFromSource);
                Assert.False(plan.DestroyExistingTarget);
                Assert.False(plan.EnsureParentChainExists);
                Assert.False(plan.ZeroLocalTransformBeforeOverride);
                Assert.False(plan.RunPrefabSanitizer);
                Assert.False(plan.StripUnsupportedRuntimeComponents);
                Assert.False(plan.ForceRenderable);
                Assert.False(plan.RunPostBindValidation);
                Assert.False(plan.OverrideLocalPosition.HasValue);
                Assert.False(plan.OverrideLocalRotation.HasValue);
                Assert.False(plan.OverrideLocalScale.HasValue);
                Assert.False(plan.SetActiveState.HasValue);
            }
        }

        public class EnabledOnlyMandela_NoSource_NoOverrides
        {
            // This is the exact shape that bit us with the Bryson
            // Freight House — `{ "enabled": true }` on a vanilla scene
            // path. The plan MUST leave the live transform untouched.

            private static FuseSceneCloneApplyPlanner.Plan Plan(bool? enabled) =>
                FuseSceneCloneApplyPlanner.Compute(
                    new FuseSceneClone
                    {
                        TargetPath = Target,
                        Enabled = enabled
                    },
                    existingTargetFound: true);

            [Fact]
            public void EnabledTrue_NoCloning_NoOverrides()
            {
                var plan = Plan(true);

                Assert.False(plan.CloneFromSource);
                Assert.False(plan.DestroyExistingTarget);
                Assert.False(plan.EnsureParentChainExists);
                Assert.False(plan.ZeroLocalTransformBeforeOverride);
                Assert.False(plan.RunPrefabSanitizer);
                Assert.False(plan.StripUnsupportedRuntimeComponents);
                Assert.False(plan.ForceRenderable);
            }

            [Fact]
            public void EnabledTrue_TransformOverridesAllNull()
            {
                // The critical Freight House contract: no source AND no
                // localPosition in the JSON means the executor MUST
                // NOT call transform.localPosition = anything.
                var plan = Plan(true);

                Assert.False(
                    plan.OverrideLocalPosition.HasValue,
                    "An enabled-only mandela must never inject a phantom position override; doing so teleports vanilla scenery to its parent's origin.");
                Assert.False(plan.OverrideLocalRotation.HasValue);
                Assert.False(plan.OverrideLocalScale.HasValue);
            }

            [Fact]
            public void EnabledTrue_SetActiveTrue()
            {
                var plan = Plan(true);

                Assert.True(plan.SetActiveState.HasValue);
                Assert.True(plan.SetActiveState.Value);
            }

            [Fact]
            public void EnabledTrue_PostBindValidationRuns()
            {
                // For visible targets we DO want the renderer-presence
                // warning — that's how we noticed the empty placeholder
                // in the first place.
                Assert.True(Plan(true).RunPostBindValidation);
            }

            [Fact]
            public void EnabledFalse_SkipsPostBindValidation()
            {
                // For deliberately hidden targets we suppress the
                // post-bind warning so logs stay clean.
                Assert.False(Plan(false).RunPostBindValidation);
            }

            [Fact]
            public void EnabledFalse_SetActiveFalse()
            {
                var plan = Plan(false);

                Assert.True(plan.SetActiveState.HasValue);
                Assert.False(plan.SetActiveState.Value);
            }

            [Fact]
            public void EnabledFalse_StillNoTransformOverrides()
            {
                // Hiding a target also must not move it.
                var plan = Plan(false);

                Assert.False(plan.OverrideLocalPosition.HasValue);
                Assert.False(plan.OverrideLocalRotation.HasValue);
                Assert.False(plan.OverrideLocalScale.HasValue);
            }

            [Fact]
            public void EnabledUnset_LeavesActiveStateAlone()
            {
                // A mandela that omits "enabled" entirely is "don't
                // change activeness" — important for round-trips where
                // we just want to attach a marker without flipping
                // any state.
                Assert.False(Plan(null).SetActiveState.HasValue);
                // And without an explicit enabled:false, post-bind
                // validation still runs.
                Assert.True(Plan(null).RunPostBindValidation);
            }
        }

        public class EnabledOnlyMandela_NoSource_WithOverrides
        {
            [Fact]
            public void OnlyPositionAuthored_OnlyPositionInPlan()
            {
                // Author wants to nudge the existing GameObject — must
                // touch ONLY the axis they specified.
                var override_ = new Vector3(1.5f, 2.5f, -3.5f);
                var plan = FuseSceneCloneApplyPlanner.Compute(
                    new FuseSceneClone
                    {
                        TargetPath = Target,
                        Enabled = true,
                        LocalPosition = override_
                    },
                    existingTargetFound: true);

                Assert.True(plan.OverrideLocalPosition.HasValue);
                Assert.Equal(override_, plan.OverrideLocalPosition.Value);
                Assert.False(plan.OverrideLocalRotation.HasValue);
                Assert.False(plan.OverrideLocalScale.HasValue);
                Assert.False(plan.CloneFromSource);
            }

            [Fact]
            public void OnlyRotationAuthored_OnlyRotationInPlan()
            {
                var override_ = new Vector3(0f, 90f, 0f);
                var plan = FuseSceneCloneApplyPlanner.Compute(
                    new FuseSceneClone
                    {
                        TargetPath = Target,
                        Enabled = true,
                        LocalRotation = override_
                    },
                    existingTargetFound: true);

                Assert.False(plan.OverrideLocalPosition.HasValue);
                Assert.True(plan.OverrideLocalRotation.HasValue);
                Assert.Equal(override_, plan.OverrideLocalRotation.Value);
                Assert.False(plan.OverrideLocalScale.HasValue);
            }

            [Fact]
            public void OnlyScaleAuthored_OnlyScaleInPlan()
            {
                var override_ = new Vector3(2f, 2f, 2f);
                var plan = FuseSceneCloneApplyPlanner.Compute(
                    new FuseSceneClone
                    {
                        TargetPath = Target,
                        Enabled = true,
                        LocalScale = override_
                    },
                    existingTargetFound: true);

                Assert.False(plan.OverrideLocalPosition.HasValue);
                Assert.False(plan.OverrideLocalRotation.HasValue);
                Assert.True(plan.OverrideLocalScale.HasValue);
                Assert.Equal(override_, plan.OverrideLocalScale.Value);
            }

            [Fact]
            public void AuthoredZeroPosition_RoundTripsAsExplicit()
            {
                // An author who writes localPosition: { 0, 0, 0 } really
                // does want the executor to force origin alignment.
                // The planner must not collapse this back to "no override".
                var plan = FuseSceneCloneApplyPlanner.Compute(
                    new FuseSceneClone
                    {
                        TargetPath = Target,
                        Enabled = true,
                        LocalPosition = Vector3.zero
                    },
                    existingTargetFound: true);

                Assert.True(plan.OverrideLocalPosition.HasValue);
                Assert.Equal(Vector3.zero, plan.OverrideLocalPosition.Value);
            }
        }

        public class WithSource_ClonePathway
        {
            private static FuseSceneCloneApplyPlanner.Plan Plan(string source, bool? enabled, bool existing) =>
                FuseSceneCloneApplyPlanner.Compute(
                    new FuseSceneClone
                    {
                        TargetPath = Target,
                        Source = source,
                        Enabled = enabled
                    },
                    existingTargetFound: existing);

            [Fact]
            public void ValidSource_TriggersCloneAndAllItsSetup()
            {
                var plan = Plan("vanilla://brysonDepot", enabled: true, existing: true);

                Assert.True(plan.CloneFromSource);
                Assert.True(plan.DestroyExistingTarget);
                Assert.True(plan.EnsureParentChainExists);
                Assert.True(plan.ZeroLocalTransformBeforeOverride);
                Assert.True(plan.RunPrefabSanitizer);
                Assert.True(plan.StripUnsupportedRuntimeComponents);
                Assert.True(plan.ForceRenderable);
                Assert.True(plan.RunPostBindValidation);
            }

            [Fact]
            public void NoExistingTarget_StillClonesButSkipsDestroy()
            {
                // Replacing nothing is a valid use case — the apply path
                // walks the parent chain (creating intermediates) and
                // instantiates the source under it. There's just no
                // pre-existing GameObject to destroy.
                var plan = Plan("vanilla://brysonDepot", enabled: true, existing: false);

                Assert.True(plan.CloneFromSource);
                Assert.False(plan.DestroyExistingTarget);
                Assert.True(plan.EnsureParentChainExists);
            }

            [Fact]
            public void EnabledFalse_StillSetsUpClone_ButSkipsForceRenderable()
            {
                // An author who disables a brand-new clone is asking
                // for the GameObject to exist (so they can flip it on
                // later or so a downstream mod can reference it) but
                // not be visible right now. We still need to clone,
                // sanitize, and strip components — but it'd be wasted
                // work to force renderers visible on something the
                // SetActive(false) is about to disable.
                var plan = Plan("vanilla://brysonDepot", enabled: false, existing: true);

                Assert.True(plan.CloneFromSource);
                Assert.True(plan.DestroyExistingTarget);
                Assert.True(plan.RunPrefabSanitizer);
                Assert.True(plan.StripUnsupportedRuntimeComponents);
                Assert.False(plan.ForceRenderable);
                Assert.False(plan.RunPostBindValidation);
                Assert.True(plan.SetActiveState.HasValue);
                Assert.False(plan.SetActiveState.Value);
            }

            [Fact]
            public void EnabledUnset_AssumesVisibleClone()
            {
                // Omitting "enabled" on a sourced clone defaults to
                // visible (we still ForceRenderable, still
                // RunPostBindValidation). Skipping these would leave
                // a half-set-up clone in a weird state.
                var plan = Plan("vanilla://brysonDepot", enabled: null, existing: true);

                Assert.True(plan.ForceRenderable);
                Assert.True(plan.RunPostBindValidation);
                Assert.False(plan.SetActiveState.HasValue);
            }

            [Fact]
            public void WhitespaceSource_TreatedAsNoSource()
            {
                // A converter that emitted source: "" or source: "   "
                // by accident must NOT be promoted into a clone — the
                // apply path would then try to destroy the vanilla
                // target and instantiate a non-existent prefab.
                Assert.False(Plan("", enabled: true, existing: true).CloneFromSource);
                Assert.False(Plan("   ", enabled: true, existing: true).CloneFromSource);
                Assert.False(Plan(null, enabled: true, existing: true).CloneFromSource);
            }

            [Fact]
            public void WithSourceAndPositionOverride_BothExpressedInPlan()
            {
                // After Instantiate sets identity, the override fires
                // and places the clone where the author wanted it.
                var pos = new Vector3(100f, 200f, 300f);
                var plan = FuseSceneCloneApplyPlanner.Compute(
                    new FuseSceneClone
                    {
                        TargetPath = Target,
                        Source = "vanilla://brysonDepot",
                        Enabled = true,
                        LocalPosition = pos
                    },
                    existingTargetFound: true);

                Assert.True(plan.ZeroLocalTransformBeforeOverride);
                Assert.True(plan.OverrideLocalPosition.HasValue);
                Assert.Equal(pos, plan.OverrideLocalPosition.Value);
            }
        }
    }
}
