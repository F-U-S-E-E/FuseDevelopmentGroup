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

        private void BuildModSetsContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 160f;
            builder.Spacing = 6f;

            builder.AddSection("Active Mod Set");
            AddValueField(builder, "Selected", FuseModSetService.ActiveSetName);
            AddValueField(builder, "Profile Hash", FuseModSetService.GetActiveSetFingerprint());
            AddWrappedField(builder, "Enabled Mods", FuseModSetService.GetActiveSetPackageSummary(), 42f);
            AddWrappedField(
                builder,
                "Guide",
                "Use mod sets as server profiles. UMM decides which mods exist; FUSE sets only choose from UMM-active mods. If no set is selected, everything UMM-active is enabled.",
                58f);
            AddWrappedField(
                builder,
                "Apply",
                "Changes take effect on the next map load or FUSE reload. Share the profile hash and exported manifest with multiplayer players.",
                48f);
            AddWrappedLabel(builder, FuseModSetService.LastStatus, 34f);
            BuildModSetHealth(builder);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Create From Current", () =>
                {
                    FuseModSetService.CreateSetFromCurrentActiveMods();
                    RebuildWindow();
                });
                row.AddButtonCompact("Use All Active Mods", () =>
                {
                    FuseModSetService.ClearActiveSet();
                    RebuildWindow();
                });
                row.AddButtonCompact("Refresh", RebuildWindow);
                row.AddButtonCompact("Export Manifest", () =>
                {
                    RunAction("export active mod-set manifest", () => "Exported active mod-set manifest: " + FuseModSetService.ExportActiveManifest());
                });
            }, 6f).Height(32f);
            builder.Spacer(6f);

            builder.AddSection("Saved Sets");
            var sets = FuseModSetService.GetSets();
            if (sets.Count == 0)
            {
                AddValueField(builder, "Sets", "None");
            }
            else
            {
                foreach (var set in sets)
                {
                    var captured = set;
                    builder.HStack(row =>
                    {
                        row.AddLabel(
                            (string.Equals(FuseModSetService.ActiveSetId, captured.Id, StringComparison.OrdinalIgnoreCase) ? "* " : string.Empty) +
                            $"{captured.Name} ({captured.EnabledFolderNames.Length} mod folder(s))",
                            text =>
                            {
                                text.enableWordWrapping = false;
                                text.overflowMode = TextOverflowModes.Ellipsis;
                            });
                        row.AddButtonCompact("Select", () =>
                        {
                            FuseModSetService.SetActive(captured.Id);
                            RebuildWindow();
                        });
                        row.AddButtonCompact("Delete", () =>
                        {
                            FuseModSetService.DeleteSet(captured.Id);
                            RebuildWindow();
                        });
                    }, 6f).Height(30f);
                }
            }

            builder.Spacer(6f);
            builder.AddSection("UMM Active Mods");
            var activeMods = FuseModSetService.GetVisibleUmmMods();
            if (activeMods.Count == 0)
            {
                AddValueField(builder, "Mods", "None found through UMM");
            }
            else
            {
                foreach (var mod in activeMods)
                {
                    var captured = mod;
                    var enabled = FuseModSetService.IsModEnabledInActiveSet(captured);
                    builder.HStack(row =>
                    {
                        var version = string.IsNullOrWhiteSpace(captured.Version) ? string.Empty : " v" + captured.Version;
                        row.AddLabel(
                            $"{captured.DisplayName}{version} ({captured.FolderName})",
                            text =>
                            {
                                text.enableWordWrapping = false;
                                text.overflowMode = TextOverflowModes.Ellipsis;
                            });
                        row.AddButtonCompact(enabled ? "On" : "Off", () =>
                        {
                            FuseModSetService.ToggleModInActiveSet(captured);
                            RebuildWindow();
                        });
                    }, 6f).Height(30f);
                }
            }

            builder.Spacer(8f);
        }

        private static void BuildDependencyGraph(UIPanelBuilder builder, IReadOnlyList<FusePackageManifestSnapshot> manifests)
        {
            if (manifests == null || manifests.Count == 0)
            {
                AddValueField(builder, "Dependencies", "No packages discovered");
                return;
            }

            var byId = manifests
                .GroupBy(manifest => manifest.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var rows = 0;
            foreach (var manifest in manifests)
            {
                var hasEdges = manifest.LoadAfter.Length > 0 || manifest.LoadBefore.Length > 0 || manifest.Faults.Length > 0;
                if (!hasEdges && !FuseSettings.ShowAdvancedHealthDetails)
                {
                    continue;
                }

                if (!hasEdges)
                {
                    continue;
                }

                AddWrappedLabel(builder, InsertBreakHints(manifest.Id), 28f);
                foreach (var dependency in manifest.LoadAfter)
                {
                    AddWrappedLabel(builder, "     after -> " + InsertBreakHints(FormatDependencyEdge(dependency, byId)), 28f);
                    rows++;
                }

                foreach (var dependency in manifest.LoadBefore)
                {
                    AddWrappedLabel(builder, "     before -> " + InsertBreakHints(FormatDependencyEdge(dependency, byId)), 28f);
                    rows++;
                }

                foreach (var fault in manifest.Faults)
                {
                    AddWrappedLabel(builder, "     fault -> " + InsertBreakHints(fault), 42f);
                    rows++;
                }
            }

            if (rows == 0)
            {
                AddValueField(builder, "Dependencies", "No package dependency edges in current profile");
            }
        }

        private static string FormatDependencyEdge(string dependencyId, Dictionary<string, FusePackageManifestSnapshot> packages)
        {
            if (string.IsNullOrWhiteSpace(dependencyId))
            {
                return "(blank) | missing";
            }

            if (packages != null && packages.TryGetValue(dependencyId, out var dependency))
            {
                return dependency.Disabled
                    ? dependencyId + " | disabled"
                    : dependencyId + " | ready";
            }

            return dependencyId + " | missing";
        }

        private static void BuildModSetHealth(UIPanelBuilder builder)
        {
            var visible = FuseModSetService.GetVisibleUmmMods();
            var enabled = visible.Count(FuseModSetService.IsModEnabledInActiveSet);
            var disabledByProfile = Math.Max(0, visible.Count - enabled);

            builder.Spacer(4f);
            builder.AddSection("Profile Health");
            AddValueField(builder, "UMM Active", visible.Count.ToString());
            AddValueField(builder, "Enabled By Profile", enabled.ToString());
            AddValueField(builder, "Disabled By Profile", disabledByProfile.ToString());
            AddValueField(builder, "Mode", FuseModSetService.HasActiveSet ? "Server profile filter active" : "All UMM-active mods");
            AddWrappedField(
                builder,
                "Server Use",
                "Share the profile hash and exported manifest. FUSE does not change UMM enablement; it only filters UMM-active packages for this profile.",
                48f);
        }
    }
}
