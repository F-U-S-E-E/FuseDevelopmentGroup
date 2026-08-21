using FUSE.Authoring.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FUSE.Loading
{
    /// <summary>
    /// Classifies an override as expected only when the resolved package graph
    /// placed the overriding package after a package it explicitly requires or
    /// names in loadAfter (or the base names it in loadBefore).
    /// </summary>
    internal static class FuseDeclaredPackageRelationship
    {
        internal static bool IsExpectedLaterOverride(
            FusePackageManifestSnapshot existingPackage,
            FusePackageManifestSnapshot laterPackage,
            FuseModDefinition laterDefinition = null)
        {
            if (existingPackage == null || laterPackage == null ||
                SamePackageId(existingPackage.Id, laterPackage.Id))
            {
                return false;
            }

            // Snapshot order is the final topological order. If both values are
            // populated, do not call a contradicted declaration "expected".
            if (existingPackage.Order > 0 && laterPackage.Order > 0 &&
                laterPackage.Order <= existingPackage.Order)
            {
                return false;
            }

            return ContainsPackageId(laterPackage.RequiredPackageIds, existingPackage.Id) ||
                   ContainsPackageId(laterPackage.LoadAfter, existingPackage.Id) ||
                   ContainsPackageId(existingPackage.LoadBefore, laterPackage.Id) ||
                   ContainsPackageId(
                       laterDefinition?.Mixinto?.Requires?.Select(requirement => requirement?.Id),
                       existingPackage.Id);
        }

        internal static bool ContainsPackageId(IEnumerable<string> values, string expected)
        {
            return !string.IsNullOrWhiteSpace(expected) &&
                   (values ?? Enumerable.Empty<string>()).Any(value => SamePackageId(value, expected));
        }

        internal static bool SamePackageId(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) &&
                   !string.IsNullOrWhiteSpace(right) &&
                   string.Equals(
                       FuseReplacementCapabilityCatalog.Normalize(left),
                       FuseReplacementCapabilityCatalog.Normalize(right),
                       StringComparison.OrdinalIgnoreCase);
        }
    }
}
