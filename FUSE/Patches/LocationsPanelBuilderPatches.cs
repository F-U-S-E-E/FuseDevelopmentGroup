using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Model.Ops;
using FUSE.Runtime.API;
using FUSE.Infrastructure;
using UI;
using UI.CompanyWindow;

namespace FUSE.Patches
{
    [HarmonyPatch]
    internal static class LocationsPanelBuilderPatches
    {
        private static MethodInfo TargetMethod()
        {
            var compilerGeneratedType = typeof(LocationsPanelBuilder).GetNestedType("<>c", BindingFlags.NonPublic);
            var method = compilerGeneratedType?.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(candidate =>
                    candidate.ReturnType == typeof(string) &&
                    candidate.GetParameters().Length == 1 &&
                    candidate.GetParameters()[0].ParameterType == typeof(Industry));
            if (method == null)
            {
                FuseLog.Warning("FUSE could not find LocationsPanelBuilder industry sort selector; source order will not affect the company locations list.");
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
                FuseLog.Exception("FUSE failed to apply location panel sort key.", ex);
            }
        }
    }

    [HarmonyPatch(typeof(ListController), nameof(ListController.SetData))]
    internal static class LocationsListControllerPatches
    {
        private static void Prefix(ref List<ListController.Item> items)
        {
            if (items == null || items.Count <= 1 || !items.All(item => item.Value is Industry))
            {
                return;
            }

            try
            {
                var entries = items
                    .Select((item, index) => new LocationListEntry(item, index))
                    .ToArray();

                var areaFirstIndexes = entries
                    .GroupBy(entry => entry.AreaKey, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Min(entry => entry.OriginalIndex), StringComparer.OrdinalIgnoreCase);

                foreach (var entry in entries)
                {
                    entry.AreaFirstIndex = areaFirstIndexes.TryGetValue(entry.AreaKey, out var index)
                        ? index
                        : entry.OriginalIndex;
                    entry.AreaSortOrder = TrackAPI.GetAreaSortOrder(entry.Area, entry.AreaFirstIndex);
                }

                items = entries
                    .OrderBy(entry => entry.AreaSortOrder)
                    .ThenBy(entry => entry.AreaFirstIndex)
                    .ThenBy(entry => entry.IndustrySortKey, StringComparer.Ordinal)
                    .ThenBy(entry => entry.OriginalIndex)
                    .Select(entry => entry.Item)
                    .ToList();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE failed to sort the company Locations list.", ex);
            }
        }

        private sealed class LocationListEntry
        {
            public LocationListEntry(ListController.Item item, int originalIndex)
            {
                Item = item;
                OriginalIndex = originalIndex;
                Industry = item.Value as Industry;
                Area = Industry != null ? Industry.GetComponentInParent<Area>() : null;
                AreaKey = Area != null
                    ? (string.IsNullOrWhiteSpace(Area.identifier) ? Area.name : Area.identifier)
                    : item.SectionText ?? string.Empty;
                HasAreaOrder = TrackAPI.TryGetAreaOrder(Area, out var order);
                AreaOrder = HasAreaOrder ? order : TrackAPI.GetSiblingAreaSortOrder(originalIndex);
                AreaSortOrder = AreaOrder;
                IndustrySortKey = IndustryAPI.LocationPanelSortKey(Industry, item.ItemText);
            }

            public ListController.Item Item { get; }
            public int OriginalIndex { get; }
            public Industry Industry { get; }
            public Area Area { get; }
            public string AreaKey { get; }
            public bool HasAreaOrder { get; }
            public int AreaOrder { get; }
            public int AreaSortOrder { get; set; }
            public string IndustrySortKey { get; }
            public int AreaFirstIndex { get; set; }
        }
    }

    [HarmonyPatch(typeof(OpsController), "get_Areas")]
    internal static class OpsControllerAreasOrderingPatches
    {
        private static void Postfix(ref IEnumerable<Area> __result)
        {
            if (__result == null)
            {
                return;
            }

            try
            {
                __result = __result
                    .Where(area => area != null)
                    .Select((area, index) => new
                    {
                        Area = area,
                        OriginalIndex = index,
                        SortOrder = TrackAPI.GetAreaSortOrder(area, index)
                    })
                    .OrderBy(entry => entry.SortOrder)
                    .ThenBy(entry => entry.OriginalIndex)
                    .Select(entry => entry.Area)
                    .ToArray();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE failed to sort OpsController.Areas for the company Locations list.", ex);
            }
        }
    }

    [HarmonyPatch(typeof(IndustryTrackDisplayableExtensions), nameof(IndustryTrackDisplayableExtensions.ShortName))]
    internal static class IndustryTrackDisplayableShortNamePatches
    {
        private static readonly string[] WordSeparators = { " " };

        private static bool Prefix(IIndustryTrackDisplayable ic, Industry industry, ref string __result)
        {
            try
            {
                var text = ic?.DisplayName ?? string.Empty;
                var industryName = industry?.name ?? string.Empty;
                if (string.Equals(text, industryName, StringComparison.Ordinal))
                {
                    __result = text;
                    return false;
                }

                int prefixLength;
                if (StartsWithSamePrefix(industryName, text, out prefixLength) &&
                    prefixLength > 3 &&
                    prefixLength < text.Length)
                {
                    __result = text.Substring(prefixLength);
                    return false;
                }

                __result = text;
                return false;
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE failed to compute a safe industry track display name.", ex);
                __result = ic?.DisplayName ?? string.Empty;
                return false;
            }
        }

        private static bool StartsWithSamePrefix(string a, string b, out int numberOfCharacters)
        {
            numberOfCharacters = 0;
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }

            var maxCharacterMatch = Math.Min(a.Length, b.Length);
            var characterMatches = 0;
            while (characterMatches < maxCharacterMatch && a[characterMatches] == b[characterMatches])
            {
                characterMatches++;
            }

            if (characterMatches <= 0)
            {
                return false;
            }

            var aWords = a.Split(WordSeparators, StringSplitOptions.None);
            var bWords = b.Split(WordSeparators, StringSplitOptions.None);
            var wordIndex = 0;
            while (wordIndex < Math.Min(aWords.Length, bWords.Length) &&
                   string.Equals(aWords[wordIndex], bWords[wordIndex], StringComparison.Ordinal))
            {
                numberOfCharacters += 1 + aWords[wordIndex].Length;
                wordIndex++;
            }

            return numberOfCharacters > 0;
        }
    }
}
