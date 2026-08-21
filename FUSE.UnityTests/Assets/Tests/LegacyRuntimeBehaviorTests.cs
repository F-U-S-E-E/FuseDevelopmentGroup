using System;
using System.Linq;
using System.Reflection;
using FUSE.Authoring.Data;
using FUSE.Runtime.API;
using Model.Ops;
using NUnit.Framework;
using Track;
using UnityEngine;
using UnityEngine.Rendering;

namespace FUSE.UnityTests
{
    public sealed class LegacyRuntimeBehaviorTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("FUSE legacy runtime behavior tests");
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void ConfusingSupplementsEmpty_RemainsVisibleAndAcceptsEveryAutoDestinationKind()
        {
            var component = _root.AddComponent<FuseLegacyPlaceholderIndustryComponent>();
            component.trackSpans = new TrackSpan[1];

            Assert.That(component.IsVisible, Is.True);
            Assert.That(
                Enum.GetValues(typeof(AutoDestinationType)).Cast<AutoDestinationType>()
                    .All(component.WantsAutoDestination),
                Is.True);
        }

        [TestCase("TurntableMeasurementTool")]
        [TestCase("scenery://TurntableMeasurementTool")]
        [TestCase("ALW_ModRes_TurntableMeasurementTool")]
        public void TurntableMeasurementPlate_IsEditorOnly(string identifier)
        {
            Assert.That(SceneryAPI.IsEditorOnlyLegacySceneryReference(new FuseScenery
            {
                AssetIdentifier = identifier
            }), Is.True);
        }

        [Test]
        public void OrdinaryTurntableScenery_IsNotEditorOnly()
        {
            Assert.That(SceneryAPI.IsEditorOnlyLegacySceneryReference(new FuseScenery
            {
                AssetIdentifier = "scenery://ALW_ModRes_plate50x250"
            }), Is.False);
        }

        [Test]
        public void ObjectLineUniformSpacing_IncludesFinalEndpoint()
        {
            var placements = FuseObjectLineLayout.Build(
                new[] { Point(0f, 0f), Point(10f, 0f) },
                4f,
                true,
                10);

            Assert.That(placements.Select(value => value.Position.x),
                Is.EqualTo(new[] { 0f, 4f, 8f, 10f }).Within(0.001f));
            Assert.That(placements.All(value => value.Forward == Vector3.right), Is.True);
        }

        [Test]
        public void ObjectLineSpacing_ContinuesAcrossPolylineCorners()
        {
            var placements = FuseObjectLineLayout.Build(
                new[] { Point(0f, 0f), Point(5f, 0f), Point(5f, 5f) },
                4f,
                true,
                10);

            Assert.That(placements.Count, Is.EqualTo(4));
            Assert.That(placements[2].Position, Is.EqualTo(new Vector3(5f, 0f, 3f)));
            Assert.That(placements[2].Forward, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void ObjectLineSafetyLimit_RejectsInstanceExplosion()
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                FuseObjectLineLayout.Build(
                    new[] { Point(0f, 0f), Point(100f, 0f) },
                    1f,
                    true,
                    20));

            StringAssert.Contains("20 instance safety limit", exception.Message);
        }

        [Test]
        public void ObjectLineDuplicateOnlyPath_IsRejected()
        {
            Assert.Throws<InvalidOperationException>(() =>
                FuseObjectLineLayout.Build(
                    new[] { Point(2f, 3f), Point(2f, 3f) },
                    5f,
                    true,
                    20));
        }

        [Test]
        public void SourceLakeProfile_CopiesRenderingFlowAndTerrainControls()
        {
            var source = _root.AddComponent<LakePolygon>();
            source.distSmooth = 7.5f;
            source.terrainSmoothMultiplier = 2.5f;
            source.overrideLakeRender = true;
            source.receiveShadows = true;
            source.shadowCastingMode = ShadowCastingMode.TwoSided;
            source.automaticFlowMapScale = 0.65f;
            source.noiseflowMap = true;
            source.noiseMultiplierflowMap = 1.7f;
            source.noiseSizeXflowMap = 0.15f;
            source.noiseSizeZflowMap = 0.35f;
            source.floatSpeed = 12f;
            source.flowSpeed = 3f;
            source.flowDirection = 42f;
            source.normalFromRaycast = true;
            source.snapMask = 123;

            var targetObject = new GameObject("target lake");
            targetObject.transform.SetParent(_root.transform, false);
            var target = targetObject.AddComponent<LakePolygon>();
            InvokeApplySourceProfile(source, target);

            Assert.That(target.distSmooth, Is.EqualTo(source.distSmooth));
            Assert.That(target.terrainSmoothMultiplier, Is.EqualTo(source.terrainSmoothMultiplier));
            Assert.That(target.overrideLakeRender, Is.EqualTo(source.overrideLakeRender));
            Assert.That(target.receiveShadows, Is.EqualTo(source.receiveShadows));
            Assert.That(target.shadowCastingMode, Is.EqualTo(source.shadowCastingMode));
            Assert.That(target.automaticFlowMapScale, Is.EqualTo(source.automaticFlowMapScale));
            Assert.That(target.noiseflowMap, Is.EqualTo(source.noiseflowMap));
            Assert.That(target.noiseMultiplierflowMap, Is.EqualTo(source.noiseMultiplierflowMap));
            Assert.That(target.noiseSizeXflowMap, Is.EqualTo(source.noiseSizeXflowMap));
            Assert.That(target.noiseSizeZflowMap, Is.EqualTo(source.noiseSizeZflowMap));
            Assert.That(target.floatSpeed, Is.EqualTo(source.floatSpeed));
            Assert.That(target.flowSpeed, Is.EqualTo(source.flowSpeed));
            Assert.That(target.flowDirection, Is.EqualTo(source.flowDirection));
            Assert.That(target.normalFromRaycast, Is.EqualTo(source.normalFromRaycast));
            Assert.That(target.snapMask.value, Is.EqualTo(source.snapMask.value));
        }

        [Test]
        public void MissingSourceLake_UsesNonShadowedSafeDefaults()
        {
            var target = _root.AddComponent<LakePolygon>();
            target.receiveShadows = true;
            target.shadowCastingMode = ShadowCastingMode.TwoSided;

            InvokeApplySourceProfile(null, target);

            Assert.That(target.currentProfile, Is.Null);
            Assert.That(target.receiveShadows, Is.False);
            Assert.That(target.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
        }

        private static FuseSplineyPoint Point(float x, float z)
        {
            return new FuseSplineyPoint { Position = new Vector3(x, 0f, z) };
        }

        private static void InvokeApplySourceProfile(LakePolygon source, LakePolygon target)
        {
            var method = typeof(WaterSurfaceAPI).GetMethod(
                "ApplySourceLakeProfile",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { source, target });
        }
    }
}
