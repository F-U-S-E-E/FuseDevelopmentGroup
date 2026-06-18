using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Progression;
using Game.State;
using KeyValue.Runtime;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Loading;
using Track;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static partial class ProgressionAPI
    {

        private static string[] ToSectionIds(IEnumerable<Section> sections)
        {
            return sections?.Where(section => section != null && !string.IsNullOrWhiteSpace(section.identifier))
                .Select(section => section.identifier)
                .ToArray();
        }

        private static string[] ToFeatureIds(IEnumerable<MapFeature> features)
        {
            return features?.Where(feature => feature != null && !string.IsNullOrWhiteSpace(feature.identifier))
                .Select(feature => feature.identifier)
                .ToArray();
        }

        private static string[] ToAreaIds(IEnumerable<Area> areas)
        {
            return areas?.Where(area => area != null && !string.IsNullOrWhiteSpace(area.identifier))
                .Select(area => area.identifier)
                .ToArray();
        }

        private static string[] ToIndustryIds(IEnumerable<Industry> industries)
        {
            return industries?.Where(industry => industry != null && !string.IsNullOrWhiteSpace(industry.identifier))
                .Select(industry => industry.identifier)
                .ToArray();
        }

        private static string[] ToIndustryComponentIds(IEnumerable<IndustryComponent> components)
        {
            return components?.Where(component => component != null)
                .Select(SafeIndustryComponentId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();
        }

        private static string[] ToGameObjectPaths(IEnumerable<GameObject> gameObjects)
        {
            return gameObjects?.Where(gameObject => gameObject != null)
                .Select(gameObject => GetScenePath(gameObject.transform))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
        }

        private static Dictionary<string, string> ToInterchangeTransfers(IEnumerable<InterchangeTransfer> transfers)
        {
            if (transfers == null)
            {
                return null;
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var transfer in transfers.Where(transfer => transfer != null))
            {
                var from = InterchangeTransferFromField?.GetValue(transfer) as Interchange;
                if (from == null)
                {
                    continue;
                }

                var fromId = SafeIndustryComponentId(from);
                if (string.IsNullOrWhiteSpace(fromId))
                {
                    continue;
                }

                var to = InterchangeTransferToField?.GetValue(transfer) as Interchange;
                result[fromId] = to != null ? SafeIndustryComponentId(to) : null;
            }

            return result.Count > 0 ? result : null;
        }

        private static FuseDeliveryPhase[] ToDeliveryPhases(IEnumerable<Section.DeliveryPhase> phases)
        {
            return phases?.Where(phase => phase != null)
                .Select(phase => new FuseDeliveryPhase
                {
                    Cost = phase.cost,
                    IndustryComponentId = phase.industryComponent != null ? phase.industryComponent.Identifier : null,
                    Deliveries = ToDeliveries(phase.deliveries)
                })
                .ToArray();
        }

        private static FuseDelivery[] ToDeliveries(IEnumerable<Section.Delivery> deliveries)
        {
            return deliveries?.Where(delivery => delivery != null)
                .Select(delivery => new FuseDelivery
                {
                    CarTypeFilter = delivery.carTypeFilter.ToString(),
                    LoadId = delivery.load != null ? delivery.load.id : null,
                    Count = delivery.count,
                    Direction = delivery.direction == Section.Delivery.Direction.LoadFromIndustry ? "loadFromIndustry" : "loadToIndustry"
                })
                .ToArray();
        }

        private static string[] PreferExplicit(string[] explicitValues, string[] fallbackValues)
        {
            return HasAny(explicitValues) ? explicitValues : (fallbackValues ?? Array.Empty<string>());
        }

        private static bool HasAny(string[] values)
        {
            return values != null && values.Any(value => !string.IsNullOrWhiteSpace(value));
        }

        private static string SafeIndustryComponentId(IndustryComponent component)
        {
            if (component == null)
            {
                return null;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(component.Identifier))
                {
                    return component.Identifier;
                }
            }
            catch
            {
                // Incomplete cloned components can throw while their parent industry identity is being rebuilt.
            }

            var industry = component.GetComponentInParent<Industry>(true);
            return industry != null &&
                   !string.IsNullOrWhiteSpace(industry.identifier) &&
                   !string.IsNullOrWhiteSpace(component.subIdentifier)
                ? industry.identifier + "." + component.subIdentifier
                : null;
        }

        private static string GetScenePath(Transform transform)
        {
            if (transform == null)
            {
                return null;
            }

            var names = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }
    }
}
