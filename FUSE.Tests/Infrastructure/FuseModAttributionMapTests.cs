using System;
using System.Collections.Generic;
using System.Text;
using FUSE.Infrastructure;
using Xunit;

namespace FUSE.Tests.Infrastructure
{
    /// <summary>
    /// Tests for the attribution map's pure cores: the stack-trace token
    /// parser (TryAttributeStackCore) and the token-map assembly with its
    /// denylist/ambiguity rules (BuildTokenMapCore). Both are static pure
    /// functions over caller-supplied data, so these tests run without
    /// Unity, UMM, or any live mod population — the runtime harvesting
    /// paths stay in-game-only by design.
    /// </summary>
    public class FuseModAttributionMapTests
    {
        private static Dictionary<string, (string modId, string displayName)> Map(
            params (string token, string modId, string displayName)[] entries)
        {
            var map = new Dictionary<string, (string modId, string displayName)>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                map[entry.token] = (entry.modId, entry.displayName);
            }

            return map;
        }

        private static readonly Dictionary<string, (string modId, string displayName)> MapEnhancerMap =
            Map(("MapEnhancer", "mapenhancer", "Map Enhancer"));

        [Fact]
        public void UnityTraceLine_Parses_AndReturnsTheFrame()
        {
            var trace =
                "MapEnhancer.MapEnhancer.UpdateCullingSpheres (Game.Events.WorldDidMoveEvent evt) (at <abc123>:0)\n" +
                "Game.Events.Messenger.SendToList (Game.Events.WorldDidMoveEvent evt) (at <def456>:0)";

            var matched = FuseModAttributionMap.TryAttributeStackCore(
                trace, MapEnhancerMap, out var modId, out var displayName, out var frame);

            Assert.True(matched);
            Assert.Equal("mapenhancer", modId);
            Assert.Equal("Map Enhancer", displayName);
            Assert.Equal("MapEnhancer.MapEnhancer.UpdateCullingSpheres", frame);
        }

        [Fact]
        public void InnermostMatchingFrame_Wins_OverALaterMatch()
        {
            var map = Map(
                ("ModA", "mod.a", "Mod A"),
                ("ModB", "mod.b", "Mod B"));
            var trace =
                "ModA.Deep.Thrower (System.String s) (at <a>:0)\n" +
                "ModB.Outer.Caller () (at <b>:0)";

            Assert.True(FuseModAttributionMap.TryAttributeStackCore(
                trace, map, out var modId, out _, out var frame));
            Assert.Equal("mod.a", modId);
            Assert.Equal("ModA.Deep.Thrower", frame);
        }

        [Fact]
        public void UnmatchedInnerFrames_FallThrough_ToTheFirstModFrame()
        {
            var trace =
                "Game.Events.Messenger.SendToList (Game.Events.WorldDidMoveEvent evt) (at <a>:0)\n" +
                "UnityEngine.Events.UnityEvent.Invoke () (at <b>:0)\n" +
                "MapEnhancer.MapEnhancer.Rebuild () (at <c>:0)";

            Assert.True(FuseModAttributionMap.TryAttributeStackCore(
                trace, MapEnhancerMap, out var modId, out _, out var frame));
            Assert.Equal("mapenhancer", modId);
            Assert.Equal("MapEnhancer.MapEnhancer.Rebuild", frame);
        }

        [Fact]
        public void TwoSegmentToken_MatchesDeepReverseDomainNamespaces()
        {
            var map = Map(("Us.Dchn", "us.dchn.rebill", "Rebill Industry Cars"));
            var trace = "Us.Dchn.Railroader.RebillIndustryCars.RebillSystem.AutoConfigUnloaders () (at <a>:0)";

            Assert.True(FuseModAttributionMap.TryAttributeStackCore(
                trace, map, out var modId, out _, out var frame));
            Assert.Equal("us.dchn.rebill", modId);
            Assert.Equal("Us.Dchn.Railroader.RebillIndustryCars.RebillSystem.AutoConfigUnloaders", frame);
        }

