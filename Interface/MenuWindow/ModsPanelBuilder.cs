using FUSE.Data;
using FUSE.Infrastructure;
using FUSE.Loading;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UI.Builder;
using UnityEngine;

namespace FUSE.Interface.MenuWindow
{
    internal struct ModsPanelBuilder
    {
        public static void Build(UIPanelBuilder builder, UIState<string> selectedItem)
        {
            var manifests = FuseDataPackageDiscovery.GetPackageManifestSnapshots();

            List<UIPanelBuilder.ListItem<FusePackageManifestSnapshot>> list = manifests
                .OrderByDescending(m => m.DisplayName)
                .Select(m => new UIPanelBuilder.ListItem<FusePackageManifestSnapshot>(m.Id, m, "Active Mods", m.DisplayName))
                .ToList();

            builder.AddListDetail(list, selectedItem, delegate (UIPanelBuilder builder, FusePackageManifestSnapshot manifest)
            {
                if (manifest == null)
                {
                    builder.AddExpandingVerticalSpacer();
                    builder.AddLabelEmptyState(manifests.Count == 0 ? "No mods found through UMM." : "Select a mod");
                    builder.AddExpandingVerticalSpacer();
                }
                else
                {
                    builder.VScrollView(b => BuildModDetail(b, manifest));
                }
            });
        }

        private static void BuildModDetail(UIPanelBuilder builder, FusePackageManifestSnapshot manifest)
        {
            builder.AddSection(manifest.DisplayName);

            builder.AddField("Status", PackageStatusText(manifest));
            builder.AddField("Version", manifest.Version ?? "Unknown");
            builder.AddField("Id", manifest.Id);
            builder.AddField("Folder", manifest.FolderName);

            var definitions = GetLoadedDefinitionsForPackage(manifest);
            builder.AddField("Definitions", definitions.Length.ToString());

            builder.AddField("Settings", CountPackageSettings(definitions).ToString());

            if (manifest.Faults.Length > 0)
            {
                builder.AddField("Faults", string.Join("; ", manifest.Faults));
            }

            if (manifest.Disabled && !string.IsNullOrEmpty(manifest.DisabledReason))
            {
                builder.AddField("Disabled", manifest.DisabledReason);
            }

            var depSummary = BuildDependencySummary(manifest);

            if (!string.IsNullOrEmpty(depSummary))
            {
                builder.AddField("Dependencies", depSummary);
            }


            builder.ButtonStrip(row =>
            {
                row.AddButton("Copy Mod Info", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildSelectedPackageReport(manifest, definitions);
                    builder.Rebuild();
                });
            });

            builder.AddSection("Mod Settings");
            if (definitions.Length == 0)
            {
                builder.AddField("Settings", "This package is not loaded, so no runtime settings definition is available.");
                return;
            }

            var rendered = 0;
            foreach (var loaded in definitions)
            {
                if (loaded.Definition?.Settings == null || loaded.Definition.Settings.Count == 0)
                {
                    continue;
                }

                if (definitions.Length > 1)
                {
                    builder.AddLabel(loaded.Definition.Id);
                }

                foreach (var pair in loaded.Definition.Settings
                    .Where(pair => pair.Value != null)
                    .Where(pair => FuseSettings.ShowAdvancedHealthDetails || !pair.Value.Advanced)
                    .OrderBy(pair => GetSettingLabel(pair.Key, pair.Value), StringComparer.OrdinalIgnoreCase))
                {
                    BuildPackageSettingControl(builder, loaded.Definition, pair.Key, pair.Value);
                    rendered++;
                }
            }

            if (rendered == 0)
            {
                builder.AddField("Settings",
                    CountPackageSettings(definitions) == 0
                        ? "This mod does not declare FUSE settings."
                        : "Only advanced settings are declared. Enable Advanced Details to show them.");
            }
        }

