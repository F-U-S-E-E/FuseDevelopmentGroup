using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AssetPack.Runtime;
using HarmonyLib;
using KeyValue.Runtime;
using Model;
using Model.ComponentBuilders;
using Model.Database;
using Model.Definition;
using Model.Definition.Components;
using Model.Definition.Data;
using FUSE.Runtime.API;
using FUSE.Infrastructure;
using RollingStock.Steam;
using UI.Builder;

namespace FUSE.Patches
{
    [HarmonyPatch(typeof(WhistleController), "Configure", new[] { typeof(WhistleCustomizationSettings) })]
    public static class FuseWhistleControllerConfigurePatch
    {
        // When FUSE owns the whistle audio, suppress vanilla entirely.
        // Vanilla would otherwise call
        // <c>prefabStore.DefinitionForIdentifier&lt;WhistleDefinition&gt;(whistleIdentifier, out metadata)</c>,
        // which walks <c>PrefabStore._stores</c> looking for an asset pack
        // that contains the identifier and throws
        // <c>UnknownIdentifierException</c> for FUSE-only whistles. The
        // async Configure aborts at that point — the 3D whistle model
        // never spawns and the loose-file clip we apply via
        // <see cref="FuseAudioAPI.TryConfigureWhistle"/> ends up being
        // the only audio configured.
        //
        // Two previous attempts at letting vanilla run for the Model
        // branch (by Prefix-patching the closed generic
        // <c>DefinitionForIdentifier&lt;WhistleDefinition&gt;</c> to
        // short-circuit the asset-pack walk) both broke scenery /
        // material / car / truck loading. Patching a generic method's
        // closed form fires the hook for EVERY closed form that shares
        // the JIT'd IL body, and even with a runtime
        // <c>__originalMethod.GetGenericArguments()</c> bail-out the
        // patched IL still corrupts the return value of every other T
        // (~2k scenery skips across 20+ packages in the regression run).
        //
        // For now we accept that FUSE-converted legacy whistles play
        // their custom audio but render the loco's vanilla 3D whistle
        // model. Restoring the FUSE whistle model needs a non-generic
        // patch surface or an in-FUSE async asset-load that writes
        // <c>WhistleController._whistleModel</c> by reflection without
        // ever calling <c>DefinitionForIdentifier</c>.
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
                // Mirror the runtime controller's lookup order: FUSE key
                // wins if set, otherwise read the SC-era legacy key so a
                // save migrated from SC shows its existing horn selection
                // pre-selected in the dropdown rather than "Default".
                var current = car.KeyValueObject?[FuseAudioAPI.HornCustomKey].StringValue;
                if (string.IsNullOrWhiteSpace(current))
                {
                    current = car.KeyValueObject?[FuseAudioAPI.LegacyHornCustomKey].StringValue;
                }
                current = current ?? string.Empty;
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
                // Same FUSE-then-legacy lookup as the horn dropdown above.
                var current = car.KeyValueObject?[FuseAudioAPI.BellCustomKey].StringValue;
                if (string.IsNullOrWhiteSpace(current))
                {
                    current = car.KeyValueObject?[FuseAudioAPI.LegacyBellCustomKey].StringValue;
                }
                current = current ?? string.Empty;
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