        [Fact]
        public void TwoSegmentToken_IsPreferredOverAOneSegmentToken()
        {
            var map = Map(
                ("Us", "wrong.mod", "Wrong"),
                ("Us.Dchn", "right.mod", "Right"));

            Assert.True(FuseModAttributionMap.TryAttributeStackCore(
                "Us.Dchn.Railroader.RebillSystem.Tick () (at <a>:0)",
                map, out var modId, out _, out _));
            Assert.Equal("right.mod", modId);
        }

        [Fact]
        public void MonoStyleFrames_WithAtPrefixAndIlOffset_Parse()
        {
            var trace =
                "  at MapEnhancer.MapEnhancer.Rebuild () [0x0001d] in <hash>:0 \n" +
                "  at MapEnhancer.MapEnhancer.OnMapDidLoad (Map.Runtime.MapDidLoadEvent evt) [0x001bb] in <hash>:0";

            Assert.True(FuseModAttributionMap.TryAttributeStackCore(
                trace, MapEnhancerMap, out var modId, out _, out var frame));
            Assert.Equal("mapenhancer", modId);
            Assert.Equal("MapEnhancer.MapEnhancer.Rebuild", frame);
        }

        [Fact]
        public void DebugLogStyleFrames_WithColonSeparator_Parse()
        {
            var trace = "MapEnhancer.MapEnhancer:Rebuild()";

            Assert.True(FuseModAttributionMap.TryAttributeStackCore(
                trace, MapEnhancerMap, out var modId, out _, out var frame));
            Assert.Equal("mapenhancer", modId);
            // The frame keeps the method tail; only token lookup cuts at ':'.
            Assert.Equal("MapEnhancer.MapEnhancer:Rebuild()", frame);
        }

        [Fact]
        public void MalformedLines_AreSkipped_NotFatal()
        {
            var trace =
                "----\n" +
                "Rethrow as TargetInvocationException\n" +
                "\n" +
                "no dots on this line\n" +
                "MapEnhancer.MapEnhancer.CreateSwitches () (at <a>:0)";

            Assert.True(FuseModAttributionMap.TryAttributeStackCore(
                trace, MapEnhancerMap, out var modId, out _, out var frame));
            Assert.Equal("mapenhancer", modId);
            Assert.Equal("MapEnhancer.MapEnhancer.CreateSwitches", frame);
        }

        [Fact]
        public void NoMatchingFrame_ReturnsFalse_WithNullOuts()
        {
            var trace =
                "Game.Events.Messenger.SendToList (Game.Events.WorldDidMoveEvent evt) (at <a>:0)\n" +
                "UnityEngine.Events.UnityEvent.Invoke () (at <b>:0)";

            Assert.False(FuseModAttributionMap.TryAttributeStackCore(
                trace, MapEnhancerMap, out var modId, out var displayName, out var frame));
            Assert.Null(modId);
            Assert.Null(displayName);
            Assert.Null(frame);
        }

        [Fact]
        public void EmptyStack_NullMap_EmptyMap_AllReturnFalse()
        {
            Assert.False(FuseModAttributionMap.TryAttributeStackCore(
                null, MapEnhancerMap, out _, out _, out _));
            Assert.False(FuseModAttributionMap.TryAttributeStackCore(
                string.Empty, MapEnhancerMap, out _, out _, out _));
            Assert.False(FuseModAttributionMap.TryAttributeStackCore(
                "MapEnhancer.MapEnhancer.Rebuild () (at <a>:0)", null, out _, out _, out _));
            Assert.False(FuseModAttributionMap.TryAttributeStackCore(
                "MapEnhancer.MapEnhancer.Rebuild () (at <a>:0)", Map(), out _, out _, out _));
        }

        [Fact]
        public void FrameCap_StopsAfterTwelveFrames()
        {
            var beyondCap = new StringBuilder();
            for (var i = 0; i < 12; i++)
            {
                beyondCap.AppendLine($"Game.Events.Frame{i}.Method (System.Int32 x) (at <a>:0)");
            }

            beyondCap.AppendLine("MapEnhancer.MapEnhancer.Rebuild () (at <b>:0)");

            Assert.False(FuseModAttributionMap.TryAttributeStackCore(
                beyondCap.ToString(), MapEnhancerMap, out _, out _, out _));

            var withinCap = new StringBuilder();
            for (var i = 0; i < 11; i++)
            {
                withinCap.AppendLine($"Game.Events.Frame{i}.Method (System.Int32 x) (at <a>:0)");
            }

            withinCap.AppendLine("MapEnhancer.MapEnhancer.Rebuild () (at <b>:0)");

            Assert.True(FuseModAttributionMap.TryAttributeStackCore(
                withinCap.ToString(), MapEnhancerMap, out var modId, out _, out _));
            Assert.Equal("mapenhancer", modId);
        }

