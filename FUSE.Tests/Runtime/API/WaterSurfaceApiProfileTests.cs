using System;
using System.Reflection;
using System.Runtime.Serialization;
using FUSE.Runtime.API;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace FUSE.Tests.Runtime.API
{
    public sealed class WaterSurfaceApiProfileTests
    {
        [Fact]
        public void SourceLakeProfileCopiesRenderingFlowAndTerrainSnapControls()
        {
            var source = UninitializedLake();
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

            var target = UninitializedLake();
            InvokeApplySourceProfile(source, target);

            Assert.Equal(source.distSmooth, target.distSmooth);
            Assert.Equal(source.terrainSmoothMultiplier, target.terrainSmoothMultiplier);
            Assert.Equal(source.overrideLakeRender, target.overrideLakeRender);
            Assert.Equal(source.receiveShadows, target.receiveShadows);
            Assert.Equal(source.shadowCastingMode, target.shadowCastingMode);
            Assert.Equal(source.automaticFlowMapScale, target.automaticFlowMapScale);
            Assert.Equal(source.noiseflowMap, target.noiseflowMap);
            Assert.Equal(source.noiseMultiplierflowMap, target.noiseMultiplierflowMap);
            Assert.Equal(source.noiseSizeXflowMap, target.noiseSizeXflowMap);
            Assert.Equal(source.noiseSizeZflowMap, target.noiseSizeZflowMap);
            Assert.Equal(source.floatSpeed, target.floatSpeed);
            Assert.Equal(source.flowSpeed, target.flowSpeed);
            Assert.Equal(source.flowDirection, target.flowDirection);
            Assert.Equal(source.normalFromRaycast, target.normalFromRaycast);
            Assert.Equal(source.snapMask.value, target.snapMask.value);
        }

        [Fact]
        public void MissingSourceUsesNonShadowedSafeDefaults()
        {
            var target = UninitializedLake();
            target.receiveShadows = true;
            target.shadowCastingMode = ShadowCastingMode.TwoSided;

            InvokeApplySourceProfile(null, target);

            Assert.Null(target.currentProfile);
            Assert.False(target.receiveShadows);
            Assert.Equal(ShadowCastingMode.Off, target.shadowCastingMode);
        }

        private static LakePolygon UninitializedLake()
        {
#pragma warning disable SYSLIB0050
            return (LakePolygon)FormatterServices.GetUninitializedObject(typeof(LakePolygon));
#pragma warning restore SYSLIB0050
        }

        private static void InvokeApplySourceProfile(LakePolygon source, LakePolygon target)
        {
            var method = typeof(WaterSurfaceAPI).GetMethod(
                "ApplySourceLakeProfile",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var exception = Record.Exception(() => method.Invoke(null, new object[] { source, target }));
            if (exception is TargetInvocationException invocation && invocation.InnerException != null)
                throw invocation.InnerException;
            Assert.Null(exception);
        }
    }
}
