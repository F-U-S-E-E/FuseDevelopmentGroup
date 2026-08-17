using System.Linq;
using FUSE.Runtime.API;
using HarmonyLib;
using Model.Definition;
using Model.Definition.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Runtime
{
    public class FuseWhistleDefinitionStoreTests
    {
        [Fact]
        public void BuildDefinitionsJson_EmitsGameSchemaObjects()
        {
            var json = FuseWhistleDefinitionStore.BuildDefinitionsJson(new[]
            {
                new FuseWhistleStoreEntry(
                    "C&O 6 Chime - Modified",
                    "C&O 6 Chime - Modified",
                    "audio.whistles01",
                    "6ChimeA")
            });

            var root = JObject.Parse(json);
            var objects = (JArray)root["objects"];
            var entry = (JObject)Assert.Single(objects);

            Assert.Equal("C&O 6 Chime - Modified", (string)entry["identifier"]);
            Assert.Equal("C&O 6 Chime - Modified", (string)entry["metadata"]["name"]);
            Assert.Equal("Whistle", (string)entry["definition"]["kind"]);
            Assert.Equal("audio.whistles01", (string)entry["definition"]["model"]["assetPackIdentifier"]);
            Assert.Equal("6ChimeA", (string)entry["definition"]["model"]["assetIdentifier"]);
        }

        [Fact]
        public void BuildDefinitionsJson_KeepsAudioReferenceEmpty()
        {
            // Vanilla's WhistleController.Configure skips its async audio
            // branch for empty references; the clip must stay FUSE-served.
            var json = FuseWhistleDefinitionStore.BuildDefinitionsJson(new[]
            {
                new FuseWhistleStoreEntry("id", "name", "audio.whistles01", "3ChimeA")
            });

            var audio = JObject.Parse(json)["objects"][0]["definition"]["audio"];
            Assert.Equal(string.Empty, (string)audio["assetPackIdentifier"]);
            Assert.Equal(string.Empty, (string)audio["assetIdentifier"]);
        }

        [Fact]
        public void BuildDefinitionsJson_NullModel_EmitsEmptyReference()
        {
            var json = FuseWhistleDefinitionStore.BuildDefinitionsJson(new[]
            {
                new FuseWhistleStoreEntry("no-model", "No Model", null, null)
            });

            var model = JObject.Parse(json)["objects"][0]["definition"]["model"];
            Assert.Equal(string.Empty, (string)model["assetPackIdentifier"]);
            Assert.Equal(string.Empty, (string)model["assetIdentifier"]);
        }

        [Fact]
        public void BuildDefinitionsJson_SkipsBlankIds_AndFallsBackToIdForBlankNames()
        {
            var json = FuseWhistleDefinitionStore.BuildDefinitionsJson(new[]
            {
                new FuseWhistleStoreEntry(" ", "blank id is dropped", "p", "a"),
                new FuseWhistleStoreEntry("kept", null, "p", "a")
            });

            var objects = (JArray)JObject.Parse(json)["objects"];
            var entry = (JObject)Assert.Single(objects);
            Assert.Equal("kept", (string)entry["identifier"]);
            Assert.Equal("kept", (string)entry["metadata"]["name"]);
        }

        [Fact]
        public void BuildDefinitionsJson_NullOrEmptyInput_EmitsEmptyObjectsArray()
        {
            foreach (var json in new[]
                     {
                         FuseWhistleDefinitionStore.BuildDefinitionsJson(null),
                         FuseWhistleDefinitionStore.BuildDefinitionsJson(Enumerable.Empty<FuseWhistleStoreEntry>())
                     })
            {
                var root = JObject.Parse(json);
                Assert.Empty((JArray)root["objects"]);
            }
        }

        [Fact]
        public void BuildDefinitionsJson_RoundTripsThroughGameContainerSerialization()
        {
            // The direct-store loader deserializes the generated file with the
            // game's own serializer settings (see FuseAssetPackRegistry's
            // BypassDeserialize). Prove the emitted schema binds: the "kind"
            // discriminator must resolve to WhistleDefinition, the model
            // reference must survive, and the audio reference must stay empty
            // so vanilla's Configure never races FUSE on the clip.
            var json = FuseWhistleDefinitionStore.BuildDefinitionsJson(new[]
            {
                new FuseWhistleStoreEntry(
                    "MTH N&W J Whistle (PS3)",
                    "MTH N&W J Whistle (PS3)",
                    "audio.whistles01",
                    "3ChimeD")
            });

            var settingsMethod = AccessTools.Method(typeof(ContainerSerialization), "JsonSerializerSettings");
            Assert.NotNull(settingsMethod);
            var settings = (JsonSerializerSettings)settingsMethod.Invoke(null, null);
            var container = JsonConvert.DeserializeObject<Container>(json, settings);

            var item = Assert.Single(container.Objects);
            Assert.Equal("MTH N&W J Whistle (PS3)", item.Identifier);
            Assert.Equal("MTH N&W J Whistle (PS3)", item.Metadata.Name);
            var whistle = Assert.IsType<WhistleDefinition>(item.Definition);
            Assert.Equal("audio.whistles01", whistle.Model.AssetPackIdentifier);
            Assert.Equal("3ChimeD", whistle.Model.AssetIdentifier);
            Assert.True(whistle.Audio.IsEmpty);
        }

        [Fact]
        public void BuildCatalogJson_DeclaresNoAssets()
        {
            var root = JObject.Parse(FuseWhistleDefinitionStore.BuildCatalogJson());

            Assert.Equal("fuse.generated.whistles", (string)root["identifier"]);
            Assert.False((bool)root["shared"]);
            Assert.Empty((JObject)root["assets"]);
        }
    }
}
