using System;
using FUSE.Data;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    public class FuseModRequirementResolverTests
    {
        public class TryParseVersionTests
        {
            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("   ")]
            [InlineData("abc")]
            [InlineData("v.x.y")]
            public void Returns_False_For_NonNumericInputs(string value)
            {
                Assert.False(FuseModRequirementResolver.TryParseVersion(value, out _));
            }

            [Theory]
            [InlineData("1", 1, 0, 0, 0)]
            [InlineData("1.2", 1, 2, 0, 0)]
            [InlineData("1.2.3", 1, 2, 3, 0)]
            [InlineData("1.2.3.4", 1, 2, 3, 4)]
            public void Parses_Standard_VersionShapes(string value, int major, int minor, int build, int revision)
            {
                Assert.True(FuseModRequirementResolver.TryParseVersion(value, out var version));
                Assert.Equal(new Version(major, minor, build, revision), version);
            }

            [Fact]
            public void Trims_Surrounding_Whitespace()
            {
                Assert.True(FuseModRequirementResolver.TryParseVersion("  1.2.3  ", out var version));
                Assert.Equal(new Version(1, 2, 3, 0), version);
            }

            [Fact]
            public void ExtractsLeading_VersionPattern_FromMixedString()
            {
                // The regex is unanchored, so it picks up the first numeric run.
                // Documenting the actual contract — "v1.2" and "1.2-beta" both parse as 1.2.0.0.
                Assert.True(FuseModRequirementResolver.TryParseVersion("v1.2", out var fromPrefix));
                Assert.Equal(new Version(1, 2, 0, 0), fromPrefix);

                Assert.True(FuseModRequirementResolver.TryParseVersion("1.2-beta", out var fromSuffix));
                Assert.Equal(new Version(1, 2, 0, 0), fromSuffix);
            }

            [Fact]
            public void TruncatesTo_FourComponents()
            {
                // {0,3} in the regex caps at four total digit groups.
                Assert.True(FuseModRequirementResolver.TryParseVersion("1.2.3.4.5", out var version));
                Assert.Equal(new Version(1, 2, 3, 4), version);
            }
        }

        public class VersionSatisfiesTests
        {
            private static FuseModRequirementResolver.InstalledMod Installed(string version) =>
                new FuseModRequirementResolver.InstalledMod { Id = "installed", Version = version };

            [Fact]
            public void NullRequirement_IsAlwaysSatisfied()
            {
                Assert.True(FuseModRequirementResolver.VersionSatisfies(
                    "pkg", null, Installed("1.0"), out var reason));
                Assert.Equal(string.Empty, reason);
            }

            [Fact]
            public void NullInstalled_IsAlwaysSatisfied()
            {
                var requirement = new FuseModRequirement { Id = "dep", NotBefore = "1.0" };

                Assert.True(FuseModRequirementResolver.VersionSatisfies(
                    "pkg", requirement, null, out _));
            }

            [Fact]
            public void NoConstraints_IsSatisfied()
            {
                var requirement = new FuseModRequirement { Id = "dep" };

                Assert.True(FuseModRequirementResolver.VersionSatisfies(
                    "pkg", requirement, Installed("0.0.1"), out _));
            }

            [Fact]
            public void Installed_BelowNotBefore_IsRejected()
            {
                var requirement = new FuseModRequirement { Id = "dep", NotBefore = "2.0" };

                var ok = FuseModRequirementResolver.VersionSatisfies(
                    "pkg", requirement, Installed("1.9.9"), out var reason);

                Assert.False(ok);
                Assert.Contains("older", reason);
            }

            [Fact]
            public void Installed_EqualToNotBefore_IsAccepted()
            {
                var requirement = new FuseModRequirement { Id = "dep", NotBefore = "2.0" };

                Assert.True(FuseModRequirementResolver.VersionSatisfies(
                    "pkg", requirement, Installed("2.0"), out _));
            }

            [Fact]
            public void Installed_AboveNotBefore_IsAccepted()
            {
                var requirement = new FuseModRequirement { Id = "dep", NotBefore = "2.0" };

                Assert.True(FuseModRequirementResolver.VersionSatisfies(
                    "pkg", requirement, Installed("2.0.1"), out _));
            }

            [Fact]
            public void Installed_AboveNotAfter_IsRejected()
            {
                var requirement = new FuseModRequirement { Id = "dep", NotAfter = "2.0" };

                var ok = FuseModRequirementResolver.VersionSatisfies(
                    "pkg", requirement, Installed("2.0.1"), out var reason);

                Assert.False(ok);
                Assert.Contains("newer", reason);
            }

            [Fact]
            public void Installed_EqualToNotAfter_IsAccepted()
            {
                var requirement = new FuseModRequirement { Id = "dep", NotAfter = "2.0" };

                Assert.True(FuseModRequirementResolver.VersionSatisfies(
                    "pkg", requirement, Installed("2.0"), out _));
            }

            [Fact]
            public void Installed_WithinRange_IsAccepted()
            {
                var requirement = new FuseModRequirement { Id = "dep", NotBefore = "1.0", NotAfter = "2.0" };

                Assert.True(FuseModRequirementResolver.VersionSatisfies(
                    "pkg", requirement, Installed("1.5"), out _));
            }

            [Fact]
            public void Unparseable_InstalledVersion_IsTreatedAsCompatible()
            {
                // The resolver chooses leniency: when it cannot parse the installed
                // version it logs a warning and returns true rather than failing the
                // whole load. This locks in that behavior so a stricter rewrite
                // is a deliberate decision, not a silent regression.
                var requirement = new FuseModRequirement { Id = "dep", NotBefore = "1.0" };

                Assert.True(FuseModRequirementResolver.VersionSatisfies(
                    "pkg", requirement, Installed("not-a-version"), out _));
            }

            [Fact]
            public void Unparseable_NotBefore_IsIgnored_AndOtherBoundStillEnforced()
            {
                // If NotBefore is junk but NotAfter is valid, the junk bound is
                // skipped and the valid bound is still checked.
                var requirement = new FuseModRequirement
                {
                    Id = "dep",
                    NotBefore = "garbage",
                    NotAfter = "2.0"
                };

                var ok = FuseModRequirementResolver.VersionSatisfies(
                    "pkg", requirement, Installed("3.0"), out var reason);

                Assert.False(ok);
                Assert.Contains("newer", reason);
            }
        }
    }
}
