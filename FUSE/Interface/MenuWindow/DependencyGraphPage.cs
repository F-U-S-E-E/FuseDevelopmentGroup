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

            builder.AddLabel("This page shows dependencies for FUSE data, UMM plugins, RailLoader packages, locomotives, railcars, and asset packs.");
            AddWrappedLabel(
                builder,
                "Legacy (converted) packages list soft load-order hints: a hint whose target is not installed is optional and does not block loading. " +
                "Local manifests are authoritative. Nexus requirements are cached by the installer for offline use and are labeled NEXUS CACHE. " +
                "Retired package IDs whose runtime capability is replaced by FUSE are labeled PROVIDED BY FUSE.",
                64f);

            builder.Spacer(24f);

            var packages = FuseInstalledDependencyCatalog.DiscoverInstalledPackages();
            if (packages == null || packages.Count == 0)
            {
                builder.AddField("Dependencies", "No packages discovered");
                return;
            }

            var rows = 0;
            foreach (var package in packages)
            {
                var hasEdges = package.HasEdges || package.Faults.Count > 0;
                if (!hasEdges && !FuseSettings.ShowAdvancedHealthDetails)
                {
                    continue;
                }

                if (!hasEdges)
                {
                    continue;
                }

                builder.FieldLabelWidth = 120f;
                var heading = package.Id + "  <color=\"grey\">[" + package.Category + " / " + package.ManifestSource + "]";
                AddWrappedLabel(builder, InsertBreakHints(heading), 28f);
                foreach (var dependency in package.Requirements)
                {
                    builder.AddField("requires", builder.AddLabelMarkup(InsertBreakHints(FormatDependencyEdge(dependency, packages, advisory: false))));
                    rows++;
                }

                foreach (var dependency in package.LoadAfter)
                {
                    builder.AddField("load after", builder.AddLabelMarkup(InsertBreakHints(FormatDependencyEdge(dependency, packages, package.IsLegacy))));
                    rows++;
                }

                foreach (var dependency in package.LoadBefore)
                {
                    builder.AddField("load before", builder.AddLabelMarkup(InsertBreakHints(FormatDependencyEdge(dependency, packages, package.IsLegacy))));
                    rows++;
                }

                foreach (var fault in package.Faults)
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
            FuseInstalledDependencyEdge edge,
            IEnumerable<FuseInstalledPackageDependencySnapshot> packages,
            bool advisory)
        {
            if (edge == null || string.IsNullOrWhiteSpace(edge.Id))
            {
                return "(blank) | <color=\"red\">MISSING";
            }

            var dependencyId = edge.Id.Trim();
            var dependencyLabel = string.IsNullOrWhiteSpace(edge.DisplayName)
                ? dependencyId
                : edge.DisplayName.Trim() + " (" + dependencyId + ")";
            var versionRange = FormatVersionRange(edge);
            var source = string.IsNullOrWhiteSpace(edge.Source) ? string.Empty : " <color=\"grey\">[" + edge.Source + "]";
            var dependency = FuseInstalledDependencyCatalog.FindInstalled(packages, dependencyId);
            if (dependency != null)
            {
                if (dependency.Disabled)
                {
                    return advisory
                        ? dependencyLabel + versionRange + " | <color=\"grey\">DISABLED (optional hint)" + source
                        : dependencyLabel + versionRange + " | <color=\"yellow\">DISABLED" + source;
                }

                var matches = FuseInstalledDependencyCatalog.VersionSatisfies(
                    dependency.Version,
                    edge.NotBefore,
                    edge.NotAfter,
                    out var versionReadable);
                var installedVersion = string.IsNullOrWhiteSpace(dependency.Version) ? string.Empty : " " + dependency.Version;
                if (!matches)
                {
                    if (!versionReadable && (!string.IsNullOrWhiteSpace(edge.NotBefore) || !string.IsNullOrWhiteSpace(edge.NotAfter)))
                    {
                        return dependencyLabel + versionRange + " | <color=\"yellow\">PRESENT; VERSION UNKNOWN" + source;
                    }

                    return dependencyLabel + versionRange + " | <color=\"red\">INCOMPATIBLE" + installedVersion + source;
                }

                return dependencyLabel + versionRange + " | <color=\"green\">READY" + installedVersion + source;
            }

            if (FuseReplacementCapabilityCatalog.IsProvided(dependencyId))
            {
                return dependencyLabel + versionRange + " | <color=\"green\">PROVIDED BY FUSE" + source;
            }

            return advisory
                ? dependencyLabel + versionRange + " | <color=\"grey\">NOT INSTALLED (optional hint)" + source
                : dependencyLabel + versionRange + " | <color=\"red\">MISSING" + source;
        }

        private static string FormatVersionRange(FuseInstalledDependencyEdge edge)
        {
            if (edge == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(edge.NotBefore) && !string.IsNullOrWhiteSpace(edge.NotAfter))
            {
                return " (" + edge.NotBefore + " to " + edge.NotAfter + ")";
            }

            if (!string.IsNullOrWhiteSpace(edge.NotBefore))
            {
                return " (>= " + edge.NotBefore + ")";
            }

            return string.IsNullOrWhiteSpace(edge.NotAfter) ? string.Empty : " (<= " + edge.NotAfter + ")";
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
