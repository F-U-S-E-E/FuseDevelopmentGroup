using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Loading;
using Newtonsoft.Json.Linq;
using Railloader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UI.Builder;
using UI.Common;
using UnityEngine;

namespace FUSE.Interface.MenuWindow
{
    internal struct ModsPanelBuilder
    {
        public static void Build(UIPanelBuilder builder, UIState<string> selectedItem)
        {
            var manifests = MergeHostedLegacyPackageSnapshots(
                FuseDataPackageDiscovery.GetPackageManifestSnapshots(),
                FuseLegacyAssemblyHost.EnumerateAllHostedPlugins().Select(info => info.Manifest));

            List<UIPanelBuilder.ListItem<FusePackageManifestSnapshot>> list = manifests
                .OrderBy(m => m.DisplayName)
                .Select(m => new UIPanelBuilder.ListItem<FusePackageManifestSnapshot>(m.Id, m, "Active Mods", m.DisplayName))
                .ToList();

            builder.AddListDetail(list, selectedItem, delegate (UIPanelBuilder builder, FusePackageManifestSnapshot manifest)
            {
                if (manifest == null)
                {
                    // With nothing selected there is no visible legacy tab; a
                    // previously selected mod's handler must not stay open.
                    CloseAllLegacyModTabs("mods selection cleared");
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

        internal static IReadOnlyList<FusePackageManifestSnapshot> MergeHostedLegacyPackageSnapshots(
            IEnumerable<FusePackageManifestSnapshot> dataPackageSnapshots,
            IEnumerable<FuseLegacyAssemblyManifest> hostedLegacyManifests)
        {
            var manifests = (dataPackageSnapshots ?? Enumerable.Empty<FusePackageManifestSnapshot>())
                .Where(manifest => manifest != null)
                .ToList();
            var knownFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var manifest in manifests)
            {
                AddPackageIdentity(knownFolders, knownIds, manifest.FolderPath, manifest.Id);
            }

            foreach (var hosted in hostedLegacyManifests ?? Enumerable.Empty<FuseLegacyAssemblyManifest>())
            {
                if (hosted == null)
                {
                    continue;
                }

                var normalizedFolder = NormalizePath(hosted.FolderPath);
                var folderName = normalizedFolder.Length == 0 ? string.Empty : Path.GetFileName(normalizedFolder);
                var id = string.IsNullOrWhiteSpace(hosted.Id) ? folderName : hosted.Id.Trim();
                if ((normalizedFolder.Length > 0 && knownFolders.Contains(normalizedFolder)) ||
                    (id.Length > 0 && knownIds.Contains(id)) ||
                    (normalizedFolder.Length == 0 && id.Length == 0))
                {
                    continue;
                }

                var folderPath = hosted.FolderPath ?? string.Empty;
                manifests.Add(new FusePackageManifestSnapshot
                {
                    Order = manifests.Count + 1,
                    Id = id,
                    DisplayName = string.IsNullOrWhiteSpace(hosted.Name) ? id : hosted.Name.Trim(),
                    Version = hosted.Version ?? string.Empty,
                    FolderName = folderName,
                    FolderPath = folderPath,
                    IsLegacyHosted = true
                });
                AddPackageIdentity(knownFolders, knownIds, folderPath, id);
            }

            return manifests;
        }

        private static void AddPackageIdentity(
            ISet<string> knownFolders,
            ISet<string> knownIds,
            string folderPath,
            string id)
        {
            var normalizedFolder = NormalizePath(folderPath);
            if (normalizedFolder.Length > 0)
            {
                knownFolders.Add(normalizedFolder);
            }

            var normalizedId = (id ?? string.Empty).Trim();
            if (normalizedId.Length > 0)
            {
                knownIds.Add(normalizedId);
            }
        }

        private static void BuildModDetail(UIPanelBuilder builder, FusePackageManifestSnapshot manifest)
        {
            builder.AddSection(manifest.DisplayName);

            builder.AddField("Status", PackageStatusTextMarkup(manifest));
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
                    Toast.Present("Copied mod info to clipboard");
                    builder.Rebuild();
                });
            });

            BuildFuseDeclaredSettings(builder, definitions);
            BuildLegacyModTabSection(builder, manifest);
        }