        private static FuseLoadedMod[] GetLoadedDefinitionsForPackage(FusePackageManifestSnapshot manifest)
        {
            if (manifest == null)
            {
                return [];
            }

            return FuseModLoader.GetLoadedModsInOrder()
                .Where(loaded => loaded?.Definition != null)
                .Where(loaded =>
                    string.Equals(NormalizePath(loaded.FolderPath), NormalizePath(manifest.FolderPath), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(loaded.Definition.Id, manifest.Id, StringComparison.OrdinalIgnoreCase) ||
                    loaded.Definition.Id.StartsWith((manifest.Id ?? string.Empty) + ".", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        private static int CountPackageSettings(IEnumerable<FuseLoadedMod> definitions)
        {
            return (definitions ?? [])
                .Where(loaded => loaded?.Definition?.Settings != null)
                .Sum(loaded => loaded.Definition.Settings.Count);
        }

        private static string BuildDependencySummary(FusePackageManifestSnapshot manifest)
        {
            if (manifest == null)
            {
                return string.Empty;
            }

            var parts = new[]
            {
                manifest.LoadAfter.Length == 0 ? string.Empty : "after: " + string.Join(", ", manifest.LoadAfter),
                manifest.LoadBefore.Length == 0 ? string.Empty : "before: " + string.Join(", ", manifest.LoadBefore)
            }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();

            return parts.Length == 0 ? string.Empty : "dependencies | " + string.Join(" | ", parts);
        }

        private static string NormalizePath(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string BuildSelectedPackageReport(FusePackageManifestSnapshot manifest, IReadOnlyList<FuseLoadedMod> definitions)
        {
            var builder = new StringBuilder();
            builder.AppendLine("FUSE selected mod");
            builder.AppendLine("Name: " + PackageDisplayName(manifest));
            builder.AppendLine("Id: " + manifest.Id);
            builder.AppendLine("Version: " + BlankAs(manifest.Version, "?"));
            builder.AppendLine("Status: " + PackageStatusText(manifest));
            builder.AppendLine("Folder: " + manifest.FolderPath);
            builder.AppendLine("Definitions: " + (definitions?.Count ?? 0));
            builder.AppendLine("Settings: " + CountPackageSettings(definitions));
            if (manifest.Faults.Length > 0)
            {
                builder.AppendLine("Faults: " + string.Join("; ", manifest.Faults));
            }

            foreach (var loaded in definitions ?? Array.Empty<FuseLoadedMod>())
            {
                builder.AppendLine("Definition: " + loaded.Definition.Id);
            }

            return builder.ToString();
        }

        private static string PackageDisplayName(FusePackageManifestSnapshot manifest)
        {
            if (manifest == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(manifest.DisplayName) ? manifest.Id : manifest.DisplayName;
        }

        private static string BlankAs(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string PackageStatusText(FusePackageManifestSnapshot manifest)
        {
            if (manifest == null)
            {
                return "unknown";
            }

            if (manifest.Disabled)
            {
                return "disabled";
            }

            if (manifest.Faults.Length > 0)
            {
                return manifest.Faults.Length + " fault(s)";
            }

            return manifest.IsLegacyConverted ? "ready | legacy" : "ready";
        }

        private static string GetSettingLabel(string key, FuseModSettingDefinition setting)
        {
            return string.IsNullOrWhiteSpace(setting?.Label) ? key : setting.Label.Trim();
        }

        private static void BuildPackageSettingControl(UIPanelBuilder builder, FuseModDefinition definition, string key, FuseModSettingDefinition setting)
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
                builder.AddField(" ", DescribePackageSetting(key, setting));
            }
            else if (!string.IsNullOrWhiteSpace(setting?.Description))
            {
                builder.AddField(" ", setting.Description);
            }
        }

        private static void BuildBoolSettingControl(UIPanelBuilder builder, FuseModDefinition definition, string key, FuseModSettingDefinition setting)
        {
            builder.HStack(row =>
            {
                AddSettingRowLabel(row, GetSettingLabel(key, setting));
                row.AddToggle(
                    () => FuseModSettingsStore.GetBoolValue(definition, key, setting),
                    value =>
                    {
                        FuseModSettingsStore.SetValue(definition, key, setting, new JValue(value));
                        //RebuildWindow();
                    });
                AddResetSettingButton(row, definition, key, setting);
            }, 8f).Height(30f);
        }

        private static void BuildEnumSettingControl(UIPanelBuilder builder, FuseModDefinition definition, string key, FuseModSettingDefinition setting)
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
                    //RebuildWindow();
                });
                AddResetSettingButton(row, definition, key, setting);
            }, 8f).Height(32f);
        }

        private static void BuildTextSettingControl(UIPanelBuilder builder, FuseModDefinition definition, string key, FuseModSettingDefinition setting, bool isNumber)
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

        private static void SaveTextSetting(FuseModDefinition definition, string key, FuseModSettingDefinition setting, string value, bool isNumber)
        {
            if (isNumber)
            {
                double parsed;
                if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                {
                    //_lastAction = $"Setting '{key}' was not saved because '{value}' is not a number.";
                    return;
                }

                FuseModSettingsStore.SetValue(definition, key, setting, new JValue(parsed));
                return;
            }

            FuseModSettingsStore.SetValue(definition, key, setting, new JValue(value ?? string.Empty));
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

        private static void AddResetSettingButton(UIPanelBuilder row, FuseModDefinition definition, string key, FuseModSettingDefinition setting)
        {
            row.AddButtonCompact("Reset", () =>
            {
                FuseModSettingsStore.ResetValue(definition, key, setting);
                //RebuildWindow();
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
