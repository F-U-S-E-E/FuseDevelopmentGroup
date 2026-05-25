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

        /// <summary>
        /// Renders the "Legacy Mods" page: a mod picker at the top, then the
        /// selected mod's <see cref="IModTabHandler"/> tab rendered into its
        /// own panel below. Only mods whose hosted plugin implements
        /// <c>IModTabHandler</c> (i.e. expose at least one option) are listed.
        ///
        /// Lifecycle: selecting a different mod fires <c>ModTabDidClose</c> on
        /// the previously-selected mod's handler so plugins like
        /// NotEnoughRosters can persist their state (it writes
        /// <c>trains.json</c> from that hook). The newly-selected mod's
        /// handler receives <c>ModTabDidOpen</c> on the next rebuild.
        /// </summary>
        private void BuildLegacyModsContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 170f;
            builder.Spacing = 6f;

            var hostedPlugins = FuseLegacyAssemblyHost
                .EnumerateAllHostedPlugins()
                .Where(info => info.Plugin is IModTabHandler)
                .Select(info => new TabHandlerEntry(
                    BuildTabHandlerSignature(info.Manifest, info.PluginType),
                    info.Manifest,
                    info.PluginType,
                    (IModTabHandler)info.Plugin))
                .OrderBy(entry => entry.DisplayLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();

            builder.AddSection("Legacy Mods Settings");
            AddWrappedField(
                builder,
                "Scope",
                "Settings tabs declared by legacy-loader plugins (Railloader IModTabHandler). Only mods that expose at least one tab option appear here.",
                52f);
            AddValueField(builder, "Mods Found", hostedPlugins.Count.ToString());

            if (hostedPlugins.Count == 0)
            {
                // No eligible mod means no signature should remain open.
                CloseAllOpenTabHandlers("no legacy mods with settings");
                AddWrappedField(
                    builder,
                    "Status",
                    "No hosted legacy plugin implements IModTabHandler. Mods that only register console commands or mixintos appear in the Mods tab instead.",
                    52f);
                builder.Spacer(8f);
                return;
            }

            // Resolve which mod is selected. If the stored selection is stale
            // (mod no longer hosted) fall back to the first available entry.
            var selected = hostedPlugins.FirstOrDefault(entry =>
                               string.Equals(entry.Signature, _selectedLegacyModSignature, StringComparison.OrdinalIgnoreCase))
                           ?? hostedPlugins[0];
            _selectedLegacyModSignature = selected.Signature;

            // Close any other mod's handler so only the selected one is "open".
            var keepOnlySelected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                selected.Signature
            };
            CloseTabHandlersExcept(keepOnlySelected, "legacy mods selection changed");

            // Mod picker.
            var labels = hostedPlugins.Select(entry => entry.DisplayLabel).ToList();
            var selectedIndex = Math.Max(0, hostedPlugins.FindIndex(entry =>
                string.Equals(entry.Signature, _selectedLegacyModSignature, StringComparison.OrdinalIgnoreCase)));
            builder.AddField(
                "Mod",
                builder.AddDropdown(labels, selectedIndex, index =>
                {
                    if (index < 0 || index >= hostedPlugins.Count)
                    {
                        return;
                    }

                    var chosen = hostedPlugins[index];
                    if (string.Equals(chosen.Signature, _selectedLegacyModSignature, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    _selectedLegacyModSignature = chosen.Signature;
                    RebuildWindow();
                })).Height(32f);
            builder.Spacer(6f);

            // Selected mod's panel.
            builder.AddSection(selected.DisplayLabel);
            AddWrappedField(builder, "Mod Id", selected.ManifestIdOrFallback, 28f);
            if (!string.IsNullOrWhiteSpace(selected.ManifestVersion))
            {
                AddValueField(builder, "Version", selected.ManifestVersion);
            }
            builder.Spacer(4f);

            _openTabHandlers[selected.Signature] = selected.Plugin;
            try
            {
                selected.Plugin.ModTabDidOpen(builder);
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    $"Legacy plugin '{selected.PluginType.FullName}' threw from ModTabDidOpen while FUSE was rendering its settings tab",
                    ex);
                AddWrappedField(
                    builder,
                    "Plugin Error",
                    $"{selected.PluginType.Name} threw {ex.GetType().Name} from ModTabDidOpen: {ex.GetBaseException().Message}",
                    54f);
            }

            builder.Spacer(8f);
        }

        private sealed class TabHandlerEntry
        {
            public TabHandlerEntry(string signature, FUSE.Loading.FuseLegacyAssemblyManifest manifest, Type pluginType, IModTabHandler plugin)
            {
                Signature = signature ?? string.Empty;
                Manifest = manifest;
                PluginType = pluginType;
                Plugin = plugin;
                DisplayLabel = BuildDisplayLabel(manifest, pluginType);
            }

            public string Signature { get; }
            public FUSE.Loading.FuseLegacyAssemblyManifest Manifest { get; }
            public Type PluginType { get; }
            public IModTabHandler Plugin { get; }
            public string DisplayLabel { get; }

            public string ManifestIdOrFallback
            {
                get
                {
                    if (Manifest != null && !string.IsNullOrWhiteSpace(Manifest.Id))
                    {
                        return Manifest.Id;
                    }

                    return PluginType == null ? "(unknown)" : (PluginType.FullName ?? PluginType.Name);
                }
            }

            public string ManifestVersion => Manifest == null ? string.Empty : (Manifest.Version ?? string.Empty);

            private static string BuildDisplayLabel(FUSE.Loading.FuseLegacyAssemblyManifest manifest, Type pluginType)
            {
                var modName = manifest == null
                    ? null
                    : (!string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Name : manifest.Id);
                var typeName = pluginType == null ? "(unnamed plugin)" : (pluginType.Name ?? pluginType.FullName);
                if (string.IsNullOrWhiteSpace(modName))
                {
                    return typeName;
                }

                return string.Equals(modName, typeName, StringComparison.OrdinalIgnoreCase)
                    ? modName
                    : modName + " | " + typeName;
            }
        }

        /// <summary>
        /// Calls <c>ModTabDidClose</c> on any tracked handlers whose signature is
        /// NOT in <paramref name="keepSignatures"/>, and forgets them. Plugins
        /// can rely on this to persist state when the user navigates away from
        /// their tab. Exceptions are logged but never bubble — a misbehaving
        /// plugin must not break FUSE's UI teardown.
        /// </summary>
        private void CloseTabHandlersExcept(HashSet<string> keepSignatures, string reason)
        {
            if (_openTabHandlers.Count == 0)
            {
                return;
            }

            var toClose = _openTabHandlers
                .Where(pair => keepSignatures == null || !keepSignatures.Contains(pair.Key))
                .ToArray();
            foreach (var entry in toClose)
            {
                _openTabHandlers.Remove(entry.Key);
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

        private void CloseAllOpenTabHandlers(string reason)
        {
            CloseTabHandlersExcept(null, reason);
        }

        private static string BuildTabHandlerSignature(FUSE.Loading.FuseLegacyAssemblyManifest manifest, Type pluginType)
        {
            var packageKey = manifest == null
                ? string.Empty
                : (manifest.Id ?? manifest.FolderPath ?? string.Empty);
            var typeKey = pluginType == null ? string.Empty : (pluginType.FullName ?? pluginType.Name ?? string.Empty);
            return packageKey + "|" + typeKey;
        }
    }
}
