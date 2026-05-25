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

        private void BuildPackagesContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 150f;
            builder.Spacing = 6f;

            var multiplayer = FuseMultiplayerGuard.GetStatus();
            var manifests = FuseDataPackageDiscovery.GetPackageManifestSnapshots();
            var selected = ResolveSelectedPackage(manifests);

            builder.AddSection("Mod Browser");
            AddValueField(builder, "Profile", FuseModSetService.ActiveSetName);
            AddValueField(builder, "Profile Hash", multiplayer.LocalPackageFingerprint);
            AddWrappedField(builder, "Packages", multiplayer.LocalPackageSummary, 38f);
            AddWrappedField(builder, "Selected", selected == null ? "No package selected." : PackageDisplayName(selected), 34f);
            builder.Spacer(4f);

            if (manifests.Count == 0)
            {
                AddValueField(builder, "Packages", "No FUSE data packages discovered.");
                builder.Spacer(8f);
                return;
            }

            BuildPackageSelector(builder, manifests, selected);
            builder.Spacer(6f);
            BuildSelectedPackagePage(builder, selected);

            if (FuseSettings.ShowAdvancedHealthDetails)
            {
                builder.Spacer(4f);
                builder.AddSection("Dependency Graph");
                BuildDependencyGraph(builder, manifests);
            }

            builder.Spacer(8f);
        }

        private FusePackageManifestSnapshot ResolveSelectedPackage(IReadOnlyList<FusePackageManifestSnapshot> manifests)
        {
            if (manifests == null || manifests.Count == 0)
            {
                _selectedPackageId = string.Empty;
                return null;
            }

            var selected = manifests.FirstOrDefault(manifest =>
                string.Equals(manifest.Id, _selectedPackageId, StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                return selected;
            }

            selected = manifests.FirstOrDefault(manifest => manifest.Faults.Length > 0)
                ?? manifests.FirstOrDefault(HasPackageSettings)
                ?? manifests.FirstOrDefault(manifest => !manifest.Disabled)
                ?? manifests[0];
            _selectedPackageId = selected.Id ?? string.Empty;
            return selected;
        }

        private void BuildPackageSelector(
            UIPanelBuilder builder,
            IReadOnlyList<FusePackageManifestSnapshot> manifests,
            FusePackageManifestSnapshot selected)
        {
            builder.AddSection("Packages");
            var rowsShown = 0;
            foreach (var manifest in manifests)
            {
                if (!FuseSettings.ShowAdvancedHealthDetails && rowsShown >= 18)
                {
                    continue;
                }

                rowsShown++;
                var captured = manifest;
                var isSelected = selected != null && string.Equals(selected.Id, manifest.Id, StringComparison.OrdinalIgnoreCase);
                builder.HStack(row =>
                {
                    row.AddButtonCompact(isSelected ? "[ " + TrimPackageLabel(PackageDisplayName(captured), 34) + " ]" : TrimPackageLabel(PackageDisplayName(captured), 38), () =>
                    {
                        _selectedPackageId = captured.Id ?? string.Empty;
                        RebuildWindow();
                    });
                    row.AddLabel(PackageStatusText(captured), text =>
                    {
                        text.enableWordWrapping = false;
                        text.overflowMode = TextOverflowModes.Ellipsis;
                        text.alignment = TextAlignmentOptions.Left;
                    });
                }, 6f).Height(30f);
            }

            if (!FuseSettings.ShowAdvancedHealthDetails && manifests.Count > rowsShown)
            {
                AddWrappedField(builder, "More", (manifests.Count - rowsShown) + " hidden. Enable Advanced Details to show the full package list.", 38f);
            }
        }

        private void BuildSelectedPackagePage(UIPanelBuilder builder, FusePackageManifestSnapshot manifest)
        {
            builder.AddSection("Selected Mod");
            if (manifest == null)
            {
                AddValueField(builder, "Status", "No package selected.");
                return;
            }

            var definitions = GetLoadedDefinitionsForPackage(manifest);
            AddWrappedLabel(builder, PackageDisplayName(manifest), 34f);
            AddValueField(builder, "Status", PackageStatusText(manifest));
            AddValueField(builder, "Version", BlankAs(manifest.Version, "?"));
            AddWrappedField(builder, "Id", manifest.Id, 34f);
            AddWrappedField(builder, "Folder", manifest.FolderName, 34f);
            AddValueField(builder, "Definitions", definitions.Length.ToString());
            AddValueField(builder, "Settings", CountPackageSettings(definitions).ToString());

            if (manifest.Faults.Length > 0)
            {
                AddWrappedField(builder, "Faults", string.Join("; ", manifest.Faults), 54f);
            }

            if (manifest.Disabled && !string.IsNullOrWhiteSpace(manifest.DisabledReason))
            {
                AddWrappedField(builder, "Disabled", manifest.DisabledReason, 44f);
            }

            var deps = BuildDependencySummary(manifest);
            if (!string.IsNullOrWhiteSpace(deps))
            {
                AddWrappedField(builder, "Dependencies", deps, 42f);
            }

            builder.HStack(row =>
            {
                row.AddButtonCompact("Copy Mod Info", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildSelectedPackageReport(manifest, definitions);
                    _lastAction = "Copied selected mod information to clipboard.";
                    RebuildWindow();
                });
                row.AddButtonCompact("Issues", () => SetPage(Page.Logs));
                row.AddButtonCompact("Refresh", RebuildWindow);
            }, 6f).Height(32f);

            builder.Spacer(6f);
            builder.AddSection("Mod Settings");
            if (definitions.Length == 0)
            {
                AddWrappedField(builder, "Settings", "This package is not loaded, so no runtime settings definition is available.", 44f);
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
                    AddWrappedLabel(builder, loaded.Definition.Id, 30f);
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
                AddWrappedField(
                    builder,
                    "Settings",
                    CountPackageSettings(definitions) == 0
                        ? "This mod does not declare FUSE settings."
                        : "Only advanced settings are declared. Enable Advanced Details to show them.",
                    44f);
            }

            // Legacy-loader plugin settings (IModTabHandler) live on their own
            // tab — see Page.LegacyMods / BuildLegacyModsContent. We keep the
            // per-package settings panel focused on FUSE-native settings so
            // legacy plugin UIs do not double-render here.
        }

        private static FuseLoadedMod[] GetLoadedDefinitionsForPackage(FusePackageManifestSnapshot manifest)
        {
            if (manifest == null)
            {
                return Array.Empty<FuseLoadedMod>();
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
            return (definitions ?? Enumerable.Empty<FuseLoadedMod>())
                .Where(loaded => loaded?.Definition?.Settings != null)
                .Sum(loaded => loaded.Definition.Settings.Count);
        }

        private static bool HasPackageSettings(FusePackageManifestSnapshot manifest)
        {
            return CountPackageSettings(GetLoadedDefinitionsForPackage(manifest)) > 0;
        }

        private static string BuildSelectedPackageReport(FusePackageManifestSnapshot manifest, FuseLoadedMod[] definitions)
        {
            var builder = new StringBuilder();
            builder.AppendLine("FUSE selected mod");
            builder.AppendLine("Name: " + PackageDisplayName(manifest));
            builder.AppendLine("Id: " + manifest.Id);
            builder.AppendLine("Version: " + BlankAs(manifest.Version, "?"));
            builder.AppendLine("Status: " + PackageStatusText(manifest));
            builder.AppendLine("Folder: " + manifest.FolderPath);
            builder.AppendLine("Definitions: " + (definitions?.Length ?? 0));
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

        private static string TrimPackageLabel(string value, int maxLength)
        {
            value = value ?? string.Empty;
            if (value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, Math.Max(1, maxLength - 3)) + "...";
        }

        private static string NormalizePath(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
    }
}
