using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FUSE.Runtime.API;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Runtime.Lifecycle;
using FUSE.Loading;
using FUSE.Authoring.Migrations;
using FUSE.Runtime.Registry;
using Model;
using Model.Ops;
using Newtonsoft.Json.Linq;
using Railloader;
using TMPro;
using Track;
using UI;
using UI.Builder;
using UI.Common;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FUSE.Interface
{
    internal sealed partial class FuseHealthUi : MonoBehaviour
    {

        private void BuildSettingsContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 170f;
            builder.Spacing = 6f;

            var multiplayer = FuseMultiplayerGuard.GetStatus();
            builder.AddSection("General");
            AddValueField(builder, "Asset Packs", AssetPackModeText());
            AddValueField(builder, "Profile", FuseModSetService.ActiveSetName);
            AddValueField(builder, "Profile Hash", multiplayer.LocalPackageFingerprint);
            AddSettingToggle(
                builder,
                "Multiplayer",
                FuseSettings.BlockNonHostMultiplayerClientWorldApply ? "Strict non-host block" : "Compatibility mode",
                FuseSettings.BlockNonHostMultiplayerClientWorldApply ? "Use Compat" : "Use Strict",
                () =>
                {
                    FuseSettings.SetBlockNonHostMultiplayerClientWorldApply(!FuseSettings.BlockNonHostMultiplayerClientWorldApply);
                    RebuildWindow();
                });
            AddSettingToggle(
                builder,
                "Visual Condition",
                FuseSettings.DecoupleVisualConditionLimits ? "independent of repair state" : "capped by repair state",
                FuseSettings.DecoupleVisualConditionLimits ? "Cap" : "Decouple",
                () =>
                {
                    FuseSettings.SetDecoupleVisualConditionLimits(!FuseSettings.DecoupleVisualConditionLimits);
                    // The setting changes how stored overrides render without
                    // any per-car value changing, so repaint them explicitly —
                    // nothing else re-derives car materials until a condition
                    // change or a culling visibility transition.
                    FuseVisualConditionAPI.RefreshAllOverriddenCars();
                    RebuildWindow();
                });
            AddSettingToggle(
                builder,
                "Loading Screen",
                FuseSettings.EnableEnhancedLoadingScreen ? "enhanced (FUSE)" : "stock game screen",
                FuseSettings.EnableEnhancedLoadingScreen ? "Use Stock" : "Use Enhanced",
                () =>
                {
                    FuseSettings.SetEnableEnhancedLoadingScreen(!FuseSettings.EnableEnhancedLoadingScreen);
                    RebuildWindow();
                });
            builder.Spacer(4f);

            builder.AddSection("Reporting");
            AddSettingToggle(
                builder,
                "Verbose Report",
                FuseSettings.VerboseApplyReportDetails ? "enabled" : "disabled",
                FuseSettings.VerboseApplyReportDetails ? "Disable" : "Enable",
                () =>
                {
                    FuseSettings.SetVerboseApplyReportDetails(!FuseSettings.VerboseApplyReportDetails);
                    RebuildWindow();
                });
            AddSettingToggle(
                builder,
                "Advanced Details",
                FuseSettings.ShowAdvancedHealthDetails ? "visible in panels" : "hidden by default",
                FuseSettings.ShowAdvancedHealthDetails ? "Hide" : "Show",
                () =>
                {
                    FuseSettings.SetShowAdvancedHealthDetails(!FuseSettings.ShowAdvancedHealthDetails);
                    RebuildWindow();
                });
            AddWrappedField(builder, "User Config", FuseSettings.GetUserSettingsPath(), 42f);
            builder.Spacer(6f);

            builder.AddSection("Car Spawning");
            AddSettingToggle(
                builder,
                "Random V-Condition",
                FuseSettings.RandomizeVisualConditionOnSpawn ? "randomized on spawn" : "disabled",
                FuseSettings.RandomizeVisualConditionOnSpawn ? "Disable" : "Enable",
                () =>
                {
                    FuseSettings.SetRandomizeVisualConditionOnSpawn(!FuseSettings.RandomizeVisualConditionOnSpawn);
                    RebuildWindow();
                });
            if (FuseSettings.RandomizeVisualConditionOnSpawn)
            {
                AddRandomVisualConditionBoundField(
                    builder, "Condition Min", FuseSettings.RandomVisualConditionMin, FuseSettings.SetRandomVisualConditionMin);
                AddRandomVisualConditionBoundField(
                    builder, "Condition Max", FuseSettings.RandomVisualConditionMax, FuseSettings.SetRandomVisualConditionMax);
                AddWrappedField(
                    builder,
                    " ",
                    "Newly spawned cars get a visual condition rolled between min and max (0 = weathered, 1 = factory fresh). Host-side only; values replicate to clients.",
                    42f);
            }
            builder.Spacer(6f);

            builder.AddSection("Package Settings");
            AddWrappedField(
                builder,
                "Location",
                "Mod-specific settings now live on each mod page. Open Mods, select a package, and its settings will appear in that package detail view.",
                52f);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Open Mods", () => SetPage(Page.Packages));
            }, 6f).Height(32f);
            builder.Spacer(6f);

            builder.AddSection("Last Action");
            AddWrappedField(builder, "Last Action", _lastAction, 52f);
            AddWrappedField(builder, "Mod Settings", FuseModSettingsStore.LastStatus, 42f);
            AddValueField(builder, "FUSE Map Load", FusePerformanceMetrics.FormatTiming("map load total"));
            AddValueField(builder, "Runtime Apply", FusePerformanceMetrics.FormatTiming("apply resident definitions"));
            builder.Spacer(8f);
        }

        private void BuildPackageSettingsContent(UIPanelBuilder builder)
        {
            builder.AddSection("Mod Settings");
            AddWrappedField(
                builder,
                "Storage",
                "Package settings are stored outside mod folders at " + FuseModSettingsStore.GetStorePath(),
                48f);

            var packages = FuseModLoader.GetLoadedModsInOrder()
                .Where(loaded => loaded?.Definition?.Settings != null && loaded.Definition.Settings.Count > 0)
                .OrderBy(loaded => loaded.Definition.Name ?? loaded.Definition.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (packages.Length == 0)
            {
                AddWrappedField(
                    builder,
                    "Status",
                    "No loaded package has declared settings yet. Add a top-level settings object to a FUSE definition to make controls appear here.",
                    52f);
                AddWrappedField(
                    builder,
                    "Schema",
                    "Supported controls: bool, enum, number, path, color, and text. Supported scopes: user, profile, and server.",
                    42f);
                builder.Spacer(6f);
                return;
            }

            AddValueField(builder, "Packages", packages.Length.ToString());
            foreach (var loaded in packages)
            {
                var visibleSettings = loaded.Definition.Settings
                    .Where(pair => pair.Value != null)
                    .Where(pair => FuseSettings.ShowAdvancedHealthDetails || !pair.Value.Advanced)
                    .OrderBy(pair => GetSettingLabel(pair.Key, pair.Value), StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var hiddenAdvanced = loaded.Definition.Settings.Count(pair => pair.Value?.Advanced == true) - visibleSettings.Count(pair => pair.Value?.Advanced == true);
                if (visibleSettings.Length == 0)
                {
                    continue;
                }

                builder.Spacer(4f);
                builder.AddSection(PackageSettingsTitle(loaded));
                if (hiddenAdvanced > 0)
                {
                    AddWrappedField(builder, "Hidden", hiddenAdvanced + " advanced setting(s). Enable Advanced Details to show them.", 34f);
                }

                foreach (var pair in visibleSettings)
                {
                    BuildPackageSettingControl(builder, loaded.Definition, pair.Key, pair.Value);
                }
            }

            builder.Spacer(6f);
        }

        private void BuildPackageSettingControl(UIPanelBuilder builder, FuseModDefinition definition, string key, FuseModSettingDefinition setting)
        {
            var type = FuseModSettingsStore.NormalizeType(setting?.Type);
            switch (type)
            {
                case "bool":
                    BuildBoolSettingControl(builder, definition, key, setting);
                    break;
                case "enum":
                    BuildEnumSettingControl(builder, definition, key, setting);
                    break;
                case "number":
                    BuildTextSettingControl(builder, definition, key, setting, isNumber: true);
                    break;
                case "path":
                case "color":
                case "text":
                default:
                    BuildTextSettingControl(builder, definition, key, setting, isNumber: false);
                    break;
            }

            if (FuseSettings.ShowAdvancedHealthDetails)
            {
                AddWrappedField(builder, " ", DescribePackageSetting(key, setting), 34f);
            }
            else if (!string.IsNullOrWhiteSpace(setting?.Description))
            {
                AddWrappedField(builder, " ", setting.Description, 34f);
            }
        }

        private void BuildBoolSettingControl(UIPanelBuilder builder, FuseModDefinition definition, string key, FuseModSettingDefinition setting)
        {
            builder.HStack(row =>
            {
                AddSettingRowLabel(row, GetSettingLabel(key, setting));
                row.AddToggle(
                    () => FuseModSettingsStore.GetBoolValue(definition, key, setting),
                    value =>
                    {
                        FuseModSettingsStore.SetValue(definition, key, setting, new JValue(value));
                        RebuildWindow();
                    });
                AddResetSettingButton(row, definition, key, setting);
            }, 8f).Height(30f);
        }

        private void BuildEnumSettingControl(UIPanelBuilder builder, FuseModDefinition definition, string key, FuseModSettingDefinition setting)
        {
            var values = (setting?.Values ?? Array.Empty<string>())
                .Where(value => value != null)
                .ToList();
            if (values.Count == 0)
            {
                BuildTextSettingControl(builder, definition, key, setting, isNumber: false);
                return;
            }

            var current = FuseModSettingsStore.GetStringValue(definition, key, setting);
            var selected = Math.Max(0, values.FindIndex(value => string.Equals(value, current, StringComparison.Ordinal)));
            builder.HStack(row =>
            {
                AddSettingRowLabel(row, GetSettingLabel(key, setting));
                row.AddDropdown(values, selected, index =>
                {
                    if (index < 0 || index >= values.Count)
                    {
                        return;
                    }

                    FuseModSettingsStore.SetValue(definition, key, setting, new JValue(values[index]));
                    RebuildWindow();
                });
                AddResetSettingButton(row, definition, key, setting);
            }, 8f).Height(32f);
        }

        private void BuildTextSettingControl(UIPanelBuilder builder, FuseModDefinition definition, string key, FuseModSettingDefinition setting, bool isNumber)
        {
            var current = isNumber
                ? FuseModSettingsStore.GetNumberValue(definition, key, setting).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                : FuseModSettingsStore.GetStringValue(definition, key, setting);
            builder.HStack(row =>
            {
                AddSettingRowLabel(row, GetSettingLabel(key, setting));
                row.AddInputField(current, value =>
                {
                    SaveTextSetting(definition, key, setting, value, isNumber);
                });
                AddResetSettingButton(row, definition, key, setting);
            }, 8f).Height(32f);
        }

        private void SaveTextSetting(FuseModDefinition definition, string key, FuseModSettingDefinition setting, string value, bool isNumber)
        {
            if (isNumber)
            {
                double parsed;
                if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                {
                    _lastAction = $"Setting '{key}' was not saved because '{value}' is not a number.";
                    return;
                }

                FuseModSettingsStore.SetValue(definition, key, setting, new JValue(parsed));
                return;
            }

            FuseModSettingsStore.SetValue(definition, key, setting, new JValue(value ?? string.Empty));
        }

        private void AddRandomVisualConditionBoundField(UIPanelBuilder builder, string label, float current, Action<float> save)
        {
            builder.HStack(row =>
            {
                AddSettingRowLabel(row, label);
                row.AddInputField(
                    current.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    value =>
                    {
                        float parsed;
                        if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                        {
                            _lastAction = $"Setting '{label}' was not saved because '{value}' is not a number.";
                            return;
                        }

                        // The setter clamps to 0..1 and persists the override.
                        save(parsed);
                    });
            }, 8f).Height(30f);
        }

        private static void AddSettingRowLabel(UIPanelBuilder row, string label)
        {
            row.AddLabel(label, text =>
            {
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.alignment = TextAlignmentOptions.Right;
            });
        }

        private void AddResetSettingButton(UIPanelBuilder row, FuseModDefinition definition, string key, FuseModSettingDefinition setting)
        {
            row.AddButtonCompact("Reset", () =>
            {
                FuseModSettingsStore.ResetValue(definition, key, setting);
                RebuildWindow();
            });
        }

        private static string PackageSettingsTitle(FuseLoadedMod loaded)
        {
            var definition = loaded?.Definition;
            if (definition == null)
            {
                return "Package Settings";
            }

            var name = string.IsNullOrWhiteSpace(definition.Name) ? definition.Id : definition.Name;
            return string.IsNullOrWhiteSpace(name) ? "Package Settings" : name;
        }

        private static string GetSettingLabel(string key, FuseModSettingDefinition setting)
        {
            return string.IsNullOrWhiteSpace(setting?.Label) ? key : setting.Label.Trim();
        }

        private static string DescribePackageSetting(string key, FuseModSettingDefinition setting)
        {
            var parts = new List<string>
            {
                "key=" + key,
                "type=" + FuseModSettingsStore.NormalizeType(setting?.Type),
                "scope=" + FuseModSettingsStore.DescribeScope(setting)
            };

            if (FuseModSettingsStore.FormatValue(setting?.Default).Length > 0)
            {
                parts.Add("default=" + FuseModSettingsStore.FormatValue(setting.Default));
            }

            if (setting?.ReloadRequired == true)
            {
                parts.Add("reload required");
            }

            if (!string.IsNullOrWhiteSpace(setting?.Description))
            {
                parts.Add(setting.Description);
            }

            return string.Join(" | ", parts.ToArray());
        }
    }
}
