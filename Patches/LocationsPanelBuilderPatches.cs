using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Model.Ops;
using RAIL.API;
using RAIL.Infrastructure;
using UI.CompanyWindow;

namespace RAIL.Patches
{
    [HarmonyPatch]
    internal static class LocationsPanelBuilderPatches
    {
        private static MethodBase TargetMethod()
        {
            var compilerGeneratedType = typeof(LocationsPanelBuilder).GetNestedType("<>c", BindingFlags.NonPublic);
            var method = compilerGeneratedType?.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(candidate =>
                    candidate.ReturnType == typeof(string) &&
                    candidate.GetParameters().Length == 1 &&
                    candidate.GetParameters()[0].ParameterType == typeof(Industry));
            if (method == null)
            {
                RailLog.Warning("RAIL could not find LocationsPanelBuilder industry sort selector; source order will not affect the company locations list.");
            }

            return method;
        }

        private static void Postfix(Industry ind, ref string __result)
        {
            try
            {
                __result = IndustryAPI.LocationPanelSortKey(ind, __result);
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL failed to apply location panel sort key.", ex);
            }
        }
    }
}
