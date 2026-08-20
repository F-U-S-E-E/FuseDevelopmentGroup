using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Infrastructure;
using Game.Messages;
using Game.State;
using HarmonyLib;
using KeyValue.Runtime;
using Model;
using Model.Definition;
using Model.Definition.Data;
using Railloader.Extensions;
using UI.Builder;
using UnityEngine;

namespace FUSE.Compatibility
{
    /// <summary>
    /// FUSE-owned implementation of the rolling-stock body-group component used by
    /// legacy Confusing Supplements asset packs. The implementation deliberately
    /// lives in FUSE's namespace: FUSE reproduces the data contract and behavior
    /// without loading or redistributing the retired library assembly.
    /// </summary>
    internal sealed class FuseConfusingSupplementsBodygroupsComponent : Model.Definition.Component
    {
        internal const string ComponentKind = "ConfusingSupplements.Bodygroups";

        public override string Kind => ComponentKind;

        public Dictionary<string, FuseConfusingSupplementsBodygroup> Groups { get; set; } =
            new Dictionary<string, FuseConfusingSupplementsBodygroup>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class FuseConfusingSupplementsBodygroup
    {
        public string Name { get; set; }

        public Dictionary<string, FuseConfusingSupplementsBodygroupOption> Options { get; set; } =
            new Dictionary<string, FuseConfusingSupplementsBodygroupOption>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class FuseConfusingSupplementsBodygroupOption
    {
        public string Name { get; set; }

        public string[] Path { get; set; }
    }

    internal sealed class FuseConfusingSupplementsBodygroupsBuilder : ComponentBuilder<FuseConfusingSupplementsBodygroupsComponent>
    {
        protected override void Build(
            ComponentBuilderContext context,
            FuseConfusingSupplementsBodygroupsComponent component)
        {
            var car = context.GameObject?.GetComponentInParent<Car>();
            var modelRoot = car?.BodyTransform;
            if (car == null || modelRoot == null)
            {
                FuseLog.Warning(
                    $"FUSE could not attach legacy bodygroups to '{context.ObjectName ?? "<unknown car>"}' " +
                    "because its car body was unavailable.");
                return;
            }

            foreach (var groupEntry in component?.Groups ??
                         Enumerable.Empty<KeyValuePair<string, FuseConfusingSupplementsBodygroup>>())
            {
                var groupId = groupEntry.Key?.Trim();
                var group = groupEntry.Value;
                if (string.IsNullOrWhiteSpace(groupId) || group?.Options == null || group.Options.Count == 0)
                {
                    FuseLog.Warning(
                        $"FUSE ignored an empty legacy bodygroup on '{context.ObjectName ?? "<unknown car>"}'.");
                    continue;
                }

                var propertyKey = "cs.bodygroups." + groupId;
                var options = group.Options.ToArray();
                context.ObserveProperty(propertyKey, value => ApplySelection(
                    context.ObjectName,
                    modelRoot,
                    groupId,
                    options,
                    ReadSelectedOption(value, options)));
            }
        }

        internal static string ReadSelectedOption(
            Value value,
            IReadOnlyList<KeyValuePair<string, FuseConfusingSupplementsBodygroupOption>> options)
        {
            var selected = value.StringValue;
            return string.IsNullOrWhiteSpace(selected) && options != null && options.Count > 0
                ? options[0].Key
                : selected;
        }

        private static void ApplySelection(
            string objectName,
            Transform modelRoot,
            string groupId,
            IEnumerable<KeyValuePair<string, FuseConfusingSupplementsBodygroupOption>> options,
            string selectedId)
        {
            foreach (var optionEntry in options)
            {
                var path = optionEntry.Value?.Path;
                if (path == null || path.Length == 0)
                {
                    continue;
                }

                try
                {
                    var target = modelRoot.ResolveTransform(
                        new TransformReference { Path = path },
                        defaultReturnsReceiver: false);
                    if (target == null)
                    {
                        FuseLog.Warning(
                            $"FUSE legacy bodygroup '{groupId}' on '{objectName ?? "<unknown car>"}' " +
                            $"could not resolve option '{optionEntry.Key}' path '{string.Join("/", path)}'.");
                        continue;
                    }

                    target.gameObject.SetActive(
                        string.Equals(optionEntry.Key, selectedId, StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE contained a malformed legacy bodygroup path for '{objectName ?? "<unknown car>"}', " +
                        $"group '{groupId}', option '{optionEntry.Key}': {ex.GetBaseException().Message}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(UI.CarCustomizeWindow.CarCustomizeWindow), "BuildColorTab")]
    internal static class FuseConfusingSupplementsBodygroupsCustomizePatch
    {
        private static void Postfix(UIPanelBuilder builder, Car ____car)
        {
            if (____car?.Definition?.Components == null)
            {
                return;
            }

            var components = ____car.Definition.Components
                .OfType<FuseConfusingSupplementsBodygroupsComponent>()
                .Where(component => component.Groups != null && component.Groups.Count > 0)
                .ToArray();
            if (components.Length == 0)
            {
                return;
            }

            try
            {
                builder.AddSection("Bodygroups", section =>
                {
                    foreach (var component in components)
                    {
                        AddGroups(section, ____car, component);
                    }
                }, 0f);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE contained a legacy bodygroup Customize-window error; " +
                    $"the rest of the window remains usable: {ex.GetBaseException().Message}");
            }
        }

        private static void AddGroups(
            UIPanelBuilder builder,
            Car car,
            FuseConfusingSupplementsBodygroupsComponent component)
        {
            foreach (var groupEntry in component.Groups)
            {
                var groupId = groupEntry.Key?.Trim();
                var group = groupEntry.Value;
                if (string.IsNullOrWhiteSpace(groupId) || group?.Options == null || group.Options.Count == 0)
                {
                    continue;
                }

                var optionEntries = group.Options.ToArray();
                var optionIds = optionEntries.Select(entry => entry.Key).ToArray();
                var optionNames = optionEntries
                    .Select(entry => string.IsNullOrWhiteSpace(entry.Value?.Name) ? entry.Key : entry.Value.Name)
                    .ToList();
                var propertyKey = "cs.bodygroups." + groupId;
                var current = car.KeyValueObject == null
                    ? null
                    : car.KeyValueObject[propertyKey].StringValue;
                var selectedIndex = Array.FindIndex(
                    optionIds,
                    optionId => string.Equals(optionId, current, StringComparison.OrdinalIgnoreCase));
                if (selectedIndex < 0)
                {
                    selectedIndex = 0;
                }

                var fieldName = string.IsNullOrWhiteSpace(group.Name) ? groupId : group.Name;
                builder.AddField(fieldName, builder.AddDropdown(optionNames, selectedIndex, index =>
                {
                    if (index < 0 || index >= optionIds.Length)
                    {
                        return;
                    }

                    StateManager.ApplyLocal(new PropertyChange(
                        car.id,
                        propertyKey,
                        new StringPropertyValue(optionIds[index])));
                }));
            }
        }
    }
}
