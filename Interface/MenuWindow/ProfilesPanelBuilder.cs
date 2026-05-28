using FUSE.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using UI.Builder;
using UI.Common;

namespace FUSE.Interface.MenuWindow
{
    internal struct ProfilesPanelBuilder
    {
        public static void Build(UIPanelBuilder builder, UIState<string> selectedItem)
        {
            var modSets = FuseModSetService.GetSets();
            var activeSetId = FuseModSetService.ActiveSetId;

            List<UIPanelBuilder.ListItem<FuseModSet>> list = modSets
                .OrderBy(m => m.Name)
                .Select(m => new UIPanelBuilder.ListItem<FuseModSet>(m.Id, m, "Saved Mod Profiles", m.Name))
                .ToList();

            if (list.Count > 0 && string.IsNullOrEmpty(selectedItem.Value))
            {
                selectedItem.Value = list.First().Value.Id;
            }

            builder.AddListDetail(list, selectedItem, delegate (UIPanelBuilder builder, FuseModSet modSet)
            {
                if (modSet == null)
                {
                    builder.AddExpandingVerticalSpacer();
                    builder.AddLabelEmptyState("Create or select a mod profile");
                    builder.AddExpandingVerticalSpacer();
                }
                else
                {
                    builder.VScrollView(b => BuildModProfileDetail(b, modSet));
                }
            });

            builder.Spacer(12f);

            builder.ButtonStrip(row =>
            {
                row.AddButton("New Profile", () =>
                {
                    var createdModSet = FuseModSetService.CreateSetFromCurrentActiveMods();
                });
            });
        }

        private static void BuildModProfileDetail(UIPanelBuilder builder, FuseModSet modSet)
        {
            var modSetIsActive = string.Equals(FuseModSetService.ActiveSetId, modSet.Id, StringComparison.OrdinalIgnoreCase);

            builder.AddTitle(modSet.Name, "Mod Profile");
            builder.HStack(row =>
            {
                row.Spacer();
                row.AddButton(modSetIsActive ? "Deactivate" : "Activate", () =>
                {
                    if (modSetIsActive)
                    {
                        FuseModSetService.ClearActiveSet();
                        builder.Rebuild();
                    }
                    else
                    {
                        FuseModSetService.SetActive(modSet.Id);
                        builder.Rebuild();
                    }
                });
                row.AddButton("Delete", delegate
                {
                    HandleDeleteModSet(modSet);
                });
                row.Spacer(8f);
            });
            builder.AddSection("Overview");


            builder.AddField("Status", modSetIsActive ? "Active" : "Inactive");
            builder.AddField("Profile Name", modSet.Name);
            builder.AddField("Enabled Mods", FuseModSetService.GetSetPackageSummary(modSet));

            builder.AddSection("How profiles work");
            builder.Spacer(4f);
            builder.AddLabel("Use profiles to manage multiple mod lists. UMM decides which mods exist; FUSE profiles only choose from UMM-active mods. If no profile is selected, everything UMM-active is enabled.");
            builder.Spacer(8f);
            builder.AddLabel("You must restart the game in order for changes to take effect on the active mod profile.");
            builder.Spacer(8f);

            builder.AddSection("Mod List");
            builder.Spacer(8f);

            builder.Spacer(8f);
            var activeMods = FuseModSetService.GetVisibleUmmMods();
            if (activeMods.Count == 0)
            {
                builder.AddField("Mods", "None found through UMM");
            }
            else
            {
                foreach (var mod in activeMods)
                {
                    BuildModListEntry(builder, mod, modSet);
                }
            }
        }

        private static void BuildModListEntry(UIPanelBuilder builder, FuseUmmModInfo mod, FuseModSet modSet)
        {
            var enabled = modSet.EnabledModIds.Contains(mod.Id);
            builder.HStack(row =>
            {
                row.AddToggle(() => enabled, (_) =>
                {
                    FuseModSetService.ToggleModInSet(mod, modSet);
                    builder.Rebuild();
                });
                row.Spacer(8f);
                var enabledButton = row.AddButtonSelectable(enabled ? "Enabled" : "Disabled", enabled, () =>
                {
                    FuseModSetService.ToggleModInSet(mod, modSet);
                    builder.Rebuild();
                }).Width(100f);

                row.Spacer(8f);
                row.FieldLabelWidth = 40f;
                row.AddField("Name", mod.DisplayName);
            });

            builder.FieldLabelWidth = 192f;
            builder.AddField("Id", mod.Id);
            builder.AddField("Version", mod.Version);

            builder.Spacer(8f);
            builder.AddHRule();
            builder.Spacer(8f);
        }

        private static void HandleDeleteModSet(FuseModSet modSet)
        {
            ModalAlertController.Present($"Delete profile {modSet.Name}?", "This cannot be undone.",
                [
                    (true, "Delete"),
                    (false, "Cancel")
                ], delegate (bool val)
                {
                    if (val)
                    {
                        FuseModSetService.DeleteSet(modSet.Id);
                    }
                });
        }
    }
}
