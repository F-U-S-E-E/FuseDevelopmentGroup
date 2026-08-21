using FUSE.Infrastructure;
using FUSE.Loading;
using System;
using System.Collections.Generic;
using System.Linq;
using UI.Builder;
using static FUSE.Interface.InterfaceUtils;

namespace FUSE.Interface.MenuWindow
{
    internal struct DependencyGraphPage
    {
        public static void Build(UIPanelBuilder builder)
        {
            builder.AddTitle("Mod Dependency Graph", "");

            builder.AddLabel("This page shows mod dependencies with load order requirements and any detected faults.");
            AddWrappedLabel(
                builder,
                "Legacy (converted) packages list soft load-order hints: a hint whose target is not installed is optional and does not block loading. " +
                "Asset-only packs and code-only plugins satisfy a dependency without being FUSE data packages. Retired package IDs whose runtime capability is replaced by FUSE are labeled PROVIDED BY FUSE.",
                48f);

            builder.Spacer(24f);

            var manifests = FuseDataPackageDiscovery.GetPackageManifestSnapshots();
            if (manifests == null || manifests.Count == 0)
            {
                builder.AddField("Dependencies", "No packages discovered");
                return;
            }

            var byId = manifests
                .GroupBy(manifest => manifest.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            // Resolve every edge target that is not a discovered data package
            // against the Mods root once, so installed asset-only packs and
            // hosted code-only plugins render as PRESENT rather than MISSING
            // (issues #207, #223).
            var undiscovered = manifests
                .SelectMany(manifest => manifest.RequiredPackageIds.Concat(manifest.LoadAfter).Concat(manifest.LoadBefore))
                .Where(id => !string.IsNullOrWhiteSpace(id) && !byId.ContainsKey(id));
            var presentInModsRoot = FuseDataPackageDiscovery.ResolvePackagesPresentInModsRoot(undiscovered);
            var rows = 0;
            foreach (var manifest in manifests)
            {
                var hasEdges = manifest.RequiredPackageIds.Length > 0 || manifest.LoadAfter.Length > 0 || manifest.LoadBefore.Length > 0 || manifest.Faults.Length > 0;
                if (!hasEdges && !FuseSettings.ShowAdvancedHealthDetails)
                {
                    continue;
                }

                if (!hasEdges)
                {
                    continue;
                }

                builder.FieldLabelWidth = 120f;
                AddWrappedLabel(builder, InsertBreakHints(manifest.Id), 28f);
                foreach (var dependencyId in manifest.RequiredPackageIds)
                {
                    builder.AddField("requires", builder.AddLabelMarkup(InsertBreakHints(FormatDependencyEdge(dependencyId, byId, presentInModsRoot, advisory: false))));
                    rows++;
                }

                foreach (var dependencyId in manifest.LoadAfter)
                {
                    builder.AddField("load after", builder.AddLabelMarkup(InsertBreakHints(FormatDependencyEdge(dependencyId, byId, presentInModsRoot, manifest.IsLegacyConverted))));
                    rows++;
                }

                foreach (var dependencyId in manifest.LoadBefore)
                {
                    builder.AddField("load before", builder.AddLabelMarkup(InsertBreakHints(FormatDependencyEdge(dependencyId, byId, presentInModsRoot, manifest.IsLegacyConverted))));
                    rows++;
                }

                foreach (var fault in manifest.Faults)
                {
                    builder.AddField("fault detected", builder.AddLabelMarkup(InsertBreakHints(FormatFault(fault))));
                    rows++;
                }

                builder.Spacer(8f);
                builder.AddHRule();
                builder.Spacer(8f);
            }

            if (rows == 0)
            {
                builder.AddField("Dependencies", "No package dependency edges in current profile");
            }
        }

        internal static string FormatDependencyEdge(
            string dependencyId,
            IDictionary<string, FusePackageManifestSnapshot> packages,
            ICollection<string> presentInModsRoot,
            bool advisory)
        {
            if (string.IsNullOrWhiteSpace(dependencyId))
            {
                return "(blank) | <color=\"red\">MISSING";
            }

            FusePackageManifestSnapshot dependency = null;
            if (packages != null)
            {
                packages.TryGetValue(dependencyId, out dependency);
                dependency = dependency ?? packages.Values.FirstOrDefault(candidate =>
                    FuseDeclaredPackageRelationship.SamePackageId(candidate?.Id, dependencyId));
            }

            if (dependency != null)
            {
                if (!dependency.Disabled)
                {
                    return dependencyId + " | <color=\"green\">READY";
                }

                return advisory
                    ? dependencyId + " | <color=\"grey\">DISABLED (optional hint)"
                    : dependencyId + " | <color=\"yellow\">DISABLED";
            }

            if (FuseReplacementCapabilityCatalog.IsProvided(dependencyId))
            {
                return dependencyId + " | <color=\"green\">PROVIDED BY FUSE";
            }

            if (presentInModsRoot != null && presentInModsRoot.Contains(dependencyId))
            {
                return dependencyId + " | <color=\"green\">PRESENT (asset/plugin mod)";
            }

            // Legacy-converted packages' loadAfter/loadBefore are advisory: the
            // loader ignores an unresolved target without recording a fault, so
            // the page must not paint it as an error either.
            return advisory
                ? dependencyId + " | <color=\"grey\">NOT INSTALLED (optional hint)"
                : dependencyId + " | <color=\"red\">MISSING";
        }

        private static string FormatFault(string fault)
        {
            return "<color=\"red\">" + (string.IsNullOrWhiteSpace(fault) ? "(blank)" : fault);
        }
    }
}
