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

    /// <summary>
    /// Prefix patch on <c>PrefabStore.DefinitionForIdentifier&lt;WhistleDefinition&gt;</c>
    /// that returns a FUSE-built <see cref="WhistleDefinition"/> when the
    /// identifier matches a FUSE-registered whistle (e.g. legacy SC-style
    /// <c>sc.Manns Creek 3-Chime - Cass 11</c>).
    ///
    /// Vanilla's implementation walks <c>PrefabStore._stores</c> looking for
    /// an <see cref="Model.Database.AssetPackRuntimeStore"/> whose
    /// <c>ContainsIdentifier</c> answers yes for the identifier and throws
    /// <c>UnknownIdentifierException</c> when none do. FUSE whistles aren't
    /// registered inside any asset-pack store — they live in
    /// <c>FuseAudioAPI.Whistles</c> and were previously only surfaced via
    /// <see cref="FusePrefabStoreWhistleDefinitionsPatch"/>'s Postfix on
    /// <c>AllDefinitionInfosOfType</c>, so customize dropdowns saw them but
    /// <c>WhistleController.Configure</c>'s
    /// <c>prefabStore.DefinitionForIdentifier&lt;WhistleDefinition&gt;(whistleIdentifier, out metadata)</c>
    /// call threw before it could read <c>whistleDefinition.Model</c> and
    /// trigger the 3D-model load. End result: every legacy-converted loco
    /// had its custom whistle audio playing (FUSE handles audio out-of-band)
    /// but no 3D whistle model on the cab.
    /// </summary>
    [HarmonyPatch]
    public static class FusePrefabStoreDefinitionForIdentifierWhistlePatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                    typeof(PrefabStore),
                    "DefinitionForIdentifier",
                    new[] { typeof(string), typeof(ObjectMetadata).MakeByRefType() })
                ?.MakeGenericMethod(typeof(WhistleDefinition));
        }

        public static bool Prefix(string definitionIdentifier, ref ObjectMetadata metadata, ref WhistleDefinition __result)
        {
            try
            {
                if (FuseAudioAPI.TryBuildWhistleDefinition(definitionIdentifier, out var definition, out var resolvedMetadata))
                {
                    __result = definition;
                    metadata = resolvedMetadata;
                    return false;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    $"FUSE audio failed while resolving FUSE whistle definition for identifier '{definitionIdentifier}'",
                    ex);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(WhistleController), "Configure", new[] { typeof(WhistleCustomizationSettings) })]
    public static class FuseWhistleControllerConfigurePatch
    {
        // Always return true so the vanilla Configure(WhistleCustomizationSettings)
        // async method runs to completion. Vanilla resolves and instantiates
        // the WhistleDefinition.Model (the 3D whistle perched on the loco
        // boiler/cab) from the asset pack — FUSE cannot easily replicate
        // that step without duplicating the async asset-load + cancellation
        // plumbing in WhistleController. Vanilla's audio branch is gated
        // behind <c>!whistleDefinition.Audio.IsEmpty</c>, and
        // <see cref="FuseAudioAPI.GetWhistleDefinitionItems"/> emits an
        // empty <c>AssetReference</c> for FUSE-registered whistles, so
        // vanilla skips its async <c>LoadAssetAsync&lt;AudioClip&gt;</c>
        // branch and the loose-file clip we apply below is the only thing
        // calling <c>whistlePlayer.Configure</c>.
        //
        // Earlier this method returned <c>!TryConfigureWhistle</c>, which
        // short-circuited vanilla whenever FUSE owned the whistle —
        // including the Model branch. That left every legacy-converted
        // loco silently missing its 3D whistle model on the cab roof
        // even though the audio played correctly.
        public static bool Prefix(WhistleController __instance, WhistleCustomizationSettings settings)
        {
            try
            {
                FuseAudioAPI.TryConfigureWhistle(__instance, settings.WhistleIdentifier);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE audio failed while configuring custom whistle", ex);
            }
            return true;
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
