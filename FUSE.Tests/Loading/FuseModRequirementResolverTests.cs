using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FUSE.Authoring.Data;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    public class FuseModRequirementResolverTests
    {
        [Fact]
        public void MissingMixintoDependency_IdentifiesPackageFolderFileAndAction()
        {
            var dependencyId = "missing." + Guid.NewGuid().ToString("N");
            var folder = Path.Combine(Path.GetTempPath(), "fuse-requirement-source");
            var definitionPath = Path.Combine(folder, "track.fuse.json");
            var loaded = new FuseLoadedMod(
                folder,
                definitionPath,
                new FuseModDefinition
                {
                    Id = "author.sample",
                    Mixinto = new FuseMixintoDefinition
                    {
                        Target = "railroader",
                        Requires = new[] { new FuseModRequirement { Id = dependencyId } }
                    }
                });

            var shouldApply = FuseModRequirementResolver.ShouldApply(loaded, out var reason);

            Assert.False(shouldApply);
            Assert.Contains("package='author.sample'", reason);
            Assert.Contains($"id='{dependencyId}'", reason);
            Assert.Contains($"folder='{folder}'", reason);
            Assert.Contains($"sourceFile='{definitionPath}'", reason);
            Assert.Contains("action='Install and enable", reason);
        }

        [Theory]
        [InlineData("Zamu.StrangeCustoms", "99.0.0")]
        [InlineData("AlinaNova21.AlinasMapMod", "99.0.0")]
        [InlineData("AssetLoader", "99.0.0")]
        public void Replacement_capability_satisfies_mixinto_requirement_without_legacy_version_comparison(
            string dependencyId,
            string legacyMinimumVersion)
        {
            var loaded = new FuseLoadedMod(
                "C:\\Mods\\Extension",
                "C:\\Mods\\Extension\\track.fuse.json",
                new FuseModDefinition
                {
                    Id = "author.extension",
                    Mixinto = new FuseMixintoDefinition
                    {
                        Target = "game-graph",
                        Requires = new[]
                        {
                            new FuseModRequirement { Id = dependencyId, NotBefore = legacyMinimumVersion }
                        }
                    }
                });

            Assert.True(FuseModRequirementResolver.ShouldApply(loaded, out var reason));
            Assert.Contains("requirements satisfied", reason);
        }

        [Fact]
        public void Mixinto_conflictsWith_replacement_capability_skips_only_the_fragment()
        {
            var loaded = new FuseLoadedMod(
                "C:\\Mods\\Conditional",
                "C:\\Mods\\Conditional\\track.fuse.json",
                new FuseModDefinition
                {
                    Id = "author.conditional",
                    Mixinto = new FuseMixintoDefinition
                    {
                        Target = "game-graph",
                        ConflictsWith = new[]
                        {
                            new FuseModRequirement { Id = "Zamu.StrangeCustoms", NotBefore = "99.0.0" }
                        }
                    }
                });

            Assert.False(FuseModRequirementResolver.ShouldApply(loaded, out var reason));
            Assert.Contains("mixinto conflict matched", reason);
            Assert.Contains("intentionally inactive", reason);
        }

        [Theory]
        [InlineData("Route.Base", "Route.Base.FUSE", "1.5", "1.0", "2.0", false, true)]
        [InlineData("Route.Base", "Route.Base.FUSE", "3.0", "1.0", "2.0", false, false)]
        [InlineData("Route.Base", "Route.Other.FUSE", "1.5", null, null, false, false)]
        [InlineData("Route.Base", "Route.Base.FUSE", "1.5", null, null, true, false)]
        public void Declared_package_conflict_matches_alias_version_and_enabled_state(
            string conflictId,
            string targetId,
            string targetVersion,
            string notBefore,
            string notAfter,
            bool disabled,
            bool expected)
        {
            var reference = new FuseModRequirement
            {
                Id = conflictId,
                NotBefore = notBefore,
                NotAfter = notAfter
            };

            Assert.Equal(expected, FuseDataPackageDiscovery.IsDeclaredConflictMatch(
                reference,
                targetId,
                targetVersion,
                disabled));
        }

        [Theory]
        [InlineData("1.5", "1.0", "2.0", false, true)]
        [InlineData("3.0", "1.0", "2.0", false, false)]
        [InlineData("0.0", "99.0", null, true, false)]
        public void Top_level_declared_conflict_matches_code_or_asset_package_inventory(
            string installedVersion,
            string notBefore,
            string notAfter,
            bool replacementCapability,
            bool expected)
        {
            var installed = new FuseModRequirementResolver.InstalledMod
            {
                Id = "External.CodeMod",
                Version = installedVersion,
                Source = replacementCapability ? "FUSE replacement capability" : "Info.json",
                IsReplacementCapability = replacementCapability,
                FolderPath = Path.Combine("C:\\", "Railroader", "Mods", "ExternalCodeMod")
            };
            var inventory = new Dictionary<string, FuseModRequirementResolver.InstalledMod>(StringComparer.OrdinalIgnoreCase)
            {
                [installed.Id] = installed
            };

            var matched = FuseDataPackageDiscovery.TryMatchInstalledConflict(
                "Author.Package",
                Path.Combine("C:\\", "Railroader", "Mods", "AuthorPackage"),
                new FuseModRequirement
                {
                    Id = installed.Id,
                    NotBefore = notBefore,
                    NotAfter = notAfter
                },
                inventory,
                out var result);

            Assert.Equal(expected, matched);
            Assert.Equal(expected ? installed : null, result);
        }

        [Fact]
        public void Top_level_declared_conflict_does_not_match_declaring_folder_alias()
        {
            var installed = new FuseModRequirementResolver.InstalledMod
            {
                Id = "Author.Package.Alias",
                Version = "1.0",
                Source = "Info.json",
                FolderPath = Path.Combine("C:\\", "Railroader", "Mods", "AuthorPackage")
            };
            var inventory = new Dictionary<string, FuseModRequirementResolver.InstalledMod>(StringComparer.OrdinalIgnoreCase)
            {
                [installed.Id] = installed
            };

            Assert.False(FuseDataPackageDiscovery.TryMatchInstalledConflict(
                "Author.Package",
                Path.Combine("C:\\", "Railroader", "Mods", "AuthorPackage") + Path.DirectorySeparatorChar,
                new FuseModRequirement { Id = installed.Id },
                inventory,
                out _));
        }

        [Fact]
        public void LegacyContextMods_IncludesInstalledManifestAndFuseReplacementCapabilities()
        {
            var root = Path.Combine(Path.GetTempPath(), "fuse-legacy-mods-" + Guid.NewGuid().ToString("N"));
            var package = Path.Combine(root, "Some Hosted Package");
            var companion = Path.Combine(root, "Some Kind Of Madness");
            Directory.CreateDirectory(package);
            Directory.CreateDirectory(companion);
            try
            {
                File.WriteAllText(
                    Path.Combine(package, "Definition.json"),
                    "{ \"id\": \"Joo.Sample\", \"name\": \"Sample\", \"version\": \"2.1\" }");
                File.WriteAllText(
                    Path.Combine(companion, "Definition.json"),
                    "{ \"id\": \"Zamu.SomeKindOfMadness\", \"name\": \"Some Kind Of Madness\", \"version\": \"1.0\" }");

                var mods = FuseLegacyAssemblyHost.EnumerateInstalledMods(root, (_, __) => true);

                Assert.Contains(mods, mod => mod.Id == "Joo.Sample" && mod.IsEnabled && mod.IsLoaded);
                Assert.Contains(mods, mod => mod.Id == "Zamu.SomeKindOfMadness" && mod.IsEnabled && mod.IsLoaded);
                Assert.Contains(mods, mod => mod.Id == "Zamu.StrangeCustoms" && mod.IsEnabled && mod.IsLoaded);
                Assert.Contains(mods, mod => mod.Id == "FUSE" && mod.IsEnabled && mod.IsLoaded);
                Assert.Equal(mods.Count, mods.Select(mod => mod.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

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
