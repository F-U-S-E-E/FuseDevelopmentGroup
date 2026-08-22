using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using FUSE.Loading;
using FUSE.Patches;
using HarmonyLib;
using Model.Definition;
using Model.Definition.Data;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace FUSE.Tests.Loading
{
    /// <summary>
    /// Real-world reproduction of issues #224 / #222 with the REAL LegosLibraryOfStuff (LLoS)
    /// assembly, its REAL Harmony patch classes, and REAL asset-pack / repaint-spec data
    /// from a local Railroader install.
    ///
    /// What is exercised:
    ///  - LegosLibraryOfStuff.dll is loaded into the test process; its actual
    ///    <c>ContainerSerializationDeserializePatch</c> (postfix on
    ///    <c>ContainerSerialization.Deserialize</c>) and <c>AddJsonSubTypesPatch</c> (postfix on
    ///    JsonSubtypes.GetAttributes) are applied through Harmony, exactly the classes LLoS's
    ///    <c>Load()</c> applies via PatchAll.
    ///  - LLoS's component-kind registration is primed the way LLoS.Load() and
    ///    LegosLogosAndDeco.Main.Load() do it (AddNewComponent for ComponentGroup /
    ///    AttributeModifierComponent / CustomImage / ...), using the real component types.
    ///  - LLoS's spec discovery (<c>LoadJsonDefinitions</c>) runs against
    ///    <c>UnityModManager.modEntries</c>, populated with real UMM ModEntry objects for the
    ///    real mod folders that ship <c>LegosLibraryOfStuff/Definitions/*.json</c>.
    ///  - The base packs' real <c>Definitions.json</c> text is pushed through FUSE's private
    ///    <c>LoadResilientDirectContainer</c> (PR #227's cold-load path) and through
    ///    <c>BypassDeserialize</c> (main's cold-load path — main's LoadResilientDirectContainer
    ///    body is exactly BypassDeserialize + the FilterUnbindableComponents retry).
    ///
    /// Not exercised (needs a Unity player): building prefabs from the definitions, PrefabStore
    /// lookups, the ComponentGroup toggle runtime. The tests stop at "does the clone
    /// ContainerItem exist in the Container FUSE hands to the game".
    ///
    /// Opt-in: the tests skip themselves unless FUSE_TEST_LLOS_GAME_DIR points at a local
    /// install (game + LLoS + LogosAndDeco + the packs), so CI and a plain `dotnet test`
    /// never load third-party mod assemblies.
    /// </summary>
    [Collection(FuseDirectStoreLegosLibraryReproCollection.Name)]
    public sealed class FuseDirectStoreLegosLibraryReproTests : IClassFixture<LegosLibraryHarness>
    {
        private readonly LegosLibraryHarness _h;
        private readonly ITestOutputHelper _out;

        public FuseDirectStoreLegosLibraryReproTests(LegosLibraryHarness harness, ITestOutputHelper output)
        {
            _h = harness;
            _out = output;
        }

        [Fact]
        public void CompatibilityLatch_ReentersInstallerAfterSuccessfulUnpatch()
        {
            var installedField = typeof(FuseLegosLibraryCompatibility).GetField(
                "_installed",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(installedField);
            var original = (bool)installedField.GetValue(null);

            try
            {
                installedField.SetValue(null, true);
                Assert.Equal(
                    "installed",
                    FuseLegosLibraryCompatibility.EnsureInstalled(null));

                FuseLegosLibraryCompatibility.ResetAfterSuccessfulUnpatch();

                Assert.Equal(
                    "unavailable (no harmony)",
                    FuseLegosLibraryCompatibility.EnsureInstalled(null));
            }
            finally
            {
                installedField.SetValue(null, original);
            }
        }

        // ------------------------------------------------------------------
        // Fixture: PS-1 40ft Boxcar Series / "PS-1-40ft-8ft p-s-door" (base pack, mod car
        // "PS-1-40ft-boxcar-8ft-p-s-door") + PS-1 Midwest Boxcar Repaints (repaint specs that
        // clone that mod car: CNW1, MP1, RI1, RI2, SOO1). Issue #224 population.
        // ------------------------------------------------------------------
        private const string Ps1BaseIdentifier = "PS-1-40ft-boxcar-8ft-p-s-door";

        private string Ps1BasePackDefinitions =>
            Path.Combine(_h.ModsRoot, "PS-1 40ft Boxcar Series", "PS-1-40ft-8ft p-s-door", "Definitions.json");

        private string[] ExpectedPs1CloneIdentifiers()
        {
            // Read the expectation from the real spec files rather than hard-coding it, so the
            // test tracks the data actually installed.
            var specDir = Path.Combine(_h.ModsRoot, "PS-1 Midwest Boxcar Repaints", "LegosLibraryOfStuff", "Definitions");
            return Directory.GetFiles(specDir, "*.json")
                .Select(TryParseSpec)
                .Where(j => j != null && (string)j["identifier"] == Ps1BaseIdentifier && (bool?)j["clone"] == true)
                .Select(j => (string)j["newIdentifier"])
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
        }

        // LLoS itself skips spec files it cannot parse ("Failed to load file"); e.g. the installed
        // PS-1 Midwest pack ships an empty RI-ps1-3.json. Mirror that here.
        private static JObject TryParseSpec(string path)
        {
            try { return JObject.Parse(File.ReadAllText(path)); }
            catch (Exception) { return null; }
        }

        [LegosLibraryRealDataFact]
        public void Harness_PrimedLikeTheRealPlugin()
        {
            _out.WriteLine(_h.Describe());
            Assert.True(_h.SpecCount > 1, "LLoS found no clone specs");
            Assert.Contains(Ps1BaseIdentifier, _h.SpecIdentifiers);
            Assert.Contains("ls-462-p37", _h.SpecIdentifiers);
            // The real spec files for the PS-1 Midwest repaints all parsed (they need the
            // LogosAndDeco 'CustomImage' kind, which the harness registered like the plugin does).
            var expected = ExpectedPs1CloneIdentifiers();
            Assert.Equal(5, expected.Length);
            foreach (var id in expected)
            {
                Assert.Contains(id, _h.SpecNewIdentifiers);
            }
        }

        // (1) main's cold-load code path (BypassDeserialize): the real LLoS postfix never
        //     runs, so no repaint clone exists in the container FUSE would hand the game.
        [LegosLibraryRealDataFact]
        public void MainCodePath_BypassDeserialize_RealPs1Pack_NoLlosClonesExist()
        {
            var text = File.ReadAllText(Ps1BasePackDefinitions);
            var before = LegosLibraryHarness.PostfixInvocations;

            var container = _h.BypassDeserialize(text);

            Assert.NotNull(container);
            Assert.Equal(before, LegosLibraryHarness.PostfixInvocations);
            var ids = container.Objects.Select(o => o.Identifier).ToArray();
            _out.WriteLine("main/bypass identifiers: " + string.Join(", ", ids));
            Assert.Equal(new[] { Ps1BaseIdentifier }, ids);
            foreach (var cloneId in ExpectedPs1CloneIdentifiers())
            {
                Assert.DoesNotContain(cloneId, ids);
            }
        }

        // (2) PR #227's cold-load code path (LoadResilientDirectContainer -> public
        //     ContainerSerialization.Deserialize): the real LLoS postfix runs once and the
        //     repaint clones (issue #224) are present, carrying the spec's edits.
        [LegosLibraryRealDataFact]
        public void Pr227CodePath_ColdLoad_RealPs1Pack_LlosRepaintClonesExist()
        {
            var text = File.ReadAllText(Ps1BasePackDefinitions);
            var before = LegosLibraryHarness.PostfixInvocations;
            var dropped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var container = _h.ColdLoad(text, "fuseasset://" + Uri.EscapeDataString(Path.GetDirectoryName(Ps1BasePackDefinitions)), dropped);

            Assert.NotNull(container);
            Assert.Equal(before + 1, LegosLibraryHarness.PostfixInvocations);
            Assert.Empty(dropped); // native path bound every kind; no tolerant fallback
            var ids = container.Objects.Select(o => o.Identifier).ToArray();
            _out.WriteLine("PR#227/cold-load identifiers: " + string.Join(", ", ids));

            Assert.Equal(Ps1BaseIdentifier, ids[0]);
            var expected = ExpectedPs1CloneIdentifiers();
            foreach (var cloneId in expected)
            {
                Assert.Contains(cloneId, ids);
            }

            // Spot-check one clone against its spec (CNW-ps1-1.json).
            var cnw = container.Objects.Single(o => o.Identifier == Ps1BaseIdentifier + "-CNW1");
            var spec = JObject.Parse(File.ReadAllText(Path.Combine(
                _h.ModsRoot, "PS-1 Midwest Boxcar Repaints", "LegosLibraryOfStuff", "Definitions", "CNW-ps1-1.json")));
            Assert.Equal((string)spec["name"], cnw.Metadata.Name);
            var car = Assert.IsAssignableFrom<CarDefinition>(cnw.Definition);
            Assert.Equal((string)spec["baseRoadNumber"], car.BaseRoadNumber);
            Assert.Equal((string)spec["CarType"], car.CarType);
            var kinds = car.Components.Select(c => c.Kind).ToArray();
            _out.WriteLine("CNW1 clone component kinds: " + string.Join(", ", kinds));
            // bulkAdds from the spec (2 Decal + 2 CustomImage) landed on the clone ...
            Assert.Equal(2, car.Components.Count(c => c.Kind == "CustomImage"));
            // ... and the 'removes' took the base's reporting-mark decals off the clone.
            Assert.DoesNotContain(car.Components, c => c.Kind == "Decal" && c.Name == "Side Reporting Marks 1");
            // The 'replace' add swapped the Colorable Sides colorizer for the spec's colours.
            var sides = car.Components.Single(c => c.Kind == "Colorizer" && c.Name == "Colorable Sides");
            var hex = (IEnumerable<string>)sides.GetType().GetProperty("HexColors").GetValue(sides);
            Assert.Equal(spec["adds"][0]["component"]["hexColors"].Select(t => (string)t).ToArray(), hex.ToArray());

            // Clone is a distinct object from the base (LLoS CloneItem JSON round-trip).
            var baseItem = container.Objects[0];
            Assert.NotSame(baseItem.Definition, cnw.Definition);
        }

        // (2b) Same evidence, but obtained by calling the game's public entry point directly —
        //      proves the clones come from LLoS's postfix on that entry point and not from
        //      anything FUSE-specific.
        [LegosLibraryRealDataFact]
        public void GamePublicDeserialize_RealPs1Pack_LlosRepaintClonesExist()
        {
            var text = File.ReadAllText(Ps1BasePackDefinitions);
            var before = LegosLibraryHarness.PostfixInvocations;

            var container = ContainerSerialization.Deserialize(text);

            Assert.Equal(before + 1, LegosLibraryHarness.PostfixInvocations);
            var ids = container.Objects.Select(o => o.Identifier).ToArray();
            foreach (var cloneId in ExpectedPs1CloneIdentifiers())
            {
                Assert.Contains(cloneId, ids);
            }
        }

        // ------------------------------------------------------------------
        // Fixture: CNWClassE462 / ls-462-p37 (mod steam locomotive) + its own
        // LegosLibraryOfStuff/Definitions/ClassEStreamlinedTender.json spec:
        //   clone ls-462-p37 -> ls-462-p37ST, tenderIdentifier lt-462-p37ES, positionTail -5.5
        // Issue #222 (LLW tender swap) population.
        // ------------------------------------------------------------------
        private string CnwBasePackDefinitions =>
            Path.Combine(_h.ModsRoot, "CNWClassE462", "ls-462-p37", "Definitions.json");

        [LegosLibraryRealDataFact]
        public void MainCodePath_BypassDeserialize_RealCnwPacificPack_NoTenderSwapClone()
        {
            var text = File.ReadAllText(CnwBasePackDefinitions);
            var before = LegosLibraryHarness.PostfixInvocations;

            var container = _h.BypassDeserialize(text);

            Assert.Equal(before, LegosLibraryHarness.PostfixInvocations);
            var ids = container.Objects.Select(o => o.Identifier).ToArray();
            _out.WriteLine("main/bypass identifiers: " + string.Join(", ", ids));
            Assert.Contains("ls-462-p37", ids);
            Assert.DoesNotContain("ls-462-p37ST", ids);
        }

        [LegosLibraryRealDataFact]
        public void Pr227CodePath_ColdLoad_RealCnwPacificPack_TenderSwapCloneExists()
        {
            var text = File.ReadAllText(CnwBasePackDefinitions);
            var before = LegosLibraryHarness.PostfixInvocations;
            var dropped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var container = _h.ColdLoad(text, "fuseasset://" + Uri.EscapeDataString(Path.GetDirectoryName(CnwBasePackDefinitions)), dropped);

            Assert.Equal(before + 1, LegosLibraryHarness.PostfixInvocations);
            Assert.Empty(dropped);
            var ids = container.Objects.Select(o => o.Identifier).ToArray();
            _out.WriteLine("PR#227/cold-load identifiers: " + string.Join(", ", ids));
            Assert.Contains("ls-462-p37", ids);
            Assert.Contains("ls-462-p37ST", ids);

            var clone = container.Objects.Single(o => o.Identifier == "ls-462-p37ST");
            var loco = Assert.IsAssignableFrom<SteamLocomotiveDefinition>(clone.Definition);
            Assert.Equal("lt-462-p37ES", loco.TenderIdentifier);
            Assert.Equal(-5.5f, loco.PositionTail);
            Assert.Equal("CNW Class E Pacific (Streamlined Tender)", clone.Metadata.Name);

            // The base item was not turned into the clone: it still points at its own tender.
            var baseLoco = Assert.IsAssignableFrom<SteamLocomotiveDefinition>(
                container.Objects.Single(o => o.Identifier == "ls-462-p37").Definition);
            Assert.Equal("lt-462-p37", baseLoco.TenderIdentifier);
        }

        // ------------------------------------------------------------------
        // The regression that motivated the original bypass (c188ad1): "ERIE LOGO going dead".
        // Real data: MSLDecalPack/LegosLibraryOfStuff/Definitions/Erie_lt-282-k27.json is an
        // in-place (clone:false) MakeComponentGroup spec targeting lt-282-k27, a MOD tender
        // defined in "LLW Generic Locomotive Catalog/ls-282-k27/Definitions.json". LLoS adds
        // its two shared CustomImage instances to the definition and renames them in place
        // (GroupID + Name) on EVERY postfix pass, so the ComponentGroup written into an
        // earlier container stops matching the shared instances' names as soon as a later
        // pass runs. This test shows (a) one cold load through PR #227 yields a
        // self-consistent group, and (b) any further pass through the public entry point
        // (which is exactly what FUSE's re-deserialize paths must keep bypassing) invalidates
        // the earlier container.
        // ------------------------------------------------------------------
        private string LlwK27PackDefinitions =>
            Path.Combine(_h.ModsRoot, "LLW Generic Locomotive Catalog", "ls-282-k27", "Definitions.json");

        private static (string[] activate, string[] present) ErieGroupState(Container c)
        {
            var tender = c.Objects.Single(o => o.Identifier == "lt-282-k27");
            var group = tender.Definition.Components.Single(comp =>
                comp.Kind == "ComponentGroup" && (string)comp.GetType().GetProperty("id").GetValue(comp) == "Erie_lt-282-k27");
            var activate = (string[])group.GetType().GetProperty("ActivateComponents").GetValue(group);
            var present = tender.Definition.Components.Where(comp => comp.Kind == "CustomImage").Select(comp => comp.Name).ToArray();
            return (activate, present);
        }

        [LegosLibraryRealDataFact]
        public void Pr227ColdLoad_RealErieLogoGroupSpec_SingleColdLoadIsSelfConsistent_SecondPassBreaksEarlierContainer()
        {
            if (!File.Exists(LlwK27PackDefinitions) ||
                !File.Exists(Path.Combine(_h.ModsRoot, "MSLDecalPack", "LegosLibraryOfStuff", "Definitions", "Erie_lt-282-k27.json")))
            {
                _out.WriteLine("ERIE LOGO fixture not installed; nothing to check.");
                return;
            }
            Assert.Contains("lt-282-k27", _h.SpecIdentifiers);

            var text = File.ReadAllText(LlwK27PackDefinitions);

            // main: no group at all (the in-place spec never applied to the mod tender).
            var mainContainer = _h.BypassDeserialize(text);
            var mainTender = mainContainer.Objects.Single(o => o.Identifier == "lt-282-k27");
            Assert.DoesNotContain(mainTender.Definition.Components, comp =>
                comp.Kind == "ComponentGroup" && (string)comp.GetType().GetProperty("id").GetValue(comp) == "Erie_lt-282-k27");

            // PR #227 cold load, exactly once: group present and self-consistent.
            var first = _h.ColdLoad(text, "fuseasset://" + Uri.EscapeDataString(Path.GetDirectoryName(LlwK27PackDefinitions)),
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
            var s1 = ErieGroupState(first);
            _out.WriteLine("after cold load: ActivateComponents=" + string.Join(" | ", s1.activate) + "   CustomImage names=" + string.Join(" | ", s1.present));
            Assert.Equal(2, s1.activate.Length);
            Assert.All(s1.activate, name => Assert.Contains(name, s1.present));

            // A second pass through the public entry point (what a native LocalLow mirror, a
            // duplicate store, or a re-deserialize through Deserialize would do): the shared
            // component instances are renamed again, so the FIRST container's group no longer
            // resolves any of its components — that is the dead ERIE LOGO toggle.
            var second = ContainerSerialization.Deserialize(text);
            var s2 = ErieGroupState(second);
            var s1After = ErieGroupState(first);
            _out.WriteLine("after 2nd pass: first container ActivateComponents=" + string.Join(" | ", s1After.activate) +
                           "   first container CustomImage names NOW=" + string.Join(" | ", s1After.present));
            Assert.All(s2.activate, name => Assert.Contains(name, s2.present));       // the latest container is fine
            Assert.All(s1After.activate, name => Assert.DoesNotContain(name, s1After.present)); // the earlier one is broken
        }

        // ------------------------------------------------------------------
        // Whole-install census: every asset pack Definitions.json under Mods (root or one level
        // deep, next to a Catalog.json — the folders FUSE mounts as direct stores) is loaded
        // through both code paths. Reports how many clone identifiers exist only under PR
        // #227, and which packs the native serializer rejected (fallback => no clones).
        // ------------------------------------------------------------------
        [LegosLibraryRealDataFact]
        public void Census_AllInstalledPacks_ClonesOnlyExistOnPr227Path()
        {
            var packs = Directory.GetDirectories(_h.ModsRoot)
                .SelectMany(m => new[] { m }.Concat(Directory.GetDirectories(m)))
                .Where(d => File.Exists(Path.Combine(d, "Catalog.json")) && File.Exists(Path.Combine(d, "Definitions.json")))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Assert.NotEmpty(packs);

            int packsOk = 0, packsFallback = 0, packsFailed = 0;
            int bypassItems = 0, coldItems = 0;
            var clonesOnlyOnPr = new List<string>();
            var fallbackReasons = new List<string>();
            var unbindableKinds = new List<string>();
            var identifierPacks = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var pack in packs)
            {
                var text = File.ReadAllText(Path.Combine(pack, "Definitions.json"));
                var rel = pack.Substring(_h.ModsRoot.Length).TrimStart('\\', '/');
                string[] bypassIds;
                var mainDropped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    bypassIds = _h.MainColdLoad(text, mainDropped).Objects.Select(o => o.Identifier).ToArray();
                }
                catch (Exception ex)
                {
                    packsFailed++;
                    fallbackReasons.Add($"[main path threw] {rel}: {ex.GetBaseException().GetType().Name}: {Trim(ex.GetBaseException().Message)}");
                    continue;
                }
                if (mainDropped.Count > 0)
                {
                    unbindableKinds.Add($"{rel}: {string.Join(", ", mainDropped.Select(kv => kv.Key + "=" + kv.Value))}");
                }

                var before = LegosLibraryHarness.PostfixInvocations;
                var dropped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                string[] coldIds;
                try
                {
                    coldIds = _h.ColdLoad(text, "fuseasset://" + Uri.EscapeDataString(pack), dropped).Objects.Select(o => o.Identifier).ToArray();
                }
                catch (Exception ex)
                {
                    packsFailed++;
                    fallbackReasons.Add($"[PR#227 cold-load threw] {rel}: {ex.GetBaseException().GetType().Name}: {Trim(ex.GetBaseException().Message)}");
                    continue;
                }
                Assert.Equal(mainDropped.OrderBy(kv => kv.Key).ToArray(), dropped.OrderBy(kv => kv.Key).ToArray());

                var fired = LegosLibraryHarness.PostfixInvocations - before;
                if (fired == 0)
                {
                    packsFallback++;
                    string reason;
                    try { ContainerSerialization.Deserialize(text); reason = "(native succeeded on retry?)"; }
                    catch (Exception ex) { reason = ex.GetBaseException().GetType().Name + ": " + Trim(ex.GetBaseException().Message); }
                    fallbackReasons.Add($"[native rejected -> tolerant bypass, no LLoS pass] {rel}: {reason}");
                }
                else
                {
                    packsOk++;
                }

                bypassItems += bypassIds.Length;
                coldItems += coldIds.Length;
                foreach (var id in bypassIds)
                {
                    if (!identifierPacks.TryGetValue(id, out var list)) identifierPacks[id] = list = new List<string>();
                    list.Add(rel);
                }
                foreach (var extra in coldIds.Except(bypassIds))
                {
                    clonesOnlyOnPr.Add(extra + "  <=  " + rel);
                }
                // The base identifiers are always still there under PR #227.
                Assert.Empty(bypassIds.Except(coldIds));
            }

            _out.WriteLine($"packs: {packs.Length}; native ok (LLoS pass ran once): {packsOk}; native rejected (tolerant fallback, no LLoS pass): {packsFallback}; both paths threw: {packsFailed}");
            _out.WriteLine($"packs whose components could not all bind in this harness (kinds from mods the harness does not register; FUSE drops them on BOTH paths): {unbindableKinds.Count}");
            foreach (var line in unbindableKinds) _out.WriteLine("  " + line);
            _out.WriteLine($"items via main/bypass: {bypassItems}; items via PR#227 cold load: {coldItems}; clone items that exist ONLY under PR#227: {clonesOnlyOnPr.Count}");
            _out.WriteLine($"identifiers defined by more than one mounted pack: {identifierPacks.Count(kv => kv.Value.Count > 1)}");
            foreach (var line in fallbackReasons) _out.WriteLine("  " + line);
            _out.WriteLine("clone identifiers only present under PR#227:");
            foreach (var line in clonesOnlyOnPr) _out.WriteLine("  " + line);

            Assert.True(clonesOnlyOnPr.Count > 0, "expected LLoS clones of mod-pack cars to exist under the PR #227 path");
        }

        private static string Trim(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 160 ? s.Substring(0, 160) + "..." : s;
        }
    }

    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class FuseDirectStoreLegosLibraryReproCollection
    {
        public const string Name = "FuseDirectStoreLegosLibraryRepro";
    }

    /// <summary>Skips when the local Railroader install with LLoS + LogosAndDeco + the packs is absent.</summary>
    public sealed class LegosLibraryRealDataFactAttribute : FactAttribute
    {
        public LegosLibraryRealDataFactAttribute()
        {
            var reason = LegosLibraryHarness.UnavailableReason();
            if (reason != null)
            {
                Skip = reason;
            }
        }
    }

    /// <summary>
    /// Loads the real LegosLibraryOfStuff.dll, primes it the way its plugin init + LogosAndDeco
    /// do, applies its real Harmony patch classes, and exposes FUSE's private cold-load /
    /// bypass helpers.
    /// </summary>
    public sealed class LegosLibraryHarness : IDisposable
    {
        // Opt-in only. This harness loads real third-party mod assemblies into the
        // test process and Harmony-patches the game's serializer, so it must never
        // run implicitly (CI, a contributor's plain `dotnet test`). Point
        // FUSE_TEST_LLOS_GAME_DIR at a Railroader install whose Mods folder holds
        // LegosLibraryOfStuff, legotrainman.logosanddeco and the base/repaint packs
        // named in the fixtures, e.g.
        //   $env:FUSE_TEST_LLOS_GAME_DIR = "F:\SteamLibrary\steamapps\common\Railroader - MyMods"
        //   dotnet test FUSE.Tests --filter FullyQualifiedName~FuseDirectStoreLegosLibraryReproTests
        private static readonly string[] GameDirCandidates =
        {
            Environment.GetEnvironmentVariable("FUSE_TEST_LLOS_GAME_DIR"),
        };

        public static string FindGameDir()
        {
            foreach (var c in GameDirCandidates)
            {
                if (string.IsNullOrEmpty(c)) continue;
                if (File.Exists(Path.Combine(c, "Railroader_Data", "Managed", "Definition.dll")) &&
                    File.Exists(Path.Combine(c, "Mods", "LegosLibraryOfStuff", "LegosLibraryOfStuff.dll")))
                {
                    return c;
                }
            }
            return null;
        }

        public static string UnavailableReason()
        {
            var game = FindGameDir();
            if (game == null) return "Opt-in real-LLoS reproduction: set FUSE_TEST_LLOS_GAME_DIR to a Railroader install whose Mods folder has LegosLibraryOfStuff.";
            var mods = Path.Combine(game, "Mods");
            var needed = new[]
            {
                Path.Combine(mods, "legotrainman.logosanddeco", "LegosLogosAndDeco.dll"),
                Path.Combine(mods, "PS-1 40ft Boxcar Series", "PS-1-40ft-8ft p-s-door", "Definitions.json"),
                Path.Combine(mods, "PS-1 Midwest Boxcar Repaints", "LegosLibraryOfStuff", "Definitions", "CNW-ps1-1.json"),
                Path.Combine(mods, "CNWClassE462", "ls-462-p37", "Definitions.json"),
                Path.Combine(mods, "CNWClassE462", "LegosLibraryOfStuff", "Definitions", "ClassEStreamlinedTender.json"),
            };
            foreach (var n in needed)
            {
                if (!File.Exists(n)) return "Missing real fixture: " + n;
            }
            return null;
        }

        public string GameDir { get; }
        public string ModsRoot { get; }
        public Assembly LlosAssembly { get; }
        public Assembly LadAssembly { get; }
        public int SpecCount { get; private set; }
        public IReadOnlyList<string> SpecIdentifiers { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> SpecNewIdentifiers { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> RegisteredSpecPacks { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> RegisteredKinds { get; private set; } = Array.Empty<string>();
        public string SpecSource { get; private set; } = "(none)";
        public string JsonSubtypesMode { get; private set; } = "(none)";

        // Counts invocations of the REAL LLoS postfix (a tiny counting postfix installed on the
        // same target; Harmony runs all postfixes, so this counts once per Deserialize call).
        private static int _postfixInvocations;
        public static int PostfixInvocations => System.Threading.Volatile.Read(ref _postfixInvocations);

        private readonly Harmony _harmony;
        private readonly ResolveEventHandler _resolver;
        private readonly MethodInfo _loadResilient;
        private readonly MethodInfo _bypass;
        private readonly MethodInfo _canBind;
        private readonly Action _restoreJsonSubtypesCache;
        private readonly string _describe;

        public LegosLibraryHarness()
        {
            var reason = UnavailableReason();
            if (reason != null)
            {
                // Tests are skipped via the attribute; keep the fixture inert.
                _describe = reason;
                return;
            }

            GameDir = FindGameDir();
            ModsRoot = Path.Combine(GameDir, "Mods");
            var managed = Path.Combine(GameDir, "Railroader_Data", "Managed");
            var llosDir = Path.Combine(ModsRoot, "LegosLibraryOfStuff");
            var ladDir = Path.Combine(ModsRoot, "legotrainman.logosanddeco");
            var lbrsDir = Path.Combine(ModsRoot, "LegosBetterRollingStock");

            var probeDirs = new[] { AppDomain.CurrentDomain.BaseDirectory, managed, Path.Combine(managed, "UnityModManager"), llosDir, ladDir, lbrsDir };
            _resolver = (sender, args) =>
            {
                var name = new AssemblyName(args.Name).Name;
                foreach (var dir in probeDirs)
                {
                    var p = Path.Combine(dir, name + ".dll");
                    if (File.Exists(p))
                    {
                        try
                        {
                            return Assembly.LoadFrom(p);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Assembly resolver could not load '{p}': {ex.Message}");
                        }
                    }
                }
                return null;
            };
            AppDomain.CurrentDomain.AssemblyResolve += _resolver;

            LlosAssembly = Assembly.LoadFrom(Path.Combine(llosDir, "LegosLibraryOfStuff.dll"));
            LadAssembly = Assembly.LoadFrom(Path.Combine(ladDir, "LegosLogosAndDeco.dll"));

            var los = LlosAssembly.GetType("LegosLibraryOfStuff.LibraryOfStuff", throwOnError: true);
            var deserPatch = LlosAssembly.GetType("LegosLibraryOfStuff.ContainerSerializationDeserializePatch", throwOnError: true);
            var subtypesPatch = LlosAssembly.GetType("LegosLibraryOfStuff.AddJsonSubTypesPatch", throwOnError: true);

            // --- UMM ModEntry plumbing (real UnityModManager types) ---
            var myModEntryField = los.GetField("myModEntry", BindingFlags.Public | BindingFlags.Static);
            var modEntryType = myModEntryField.FieldType;                       // UnityModManagerNet.UnityModManager+ModEntry
            var ummType = modEntryType.DeclaringType;                            // UnityModManagerNet.UnityModManager
            var modInfoType = ummType.GetNestedType("ModInfo");
            var modLoggerType = modEntryType.GetNestedType("ModLogger");
            Func<string, string, object> makeEntry = (id, path) =>
            {
                var info = Activator.CreateInstance(modInfoType);
                modInfoType.GetField("Id").SetValue(info, id);
                modInfoType.GetField("Version").SetValue(info, "1.0.0");
                modInfoType.GetField("ManagerVersion").SetValue(info, "0.32.4");
                modInfoType.GetField("DisplayName").SetValue(info, id);
                try
                {
                    // Real ctor: ModEntry(ModInfo info, string path).
                    return Activator.CreateInstance(modEntryType, info, path);
                }
                catch (Exception)
                {
                    // UnityModManager's static initializer is unavailable in this process
                    // (it needs the UnityEngine facade). Build the entry without running the
                    // ctor: the postfix path only reads Info, Path, Enabled and Logger.
                    var entry = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(modEntryType);
                    modEntryType.GetField("Info").SetValue(entry, info);
                    modEntryType.GetField("Path").SetValue(entry, path);
                    modEntryType.GetField("Logger").SetValue(entry, Activator.CreateInstance(modLoggerType, id));
                    modEntryType.GetField("Enabled").SetValue(entry, true);
                    return entry;
                }
            };

            // LibraryOfStuff.Load(): myModEntry = modEntry (Logger is used by the postfix).
            myModEntryField.SetValue(null, makeEntry("LegosLibraryOfStuff", llosDir));

            // LibraryOfStuff.Load(): AddNewComponent(AttributeModifierComponent, builder),
            //                        AddNewComponent(ComponentGroupComponent, builder)
            var addByTypes = los.GetMethod("AddNewComponent", new[] { typeof(Type), typeof(Type) });
            var addByKind = los.GetMethods().Single(m => m.Name == "AddNewComponent" && m.GetParameters().Length == 4);
            var kinds = new List<string>();
            addByTypes.Invoke(null, new object[]
            {
                LlosAssembly.GetType("LegosLibraryOfStuff.AttributeModifierComponent", true),
                LlosAssembly.GetType("LegosLibraryOfStuff.AttributeModifierComponentBuilder", true),
            });
            kinds.Add("AttributeModifierComponent");
            addByTypes.Invoke(null, new object[]
            {
                LlosAssembly.GetType("LegosLibraryOfStuff.ComponentGroupComponent", true),
                LlosAssembly.GetType("LegosLibraryOfStuff.ComponentGroupComponentBuilder", true),
            });
            kinds.Add("ComponentGroup");

            // LegosLogosAndDeco.Main.Load(): the seven named kinds + PrefabModelComponent.
            foreach (var pair in new[]
            {
                ("CustomImage", "CustomImageComponent"),
                ("ColorPainterComponent", "ColorPainterComponent"),
                ("SetTextDecalComponent", "SetTextDecalComponent"),
                ("CustomTextDecalComponent", "CustomTextDecalComponent"),
                ("DefaultLivelryComponent", "DefaultLivelryComponent"),
                ("ColorableImageComponent", "ColorableImageComponent"),
                ("MaterialColorizerComponent", "MaterialColorizerComponent"),
            })
            {
                var compType = LadAssembly.GetType("LegosLogosAndDeco." + pair.Item2, true);
                var builderType = LadAssembly.GetType("LegosLogosAndDeco." + pair.Item2 + "Builder", true);
                addByKind.Invoke(null, new object[] { typeof(Component), pair.Item1, compType, Activator.CreateInstance(builderType) });
                kinds.Add(pair.Item1);
            }
            addByTypes.Invoke(null, new object[]
            {
                LadAssembly.GetType("LegosLogosAndDeco.PrefabModelComponent", true),
                LadAssembly.GetType("LegosLogosAndDeco.PrefabModelComponentBuilder", true),
            });
            kinds.Add("PrefabModelComponent");

            // LegosBetterRollingStock.Main.Load() kinds (best effort; only matters for packs
            // that use them — without them such packs fall back to the tolerant path).
            var lbrsDll = Path.Combine(lbrsDir, "LegosBetterRollingStock.dll");
            if (File.Exists(lbrsDll))
            {
                try
                {
                    var lbrs = Assembly.LoadFrom(lbrsDll);
                    foreach (var pair in new[]
                    {
                        ("CustomTruckComponent", "CustomTruckComponent", "CustomTruckComponentBuilder"),
                        ("CustomCompressorComponent", "CustomCompressorComponent", "CustomCompressorComponentBuilder"),
                    })
                    {
                        var compType = lbrs.GetType("LegosBetterRollingStock." + pair.Item2, true);
                        var builderType = lbrs.GetType("LegosBetterRollingStock." + pair.Item3, true);
                        addByKind.Invoke(null, new object[] { typeof(Component), pair.Item1, compType, Activator.CreateInstance(builderType) });
                        kinds.Add(pair.Item1);
                    }
                    foreach (var pair in new[]
                    {
                        ("CouplerMoverCompnent", "CouplerMoverComponentBuilder"),
                        ("CustomAnimationControllerComponent", "CustomAnimationControllerBuilder"),
                    })
                    {
                        var compType = lbrs.GetType("LegosBetterRollingStock." + pair.Item1, true);
                        var builderType = lbrs.GetType("LegosBetterRollingStock." + pair.Item2, true);
                        addByTypes.Invoke(null, new object[] { compType, builderType });
                        kinds.Add(((Component)Activator.CreateInstance(compType)).Kind);
                    }
                }
                catch (Exception ex)
                {
                    kinds.Add("(LegosBetterRollingStock kinds NOT registered: " + ex.GetBaseException().Message + ")");
                }
            }
            RegisteredKinds = kinds;

            // --- Every real mod folder that ships LegosLibraryOfStuff/Definitions (what
            //     LLoS.LoadJsonDefinitions scans through UnityModManager.modEntries) ---
            var specPackDirs = new List<(string id, string dir)>();
            foreach (var modDir in Directory.GetDirectories(ModsRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(Path.Combine(modDir, "LegosLibraryOfStuff", "Definitions"))) continue;
                var id = Path.GetFileName(modDir);
                var infoPath = Path.Combine(modDir, "info.json");
                if (File.Exists(infoPath))
                {
                    try
                    {
                        id = (string)JObject.Parse(File.ReadAllText(infoPath))["Id"] ?? id;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Could not read mod id from '{infoPath}'; using folder name: {ex.Message}");
                    }
                }
                specPackDirs.Add((id, modDir));
            }
            RegisteredSpecPacks = specPackDirs.Select(t => t.id).ToList();

            IList modEntries = null;
            try
            {
                modEntries = (IList)ummType.GetField("modEntries", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            }
            catch (Exception)
            {
                // UnityModManager..cctor unavailable in this process (see makeEntry).
                modEntries = null;
            }
            if (modEntries != null)
            {
                foreach (var t in specPackDirs)
                {
                    modEntries.Add(makeEntry(t.id, t.dir));
                }
            }

            // --- Real Harmony patch classes (the two the postfix path needs) ---
            _harmony = new Harmony("fuse.tests.legos-library-repro." + Guid.NewGuid().ToString("N"));
            _harmony.CreateClassProcessor(subtypesPatch).Patch();
            _harmony.CreateClassProcessor(deserPatch).Patch();
            _harmony.Patch(
                AccessTools.Method(typeof(ContainerSerialization), nameof(ContainerSerialization.Deserialize), new[] { typeof(string) }),
                postfix: new HarmonyMethod(typeof(LegosLibraryHarness), nameof(CountingPostfix)));

            // LLoS's AddJsonSubTypesPatch postfixes JsonSubtypes.GetAttributes(Type) — a
            // two-line method that the .NET Framework JIT inlines into its generic callers.
            // In-game LLoS patches it before anything deserializes a Component; in this test
            // process another test class may already have JIT-compiled those callers, in
            // which case the postfix can no longer intercept. Probe, and if so, install what
            // the postfix would have returned straight into JsonSubtypes' attribute cache (a
            // live view over LLoS's JsonSubTypeRegistry) — a harness shim, reported below.
            if (ProbeKindBinds("ComponentGroup"))
            {
                JsonSubtypesMode = "LLoS AddJsonSubTypesPatch (real Harmony postfix) is effective";
            }
            else
            {
                var getAttributes = (MethodInfo)subtypesPatch.GetMethod("TargetMethod", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                _harmony.Unpatch(getAttributes, HarmonyPatchType.Postfix, _harmony.Id);
                _restoreJsonSubtypesCache = InstallLiveKnownSubTypeView(LlosAssembly, getAttributes.DeclaringType);
                if (!ProbeKindBinds("ComponentGroup"))
                {
                    throw new InvalidOperationException("Could not make LLoS-registered component kinds bind in this process.");
                }
                JsonSubtypesMode = "harness shim: JsonSubtypes.GetAttributes was already JIT-inlined into its callers before LLoS's postfix was applied; " +
                                   "seeded JsonSubtypes._attributesCache[Component] with a live view over LLoS's JsonSubTypeRegistry instead";
            }

            // Prime the spec cache now (the postfix would do it lazily on first Deserialize).
            if (modEntries != null)
            {
                // The real thing: LLoS scans UnityModManager.modEntries itself.
                los.GetMethod("LoadJsonDefinitions", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                SpecSource = "LibraryOfStuff.LoadJsonDefinitions() over UnityModManager.modEntries";
            }
            else
            {
                // Replica of LoadJsonDefinitions (same folders, same file glob, same
                // JsonConvert call with LLoS's own jsonSettings and DefinitionEditorJsonObject),
                // seeding LLoS's own static lists. With Count > 1 the real method returns
                // early on every later postfix call, exactly as it does in-game after the
                // first scan.
                SeedSpecsLikeLoadJsonDefinitions(los, specPackDirs.Select(t => t.dir));
                SpecSource = "replica of LoadJsonDefinitions (UnityModManager statics unavailable in this process)";
            }
            var newDefinitions = (IEnumerable)los.GetField("newDefinitions").GetValue(null);
            var identifiers = new List<string>();
            var newIdentifiers = new List<string>();
            foreach (var wrapper in newDefinitions)
            {
                var json = wrapper.GetType().GetField("json").GetValue(wrapper);
                identifiers.Add((string)json.GetType().GetField("identifier").GetValue(json));
                newIdentifiers.Add((string)json.GetType().GetField("newIdentifier").GetValue(json));
            }
            SpecCount = identifiers.Count;
            SpecIdentifiers = identifiers;
            SpecNewIdentifiers = newIdentifiers;

            _loadResilient = typeof(FuseAssetPackRegistry).GetMethod("LoadResilientDirectContainer",
                BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(string), typeof(string), typeof(IDictionary<string, int>) }, null);
            _bypass = typeof(FuseAssetPackRegistry).GetMethod("BypassDeserialize",
                BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
            _canBind = typeof(FuseAssetPackRegistry).GetMethod("CanBindComponent",
                BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(JObject) }, null);
            if (_loadResilient == null || _bypass == null || _canBind == null)
            {
                throw new InvalidOperationException("FUSE private helpers not found (LoadResilientDirectContainer / BypassDeserialize / CanBindComponent)");
            }

            _describe =
                $"GameDir={GameDir}\nLLoS={LlosAssembly.Location} v{LlosAssembly.GetName().Version}\nLogosAndDeco={LadAssembly.Location}\n" +
                $"kinds registered: {string.Join(", ", kinds)}\n" +
                $"spec source: {SpecSource}\n" +
                $"json subtypes: {JsonSubtypesMode}\n" +
                $"spec packs registered ({RegisteredSpecPacks.Count}): {string.Join(", ", RegisteredSpecPacks)}\n" +
                $"LLoS specs parsed: {SpecCount} (clone specs: {newIdentifiers.Count(s => !string.IsNullOrEmpty(s))})";
        }

        private static readonly MethodInfo GameSettingsMethod =
            AccessTools.Method(typeof(ContainerSerialization), "JsonSerializerSettings");

        private static bool ProbeKindBinds(string kind)
        {
            try
            {
                var settings = (Newtonsoft.Json.JsonSerializerSettings)GameSettingsMethod.Invoke(null, null);
                var component = Newtonsoft.Json.JsonConvert.DeserializeObject<Component>("{\"kind\":\"" + kind + "\",\"name\":\"probe\"}", settings);
                return component != null && component.Kind == kind;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Equivalent of LLoS's AddJsonSubTypesPatch.Postfix, materialised into the cache:
        //   __result = __result.Concat(registry[Component].Select(s => new KnownSubTypeAttribute(s.Type, s.TypeIdentifier)))
        private static Action InstallLiveKnownSubTypeView(Assembly llos, Type jsonSubtypesType)
        {
            var cache = (IDictionary<Type, IEnumerable<object>>)AccessTools.Field(jsonSubtypesType, "_attributesCache").GetValue(null);
            var registry = llos.GetType("LegosLibraryOfStuff.JsonSubTypeRegistry", true);
            var tryGet = registry.GetMethod("TryGetForBaseClass", BindingFlags.Public | BindingFlags.Static);
            var baseType = typeof(Component);
            var original = baseType.GetCustomAttributes(inherit: false);
            var knownSubTypeAttributeType = original.Select(a => a.GetType()).First(t => t.Name == "KnownSubTypeAttribute");
            var ctor = knownSubTypeAttributeType.GetConstructor(new[] { typeof(Type), typeof(object) });

            IEnumerable<object> Live()
            {
                foreach (var a in original)
                {
                    yield return a;
                }

                var args = new object[] { baseType, null };
                if ((bool)tryGet.Invoke(null, args) && args[1] is IEnumerable list)
                {
                    foreach (var tuple in list)
                    {
                        var id = (string)tuple.GetType().GetField("Item1").GetValue(tuple);
                        var type = (Type)tuple.GetType().GetField("Item2").GetValue(tuple);
                        yield return ctor.Invoke(new object[] { type, id });
                    }
                }
            }

            var hadOriginal = cache.TryGetValue(baseType, out var previous);
            cache[baseType] = Live();
            return () =>
            {
                if (hadOriginal) cache[baseType] = previous; else cache.Remove(baseType);
            };
        }

        // Mirrors LegosLibraryOfStuff.LibraryOfStuff.LoadJsonDefinitions() body (1.4.6):
        //   for each enabled mod entry: <Path>/LegosLibraryOfStuff/Definitions + its immediate
        //   subdirectories, "*.json"; each file -> JsonConvert.DeserializeObject<DefinitionEditorJsonObject>(text, jsonSettings)
        //   -> new DefinitionEditorWrapper { path, json } appended to newDefinitions, identifier to definitionIdentifiers;
        //   per-file failures are logged "Failed to load file" and skipped.
        private static void SeedSpecsLikeLoadJsonDefinitions(Type los, IEnumerable<string> modDirs)
        {
            var jsonSettings = (Newtonsoft.Json.JsonSerializerSettings)los.GetField("jsonSettings").GetValue(null);
            var specType = los.Assembly.GetType("LegosLibraryOfStuff.DefinitionEditorJsonObject", true);
            var wrapperType = los.Assembly.GetType("LegosLibraryOfStuff.DefinitionEditorWrapper", true);
            var newDefinitions = (IList)los.GetField("newDefinitions").GetValue(null);
            var definitionIdentifiers = (IList)los.GetField("definitionIdentifiers").GetValue(null);
            var files = new List<string>();
            foreach (var modDir in modDirs)
            {
                var text = Path.Combine(modDir, "LegosLibraryOfStuff", "Definitions");
                if (!Directory.Exists(text)) continue;
                var dirs = new List<string> { text };
                dirs.AddRange(Directory.GetDirectories(text));
                foreach (var d in dirs)
                {
                    files.AddRange(Directory.EnumerateFiles(d, "*.json"));
                }
            }
            foreach (var file in files)
            {
                try
                {
                    var json = Newtonsoft.Json.JsonConvert.DeserializeObject(File.ReadAllText(file), specType, jsonSettings)
                               ?? throw new Exception("No json Loaded");
                    var wrapper = Activator.CreateInstance(wrapperType);
                    wrapperType.GetField("path").SetValue(wrapper, file);
                    wrapperType.GetField("json").SetValue(wrapper, json);
                    newDefinitions.Add(wrapper);
                    definitionIdentifiers.Add((string)specType.GetField("identifier").GetValue(json));
                }
                catch (Exception)
                {
                    System.Console.WriteLine("[LegosLibraryOfStuff-replica] Failed to load file " + file);
                }
            }
        }

        // Harmony postfix; static, tiny.
        private static void CountingPostfix()
        {
            System.Threading.Interlocked.Increment(ref _postfixInvocations);
        }

        public string Describe() => _describe;

        /// <summary>PR #227 cold-load path: FUSE's private LoadResilientDirectContainer.</summary>
        public Container ColdLoad(string text, string storeIdentifier, IDictionary<string, int> dropped)
        {
            try
            {
                return (Container)_loadResilient.Invoke(null, new object[] { text, storeIdentifier, dropped });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        /// <summary>main's cold-load path: FUSE's private BypassDeserialize (main's
        /// LoadResilientDirectContainer body is BypassDeserialize + FilterUnbindableComponents retry).</summary>
        public Container BypassDeserialize(string text)
        {
            try
            {
                return (Container)_bypass.Invoke(null, new object[] { text });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        /// <summary>main's LoadResilientDirectContainer, verbatim: BypassDeserialize, and on
        /// failure FilterUnbindableComponents(CanBindComponent) + BypassDeserialize again.</summary>
        public Container MainColdLoad(string text, IDictionary<string, int> dropped)
        {
            try
            {
                return BypassDeserialize(text);
            }
            catch (Exception)
            {
                var canBind = (Func<JObject, bool>)Delegate.CreateDelegate(typeof(Func<JObject, bool>), _canBind);
                var filtered = FuseAssetPackRegistry.FilterUnbindableComponents(text, dropped, canBind);
                return BypassDeserialize(filtered);
            }
        }

        public void Dispose()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchAll(_harmony.Id);
            }
            _restoreJsonSubtypesCache?.Invoke();
            if (_resolver != null)
            {
                AppDomain.CurrentDomain.AssemblyResolve -= _resolver;
            }
        }
    }
}