        private static void BuildFuseDeclaredSettings(UIPanelBuilder builder, FuseLoadedMod[] definitions)
        {
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

        // Tracks legacy IModTabHandler plugin instances whose settings tab is
        // currently rendered in the Mods detail. Key is
        // "{packageId}|{pluginTypeFullName}"; the plugin reference is held until
        // we call ModTabDidClose on it. This honours the Railloader contract:
        // ModTabDidOpen runs on every rebuild while the tab is visible, and
        // ModTabDidClose runs exactly once when the tab stops being visible —
        // plugins like NotEnoughRosters persist their settings from that hook.
        private static readonly Dictionary<string, IModTabHandler> _openLegacyTabHandlers =
            new(StringComparer.OrdinalIgnoreCase);

        internal static bool HasOpenLegacyModTabs => _openLegacyTabHandlers.Count > 0;

        /// <summary>
        /// Renders the selected mod's legacy Railloader settings tab(s), if any
        /// of its hosted plugins declare one, and closes handlers that belonged
        /// to a previously selected mod. <see cref="FuseMenuWindow"/> closes the
        /// remainder when the Mods detail stops being visible entirely.
        /// </summary>
        private static void BuildLegacyModTabSection(UIPanelBuilder builder, FusePackageManifestSnapshot manifest)
        {
            var handlers = FuseLegacyAssemblyHost
                .EnumerateHostedPlugins(manifest.FolderPath, manifest.Id)
                .Where(info => info.Plugin is IModTabHandler)
                .OrderBy(info => info.PluginType?.FullName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Selecting a different mod is a navigate-away for whatever was open.
            var keepSignatures = new HashSet<string>(
                handlers.Select(info => BuildLegacyTabHandlerSignature(info.Manifest, info.PluginType)),
                StringComparer.OrdinalIgnoreCase);
            CloseLegacyModTabsExcept(keepSignatures, "mods selection changed");

            if (handlers.Length == 0)
            {
                return;
            }

            builder.AddSection("Legacy Settings");
            foreach (var info in handlers)
            {
                if (handlers.Length > 1)
                {
                    builder.AddLabel(info.PluginType?.Name ?? "(unnamed plugin)");
                }

                var handler = (IModTabHandler)info.Plugin;
                _openLegacyTabHandlers[BuildLegacyTabHandlerSignature(info.Manifest, info.PluginType)] = handler;
                try
                {
                    handler.ModTabDidOpen(builder);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception(
                        $"Legacy plugin '{info.PluginType?.FullName}' threw from ModTabDidOpen while FUSE was rendering its settings tab",
                        ex);
                    builder.AddField("Plugin Error",
                        $"{info.PluginType?.Name ?? "(unnamed plugin)"} threw {ex.GetType().Name} from ModTabDidOpen: {ex.GetBaseException().Message}");
                }
            }
        }

        internal static void CloseAllLegacyModTabs(string reason)
        {
            CloseLegacyModTabsExcept(null, reason);
        }

        /// <summary>
        /// Calls ModTabDidClose on any tracked handler whose signature is NOT in
        /// <paramref name="keepSignatures"/>, and forgets it. Exceptions are
        /// logged but never bubble — a misbehaving plugin must not break FUSE's
        /// UI teardown.
        /// </summary>
        private static void CloseLegacyModTabsExcept(HashSet<string> keepSignatures, string reason)
        {
            if (_openLegacyTabHandlers.Count == 0)
            {
                return;
            }

            var toClose = _openLegacyTabHandlers
                .Where(pair => keepSignatures == null || !keepSignatures.Contains(pair.Key))
                .ToArray();
            foreach (var entry in toClose)
            {
                _openLegacyTabHandlers.Remove(entry.Key);
                try
                {
                    entry.Value?.ModTabDidClose();
                }
                catch (Exception ex)
                {
                    FuseLog.Exception(
                        $"Legacy plugin handler '{entry.Key}' threw from ModTabDidClose ({reason})",
                        ex);
                }
            }
        }

        private static string BuildLegacyTabHandlerSignature(FuseLegacyAssemblyManifest manifest, Type pluginType)
        {
            var packageKey = manifest == null
                ? string.Empty
                : (manifest.Id ?? manifest.FolderPath ?? string.Empty);
            var typeKey = pluginType == null ? string.Empty : (pluginType.FullName ?? pluginType.Name ?? string.Empty);
            return packageKey + "|" + typeKey;
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
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(value.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return value.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
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

            return manifest.IsLegacyConverted || manifest.IsLegacyHosted ? "ready | legacy" : "ready";
        }

        private static string PackageStatusTextMarkup(FusePackageManifestSnapshot manifest)
        {
            if (manifest == null)
            {
                return "<color=\"orange\">Unknown";
            }

            if (manifest.Disabled)
            {
                return "<color=\"yellow\">Disabled";
            }

            if (manifest.Faults.Length > 0)
            {
                return "<color=\"red\">Error: " + manifest.Faults.Length + " fault(s)";
            }

            return manifest.IsLegacyConverted || manifest.IsLegacyHosted
                ? "<color=\"green\">Ready</color> - Legacy"
                : "<color=\"green\"Ready";
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
