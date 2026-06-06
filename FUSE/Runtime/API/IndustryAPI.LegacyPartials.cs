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

        private static FuseIndustryComponent MaterializeMissingPartialComponent(Industry industry, string subId, FuseIndustryComponent definition)
        {
            if (definition == null ||
                !definition.Partial ||
                !string.IsNullOrWhiteSpace(definition.Type) ||
                !HasTrackSpanPatch(definition))
            {
                return null;
            }

            var materialized = CloneComponentDefinition(definition);
            materialized.Partial = false;
            var inferredLoad = InferLegacyPartialComponentLoad(industry, subId, materialized);
            if (string.IsNullOrWhiteSpace(materialized.LoadId) &&
                !string.IsNullOrWhiteSpace(inferredLoad?.LoadId))
            {
                materialized.LoadId = inferredLoad.LoadId;
            }

            var snapshot = FuseBaseGameIndustrySnapshot.Find(industry?.identifier, subId);
            if (snapshot != null)
            {
                FuseLog.Info(
                    $"FUSE recovered destroyed base-game component '{industry.identifier}.{subId}' " +
                    $"from snapshot type='{snapshot.ComponentTypeFullName}' loadId='{snapshot.LoadId ?? "<null>"}' " +
                    $"existingSpans=[{string.Join(",", snapshot.TrackSpanIds)}].");

                if (string.IsNullOrWhiteSpace(materialized.LoadId) &&
                    !string.IsNullOrWhiteSpace(snapshot.LoadId))
                {
                    materialized.LoadId = snapshot.LoadId;
                }

                if (string.IsNullOrWhiteSpace(materialized.Name) &&
                    !string.IsNullOrWhiteSpace(snapshot.Name))
                {
                    materialized.Name = snapshot.Name;
                }

                materialized.Type = ResolveSnapshotComponentTypeAlias(snapshot.ComponentTypeFullName)
                    ?? InferMissingPartialComponentType(subId, inferredLoad);

                MergeSnapshotTrackSpansIntoMaterialized(materialized, snapshot.TrackSpanIds);
            }
            else
            {
                materialized.Type = InferMissingPartialComponentType(subId, inferredLoad);
            }
            var legacyInterchangeTarget = string.Equals(
                    FuseIndustryComponentTypes.Normalize(materialized.Type),
                    FuseIndustryComponentTypes.Interchange,
                    StringComparison.OrdinalIgnoreCase)
                ? FindLegacyInterchangeMaterializationTarget(industry, definition)
                : null;
            if (legacyInterchangeTarget != null)
            {
                var targetSpanIds = legacyInterchangeTarget.trackSpans?
                    .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id))
                    .Select(span => span.id)
                    .ToArray();
                if (targetSpanIds != null && targetSpanIds.Length > 0)
                {
                    materialized.TrackSpanIds = targetSpanIds;
                    materialized.TrackSpanPatch = null;
                }
            }

            if (string.IsNullOrWhiteSpace(materialized.Name))
            {
                materialized.Name = InferMissingPartialComponentName(industry, subId, materialized, legacyInterchangeTarget);
            }

            if (string.IsNullOrWhiteSpace(materialized.CarTypeFilter) &&
                ShouldDefaultMaterializedCarTypeFilter(materialized.Type))
            {
                materialized.CarTypeFilter = "*";
            }

            return materialized;
        }

        private static string ResolveSnapshotComponentTypeAlias(string componentTypeFullName)
        {
            if (string.IsNullOrWhiteSpace(componentTypeFullName))
            {
                return null;
            }

            // Map the runtime IndustryComponent System.Type.FullName onto the FUSE type
            // alias the rest of the materialization pipeline expects. We avoid hardcoding
            // the assembly-qualified form so the materialized definition stays consistent
            // with what the converter produces.
            if (componentTypeFullName.EndsWith("IndustryUnloader", StringComparison.Ordinal) ||
                componentTypeFullName.EndsWith(".IndustryUnloader", StringComparison.Ordinal))
            {
                return FuseIndustryComponentTypes.Unloader;
            }

            if (componentTypeFullName.EndsWith("IndustryLoader", StringComparison.Ordinal) ||
                componentTypeFullName.EndsWith(".IndustryLoader", StringComparison.Ordinal))
            {
                return FuseIndustryComponentTypes.Loader;
            }

            if (componentTypeFullName.EndsWith("InterchangedIndustryUnloader", StringComparison.Ordinal))
            {
                return FuseIndustryComponentTypes.InterchangedUnloader;
            }

            if (componentTypeFullName.EndsWith("InterchangedIndustryLoader", StringComparison.Ordinal))
            {
                return FuseIndustryComponentTypes.InterchangedLoader;
            }

            if (componentTypeFullName.EndsWith("Interchange", StringComparison.Ordinal))
            {
                return FuseIndustryComponentTypes.Interchange;
            }

            if (componentTypeFullName.EndsWith("RepairTrack", StringComparison.Ordinal))
            {
                return FuseIndustryComponentTypes.RepairTrack;
            }

            if (componentTypeFullName.EndsWith("TeamTrack", StringComparison.Ordinal))
            {
                return FuseIndustryComponentTypes.TeamTrack;
            }

            if (componentTypeFullName.EndsWith("FormulaicIndustryComponent", StringComparison.Ordinal))
            {
                return FuseIndustryComponentTypes.Formulaic;
            }

            return null;
        }

        private static void MergeSnapshotTrackSpansIntoMaterialized(FuseIndustryComponent materialized, string[] snapshotSpanIds)
        {
            if (materialized == null || snapshotSpanIds == null || snapshotSpanIds.Length == 0)
            {
                return;
            }

            // Prepend the snapshot's existing spans onto whatever the patch is adding, so
            // the legacy {"$add": ...} entries layer on top of the original base-game
            // configuration instead of replacing it.
            if (materialized.TrackSpanPatch != null)
            {
                materialized.TrackSpanPatch = PrependSpansToPatch(materialized.TrackSpanPatch, snapshotSpanIds);
            }

            var existingIds = materialized.TrackSpanIds ?? Array.Empty<string>();
            var combined = new List<string>(snapshotSpanIds.Length + existingIds.Length);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var spanId in snapshotSpanIds)
            {
                if (!string.IsNullOrWhiteSpace(spanId) && seen.Add(spanId))
                {
                    combined.Add(spanId);
                }
            }
            foreach (var spanId in existingIds)
            {
                if (!string.IsNullOrWhiteSpace(spanId) && seen.Add(spanId))
                {
                    combined.Add(spanId);
                }
            }

            materialized.TrackSpanIds = combined.ToArray();
        }

        private static FuseStringListPatch PrependSpansToPatch(FuseStringListPatch patch, string[] snapshotSpanIds)
        {
            if (patch == null)
            {
                return null;
            }

            var prepend = new List<string>(patch.Prepend ?? Array.Empty<string>());
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in prepend)
            {
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    seen.Add(existing);
                }
            }

            var inserts = new List<string>();
            foreach (var spanId in snapshotSpanIds)
            {
                if (!string.IsNullOrWhiteSpace(spanId) && seen.Add(spanId))
                {
                    inserts.Add(spanId);
                }
            }

            // Insert at the front so subsequent $add / $append entries come AFTER the
            // snapshot's original spans (matching the legacy ordering).
            inserts.AddRange(prepend);
            return new FuseStringListPatch
            {
                Replace = patch.Replace,
                Prepend = inserts.ToArray(),
                Add = patch.Add,
                Append = patch.Append,
                Insert = patch.Insert,
                Remove = patch.Remove
            };
        }

        private static string InferMissingPartialComponentType(string subId, LegacyPartialLoadInference inferredLoad)
        {
            if (IsLegacyInterchangeAlias(subId))
            {
                return FuseIndustryComponentTypes.Interchange;
            }

            if (inferredLoad != null)
            {
                if (inferredLoad.IsInput && !inferredLoad.IsOutput)
                {
                    return FuseIndustryComponentTypes.Unloader;
                }

                if (inferredLoad.IsOutput && !inferredLoad.IsInput)
                {
                    return FuseIndustryComponentTypes.Loader;
                }
            }

            return LegacyEmptyComponentType;
        }

        private static string InferMissingPartialComponentName(
            Industry industry,
            string subId,
            FuseIndustryComponent definition,
            IndustryComponent legacyInterchangeTarget)
        {
            if (string.Equals(
                    FuseIndustryComponentTypes.Normalize(definition?.Type),
                    FuseIndustryComponentTypes.Interchange,
                    StringComparison.OrdinalIgnoreCase))
            {
                var targetName = ReadDisplayName(legacyInterchangeTarget);
                if (!LooksLikeRawLegacyDisplayName(targetName))
                {
                    return targetName;
                }
            }

            if (!LooksLikeRawLegacyDisplayName(industry?.name))
            {
                return industry.name;
            }

            return subId;
        }

        private sealed class LegacyPartialLoadInference
        {
            public string LoadId { get; set; }
            public bool IsInput { get; set; }
            public bool IsOutput { get; set; }
        }

        private static LegacyPartialLoadInference InferLegacyPartialComponentLoad(
            Industry industry,
            string subId,
            FuseIndustryComponent definition)
        {
            if (industry == null)
            {
                return null;
            }

            foreach (var loadId in GetLegacyPartialLoadCandidates(subId, definition))
            {
                var inference = FindFormulaLoadRole(industry, loadId);
                if (inference != null)
                {
                    return inference;
                }
            }

            var explicitLoadId = definition?.LoadId;
            return string.IsNullOrWhiteSpace(explicitLoadId)
                ? null
                : new LegacyPartialLoadInference { LoadId = explicitLoadId.Trim() };
        }

        private static IEnumerable<string> GetLegacyPartialLoadCandidates(string subId, FuseIndustryComponent definition)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in new[] { definition?.LoadId, subId })
            {
                var candidate = value?.Trim();
                if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        private static LegacyPartialLoadInference FindFormulaLoadRole(Industry industry, string loadId)
        {
            if (industry == null || string.IsNullOrWhiteSpace(loadId))
            {
                return null;
            }

            var inference = new LegacyPartialLoadInference { LoadId = loadId.Trim() };
            foreach (var formulaic in industry.GetComponentsInChildren<FormulaicIndustryComponent>(true))
            {
                if (formulaic == null)
                {
                    continue;
                }

                inference.IsInput |= ContainsFormulaLoad(formulaic.inputTerms, inference.LoadId);
                inference.IsOutput |= ContainsFormulaLoad(formulaic.outputTerms, inference.LoadId);
            }

            return inference.IsInput || inference.IsOutput ? inference : null;
        }

        private static bool ContainsFormulaLoad(IEnumerable<FormulaicIndustryComponent.Term> terms, string loadId)
        {
            if (terms == null || string.IsNullOrWhiteSpace(loadId))
            {
                return false;
            }

            return terms.Any(term =>
                term.load != null &&
                string.Equals(term.load.id, loadId, StringComparison.OrdinalIgnoreCase));
        }

        private static Interchange FindLegacyInterchangeMaterializationTarget(Industry industry, FuseIndustryComponent definition)
        {
            var requestedSpanIds = GetTrackSpanPatchReferenceIds(definition);
            if (industry == null || requestedSpanIds.Length == 0)
            {
                return null;
            }

            var requested = new HashSet<string>(requestedSpanIds, StringComparer.OrdinalIgnoreCase);
            var area = industry.GetComponentInParent<Area>(true);
            var candidates = area != null
                ? area.GetComponentsInChildren<Interchange>(true)
                : UnityEngine.Object.FindObjectsOfType<Interchange>(true);

            var target = candidates
                .Where(component => component != null)
                .Select(component => new
                {
                    Component = component,
                    Score = CountTrackSpanMatches(component, requested),
                    FullMatch = ContainsAllTrackSpanIds(component, requested)
                })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.FullMatch)
                .ThenByDescending(candidate => candidate.Score)
                .ThenBy(candidate => ReadDisplayName(candidate.Component), StringComparer.OrdinalIgnoreCase)
                .Select(candidate => candidate.Component)
                .FirstOrDefault();

            if (target != null)
            {
                FuseLog.Info(
                    $"FUSE matched missing legacy interchange component for industry '{industry.identifier}' " +
                    $"to overlapping component '{DescribeComponent(target)}'.");
            }

            return target;
        }

        private static string[] GetTrackSpanPatchReferenceIds(FuseIndustryComponent definition)
        {
            var ids = new List<string>();
            AddDistinct(ids, definition?.TrackSpanIds);
            var patch = definition?.TrackSpanPatch;
            if (patch != null)
            {
                AddDistinct(ids, patch.Replace);
                AddDistinct(ids, patch.Prepend);
                AddDistinct(ids, patch.Add);
                AddDistinct(ids, patch.Append);
                AddDistinct(ids, patch.Insert);
            }

            return ids.ToArray();
        }

        private static int CountTrackSpanMatches(IndustryComponent component, HashSet<string> requestedSpanIds)
        {
            if (component?.trackSpans == null || requestedSpanIds == null || requestedSpanIds.Count == 0)
            {
                return 0;
            }

            return component.trackSpans.Count(span =>
                span != null &&
                !string.IsNullOrWhiteSpace(span.id) &&
                requestedSpanIds.Contains(span.id));
        }

        private static bool ContainsAllTrackSpanIds(IndustryComponent component, HashSet<string> requestedSpanIds)
        {
            if (component?.trackSpans == null || requestedSpanIds == null || requestedSpanIds.Count == 0)
            {
                return false;
            }

            var componentSpanIds = new HashSet<string>(
                component.trackSpans
                    .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id))
                    .Select(span => span.id),
                StringComparer.OrdinalIgnoreCase);
            return requestedSpanIds.All(componentSpanIds.Contains);
        }

        private static string ReadDisplayName(IndustryComponent component)
        {
            if (component == null)
            {
                return null;
            }

            try
            {
                return component.DisplayName;
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE could not read display name for component '{component.name}'", ex);
                return component.name;
            }
        }

        private static bool LooksLikeRawLegacyDisplayName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            var text = value.Trim();
            return string.Equals(text, "t1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "interchange", StringComparison.OrdinalIgnoreCase) ||
                   (!text.Any(char.IsWhiteSpace) &&
                    text.Any(ch => ch == '-' || ch == '_' || ch == '.'));
        }

        private static bool ContainsText(string value, string token)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasTrackSpanPatch(FuseIndustryComponent definition)
        {
            return (definition?.TrackSpanIds != null &&
                    definition.TrackSpanIds.Any(id => !string.IsNullOrWhiteSpace(id))) ||
                   HasStringListPatch(definition?.TrackSpanPatch);
        }

        private static FuseIndustryComponent CloneComponentDefinition(FuseIndustryComponent definition)
        {
            if (definition == null)
            {
                return null;
            }

            return new FuseIndustryComponent
            {
                Partial = definition.Partial,
                Type = definition.Type,
                Name = definition.Name,
                TrackSpanIds = definition.TrackSpanIds?.ToArray(),
                TrackSpanPatch = CloneStringListPatch(definition.TrackSpanPatch),
                CarTypeFilter = definition.CarTypeFilter,
                LoadId = definition.LoadId,
                ConvertedLoadId = definition.ConvertedLoadId,
                SharedStorage = definition.SharedStorage,
                StorageChangeRate = definition.StorageChangeRate,
                MaxStorage = definition.MaxStorage,
                CarTransferRate = definition.CarTransferRate,
                CostPerUnit = definition.CostPerUnit,
                NotBeforeHour = definition.NotBeforeHour,
                NotAfterHour = definition.NotAfterHour,
                FillPercentage = definition.FillPercentage,
                BookReasons = definition.BookReasons?.ToArray(),
                Title = definition.Title,
                OrderAroundEmpties = definition.OrderAroundEmpties,
                OrderAroundLoaded = definition.OrderAroundLoaded,
                InputSpanIds = definition.InputSpanIds?.ToArray(),
                InputTermsPerDay = definition.InputTermsPerDay == null ? null : new Dictionary<string, float>(definition.InputTermsPerDay),
                OutputTermsPerDay = definition.OutputTermsPerDay == null ? null : new Dictionary<string, float>(definition.OutputTermsPerDay),
                IdealCars = definition.IdealCars,
                TeamProfiles = definition.TeamProfiles == null ? null : new Dictionary<string, FuseTeamTrackEntry>(definition.TeamProfiles),
                CanOverhaul = definition.CanOverhaul,
                PassengerStopId = definition.PassengerStopId,
                TimetableCode = definition.TimetableCode,
                BasePopulation = definition.BasePopulation,
                NeighborIds = definition.NeighborIds?.ToArray(),
                Branch = definition.Branch,
                BranchDefinitions = definition.BranchDefinitions?.ToArray(),
                OutputSpanIds = definition.OutputSpanIds?.ToArray(),
                CarLoadPeriod = definition.CarLoadPeriod,
                CarLengthFeet = definition.CarLengthFeet,
                Fields = definition.Fields == null ? null : new Dictionary<string, object>(definition.Fields)
            };
        }

        private static FuseStringListPatch CloneStringListPatch(FuseStringListPatch patch)
        {
            if (patch == null)
            {
                return null;
            }

            return new FuseStringListPatch
            {
                Add = patch.Add?.ToArray(),
                Append = patch.Append?.ToArray(),
                Prepend = patch.Prepend?.ToArray(),
                Insert = patch.Insert?.ToArray(),
                Replace = patch.Replace?.ToArray(),
                Remove = patch.Remove?.ToArray()
            };
        }

        private static bool HasStringListPatch(FuseStringListPatch patch)
        {
            return patch != null &&
                   (patch.Add != null ||
                    patch.Append != null ||
                    patch.Prepend != null ||
                    patch.Insert != null ||
                    patch.Replace != null ||
                    patch.Remove != null);
        }

        private static TrackSpan[] ApplyTrackSpanPatch(TrackSpan[] current, FuseStringListPatch patch)
        {
            if (!HasStringListPatch(patch))
            {
                return current ?? Array.Empty<TrackSpan>();
            }

            var ids = new List<string>();
            if (patch.Replace != null)
            {
                AddDistinct(ids, patch.Replace);
            }
            else if (current != null)
            {
                AddDistinct(ids, current
                    .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id))
                    .Select(span => span.id));
            }

            PrependDistinct(ids, patch.Prepend);
            AddDistinct(ids, patch.Add);
            AddDistinct(ids, patch.Append);
            AddDistinct(ids, patch.Insert);
            RemoveIds(ids, patch.Remove);
            return ResolveSpans(ids.ToArray());
        }

        private static void AddDistinct(List<string> target, IEnumerable<string> values)
        {
            if (target == null || values == null)
            {
                return;
            }

            var seen = new HashSet<string>(target.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                var id = value?.Trim();
                if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
                {
                    target.Add(id);
                }
            }
        }

        private static void PrependDistinct(List<string> target, IEnumerable<string> values)
        {
            if (target == null || values == null)
            {
                return;
            }

            var prepend = new List<string>();
            var seen = new HashSet<string>(target.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                var id = value?.Trim();
                if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
                {
                    prepend.Add(id);
                }
            }

            if (prepend.Count > 0)
            {
                target.InsertRange(0, prepend);
            }
        }

        private static void RemoveIds(List<string> target, IEnumerable<string> values)
        {
            if (target == null || values == null)
            {
                return;
            }

            var removals = new HashSet<string>(
                values.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
                StringComparer.OrdinalIgnoreCase);
            if (removals.Count == 0)
            {
                return;
            }

            var retained = target.Where(id => !removals.Contains(id ?? string.Empty)).ToArray();
            target.Clear();
            foreach (var id in retained)
            {
                target.Add(id);
            }
        }

        private static string ResolveCarTypeFilter(IndustryComponent component, string value, bool isPassengerStop)
        {
            if (isPassengerStop && string.IsNullOrWhiteSpace(value))
            {
                return "*";
            }

            if (component is Interchange && string.IsNullOrWhiteSpace(value))
            {
                return "*";
            }

            return value ?? string.Empty;
        }

        private static IndustryComponent ResolveLegacyComponentAlias(
            Industry industry,
            string subId,
            FuseIndustryComponent definition,
            ISet<string> definedSubIds)
        {
            if (industry == null ||
                string.IsNullOrWhiteSpace(subId) ||
                definition?.Partial != true ||
                !string.IsNullOrWhiteSpace(definition.Type) ||
                !HasTrackSpanPatch(definition))
            {
                return null;
            }

            IndustryComponent matched = null;
            if (IsLegacyInterchangeAlias(subId))
            {
                var interchanges = industry.GetComponentsInChildren<Interchange>(true)
                    .Where(component => component != null)
                    .Cast<IndustryComponent>()
                    .ToArray();
                matched = interchanges.FirstOrDefault(component =>
                              string.Equals(component.subIdentifier, "interchange", StringComparison.OrdinalIgnoreCase)) ??
                          (interchanges.Length == 1 ? interchanges[0] : null);
            }

            if (matched == null)
            {
                var inferredLoad = InferLegacyPartialComponentLoad(industry, subId, definition);
                matched = FindLegacyLoadComponentAlias(
                    industry,
                    inferredLoad,
                    definedSubIds);
                if (matched != null &&
                    string.IsNullOrWhiteSpace(definition.LoadId) &&
                    !string.IsNullOrWhiteSpace(inferredLoad?.LoadId) &&
                    string.IsNullOrWhiteSpace(GetDefinition(matched)?.LoadId))
                {
                    definition.LoadId = inferredLoad.LoadId;
                }
            }

            if (matched != null)
            {
                FuseLog.Info(
                    $"FUSE bound legacy partial industry component '{industry.identifier}.{subId}' " +
                    $"to existing component '{DescribeComponent(matched)}'.");
            }

            return matched;
        }

        private static IndustryComponent FindLegacyLoadComponentAlias(
            Industry industry,
            LegacyPartialLoadInference inferredLoad,
            ISet<string> definedSubIds)
        {
            if (industry == null || string.IsNullOrWhiteSpace(inferredLoad?.LoadId))
            {
                return null;
            }

            var inferredRuntimeLoad = ResolveLoad(inferredLoad.LoadId);
            var inferredCarTypes = GetCarTypesForLoad(inferredLoad.LoadId);
            var candidates = industry.GetComponentsInChildren<IndustryComponent>(true)
                .Where(component => component != null && !(component is FormulaicIndustryComponent))
                .Select(component => new LegacyLoadAliasCandidate
                {
                    Component = component,
                    LoadId = GetDefinition(component)?.LoadId,
                    AcceptsInferredLoad = ComponentAcceptsCarsWithLoad(component, inferredRuntimeLoad),
                    LoadCarTypeMatchCount = CountLoadCompatibleCarTypes(component, inferredCarTypes)
                })
                .ToArray();

            var exact = candidates
                .Where(candidate =>
                    string.Equals(candidate.LoadId, inferredLoad.LoadId, StringComparison.OrdinalIgnoreCase) ||
                    candidate.AcceptsInferredLoad)
                .OrderByDescending(candidate => ScoreLegacyLoadComponentAlias(candidate.Component, inferredLoad))
                .ThenByDescending(candidate => candidate.LoadCarTypeMatchCount)
                .ThenBy(candidate => candidate.Component.subIdentifier, StringComparer.OrdinalIgnoreCase)
                .Select(candidate => candidate.Component)
                .FirstOrDefault();
            if (exact != null)
            {
                return exact;
            }

            var compatibleCarType = FindCarTypeLegacyLoadComponentAlias(candidates, inferredLoad, definedSubIds);
            if (compatibleCarType != null)
            {
                return compatibleCarType;
            }

            return FindDirectionalLegacyLoadComponentAlias(candidates, inferredLoad, definedSubIds);
        }

        private static IndustryComponent FindCarTypeLegacyLoadComponentAlias(
            IEnumerable<LegacyLoadAliasCandidate> candidates,
            LegacyPartialLoadInference inferredLoad,
            ISet<string> definedSubIds)
        {
            if (candidates == null || inferredLoad == null)
            {
                return null;
            }

            var ranked = candidates
                .Where(candidate => candidate.Component != null)
                .Where(candidate => string.IsNullOrWhiteSpace(candidate.LoadId))
                .Where(candidate => candidate.LoadCarTypeMatchCount > 0)
                .Where(candidate => !IsDefinedLegacyComponent(candidate.Component, definedSubIds))
                .Where(candidate => IsLegacyLoadDirectionMatch(candidate.Component, inferredLoad))
                .Select(candidate => new
                {
                    candidate.Component,
                    candidate.LoadCarTypeMatchCount,
                    Score = ScoreLegacyLoadComponentAlias(candidate.Component, inferredLoad)
                })
                .OrderByDescending(candidate => candidate.LoadCarTypeMatchCount)
                .ThenByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Component.subIdentifier, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (ranked.Length == 0)
            {
                return null;
            }

            if (ranked.Length > 1 &&
                ranked[0].LoadCarTypeMatchCount == ranked[1].LoadCarTypeMatchCount &&
                ranked[0].Score == ranked[1].Score)
            {
                return null;
            }

            return ranked[0].Component;
        }

        private static IndustryComponent FindDirectionalLegacyLoadComponentAlias(
            IEnumerable<LegacyLoadAliasCandidate> candidates,
            LegacyPartialLoadInference inferredLoad,
            ISet<string> definedSubIds)
        {
            if (candidates == null || inferredLoad == null)
            {
                return null;
            }

            var directional = candidates
                .Where(candidate => candidate.Component != null)
                .Where(candidate => string.IsNullOrWhiteSpace(candidate.LoadId))
                .Where(candidate => !IsDefinedLegacyComponent(candidate.Component, definedSubIds))
                .Where(candidate => IsLegacyLoadDirectionMatch(candidate.Component, inferredLoad))
                .Select(candidate => candidate.Component)
                .ToArray();

            return directional.Length == 1 ? directional[0] : null;
        }

        private sealed class LegacyLoadAliasCandidate
        {
            public IndustryComponent Component { get; set; }
            public string LoadId { get; set; }
            public bool AcceptsInferredLoad { get; set; }
            public int LoadCarTypeMatchCount { get; set; }
        }

        private static bool ComponentAcceptsCarsWithLoad(IndustryComponent component, Load load)
        {
            if (component == null || load == null)
            {
                return false;
            }

            try
            {
                return component.AcceptsCarsWithLoad(load);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE legacy support could not query load acceptance for component '{DescribeComponent(component)}' " +
                    $"loadId='{load.id ?? string.Empty}': {ex.Message}");
                return false;
            }
        }

        private static string[] GetCarTypesForLoad(string loadId)
        {
            if (string.IsNullOrWhiteSpace(loadId))
            {
                return Array.Empty<string>();
            }

            try
            {
                var prefabStore = TrainController.Shared?.PrefabStore;
                if (prefabStore == null)
                {
                    return Array.Empty<string>();
                }

                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in prefabStore.AllCarDefinitionInfos)
                {
                    var definition = item.Definition;
                    if (definition == null ||
                        definition.LoadSlots == null ||
                        string.IsNullOrWhiteSpace(definition.CarType))
                    {
                        continue;
                    }

                    if (definition.LoadSlots.Any(slot =>
                            slot != null &&
                            string.Equals(slot.RequiredLoadIdentifier, loadId, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(definition.CarType.Trim());
                    }
                }

                return result.ToArray();
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE legacy support could not infer car types for loadId='{loadId.Trim()}': {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static int CountLoadCompatibleCarTypes(IndustryComponent component, IEnumerable<string> carTypes)
        {
            if (component?.carTypeFilter == null || carTypes == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var carType in carTypes)
            {
                if (!string.IsNullOrWhiteSpace(carType) &&
                    component.carTypeFilter.Matches(carType.Trim()))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsDefinedLegacyComponent(IndustryComponent component, ISet<string> definedSubIds)
        {
            return component != null &&
                   definedSubIds != null &&
                   !string.IsNullOrWhiteSpace(component.subIdentifier) &&
                   definedSubIds.Contains(component.subIdentifier);
        }

        private static bool IsLegacyLoadDirectionMatch(IndustryComponent component, LegacyPartialLoadInference inferredLoad)
        {
            if (component == null || inferredLoad == null)
            {
                return false;
            }

            if (inferredLoad.IsInput && !inferredLoad.IsOutput)
            {
                return component is IndustryUnloader;
            }

            if (inferredLoad.IsOutput && !inferredLoad.IsInput)
            {
                return component is IndustryLoaderBase;
            }

            return false;
        }

        private static bool ShouldDefaultMaterializedCarTypeFilter(string type)
        {
            var normalized = FuseIndustryComponentTypes.Normalize(type);
            return string.Equals(normalized, FuseIndustryComponentTypes.Loader, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, FuseIndustryComponentTypes.Unloader, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, FuseIndustryComponentTypes.InterchangedLoader, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, FuseIndustryComponentTypes.InterchangedUnloader, StringComparison.OrdinalIgnoreCase);
        }

        private static int ScoreLegacyLoadComponentAlias(
            IndustryComponent component,
            LegacyPartialLoadInference inferredLoad)
        {
            if (component == null || inferredLoad == null)
            {
                return 0;
            }

            if (inferredLoad.IsInput && !inferredLoad.IsOutput && component is IndustryUnloader)
            {
                return 3;
            }

            if (inferredLoad.IsOutput && !inferredLoad.IsInput && component is IndustryLoader)
            {
                return 3;
            }

            if (component is IndustryLoader || component is IndustryUnloader)
            {
                return 2;
            }

            return 1;
        }

        private static bool IsLegacyInterchangeAlias(string subId)
        {
            return string.Equals(subId, "t1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(subId, "interchange", StringComparison.OrdinalIgnoreCase) ||
                   ContainsText(subId, "interchange");
        }
    }
}
