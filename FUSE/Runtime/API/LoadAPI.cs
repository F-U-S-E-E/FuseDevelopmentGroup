using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Model.Definition.Data;
using Model;
using Model.Ops.Definition;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static class LoadAPI
    {
        private static readonly HashSet<string> PlaceholderLoadWarnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static Load GetLoad(string id)
        {
            if (FuseLoadRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (Load)cached;
            }

            var load = !string.IsNullOrWhiteSpace(id) ? CarPrototypeLibrary.instance?.LoadForId(id) : null;
            if (load != null)
            {
                FuseLoadRuntimeIndex.Instance.Set(load.id, load);
            }

            return load;
        }

        public static Load GetOrCreatePlaceholderLoad(string id, string reason)
        {
            var existing = GetLoad(id);
            if (existing != null || string.IsNullOrWhiteSpace(id))
            {
                return existing;
            }

            var definition = CreatePlaceholderDefinition(id);
            var load = AddLoad(id, definition);
            if (PlaceholderLoadWarnings.Add(id))
            {
                FuseLog.Warning(
                    $"FUSE created placeholder load '{id}' reason='{reason ?? "missing referenced load"}'. " +
                    "A converted load pack should define this id explicitly; placeholder values keep legacy progressions/industries loadable instead of dropping the reference.");
            }

            return load;
        }

        public static IEnumerable<Load> GetAllLoads()
        {
            return CarPrototypeLibrary.instance?.opsLoads?.Where(load => load != null) ?? Enumerable.Empty<Load>();
        }

        public static Load AddLoad(string id, FuseLoad definition)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Load id is required.", nameof(id));
            }

            var library = CarPrototypeLibrary.instance ?? throw new InvalidOperationException("CarPrototypeLibrary.instance is not available.");
            var existing = library.LoadForId(id);
            var load = existing ?? ScriptableObject.CreateInstance<Load>();
            if (existing == null)
            {
                load.name = id;
            }

            ApplyDefinition(load, definition);

            if (existing == null)
            {
                var currentLoads = library.opsLoads ?? Array.Empty<Load>();
                library.opsLoads = currentLoads.Concat(new[] { load }).ToArray();
            }

            FuseLoadRuntimeIndex.Instance.Set(load.id, load);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Load, id, definition);
            return load;
        }

        public static Load UpdateLoad(string id, FuseLoad definition)
        {
            return AddLoad(id, definition);
        }

        public static void RemoveLoad(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            var library = CarPrototypeLibrary.instance;
            if (library?.opsLoads != null)
            {
                library.opsLoads = library.opsLoads
                    .Where(load => load != null && !string.Equals(load.id, id, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            FuseLoadRuntimeIndex.Instance.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.Load, id);
        }

        public static FuseLoad GetLoadDefinition(string id)
        {
            return GetDefinition(GetLoad(id));
        }

        public static FuseLoad GetDefinition(Load load)
        {
            if (load == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.Load, load.id, out FuseLoad definition);
            definition = definition ?? new FuseLoad();
            definition.Name = load.description;
            definition.Units = load.units.ToString();
            definition.Density = load.density;
            definition.UnitWeightInPounds = load.unitWeightInPounds;
            definition.Importable = load.importable;
            definition.PayPerQuantity = load.payPerQuantity;
            definition.CostPerUnit = load.costPerUnit;
            return definition;
        }

        private static void ApplyDefinition(Load load, FuseLoad definition)
        {
            if (load == null)
            {
                throw new ArgumentNullException(nameof(load));
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            load.description = !string.IsNullOrWhiteSpace(definition.Name) ? definition.Name : load.id;
            load.units = ParseUnits(definition.Units);
            load.density = definition.Density ?? load.density;
            load.unitWeightInPounds = definition.UnitWeightInPounds ?? 0f;
            load.importable = definition.Importable ?? true;
            load.payPerQuantity = definition.PayPerQuantity ?? 0f;
            load.costPerUnit = definition.CostPerUnit ?? 0f;
            ApplyCustomLoadFields(load, definition.Fields);
        }

        private static void ApplyCustomLoadFields(Load load, IDictionary<string, object> fields)
        {
            if (load == null || fields == null || fields.Count == 0)
            {
                return;
            }

            foreach (var pair in fields)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                SetMemberValue(load, pair.Key, pair.Value);
            }
        }

        private static void SetMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName) || value == null)
            {
                return;
            }

            var type = instance.GetType();
            while (type != null)
            {
                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    TrySet(instance, memberName, field.FieldType, converted => field.SetValue(instance, converted), value);
                    return;
                }

                var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanWrite)
                {
                    TrySet(instance, memberName, property.PropertyType, converted => property.SetValue(instance, converted, null), value);
                    return;
                }

                type = type.BaseType;
            }
        }

        private static void TrySet(object instance, string memberName, Type memberType, Action<object> setter, object value)
        {
            try
            {
                var converted = ConvertLoadFieldValue(memberType, value);
                if (converted != null || !memberType.IsValueType || Nullable.GetUnderlyingType(memberType) != null)
                {
                    setter(converted);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not set custom load field '{memberName}' " +
                    $"type='{instance.GetType().FullName}' error='{ex.Message}'.");
            }
        }

        private static object ConvertLoadFieldValue(Type targetType, object value)
        {
            if (targetType == null || value == null)
            {
                return null;
            }

            var nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType != null)
            {
                targetType = nullableType;
            }

            if (value is JValue jValue)
            {
                value = jValue.Value;
            }

            if (value is JToken token)
            {
                return token.ToObject(targetType);
            }

            if (targetType.IsInstanceOfType(value))
            {
                return value;
            }

            if (targetType.IsEnum)
            {
                return value is string text
                    ? Enum.Parse(targetType, text, true)
                    : Enum.ToObject(targetType, value);
            }

            return Convert.ChangeType(value, targetType);
        }

        private static FuseLoad CreatePlaceholderDefinition(string id)
        {
            var normalized = id?.Trim() ?? string.Empty;
            if (string.Equals(normalized, "machine-parts", StringComparison.OrdinalIgnoreCase))
            {
                return new FuseLoad
                {
                    Name = "Machine Parts",
                    Units = nameof(LoadUnits.Pounds),
                    Density = 42.5f,
                    UnitWeightInPounds = 0f,
                    Importable = true,
                    PayPerQuantity = 0f,
                    CostPerUnit = 0f
                };
            }

            if (string.Equals(normalized, "mining-explosives", StringComparison.OrdinalIgnoreCase))
            {
                return new FuseLoad
                {
                    Name = "Mining Explosives",
                    Units = nameof(LoadUnits.Pounds),
                    Density = 37.5f,
                    UnitWeightInPounds = 0f,
                    Importable = true,
                    PayPerQuantity = 0f,
                    CostPerUnit = 0f
                };
            }

            return new FuseLoad
            {
                Name = HumanizeLoadId(normalized),
                Units = nameof(LoadUnits.Pounds),
                Density = 50f,
                UnitWeightInPounds = 0f,
                Importable = true,
                PayPerQuantity = 0f,
                CostPerUnit = 0f
            };
        }

        private static string HumanizeLoadId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return "Unknown Load";
            }

            return string.Join(" ", id.Split(new[] { '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Length == 0 ? token : char.ToUpperInvariant(token[0]) + token.Substring(1)));
        }

        private static LoadUnits ParseUnits(string units)
        {
            if (string.IsNullOrWhiteSpace(units))
            {
                return LoadUnits.Pounds;
            }

            if (string.Equals(units, nameof(LoadUnits.Gallons), StringComparison.OrdinalIgnoreCase))
            {
                return LoadUnits.Gallons;
            }

            if (string.Equals(units, nameof(LoadUnits.Quantity), StringComparison.OrdinalIgnoreCase))
            {
                return LoadUnits.Quantity;
            }

            return LoadUnits.Pounds;
        }
    }
}
