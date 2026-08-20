using System;
using FUSE.Authoring.Data;
using FUSE.Authoring.Migrations;
using Xunit;

namespace FUSE.Tests.Migrations
{
    public class FuseMigrationTests
    {
        public class TryParseSchemaVersionTests
        {
            [Theory]
            [InlineData("1.0", 1, 0)]
            [InlineData("2.3", 2, 3)]
            [InlineData("1", 1, 0)]
            [InlineData("  1.0  ", 1, 0)]
            [InlineData("1.0.0", 1, 0)] // trailing patch component is tolerated but ignored
            public void Valid_Versions_Parse(string input, int major, int minor)
            {
                Assert.True(FuseMigration.TryParseSchemaVersion(input, out var version));
                Assert.Equal(new Version(major, minor), version);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("   ")]
            [InlineData("abc")]
            [InlineData("1.x")]
            [InlineData("-1.0")]
            [InlineData("1.0.0.0")] // 4+ parts rejected
            public void Invalid_Versions_AreRejected(string input)
            {
                Assert.False(FuseMigration.TryParseSchemaVersion(input, out _));
            }
        }

        public class IsFutureSchemaVersionTests
        {
            [Theory]
            [InlineData("1.0", false)]   // equal to current
            [InlineData("0.9", false)]   // older
            [InlineData("1.1", true)]    // newer minor
            [InlineData("2.0", true)]    // newer major
            [InlineData("invalid", false)]
            [InlineData(null, false)]
            public void Returns_ExpectedResult(string input, bool expected)
            {
                Assert.Equal(expected, FuseMigration.IsFutureSchemaVersion(input));
            }
        }

        public class MigrateTests
        {
            [Fact]
            public void Null_Throws_ArgumentNullException()
            {
                Assert.Throws<ArgumentNullException>(() => FuseMigration.Migrate(null));
            }

            [Fact]
            public void EmptyDefinition_SetsSchemaVersionToCurrent()
            {
                var definition = new FuseModDefinition { SchemaVersion = null };

                FuseMigration.Migrate(definition);

                Assert.Equal(FuseMigration.CurrentVersion, definition.SchemaVersion);
            }

            [Fact]
            public void CurrentSchemaVersion_StaysAtCurrent()
            {
                var definition = new FuseModDefinition { SchemaVersion = "1.0" };

                FuseMigration.Migrate(definition);

                Assert.Equal("1.0", definition.SchemaVersion);
            }

            [Fact]
            public void OlderUnknownSchemaVersion_BumpsToCurrent()
            {
                // 0.5 has no specific migration step — best-effort path bumps it to current.
                var definition = new FuseModDefinition { SchemaVersion = "0.5" };

                FuseMigration.Migrate(definition);

                Assert.Equal(FuseMigration.CurrentVersion, definition.SchemaVersion);
            }

            [Fact]
            public void FutureSchemaVersion_IsPreserved_BestEffortLoad()
            {
                // Future versions retain their declared schemaVersion — caller is on the
                // hook for compatibility. Locking this in so a stricter rewrite is a
                // deliberate decision.
                var definition = new FuseModDefinition { SchemaVersion = "1.2" };

                FuseMigration.Migrate(definition);

                Assert.Equal("1.2", definition.SchemaVersion);
            }

            [Fact]
            public void InvalidSchemaVersion_DefaultsToCurrent()
            {
                var definition = new FuseModDefinition { SchemaVersion = "garbage" };

                FuseMigration.Migrate(definition);

                Assert.Equal(FuseMigration.CurrentVersion, definition.SchemaVersion);
            }
        }

        public class NormalizeTests
        {
            [Fact]
            public void Null_IsNoOp()
            {
                FuseMigration.Normalize(null); // must not throw
            }

            [Fact]
            public void Fills_Author_ModVersion_CoordinateSpace_Defaults()
            {
                var definition = new FuseModDefinition
                {
                    Author = null,
                    ModVersion = null,
                    CoordinateSpace = null
                };

                FuseMigration.Normalize(definition);

                Assert.Equal(string.Empty, definition.Author);
                Assert.Equal("1.0.0", definition.ModVersion);
                Assert.Equal("world", definition.CoordinateSpace);
            }

            [Fact]
            public void Tags_AreTrimmed_DistinctIgnoreCase_AndWhitespaceFiltered()
            {
                var definition = new FuseModDefinition
                {
                    Tags = new[] { "  foo  ", "FOO", "bar", "", "   ", null }
                };

                FuseMigration.Normalize(definition);

                Assert.Equal(2, definition.Tags.Length);
                Assert.Contains("foo", definition.Tags); // case-folded dedupe keeps first occurrence
                Assert.Contains("bar", definition.Tags);
            }

            [Fact]
            public void Initializes_All_NullChildContainers()
            {
                var definition = new FuseModDefinition
                {
                    Tracks = null,
                    Operations = null,
                    World = null,
                    Audio = null,
                    Progression = null,
                    Settings = null,
                    Extensions = null
                };

                FuseMigration.Normalize(definition);

                Assert.NotNull(definition.Tracks);
                Assert.NotNull(definition.Operations);
                Assert.NotNull(definition.World);
                Assert.NotNull(definition.Audio);
                Assert.NotNull(definition.Progression);
                Assert.NotNull(definition.Settings);
                Assert.NotNull(definition.Extensions);
            }

            [Fact]
            public void Initializes_NullWaterSurfaceContainers()
            {
                var definition = new FuseModDefinition();
                definition.World.WaterSurfaces = null;
                definition.World.Removals.WaterSurfaces = null;

                FuseMigration.Normalize(definition);

                Assert.NotNull(definition.World.WaterSurfaces);
                Assert.NotNull(definition.World.Removals.WaterSurfaces);
            }

            [Fact]
            public void IsIdempotent_RunningTwiceProducesSameResult()
            {
                var definition = new FuseModDefinition
                {
                    Id = "pkg",
                    Tags = new[] { "a", "B", "a" }
                };

                FuseMigration.Normalize(definition);
                var firstTagCount = definition.Tags.Length;
                var firstAuthor = definition.Author;

                FuseMigration.Normalize(definition);

                Assert.Equal(firstTagCount, definition.Tags.Length);
                Assert.Equal(firstAuthor, definition.Author);
            }
        }
    }
}
