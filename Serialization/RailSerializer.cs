using System;
using System.IO;
using RAIL.Data;
using RAIL.Migrations;
using RAIL.Serialization.Converters;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Serialization;

namespace RAIL.Serialization
{
    public static class RailSerializer
    {
        public static RailModDefinition Load(string path)
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
                    throw new ArgumentException($"Unknown RAIL definition format: {extension}", nameof(path));
            }
        }

        public static void SaveJson(RailModDefinition definition, string path)
        {
            File.WriteAllText(path, ToJson(definition));
        }

        public static void SaveBson(RailModDefinition definition, string path)
        {
            File.WriteAllBytes(path, ToBson(definition));
        }

        public static string ToJson(RailModDefinition definition)
        {
            return JsonConvert.SerializeObject(PrepareForWrite(definition), Formatting.Indented, GetSettings());
        }

        public static RailModDefinition FromJson(string json)
        {
            var definition = JsonConvert.DeserializeObject<RailModDefinition>(json, GetSettings());
            return RailMigration.Migrate(definition);
        }

        public static byte[] ToBson(RailModDefinition definition)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BsonWriter(stream))
            {
                GetSerializer().Serialize(writer, PrepareForWrite(definition));
                return stream.ToArray();
            }
        }

        public static RailModDefinition FromBson(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            using (var reader = new BsonReader(stream))
            {
                var definition = GetSerializer().Deserialize<RailModDefinition>(reader);
                return RailMigration.Migrate(definition);
            }
        }

        public static JsonSerializerSettings GetSettings()
        {
            return new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
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

        private static RailModDefinition PrepareForWrite(RailModDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            RailMigration.Normalize(definition);
            return definition;
        }
    }
}
