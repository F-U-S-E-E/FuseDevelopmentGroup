using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using FUSE.Infrastructure;
using HarmonyLib;
using Model.Ops;
using UI.Builder;

namespace FUSE.Patches
{
    /// <summary>
    /// Removes semantically duplicate destinations from the car inspector's
    /// automatic-waybill picker. Legacy packages can leave multiple runtime
    /// component objects with the same identifier; Railroader otherwise shows
    /// every object as a separate, indistinguishable picker row.
    /// </summary>
    [HarmonyPatch]
    internal static class FuseLocationPickerDeduplicationPatch
    {
        private const string EmptyDestinationKey = "\u0000none";
        private static int _reported;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(UIPanelBuilder),
                nameof(UIPanelBuilder.AddLocationPicker),
                new[]
                {
                    typeof(string),
                    typeof(List<ValueTuple<IndustryComponent, Area>>),
                    typeof(IndustryComponent),
                    typeof(Action<IndustryComponent>)
                });
        }

        private static void Prefix(
            ref List<ValueTuple<IndustryComponent, Area>> options,
            ref IndustryComponent selected)
        {
            var selectedKey = SelectedDestinationKey(selected, options);
            var removed = DeduplicateByKey(options, DestinationKey, out var deduplicated);
            if (removed == 0)
            {
                return;
            }

            options = deduplicated;
            selected = ResolveCanonicalSelection(selected, selectedKey, deduplicated);

            if (Interlocked.Exchange(ref _reported, 1) == 0)
            {
                FuseLog.Info(
                    $"FUSE removed {removed} duplicate automatic-waybill destination row(s). " +
                    "The first matching destination remains selectable; runtime industry data was not deleted.");
            }
        }

        private static string DestinationKey(ValueTuple<IndustryComponent, Area> option)
        {
            var component = option.Item1;
            var identifier = DestinationKey(component);
            if (component == null || identifier == null)
            {
                return identifier;
            }

            try
            {
                var spanIds = (component.trackSpans ?? Array.Empty<Track.TrackSpan>())
                    .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id))
                    .Select(span => span.id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var displayName = component.DisplayName?.Trim();
                if (spanIds.Length == 0 || string.IsNullOrWhiteSpace(displayName))
                {
                    return identifier;
                }

                return string.Join(
                    "\u001f",
                    "semantic",
                    component.GetType().FullName ?? component.GetType().Name,
                    option.Item2?.identifier?.Trim() ?? string.Empty,
                    displayName,
                    string.Join("\u001e", spanIds));
            }
            catch
            {
                return identifier;
            }
        }

        private static string DestinationKey(IndustryComponent component)
        {
            if (component == null)
            {
                return EmptyDestinationKey;
            }

            try
            {
                var identifier = component.Identifier?.Trim();
                return string.IsNullOrWhiteSpace(identifier) ? null : identifier;
            }
            catch
            {
                // A malformed component must not be allowed to break the UI.
                // A null key tells the helper to preserve that row unchanged.
                return null;
            }
        }

        private static string SelectedDestinationKey(
            IndustryComponent selected,
            IList<ValueTuple<IndustryComponent, Area>> options)
        {
            if (selected == null || options == null)
            {
                return DestinationKey(selected);
            }

            foreach (var option in options)
            {
                if (ReferenceEquals(option.Item1, selected) || option.Item1 == selected)
                {
                    return DestinationKey(option);
                }
            }

            return DestinationKey(selected);
        }

        private static IndustryComponent ResolveCanonicalSelection(
            IndustryComponent selected,
            string selectedKey,
            IList<ValueTuple<IndustryComponent, Area>> options)
        {
            if (selected == null || selectedKey == null || options == null)
            {
                return selected;
            }

            foreach (var option in options)
            {
                if (string.Equals(
                    selectedKey,
                    DestinationKey(option),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return option.Item1;
                }
            }

            return selected;
        }

        internal static int DeduplicateByKey<T>(
            IList<T> source,
            Func<T, string> keySelector,
            out List<T> result)
        {
            result = source == null ? new List<T>() : new List<T>(source.Count);
            if (source == null || source.Count == 0 || keySelector == null)
            {
                return 0;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in source)
            {
                var key = keySelector(item);
                if (key == null || seen.Add(key))
                {
                    result.Add(item);
                }
            }

            return source.Count - result.Count;
        }

        internal static T ResolveCanonicalByKey<T>(
            T selected,
            IList<T> options,
            Func<T, string> keySelector)
        {
            if (ReferenceEquals(selected, null) || options == null || keySelector == null)
            {
                return selected;
            }

            var selectedKey = keySelector(selected);
            if (selectedKey == null)
            {
                return selected;
            }

            foreach (var option in options)
            {
                var optionKey = keySelector(option);
                if (string.Equals(selectedKey, optionKey, StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }

            return selected;
        }
    }
}
