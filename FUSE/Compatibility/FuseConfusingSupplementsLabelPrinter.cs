using System;
using System.Collections.Generic;
using System.Linq;
using Effects.Decals;
using FUSE.Infrastructure;
using Game.Messages;
using Game.State;
using HarmonyLib;
using Helpers;
using KeyValue.Runtime;
using Model;
using Model.Definition;
using Model.Definition.Components;
using Newtonsoft.Json;
using Railloader.Extensions;
using UI.Builder;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FUSE.Compatibility
{
    internal sealed class FuseConfusingSupplementsLabelPrinterComponent : DecalComponent
    {
        internal const string ComponentKind = "ConfusingSupplements.LabelPrinter";

        public override string Kind => ComponentKind;

        public string Group { get; set; }

        [JsonIgnore]
        [DefinitionProperty(Hidden = true)]
        public string SavedPropertyId => (string.IsNullOrWhiteSpace(Group) ? Name : Group)?.Trim();
    }

    internal sealed class FuseConfusingSupplementsLabelPrinterBuilder : ComponentBuilder<FuseConfusingSupplementsLabelPrinterComponent>
    {
        internal const string SavedPropertyPrefix = "cs.labelprinter.";
        internal const string TextFieldName = "text";

        protected override void Build(
            ComponentBuilderContext context,
            FuseConfusingSupplementsLabelPrinterComponent component)
        {
            if (context.GameObject == null || component == null)
            {
                return;
            }

            var savedPropertyId = component.SavedPropertyId;
            if (string.IsNullOrWhiteSpace(savedPropertyId))
            {
                FuseLog.Warning(
                    $"FUSE ignored a legacy label-printer component on '{context.ObjectName ?? "<unknown car>"}' " +
                    "because both name and group are empty.");
                return;
            }

            if (!TryGetTemplateName(component.Content, out var templateName))
            {
                FuseLog.Warning(
                    $"FUSE ignored legacy label-printer '{savedPropertyId}' on " +
                    $"'{context.ObjectName ?? "<unknown car>"}' because decal content " +
                    $"'{component.Content}' is unsupported.");
                return;
            }

            var projector = context.GameObject.AddComponent<DecalProjector>();
            FuseConfusingSupplementsDecalProjector.Configure(projector, component.Size);

            var helper = context.GameObject.AddComponent<DecalProjectorHelper>();
            helper.decalRenderer = CanvasDecalRenderer.Shared;
            helper.templateName = templateName;
            if (!string.IsNullOrWhiteSpace(component.ForceColor))
            {
                var color = ColorHelper.ColorFromHex(component.ForceColor);
                if (color.HasValue)
                {
                    helper.ForceColor(color.Value);
                }
            }

            context.ObserveProperty(SavedPropertyKey(savedPropertyId), value =>
            {
                helper.text = ReadText(value);
                helper.RenderDecal();
            });
        }

        internal static string ReadText(Value value)
        {
            if (value.Type == KeyValue.Runtime.ValueType.Dictionary)
            {
                var values = value.DictionaryValue;
                return values != null && values.TryGetValue(TextFieldName, out var text)
                    ? text.StringValue ?? string.Empty
                    : string.Empty;
            }

            return value.StringValue ?? string.Empty;
        }

        internal static IPropertyValue UpdatedTextValue(Value current, string text)
        {
            var updatedText = new StringPropertyValue(text ?? string.Empty);
            if (current.Type != KeyValue.Runtime.ValueType.Dictionary)
            {
                return updatedText;
            }

            var values = PropertyValueConverter.RuntimeToSnapshot(current.DictionaryValue);
            values[TextFieldName] = updatedText;
            return new DictionaryPropertyValue(values);
        }

        internal static string SavedPropertyKey(string savedPropertyId)
        {
            return SavedPropertyPrefix + savedPropertyId;
        }

        private static bool TryGetTemplateName(DecalContent content, out string templateName)
        {
            switch (content)
            {
                case DecalContent.RoadNumber:
                    templateName = "Number";
                    return true;
                case DecalContent.Lettering:
                    templateName = "Tender";
                    return true;
                default:
                    templateName = null;
                    return false;
            }
        }
    }

    [HarmonyPatch(typeof(UI.CarCustomizeWindow.CarCustomizeWindow), "BuildColorTab")]
    internal static class FuseConfusingSupplementsLabelPrinterCustomizePatch
    {
        private static void Postfix(UIPanelBuilder builder, Car ____car)
        {
            if (____car?.Definition?.Components == null)
            {
                return;
            }

            var labels = ____car.Definition.Components
                .OfType<FuseConfusingSupplementsLabelPrinterComponent>()
                .Where(component => !string.IsNullOrWhiteSpace(component.SavedPropertyId))
                .GroupBy(component => component.SavedPropertyId, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    Id = group.Key,
                    Name = group.Select(component => component.Name)
                        .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? group.Key
                })
                .OrderBy(label => label.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (labels.Length == 0)
            {
                return;
            }

            try
            {
                builder.AddSection("Labels", section =>
                {
                    foreach (var label in labels)
                    {
                        section.AddField(
                            label.Name,
                            section.AddInputField(
                                GetText(____car, label.Id),
                                text => SetText(____car, label.Id, text),
                                null,
                                100));
                    }
                }, 20f);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE contained a legacy label-printer Customize-window error; " +
                    $"the rest of the window remains usable: {ex.GetBaseException().Message}");
            }
        }

        internal static string GetText(Car car, string savedPropertyId)
        {
            if (car?.KeyValueObject == null || string.IsNullOrWhiteSpace(savedPropertyId))
            {
                return string.Empty;
            }

            return FuseConfusingSupplementsLabelPrinterBuilder.ReadText(
                car.KeyValueObject[FuseConfusingSupplementsLabelPrinterBuilder.SavedPropertyKey(savedPropertyId)]);
        }

        private static void SetText(Car car, string savedPropertyId, string text)
        {
            if (car?.KeyValueObject == null || string.IsNullOrWhiteSpace(savedPropertyId))
            {
                return;
            }

            var propertyKey = FuseConfusingSupplementsLabelPrinterBuilder.SavedPropertyKey(savedPropertyId);
            StateManager.ApplyLocal(new PropertyChange(
                car.id,
                propertyKey,
                FuseConfusingSupplementsLabelPrinterBuilder.UpdatedTextValue(
                    car.KeyValueObject[propertyKey],
                    text)));
        }
    }

    internal static class FuseConfusingSupplementsDecalProjector
    {
        internal static void Configure(DecalProjector projector, Vector3 size)
        {
            projector.size = size;
            projector.pivot = Vector3.zero;
            projector.drawDistance = Mathf.Max(600f, Mathf.Max(size.x, size.y) * 100f);
        }
    }
}
