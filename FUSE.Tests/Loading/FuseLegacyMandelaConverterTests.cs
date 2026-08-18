using FUSE.Loading;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Loading
{
    /// <summary>
    /// Pin the legacy "mandela" → FUSE conversion contract. Mandelas are
    /// the StrangeCustoms syntax for marking, repositioning, instantiating,
    /// or suppressing a base-game scenery GameObject by scene path; FUSE
    /// translates each JSON entry into either a
    /// <c>world.sceneClones</c> entry, a <c>world.suppressBaseScenePaths</c>
    /// entry, or a <c>world.removals.sceneClones</c> entry depending on
    /// shape. Getting the dispatch wrong has already cost us two visible
    /// regressions:
    ///
    /// <list type="bullet">
    ///   <item><description>An <c>{ "enabled": true }</c> mandela on a
    ///   vanilla scenery path being routed through the entity
    ///   round-trip and emerging with <c>localPosition: (0,0,0)</c>,
    ///   zeroing the live transform (the Bryson Freight House case).
    ///   That bug lives in <c>FuseConfigurableStructureEntity</c> and
    ///   has its own test suite; here we lock in that the CONVERTER
    ///   produces the right input for that downstream code — i.e.
    ///   <c>LocalPosition == null</c> when the source JSON did not
    ///   author a position.</description></item>
    ///   <item><description>An <c>{ "enabled": false }</c> mandela being
    ///   silently dropped instead of producing a suppression — leaking
    ///   vanilla scenery the author meant to hide.</description></item>
    /// </list>
    ///
    /// Both regressions slipped because we had no unit coverage on the
    /// dispatch. These tests close that gap.
    /// </summary>
    public class FuseLegacyMandelaConverterTests
    {
        private const string Target = "World/Large Scenery/Bryson/Freight House";

        private static (JObject root, JObject sceneClones, JArray removals, JArray suppressions)
            ConvertMandelas(JObject mandelas)
        {
            var root = ConvertLegacySource(new JObject
            {
                ["mandelas"] = mandelas
            });

            var world = (JObject)root["world"];
            var sceneClones = (JObject)world["sceneClones"];
            var removals = (JArray)world["removals"]["sceneClones"];
            var suppressions = (JArray)world["suppressBaseScenePaths"];
            return (root, sceneClones, removals, suppressions);
        }

        private static JObject ConvertLegacySource(JObject source)
        {
            var manifest = new FuseLegacyPackageManifest
            {
                PackageId = "test-pkg",
                DisplayName = "Test Package",
                Author = "tester",
                Version = "1.0.0"
            };
            var root = FuseLegacyDataConverter.CreateSkeleton(manifest, "mandela-fragment");
            FuseLegacyDataConverter.ConvertSource(source, root, manifest);
            return root;
        }

        public class EnabledTrue
        {
            [Fact]
            public void NoSource_NoPosition_BecomesPlainSceneClone_WithoutAnyTransformFields()
            {
                // This is the exact shape that Stryker's nullBryson.json
                // uses for the Freight House. The converter must NOT
                // invent a position; doing so cascades into the entity
                // round-trip bug that zeroed the vanilla building.
                var mandelas = new JObject
                {
                    [Target] = new JObject { ["enabled"] = true }
                };

                var (_, sceneClones, removals, suppressions) = FuseLegacyMandelaConverterTests.ConvertMandelas(mandelas);

                Assert.True(sceneClones.ContainsKey(Target));
                var entry = (JObject)sceneClones[Target];
                Assert.Equal(Target, (string)entry["targetPath"]);
                Assert.True((bool)entry["enabled"]);
                // The apply path treats Source as "instantiate from this
                // prefab if non-empty" — SceneCloneAPI.ApplyDefinition
                // bases its clonedFromSource flag on
                // !string.IsNullOrWhiteSpace(definition.Source). So the
                // converter is free to leave the property absent OR to
                // carry an empty/null/whitespace value through; what
                // matters is that downstream code sees "no source". A
                // non-empty string here would cause FUSE to destroy the
                // vanilla GameObject and instantiate a clone in its
                // place, which is the opposite of an enabled-only mark.
                var src = entry["source"];
                var asString = src?.Type == JTokenType.String ? (string)src : null;
                Assert.True(
                    string.IsNullOrWhiteSpace(asString),
                    $"no instantiateFrom -> source must read as empty for the apply path, not '{asString}'");
                Assert.False(entry.ContainsKey("localPosition"),
                    "no localPosition in JSON -> no localPosition in converted output");
                Assert.False(entry.ContainsKey("localRotation"));
                Assert.False(entry.ContainsKey("localScale"));
                Assert.Empty(removals);
                Assert.DoesNotContain(suppressions, value => (string)value == Target);
            }

            [Fact]
            public void WithLocalPosition_PreservesAuthoredCoordinates()
            {
                var mandelas = new JObject
                {
                    [Target] = new JObject
                    {
                        ["enabled"] = true,
                        ["localPosition"] = new JObject
                        {
                            ["x"] = 1.5f,
                            ["y"] = 2.5f,
                            ["z"] = -3.5f
                        }
                    }
                };

                var (_, sceneClones, _, _) = FuseLegacyMandelaConverterTests.ConvertMandelas(mandelas);

                var entry = (JObject)sceneClones[Target];
                Assert.True(entry.ContainsKey("localPosition"));
                var pos = (JObject)entry["localPosition"];
                Assert.Equal(1.5f, (float)pos["x"]);
                Assert.Equal(2.5f, (float)pos["y"]);
                Assert.Equal(-3.5f, (float)pos["z"]);
            }

            [Fact]
            public void PositionAlias_TreatedSameAsLocalPosition()
            {
                // The legacy SC syntax accepted both "position" and
                // "localPosition" — both alias to the same write.
                var mandelas = new JObject
                {
                    [Target] = new JObject
                    {
                        ["enabled"] = true,
                        ["position"] = new JObject
                        {
                            ["x"] = 10f,
                            ["y"] = 20f,
                            ["z"] = 30f
                        }
                    }
                };

                var (_, sceneClones, _, _) = FuseLegacyMandelaConverterTests.ConvertMandelas(mandelas);

                var entry = (JObject)sceneClones[Target];
                Assert.True(entry.ContainsKey("localPosition"));
                var pos = (JObject)entry["localPosition"];
                Assert.Equal(10f, (float)pos["x"]);
                Assert.Equal(20f, (float)pos["y"]);
                Assert.Equal(30f, (float)pos["z"]);
            }

            [Fact]
            public void WithInstantiateFrom_PrefixesPathScheme()
            {
                // A source without a "://" scheme prefix is taken as a
                // scene path; the converter is responsible for promoting
                // it to "path://scene/<...>" so the resolver can pick
                // the right handler downstream.
                var mandelas = new JObject
                {
                    [Target] = new JObject
                    {
                        ["enabled"] = true,
                        ["instantiateFrom"] = "World/Large Scenery/Dillsboro/Freight House"
                    }
                };

                var (_, sceneClones, _, _) = FuseLegacyMandelaConverterTests.ConvertMandelas(mandelas);

                var entry = (JObject)sceneClones[Target];
                Assert.Equal(
                    "path://scene/World/Large Scenery/Dillsboro/Freight House",
                    (string)entry["source"]);
            }

            [Fact]
            public void SchemePrefixedSource_PassesThroughUnchanged()
            {
                var mandelas = new JObject
                {
                    [Target] = new JObject
                    {
                        ["enabled"] = true,
                        ["instantiateFrom"] = "vanilla://brysonDepot"
                    }
                };

                var (_, sceneClones, _, _) = FuseLegacyMandelaConverterTests.ConvertMandelas(mandelas);

                var entry = (JObject)sceneClones[Target];
                Assert.Equal("vanilla://brysonDepot", (string)entry["source"]);
            }
        }

        public class EnabledFalse
        {
            [Fact]
            public void NoSource_NoPosition_BecomesSuppression()
            {
                // The classic "hide this vanilla GameObject" intent —
                // must land in suppressBaseScenePaths, NOT in sceneClones.
                // If it ends up in sceneClones, FUSE will scene-clone an
                // inactive duplicate but leave the original visible.
                var mandelas = new JObject
                {
                    [Target] = new JObject { ["enabled"] = false }
                };

                var (_, sceneClones, removals, suppressions) = FuseLegacyMandelaConverterTests.ConvertMandelas(mandelas);

                Assert.False(sceneClones.ContainsKey(Target));
                Assert.Empty(removals);
                Assert.Contains(suppressions, value => (string)value == Target);
            }

            [Fact]
            public void WithInstantiateFrom_StaysASceneCloneNotASuppression()
            {
                // An entry that authors a source is asking FUSE to
                // INSTANTIATE a fresh copy (initially disabled) — that
                // is a scene clone, not a suppression of the base path.
                var mandelas = new JObject
                {
                    [Target] = new JObject
                    {
                        ["enabled"] = false,
                        ["instantiateFrom"] = "World/Large Scenery/Dillsboro/Freight House"
                    }
                };

                var (_, sceneClones, _, suppressions) = FuseLegacyMandelaConverterTests.ConvertMandelas(mandelas);

                Assert.True(sceneClones.ContainsKey(Target));
                var entry = (JObject)sceneClones[Target];
                Assert.False((bool)entry["enabled"]);
                Assert.NotNull(entry["source"]);
                Assert.DoesNotContain(suppressions, value => (string)value == Target);
            }
        }

        public class NullValue
        {
            [Fact]
            public void NullEntry_BecomesRemoval_NotSceneCloneOrSuppression()
            {
                // SC's "delete this base GameObject entirely" syntax.
                var mandelas = new JObject
                {
                    [Target] = JValue.CreateNull()
                };

                var (_, sceneClones, removals, suppressions) = FuseLegacyMandelaConverterTests.ConvertMandelas(mandelas);

                Assert.False(sceneClones.ContainsKey(Target));
                Assert.Contains(removals, value => (string)value == Target);
                Assert.DoesNotContain(suppressions, value => (string)value == Target);
            }
        }

        public class MixedBatch
        {
            [Fact]
            public void EachEntry_RoutedToCorrectBucket()
            {
                // Mirror the actual nullBryson.json shape: many disables
                // (suppressions), one enabled holdout (scene clone).
                const string keep = "World/Large Scenery/Bryson/Freight House";
                const string hide1 = "World/Large Scenery/Bryson/Bryson Water Tower";
                const string hide2 = "World/Large Scenery/Bryson/Bryson Coaling Tower";
                const string drop = "World/Large Scenery/Bryson/MOW Area";

                var mandelas = new JObject
                {
                    [keep] = new JObject { ["enabled"] = true },
                    [hide1] = new JObject { ["enabled"] = false },
                    [hide2] = new JObject { ["enabled"] = false },
                    [drop] = JValue.CreateNull()
                };

                var (_, sceneClones, removals, suppressions) = FuseLegacyMandelaConverterTests.ConvertMandelas(mandelas);

                Assert.True(sceneClones.ContainsKey(keep));
                Assert.False(sceneClones.ContainsKey(hide1));
                Assert.False(sceneClones.ContainsKey(hide2));
                Assert.False(sceneClones.ContainsKey(drop));

                Assert.Contains(suppressions, value => (string)value == hide1);
                Assert.Contains(suppressions, value => (string)value == hide2);
                Assert.DoesNotContain(suppressions, value => (string)value == keep);
                Assert.DoesNotContain(suppressions, value => (string)value == drop);

                Assert.Contains(removals, value => (string)value == drop);
                Assert.DoesNotContain(removals, value => (string)value == keep);
                Assert.DoesNotContain(removals, value => (string)value == hide1);

                // And the kept entry must still be position-agnostic.
                var keepEntry = (JObject)sceneClones[keep];
                Assert.False(keepEntry.ContainsKey("localPosition"));
                Assert.False(keepEntry.ContainsKey("localRotation"));
                Assert.False(keepEntry.ContainsKey("localScale"));
            }
        }

        public class GeneratedLoader
        {
            [Fact]
            public void MatchingLoaderTransform_IsFoldedIntoLoader_NotRegisteredAsSceneClone()
            {
                const string loaderId = "FranklinCoalTower";
                const string loaderPath = "World/Loaders/FranklinCoalTower";
                var root = FuseLegacyMandelaConverterTests.ConvertLegacySource(new JObject
                {
                    ["splineys"] = new JObject
                    {
                        [loaderId] = new JObject
                        {
                            ["Position"] = new JObject { ["x"] = 11190f, ["y"] = 615f, ["z"] = -22610f },
                            ["Rotation"] = new JObject { ["x"] = 0f, ["y"] = 159f, ["z"] = 0f },
                            ["Prefab"] = "vanilla://coalTower",
                            ["Industry"] = "franklinservice",
                            ["Handler"] = "AlinasMapMod.LoaderBuilder"
                        }
                    },
                    ["mandelas"] = new JObject
                    {
                        [loaderPath] = new JObject
                        {
                            ["localPosition"] = new JObject { ["x"] = 11190.2021f, ["y"] = 616.399658f, ["z"] = -22610.15f },
                            ["localRotation"] = new JObject { ["x"] = 0f, ["y"] = 271f, ["z"] = 0f },
                            ["enabled"] = true
                        }
                    }
                });

                var loader = (JObject)root["operations"]["loaders"][loaderId];
                var position = (JObject)loader["position"];
                var rotation = (JObject)loader["rotation"];
                var sceneClones = (JObject)root["world"]["sceneClones"];

                Assert.Equal(11190.2021f, (float)position["x"], precision: 2);
                Assert.Equal(616.399658f, (float)position["y"], precision: 3);
                Assert.Equal(-22610.15f, (float)position["z"], precision: 2);
                Assert.Equal(271f, (float)rotation["y"]);
                Assert.False(sceneClones.ContainsKey(loaderPath));
            }

            [Fact]
            public void ScaledLoaderPath_RemainsSceneCloneBecauseLoaderDefinitionsCannotRepresentScale()
            {
                const string loaderId = "ScaledLoader";
                const string loaderPath = "World/Loaders/ScaledLoader";
                var root = FuseLegacyMandelaConverterTests.ConvertLegacySource(new JObject
                {
                    ["splineys"] = new JObject
                    {
                        [loaderId] = new JObject
                        {
                            ["Position"] = new JObject { ["x"] = 1f, ["y"] = 2f, ["z"] = 3f },
                            ["Prefab"] = "vanilla://coalTower",
                            ["Handler"] = "AlinasMapMod.LoaderBuilder"
                        }
                    },
                    ["mandelas"] = new JObject
                    {
                        [loaderPath] = new JObject
                        {
                            ["localScale"] = new JObject { ["x"] = 2f, ["y"] = 2f, ["z"] = 2f },
                            ["enabled"] = true
                        }
                    }
                });

                Assert.True(((JObject)root["world"]["sceneClones"]).ContainsKey(loaderPath));
            }
        }
    }
}
