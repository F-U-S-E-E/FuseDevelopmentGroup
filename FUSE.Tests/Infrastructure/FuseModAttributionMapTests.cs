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
