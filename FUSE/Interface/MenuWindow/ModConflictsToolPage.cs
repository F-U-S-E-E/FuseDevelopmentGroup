using FUSE.Runtime.Registry;
using FUSE.Loading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UI.Builder;
using UI.Common;
using UnityEngine;
using static FUSE.Interface.InterfaceUtils;

namespace FUSE.Interface.MenuWindow
{
    /// <summary>
    /// Human-readable package-pair view over the registry's object-level
    /// conflict history. The raw history remains available through
    /// /fuse.conflicts and the health report.
    /// </summary>
    internal static class ModConflictsToolPage
    {
        internal sealed class ConflictGroup
        {
            public string FirstPackageId { get; set; }
            public string SecondPackageId { get; set; }
            public FuseRegistryConflict[] Conflicts { get; set; }
        }

        internal sealed class DeclaredConflictMatch
        {
            public FusePackageManifestSnapshot DeclaringPackage { get; set; }
            public FusePackageManifestSnapshot ConflictingPackage { get; set; }
            public FUSE.Authoring.Data.FuseModRequirement Reference { get; set; }
        }

        public static void Build(UIPanelBuilder builder)
        {
            builder.AddTitle("Mod Conflicts", "");
            AddWrappedLabel(
                builder,
                "Packages are grouped in pairs so you can see which mods are changing the same runtime objects. " +
                "Declared requires/loadAfter/loadBefore and conditional mixinto layering is expected and is not listed here. " +
                "FUSE keeps unrelated mods unless the resolution below says that one definition won. Spatial track overlap is advisory because nearby track can be intentional.",
                62f);

            var registryRecords = FuseRegistry.Conflicts.ToArray();
            var cooperativeRecords = registryRecords.Where(conflict => conflict.IsCooperativeMerge).ToArray();
            var registryConflicts = registryRecords.Where(conflict => !conflict.IsCooperativeMerge).ToArray();
            var spatialConflicts = FuseSpatialTrackConflictDetector.Conflicts.ToArray();
            var packageSnapshots = (FuseDataPackageDiscovery.GetPackageManifestSnapshots() ?? Array.Empty<FusePackageManifestSnapshot>())
                .ToArray();
            var knownPackageIds = packageSnapshots
                .Select(package => package.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();
            var groups = BuildGroups(registryConflicts.Concat(spatialConflicts), knownPackageIds);
            var cooperativeGroups = BuildGroups(cooperativeRecords, knownPackageIds);
            var declaredConflicts = BuildDeclaredConflictMatches(packageSnapshots);
            builder.AddSection("Overview");
            AddValueField(builder, "Pairs Needing Attention", groups.Count.ToString());
            AddValueField(builder, "Declared Incompatibilities", declaredConflicts.Count.ToString());
            AddValueField(builder, "Ownership Conflicts", registryConflicts.Length.ToString());
            AddValueField(builder, "Shared Extension Targets", cooperativeRecords.Length.ToString());
            AddValueField(builder, "Spatial Warnings", spatialConflicts.Length.ToString());

            builder.Spacer(8f);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Refresh", builder.Rebuild);
                row.AddButtonCompact("Copy Full Report", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildReport(groups, declaredConflicts, cooperativeGroups);
                    Toast.Present("Copied FUSE mod conflict report to clipboard.");
                });
            }, 6f).Height(32f);
            builder.Spacer(8f);

            if (groups.Count == 0 && declaredConflicts.Count == 0 && cooperativeGroups.Count == 0)
            {
                builder.AddField("Status", builder.AddLabelMarkup("<color=\"green\">No mod ownership conflicts have been recorded in this session."));
                return;
            }

            if (declaredConflicts.Count > 0)
            {
                builder.AddSection("Author-Declared Incompatibilities");
                AddWrappedLabel(
                    builder,
                    "These pairs come from a package's conflictsWith declaration, not from FUSE guessing at nearby track. " +
                    "The declaring package is skipped while the matching incompatible package/version is enabled.",
                    48f);
                foreach (var match in declaredConflicts.Take(30))
                {
                    AddWrappedField(builder, "Skipped Package", InsertBreakHints(match.DeclaringPackage.Id), 34f);
                    AddWrappedField(builder, "Conflicts With", InsertBreakHints(match.ConflictingPackage.Id), 34f);
                    AddWrappedField(builder, "Versions", FormatDeclaredConflict(match), 42f);
                    builder.Spacer(6f);
                }

                if (groups.Count == 0 && cooperativeGroups.Count == 0)
                {
                    return;
                }
            }

