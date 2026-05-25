using FUSE.Authoring;
using FUSE.Data;
using UnityEngine;
using Xunit;

namespace FUSE.Tests.Authoring
{
    /// <summary>
    /// Regression tests for <see cref="FuseConfigurableStructureEntity"/>'s
    /// definition round-trip. The entity wraps a
    /// <see cref="FuseSceneClone"/> for the authoring/editor pipeline; the
    /// runtime apply path
    /// (<c>FUSE.API.SceneCloneAPI.ApplyDefinition</c>) treats
    /// <c>LocalPosition.HasValue == true</c> as an explicit "force the
    /// live transform to this value" command. So if BuildRuntimeData
    /// promotes the entity's default <c>Vector3.zero</c> Position into an
    /// explicit override, every mandela that omits <c>localPosition</c>
    /// silently teleports its bound GameObject to its parent's origin
    /// on apply — which is exactly how the vanilla Bryson Freight House
    /// got zeroed out of its <c>(202.36, 1.0, 210.45)</c> local position
    /// into <c>(0, 0, 0)</c>, on top of Lego's Scrappalachia yard, by
    /// Stryker's <c>"World/Large Scenery/Bryson/Freight House": {
    /// "enabled": true }</c> entry.
    ///
    /// These tests pin the round-trip behaviour: any transform component
    /// the source JSON DID NOT specify must come back out of
    /// <see cref="FuseConfigurableStructureEntity.BuildRuntimeData"/> as
    /// <c>null</c>, never as <c>Vector3.zero</c>.
    /// </summary>
    public class FuseConfigurableStructureEntityTests
    {
        private static FuseConfigurableStructureEntity NewEntity() =>
            new FuseConfigurableStructureEntity("test-scene-clone", "test-package");

        public class LoadDefinitionThenBuildRuntimeData
        {
            [Fact]
            public void EnabledOnly_NoTransformOverridesEmitted()
            {
                // The canonical Bryson Freight House bug: nullBryson.json's
                // entry is just `{ "enabled": true }`. The legacy converter
                // parses that into a FuseSceneClone with all three Local*
                // fields null. The entity must round-trip that exactly.
                var entity = NewEntity();

                entity.LoadDefinition(new FuseSceneClone
                {
                    TargetPath = "World/Large Scenery/Bryson/Freight House",
                    Enabled = true
                });

                var data = (FuseSceneClone)entity.BuildRuntimeData();

                Assert.True(data.Enabled);
                Assert.False(
                    data.LocalPosition.HasValue,
                    "An enabled-only mandela MUST NOT promote the entity's default Position into an explicit LocalPosition; doing so zeroes the live transform on apply.");
                Assert.False(data.LocalRotation.HasValue);
                Assert.False(data.LocalScale.HasValue);
            }

            [Fact]
            public void NoFieldsAtAll_AllOverridesAreNull()
            {
                var entity = NewEntity();

                entity.LoadDefinition(new FuseSceneClone
                {
                    TargetPath = "World/Large Scenery/Bryson/Freight House"
                });

                var data = (FuseSceneClone)entity.BuildRuntimeData();

                Assert.False(data.LocalPosition.HasValue);
                Assert.False(data.LocalRotation.HasValue);
                Assert.False(data.LocalScale.HasValue);
            }

            [Fact]
            public void PositionOnly_OnlyPositionRoundTrips()
            {
                // Authored position must come back unchanged AND the
                // unspecified rotation/scale must stay null (we do not
                // want a partial override to drag the others to zero).
                var entity = NewEntity();
                var authored = new Vector3(123.4f, 56.7f, -89f);

                entity.LoadDefinition(new FuseSceneClone
                {
                    TargetPath = "anywhere",
                    Enabled = true,
                    LocalPosition = authored
                });

                var data = (FuseSceneClone)entity.BuildRuntimeData();

                Assert.True(data.LocalPosition.HasValue);
                Assert.Equal(authored, data.LocalPosition.Value);
                Assert.False(data.LocalRotation.HasValue);
                Assert.False(data.LocalScale.HasValue);
            }

            [Fact]
            public void RotationOnly_OnlyRotationRoundTrips()
            {
                var entity = NewEntity();
                var authored = new Vector3(0f, 90f, 0f);

                entity.LoadDefinition(new FuseSceneClone
                {
                    TargetPath = "anywhere",
                    Enabled = true,
                    LocalRotation = authored
                });

                var data = (FuseSceneClone)entity.BuildRuntimeData();

                Assert.False(data.LocalPosition.HasValue);
                Assert.True(data.LocalRotation.HasValue);
                Assert.Equal(authored, data.LocalRotation.Value);
                Assert.False(data.LocalScale.HasValue);
            }

