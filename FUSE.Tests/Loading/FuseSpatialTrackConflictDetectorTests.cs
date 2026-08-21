using FUSE.Authoring.Data;
using FUSE.Loading;
using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace FUSE.Tests.Loading
{
    public sealed class FuseSpatialTrackConflictDetectorTests
    {
        [Fact]
        public void Detect_reports_two_independent_track_layouts_with_multiple_nearby_nodes()
        {
            var conflicts = FuseSpatialTrackConflictDetector.Detect(new[]
            {
                Package("yard-a", "C:\\Mods\\YardA", ("a1", 100f, 200f), ("a2", 130f, 230f)),
                Package("yard-b", "C:\\Mods\\YardB", ("b1", 105f, 203f), ("b2", 138f, 235f))
            });

            var conflict = Assert.Single(conflicts);
            Assert.StartsWith("spatial-overlap:", conflict.Id);
            Assert.Contains("both packages retained", conflict.Resolution);
            Assert.Equal("yard-a", conflict.OwnerPackageId);
            Assert.Equal("yard-b", conflict.AttemptedPackageId);
        }

        [Fact]
        public void Detect_ignores_a_single_shared_connection_point()
        {
            var conflicts = FuseSpatialTrackConflictDetector.Detect(new[]
            {
                Package("extension-a", "C:\\Mods\\A", ("a1", 100f, 200f), ("a2", 130f, 230f)),
                Package("extension-b", "C:\\Mods\\B", ("b1", 105f, 203f), ("b2", 500f, 600f))
            });

            Assert.Empty(conflicts);
        }

        [Fact]
        public void Detect_ignores_definition_fragments_from_the_same_package_folder()
        {
            var conflicts = FuseSpatialTrackConflictDetector.Detect(new[]
            {
                Package("package.track", "C:\\Mods\\OnePackage", ("a1", 100f, 200f), ("a2", 130f, 230f)),
                Package("package.spans", "C:\\Mods\\OnePackage", ("b1", 105f, 203f), ("b2", 138f, 235f))
            });

            Assert.Empty(conflicts);
        }

        [Fact]
        public void Detect_ignores_declared_base_and_extension_layering()
        {
            var basePackage = Package(
                "Katers.SylvaInterchange.FUSE",
                "C:\\Mods\\SylvaBase",
                ("a1", 100f, 200f),
                ("a2", 130f, 230f));
            var extension = Package(
                "Katers.SylvaInterchangeHYSpans.FUSE",
                "C:\\Mods\\SylvaHYSpans",
                ("b1", 105f, 203f),
                ("b2", 138f, 235f));
            extension.RequiredPackageIds = new[] { "Katers.SylvaInterchange" };

            var conflicts = FuseSpatialTrackConflictDetector.Detect(new[] { basePackage, extension });

            Assert.Empty(conflicts);
        }

        private static FuseSpatialTrackPackage Package(
            string id,
            string folder,
            params (string id, float x, float z)[] nodes)
        {
            var tracks = new FuseTrackDefinition();
            foreach (var node in nodes)
            {
                tracks.Nodes[node.id] = new FuseNode { Position = new Vector3(node.x, 500f, node.z) };
            }

            tracks.Segments["segment-1"] = new FuseSegment();
            tracks.Segments["segment-2"] = new FuseSegment();
            return new FuseSpatialTrackPackage
            {
                PackageId = id,
                FolderPath = folder,
                TrackDefinitions = new[] { tracks }
            };
        }
    }
}
