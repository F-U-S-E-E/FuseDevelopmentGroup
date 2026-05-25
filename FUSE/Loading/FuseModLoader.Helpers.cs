using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Model.Ops;
using FUSE.Runtime.API;
using FUSE.Authoring.Entities;
using FUSE.Authoring.Data;
using FUSE.Runtime.Events;
using FUSE.Infrastructure;
using FUSE.Runtime.Registry;
using FUSE.Authoring.Serialization;
using FUSE.Authoring.Validation;
using Newtonsoft.Json.Linq;
using Map.Runtime;
using Track;

namespace FUSE.Loading
{
    public static partial class FuseModLoader
    {

        private static HashSet<string> CollectLoadedNodeIds(FuseModDefinition current)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddKeys(result, current?.Tracks?.Nodes);
            foreach (var loaded in LoadedMods.Values)
            {
                AddKeys(result, loaded?.Definition?.Tracks?.Nodes);
            }

            return result;
        }

        private static HashSet<string> CollectLoadedSegmentIds(FuseModDefinition current)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddKeys(result, current?.Tracks?.Segments);
            foreach (var loaded in LoadedMods.Values)
            {
                AddKeys(result, loaded?.Definition?.Tracks?.Segments);
            }

            return result;
        }

        private static HashSet<string> CollectLoadedSpanIds(FuseModDefinition current)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddKeys(result, current?.Tracks?.Spans);
            foreach (var loaded in LoadedMods.Values)
            {
                AddKeys(result, loaded?.Definition?.Tracks?.Spans);
            }

            return result;
        }

        private static HashSet<string> CollectLoadedLoadIds(FuseModDefinition current)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddKeys(result, current?.Operations?.Loads);
            foreach (var loaded in LoadedMods.Values)
            {
                AddKeys(result, loaded?.Definition?.Operations?.Loads);
            }

            return result;
        }

        private static HashSet<string> CollectLoadedGeneratedNodeIds(FuseModDefinition current)
        {
            var result = CollectGeneratedNodeIds(current);
            foreach (var loaded in LoadedMods.Values)
            {
                result.UnionWith(CollectGeneratedNodeIds(loaded?.Definition));
            }

            return result;
        }

        private static HashSet<string> CollectLoadedGeneratedSegmentIds(FuseModDefinition current)
        {
            var result = CollectGeneratedSegmentIds(current);
            foreach (var loaded in LoadedMods.Values)
            {
                result.UnionWith(CollectGeneratedSegmentIds(loaded?.Definition));
            }

            return result;
        }

        private static void AddKeys<TValue>(HashSet<string> sink, IDictionary<string, TValue> dictionary)
        {
            if (sink == null || dictionary == null)
            {
                return;
            }

            foreach (var key in dictionary.Keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    sink.Add(key);
                }
            }
        }

        private static bool HasPassengerStop(FuseModDefinition definition, string passengerStopId)
        {
            foreach (var industry in definition.Operations?.Industries ?? new Dictionary<string, FuseIndustry>())
            {
                foreach (var component in industry.Value?.Components ?? new Dictionary<string, FuseIndustryComponent>())
                {
                    if (string.Equals(component.Value?.PassengerStopId, passengerStopId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(component.Key, passengerStopId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return IndustryAPI.GetIndustry(passengerStopId) != null;
        }

        private static HashSet<string> CollectGeneratedSegmentIds(FuseModDefinition definition)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var turntable in definition.Operations?.Turntables ?? new Dictionary<string, FuseTurntable>())
            {
                var roundhouse = turntable.Value?.Roundhouse;
                if (roundhouse == null || roundhouse.Stalls <= 0)
                {
                    continue;
                }

                for (var index = 1; index <= roundhouse.Stalls; index++)
                {
                    result.Add(FuseTurntableIds.GetRoundhouseSegmentId(turntable.Key, index, turntable.Value));
                }
            }

            return result;
        }

        private static HashSet<string> CollectGeneratedNodeIds(FuseModDefinition definition)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var turntable in definition.Operations?.Turntables ?? new Dictionary<string, FuseTurntable>())
            {
                var value = turntable.Value;
                if (value == null)
                {
                    continue;
                }

                // Pit nodes are created for every subdivision of the turntable.
                var subdivisions = value.Subdivisions > 0 ? value.Subdivisions : 16;
                for (var index = 0; index < subdivisions; index++)
                {
                    result.Add(FuseTurntableIds.GetPitNodeId(turntable.Key, index, value));
                }

                // Roundhouse stalls add their own roundhouse nodes (1-based).
                var roundhouse = value.Roundhouse;
                if (roundhouse != null && roundhouse.Stalls > 0)
                {
                    for (var index = 1; index <= roundhouse.Stalls; index++)
                    {
                        result.Add(FuseTurntableIds.GetRoundhouseNodeId(turntable.Key, index, value));
                    }
                }
            }

            return result;
        }
    }
}
