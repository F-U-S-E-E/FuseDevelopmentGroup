using System;
using System.Collections.Generic;
using System.Linq;
using Model.Definition.Data;
using Model;
using Model.Ops.Definition;
using RAIL.Cache;
using RAIL.Data;
using UnityEngine;

namespace RAIL.API
{
    public static class LoadAPI
    {
        public static Load GetLoad(string id)
        {
            if (RailLoadRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (Load)cached;
            }

            var load = !string.IsNullOrWhiteSpace(id) ? CarPrototypeLibrary.instance?.LoadForId(id) : null;
            if (load != null)
            {
                RailLoadRuntimeIndex.Instance.Set(load.id, load);
            }

            return load;
        }

        public static IEnumerable<Load> GetAllLoads()
        {
            return CarPrototypeLibrary.instance?.opsLoads?.Where(load => load != null) ?? Enumerable.Empty<Load>();
        }

        public static Load AddLoad(string id, RailLoad definition)
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

            RailLoadRuntimeIndex.Instance.Set(load.id, load);
            RailApiPersistence.RecordDefinition(RailDefinitionKind.Load, id, definition);
            return load;
        }

        public static Load UpdateLoad(string id, RailLoad definition)
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

            RailLoadRuntimeIndex.Instance.Remove(id);
            RailRuntimeDefinitionCache.Remove(RailDefinitionKind.Load, id);
        }

        public static RailLoad GetLoadDefinition(string id)
        {
            return GetDefinition(GetLoad(id));
        }

        public static RailLoad GetDefinition(Load load)
        {
            if (load == null)
            {
                return null;
            }

            RailRuntimeDefinitionCache.TryGet(RailDefinitionKind.Load, load.id, out RailLoad definition);
            definition = definition ?? new RailLoad();
            definition.Name = load.description;
            definition.Units = load.units.ToString();
            definition.Density = load.density;
            definition.UnitWeightInPounds = load.unitWeightInPounds;
            definition.Importable = load.importable;
            definition.PayPerQuantity = load.payPerQuantity;
            definition.CostPerUnit = load.costPerUnit;
            return definition;
        }

        private static void ApplyDefinition(Load load, RailLoad definition)
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
