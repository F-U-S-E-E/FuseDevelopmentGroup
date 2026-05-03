using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KeyValue.Runtime;
using Model;
using Model.ComponentBuilders;
using Model.Database;
using Model.Definition;
using Model.Definition.Components;
using Model.Definition.Data;
using FUSE.API;
using FUSE.Infrastructure;
using RollingStock.Steam;
using UI.Builder;

namespace FUSE.Patches
{
    [HarmonyPatch]
    public static class FusePrefabStoreWhistleDefinitionsPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PrefabStore), "AllDefinitionInfosOfType")
                ?.MakeGenericMethod(typeof(WhistleDefinition));
        }

        public static void Postfix(ref IEnumerable<TypedContainerItem<WhistleDefinition>> __result)
        {
            if (!FuseAudioAPI.HasWhistles)
            {
                return;
            }

            __result = (__result ?? Enumerable.Empty<TypedContainerItem<WhistleDefinition>>())
                .Concat(FuseAudioAPI.GetWhistleDefinitionItems());
        }
    }

    [HarmonyPatch(typeof(WhistleController), "Configure", new[] { typeof(WhistleCustomizationSettings) })]
    public static class FuseWhistleControllerConfigurePatch
    {
        public static bool Prefix(WhistleController __instance, WhistleCustomizationSettings settings)
        {
            try
            {
                return !FuseAudioAPI.TryConfigureWhistle(__instance, settings.WhistleIdentifier);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE audio failed while configuring custom whistle", ex);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(HornComponentBuilder), "Build")]
    public static class FuseHornComponentBuilderPatch
    {
        public static void Postfix(ComponentBuilderContext ctx, Model.Definition.Component component)
        {
            if (!(component is HornComponent))
            {
                return;
            }

            FuseAudioAPI.AttachRuntimeControllers(ctx.GameObject?.GetComponentInParent<Car>());
        }
    }

    [HarmonyPatch(typeof(BellComponentBuilder), "Build")]
    public static class FuseBellComponentBuilderPatch
    {
        public static void Postfix(ComponentBuilderContext ctx, Model.Definition.Component component)
        {
            if (!(component is BellComponent))
            {
                return;
            }

            FuseAudioAPI.AttachRuntimeControllers(ctx.GameObject?.GetComponentInParent<Car>());
        }
    }

    [HarmonyPatch(typeof(UI.CarCustomizeWindow.CarCustomizeWindow), "BuildSoundTab")]
    public static class FuseCarCustomizeSoundTabPatch
    {
        public static void Postfix(UIPanelBuilder builder, Car ____car)
        {
            if (____car == null || ____car.Definition?.Components == null)
            {
                return;
            }

            AddHornDropdown(builder, ____car);
            AddBellDropdown(builder, ____car);
        }

        private static void AddHornDropdown(UIPanelBuilder builder, Car car)
        {
            if (!FuseAudioAPI.HasHorns || !car.Definition.Components.OfType<HornComponent>().Any())
            {
                return;
            }

            var choices = FuseAudioAPI.HornChoices();
            if (choices.Count == 0)
            {
                return;
            }

            builder.AddSection("FUSE Horn", section =>
            {
                var ids = new List<string> { string.Empty };
                ids.AddRange(choices.Select(choice => choice.Id));
                var names = new List<string> { "Default" };
                names.AddRange(choices.Select(choice => choice.Name));
                var current = car.KeyValueObject?[FuseAudioAPI.HornCustomKey].StringValue ?? string.Empty;
                var selected = Math.Max(0, ids.FindIndex(id => string.Equals(id, current, StringComparison.OrdinalIgnoreCase)));
                section.AddField("Horn", section.AddDropdown(names, selected, index =>
                {
                    car.KeyValueObject[FuseAudioAPI.HornCustomKey] = string.IsNullOrWhiteSpace(ids[index])
                        ? Value.Null()
                        : Value.String(ids[index]);
                }));
            }, 0f);
        }

        private static void AddBellDropdown(UIPanelBuilder builder, Car car)
        {
            if (!FuseAudioAPI.HasBells || !car.Definition.Components.OfType<BellComponent>().Any())
            {
                return;
            }

            var choices = FuseAudioAPI.BellChoices();
            if (choices.Count == 0)
            {
                return;
            }

            builder.AddSection("FUSE Bell", section =>
            {
                var ids = new List<string> { string.Empty };
                ids.AddRange(choices.Select(choice => choice.Id));
                var names = new List<string> { "Default" };
                names.AddRange(choices.Select(choice => choice.Name));
                var current = car.KeyValueObject?[FuseAudioAPI.BellCustomKey].StringValue ?? string.Empty;
                var selected = Math.Max(0, ids.FindIndex(id => string.Equals(id, current, StringComparison.OrdinalIgnoreCase)));
                section.AddField("Bell", section.AddDropdown(names, selected, index =>
                {
                    car.KeyValueObject[FuseAudioAPI.BellCustomKey] = string.IsNullOrWhiteSpace(ids[index])
                        ? Value.Null()
                        : Value.String(ids[index]);
                }));
            }, 0f);
        }
    }
}
