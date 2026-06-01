using System;
using System.IO;
using FUSE.Authoring.Serialization;
using Newtonsoft.Json.Linq;
using Xunit;
using CoreSerializer = Fuse.Core.Serialization.FuseCoreSerializer;

namespace FUSE.Tests.Serialization
{
    /// <summary>
    /// Cross-serializer contract: the Unity-free <c>Fuse.Core.Serialization.FuseCoreSerializer</c>
    /// (used by FUSE.ExternalEditor on .NET 10) must produce output the shipping
    /// Unity <see cref="FuseSerializer"/> reads identically, and round-tripping
    /// FUSE.Core output through the shipping serializer must be stable. This is
    /// the byte-compatibility gate for <c>*.fuse.json</c> authored by the
    /// external editor.
    ///
    /// <para>Runs on the net48 CI lane that provides the game reference
    /// assemblies — the shipping serializer pulls in <c>UnityEngine.Vector3</c>,
    /// so this cannot execute on a machine without the Railroader/UMM refs.</para>
    /// </summary>
    public class FuseCoreSerializerContractTests
    {
        private static string ExamplePath =>
            Path.Combine(AppContext.BaseDirectory, "fuse-mod.example.json");

        private static string ReadExample() => File.ReadAllText(ExamplePath);

        [Fact]
        public void Core_And_Shipping_Produce_Equivalent_Json()
        {
            var text = ReadExample();

            // Each serializer parses the text into its OWN model type and re-emits;
            // we compare the JSON, never the C# objects, so the two parallel models
            // never need to share types.
            var shippingJson = FuseSerializer.ToJson(FuseSerializer.FromJson(text));
            var coreJson = CoreSerializer.ToJson(CoreSerializer.FromJson(text));

            Assert.True(
                JToken.DeepEquals(JObject.Parse(shippingJson), JObject.Parse(coreJson)),
                "FUSE.Core serializer output must be semantically identical to the shipping FuseSerializer.");
        }

        [Fact]
        public void Editor_Output_RoundTrips_Through_Shipping_Serializer()
        {
            var text = ReadExample();

            // Simulate: editor authors via FUSE.Core, game loads via the shipping serializer.
            var editorJson = CoreSerializer.ToJson(CoreSerializer.FromJson(text));

            var shippingReEmit = FuseSerializer.ToJson(FuseSerializer.FromJson(editorJson));
            var coreReEmit = CoreSerializer.ToJson(CoreSerializer.FromJson(editorJson));

            Assert.True(
                JToken.DeepEquals(JObject.Parse(shippingReEmit), JObject.Parse(coreReEmit)),
                "Round-tripping FUSE.Core output through the shipping serializer must be stable.");
        }
    }
}
