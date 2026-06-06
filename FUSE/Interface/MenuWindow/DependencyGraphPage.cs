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
            var rows = 0;
            foreach (var manifest in manifests)
            {
                var hasEdges = manifest.LoadAfter.Length > 0 || manifest.LoadBefore.Length > 0 || manifest.Faults.Length > 0;
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
                foreach (var dependencyId in manifest.LoadAfter)
                {
                    builder.AddField("load after", builder.AddLabelMarkup(InsertBreakHints(FormatDependencyEdge(dependencyId, byId))));
                    rows++;
                }

                foreach (var dependencyId in manifest.LoadBefore)
                {
                    builder.AddField("load before", builder.AddLabelMarkup(InsertBreakHints(FormatDependencyEdge(dependencyId, byId))));
                    rows++;
                }

                foreach (var fault in manifest.Faults)
                {
                    builder.AddField("fault detected", builder.AddLabelMarkup(InsertBreakHints(FormatDependencyEdge(fault, byId))));
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

        private static string FormatDependencyEdge(string dependencyId, IDictionary<string, FusePackageManifestSnapshot> packages)
        {
            if (string.IsNullOrWhiteSpace(dependencyId))
            {
                return "(blank) | <color=\"red\">MISSING";
            }

            if (packages != null && packages.TryGetValue(dependencyId, out var dependency))
            {
                return dependency.Disabled
                    ? dependencyId + " | <color=\"yellow\">DISABLED"
                    : dependencyId + " | <color=\"green\">READY";
            }

            return dependencyId + " | <color=\"red\">MISSING";
        }
    }
}
