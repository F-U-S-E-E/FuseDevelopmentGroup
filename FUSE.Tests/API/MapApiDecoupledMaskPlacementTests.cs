using FUSE.Runtime.API;
using UnityEngine;
using Xunit;

namespace FUSE.Tests.API
{
    /// <summary>
    /// Pure-math coverage for <see cref="MapAPI.ComputeMaskGamePosition"/>, the rebase-invariant
    /// game-space anchor a decoupled map mask registers its terrain modifier at. The inputs are a
    /// scenery root's local position (its authored game position — a parent translation never
    /// changes it), the root's world position, and the welded mask's world position. The mask and
    /// root share a hierarchy, so their world delta is the same in every floating-origin state;
    /// the sum must therefore return the same game position whether the chain was sampled before
    /// a MoveWorld rebase, after it, or while the rebase only half-propagated relative to
    /// MapManager's offset — the race that put the Bryson roundhouse flatten a whole origin block
    /// (~4000/5000m) off its tile, where no terrain rebuild could ever pick it up.
    /// </summary>
    public class MapApiDecoupledMaskPlacementTests
    {
        // Bryson roundhouse numbers from the field failure: authored game position of the
        // scenery root, the mask welded ~22m away inside the model, and the floating-origin
        // offset after the spawn MoveWorld to tile (8,10).
        private static readonly Vector3 RootGame = new Vector3(4300f, 529f, 5400f);
        private static readonly Vector3 MaskOffsetInModel = new Vector3(2.98f, 0.01f, -24.98f);
        private static readonly Vector3 RebaseOffset = new Vector3(-4000f, 0f, -5000f);

        private static readonly Vector3 ExpectedGame = RootGame + MaskOffsetInModel;

        [Fact]
        public void PreRebaseChain_YieldsAuthoredGamePosition()
        {
            // Before the first MoveWorld the world equals game space: root sits at its authored
            // position and the mask at the authored offset within the model.
            var game = MapAPI.ComputeMaskGamePosition(
                sceneryRootLocalPosition: RootGame,
                sceneryRootWorldPosition: RootGame,
                maskWorldPosition: RootGame + MaskOffsetInModel);

            Assert.Equal(ExpectedGame, game);
        }

        [Fact]
        public void PostRebaseChain_YieldsSameGamePosition()
        {
            // After MoveWorld the whole chain is translated by the offset; localPosition is
            // untouched. The world delta cancels the offset, so the result is identical.
            var game = MapAPI.ComputeMaskGamePosition(
                sceneryRootLocalPosition: RootGame,
                sceneryRootWorldPosition: RootGame + RebaseOffset,
                maskWorldPosition: RootGame + RebaseOffset + MaskOffsetInModel);

            Assert.Equal(ExpectedGame, game);
        }

        [Fact]
        public void RebasedAndUnRebasedSamples_Agree()
        {
            // The race-proof property itself: a decouple burst can sample one mask before the
            // chain rode a MoveWorld and the next after (or from a chain that never rides, e.g.
            // a non-moving fallback parent). An absolute transform read differs by a whole
            // origin block between those samples — the failure that buried the Bryson
            // roundhouse; the local+delta form must not.
            var sampledUnRebased = MapAPI.ComputeMaskGamePosition(
                sceneryRootLocalPosition: RootGame,
                sceneryRootWorldPosition: RootGame,
                maskWorldPosition: RootGame + MaskOffsetInModel);

            var sampledRebased = MapAPI.ComputeMaskGamePosition(
                sceneryRootLocalPosition: RootGame,
                sceneryRootWorldPosition: RootGame + RebaseOffset,
                maskWorldPosition: RootGame + RebaseOffset + MaskOffsetInModel);

            Assert.Equal(sampledUnRebased, sampledRebased);
        }

        [Fact]
        public void RootMovedByUpdateScenery_TracksTheNewAuthoredPosition()
        {
            // UpdateScenery rewrites the root's localPosition; the recomputed anchor must follow
            // the building, not the original placement.
            var newRootGame = RootGame + new Vector3(50f, 0f, -75f);

            var game = MapAPI.ComputeMaskGamePosition(
                sceneryRootLocalPosition: newRootGame,
                sceneryRootWorldPosition: newRootGame + RebaseOffset,
                maskWorldPosition: newRootGame + RebaseOffset + MaskOffsetInModel);

            Assert.Equal(newRootGame + MaskOffsetInModel, game);
        }
    }
}
