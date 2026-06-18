using System;
using System.Reflection;
using AssetPack.Runtime;
using FUSE.Loading;
using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    /// <summary>
    /// Reflection-driven tests for the AssetBundlePath and LoadedBundle
    /// Harmony patch Prefix methods. The Prefixes are private statics on
    /// internal classes, so we invoke them by reflection — that's the
    /// same way Harmony invokes them at runtime. We can drive the null /
    /// no-collision / collision-present branches and verify that the
    /// Prefix returns the right value for each. The actual reflection
    /// into the live <c>PrefabStore._stores</c> still requires Unity,
    /// so collision-with-winner-found scenarios are covered indirectly
    /// via the helper tests rather than by faking a PrefabStore here.
    /// </summary>
    [Collection(FUSE.Tests.Loading.FuseAssetCollisionRegistryTestCollection.Name)]
    public class FuseAssetPackBundlePathPatchTests : System.IDisposable
    {
        public FuseAssetPackBundlePathPatchTests()
        {
            FuseAssetCollisionRegistry.Reset();
            FuseAssetPackPatchHelpers.ResetLoggedLoserRedirects();
        }

        public void Dispose()
        {
            FuseAssetCollisionRegistry.Reset();
            FuseAssetPackPatchHelpers.ResetLoggedLoserRedirects();
            GC.SuppressFinalize(this);
        }

        // ----- AssetBundlePath patch -----

        [Fact]
        public void AssetBundlePathPrefix_returns_true_on_null_instance()
        {
            string result = null;
            var passThrough = InvokeAssetBundlePathPrefix(null, ref result);
            Assert.True(passThrough);
            Assert.Null(result);
        }

        [Fact]
        public void AssetBundlePathPrefix_returns_true_when_no_redirect_recorded()
        {
            // No scan has populated the redirect table → no collision
            // for this folder → patch must pass through.
            var store = new AssetPackRuntimeStore("UnknownStoreId", (AssetPackRuntimeStore.StoreLocation)1);
            string result = null;
            var passThrough = InvokeAssetBundlePathPrefix(store, ref result);
            Assert.True(passThrough);
        }

        // ----- LoadedBundle patch -----

        [Fact]
        public void LoadedBundlePrefix_returns_true_on_null_instance()
        {
            System.Threading.Tasks.Task<UnityEngine.AssetBundle> result = null;
            var passThrough = InvokeLoadedBundlePrefix(null, ref result);
            Assert.True(passThrough);
            Assert.Null(result);
        }

        [Fact]
        public void LoadedBundlePrefix_returns_true_when_no_redirect_recorded()
        {
            // Same as AssetBundlePath: empty registry → pass-through.
            var store = new AssetPackRuntimeStore("UnknownStoreId", (AssetPackRuntimeStore.StoreLocation)1);
            System.Threading.Tasks.Task<UnityEngine.AssetBundle> result = null;
            var passThrough = InvokeLoadedBundlePrefix(store, ref result);
            Assert.True(passThrough);
        }

        // ----- Reflection plumbing -----

        private static readonly MethodInfo AssetBundlePathPrefix =
            typeof(FuseAssetPackRuntimeStoreAssetBundlePathPatch)
                .GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo LoadedBundlePrefix =
            typeof(FuseAssetPackRuntimeStoreLoadedBundlePatch)
                .GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);

        private static bool InvokeAssetBundlePathPrefix(AssetPackRuntimeStore instance, ref string result)
        {
            Assert.NotNull(AssetBundlePathPrefix);
            var args = new object[] { instance, result };
            var ret = (bool)AssetBundlePathPrefix.Invoke(null, args);
            result = (string)args[1];
            return ret;
        }

        private static bool InvokeLoadedBundlePrefix(
            AssetPackRuntimeStore instance,
            ref System.Threading.Tasks.Task<UnityEngine.AssetBundle> result)
        {
            Assert.NotNull(LoadedBundlePrefix);
            var args = new object[] { instance, result };
            var ret = (bool)LoadedBundlePrefix.Invoke(null, args);
            result = (System.Threading.Tasks.Task<UnityEngine.AssetBundle>)args[1];
            return ret;
        }
    }
}
