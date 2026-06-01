using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Loading;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Loading
{
    /// <summary>
    /// Covers <see cref="FuseAssetPackRegistry.FilterUnbindableComponents"/>, the resilient
    /// retry filter that replaced the old static allow-list strip. The allow-list silently
    /// deleted runtime-registered customization components (MaterialColorizerComponent,
    /// DefaultLivelryComponent, ComponentGroup, ...), emptying the in-game Customize menu for
    /// ~60 modded cars. These tests pin the new contract: only components that genuinely fail
    /// to bind are dropped, and never their valid siblings or the whole pack. The bind check is
    /// injected so the test stays hermetic (no game / library DLLs needed in CI).
    /// </summary>
    public class FuseAssetPackSanitizerTests
    {
        // Builds a Definitions.json-shaped document; each entry is one object's component-kind
        // list. A null kind produces a component with no "kind" property.
        private static string Definitions(params string[][] objectComponentKinds)
        {
            var objects = new JArray();
            var i = 0;
            foreach (var kinds in objectComponentKinds)
            {
                var components = new JArray(kinds.Select(k =>
                    k == null ? new JObject() : new JObject { ["kind"] = k, ["name"] = k + "_inst" }));
                objects.Add(new JObject
                {
                    ["identifier"] = "obj-" + i++,
                    ["definition"] = new JObject { ["kind"] = "CarDefinition", ["components"] = components },
                });
            }

            return new JObject { ["objects"] = objects }.ToString();
        }

        private static string[] ComponentKinds(string json, int objectIndex = 0)
        {
            var objects = (JArray)JObject.Parse(json)["objects"];
            var components = (JArray)objects[objectIndex]["definition"]["components"];
            return components.Select(c => (string)c["kind"]).ToArray();
        }

        // Simulates a missing defining library: the named kinds cannot bind, everything else can.
        private static Func<JObject, bool> RejectKinds(params string[] unbindable)
        {
            var set = new HashSet<string>(unbindable, StringComparer.Ordinal);
            return comp => comp["kind"] != null && !set.Contains((string)comp["kind"]);
        }

        [Fact]
        public void AllBindable_ReturnsInputUnchanged_NoDrops()
        {
            var input = Definitions(new[] { "Bell", "MaterialColorizerComponent", "DefaultLivelryComponent" });
            var dropped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var result = FuseAssetPackRegistry.FilterUnbindableComponents(input, dropped, RejectKinds());

            Assert.Same(input, result); // same reference == nothing rewritten
            Assert.Empty(dropped);
        }

        [Fact]
        public void UnbindableKind_Dropped_ValidSiblingsRetained()
        {
            var input = Definitions(new[] { "Bell", "BogusKind", "MaterialColorizerComponent" });
            var dropped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var result = FuseAssetPackRegistry.FilterUnbindableComponents(input, dropped, RejectKinds("BogusKind"));

            Assert.Equal(new[] { "Bell", "MaterialColorizerComponent" }, ComponentKinds(result));
            Assert.Single(dropped);
            Assert.Equal(1, dropped["BogusKind"]);
        }

        [Fact]
        public void PerObjectIsolation_BadComponentInOneObject_DoesNotAffectOthers()
        {
            var input = Definitions(
                new[] { "Bell", "Headlight" },     // object 0 — all bindable
                new[] { "BogusKind", "Whistle" }); // object 1 — one unbindable
            var dropped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var result = FuseAssetPackRegistry.FilterUnbindableComponents(input, dropped, RejectKinds("BogusKind"));

            Assert.Equal(new[] { "Bell", "Headlight" }, ComponentKinds(result, 0));
            Assert.Equal(new[] { "Whistle" }, ComponentKinds(result, 1));
            Assert.Single(dropped);
            Assert.Equal(1, dropped["BogusKind"]);
        }

        [Fact]
        public void MultipleUnbindable_CountedByKind()
        {
            var input = Definitions(
                new[] { "BogusKind", "Bell" },
                new[] { "BogusKind", "OtherBogus" });
            var dropped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            FuseAssetPackRegistry.FilterUnbindableComponents(input, dropped, RejectKinds("BogusKind", "OtherBogus"));

            Assert.Equal(2, dropped["BogusKind"]);
            Assert.Equal(1, dropped["OtherBogus"]);
        }

        [Fact]
        public void ComponentWithoutKind_DroppedAsMissing()
        {
            var input = Definitions(new[] { (string)null, "Bell" });
            var dropped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var result = FuseAssetPackRegistry.FilterUnbindableComponents(input, dropped, RejectKinds());

            Assert.Equal(new[] { "Bell" }, ComponentKinds(result));
            Assert.Equal(1, dropped["<missing>"]);
        }

        [Fact]
        public void NoObjectsArray_ReturnsInputUnchanged()
        {
            var input = "{\"notObjects\":1}";
            var dropped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var result = FuseAssetPackRegistry.FilterUnbindableComponents(input, dropped, RejectKinds("anything"));

            Assert.Same(input, result);
            Assert.Empty(dropped);
        }

        [Fact]
        public void ObjectWithoutComponents_Ignored()
        {
            var input = "{\"objects\":[{\"identifier\":\"a\",\"definition\":{\"kind\":\"CarDefinition\"}}]}";
            var dropped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var result = FuseAssetPackRegistry.FilterUnbindableComponents(input, dropped, RejectKinds("anything"));

            Assert.Same(input, result);
            Assert.Empty(dropped);
        }
    }
}
