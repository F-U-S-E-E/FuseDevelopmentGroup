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
                .SingleOrDefault(method =>
                {
                    if (!method.IsStatic ||
                        method.ReturnType != typeof(string) ||
                        method.Name.IndexOf("TitleForComponent", StringComparison.Ordinal) < 0)
                    {
                        return false;
                    }

                    var parameters = method.GetParameters();
                    return parameters.Length == 1 &&
                           parameters[0].ParameterType == typeof(IndustryComponent);
                });
        }

        internal static bool Prefix(
            [HarmonyArgument(0)] IndustryComponent component,
            ref string __result)
        {
            if (!TryGetCustomTitle(component as ICustomIndustryTitle, out var title))
            {
                return true;
            }

            __result = title;
            return false;
        }

        internal static bool TryGetCustomTitle(ICustomIndustryTitle titled, out string title)
        {
            title = titled?.Title;
            return !string.IsNullOrWhiteSpace(title);
        }
    }
}
