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
    internal sealed class FusePackageDisplayStatus
    {
        public FusePackageDisplayStatus(string label, string detail, string listGroup, int sortOrder, string markup)
        {
            Label = label ?? string.Empty;
            Detail = detail ?? string.Empty;
            ListGroup = listGroup ?? string.Empty;
            SortOrder = sortOrder;
            Markup = markup ?? string.Empty;
        }

        public string Label { get; }
        public string Detail { get; }
        public string ListGroup { get; }
        public int SortOrder { get; }
        public string Markup { get; }
    }

    internal struct ModsPanelBuilder
    {
        public static void Build(UIPanelBuilder builder, UIState<string> selectedItem)
        {
            var manifests = MergeHostedLegacyPackageSnapshots(
                FuseDataPackageDiscovery.GetPackageManifestSnapshots(),
                FuseLegacyAssemblyHost.EnumerateAllHostedPlugins().Select(info => info.Manifest))
                .ToArray();
            HydratePackageRuntimeState(manifests);

            List<UIPanelBuilder.ListItem<FusePackageManifestSnapshot>> list = manifests
                .OrderBy(m => ClassifyPackageStatus(m).SortOrder)
                .ThenBy(m => m.DisplayName)
                .Select(m => new UIPanelBuilder.ListItem<FusePackageManifestSnapshot>(
                    m.Id,
                    m,
                    ClassifyPackageStatus(m).ListGroup,
                    m.DisplayName))
                .ToList();

            builder.AddListDetail(list, selectedItem, delegate (UIPanelBuilder builder, FusePackageManifestSnapshot manifest)
            {
                if (manifest == null)
                {
                    // With nothing selected there is no visible legacy tab; a
                    // previously selected mod's handler must not stay open.
                    CloseAllLegacyModTabs("mods selection cleared");
                    builder.AddExpandingVerticalSpacer();
                    builder.AddLabelEmptyState(manifests.Length == 0 ? "No mods found through UMM." : "Select a mod");
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
                    RequiredPackageIds = (hosted.RequiredReferences ?? Array.Empty<Railloader.ModReference>())
                        .Select(reference => reference.Id)
                        .Where(referenceId => !string.IsNullOrWhiteSpace(referenceId))
                        .ToArray(),
                    LoadAfter = (hosted.LoadAfter ?? Array.Empty<Railloader.ModReference>())
                        .Select(reference => reference.Id)
                        .Where(referenceId => !string.IsNullOrWhiteSpace(referenceId))
                        .ToArray(),
                    LoadBefore = hosted.LoadBefore ?? Array.Empty<string>(),
                    ConflictsWith = (hosted.ConflictsWith ?? Array.Empty<Railloader.ModReference>())
                        .Select(reference => new FUSE.Authoring.Data.FuseModRequirement
                        {
                            Id = reference.Id,
                            NotBefore = reference.NotBefore?.ToString(),
                            NotAfter = reference.NotAfter?.ToString()
                        })
                        .ToArray(),
                    IsLegacyHosted = true,
                    LoadedFromDisk = true,
                    AppliedToRuntime = true
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

            var status = ClassifyPackageStatus(manifest);
            builder.AddField("Status", status.Markup);
            if (!string.IsNullOrWhiteSpace(status.Detail))
            {
                builder.AddField("State details", status.Detail);
            }
            builder.AddField("Version", manifest.Version ?? "Unknown");
            builder.AddField("Id", manifest.Id);
            builder.AddField("Folder", manifest.FolderName);

            var definitions = GetLoadedDefinitionsForPackage(manifest);
            builder.AddField("Definitions", definitions.Length.ToString());

            builder.AddField("Settings", CountPackageSettings(definitions).ToString());

            var faults = GetAllFaults(manifest);
            if (faults.Length > 0)
            {
                builder.AddField("Faults", string.Join("; ", faults));
            }

            if (manifest.Disabled && !string.IsNullOrEmpty(manifest.DisabledReason))
            {
                builder.AddField("Disabled", manifest.DisabledReason);
            }

            var skipReasons = GetSkipReasons(manifest);
            if (skipReasons.Length > 0)
            {
                var label = skipReasons.All(FusePackageFaultRegistry.IsOptionalSkipReason)
                    ? "Optional content"
                    : "Skipped";
                builder.AddField(label, string.Join("; ", skipReasons));
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
                    GUIUtility.systemCopyBuffer = BuildSelectedPackageReport(manifest, definitions, status);
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

                if (loaded.FeatureEvaluation.RuleCount > 0)
                {
                    builder.AddField("Active feature set", loaded.FeatureEvaluation.Summary);
                    if (loaded.FeatureEvaluation.DisabledRuleIds.Length > 0)
                    {
                        builder.AddField(
                            "Disabled options",
                            string.Join(", ", loaded.FeatureEvaluation.DisabledRuleIds));
                    }
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

        private static void HydratePackageRuntimeState(IReadOnlyList<FusePackageManifestSnapshot> manifests)
        {
            if (manifests == null || manifests.Count == 0)
            {
                return;
            }

            var loadedIds = new HashSet<string>(
                FusePackageFaultRegistry.GetLoadedPackageIds(),
                StringComparer.OrdinalIgnoreCase);
            var appliedIds = new HashSet<string>(
                FusePackageFaultRegistry.GetAppliedPackageIds(),
                StringComparer.OrdinalIgnoreCase);
            var skipped = FusePackageFaultRegistry.GetSkippedPackages();
            var disabled = FusePackageFaultRegistry.GetDisabledPackages();
            var faultsByPackage = FusePackageFaultRegistry.GetFaults()
                .GroupBy(fault => fault.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(FormatRuntimeFault).ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            var loadedDefinitions = FuseModLoader.GetLoadedModsInOrder()
                .Where(loaded => loaded?.Definition != null)
                .ToArray();

            foreach (var manifest in manifests.Where(manifest => manifest != null))
            {
                var definitionIds = loadedDefinitions
                    .Where(loaded => DefinitionBelongsToPackage(loaded, manifest))
                    .Select(loaded => loaded.Definition.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToArray();
                var registryIds = new[] { manifest.Id }
                    .Concat(definitionIds)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                manifest.LoadedFromDisk = manifest.LoadedFromDisk || registryIds.Any(loadedIds.Contains);
                manifest.AppliedToRuntime = manifest.AppliedToRuntime || registryIds.Any(appliedIds.Contains);

                var skipReasons = registryIds
                    .Select(id => skipped.TryGetValue(id, out var reason) ? reason : string.Empty)
                    .Concat(manifest.SkipReasons ?? Array.Empty<string>())
                    .Concat(string.IsNullOrWhiteSpace(manifest.SkipReason)
                        ? Array.Empty<string>()
                        : new[] { manifest.SkipReason })
                    .Where(reason => !string.IsNullOrWhiteSpace(reason))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                manifest.SkipReasons = skipReasons;
                manifest.SkipReason = skipReasons.FirstOrDefault() ?? string.Empty;

                if (!manifest.Disabled)
                {
                    var disabledReason = registryIds
                        .Select(id => disabled.TryGetValue(id, out var reason) ? reason : string.Empty)
                        .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
                    if (!string.IsNullOrWhiteSpace(disabledReason))
                    {
                        manifest.Disabled = true;
                        manifest.DisabledReason = disabledReason;
                    }
                }

                manifest.RuntimeFaults = registryIds
                    .SelectMany(id => faultsByPackage.TryGetValue(id, out var packageFaults)
                        ? packageFaults
                        : Array.Empty<string>())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }
        }

        private static bool DefinitionBelongsToPackage(FuseLoadedMod loaded, FusePackageManifestSnapshot manifest)
        {
            return loaded?.Definition != null && manifest != null &&
                   (string.Equals(NormalizePath(loaded.FolderPath), NormalizePath(manifest.FolderPath), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(loaded.Definition.Id, manifest.Id, StringComparison.OrdinalIgnoreCase) ||
                    loaded.Definition.Id.StartsWith((manifest.Id ?? string.Empty) + ".", StringComparison.OrdinalIgnoreCase));
        }

        private static string FormatRuntimeFault(FusePackageFault fault)
        {
            if (fault == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(fault.Stage)
                ? fault.Message
                : fault.Stage + ": " + fault.Message;
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
                manifest.RequiredPackageIds.Length == 0 ? string.Empty : "requires: " + string.Join(", ", manifest.RequiredPackageIds),
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

        private static string BuildSelectedPackageReport(
            FusePackageManifestSnapshot manifest,
            IReadOnlyList<FuseLoadedMod> definitions,
            FusePackageDisplayStatus status)
        {
            var builder = new StringBuilder();
            builder.AppendLine("FUSE selected mod");
            builder.AppendLine("Name: " + PackageDisplayName(manifest));
            builder.AppendLine("Id: " + manifest.Id);
            builder.AppendLine("Version: " + BlankAs(manifest.Version, "?"));
            builder.AppendLine("Status: " + status.Label);
            if (!string.IsNullOrWhiteSpace(status.Detail))
            {
                builder.AppendLine("State details: " + status.Detail);
            }
            builder.AppendLine("Folder: " + manifest.FolderPath);
            builder.AppendLine("Definitions: " + (definitions?.Count ?? 0));
            builder.AppendLine("Settings: " + CountPackageSettings(definitions));
            var skipReasons = GetSkipReasons(manifest);
            if (skipReasons.Length > 0)
            {
                builder.AppendLine("Skip reasons: " + string.Join("; ", skipReasons));
            }

            var faults = GetAllFaults(manifest);
            if (faults.Length > 0)
            {
                builder.AppendLine("Faults: " + string.Join("; ", faults));
            }

            var definitionIds = (definitions ?? Array.Empty<FuseLoadedMod>())
                .Where(loaded => loaded?.Definition != null)
                .Select(loaded => loaded.Definition.Id)
                .ToArray();
            var detailedFaults = FusePackageFaultRegistry.GetFaults()
                .Where(fault => string.Equals(fault.PackageId, manifest.Id, StringComparison.OrdinalIgnoreCase)
                                || definitionIds.Contains(fault.PackageId, StringComparer.OrdinalIgnoreCase)
                                || (!string.IsNullOrWhiteSpace(fault.FolderPath)
                                    && string.Equals(NormalizePath(fault.FolderPath), NormalizePath(manifest.FolderPath), StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (detailedFaults.Length > 0)
            {
                builder.AppendLine("Actionable package diagnostics:");
                foreach (var fault in detailedFaults)
                {
                    builder.AppendLine("- Stage: " + BlankAs(fault.Stage, "unknown"));
                    builder.AppendLine("  Message: " + BlankAs(fault.Message, "unknown"));
                    builder.AppendLine("  Package: " + BlankAs(fault.PackageName, fault.PackageId)
                                       + " [" + fault.PackageId + "]");
                    builder.AppendLine("  Source root: " + BlankAs(fault.FolderPath, "unknown"));
                    builder.AppendLine("  Relative file: " + BlankAs(fault.RelativeSourceFile, "unknown"));
                    builder.AppendLine("  Absolute file: " + BlankAs(fault.SourceFile, "unknown"));
                    if (!string.IsNullOrWhiteSpace(fault.JsonPath) || fault.LineNumber > 0)
                    {
                        builder.AppendLine("  JSON location: " + BlankAs(fault.JsonPath, "<root>")
                                           + (fault.LineNumber > 0
                                               ? $" line {fault.LineNumber}, position {fault.LinePosition}"
                                               : string.Empty));
                    }
                    if (!string.IsNullOrWhiteSpace(fault.ValidationCode))
                        builder.AppendLine("  Validation code: " + fault.ValidationCode);
                    if (!string.IsNullOrWhiteSpace(fault.ExpectedShape))
                        builder.AppendLine("  Expected: " + fault.ExpectedShape);
                    if (!string.IsNullOrWhiteSpace(fault.ReceivedValue))
                        builder.AppendLine("  Received: " + fault.ReceivedValue);
                    builder.AppendLine("  Fix: " + BlankAs(fault.SuggestedAction, "Correct the named package source and reload the map."));
                }
            }

            foreach (var loaded in definitions ?? Array.Empty<FuseLoadedMod>())
            {
                builder.AppendLine("Definition: " + loaded.Definition.Id);
                if (loaded.FeatureEvaluation.RuleCount > 0)
                {
                    builder.AppendLine("Feature rules: " + loaded.FeatureEvaluation.Summary);
                    if (loaded.FeatureEvaluation.DisabledRuleIds.Length > 0)
                    {
                        builder.AppendLine(
                            "Disabled feature rules: " +
                            string.Join(", ", loaded.FeatureEvaluation.DisabledRuleIds));
                    }
                }
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

        internal static FusePackageDisplayStatus ClassifyPackageStatus(FusePackageManifestSnapshot manifest)
        {
            if (manifest == null)
            {
                return new FusePackageDisplayStatus(
                    "Unknown",
                    "Package state is unavailable.",
                    "Unknown",
                    80,
                    "<color=\"orange\">Unknown</color>");
            }

            if (manifest.Disabled)
            {
                return new FusePackageDisplayStatus(
                    "Disabled",
                    BlankAs(manifest.DisabledReason, "Disabled by the active mod configuration."),
                    "Disabled Mods",
                    60,
                    "<color=\"yellow\">Disabled</color>");
            }

            var faults = GetAllFaults(manifest);
            var skipReasons = GetSkipReasons(manifest);
            var evidence = string.Join(
                " | ",
                faults
                    .Concat(skipReasons.Where(reason => !FusePackageFaultRegistry.IsOptionalSkipReason(reason)))
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            var problem = ClassifyProblem(evidence);
            var hasSkips = skipReasons.Length > 0;
            var optionalSkip = hasSkips && skipReasons.All(FusePackageFaultRegistry.IsOptionalSkipReason);

            if (manifest.AppliedToRuntime &&
                (faults.Length > 0 || (hasSkips && !optionalSkip)))
            {
                var actionableSkipDetail = string.Join(
                    "; ",
                    skipReasons.Where(reason => !FusePackageFaultRegistry.IsOptionalSkipReason(reason)));
                var detail = problem.Length == 0
                    ? (hasSkips ? string.Join("; ", skipReasons) : "One or more definitions failed while other content applied.")
                    : problem + ". Other content from this package applied successfully.";
                if (!string.IsNullOrWhiteSpace(actionableSkipDetail))
                {
                    detail += " Actionable skip: " + actionableSkipDetail;
                }
                return new FusePackageDisplayStatus(
                    "Partially applied",
                    detail,
                    "Mods Needing Attention",
                    20,
                    "<color=\"orange\">Partially applied</color>");
            }

            if (faults.Length > 0)
            {
                var label = problem.Length == 0 ? "Failed" : problem;
                return new FusePackageDisplayStatus(
                    label,
                    faults[0],
                    "Mods Needing Attention",
                    10,
                    "<color=\"red\">" + label + "</color>");
            }

            if (hasSkips && !optionalSkip)
            {
                var label = problem.Length == 0 ? "Skipped" : problem;
                return new FusePackageDisplayStatus(
                    label,
                    string.Join("; ", skipReasons),
                    "Mods Needing Attention",
                    30,
                    "<color=\"orange\">" + label + "</color>");
            }

            if (manifest.AppliedToRuntime)
            {
                var detail = optionalSkip
                    ? "Applied successfully. Optional content is inactive: " + string.Join("; ", skipReasons)
                    : string.Empty;
                var legacy = manifest.IsLegacyConverted || manifest.IsLegacyHosted
                    ? " - Legacy compatibility"
                    : string.Empty;
                return new FusePackageDisplayStatus(
                    "Applied",
                    detail,
                    "Applied Mods",
                    40,
                    "<color=\"green\">Applied</color>" + legacy);
            }

            if (manifest.LoadedFromDisk)
            {
                return new FusePackageDisplayStatus(
                    "Loaded; awaiting map apply",
                    optionalSkip ? "Optional content is inactive: " + string.Join("; ", skipReasons) : string.Empty,
                    "Ready Mods",
                    50,
                    "<color=\"green\">Loaded</color> - awaiting map apply");
            }

            if (manifest.IsLegacyHosted)
            {
                return new FusePackageDisplayStatus(
                    "Active legacy code",
                    string.Empty,
                    "Applied Mods",
                    40,
                    "<color=\"green\">Active</color> - Legacy compatibility");
            }

            return new FusePackageDisplayStatus(
                "Discovered; ready to load",
                optionalSkip ? "Optional content is inactive: " + string.Join("; ", skipReasons) : string.Empty,
                "Ready Mods",
                50,
                "<color=\"green\">Ready</color>");
        }

        private static string[] GetAllFaults(FusePackageManifestSnapshot manifest)
        {
            return (manifest?.Faults ?? Array.Empty<string>())
                .Concat(manifest?.RuntimeFaults ?? Array.Empty<string>())
                .Where(fault => !string.IsNullOrWhiteSpace(fault))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] GetSkipReasons(FusePackageManifestSnapshot manifest)
        {
            return (manifest?.SkipReasons ?? Array.Empty<string>())
                .Concat(string.IsNullOrWhiteSpace(manifest?.SkipReason)
                    ? Array.Empty<string>()
                    : new[] { manifest.SkipReason })
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static string ClassifyProblem(string evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence))
            {
                return string.Empty;
            }

            if (ContainsAny(evidence, "conflictswith", "conflicts with", "incompatible"))
            {
                return "Incompatible mod installed";
            }

            if (ContainsAny(
                evidence,
                "dependency missing",
                "no matching package",
                " requires '",
                "required package",
                "required dependency"))
            {
                return "Missing dependency";
            }

            if (ContainsAny(evidence, "manifest json", "json:", "json ", "deserial", "schema", "could not be parsed"))
            {
                return "Invalid package data";
            }

            return string.Empty;
        }

        private static bool ContainsAny(string value, params string[] candidates)
        {
            return candidates.Any(candidate => value.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0);
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
                    BuildNumberSettingControl(builder, definition, key, setting);
                    break;
                case "path":
                case "color":
                case "text":
                default:
                    BuildTextSettingControl(builder, definition, key, setting, isNumber: false);
                    break;
            }

            var featureRules = definition?.FeatureRules == null
                ? Enumerable.Empty<FuseFeatureRule>()
                : definition.FeatureRules.Values;
            var controlsFeature = featureRules
                .Any(rule => rule != null && string.Equals(rule.Setting, key, StringComparison.Ordinal));
            if (FuseSettings.ShowAdvancedHealthDetails)
            {
                builder.AddField(" ", DescribePackageSetting(key, setting));
            }
            else if (controlsFeature || !string.IsNullOrWhiteSpace(setting?.Description))
            {
                var note = string.IsNullOrWhiteSpace(setting?.Description)
                    ? string.Empty
                    : setting.Description.Trim() + " ";
                if (controlsFeature)
                {
                    note += "This option changes authored map content and takes effect after saving/reloading the map.";
                }
                builder.AddField(" ", note.Trim());
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

        private static void BuildNumberSettingControl(
            UIPanelBuilder builder,
            FuseModDefinition definition,
            string key,
            FuseModSettingDefinition setting)
        {
            if (!setting.Min.HasValue || !setting.Max.HasValue
                || setting.Min.Value >= setting.Max.Value)
            {
                BuildTextSettingControl(builder, definition, key, setting, isNumber: true);
                return;
            }

            var minimum = (float)setting.Min.Value;
            var maximum = (float)setting.Max.Value;
            var step = setting.Step.GetValueOrDefault(0d);
            builder.HStack(row =>
            {
                AddSettingRowLabel(row, GetSettingLabel(key, setting));
                row.AddSlider(
                    () => (float)FuseModSettingsStore.GetNumberValue(definition, key, setting),
                    () => FuseModSettingsStore.GetNumberValue(definition, key, setting)
                        .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                    value =>
                    {
                        var selected = Math.Max(minimum, Math.Min(maximum, value));
                        if (step > 0d)
                        {
                            selected = minimum
                                + (float)(Math.Round((selected - minimum) / step) * step);
                            selected = Math.Max(minimum, Math.Min(maximum, selected));
                        }
                        FuseModSettingsStore.SetValue(definition, key, setting, new JValue(selected));
                    },
                    minimum,
                    maximum);
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
