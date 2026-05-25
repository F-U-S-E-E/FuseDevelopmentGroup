using System;
using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    /// <summary>
    /// Smoke tests for the static helpers used by the asset-pack
    /// collision Harmony patches. These exercise the null- and error-
    /// guard paths that we can drive without Unity present, plus the
    /// pure-logic helpers (path formatting, log-dedup) that the
    /// runtime patches delegate to. The reflection-into-PrefabStore
    /// paths still require the live runtime and are validated
    /// empirically via the game log.
    /// </summary>
    public class FuseAssetPackPatchHelpersTests : IDisposable
    {
        public FuseAssetPackPatchHelpersTests()
        {
            // Each test starts with a clean log-dedup table so the
            // first-fire behavior is observable in isolation.
            FuseAssetPackPatchHelpers.ResetLoggedLoserRedirects();
        }

        public void Dispose()
        {
            FuseAssetPackPatchHelpers.ResetLoggedLoserRedirects();
            GC.SuppressFinalize(this);
        }

        // ---- Reflection-helper null safety ----

        [Fact]
        public void ResolveBasePath_returns_null_for_null_store()
        {
            Assert.Null(FuseAssetPackPatchHelpers.ResolveBasePath(null));
        }

        [Fact]
        public void InvokeLoadedBundle_returns_null_for_null_store()
        {
            Assert.Null(FuseAssetPackPatchHelpers.InvokeLoadedBundle(null));
        }

        [Fact]
        public void FindStoreByBasePath_returns_null_for_null_input()
        {
            Assert.Null(FuseAssetPackPatchHelpers.FindStoreByBasePath(null));
        }

        [Fact]
        public void FindStoreByBasePath_returns_null_for_empty_input()
        {
            Assert.Null(FuseAssetPackPatchHelpers.FindStoreByBasePath(string.Empty));
            Assert.Null(FuseAssetPackPatchHelpers.FindStoreByBasePath("   "));
        }

        [Fact]
        public void FindStoreByBasePath_returns_null_when_PrefabStore_not_initialized()
        {
            // Test-context invariant: TrainController.Shared isn't wired
            // up in unit tests, so the helper must gracefully return null
            // rather than throw a NullReferenceException. The runtime
            // patches relying on this helper handle null by falling
            // through to the original method, so a null here is the
            // correct "no winner found, proceed normally" signal.
            Assert.Null(FuseAssetPackPatchHelpers.FindStoreByBasePath(@"C:\does\not\exist"));
        }

        // ---- ShortenForLog formatting ----

        [Fact]
        public void ShortenForLog_returns_unknown_marker_for_null_or_empty()
        {
            Assert.Equal("<unknown>", FuseAssetPackPatchHelpers.ShortenForLog(null));
            Assert.Equal("<unknown>", FuseAssetPackPatchHelpers.ShortenForLog(string.Empty));
            Assert.Equal("<unknown>", FuseAssetPackPatchHelpers.ShortenForLog("   "));
        }

        [Fact]
        public void ShortenForLog_returns_full_path_for_short_inputs()
        {
            // Two segments or fewer means there's nothing useful to
            // shorten — keep the original so log lines don't lose
            // information for paths that were already small.
            var shortPath = @"C:\one";
            Assert.Equal(shortPath, FuseAssetPackPatchHelpers.ShortenForLog(shortPath));
        }

        [Fact]
        public void ShortenForLog_keeps_last_three_segments_for_long_paths()
        {
            var full = @"C:\SteamLibrary\steamapps\common\Railroader\Mods\TOFC Cars\SCAssetPacks\spinecar1";
            var shortened = FuseAssetPackPatchHelpers.ShortenForLog(full);
            Assert.Equal(".../TOFC Cars/SCAssetPacks/spinecar1", shortened);
        }

        [Fact]
        public void ShortenForLog_handles_forward_slash_separators()
        {
            var full = "/var/lib/Mods/MyMod/SCAssetPacks/widget";
            var shortened = FuseAssetPackPatchHelpers.ShortenForLog(full);
            // Last three components, joined with forward slashes.
            Assert.Equal(".../MyMod/SCAssetPacks/widget", shortened);
        }

        // ---- One-shot redirect-log dedup ----

        [Fact]
        public void ShouldLogLoserRedirectOnce_returns_true_first_time_false_after()
        {
            const string loser = @"C:\Mods\TOFC Cars\spinecar1";
            Assert.True(FuseAssetPackPatchHelpers.ShouldLogLoserRedirectOnce(loser));
            Assert.False(FuseAssetPackPatchHelpers.ShouldLogLoserRedirectOnce(loser));
            Assert.False(FuseAssetPackPatchHelpers.ShouldLogLoserRedirectOnce(loser));
        }

        [Fact]
        public void ShouldLogLoserRedirectOnce_dedupes_independently_per_loser()
        {
            const string loserA = @"C:\Mods\ModA\widget";
            const string loserB = @"C:\Mods\ModB\widget";
            Assert.True(FuseAssetPackPatchHelpers.ShouldLogLoserRedirectOnce(loserA));
            Assert.True(FuseAssetPackPatchHelpers.ShouldLogLoserRedirectOnce(loserB));
            Assert.False(FuseAssetPackPatchHelpers.ShouldLogLoserRedirectOnce(loserA));
            Assert.False(FuseAssetPackPatchHelpers.ShouldLogLoserRedirectOnce(loserB));
        }

        [Fact]
        public void ShouldLogLoserRedirectOnce_is_case_insensitive()
        {
            // Windows filesystem is case-insensitive; the same logical
            // pack folder must dedupe across surface-form variations.
            const string upper = @"C:\Mods\TOFC CARS\spinecar1";
            const string lower = @"c:\mods\tofc cars\spinecar1";
            Assert.True(FuseAssetPackPatchHelpers.ShouldLogLoserRedirectOnce(upper));
            Assert.False(FuseAssetPackPatchHelpers.ShouldLogLoserRedirectOnce(lower));
        }

        [Fact]
        public void ShouldLogLoserRedirectOnce_treats_null_as_sentinel_key()
        {
            // Null still dedupes — we'd rather log the null-key bug
            // once than spam it.
            Assert.True(FuseAssetPackPatchHelpers.ShouldLogLoserRedirectOnce(null));
            Assert.False(FuseAssetPackPatchHelpers.ShouldLogLoserRedirectOnce(null));
        }

        [Fact]
        public void ResetLoggedLoserRedirects_clears_dedup_table()
        {
            const string loser = @"C:\Mods\TOFC Cars\spinecar1";
            Assert.True(FuseAssetPackPatchHelpers.ShouldLogLoserRedirectOnce(loser));
            Assert.False(FuseAssetPackPatchHelpers.ShouldLogLoserRedirectOnce(loser));

            FuseAssetPackPatchHelpers.ResetLoggedLoserRedirects();
            Assert.True(FuseAssetPackPatchHelpers.ShouldLogLoserRedirectOnce(loser));
        }

        [Fact]
        public void ResetLoggedLoserRedirects_when_already_empty_is_noop()
        {
            FuseAssetPackPatchHelpers.ResetLoggedLoserRedirects();
            FuseAssetPackPatchHelpers.ResetLoggedLoserRedirects();
            // No throw, no observable side effect.
            Assert.True(FuseAssetPackPatchHelpers.ShouldLogLoserRedirectOnce("anything"));
        }

        // ---- TryReadBundleCabName ----

        [Fact]
        public void TryReadBundleCabName_returns_null_for_null_or_empty()
        {
            Assert.Null(FuseAssetPackPatchHelpers.TryReadBundleCabName(null));
            Assert.Null(FuseAssetPackPatchHelpers.TryReadBundleCabName(string.Empty));
            Assert.Null(FuseAssetPackPatchHelpers.TryReadBundleCabName("   "));
        }

        [Fact]
        public void TryReadBundleCabName_returns_null_for_missing_file()
        {
            Assert.Null(FuseAssetPackPatchHelpers.TryReadBundleCabName(
                @"C:\does\not\exist\Bundle"));
        }

        [Fact]
        public void TryReadBundleCabName_returns_null_for_non_unityfs_file()
        {
            // Any random file content that doesn't start with the UnityFS
            // signature must be rejected; we don't want to accidentally
            // mis-identify garbage as a Unity AssetBundle.
            var path = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(path, "this is definitely not a unity bundle CAB-abc123");
                Assert.Null(FuseAssetPackPatchHelpers.TryReadBundleCabName(path));
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void TryReadBundleCabName_extracts_hex_suffix_for_synthetic_unityfs_header()
        {
            // Synthesize a minimal UnityFS-flavored header with a known
            // CAB string a few bytes in. We don't need a real bundle —
            // the helper only looks for the UnityFS signature and then
            // scans for the CAB-<hex> pattern terminated by a null byte
            // or non-hex character. This locks down the parser shape
            // without requiring a 100 KB real bundle in the test data.
            var bytes = new System.Collections.Generic.List<byte>();
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("UnityFS"));
            bytes.Add(0);
            // Padding to land the CAB string at a non-zero offset.
            for (var index = 0; index < 32; index++)
            {
                bytes.Add((byte)index);
            }
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("CAB-deadbeefcafef00d1234567890abcdef"));
            bytes.Add(0);

            var path = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllBytes(path, bytes.ToArray());
                Assert.Equal(
                    "CAB-deadbeefcafef00d1234567890abcdef",
                    FuseAssetPackPatchHelpers.TryReadBundleCabName(path));
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void TryReadBundleCabName_stops_at_non_hex_character_in_suffix()
        {
            // The bundle file format puts the CAB- name as a null-
            // terminated ASCII string. A non-hex byte following the
            // legitimate hex digits means we've walked into the next
            // field — stop there so we don't return garbage.
            var bytes = new System.Collections.Generic.List<byte>();
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("UnityFS"));
            bytes.Add(0);
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("CAB-abc1234XYZ"));

            var path = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllBytes(path, bytes.ToArray());
                Assert.Equal(
                    "CAB-abc1234",
                    FuseAssetPackPatchHelpers.TryReadBundleCabName(path));
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        // ---- _loadAssetBundleTask cache reflection helpers ----

        [Fact]
        public void GetCachedLoadAssetBundleTask_returns_null_for_null_store()
        {
            Assert.Null(FuseAssetPackPatchHelpers.GetCachedLoadAssetBundleTask(null));
        }

        [Fact]
        public void SetCachedLoadAssetBundleTask_handles_null_inputs_safely()
        {
            // Both null store and null task should be silent no-ops; the
            // helper is invoked from the LoadedBundle Prefix's fall-back
            // path and a throw there would interrupt the original load
            // pipeline.
            var ex = Record.Exception(() =>
            {
                FuseAssetPackPatchHelpers.SetCachedLoadAssetBundleTask(null, null);
                FuseAssetPackPatchHelpers.SetCachedLoadAssetBundleTask(
                    null,
                    System.Threading.Tasks.Task.FromResult<UnityEngine.AssetBundle>(null));
            });
            Assert.Null(ex);
        }
    }
}
