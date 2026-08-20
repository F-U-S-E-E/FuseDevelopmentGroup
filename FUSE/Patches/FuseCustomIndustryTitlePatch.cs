using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Model.Ops;
using StrangeCustoms.Tracks.Industries;
using UI;

namespace FUSE.Patches
{
    [HarmonyPatch]
    internal static class FuseCustomIndustryTitlePatch
    {
        internal static MethodBase TargetMethod()
        {
            return AccessTools.GetDeclaredMethods(typeof(DropdownLocationPickerRowData))
                .FirstOrDefault(method =>
                    method.Name.IndexOf(
                        "TitleForComponent",
                        StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static bool Prefix(IndustryComponent ic, ref string __result)
        {
            if (!(ic is ICustomIndustryTitle titled)
                || string.IsNullOrWhiteSpace(titled.Title))
            {
                return true;
            }

            __result = titled.Title;
            return false;
        }
    }
}
