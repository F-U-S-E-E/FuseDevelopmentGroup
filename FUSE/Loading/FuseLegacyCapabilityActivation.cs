using System;
using System.Collections.Generic;
using System.Linq;

namespace FUSE.Loading
{
    /// <summary>
    /// Determines whether a non-identity gameplay replacement was actually
    /// requested by an enabled package. This lets FUSE advertise a retired
    /// dependency without globally turning that mod's gameplay choices on for
    /// every player.
    /// </summary>
    internal static class FuseLegacyCapabilityActivation
    {
        private static readonly object Gate = new object();
        private static HashSet<string> _requested;

        internal static bool IsRequested(params string[] packageIds)
        {
            var requested = GetRequestedIds();
            return (packageIds ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(FuseReplacementCapabilityCatalog.Normalize)
                .Any(requested.Contains);
        }

        internal static void Reset()
        {
            lock (Gate)
            {
                _requested = null;
            }
        }

        private static HashSet<string> GetRequestedIds()
        {
            lock (Gate)
            {
                if (_requested != null)
                {
                    return _requested;
                }

                _requested = BuildRequestedIds(FuseDataPackageDiscovery.GetPackageManifestSnapshots());
                return _requested;
            }
        }

        internal static HashSet<string> BuildRequestedIds(
            IEnumerable<FusePackageManifestSnapshot> manifests)
        {
            var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var manifest in manifests ?? Enumerable.Empty<FusePackageManifestSnapshot>())
            {
                if (manifest == null || manifest.Disabled)
                {
                    continue;
                }

                Add(requested, manifest.Id);
                foreach (var requirement in manifest.RequiredPackageIds ?? Array.Empty<string>())
                {
                    Add(requested, requirement);
                }
            }

            return requested;
        }

        private static void Add(HashSet<string> requested, string packageId)
        {
            var normalized = FuseReplacementCapabilityCatalog.Normalize(packageId);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                requested.Add(normalized);
            }
        }
    }
}
