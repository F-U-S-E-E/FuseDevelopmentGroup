using FUSE.Authoring.Data;
using FUSE.Runtime.Registry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FUSE.Loading
{
    internal sealed class FuseSpatialTrackPackage
    {
        public string PackageId { get; set; }
        public string FolderPath { get; set; }
        public string[] RequiredPackageIds { get; set; } = Array.Empty<string>();
        public string[] LoadAfter { get; set; } = Array.Empty<string>();
        public string[] LoadBefore { get; set; } = Array.Empty<string>();
        public FuseTrackDefinition[] TrackDefinitions { get; set; } = Array.Empty<FuseTrackDefinition>();
    }

    /// <summary>
    /// Finds conservative, advisory overlap between independent track
    /// packages. This is deliberately separate from FuseRegistry: nearby
    /// custom track may be a legitimate extension, so these findings must not
    /// affect ownership, apply order, or the main Status conflict count.
    /// </summary>
    internal static class FuseSpatialTrackConflictDetector
    {
        private const float NearbyHorizontalDistance = 20f;
        private const float NearbyVerticalDistance = 10f;
        private static readonly object Sync = new object();
        private static FuseRegistryConflict[] _conflicts = Array.Empty<FuseRegistryConflict>();

        public static IReadOnlyList<FuseRegistryConflict> Conflicts
        {
            get
            {
                lock (Sync)
                {
                    return _conflicts.ToArray();
                }
            }
        }

        public static void Replace(IEnumerable<FuseSpatialTrackPackage> packages)
        {
            var detected = Detect(packages);
            lock (Sync)
            {
                _conflicts = detected;
            }
        }

        public static void Clear()
        {
            lock (Sync)
            {
                _conflicts = Array.Empty<FuseRegistryConflict>();
            }
        }

        internal static FuseRegistryConflict[] Detect(IEnumerable<FuseSpatialTrackPackage> packages)
        {
            var footprints = (packages ?? Enumerable.Empty<FuseSpatialTrackPackage>())
                .Select(BuildFootprint)
                .Where(footprint => footprint != null && footprint.Nodes.Length >= 2 && footprint.SegmentCount >= 2)
                .ToArray();
            var conflicts = new List<FuseRegistryConflict>();

            for (var leftIndex = 0; leftIndex < footprints.Length; leftIndex++)
            {
                var left = footprints[leftIndex];
                for (var rightIndex = leftIndex + 1; rightIndex < footprints.Length; rightIndex++)
                {
                    var right = footprints[rightIndex];
                    if (SamePackageFolder(left.FolderPath, right.FolderPath) ||
                        IsExpectedLayering(left, right) ||
                        !BoundsMayOverlap(left, right, NearbyHorizontalDistance))
                    {
                        continue;
                    }

                    var nearby = FindNearbyNodePairs(left.Nodes, right.Nodes);
                    if (nearby.Count < 2)
                    {
                        continue;
                    }

                    var centerX = nearby.Average(pair => (pair.Left.Position.x + pair.Right.Position.x) * 0.5f);
                    var centerZ = nearby.Average(pair => (pair.Left.Position.z + pair.Right.Position.z) * 0.5f);
                    conflicts.Add(new FuseRegistryConflict
                    {
                        Kind = FuseClaimKind.Segment,
                        Target = "SpatialTrackOverlap",
                        Id = $"spatial-overlap:{Math.Round(centerX)},{Math.Round(centerZ)}",
                        OwnerPackageId = left.PackageId,
                        AttemptedPackageId = right.PackageId,
                        Resolution =
                            $"spatial track overlap detected from {nearby.Count} nearby node pair(s) within {NearbyHorizontalDistance:0}m; " +
                            "both packages retained because proximity may be intentional",
                        AtUtc = DateTime.UtcNow
                    });
                }
            }

            return conflicts.ToArray();
        }

        private static TrackFootprint BuildFootprint(FuseSpatialTrackPackage package)
        {
            if (package == null || string.IsNullOrWhiteSpace(package.PackageId))
            {
                return null;
            }

            var nodes = new Dictionary<string, PositionedNode>(StringComparer.OrdinalIgnoreCase);
            var segmentCount = 0;
            foreach (var tracks in package.TrackDefinitions ?? Array.Empty<FuseTrackDefinition>())
            {
                segmentCount += tracks?.Segments?.Count ?? 0;
                if (tracks?.Nodes == null)
                {
                    continue;
                }

                foreach (var pair in tracks.Nodes)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null || !IsUsablePosition(pair.Value.Position))
                    {
                        continue;
                    }

                    nodes[pair.Key] = new PositionedNode(pair.Key, pair.Value.Position);
                }
            }

            if (nodes.Count == 0)
            {
                return null;
            }

            var values = nodes.Values.ToArray();
            return new TrackFootprint
            {
                PackageId = package.PackageId.Trim(),
                FolderPath = package.FolderPath ?? string.Empty,
                RequiredPackageIds = package.RequiredPackageIds ?? Array.Empty<string>(),
                LoadAfter = package.LoadAfter ?? Array.Empty<string>(),
                LoadBefore = package.LoadBefore ?? Array.Empty<string>(),
                Nodes = values,
                SegmentCount = segmentCount,
                MinX = values.Min(node => node.Position.x),
                MaxX = values.Max(node => node.Position.x),
                MinZ = values.Min(node => node.Position.z),
                MaxZ = values.Max(node => node.Position.z)
            };
        }

        private static List<NearbyNodePair> FindNearbyNodePairs(
            IReadOnlyList<PositionedNode> left,
            IReadOnlyList<PositionedNode> right)
        {
            var maxDistanceSquared = NearbyHorizontalDistance * NearbyHorizontalDistance;
            var candidates = new List<NearbyNodePair>();
            foreach (var leftNode in left)
            {
                foreach (var rightNode in right)
                {
                    var vertical = Math.Abs(leftNode.Position.y - rightNode.Position.y);
                    if (vertical > NearbyVerticalDistance)
                    {
                        continue;
                    }

                    var x = leftNode.Position.x - rightNode.Position.x;
                    var z = leftNode.Position.z - rightNode.Position.z;
                    var distanceSquared = (x * x) + (z * z);
                    if (distanceSquared <= maxDistanceSquared)
                    {
                        candidates.Add(new NearbyNodePair(leftNode, rightNode, distanceSquared));
                    }
                }
            }

            // Greedy nearest matching prevents one dense package node from
            // satisfying the two-node threshold more than once.
            var usedLeft = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedRight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var selected = new List<NearbyNodePair>();
            foreach (var pair in candidates.OrderBy(candidate => candidate.DistanceSquared))
            {
                if (usedLeft.Contains(pair.Left.Id) || usedRight.Contains(pair.Right.Id))
                {
                    continue;
                }

                usedLeft.Add(pair.Left.Id);
                usedRight.Add(pair.Right.Id);
                selected.Add(pair);
            }

            return selected;
        }

        private static bool BoundsMayOverlap(TrackFootprint left, TrackFootprint right, float padding)
        {
            return left.MinX - padding <= right.MaxX && left.MaxX + padding >= right.MinX &&
                   left.MinZ - padding <= right.MaxZ && left.MaxZ + padding >= right.MinZ;
        }

        private static bool IsExpectedLayering(TrackFootprint existing, TrackFootprint later)
        {
            return FuseDeclaredPackageRelationship.ContainsPackageId(later.RequiredPackageIds, existing.PackageId) ||
                   FuseDeclaredPackageRelationship.ContainsPackageId(later.LoadAfter, existing.PackageId) ||
                   FuseDeclaredPackageRelationship.ContainsPackageId(existing.LoadBefore, later.PackageId);
        }

        private static bool SamePackageFolder(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            try
            {
                left = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                right = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex)
            {
                // Compare the original values when a synthetic/test path is
                // not valid for the current platform.
                FUSE.Infrastructure.FuseLog.Warning(
                    "FUSE spatial track overlap check could not normalize package folders " +
                    $"left='{left}' right='{right}' message='{ex.GetBaseException().Message}'.");
            }

            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUsablePosition(Vector3 position)
        {
            // A zero vector is the deserializer default for legacy partial
            // node declarations, not reliable spatial evidence.
            return position != Vector3.zero &&
                   IsFinite(position.x) && IsFinite(position.y) && IsFinite(position.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private sealed class TrackFootprint
        {
            public string PackageId { get; set; }
            public string FolderPath { get; set; }
            public string[] RequiredPackageIds { get; set; }
            public string[] LoadAfter { get; set; }
            public string[] LoadBefore { get; set; }
            public PositionedNode[] Nodes { get; set; }
            public int SegmentCount { get; set; }
            public float MinX { get; set; }
            public float MaxX { get; set; }
            public float MinZ { get; set; }
            public float MaxZ { get; set; }
        }

        private sealed class PositionedNode
        {
            public PositionedNode(string id, Vector3 position)
            {
                Id = id;
                Position = position;
            }

            public string Id { get; }
            public Vector3 Position { get; }
        }

        private sealed class NearbyNodePair
        {
            public NearbyNodePair(PositionedNode left, PositionedNode right, float distanceSquared)
            {
                Left = left;
                Right = right;
                DistanceSquared = distanceSquared;
            }

            public PositionedNode Left { get; }
            public PositionedNode Right { get; }
            public float DistanceSquared { get; }
        }
    }
}