        [Fact]
        public void BuildTokenMapCore_DropsDenylistedTokens()
        {
            var map = FuseModAttributionMap.BuildTokenMapCore(
                new[]
                {
                    ("Game", "mod.a", "Mod A"),          // shadows a game root: dropped
                    ("ModA", "mod.a", "Mod A")
                },
                new[] { "Game" },
                out var dropped);

            Assert.Equal(1, dropped);
            Assert.False(map.ContainsKey("Game"));
            Assert.True(map.ContainsKey("ModA"));
            Assert.Equal("mod.a", map["ModA"].modId);
        }

        [Fact]
        public void BuildTokenMapCore_DropsTwoSegmentTokensUnderADeniedRoot()
        {
            // Mods routinely ship polyfill/generated types under BCL roots
            // (System.Runtime.CompilerServices.IsExternalInit and friends).
            // Harvesting those as two-segment tokens would attribute every
            // engine/BCL frame to whichever mod happened to embed the
            // polyfill — the denied root has to cover its children too.
            var map = FuseModAttributionMap.BuildTokenMapCore(
                new[]
                {
                    ("System.Runtime", "mod.a", "Mod A"),
                    ("System.Diagnostics", "mod.a", "Mod A"),
                    ("Microsoft.CodeAnalysis", "mod.b", "Mod B"),
                    ("ModA.Internals", "mod.a", "Mod A")
                },
                new[] { "System", "Microsoft" },
                out var dropped);

            Assert.Equal(3, dropped);
            Assert.False(map.ContainsKey("System.Runtime"));
            Assert.False(map.ContainsKey("System.Diagnostics"));
            Assert.False(map.ContainsKey("Microsoft.CodeAnalysis"));

            // A mod-owned two-segment token still attributes normally.
            Assert.True(map.ContainsKey("ModA.Internals"));
            Assert.Equal("mod.a", map["ModA.Internals"].modId);
        }

        [Fact]
        public void BuildTokenMapCore_DropsTokensClaimedByTwoMods()
        {
            var map = FuseModAttributionMap.BuildTokenMapCore(
                new[]
                {
                    ("Shared", "mod.a", "Mod A"),
                    ("Shared", "mod.b", "Mod B"),   // conflict: token removed + denied
                    ("Shared", "mod.c", "Mod C")    // now denied
                },
                Array.Empty<string>(),
                out var dropped);

            Assert.Equal(2, dropped);
            Assert.False(map.ContainsKey("Shared"));
        }

        [Fact]
        public void BuildTokenMapCore_IgnoresDuplicatesFromTheSameMod_AndBlankTokens()
        {
            var map = FuseModAttributionMap.BuildTokenMapCore(
                new[]
                {
                    ("ModA", "mod.a", "Mod A"),
                    ("ModA", "mod.a", "Mod A"),
                    ("  ", "mod.a", "Mod A"),
                    ((string)null, "mod.a", "Mod A")
                },
                Array.Empty<string>(),
                out var dropped);

            Assert.Equal(0, dropped);
            Assert.Single(map);
        }

        [Fact]
        public void BuildTokenMapCore_Output_IsCaseInsensitive_ForCoreLookups()
        {
            var map = FuseModAttributionMap.BuildTokenMapCore(
                new[] { ("mapenhancer", "mapenhancer", "Map Enhancer") },
                Array.Empty<string>(),
                out _);

            Assert.True(FuseModAttributionMap.TryAttributeStackCore(
                "MapEnhancer.MapEnhancer.Rebuild () (at <a>:0)",
                map, out var modId, out _, out _));
            Assert.Equal("mapenhancer", modId);
        }

