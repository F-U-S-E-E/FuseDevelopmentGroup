using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    /// <summary>
    /// Locks in the leaf-folder-name detection contract. The detection
    /// MUST flag two within-mod packs that share a leaf folder name and
    /// MUST NOT flag distinct packs that merely share a Catalog.json
    /// identifier or that live in different mod folders. Both regressions
    /// have happened during development — the tests are here to keep them
    /// from regressing.
    /// </summary>
    [Collection(FuseAssetCollisionRegistryTestCollection.Name)]
    public class FuseAssetCollisionRegistryTests : System.IDisposable
    {
        // Defensive cleanup so tests are order-independent: each test
        // starts with a clean registry, and the disposer flushes any
        // claims this test added before xUnit hands control to the next
        // one. Without this, a failing test could leak shared-owner
        // claims into the global FuseRegistry and corrupt unrelated test
        // suites' assertions on its shared-owner table.
        public FuseAssetCollisionRegistryTests()
        {
            FuseAssetCollisionRegistry.Reset();
        }

        public void Dispose()
        {
            FuseAssetCollisionRegistry.Reset();
            GC.SuppressFinalize(this);
        }

        // Synthetic mods root used so the host-mod-folder helper has a
        // stable boundary to climb to. The actual files don't exist;
        // ScanForCollisions only inspects folder names and asks our
        // callback for the host-mod folder, so we can run entirely on
        // string inputs.
        private const string ModsRoot = @"C:\fake\Mods";

        [Fact]
        public void Flags_within_mod_duplicate_leaf_names()
        {
            FuseAssetCollisionRegistry.Reset();

            var rootPack = $@"{ModsRoot}\TOFC Cars\spinecar1";
            var scPack = $@"{ModsRoot}\TOFC Cars\SCAssetPacks\spinecar1";
            var folders = new[] { rootPack, scPack };

            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                folders,
                _ => "TOFC Cars",
                pack => HostMod(pack));

            var collision = Assert.Single(collisions);
            Assert.Equal("spinecar1", collision.SharedIdentifier);
            Assert.Single(collision.LoserFolders);
            // Root pack wins regardless of input order — modern
            // definitions live at the mod root, SCAssetPacks/&lt;X&gt;
            // is the legacy fallback that must be filtered out.
            Assert.Equal(rootPack, collision.WinnerFolder);
            Assert.Equal(scPack, collision.LoserFolders[0]);
        }

        [Fact]
        public void Ignores_distinct_leaf_names_within_same_mod()
        {
            // The LLW Generic Locomotive Catalog regression: two distinct
            // packs whose Catalog.json identifier was typo-shared, but
            // whose folder names differ. Must not be flagged.
            FuseAssetCollisionRegistry.Reset();

            var k35a = $@"{ModsRoot}\LLW Generic Locomotive Catalog\ls-282-k35a";
            var k35b = $@"{ModsRoot}\LLW Generic Locomotive Catalog\ls-282-k35b";
            var folders = new[] { k35a, k35b };

            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                folders,
                _ => "LLW Generic Locomotive Catalog",
                pack => HostMod(pack));

            Assert.Empty(collisions);
        }

        [Fact]
        public void Ignores_cross_mod_overlap_even_when_leaf_names_match()
        {
            // The C_L_B.ASSETS01 vs RTM_Objects_Pack regression: two
            // entirely different mods publishing identically-named packs.
            // Each ships its own bundle with its own internal manifest
            // name; Unity does not collide on these and FUSE must not
            // redirect them.
            FuseAssetCollisionRegistry.Reset();

            var clbPack = $@"{ModsRoot}\C_L_B.ASSETS01\SCAssetPacks\CLB_ASSETS_01";
            var rtmPack = $@"{ModsRoot}\RTM_Objects_Pack\SCAssetPacks\CLB_ASSETS_01";
            var folders = new[] { clbPack, rtmPack };

            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                folders,
                pack => HostModName(pack),
                pack => HostMod(pack));

            Assert.Empty(collisions);
        }

        [Fact]
        public void Reset_clears_existing_redirects()
        {
            FuseAssetCollisionRegistry.Reset();

            var rootPack = $@"{ModsRoot}\TOFC Cars\spinecar1";
            var scPack = $@"{ModsRoot}\TOFC Cars\SCAssetPacks\spinecar1";
            var folders = new[] { rootPack, scPack };

            FuseAssetCollisionRegistry.ScanForCollisions(
                folders,
                _ => "TOFC Cars",
                pack => HostMod(pack));

            // SCAssetPacks-located pack is the loser; the root pack is
            // the winner so its lookup returns the no-redirect sentinel.
            Assert.True(FuseAssetCollisionRegistry.TryGetBundleRedirect(scPack, out _));

            FuseAssetCollisionRegistry.Reset();
            Assert.False(FuseAssetCollisionRegistry.TryGetBundleRedirect(scPack, out _));
            Assert.Empty(FuseAssetCollisionRegistry.CurrentCollisions);
        }

        [Fact]
        public void Winner_gets_no_redirect_loser_redirects_to_winner_bundle()
        {
            FuseAssetCollisionRegistry.Reset();

            var rootPack = $@"{ModsRoot}\TOFC Cars\spinecar1";
            var scPack = $@"{ModsRoot}\TOFC Cars\SCAssetPacks\spinecar1";
            var folders = new[] { rootPack, scPack };

            FuseAssetCollisionRegistry.ScanForCollisions(
                folders,
                _ => "TOFC Cars",
                pack => HostMod(pack));

            // Winner: root spinecar1 -> no redirect (sentinel).
            Assert.False(FuseAssetCollisionRegistry.TryGetBundleRedirect(rootPack, out _));

            // Loser: SCAssetPacks/spinecar1 -> redirects to root
            // spinecar1's Bundle.
            Assert.True(FuseAssetCollisionRegistry.TryGetBundleRedirect(scPack, out var redirect));
            Assert.EndsWith(@"TOFC Cars\spinecar1\Bundle", redirect);

            // TryGetWinnerFolder on the loser -> root spinecar1.
            Assert.True(FuseAssetCollisionRegistry.TryGetWinnerFolder(scPack, out var winnerFolder));
            Assert.EndsWith(@"TOFC Cars\spinecar1", winnerFolder);

            // TryGetWinnerFolder on the winner itself -> false.
            Assert.False(FuseAssetCollisionRegistry.TryGetWinnerFolder(rootPack, out _));
        }

        [Fact]
        public void Cross_mod_with_same_leaf_does_not_collide()
        {
            // Two entirely different mods that each ship a "spinecar1"
            // folder. Same leaf name, different mods, should not be
            // grouped together.
            FuseAssetCollisionRegistry.Reset();

            var modA = $@"{ModsRoot}\ModA\spinecar1";
            var modB = $@"{ModsRoot}\ModB\spinecar1";
            var folders = new[] { modA, modB };

            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                folders,
                pack => HostModName(pack),
                pack => HostMod(pack));

            Assert.Empty(collisions);
        }

        [Fact]
        public void Three_within_mod_duplicates_pick_one_winner_two_losers()
        {
            // RTM_Objects_Pack ships nick_building_pack twice nested
            // under SCAssetPacks at different depths. Both are
            // convention folders. ChooseWinner must pick one
            // deterministically — since neither is at the mod root,
            // they fall through to the ordinal-folder tiebreak.
            FuseAssetCollisionRegistry.Reset();

            var deep = $@"{ModsRoot}\RTM_Objects_Pack\SCAssetPacks\Nicks Building Pack with Steel Mill\SCAssetPacks\nick_building_pack";
            var shallow = $@"{ModsRoot}\RTM_Objects_Pack\SCAssetPacks\nick_building_pack";
            var folders = new[] { deep, shallow };

            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                folders,
                _ => "RTM_Objects_Pack",
                pack => HostMod(pack));

            var collision = Assert.Single(collisions);
            Assert.Equal("nick_building_pack", collision.SharedIdentifier);
            Assert.Single(collision.LoserFolders);
        }

        // ===== Edge cases on inputs =====

        [Fact]
        public void Null_input_returns_empty_collisions_no_throw()
        {
            FuseAssetCollisionRegistry.Reset();
            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(null, _ => "x", pack => HostMod(pack));
            Assert.Empty(collisions);
        }

        [Fact]
        public void Empty_input_returns_empty_collisions()
        {
            FuseAssetCollisionRegistry.Reset();
            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                Array.Empty<string>(),
                _ => "x",
                pack => HostMod(pack));
            Assert.Empty(collisions);
        }

        [Fact]
        public void Single_pack_never_collides()
        {
            FuseAssetCollisionRegistry.Reset();
            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { $@"{ModsRoot}\Mod\onlyone" },
                _ => "Mod",
                pack => HostMod(pack));
            Assert.Empty(collisions);
        }

        [Fact]
        public void Whitespace_and_null_pack_entries_are_skipped()
        {
            FuseAssetCollisionRegistry.Reset();
            var folders = new[]
            {
                null,
                string.Empty,
                "   ",
                $@"{ModsRoot}\TOFC Cars\spinecar1",
                $@"{ModsRoot}\TOFC Cars\SCAssetPacks\spinecar1",
            };

            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                folders,
                _ => "TOFC Cars",
                pack => HostMod(pack));

            Assert.Single(collisions);
        }

        [Fact]
        public void Pack_with_no_host_mod_callback_is_skipped()
        {
            // If the callback returns null (e.g., the pack lives outside
            // any known mod root), the pack must not participate in any
            // collision group — better to under-flag than risk a
            // spurious redirect.
            FuseAssetCollisionRegistry.Reset();
            var orphanA = @"C:\elsewhere\orphan\spinecar1";
            var orphanB = $@"{ModsRoot}\TOFC Cars\spinecar1";

            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { orphanA, orphanB },
                _ => "Mod",
                pack => HostMod(pack));

            Assert.Empty(collisions);
        }

        [Fact]
        public void Duplicate_folder_in_input_is_only_counted_once()
        {
            FuseAssetCollisionRegistry.Reset();
            var same = $@"{ModsRoot}\Mod\spinecar1";
            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { same, same, same },
                _ => "Mod",
                pack => HostMod(pack));

            // Three references to the same folder = one group member,
            // which does not constitute a collision.
            Assert.Empty(collisions);
        }

        // ===== Multi-pack scenarios =====

        [Fact]
        public void Three_packs_same_leaf_yield_one_winner_two_losers()
        {
            FuseAssetCollisionRegistry.Reset();
            var convA = $@"{ModsRoot}\Mod\SCAssetPacks\foo";
            var convB = $@"{ModsRoot}\Mod\SCAssetPacks\inner\SCAssetPacks\foo";
            var rootC = $@"{ModsRoot}\Mod\foo";

            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { convA, convB, rootC },
                _ => "Mod",
                pack => HostMod(pack));

            var collision = Assert.Single(collisions);
            Assert.Equal("foo", collision.SharedIdentifier);
            Assert.Equal(2, collision.LoserFolders.Count);
            // The root-level pack wins when present; both
            // SCAssetPacks-located variants are losers because that
            // folder is the legacy fallback convention.
            Assert.Equal(rootC, collision.WinnerFolder);
            Assert.Contains(convA, collision.LoserFolders);
            Assert.Contains(convB, collision.LoserFolders);
        }

        [Fact]
        public void Root_level_pack_wins_over_SCAssetPacks_sibling()
        {
            // Empirical convention: mod authors put the canonical
            // modern build at the mod root and keep the
            // SCAssetPacks/&lt;X&gt; folder as a legacy fallback.
            // The collision scanner reflects that — the root sibling
            // is the winner, the SCAssetPacks copy is the loser whose
            // car definitions get filtered out of the spawn pool.
            FuseAssetCollisionRegistry.Reset();
            var root = $@"{ModsRoot}\Mod\widget";
            var conv = $@"{ModsRoot}\Mod\SCAssetPacks\widget";

            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { root, conv },
                _ => "Mod",
                pack => HostMod(pack));

            var collision = Assert.Single(collisions);
            Assert.Equal(root, collision.WinnerFolder);
            Assert.Contains(conv, collision.LoserFolders);
        }

        [Fact]
        public void Input_order_does_not_affect_winner_choice()
        {
            FuseAssetCollisionRegistry.Reset();
            var root = $@"{ModsRoot}\Mod\widget";
            var conv = $@"{ModsRoot}\Mod\SCAssetPacks\widget";

            var collisions1 = FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { root, conv },
                _ => "Mod",
                pack => HostMod(pack));

            FuseAssetCollisionRegistry.Reset();

            var collisions2 = FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { conv, root },
                _ => "Mod",
                pack => HostMod(pack));

            Assert.Equal(collisions1.Count, collisions2.Count);
            Assert.Equal(collisions1[0].WinnerFolder, collisions2[0].WinnerFolder);
        }

        [Fact]
        public void Multiple_mods_each_with_internal_collisions_are_independent()
        {
            FuseAssetCollisionRegistry.Reset();
            var aRoot = $@"{ModsRoot}\ModA\foo";
            var aConv = $@"{ModsRoot}\ModA\SCAssetPacks\foo";
            var bRoot = $@"{ModsRoot}\ModB\foo";
            var bConv = $@"{ModsRoot}\ModB\SCAssetPacks\foo";

            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { aRoot, aConv, bRoot, bConv },
                pack => HostModName(pack),
                pack => HostMod(pack));

            // Two independent collision groups; each in its own host
            // mod. Root variant wins in both — the SCAssetPacks copy
            // is the legacy fallback in both.
            Assert.Equal(2, collisions.Count);
            Assert.Contains(collisions, c => c.WinnerFolder == aRoot);
            Assert.Contains(collisions, c => c.WinnerFolder == bRoot);
        }

        // ===== Lookup behavior =====

        [Fact]
        public void TryGetBundleRedirect_normalizes_input_path()
        {
            FuseAssetCollisionRegistry.Reset();
            var root = $@"{ModsRoot}\Mod\widget";
            var conv = $@"{ModsRoot}\Mod\SCAssetPacks\widget";

            FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { root, conv },
                _ => "Mod",
                pack => HostMod(pack));

            // Same logical loser path (SCAssetPacks variant),
            // different surface form: trailing slash, mixed
            // separators. Must resolve to the same redirect target.
            Assert.True(FuseAssetCollisionRegistry.TryGetBundleRedirect(conv + @"\", out var redirect1));
            Assert.True(FuseAssetCollisionRegistry.TryGetBundleRedirect(conv.Replace('\\', '/'), out var redirect2));
            Assert.Equal(redirect1, redirect2);
        }

        [Fact]
        public void TryGetBundleRedirect_returns_false_for_unknown_folder()
        {
            FuseAssetCollisionRegistry.Reset();
            Assert.False(FuseAssetCollisionRegistry.TryGetBundleRedirect($@"{ModsRoot}\Nothing\here", out _));
        }

        [Fact]
        public void TryGetBundleRedirect_returns_false_for_winner_path()
        {
            FuseAssetCollisionRegistry.Reset();
            var root = $@"{ModsRoot}\Mod\widget";
            var conv = $@"{ModsRoot}\Mod\SCAssetPacks\widget";

            FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { root, conv },
                _ => "Mod",
                pack => HostMod(pack));

            // Winner (root pack) participates in a collision but has
            // no redirect of its own (the sentinel case).
            Assert.False(FuseAssetCollisionRegistry.TryGetBundleRedirect(root, out _));
        }

        [Fact]
        public void TryGetBundleRedirect_returns_false_for_null_or_empty()
        {
            FuseAssetCollisionRegistry.Reset();
            Assert.False(FuseAssetCollisionRegistry.TryGetBundleRedirect(null, out _));
            Assert.False(FuseAssetCollisionRegistry.TryGetBundleRedirect(string.Empty, out _));
            Assert.False(FuseAssetCollisionRegistry.TryGetBundleRedirect("   ", out _));
        }

        [Fact]
        public void TryGetWinnerFolder_returns_winner_path_minus_Bundle_segment()
        {
            FuseAssetCollisionRegistry.Reset();
            var root = $@"{ModsRoot}\Mod\widget";
            var conv = $@"{ModsRoot}\Mod\SCAssetPacks\widget";

            FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { root, conv },
                _ => "Mod",
                pack => HostMod(pack));

            Assert.True(FuseAssetCollisionRegistry.TryGetWinnerFolder(conv, out var winnerFolder));
            Assert.Equal(root, winnerFolder);
        }

        // ===== Reset / lifecycle =====

        [Fact]
        public void Reset_when_already_empty_is_idempotent_noop()
        {
            FuseAssetCollisionRegistry.Reset();
            FuseAssetCollisionRegistry.Reset();
            FuseAssetCollisionRegistry.Reset();
            Assert.Empty(FuseAssetCollisionRegistry.CurrentCollisions);
        }

        [Fact]
        public void Reset_after_scan_clears_both_collisions_and_redirects()
        {
            FuseAssetCollisionRegistry.Reset();
            var root = $@"{ModsRoot}\Mod\widget";
            var conv = $@"{ModsRoot}\Mod\SCAssetPacks\widget";

            FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { root, conv },
                _ => "Mod",
                pack => HostMod(pack));

            Assert.NotEmpty(FuseAssetCollisionRegistry.CurrentCollisions);
            // SCAssetPacks copy is the loser; lookup returns a
            // redirect to the root variant's bundle.
            Assert.True(FuseAssetCollisionRegistry.TryGetBundleRedirect(conv, out _));

            FuseAssetCollisionRegistry.Reset();

            Assert.Empty(FuseAssetCollisionRegistry.CurrentCollisions);
            Assert.False(FuseAssetCollisionRegistry.TryGetBundleRedirect(conv, out _));
            Assert.False(FuseAssetCollisionRegistry.TryGetWinnerFolder(conv, out _));
        }

        [Fact]
        public void Repeated_scan_appends_more_collisions_until_Reset()
        {
            // Two consecutive scans without an intervening Reset must
            // not cause the second scan to wipe the first scan's data.
            // (Reset is the explicit "start over" boundary.)
            FuseAssetCollisionRegistry.Reset();
            FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { $@"{ModsRoot}\ModA\foo", $@"{ModsRoot}\ModA\SCAssetPacks\foo" },
                _ => "ModA",
                pack => HostMod(pack));
            FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { $@"{ModsRoot}\ModB\bar", $@"{ModsRoot}\ModB\SCAssetPacks\bar" },
                _ => "ModB",
                pack => HostMod(pack));

            Assert.Equal(2, FuseAssetCollisionRegistry.CurrentCollisions.Count);
        }

        // ===== CurrentCollisions snapshot semantics =====

        [Fact]
        public void CurrentCollisions_returns_snapshot_not_live_view()
        {
            FuseAssetCollisionRegistry.Reset();
            var root = $@"{ModsRoot}\Mod\widget";
            var conv = $@"{ModsRoot}\Mod\SCAssetPacks\widget";

            FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { root, conv },
                _ => "Mod",
                pack => HostMod(pack));

            var snapshot = FuseAssetCollisionRegistry.CurrentCollisions;
            FuseAssetCollisionRegistry.Reset();
            // Snapshot must not see the post-Reset mutation.
            Assert.Single(snapshot);
        }

        // ===== Integration with FuseRegistry shared claims =====

        [Fact]
        public void Scan_records_one_shared_claim_per_participant()
        {
            FuseAssetCollisionRegistry.Reset();
            var root = $@"{ModsRoot}\Widgets\widget";
            var conv = $@"{ModsRoot}\Widgets\SCAssetPacks\widget";

            FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { root, conv },
                _ => "Widgets",
                pack => HostMod(pack));

            var owners = FUSE.Runtime.Registry.FuseRegistry.GetSharedOwners(
                FUSE.Runtime.Registry.FuseClaimKind.AssetCollision, "widget");

            // Both pack folders contribute the same owner label ("Widgets"
            // from the test stub), de-duped by the shared-owners HashSet.
            Assert.Single(owners);
            Assert.Contains("Widgets", owners);
        }

        [Fact]
        public void Reset_releases_recorded_FuseRegistry_claims()
        {
            FuseAssetCollisionRegistry.Reset();
            var root = $@"{ModsRoot}\Widgets\widget";
            var conv = $@"{ModsRoot}\Widgets\SCAssetPacks\widget";

            FuseAssetCollisionRegistry.ScanForCollisions(
                new[] { root, conv },
                _ => "Widgets",
                pack => HostMod(pack));

            Assert.NotEmpty(FUSE.Runtime.Registry.FuseRegistry.GetSharedOwners(
                FUSE.Runtime.Registry.FuseClaimKind.AssetCollision, "widget"));

            FuseAssetCollisionRegistry.Reset();

            // Reset must drop every claim the scan recorded — otherwise
            // FuseRegistry state leaks across game-session reloads.
            Assert.Empty(FUSE.Runtime.Registry.FuseRegistry.GetSharedOwners(
                FUSE.Runtime.Registry.FuseClaimKind.AssetCollision, "widget"));
        }

        // ===== Construction / data class invariants =====

        [Fact]
        public void FuseAssetCollision_constructor_normalizes_nulls_to_empty_or_empty_list()
        {
            // The data class should not throw when handed nulls — every
            // collision goes through reporting surfaces that expect
            // non-null values.
            var collision = new FUSE.Authoring.Data.FuseAssetCollision(null, null, null, null);
            Assert.Equal(string.Empty, collision.SharedIdentifier);
            Assert.Equal(string.Empty, collision.WinnerFolder);
            Assert.Equal(string.Empty, collision.WinnerBundlePath);
            Assert.NotNull(collision.LoserFolders);
            Assert.Empty(collision.LoserFolders);
        }

        [Fact]
        public void FuseAssetCollision_constructor_preserves_supplied_values()
        {
            var collision = new FUSE.Authoring.Data.FuseAssetCollision(
                "shared-id",
                @"C:\winner",
                @"C:\winner\Bundle",
                new[] { @"C:\loser1", @"C:\loser2" });

            Assert.Equal("shared-id", collision.SharedIdentifier);
            Assert.Equal(@"C:\winner", collision.WinnerFolder);
            Assert.Equal(@"C:\winner\Bundle", collision.WinnerBundlePath);
            Assert.Equal(2, collision.LoserFolders.Count);
        }

        // Helper: climb a synthetic path until the parent is ModsRoot.
        private static string HostMod(string pack)
        {
            var cursor = pack;
            while (true)
            {
                var parent = Path.GetDirectoryName(cursor);
                if (string.IsNullOrEmpty(parent))
                {
                    return null;
                }
                if (string.Equals(parent, ModsRoot, System.StringComparison.OrdinalIgnoreCase))
                {
                    return cursor;
                }
                cursor = parent;
            }
        }

        private static string HostModName(string pack)
        {
            var host = HostMod(pack);
            return host == null ? null : Path.GetFileName(host);
        }
    }
}