            [Fact]
            public void ScaleOnly_OnlyScaleRoundTrips()
            {
                var entity = NewEntity();
                var authored = new Vector3(2f, 2f, 2f);

                entity.LoadDefinition(new FuseSceneClone
                {
                    TargetPath = "anywhere",
                    Enabled = true,
                    LocalScale = authored
                });

                var data = (FuseSceneClone)entity.BuildRuntimeData();

                Assert.False(data.LocalPosition.HasValue);
                Assert.False(data.LocalRotation.HasValue);
                Assert.True(data.LocalScale.HasValue);
                Assert.Equal(authored, data.LocalScale.Value);
            }

            [Fact]
            public void AllThreeAuthored_AllRoundTrip()
            {
                var entity = NewEntity();
                var p = new Vector3(1f, 2f, 3f);
                var r = new Vector3(45f, 0f, 0f);
                var s = new Vector3(0.5f, 0.5f, 0.5f);

                entity.LoadDefinition(new FuseSceneClone
                {
                    TargetPath = "anywhere",
                    Enabled = true,
                    LocalPosition = p,
                    LocalRotation = r,
                    LocalScale = s
                });

                var data = (FuseSceneClone)entity.BuildRuntimeData();

                Assert.Equal(p, data.LocalPosition);
                Assert.Equal(r, data.LocalRotation);
                Assert.Equal(s, data.LocalScale);
            }

            [Fact]
            public void AuthoredZero_IsStillExplicit()
            {
                // An author who explicitly writes localPosition: { 0, 0, 0 }
                // really does want the transform forced to origin. We must
                // round-trip that as a SET value, not collapse it to null
                // (which would silently leave the live transform alone).
                var entity = NewEntity();

                entity.LoadDefinition(new FuseSceneClone
                {
                    TargetPath = "anywhere",
                    Enabled = true,
                    LocalPosition = Vector3.zero
                });

                var data = (FuseSceneClone)entity.BuildRuntimeData();

                Assert.True(
                    data.LocalPosition.HasValue,
                    "An explicit Vector3.zero in the source JSON must remain explicit after the round-trip; otherwise authors lose the ability to force origin alignment.");
                Assert.Equal(Vector3.zero, data.LocalPosition.Value);
            }

            [Fact]
            public void OverwriteWithSubsetDefinition_PreviousAuthorityIsReplaced()
            {
                // A second LoadDefinition that authors fewer fields must
                // RESET the "specified" flags accordingly — otherwise
                // re-loading from a stripped definition would emit ghost
                // values from a prior fully-authored load.
                var entity = NewEntity();
                entity.LoadDefinition(new FuseSceneClone
                {
                    TargetPath = "anywhere",
                    Enabled = true,
                    LocalPosition = new Vector3(10f, 20f, 30f),
                    LocalRotation = new Vector3(40f, 50f, 60f),
                    LocalScale = new Vector3(2f, 3f, 4f)
                });

                entity.LoadDefinition(new FuseSceneClone
                {
                    TargetPath = "anywhere",
                    Enabled = true
                });

                var data = (FuseSceneClone)entity.BuildRuntimeData();

                Assert.False(data.LocalPosition.HasValue);
                Assert.False(data.LocalRotation.HasValue);
                Assert.False(data.LocalScale.HasValue);
            }

            [Fact]
            public void NullDefinition_IsTolerated()
            {
                var entity = NewEntity();

                entity.LoadDefinition(null);

                var data = (FuseSceneClone)entity.BuildRuntimeData();
                Assert.NotNull(data);
                // Default-constructed entity must not emit phantom transform values.
                Assert.False(data.LocalPosition.HasValue);
                Assert.False(data.LocalRotation.HasValue);
                Assert.False(data.LocalScale.HasValue);
            }
        }

        public class FreshEntityWithoutLoadDefinition
        {
            [Fact]
            public void DefaultEntity_BuildRuntimeData_EmitsNoTransformOverrides()
            {
                // Defends against the scenario where an entity is
                // constructed but never had LoadDefinition called on it
                // (e.g. a brand-new editor-created entity that the user
                // has not yet typed into). We do not want the default
                // Vector3.zero / Vector3.one to silently become apply-time
                // overrides on every save.
                var entity = NewEntity();
                entity.TargetPath = "anywhere";
                entity.Enabled = true;

                var data = (FuseSceneClone)entity.BuildRuntimeData();

                Assert.False(data.LocalPosition.HasValue);
                Assert.False(data.LocalRotation.HasValue);
                Assert.False(data.LocalScale.HasValue);
            }
        }
    }
}