        // ---- Harmony dynamic-method wrapper frames ----

        private const string ObservedWrapperLine =
            "  at (wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition" +
            ".TrainController.AddCarInternal_Patch1(TrainController,Game.Messages.Snapshot/Car," +
            "System.Collections.Generic.Dictionary`2<string, Game.Messages.IPropertyValue>,int)";

        [Fact]
        public void ParseDynamicMethodFrame_ObservedFieldLine_YieldsTypeAndMethod()
        {
            Assert.True(FuseModAttributionMap.TryParseDynamicMethodFrame(
                ObservedWrapperLine, out var typeName, out var methodName));
            Assert.Equal("TrainController", typeName);
            Assert.Equal("AddCarInternal", methodName);
        }

        [Fact]
        public void ParseDynamicMethodFrame_NamespacedType_KeepsTheFullTypePath()
        {
            var line =
                "(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition" +
                ".Game.State.StateManager.ApplyStateChange_Patch2(Game.State.StateManager,bool)";

            Assert.True(FuseModAttributionMap.TryParseDynamicMethodFrame(
                line, out var typeName, out var methodName));
            Assert.Equal("Game.State.StateManager", typeName);
            Assert.Equal("ApplyStateChange", methodName);
        }

        [Fact]
        public void ParseDynamicMethodFrame_WithoutDmdPrefix_StillParses()
        {
            Assert.True(FuseModAttributionMap.TryParseDynamicMethodFrame(
                "(wrapper dynamic-method) TrainController.AddCarInternal_Patch1(TrainController)",
                out var typeName, out var methodName));
            Assert.Equal("TrainController", typeName);
            Assert.Equal("AddCarInternal", methodName);
        }