            if (groups.Count > 0)
            {
                builder.AddSection("Runtime Ownership Needing Attention");
                foreach (var group in groups.Take(30))
                {
                    AddWrappedField(builder, "Mod A", InsertBreakHints(group.FirstPackageId), 34f);
                    AddWrappedField(builder, "Mod B", InsertBreakHints(group.SecondPackageId), 34f);
                    builder.AddField("Records", group.Conflicts.Length.ToString());
                    builder.AddField("Object Types", FormatKinds(group.Conflicts));
                    builder.AddField("Result", DescribeGroupResult(group.Conflicts));

                    foreach (var conflict in group.Conflicts.Take(12))
                    {
                        AddWrappedField(builder, conflict.Kind.ToString(), FormatConflict(conflict), 54f);
                    }

                    if (group.Conflicts.Length > 12)
                    {
                        AddWrappedField(
                            builder,
                            "More",
                            (group.Conflicts.Length - 12) + " additional records are available in Copy Full Report or /fuse.conflicts.",
                            34f);
                    }

                    builder.Spacer(8f);
                    builder.AddHRule();
                    builder.Spacer(8f);
                }

                if (groups.Count > 30)
                {
                    AddWrappedField(
                        builder,
                        "More Pairs",
                        (groups.Count - 30) + " additional package pairs are available in Copy Full Report or /fuse.conflicts.",
                        34f);
                }
            }

