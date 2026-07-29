using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Runtime.Events;
using FUSE.Infrastructure;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static partial class IndustryAPI
    {

        private static void InvalidateIndustryComponents(Industry industry)
        {
            if (industry == null)
            {
                return;
            }

            var clearedIndustryComponentList = IndustryRuntimeComponentsField != null;
            IndustryRuntimeComponentsField?.SetValue(industry, null);

            var refreshedCount = 0;
            foreach (var component in industry.GetComponentsInChildren<IndustryComponent>(true))
            {
                if (!IsLiveIndustryComponent(component) || string.IsNullOrWhiteSpace(component.subIdentifier))
                {
                    continue;
                }

                CachedIndustryField?.SetValue(component, null);
                ComponentIdentifierField?.SetValue(component, null);
                PrimeComponentIdentity(industry, component);
                refreshedCount++;
            }

            var formulaCacheCleared = ClearFormulaicSiblingCaches(industry);
            var interchangeCacheCleared = ClearInterchangedSiblingCaches(industry);

            FuseLog.Info($"FUSE invalidated industry component caches for '{industry.identifier}' cachedComponentsCleared={clearedIndustryComponentList} componentIdentityRefreshed={refreshedCount} formulaSiblingCachesCleared={formulaCacheCleared} interchangedSiblingCachesCleared={interchangeCacheCleared}.");
        }

        /// <summary>
        /// Forces every <see cref="FormulaicIndustryComponent"/> on the
        /// industry to drop its private <c>_otherComponents</c> sibling
        /// cache. The cache is lazy-initialized on first <c>Service</c>
        /// call and never refreshed by the game; if FUSE has just
        /// destroyed and re-created a sibling component (e.g. an
        /// IndustryLoader being replaced by a TeleportLoadingIndustry
        /// because a Foxy patch changed its <c>type</c>), the cache still
        /// holds the destroyed instance and
        /// <see cref="FormulaicIndustryComponent.MaxStorageForLoad"/>
        /// returns 0, which makes the formula spuriously report
        /// "Production Stopped: &lt;output load&gt;" even when storage has
        /// abundant headroom. Resetting the field to <c>null</c> here
        /// makes the next Service tick walk
        /// <see cref="UnityEngine.Component.GetComponentsInChildren{T}()"/>
        /// fresh and pick up the live replacement.
        /// </summary>
        private static int ClearFormulaicSiblingCaches(Industry industry)
        {
            if (FormulaicOtherComponentsField == null)
            {
                return 0;
            }

            var cleared = 0;
            foreach (var formulaic in industry.GetComponentsInChildren<FormulaicIndustryComponent>(true))
            {
                if (formulaic == null)
                {
                    continue;
                }

                try
                {
                    FormulaicOtherComponentsField.SetValue(formulaic, null);
                    cleared++;
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE could not clear FormulaicIndustryComponent._otherComponents on " +
                        $"'{formulaic.Identifier ?? formulaic.name ?? "<unknown>"}' message='{ex.Message}'.");
                }
            }

            return cleared;
        }

        /// <summary>
        /// Resets the sibling-Interchange cache that
        /// <see cref="InterchangedIndustryLoader"/> (vanilla) and
        /// <see cref="FuseInterchangedIndustryUnloader"/> both maintain via
        /// the private pair <c>_interchange</c> / <c>_hasInterchange</c>.
        /// First access to the <c>Interchange</c> property memoizes the
        /// sibling <see cref="Interchange"/> MonoBehaviour for the
        /// lifetime of the component instance; if FUSE later type-changes
        /// the Interchange sibling (Remove+Add), the cached pointer is to
        /// a destroyed object. Subsequent reads return null-equivalent
        /// values silently: <c>DisplayName</c> collapses to <c>base.name</c>,
        /// <c>ServeInterchange</c> short-circuits on
        /// <c>interchange == null</c>, and the bardo-return loop stops.
        /// Clearing both fields makes the next access re-evaluate
        /// <c>Industry.GetComponentInChildren&lt;Interchange&gt;()</c>
        /// against the live component graph.
        /// </summary>
        private static int ClearInterchangedSiblingCaches(Industry industry)
        {
            var cleared = 0;
            foreach (var component in industry.GetComponentsInChildren<IndustryComponent>(true))
            {
                if (component == null)
                {
                    continue;
                }

                var componentType = component.GetType();
                var clearedAny = false;
                foreach (var fieldName in InterchangedComponentCacheFieldNames)
                {
                    var field = componentType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field == null)
                    {
                        continue;
                    }

                    try
                    {
                        // Both fields are nullable reference / nullable value
                        // shapes — setting to null is the canonical reset.
                        // Use the declared field type's default so a
                        // nullable-bool gets a default(bool?) rather than
                        // throwing on a value-type null assignment.
                        field.SetValue(component, GetDefaultFieldValue(field.FieldType));
                        clearedAny = true;
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Warning(
                            $"FUSE could not clear {componentType.Name}.{fieldName} on " +
                            $"'{component.Identifier ?? component.name ?? "<unknown>"}' message='{ex.Message}'.");
                    }
                }

                if (clearedAny)
                {
                    cleared++;
                }
            }

            return cleared;
        }

        private static object GetDefaultFieldValue(Type fieldType)
        {
            if (fieldType == null)
            {
                return null;
            }

            return fieldType.IsValueType ? Activator.CreateInstance(fieldType) : null;
        }

        internal static int ScrubIndustryComponentCaches(string source)
        {
            var operation = source ?? "unspecified";
            var prunedSpanReferences = ScrubIndustryComponentTrackSpanReferences(operation);
            if (IndustryRuntimeComponentsField == null)
            {
                return prunedSpanReferences;
            }

            var scrubbed = 0;
            foreach (var industry in UnityEngine.Object.FindObjectsOfType<Industry>(true))
            {
                scrubbed += ScrubIndustryComponentCache(industry, operation);
            }

            if (scrubbed > 0)
            {
                FuseLog.Warning($"FUSE scrubbed {scrubbed} stale industry component cache(s) after '{operation}'.");
            }

            return scrubbed + prunedSpanReferences;
        }

        internal static int DisableOrphanedBaseGameIndustries(string source)
        {
            var operation = source ?? "unspecified";
            ScrubIndustryComponentTrackSpanReferences(operation);

            var disabled = 0;
            foreach (var industry in UnityEngine.Object.FindObjectsOfType<Industry>(true))
            {
                if (!ShouldDisableOrphanedBaseGameIndustry(industry))
                {
                    continue;
                }

                var id = industry.identifier ?? string.Empty;
                try
                {
                    industry.ProgressionDisabled = true;
                    foreach (var component in industry.GetComponentsInChildren<IndustryComponent>(true))
                    {
                        if (component != null)
                        {
                            component.ProgressionDisabled = true;
                        }
                    }

                    // PassengerStop is not an IndustryComponent, so the loop
                    // above never reaches it — and the game's own feature pass
                    // cannot either, because the orphaned industry's area
                    // wiring was severed by the same track replacement that
                    // orphaned it. Left enabled, the stop keeps appearing in
                    // the destination picker and keeps being generated FOR by
                    // every other station (seen in the field: 'almond' and
                    // 'nantahala' on the EWH map). Everything downstream —
                    // picker filter, ActiveAvailableDestinations, the spawn
                    // loop — keys off ProgressionDisabled, so setting it here
                    // retires the stop everywhere at once.
                    foreach (var stop in industry.GetComponentsInChildren<PassengerStop>(true))
                    {
                        if (stop != null && !stop.ProgressionDisabled)
                        {
                            stop.ProgressionDisabled = true;
                            FuseLog.Info(
                                $"FUSE disabled passenger stop '{stop.identifier}' with orphaned base-game industry '{id}'.");
                        }
                    }

                    industry.gameObject.SetActive(false);
                    FuseIndustryRuntimeIndex.Instance.Remove(id);
                    disabled++;
                    FuseLog.Info(
                        $"FUSE disabled orphaned base-game industry '{id}' " +
                        $"reason='{operation}' detail='all live industry components lost their track spans after FUSE track replacement'.");
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE could not disable orphaned base-game industry '{id}' after '{operation}'", ex);
                }
            }

            if (disabled > 0)
            {
                RefreshIndustriesAfterBatch("DisableOrphanedBaseGameIndustries:" + operation);
                FuseLog.Info($"FUSE disabled {disabled} orphaned base-game industr{(disabled == 1 ? "y" : "ies")} after '{operation}'.");
            }

            return disabled;
        }

        internal static int ScrubIndustryComponentCache(Industry industry, string source)
        {
            if (industry == null || IndustryRuntimeComponentsField == null)
            {
                return 0;
            }

            var operation = source ?? "unspecified";
            IndustryComponent[] cachedComponents;
            try
            {
                cachedComponents = IndustryRuntimeComponentsField.GetValue(industry) as IndustryComponent[];
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE could not inspect cached industry components for '{industry.identifier}' after '{operation}'", ex);
                return 0;
            }

            if (cachedComponents == null)
            {
                return 0;
            }

            var hasStaleComponent = false;
            for (var i = 0; i < cachedComponents.Length; i++)
            {
                if (!IsLiveIndustryComponent(cachedComponents[i]))
                {
                    hasStaleComponent = true;
                    break;
                }
            }

            if (!hasStaleComponent)
            {
                return 0;
            }

            try
            {
                IndustryRuntimeComponentsField.SetValue(industry, null);
                FuseLog.Warning($"FUSE scrubbed stale industry component cache for '{industry.identifier}' after '{operation}'.");
                return 1;
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE could not clear stale industry component cache for '{industry.identifier}' after '{operation}'", ex);
                return 0;
            }
        }

        private static int ScrubIndustryComponentTrackSpanReferences(string source)
        {
            var pruned = 0;
            foreach (var component in UnityEngine.Object.FindObjectsOfType<IndustryComponent>(true))
            {
                if (!IsLiveIndustryComponent(component) || component.trackSpans == null || component.trackSpans.Length == 0)
                {
                    continue;
                }

                var retained = component.trackSpans.Where(IsLiveTrackSpan).ToArray();
                if (retained.Length == component.trackSpans.Length)
                {
                    continue;
                }

                var removed = component.trackSpans.Length - retained.Length;
                pruned += removed;
                component.trackSpans = retained;
                FuseLog.Warning(
                    $"FUSE pruned stale industry component track span reference(s) component='{DescribeComponent(component)}' " +
                    $"removed={removed} reason='{source ?? "unspecified"}'.");
            }

            if (pruned > 0)
            {
                FuseLog.Warning($"FUSE pruned {pruned} stale industry component track span reference(s) after '{source ?? "unspecified"}'.");
            }

            return pruned;
        }

        internal static int RemoveTrackSpanReferences(TrackSpan removedSpan, string source)
        {
            if (removedSpan == null)
            {
                return 0;
            }

            var removedSpanId = SafeTrackSpanId(removedSpan);
            var pruned = 0;
            foreach (var component in UnityEngine.Object.FindObjectsOfType<IndustryComponent>(true))
            {
                if (!IsLiveIndustryComponent(component) || component.trackSpans == null || component.trackSpans.Length == 0)
                {
                    continue;
                }

                var retained = new List<TrackSpan>(component.trackSpans.Length);
                var removed = 0;
                foreach (var span in component.trackSpans)
                {
                    if (TrackSpanReferencesRemovedInstance(span, removedSpan))
                    {
                        removed++;
                        continue;
                    }

                    retained.Add(span);
                }

                if (removed == 0)
                {
                    continue;
                }

                component.trackSpans = retained.ToArray();
                pruned += removed;
                if (FuseSettings.VerboseApplyReportDetails)
                {
                    FuseLog.Warning(
                        $"FUSE removed industry component reference(s) to removed TrackSpan component='{DescribeComponent(component)}' " +
                        $"spanId='{removedSpanId}' removed={removed} reason='{source ?? "unspecified"}'.");
                }
            }

            if (pruned > 0)
            {
                if (FuseSettings.VerboseApplyReportDetails)
                {
                    FuseLog.Warning(
                        $"FUSE removed {pruned} industry component reference(s) to removed TrackSpan " +
                        $"spanId='{removedSpanId}' after '{source ?? "unspecified"}'.");
                }
            }

            return pruned;
        }

        private static bool TrackSpanReferencesRemovedInstance(TrackSpan candidate, TrackSpan removedSpan)
        {
            if (ReferenceEquals(candidate, removedSpan))
            {
                return true;
            }

            if (candidate == null || removedSpan == null)
            {
                return false;
            }

            try
            {
                return candidate == removedSpan;
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldDisableOrphanedBaseGameIndustry(Industry industry)
        {
            if (!IsLiveIndustry(industry) || !industry.gameObject.activeSelf)
            {
                return false;
            }

            var id = industry.identifier ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id) || FuseCreatedIndustryIds.Contains(id))
            {
                return false;
            }

            if (FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.Industry, id, out FuseIndustry _))
            {
                return false;
            }

            var components = industry.GetComponentsInChildren<IndustryComponent>(true)
                .Where(IsLiveIndustryComponent)
                .ToArray();
            if (components.Length == 0)
            {
                return false;
            }

            return components.All(component =>
                component.trackSpans == null ||
                component.trackSpans.Length == 0 ||
                component.trackSpans.All(span => !IsLiveTrackSpan(span)));
        }

        private static bool IsLiveTrackSpan(TrackSpan span)
        {
            if (span == null)
            {
                return false;
            }

            try
            {
                return span.gameObject != null;
            }
            catch
            {
                return false;
            }
        }

        private static string SafeTrackSpanId(TrackSpan span)
        {
            if (span == null)
            {
                return string.Empty;
            }

            try
            {
                return span.id ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsLiveIndustry(Industry industry)
        {
            if (industry == null)
            {
                return false;
            }

            try
            {
                return industry.gameObject != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsLiveIndustryComponent(IndustryComponent component)
        {
            if (component == null)
            {
                return false;
            }

            try
            {
                return component.gameObject != null;
            }
            catch
            {
                return false;
            }
        }

        private static string GetComponentIdentifier(Industry industry, IndustryComponent component)
        {
            if (industry == null)
            {
                throw new ArgumentNullException(nameof(industry));
            }

            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            PrimeComponentIdentity(industry, component);
            return component.Identifier;
        }

        private static void PrimeComponentIdentity(Industry industry, IndustryComponent component)
        {
            if (industry == null || component == null)
            {
                return;
            }

            CachedIndustryField?.SetValue(component, industry);
            ComponentIdentifierField?.SetValue(component, industry.identifier + "." + component.subIdentifier);
        }

        internal static void BeginIndustryApplyBatch()
        {
            _industryApplyBatchDepth++;
        }

        internal static void EndIndustryApplyBatch(string source)
        {
            if (_industryApplyBatchDepth > 0)
            {
                _industryApplyBatchDepth--;
            }

            if (_industryApplyBatchDepth == 0)
            {
                // Batch finished: drop the scene snapshot so the next apply rebuilds
                // it from the then-current scene.
                _batchIndustrySnapshot = null;

                if (_industryRefreshPending)
                {
                    _industryRefreshPending = false;
                    RefreshIndustriesAfterBatch(source ?? "industry apply batch");
                }
            }
        }

        internal static void RefreshIndustriesAfterBatch(string source)
        {
            if (_industryApplyBatchDepth > 0)
            {
                _industryRefreshPending = true;
                return;
            }

            ApplyIndustryOrdering();
            var scrubbedCacheCount = ScrubIndustryComponentCaches(source);
            Messenger.Default.Send(default(IndustriesDidChange));
            FuseIndustryRuntimeIndex.Instance.Rebuild();
            FuseIndustryComponentRuntimeIndex.Instance.Rebuild();
            var industryCount = UnityEngine.Object.FindObjectsOfType<Industry>(true).Length;
            var componentCount = UnityEngine.Object.FindObjectsOfType<IndustryComponent>(true).Length;
            FuseLog.Info($"FUSE refreshed industries after '{source}' sceneIndustryCount={industryCount} sceneIndustryComponentCount={componentCount} cacheIndustryCount={FuseIndustryRuntimeIndex.Instance.Count} cacheIndustryComponentCount={FuseIndustryComponentRuntimeIndex.Instance.Count} staleCacheScrubs={scrubbedCacheCount}.");
            foreach (var industryId in FuseCreatedIndustryIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray())
            {
                var industry = GetIndustry(industryId);
                if (industry == null)
                {
                    FuseLog.Warning($"FUSE-created industry '{industryId}' was not found after '{source}'.");
                    continue;
                }

                var railComponentCount = industry.GetComponentsInChildren<IndustryComponent>(true)
                    .Count(component => component != null && !string.IsNullOrWhiteSpace(component.subIdentifier));
                FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.Industry, industryId, out FuseIndustry sourceDefinition);
                var sourceComponentCount = sourceDefinition?.Components?.Count ?? 0;
                if (railComponentCount == 0 && sourceComponentCount == 0)
                {
                    FuseLog.Info($"FUSE-created source-empty industry shell '{industryId}' name='{industry.name}' runtimeComponents=0 sourceComponents=0.");
                    continue;
                }

                FuseLog.Info($"FUSE-created industry '{industryId}' name='{industry.name}' runtimeComponents={railComponentCount} sourceComponents={sourceComponentCount}.");
            }
        }

        internal static string LocationPanelSortKey(Industry industry, string fallback)
        {
            if (industry != null &&
                !string.IsNullOrWhiteSpace(industry.identifier) &&
                IndustryOrders.TryGetValue(industry.identifier, out var order))
            {
                var signedSortKey = (long)order - int.MinValue;
                return signedSortKey.ToString("D10") + "|" + (fallback ?? string.Empty);
            }

            return "Z|" + (fallback ?? string.Empty);
        }

        private static void ApplyIndustryOrdering()
        {
            var areas = UnityEngine.Object.FindObjectsOfType<Area>(true);
            var orderedCount = 0;
            foreach (var area in areas)
            {
                if (area == null)
                {
                    continue;
                }

                var orderedIndustries = area.GetComponentsInChildren<Industry>(true)
                    .Where(industry =>
                        industry != null &&
                        industry.transform.parent == area.transform &&
                        !string.IsNullOrWhiteSpace(industry.identifier) &&
                        IndustryOrders.ContainsKey(industry.identifier))
                    .OrderBy(industry => IndustryOrders[industry.identifier])
                    .ThenBy(industry => industry.name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (orderedIndustries.Length == 0)
                {
                    continue;
                }

                var firstIndex = orderedIndustries.Min(industry => industry.transform.GetSiblingIndex());
                for (var index = 0; index < orderedIndustries.Length; index++)
                {
                    orderedIndustries[index].transform.SetSiblingIndex(firstIndex + index);
                }

                orderedCount += orderedIndustries.Length;
            }

            if (orderedCount > 0)
            {
                FuseLog.Info($"FUSE applied industry ordering for {orderedCount} industry object(s).");
            }
        }

        private static void RememberIndustryOrder(string id, int? order)
        {
            if (order.HasValue)
            {
                IndustryOrders[id] = order.Value;
                return;
            }

            IndustryOrders.Remove(id);
        }

        private static string DescribeIndustryParent(Transform parent)
        {
            if (parent == null)
            {
                return "<none>";
            }

            var area = parent.GetComponent<Area>();
            if (area != null)
            {
                return $"{parent.name} (Area id='{area.identifier ?? string.Empty}')";
            }

            var ops = parent.GetComponent<OpsController>();
            if (ops != null)
            {
                return $"{parent.name} (OpsController)";
            }

            return $"{parent.name} ({parent.gameObject.GetType().Name})";
        }

        private static string DescribeComponent(IndustryComponent component)
        {
            if (component == null)
            {
                return "<null>";
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(component.Identifier))
                {
                    return component.Identifier;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE could not read industry component Identifier for '{component.name}'", ex);
            }

            return string.IsNullOrWhiteSpace(component.subIdentifier) ? component.name : component.subIdentifier;
        }
    }
}
