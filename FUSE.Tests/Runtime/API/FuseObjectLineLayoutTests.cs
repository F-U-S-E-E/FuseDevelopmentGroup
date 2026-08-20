using System;
using FUSE.Authoring.Data;
using FUSE.Runtime.API;
using UnityEngine;
using Xunit;

namespace FUSE.Tests.Runtime.API
{
    public sealed class FuseObjectLineLayoutTests
    {
        [Fact]
        public void UniformSpacingIncludesFinalEndpointWhenRequested()
        {
            var placements = FuseObjectLineLayout.Build(
                new[]
                {
                    Point(0f, 0f),
                    Point(10f, 0f),
                },
                4f,
                true,
                10);

            Assert.Collection(
                placements,
                placement => Assert.Equal(0f, placement.Position.x, 3),
                placement => Assert.Equal(4f, placement.Position.x, 3),
                placement => Assert.Equal(8f, placement.Position.x, 3),
                placement => Assert.Equal(10f, placement.Position.x, 3));
            Assert.All(placements, placement => Assert.Equal(Vector3.right, placement.Forward));
        }

        [Fact]
        public void SpacingContinuesAcrossPolylineCorners()
        {
            var placements = FuseObjectLineLayout.Build(
                new[]
                {
                    Point(0f, 0f),
                    Point(5f, 0f),
                    Point(5f, 5f),
                },
                4f,
                true,
                10);

            Assert.Equal(4, placements.Count);
            Assert.Equal(new Vector3(0f, 0f, 0f), placements[0].Position);
            Assert.Equal(new Vector3(4f, 0f, 0f), placements[1].Position);
            Assert.Equal(new Vector3(5f, 0f, 3f), placements[2].Position);
            Assert.Equal(new Vector3(5f, 0f, 5f), placements[3].Position);
            Assert.Equal(Vector3.forward, placements[2].Forward);
        }

        [Fact]
        public void SafetyLimitRejectsAccidentalInstanceExplosion()
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                FuseObjectLineLayout.Build(
                    new[]
                    {
                        Point(0f, 0f),
                        Point(100f, 0f),
                    },
                    1f,
                    true,
                    20));

            Assert.Contains("20 instance safety limit", exception.Message);
            Assert.Contains("Increase spacing", exception.Message);
        }

        [Fact]
        public void DuplicateOnlyPathIsRejected()
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                FuseObjectLineLayout.Build(
                    new[]
                    {
                        Point(2f, 3f),
                        Point(2f, 3f),
                    },
                    5f,
                    true,
                    20));

            Assert.Contains("non-zero path", exception.Message);
        }

        private static FuseSplineyPoint Point(float x, float z)
        {
            return new FuseSplineyPoint
            {
                Position = new Vector3(x, 0f, z),
            };
        }
    }
}
