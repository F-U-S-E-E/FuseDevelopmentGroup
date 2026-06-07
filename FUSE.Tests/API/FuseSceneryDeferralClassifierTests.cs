using FUSE.Runtime.API;
using Xunit;

namespace FUSE.Tests.API
{
    /// <summary>
    /// Covers the pure component-type-name classification in
    /// <see cref="FuseSceneryDeferralClassifier"/>. The full <c>CanDefer</c> path
    /// resolves a typed <c>SceneryDefinition</c> from the game's
    /// <c>SceneryAssetManager</c> and is exercised in-game; these tests pin the
    /// decision logic that decides which component type names force eager activation
    /// (KeyValue/animation components, which register persistent state at activation;
    /// masks no longer force eager — they defer and are held resident by the cull
    /// debounce instead), and the fail-safe that treats an unknown/empty type name as
    /// eager-only.
    /// </summary>
    public class FuseSceneryDeferralClassifierTests
    {
        [Theory]
        // Mask components — matched on the "MapMask" fragment in the full type name,
        // so both the namespace and the class name participate.
        [InlineData("Model.Definition.Components.MapMasks.RectangleMapMaskComponent", true)]
        [InlineData("Model.Definition.Components.MapMasks.CircleMapMaskComponent", true)]
        [InlineData("Model.Definition.Components.MapMasks.CurveMapMaskComponent", true)]
        [InlineData("Some.Pack.MapMaskComponent", true)]
        // Case-insensitive.
        [InlineData("some.pack.mapmaskcomponent", true)]
        // Non-mask components.
        [InlineData("Model.Definition.Components.MeshComponent", false)]
        [InlineData("Model.Definition.Components.MaterialColorizerComponent", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsMaskTypeName_DetectsMaskComponents(string typeName, bool expected)
        {
            Assert.Equal(expected, FuseSceneryDeferralClassifier.IsMaskTypeName(typeName));
        }

        [Theory]
        // Mask components are NO LONGER forced eager: they defer like plain scenery (so
        // they activate against a live camera and stream correctly) and are kept resident
        // by the cull debounce instead. Only stateful scenery stays eager. (Masks are still
        // a mask-type-name — see IsMaskTypeName above — which is how they get tagged and
        // held resident; they just no longer gate deferral.)
        [InlineData("Model.Definition.Components.MapMasks.RectangleMapMaskComponent", false)]
        // Stateful components force eager activation (save-restore correctness):
        // KeyValue / animation register persistent property objects on activation.
        [InlineData("Some.Pack.KeyValueBoolAnimatorComponent", true)]
        [InlineData("Some.Pack.KeyValueComponent", true)]
        [InlineData("Some.Pack.AnimatorComponent", true)]
        [InlineData("Some.Pack.AnimationComponent", true)]
        // Case-insensitive.
        [InlineData("some.pack.animatorcomponent", true)]
        // Plain static / visual components are deferrable.
        [InlineData("Model.Definition.Components.MeshComponent", false)]
        [InlineData("Model.Definition.Components.MaterialColorizerComponent", false)]
        [InlineData("Some.Pack.DefaultLivelryComponent", false)]
        // Fail-safe: an unknown/empty type name is treated as eager-only so we never
        // defer something we could not classify.
        [InlineData("", true)]
        [InlineData(null, true)]
        public void IsEagerOnlyTypeName_ForcesEagerForStatefulOnly(string typeName, bool expected)
        {
            Assert.Equal(expected, FuseSceneryDeferralClassifier.IsEagerOnlyTypeName(typeName));
        }
    }
}
