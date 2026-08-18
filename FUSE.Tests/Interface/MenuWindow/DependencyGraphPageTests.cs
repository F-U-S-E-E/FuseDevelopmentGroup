using System;
using System.Collections.Generic;
using FUSE.Interface.MenuWindow;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Interface.MenuWindow
{
    /// <summary>
    /// Issues #207 / #223: the Dependency Graph page painted every load-order
    /// target that is not a discovered FUSE data package as red MISSING — even
    /// installed asset-only packs / code-only plugins, and even the advisory
    /// hints on legacy-converted packages that the loader deliberately ignores.
    /// </summary>
    public sealed class DependencyGraphPageTests
    {
        private static readonly IDictionary<string, FusePackageManifestSnapshot> Packages =
            new Dictionary<string, FusePackageManifestSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ready.Pkg"] = new FusePackageManifestSnapshot { Id = "Ready.Pkg" },
                ["Disabled.Pkg"] = new FusePackageManifestSnapshot { Id = "Disabled.Pkg", Disabled = true },
            };

        private static readonly ICollection<string> PresentInModsRoot =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "C_L_B.ASSETS01" };

        [Fact]
        public void DiscoveredEnabledPackage_IsReady()
        {
            Assert.Equal(
                "Ready.Pkg | <color=\"green\">READY",
                DependencyGraphPage.FormatDependencyEdge("Ready.Pkg", Packages, PresentInModsRoot, advisory: false));
        }

        [Fact]
        public void DiscoveredDisabledPackage_IsDisabled_OrOptionalWhenAdvisory()
        {
            Assert.Equal(
                "Disabled.Pkg | <color=\"yellow\">DISABLED",
                DependencyGraphPage.FormatDependencyEdge("Disabled.Pkg", Packages, PresentInModsRoot, advisory: false));
            Assert.Equal(
                "Disabled.Pkg | <color=\"grey\">DISABLED (optional hint)",
                DependencyGraphPage.FormatDependencyEdge("Disabled.Pkg", Packages, PresentInModsRoot, advisory: true));
        }

        [Fact]
        public void InstalledAssetOrPluginMod_IsPresent_NotMissing()
        {
            Assert.Equal(
                "C_L_B.ASSETS01 | <color=\"green\">PRESENT (asset/plugin mod)",
                DependencyGraphPage.FormatDependencyEdge("C_L_B.ASSETS01", Packages, PresentInModsRoot, advisory: false));
            // Case-insensitive like the discovery matcher.
            Assert.Equal(
                "c_l_b.assets01 | <color=\"green\">PRESENT (asset/plugin mod)",
                DependencyGraphPage.FormatDependencyEdge("c_l_b.assets01", Packages, PresentInModsRoot, advisory: true));
        }

        [Fact]
        public void UnknownTarget_IsMissing_ForNativePackages_ButOptionalForLegacyHints()
        {
            Assert.Equal(
                "Nope.Pkg | <color=\"red\">MISSING",
                DependencyGraphPage.FormatDependencyEdge("Nope.Pkg", Packages, PresentInModsRoot, advisory: false));
            Assert.Equal(
                "Nope.Pkg | <color=\"grey\">NOT INSTALLED (optional hint)",
                DependencyGraphPage.FormatDependencyEdge("Nope.Pkg", Packages, PresentInModsRoot, advisory: true));
        }

        [Fact]
        public void BlankTarget_IsAlwaysMissing()
        {
            Assert.Equal(
                "(blank) | <color=\"red\">MISSING",
                DependencyGraphPage.FormatDependencyEdge(" ", Packages, PresentInModsRoot, advisory: true));
            Assert.Equal(
                "(blank) | <color=\"red\">MISSING",
                DependencyGraphPage.FormatDependencyEdge(null, null, null, advisory: false));
        }
    }
}
