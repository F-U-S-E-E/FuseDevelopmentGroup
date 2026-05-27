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
    [HarmonyPatch]
    public static class FusePrefabStoreWhistleDefinitionsPatch
    {
        private static readonly FieldInfo StoresField = AccessTools.Field(typeof(PrefabStore), "_stores");
        private static readonly HashSet<string> LoggedWhistleStoreFailures =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PrefabStore), "AllDefinitionInfosOfType")
                ?.MakeGenericMethod(typeof(WhistleDefinition));
        }

        public static bool Prefix(
            PrefabStore __instance,
            ref IEnumerable<TypedContainerItem<WhistleDefinition>> __result)
        {
            __result = SafeWhistleDefinitions(__instance);
            return false;
        }

        internal static IEnumerable<TypedContainerItem<WhistleDefinition>> SafeWhistleDefinitions(PrefabStore prefabStore)
        {
            return EnumerateWhistleDefinitions(prefabStore)
                .Concat(FuseAudioAPI.GetWhistleDefinitionItems());
        }

        private static IEnumerable<TypedContainerItem<WhistleDefinition>> EnumerateWhistleDefinitions(PrefabStore prefabStore)
        {
            if (prefabStore == null)
            {
                yield break;
            }

            var stores = StoresField?.GetValue(prefabStore) as IEnumerable<AssetPackRuntimeStore>;
            if (stores == null)
            {
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var store in stores)
            {
                Container container;
                try
                {
                    container = store?.Container();
                }
                catch (Exception ex)
                {
                    var storeId = store?.Identifier ?? "<unknown>";
                    if (LoggedWhistleStoreFailures.Add(storeId))
                    {
                        FuseLog.Warning(
                            $"FUSE skipped whistle definitions from asset store '{storeId}' " +
                            $"because its definitions could not be inspected: {ex.GetBaseException().Message}");
                    }

                    continue;
                }

                foreach (var item in container?.Objects ?? Enumerable.Empty<ContainerItem>())
                {
                    var definition = item?.Definition as WhistleDefinition;
                    if (definition == null || string.IsNullOrEmpty(item.Identifier) || !seen.Add(item.Identifier))
                    {
                        continue;
                    }

                    yield return new TypedContainerItem<WhistleDefinition>
                    {
                        Identifier = item.Identifier,
                        Metadata = item.Metadata,
                        Definition = definition
                    };
                }
            }
        }
    }

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
        public static bool Prefix(UIPanelBuilder builder, Car ____car)
        {
            try
            {
                if (____car == null || ____car.Definition?.Components == null)
                {
                    return false;
                }

                AddWhistleDropdown(builder, ____car);
                AddHornDropdown(builder, ____car);
                AddBellDropdown(builder, ____car);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE suppressed an exception while building the locomotive customize sound tab", ex);
            }

            return false;
        }

        private static void AddWhistleDropdown(UIPanelBuilder builder, Car car)
        {
            var whistleComponent = car.Definition.Components.OfType<WhistleComponent>().FirstOrDefault();
            if (whistleComponent == null)
            {
                return;
            }

            builder.AddSection("Whistle", section =>
            {
                try
                {
                    AddWhistleDropdownField(section, car, whistleComponent);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception(
                        $"FUSE skipped whistle customization UI for car '{car.id ?? "<unknown>"}' " +
                        "so the customize window can still open",
                        ex);
                }
            }, 0f);
        }

        private static void AddWhistleDropdownField(UIPanelBuilder builder, Car car, WhistleComponent whistleComponent)
        {
            var settings = WhistleCustomizationSettings.FromPropertyValue(ReadWhistleCustomizationValue(car)) ??
                           new WhistleCustomizationSettings(whistleComponent.DefaultWhistleIdentifier);
            var whistleItems = FusePrefabStoreWhistleDefinitionsPatch
                .SafeWhistleDefinitions(TrainController.Shared?.PrefabStore as PrefabStore)
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Identifier))
                .GroupBy(item => item.Identifier, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => DisplayNameForWhistle(item), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var whistleIds = whistleItems.Select(item => item.Identifier).ToList();
            var values = whistleItems.Select(DisplayNameForWhistle).ToList();
            var currentIdentifier = settings.WhistleIdentifier ?? whistleComponent.DefaultWhistleIdentifier ?? string.Empty;
            if (whistleIds.Count == 0 && !string.IsNullOrWhiteSpace(currentIdentifier))
            {
                whistleIds.Add(currentIdentifier);
                values.Add(currentIdentifier);
            }

            var currentSelectedIndex = whistleIds.FindIndex(id =>
                string.Equals(currentIdentifier, id, StringComparison.OrdinalIgnoreCase));
            if (currentSelectedIndex < 0 && !string.IsNullOrWhiteSpace(currentIdentifier))
            {
                whistleIds.Insert(0, currentIdentifier);
                values.Insert(0, currentIdentifier);
                currentSelectedIndex = 0;
            }

            if (whistleIds.Count == 0)
            {
                return;
            }

            currentSelectedIndex = Math.Max(0, currentSelectedIndex);
            builder.AddField("Whistle", builder.AddDropdown(values, currentSelectedIndex, index =>
            {
                if (index < 0 || index >= whistleIds.Count)
                {
                    return;
                }

                car.KeyValueObject[WhistleCustomizationSettings.ObjectKey] =
                    new WhistleCustomizationSettings(whistleIds[index]).PropertyValue;
            }));
        }

        private static Value ReadWhistleCustomizationValue(Car car)
        {
            try
            {
                return car?.KeyValueObject?[WhistleCustomizationSettings.ObjectKey] ?? Value.Null();
            }
            catch
            {
                return Value.Null();
            }
        }

        private static string DisplayNameForWhistle(TypedContainerItem<WhistleDefinition> item)
        {
            return string.IsNullOrWhiteSpace(item?.Metadata?.Name)
                ? item?.Identifier ?? string.Empty
                : item.Metadata.Name;
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
