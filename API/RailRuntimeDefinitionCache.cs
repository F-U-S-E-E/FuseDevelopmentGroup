using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RAIL.Infrastructure;
using RAIL.Serialization;

namespace RAIL.API
{
    public static class RailRuntimeDefinitionCache
    {
        private static readonly Dictionary<string, object> Definitions =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public static void Store<T>(string kind, string id, T definition)
            where T : class
        {
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(id) || definition == null)
            {
                return;
            }

            Definitions[MakeKey(kind, id)] = Clone(definition);
        }

        public static bool TryGet<T>(string kind, string id, out T definition)
            where T : class
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            if (!Definitions.TryGetValue(MakeKey(kind, id), out var stored) || stored == null)
            {
                return false;
            }

            if (stored is T typed)
            {
                definition = Clone(typed);
                return definition != null;
            }

            return false;
        }

        public static void Remove(string kind, string id)
        {
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            Definitions.Remove(MakeKey(kind, id));
        }

        private static string MakeKey(string kind, string id)
        {
            return kind.Trim() + "\n" + id.Trim();
        }

        private static T Clone<T>(T definition)
            where T : class
        {
            if (definition == null)
            {
                return null;
            }

            try
            {
                var serializer = RailSerializer.GetSerializer();
                return JToken.FromObject(definition, serializer).ToObject<T>(serializer);
            }
            catch (Exception ex)
            {
                RailLog.Warning($"RAIL failed to clone runtime definition '{typeof(T).FullName}': {ex.Message}");
                return definition;
            }
        }
    }
}
