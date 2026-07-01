using System;
using System.IO;
using FUSE.Authoring.Data;
using FUSE.Authoring.Migrations;
using FUSE.Authoring.Serialization.Converters;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace FUSE.Authoring.Serialization
{
    public static class FuseSerializer
    {
        public static FuseModDefinition Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path is required.", nameof(path));
            }

            var extension = Path.GetExtension(path).ToLowerInvariant();
            switch (extension)
            {
                case ".bson":
                    return FromBson(File.ReadAllBytes(path));
                case ".json":
                    return FromJson(File.ReadAllText(path));
                default:
                    throw new ArgumentException($"Unknown FUSE definition format: {extension}", nameof(path));
            }
        }

        public static void SaveJson(FuseModDefinition definition, string path)
        {
            File.WriteAllText(path, ToJson(definition));
        }

        public static void SaveBson(FuseModDefinition definition, string path)
        {
            File.WriteAllBytes(path, ToBson(definition));
        }

        public static string ToJson(FuseModDefinition definition)
        {
            return JsonConvert.SerializeObject(PrepareForWrite(definition), Formatting.Indented, GetSettings());
        }

        public static FuseModDefinition FromJson(string json)
        {
            ValidateNoDuplicateProperties(json);
            var definition = JsonConvert.DeserializeObject<FuseModDefinition>(json, GetSettings());
            return FuseMigration.Migrate(definition);
        }

        public static byte[] ToBson(FuseModDefinition definition)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BsonWriter(stream))
            {
                GetSerializer().Serialize(writer, PrepareForWrite(definition));
                return stream.ToArray();
            }
        }

        public static FuseModDefinition FromBson(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            using (var reader = new BsonReader(stream))
            {
                var definition = GetSerializer().Deserialize<FuseModDefinition>(reader);
                return FuseMigration.Migrate(definition);
            }
        }

        public static JsonSerializerSettings GetSettings()
        {
            return new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePreserveDictionaryKeysResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Include,
                Formatting = Formatting.Indented,
                Converters =
                {
                    new Vector3JsonConverter(),
                    new FlexibleStringArrayJsonConverter()
                }
            };
        }

        public static JsonSerializer GetSerializer()
        {
            return JsonSerializer.Create(GetSettings());
        }

        private static void ValidateNoDuplicateProperties(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json ?? string.Empty)))
            {
                JObject.Load(reader, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
            }
        }

        private static FuseModDefinition PrepareForWrite(FuseModDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            FuseMigration.Normalize(definition);
            return definition;
        }
    }
}
