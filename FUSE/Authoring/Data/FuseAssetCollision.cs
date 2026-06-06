using System;
using System.Collections.Generic;

namespace FUSE.Authoring.Data
{
    /// <summary>
    /// Describes one asset-pack identifier collision discovered during pack
    /// discovery: multiple pack folders inside a single mod folder publish the
    /// same Catalog identifier (e.g. <c>"spinecar1"</c>) and were therefore
    /// almost certainly built with the same internal Unity AssetBundle
    /// manifest name. Unity will refuse the second <c>LoadFromFile</c> call
    /// on a bundle whose internal manifest is already loaded — exactly the
    /// <c>"another AssetBundle with the same files is already loaded"</c>
    /// failure mode that leaves base-only car definitions invisible when
    /// their bundle loses the load race to a sibling pack.
    ///
    /// <para>FUSE handles the collision by picking one folder as the
    /// <see cref="WinnerFolder"/> and redirecting the others' bundle loads
    /// to its <see cref="WinnerBundlePath"/>. Each loser's
    /// <c>Definitions.json</c> is still consulted (so the loser pack's
    /// definitions remain reachable in the definition registry), but every
    /// participating store ends up sharing a single AssetBundle reference
    /// in memory. Net effect: every definition resolves to a renderable
    /// prefab, at the cost of all participants sharing whichever pack's
    /// prefab Unity actually loaded.</para>
    ///
    /// <para>Mirrors the shape used by suppression records elsewhere in
    /// FUSE — there is a corresponding <c>FuseClaimKind.AssetCollision</c>
    /// claim per collision, with the catalog identifier as the claim id
    /// and every participating pack folder as a shared owner, so console
    /// surfaces like <c>/fuse.assets</c> can enumerate collisions the same
    /// way they enumerate suppressions.</para>
    /// </summary>
    public sealed class FuseAssetCollision
    {
        public FuseAssetCollision(
            string sharedIdentifier,
            string winnerFolder,
            string winnerBundlePath,
            IReadOnlyList<string> loserFolders)
        {
            SharedIdentifier = sharedIdentifier ?? string.Empty;
            WinnerFolder = winnerFolder ?? string.Empty;
            WinnerBundlePath = winnerBundlePath ?? string.Empty;
            LoserFolders = loserFolders ?? Array.Empty<string>();
        }

        /// <summary>
        /// The catalog identifier the participating packs all publish (the
        /// value of <c>Catalog.json</c>'s <c>identifier</c> field).
        /// </summary>
        public string SharedIdentifier { get; }

        /// <summary>
        /// Absolute filesystem path of the pack folder whose AssetBundle
        /// will load successfully and which every loser will redirect to.
        /// </summary>
        public string WinnerFolder { get; }

        /// <summary>
        /// Absolute filesystem path of the winner's <c>Bundle</c> file —
        /// the redirect target used by
        /// <c>FuseAssetPackRuntimeStoreAssetBundlePathPatch</c>.
        /// </summary>
        public string WinnerBundlePath { get; }

        /// <summary>
        /// Absolute filesystem paths of the pack folders whose bundles
        /// would fail Unity's duplicate-bundle check and are therefore
        /// redirected to <see cref="WinnerBundlePath"/>.
        /// </summary>
        public IReadOnlyList<string> LoserFolders { get; }
    }
}
