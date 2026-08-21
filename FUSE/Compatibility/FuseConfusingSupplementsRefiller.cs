using System;
using System.Collections.Generic;
using FUSE.Infrastructure;
using Game.State;
using Model;
using Model.Definition;
using Model.Definition.Data;
using Model.Ops;
using Model.Physics;
using Railloader.Extensions;
using UnityEngine;

namespace FUSE.Compatibility
{
    internal sealed class FuseConfusingSupplementsRefillerComponent : Model.Definition.Component
    {
        internal const string ComponentKind = "ConfusingSupplements.Refiller";

        public override string Kind => ComponentKind;

        public int TransferRate { get; set; } = 36000;
    }

    internal sealed class FuseConfusingSupplementsRefillerBuilder : ComponentBuilder<FuseConfusingSupplementsRefillerComponent>
    {
        protected override void Build(
            ComponentBuilderContext context,
            FuseConfusingSupplementsRefillerComponent component)
        {
            var car = context.GameObject?.GetComponentInParent<Car>();
            if (car == null)
            {
                FuseLog.Warning(
                    $"FUSE could not attach a legacy refiller to '{context.ObjectName ?? "<unknown car>"}' " +
                    "because its car object was unavailable.");
                return;
            }

            var runtime = car.GetComponent<FuseConfusingSupplementsRefillerRuntime>() ??
                          car.gameObject.AddComponent<FuseConfusingSupplementsRefillerRuntime>();
            runtime.TransferPerSecond = Mathf.Max(0f, (component?.TransferRate ?? 0) / 3600f);
        }
    }

    internal static class FuseConfusingSupplementsRefillerPolicy
    {
        internal static bool CanTargetReceiveFromSource(
            IEnumerable<string> sourceLoadIdentifiers,
            IEnumerable<string> targetLoadIdentifiers)
        {
            if (sourceLoadIdentifiers == null || targetLoadIdentifiers == null)
            {
                return false;
            }

            foreach (var targetIdentifier in targetLoadIdentifiers)
            {
                if (string.IsNullOrWhiteSpace(targetIdentifier))
                {
                    continue;
                }

                foreach (var sourceIdentifier in sourceLoadIdentifiers)
                {
                    if (string.Equals(
                            sourceIdentifier,
                            targetIdentifier,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static float Take(
            ref float remainingTransfer,
            float availableCapacity,
            float availableSource)
        {
            var quantity = Math.Min(
                Math.Max(0f, remainingTransfer),
                Math.Min(Math.Max(0f, availableCapacity), Math.Max(0f, availableSource)));
            remainingTransfer -= quantity;
            return quantity;
        }
    }

    internal sealed class FuseConfusingSupplementsRefillerRuntime : MonoBehaviour
    {
        internal float TransferPerSecond { get; set; }

        private Car _source;
        private float _nextUpdate;

        private void Awake()
        {
            _source = GetComponent<Car>();
        }

        private void Update()
        {
            if (!StateManager.IsHost || _source == null || Time.time < _nextUpdate)
            {
                return;
            }

            _nextUpdate = Time.time + 1f;
            try
            {
                var target = FindNearestCompatibleCar(_source, Car.LogicalEnd.A) ??
                             FindNearestCompatibleCar(_source, Car.LogicalEnd.B);
                if (target != null)
                {
                    TransferLoads(_source, target, TransferPerSecond);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE contained a rolling-stock refiller error on '{_source.DisplayName ?? _source.id}': " +
                    ex.GetBaseException().Message);
            }
        }

        private static bool CanTargetReceiveFromSource(CarDefinition source, CarDefinition target)
        {
            if (source?.LoadSlots == null || target?.LoadSlots == null)
            {
                return false;
            }

            for (var targetIndex = 0; targetIndex < target.LoadSlots.Count; targetIndex++)
            {
                var targetIdentifier = target.LoadSlots[targetIndex]?.RequiredLoadIdentifier;
                if (FindLoadSlot(source, targetIdentifier) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindLoadSlot(CarDefinition definition, string requiredLoadIdentifier)
        {
            if (definition?.LoadSlots == null || string.IsNullOrWhiteSpace(requiredLoadIdentifier))
            {
                return -1;
            }

            for (var index = 0; index < definition.LoadSlots.Count; index++)
            {
                if (string.Equals(
                        definition.LoadSlots[index]?.RequiredLoadIdentifier,
                        requiredLoadIdentifier,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private static Car FindNearestCompatibleCar(Car source, Car.LogicalEnd end)
        {
            var set = source.set;
            var index = set?.IndexOfCar(source);
            if (!index.HasValue)
            {
                return null;
            }

            var cursor = index.Value;
            var stop = false;
            while (!stop)
            {
                var candidate = set.NextCarConnected(
                    ref cursor,
                    end,
                    IntegrationSet.EnumerationCondition.AirConnected,
                    out stop);
                if (candidate == null)
                {
                    break;
                }

                if (candidate == source)
                {
                    continue;
                }

                if (IsSupportedTarget(candidate) &&
                    CanTargetReceiveFromSource(source.Definition, candidate.Definition))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool IsSupportedTarget(Car car)
        {
            return car != null &&
                   (car.Archetype == CarArchetype.LocomotiveDiesel ||
                    car.Archetype == CarArchetype.LocomotiveSteam ||
                    car.Archetype == CarArchetype.Tender);
        }

        private static void TransferLoads(Car source, Car target, float maximumTransfer)
        {
            if (maximumTransfer <= 0f || source?.Definition?.LoadSlots == null || target?.Definition?.LoadSlots == null)
            {
                return;
            }

            var remainingTransfer = maximumTransfer;
            for (var targetIndex = 0; targetIndex < target.Definition.LoadSlots.Count; targetIndex++)
            {
                if (remainingTransfer <= 0f)
                {
                    break;
                }

                var targetSlot = target.Definition.LoadSlots[targetIndex];
                var loadId = targetSlot?.RequiredLoadIdentifier;
                if (string.IsNullOrWhiteSpace(loadId))
                {
                    continue;
                }

                var sourceIndex = FindLoadSlot(source.Definition, loadId);
                if (sourceIndex < 0)
                {
                    continue;
                }

                var sourceLoad = source.GetLoadInfo(sourceIndex);
                if (!sourceLoad.HasValue || sourceLoad.Value.Quantity <= 0f)
                {
                    continue;
                }

                var targetLoad = target.GetLoadInfo(targetIndex);
                var targetQuantity = targetLoad?.Quantity ?? 0f;
                var quantity = FuseConfusingSupplementsRefillerPolicy.Take(
                    ref remainingTransfer,
                    targetSlot.MaximumCapacity - targetQuantity,
                    sourceLoad.Value.Quantity);
                if (quantity <= 0f)
                {
                    continue;
                }

                var remainingSource = sourceLoad.Value;
                remainingSource.Quantity -= quantity;
                target.SetLoadInfo(targetIndex, new CarLoadInfo(loadId, targetQuantity + quantity));
                source.SetLoadInfo(sourceIndex, remainingSource);
            }
        }
    }
}