            if (cooperativeGroups.Count > 0)
            {
                builder.AddSection("Shared Extension Targets (Informational)");
                AddWrappedLabel(
                    builder,
                    "These mods touched the same cumulative industry target and FUSE merged their contributions. " +
                    "Nothing was skipped or replaced, so these records do not count as conflicts or load-health problems.",
                    50f);
                foreach (var group in cooperativeGroups.Take(30))
                {
                    AddWrappedField(builder, "Mod A", InsertBreakHints(group.FirstPackageId), 34f);
                    AddWrappedField(builder, "Mod B", InsertBreakHints(group.SecondPackageId), 34f);
                    builder.AddField("Shared Records", group.Conflicts.Length.ToString());
                    builder.AddField("Object Types", FormatKinds(group.Conflicts));
                    builder.AddField("Result", "Definitions merged successfully; no mod lost content.");
                    foreach (var conflict in group.Conflicts.Take(12))
                    {
                        AddWrappedField(builder, conflict.Kind.ToString(), FormatConflict(conflict), 54f);
                    }
                    builder.Spacer(8f);
                    builder.AddHRule();
                    builder.Spacer(8f);
                }
            }
        }

        internal static List<ConflictGroup> BuildGroups(
            IEnumerable<FuseRegistryConflict> conflicts,
            IEnumerable<string> knownPackageIds = null)
        {
            var packageRoots = (knownPackageIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(id => id.Length)
                .ToArray();
            return (conflicts ?? Enumerable.Empty<FuseRegistryConflict>())
                .Where(conflict => conflict != null)
                .Select(conflict => new
                {
                    Conflict = conflict,
                    First = FindPackageRoot(conflict.OwnerPackageId, packageRoots),
                    Second = FindPackageRoot(conflict.AttemptedPackageId, packageRoots)
                })
                .GroupBy(item => BuildPairKey(item.First, item.Second), StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var first = group.First();
                    OrderPackagePair(first.First, first.Second, out var firstId, out var secondId);
                    return new ConflictGroup
                    {
                        FirstPackageId = firstId,
                        SecondPackageId = secondId,
                        Conflicts = group
                            .Select(item => item.Conflict)
                            .OrderBy(conflict => conflict.Kind)
                            .ThenBy(conflict => conflict.Id, StringComparer.OrdinalIgnoreCase)
                            .ToArray()
                    };
                })
                .OrderByDescending(group => group.Conflicts.Length)
                .ThenBy(group => group.FirstPackageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.SecondPackageId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static string BuildReport(IEnumerable<ConflictGroup> groups)
        {
            return BuildReport(groups, Enumerable.Empty<DeclaredConflictMatch>());
        }

        internal static string BuildReport(
            IEnumerable<ConflictGroup> groups,
            IEnumerable<DeclaredConflictMatch> declaredConflicts)
        {
            return BuildReport(groups, declaredConflicts, Enumerable.Empty<ConflictGroup>());
        }

        internal static string BuildReport(
            IEnumerable<ConflictGroup> groups,
            IEnumerable<DeclaredConflictMatch> declaredConflicts,
            IEnumerable<ConflictGroup> cooperativeGroups)
        {
            var materialized = (groups ?? Enumerable.Empty<ConflictGroup>()).ToArray();
            var declared = (declaredConflicts ?? Enumerable.Empty<DeclaredConflictMatch>()).ToArray();
            var cooperative = (cooperativeGroups ?? Enumerable.Empty<ConflictGroup>()).ToArray();
            var sb = new StringBuilder();
            sb.AppendLine("FUSE Mod Conflict Breakdown");
            sb.AppendLine("Package pairs needing attention: " + materialized.Length);
            sb.AppendLine("Actionable conflict/warning records: " + materialized.Sum(group => group.Conflicts?.Length ?? 0));
            sb.AppendLine("Author-declared incompatibilities: " + declared.Length);
            sb.AppendLine("Informational shared-extension records: " + cooperative.Sum(group => group.Conflicts?.Length ?? 0));
            foreach (var match in declared)
            {
                sb.AppendLine();
                sb.AppendLine("DECLARED: " + match.DeclaringPackage.Id + "  X  " + match.ConflictingPackage.Id);
                sb.AppendLine("  " + FormatDeclaredConflict(match));
            }

            foreach (var group in materialized)
            {
                sb.AppendLine();
                sb.AppendLine(group.FirstPackageId + "  <->  " + group.SecondPackageId);
                sb.AppendLine("  object types: " + FormatKinds(group.Conflicts));
                sb.AppendLine("  result: " + DescribeGroupResult(group.Conflicts));
                foreach (var conflict in group.Conflicts ?? Array.Empty<FuseRegistryConflict>())
                {
                    var id = string.IsNullOrWhiteSpace(conflict?.Id) ? "(unknown target)" : conflict.Id;
                    var resolution = string.IsNullOrWhiteSpace(conflict?.Resolution) ? "No resolution recorded." : conflict.Resolution;
                    sb.AppendLine(
                        "  - " + conflict.Kind + " " + id + " — " + resolution +
                        " [" + conflict.OwnerPackageId + " -> " + conflict.AttemptedPackageId + "]");
                }
            }

            foreach (var group in cooperative)
            {
                sb.AppendLine();
                sb.AppendLine("SHARED: " + group.FirstPackageId + "  +  " + group.SecondPackageId);
                sb.AppendLine("  result: definitions merged successfully; no mod lost content");
                foreach (var conflict in group.Conflicts ?? Array.Empty<FuseRegistryConflict>())
                {
                    sb.AppendLine("  - " + conflict.Kind + " " + conflict.Id + " — " + conflict.Resolution);
                }
            }

            return sb.ToString().TrimEnd();
        }

        internal static List<DeclaredConflictMatch> BuildDeclaredConflictMatches(
            IEnumerable<FusePackageManifestSnapshot> packages)
        {
            var materialized = (packages ?? Enumerable.Empty<FusePackageManifestSnapshot>())
                .Where(package => package != null && !string.IsNullOrWhiteSpace(package.Id))
                .ToArray();
            var result = new List<DeclaredConflictMatch>();
            foreach (var declaring in materialized)
            {
                foreach (var reference in declaring.ConflictsWith ?? Array.Empty<FUSE.Authoring.Data.FuseModRequirement>())
                {
                    var target = materialized.FirstOrDefault(candidate =>
                        !ReferenceEquals(candidate, declaring) &&
                        FuseDataPackageDiscovery.IsDeclaredConflictMatch(
                            reference,
                            candidate.Id,
                            candidate.Version,
                            candidate.Disabled));
                    if (target == null)
                    {
                        continue;
                    }

                    result.Add(new DeclaredConflictMatch
                    {
                        DeclaringPackage = declaring,
                        ConflictingPackage = target,
                        Reference = reference
                    });
                }
            }

            return result
                .GroupBy(match =>
                    match.DeclaringPackage.Id + "\0" + match.ConflictingPackage.Id,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(match => match.DeclaringPackage.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(match => match.ConflictingPackage.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string FormatDeclaredConflict(DeclaredConflictMatch match)
        {
            var reference = match?.Reference;
            var installedVersion = match?.ConflictingPackage?.Version ?? string.Empty;
            var bounds = string.IsNullOrWhiteSpace(reference?.NotBefore) && string.IsNullOrWhiteSpace(reference?.NotAfter)
                ? "all versions"
                : "notBefore=" + (reference?.NotBefore ?? "(none)") + ", notAfter=" + (reference?.NotAfter ?? "(none)");
            return "Installed version " + (string.IsNullOrWhiteSpace(installedVersion) ? "(unknown)" : installedVersion) +
                   " matches " + bounds + ".";
        }

        internal static bool IsSpatialConflict(FuseRegistryConflict conflict)
        {
            return conflict != null &&
                ((conflict.Id ?? string.Empty).StartsWith("spatial-overlap:", StringComparison.OrdinalIgnoreCase) ||
                 (conflict.Resolution ?? string.Empty).IndexOf("spatial", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string DescribeGroupResult(IEnumerable<FuseRegistryConflict> conflicts)
        {
            var materialized = (conflicts ?? Enumerable.Empty<FuseRegistryConflict>()).ToArray();
            if (materialized.Any(IsSpatialConflict))
            {
                return "Potential track-layout overlap; both mods retained for the author/user to resolve.";
            }

            if (materialized.Any(conflict => ContainsAny(conflict.Resolution, "won", "suppressed", "skipped", "retained")))
            {
                return "At least one conflicting operation was skipped or replaced; see each record below.";
            }

            return "Both mods contributed to the same target; FUSE retained the shared merge.";
        }

        private static string FormatKinds(IEnumerable<FuseRegistryConflict> conflicts)
        {
            return string.Join(", ", (conflicts ?? Enumerable.Empty<FuseRegistryConflict>())
                .Select(conflict => conflict.Kind.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase));
        }

        private static string FormatConflict(FuseRegistryConflict conflict)
        {
            var id = string.IsNullOrWhiteSpace(conflict?.Id) ? "(unknown target)" : conflict.Id;
            var resolution = string.IsNullOrWhiteSpace(conflict?.Resolution) ? "No resolution recorded." : conflict.Resolution;
            var definitions = string.IsNullOrWhiteSpace(conflict?.OwnerPackageId) && string.IsNullOrWhiteSpace(conflict?.AttemptedPackageId)
                ? string.Empty
                : " [" + conflict.OwnerPackageId + " -> " + conflict.AttemptedPackageId + "]";
            return InsertBreakHints(id) + " — " + resolution + InsertBreakHints(definitions);
        }

        private static string FindPackageRoot(string definitionId, IEnumerable<string> packageRoots)
        {
            definitionId = string.IsNullOrWhiteSpace(definitionId) ? "(unknown package)" : definitionId.Trim();
            foreach (var root in packageRoots ?? Enumerable.Empty<string>())
            {
                if (string.Equals(definitionId, root, StringComparison.OrdinalIgnoreCase) ||
                    definitionId.StartsWith(root + ".", StringComparison.OrdinalIgnoreCase))
                {
                    return root;
                }
            }

            return definitionId;
        }

        private static string BuildPairKey(string first, string second)
        {
            OrderPackagePair(first, second, out var orderedFirst, out var orderedSecond);
            return orderedFirst + "\0" + orderedSecond;
        }

        private static void OrderPackagePair(string first, string second, out string orderedFirst, out string orderedSecond)
        {
            first = string.IsNullOrWhiteSpace(first) ? "(unknown package)" : first.Trim();
            second = string.IsNullOrWhiteSpace(second) ? "(unknown package)" : second.Trim();
            if (StringComparer.OrdinalIgnoreCase.Compare(first, second) <= 0)
            {
                orderedFirst = first;
                orderedSecond = second;
            }
            else
            {
                orderedFirst = second;
                orderedSecond = first;
            }
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                terms.Any(term => value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