        [Fact]
        public void ParseDynamicMethodFrame_UnderscoredMethodName_SurvivesTheSuffixCut()
        {
            Assert.True(FuseModAttributionMap.TryParseDynamicMethodFrame(
                "(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition" +
                ".CarPrototype.get_Custom_Length_Patch10(CarPrototype)",
                out var typeName, out var methodName));
            Assert.Equal("CarPrototype", typeName);
            Assert.Equal("get_Custom_Length", methodName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("MapEnhancer.MapEnhancer.Rebuild () (at <a>:0)")]                       // ordinary frame
        [InlineData("(wrapper managed-to-native) UnityEngine.Object.Internal_Clone(x)")]   // not dynamic-method
        [InlineData("(wrapper dynamic-method) Some.Type.Method(int)")]                     // no _PatchN suffix
        [InlineData("(wrapper dynamic-method) Some.Type.Method_PatchX(int)")]              // non-digit suffix
        [InlineData("(wrapper dynamic-method) Some.Type.Method_Patch(int)")]               // no digits at all
        [InlineData("(wrapper dynamic-method) NoTypeDot_Patch1(int)")]                     // no type separator
        public void ParseDynamicMethodFrame_NonHarmonyShapes_AreRejected(string line)
        {
            Assert.False(FuseModAttributionMap.TryParseDynamicMethodFrame(line, out var typeName, out var methodName));
            Assert.Null(typeName);
            Assert.Null(methodName);
        }

        [Fact]
        public void WrapperFrame_ResolvedOwner_WinsAtTheInnermostPosition()
        {
            var map = Map(("ModB", "mod.b", "Mod B"));
            var trace =
                ObservedWrapperLine + "\n" +
                "ModB.Outer.Caller () (at <b>:0)";

            Assert.True(FuseModAttributionMap.TryAttributeStackCore(
                trace, map, out var modId, out var displayName, out var frame,
                (type, method) => type == "TrainController" && method == "AddCarInternal"
                    ? ("mod.a", "Mod A")
                    : ((string, string)?)null));

            Assert.Equal("mod.a", modId);
            Assert.Equal("Mod A", displayName);
            Assert.Equal("TrainController.AddCarInternal [via Harmony patch]", frame);
        }

        [Fact]
        public void WrapperFrame_UnresolvedOwner_FallsThroughToDeeperFrames()
        {
            var map = Map(("ModB", "mod.b", "Mod B"));
            var trace =
                ObservedWrapperLine + "\n" +
                "ModB.Outer.Caller () (at <b>:0)";

            Assert.True(FuseModAttributionMap.TryAttributeStackCore(
                trace, map, out var modId, out _, out var frame, (type, method) => null));
            Assert.Equal("mod.b", modId);
            Assert.Equal("ModB.Outer.Caller", frame);
        }

        [Fact]
        public void WrapperFrame_WithoutAResolver_IsSkippedLikeAnyPlumbingLine()
        {
            var trace =
                ObservedWrapperLine + "\n" +
                "MapEnhancer.MapEnhancer.Rebuild () (at <b>:0)";

            Assert.True(FuseModAttributionMap.TryAttributeStackCore(
                trace, MapEnhancerMap, out var modId, out _, out var frame));
            Assert.Equal("mapenhancer", modId);
            Assert.Equal("MapEnhancer.MapEnhancer.Rebuild", frame);
        }

        [Fact]
        public void WrapperFrame_ThrowingResolver_DegradesToASkippedLine()
        {
            var trace =
                ObservedWrapperLine + "\n" +
                "MapEnhancer.MapEnhancer.Rebuild () (at <b>:0)";

            Assert.True(FuseModAttributionMap.TryAttributeStackCore(
                trace, MapEnhancerMap, out var modId, out _, out _,
                (type, method) => throw new InvalidOperationException("resolver fault")));
            Assert.Equal("mapenhancer", modId);
        }

        [Fact]
        public void WrapperFrames_DoNotConsumeTheFrameBudget()
        {
            // Five wrapper lines, then eleven game frames, then the mod frame:
            // the mod frame is the twelfth COUNTED frame, so it still matches.
            var trace = new StringBuilder();
            for (var i = 0; i < 5; i++)
            {
                trace.AppendLine("(wrapper managed-to-native) UnityEngine.Object.Internal_Clone(x)");
            }

            for (var i = 0; i < 11; i++)
            {
                trace.AppendLine($"Game.Events.Frame{i}.Method (System.Int32 x) (at <a>:0)");
            }

            trace.AppendLine("MapEnhancer.MapEnhancer.Rebuild () (at <b>:0)");

            Assert.True(FuseModAttributionMap.TryAttributeStackCore(
                trace.ToString(), MapEnhancerMap, out var modId, out _, out _));
            Assert.Equal("mapenhancer", modId);
        }

        [Fact]
        public void WrapperResolutions_AreCappedPerScan()
        {
            // Six resolvable wrapper lines; the resolver answers null so the
            // scan keeps going, but only the first four lines may invoke it.
            var trace = new StringBuilder();
            for (var i = 0; i < 6; i++)
            {
                trace.AppendLine(
                    $"(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition.Type{i}.Method_Patch1(int)");
            }

            var calls = 0;
            Assert.False(FuseModAttributionMap.TryAttributeStackCore(
                trace.ToString(), MapEnhancerMap, out _, out _, out _,
                (type, method) => { calls++; return null; }));
            Assert.Equal(4, calls);
        }

        [Fact]
        public void IsExactTypeMatch_RequiresTheFullTypePath()
        {
            var method = typeof(MatchTarget).GetMethod(nameof(MatchTarget.Plain));
            var nested = typeof(MatchTarget.Nested).GetMethod(nameof(MatchTarget.Nested.Inner));

            Assert.True(FuseModAttributionMap.IsExactTypeMatch(
                method, "FUSE.Tests.Infrastructure.FuseModAttributionMapTests.MatchTarget"));
            Assert.True(FuseModAttributionMap.IsExactTypeMatch(
                nested, "FUSE.Tests.Infrastructure.FuseModAttributionMapTests/MatchTarget/Nested"));

            // The loose arms of MatchesPatchedMethod are NOT exact.
            Assert.False(FuseModAttributionMap.IsExactTypeMatch(method, "MatchTarget"));
            Assert.False(FuseModAttributionMap.IsExactTypeMatch(
                method, "FuseModAttributionMapTests.MatchTarget"));
            Assert.False(FuseModAttributionMap.IsExactTypeMatch(null, "MatchTarget"));
        }

        [Fact]
        public void MergeOwnerCandidates_EmptyOrNull_YieldsNoOwner()
        {
            Assert.Null(FuseModAttributionMap.MergeOwnerCandidates(null));
            Assert.Null(FuseModAttributionMap.MergeOwnerCandidates(
                Array.Empty<(string, string)>()));
            Assert.Null(FuseModAttributionMap.MergeOwnerCandidates(
                new (string, string)[] { (null, "No Id"), ("  ", "Blank Id") }));
        }

        [Fact]
        public void MergeOwnerCandidates_AgreeingOwners_MergeToTheSingleMod()
        {
            var merged = FuseModAttributionMap.MergeOwnerCandidates(new[]
            {
                ("mod.a", "Mod A"),
                ("MOD.A", "Mod A (other patch)")   // same id, case-insensitive
            });

            Assert.NotNull(merged);
            Assert.Equal("mod.a", merged.Value.modId);
        }

        [Fact]
        public void MergeOwnerCandidates_ConflictingOwners_YieldNoOwner()
        {
            Assert.Null(FuseModAttributionMap.MergeOwnerCandidates(new[]
            {
                ("mod.a", "Mod A"),
                ("mod.b", "Mod B")
            }));
        }

        private static class MatchTarget
        {
            public static void Plain() { }

            public static class Nested
            {
                public static void Inner() { }
            }
        }

        [Fact]
        public void MatchesPatchedMethod_FullPath_TrailingSegment_AndBareName_AllMatch()
        {
            var method = typeof(MatchTarget).GetMethod(nameof(MatchTarget.Plain));
            var fullPath = "FUSE.Tests.Infrastructure.FuseModAttributionMapTests.MatchTarget";

            Assert.True(FuseModAttributionMap.MatchesPatchedMethod(method, fullPath, "Plain"));
            Assert.True(FuseModAttributionMap.MatchesPatchedMethod(method, "FuseModAttributionMapTests.MatchTarget", "Plain"));
            Assert.True(FuseModAttributionMap.MatchesPatchedMethod(method, "MatchTarget", "Plain"));
            Assert.False(FuseModAttributionMap.MatchesPatchedMethod(method, "MatchTarget", "SomethingElse"));
            Assert.False(FuseModAttributionMap.MatchesPatchedMethod(method, "SomeOtherType", "Plain"));
            Assert.False(FuseModAttributionMap.MatchesPatchedMethod(null, "MatchTarget", "Plain"));
        }

        [Fact]
        public void MatchesPatchedMethod_NestedTypeSeparators_NormalizeBothWays()
        {
            var method = typeof(MatchTarget.Nested).GetMethod(nameof(MatchTarget.Nested.Inner));

            // Runtime FullName uses '+'; Mono wrapper text may print '/'.
            Assert.True(FuseModAttributionMap.MatchesPatchedMethod(method, "MatchTarget/Nested", "Inner"));
            Assert.True(FuseModAttributionMap.MatchesPatchedMethod(method, "MatchTarget+Nested", "Inner"));
            Assert.True(FuseModAttributionMap.MatchesPatchedMethod(method, "MatchTarget.Nested", "Inner"));
        }

        [Fact]
        public void DeniedGameToken_NeverAttributes_EvenWhenAModClaimedIt()
        {
            // A mod claiming "Game" loses the token to the denylist, so a
            // stack whose inner frames are game code still attributes to the
            // mod's own namespace further out — never the other way around.
            var map = FuseModAttributionMap.BuildTokenMapCore(
                new[]
                {
                    ("Game", "greedy.mod", "Greedy Mod"),
                    ("GreedyMod", "greedy.mod", "Greedy Mod")
                },
                new[] { "Game" },
                out _);

            var trace =
                "Game.Messages.Handler.Dispatch () (at <a>:0)\n" +
                "GreedyMod.Listener.OnMessage () (at <b>:0)";

            Assert.True(FuseModAttributionMap.TryAttributeStackCore(
                trace, map, out var modId, out _, out var frame));
            Assert.Equal("greedy.mod", modId);
            Assert.Equal("GreedyMod.Listener.OnMessage", frame);
        }
    }
}
