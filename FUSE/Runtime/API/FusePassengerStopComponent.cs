using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Progression;
using Model.Ops;
using Model.Ops.Definition;
using Model.Ops.Timetable;
using FUSE.Authoring.Data;
using FUSE.Runtime.Events;
using FUSE.Infrastructure;
using Track;
using UnityEngine;
using KeyValue.Runtime;

namespace FUSE.Runtime.API
{
    public sealed class FusePassengerStopComponent : IndustryComponent, IFuseAppliedComponent
    {
        private static readonly FieldInfo AllPassengerStopsField = typeof(PassengerStop).GetField("_allPassengerStops", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo PassengerStopSpansField = typeof(PassengerStop).GetField("_spans", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo PassengerStopMarkersField = typeof(PassengerStop).GetField("_markers", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo KeyValueObjectField = typeof(PassengerStop).GetField("_keyValueObject", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly HashSet<string> LoggedInvalidSpanWarnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _subscribedToGraphRefresh;

        public string PassengerStopId { get; set; }
        public string TimetableCode { get; set; }
        public int BasePopulation { get; set; } = 40;
        public string[] NeighborIds { get; set; } = Array.Empty<string>();
        public string Branch { get; set; }
        public FusePassengerBranch[] BranchDefinitions { get; set; } = Array.Empty<FusePassengerBranch>();
        public Load PassengerLoad { get; set; }

        public override bool IsVisible => false;

        protected override void ValidateIndustryComponent()
        {
            // Passenger stops are a FUSE-owned virtual IndustryComponent used
            // to materialize Railroader PassengerStop objects. Legacy station
            // files may intentionally omit freight car filters or platform
            // spans, so the base IndustryComponent validation would emit
            // misleading errors for valid virtual stops.
        }

        private void OnEnable()
        {
            SubscribeToGraphRefresh();
            if (PassengerLoad != null && !string.IsNullOrWhiteSpace(GetStopIdentifier()))
            {
                TryRefreshPassengerStop("OnEnable");
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromGraphRefresh();
        }

        private void OnDestroy()
        {
            UnsubscribeFromGraphRefresh();
        }

        public void OnFuseDefinitionApplied()
        {
            SubscribeToGraphRefresh();
            if (PassengerLoad != null && !string.IsNullOrWhiteSpace(GetStopIdentifier()))
            {
                TryRefreshPassengerStop("OnFuseDefinitionApplied");
            }
        }

        public override void Service(IIndustryContext ctx)
        {
        }

        public override void OrderCars(IIndustryContext ctx)
        {
        }

        private void SubscribeToGraphRefresh()
        {
            if (_subscribedToGraphRefresh)
            {
                return;
            }

            FuseEvents.GraphRebuilt += OnGraphRebuilt;
            _subscribedToGraphRefresh = true;
        }

        private void UnsubscribeFromGraphRefresh()
        {
            if (!_subscribedToGraphRefresh)
            {
                return;
            }

            FuseEvents.GraphRebuilt -= OnGraphRebuilt;
            _subscribedToGraphRefresh = false;
        }

        private void OnGraphRebuilt()
        {
            if (isActiveAndEnabled && PassengerLoad != null && !string.IsNullOrWhiteSpace(GetStopIdentifier()))
            {
                TryRefreshPassengerStop("GraphRebuilt");
            }
        }

        private void TryRefreshPassengerStop(string source)
        {
            try
            {
                RefreshPassengerStop();
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE passenger stop refresh failed source='{source ?? string.Empty}' " +
                    $"id='{GetStopIdentifier() ?? string.Empty}' component='{Identifier ?? string.Empty}' " +
                    $"message='{ex.Message}'.");
            }
        }

        private void RefreshPassengerStop()
        {
            var stopIdentifier = GetStopIdentifier();
            if (string.IsNullOrWhiteSpace(stopIdentifier))
            {
                throw new InvalidOperationException($"Passenger stop component '{Identifier}' is missing a passenger stop identifier.");
            }

            if (PassengerLoad == null)
            {
                throw new InvalidOperationException($"Passenger stop component '{Identifier}' is missing a valid passenger load.");
            }

            var oldStop = StationAPI.GetPassengerStop(stopIdentifier);

            if (oldStop != null)
            {
                KeyValueObject keyValue = (KeyValueObject)KeyValueObjectField.GetValue(oldStop);
                if (keyValue != null)
                    DestroyImmediate(keyValue);
                DestroyImmediate(oldStop);
            }

            var stopRoot = transform.Find(string.IsNullOrWhiteSpace(name) ? stopIdentifier : name);
            var stopObject = stopRoot != null ? stopRoot.gameObject : new GameObject("PassengerStop");
            stopObject.transform.SetParent(transform, false);
            stopObject.SetActive(false);

            var passengerStop = stopObject.GetComponent<PassengerStop>() ?? stopObject.AddComponent<PassengerStop>();
            passengerStop.identifier = stopIdentifier;
            passengerStop.passengerLoad = PassengerLoad;
            passengerStop.basePopulation = BasePopulation;
            passengerStop.timetableCode = string.IsNullOrWhiteSpace(TimetableCode) ? stopIdentifier : TimetableCode;

            // ProgressionDisabled inheritance: when MapFeatureManager.UpdateFeatureForUnlocked
            // walks a feature's areasEnableOnUnlock, it iterates `a.Industries`
            // and `a.GetComponentsInChildren<PassengerStop>()` and sets
            // ProgressionDisabled on each — but it never touches the
            // intermediate IndustryComponent layer (us). If FUSE refreshes the
            // PassengerStop AFTER that pass, sourcing the flag from our own
            // (untouched, default-false) `ProgressionDisabled` would clear the
            // game's correct decision. Prefer the parent Industry's flag,
            // which IS managed by the game's feature progression pass —
            // falling back to our own field only when the industry lookup
            // fails.
            //
            // VERIFY-NOT-GUESS: this is a hypothesis. The verbose log below
            // captures the actual values so the hypothesis can be confirmed
            // (or refuted) from the log, not from reading code.
            var parentIndustry = GetComponentInParent<Industry>();
            var componentDisabled = ProgressionDisabled;
            var industryDisabled = parentIndustry != null ? parentIndustry.ProgressionDisabled : false;
            var chosenDisabled = parentIndustry != null ? industryDisabled : componentDisabled;
            passengerStop.ProgressionDisabled = chosenDisabled;

            if (FuseSettings.VerboseApplyReportDetails)
            {
                FuseLog.Info(
                    $"FUSE diag TryRefreshPassengerStop ProgressionDisabled decision id='{stopIdentifier}' " +
                    $"componentId='{Identifier ?? "<none>"}' componentDisabled={componentDisabled} " +
                    $"parentIndustry='{(parentIndustry != null ? parentIndustry.identifier : "<none>")}' " +
                    $"industryDisabled={industryDisabled} " +
                    $"wrote passengerStop.ProgressionDisabled={chosenDisabled}.");
            }

            var boundSpans = ResolveBoundSpans().ToArray();
            stopObject.name = string.IsNullOrWhiteSpace(name) ? stopIdentifier : name;
            passengerStop.neighbors = ResolveNeighbors().ToArray();
            ConfigureTimetable(passengerStop);

            stopObject.SetActive(true);
            PassengerStopSpansField?.SetValue(passengerStop, boundSpans);
            PassengerStopMarkersField?.SetValue(passengerStop, RebuildPassengerStopMarkers(passengerStop.transform, boundSpans));
            var cacheCount = RefreshPassengerStopCache();
            FuseLog.Info(
                $"FUSE passenger stop refreshed id='{stopIdentifier}' " +
                $"component='{Identifier}' spanCount={boundSpans.Length} " +
                $"loadId='{PassengerLoad.id ?? string.Empty}' cacheCount={cacheCount}.");
        }

        private IEnumerable<TrackSpan> ResolveBoundSpans()
        {
            var spans = TrackSpans ?? Array.Empty<TrackSpan>();
            var valid = new List<TrackSpan>();
            var invalid = new List<string>();

            foreach (var span in spans)
            {
                if (span == null)
                {
                    invalid.Add("<null>");
                    continue;
                }

                var lower = span.lower;
                var upper = span.upper;
                if (lower != null && lower.Value.segment != null &&
                    upper != null && upper.Value.segment != null)
                {
                    valid.Add(span);
                    continue;
                }

                invalid.Add(string.IsNullOrWhiteSpace(span.id) ? span.name : span.id);
            }

            if (invalid.Count > 0)
            {
                var key = $"{Identifier ?? string.Empty}|{GetStopIdentifier() ?? string.Empty}|{string.Join(",", invalid.ToArray())}";
                if (LoggedInvalidSpanWarnings.Add(key))
                {
                    FuseLog.Warning(
                        $"FUSE passenger stop id='{GetStopIdentifier() ?? string.Empty}' " +
                        $"component='{Identifier ?? string.Empty}' ignored invalid span binding(s): {string.Join(", ", invalid.ToArray())}. " +
                        "The stop will refresh again after graph rebuilds.");
                }
            }

            return valid;
        }

        private HashSet<TrackMarker> RebuildPassengerStopMarkers(Transform stopTransform, IEnumerable<TrackSpan> boundSpans)
        {
            for (var index = stopTransform.childCount - 1; index >= 0; index--)
            {
                var child = stopTransform.GetChild(index);
                foreach (var marker in child.GetComponentsInChildren<TrackMarker>(true))
                {
                    if (marker != null)
                    {
                        marker.enabled = false;
                    }
                }

                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }

            var markers = new HashSet<TrackMarker>();
            var graph = Graph.Shared;
            if (graph == null)
            {
                return markers;
            }

            foreach (var sourceSpan in boundSpans ?? Enumerable.Empty<TrackSpan>())
            {
                if (sourceSpan == null || sourceSpan.lower == null || sourceSpan.upper == null)
                {
                    continue;
                }

                var child = new GameObject("PassengerStopMarker");
                child.SetActive(false);
                child.transform.SetParent(stopTransform, false);
                var marker = child.AddComponent<TrackMarker>();
                marker.type = TrackMarkerType.PassengerStop;
                marker.Location = graph.Lerp(sourceSpan.lower.Value, sourceSpan.upper.Value, 0.5f);
                child.SetActive(true);
                markers.Add(marker);
            }

            return markers;
        }

        private IEnumerable<PassengerStop> ResolveNeighbors()
        {
            if (NeighborIds == null || NeighborIds.Length == 0)
            {
                return Enumerable.Empty<PassengerStop>();
            }

            var neighborCodes = new HashSet<string>(NeighborIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
            return PassengerStop.FindAll().Where(stop =>
                stop != null &&
                stop != this &&
                ((!string.IsNullOrWhiteSpace(stop.identifier) && neighborCodes.Contains(stop.identifier)) ||
                 (!string.IsNullOrWhiteSpace(stop.timetableCode) && neighborCodes.Contains(stop.timetableCode))));
        }

        private void ConfigureTimetable(PassengerStop passengerStop)
        {
            if (string.IsNullOrWhiteSpace(passengerStop.timetableCode))
            {
                return;
            }

            var controller = TimetableController.Shared;
            if (controller == null)
            {
                return;
            }

            if (controller.branches == null)
            {
                controller.branches = new List<TimetableBranch>();
            }

            var branchDefinitions = BuildBranchDefinitions();
            for (var index = 0; index < branchDefinitions.Count; index++)
            {
                var branchDefinition = branchDefinitions[index];
                var junctionType = TimetableStation.JunctionType.None;
                if (branchDefinitions.Count > 1)
                {
                    junctionType = index == 0
                        ? TimetableStation.JunctionType.JunctionStation
                        : TimetableStation.JunctionType.JunctionDuplicate;
                }

                var branch = controller.branches.FirstOrDefault(candidate => string.Equals(candidate.name, branchDefinition.Branch, StringComparison.OrdinalIgnoreCase));
                if (branch == null)
                {
                    branch = new TimetableBranch
                    {
                        name = branchDefinition.Branch,
                        stations = new List<TimetableStation>()
                    };
                    controller.branches.Add(branch);
                }

                if (branch.stations == null)
                {
                    branch.stations = new List<TimetableStation>();
                }

                var existingStation = branch.stations.FirstOrDefault(station => string.Equals(station.code, passengerStop.timetableCode, StringComparison.OrdinalIgnoreCase));
                if (existingStation == null)
                {
                    var additions = new List<TimetableStation>
                    {
                        new TimetableStation
                        {
                            passengerStop = passengerStop,
                            code = passengerStop.timetableCode,
                            name = passengerStop.TimetableName,
                            junctionType = junctionType,
                            traverseTimeToNext = branchDefinition.TraverseTimeToNext,
                            mapFeature = ResolveMapFeature(branchDefinition.MapFeature)
                        }
                    };

                    if (branchDefinition.Intermediates != null)
                    {
                        foreach (var intermediate in branchDefinition.Intermediates)
                        {
                            additions.Add(new TimetableStation
                            {
                                passengerStop = null,
                                code = intermediate.Value?.Code,
                                name = intermediate.Key,
                                junctionType = TimetableStation.JunctionType.None,
                                traverseTimeToNext = intermediate.Value?.TraverseTimeToNext ?? 0,
                                mapFeature = ResolveMapFeature(branchDefinition.MapFeature)
                            });
                        }
                    }

                    branch.stations.AddRange(additions);
                    continue;
                }

                existingStation.code = passengerStop.timetableCode;
                existingStation.name = passengerStop.TimetableName;
                existingStation.passengerStop = passengerStop;
                existingStation.junctionType = junctionType;
                if (branchDefinition.TraverseTimeToNext != 0)
                {
                    existingStation.traverseTimeToNext = branchDefinition.TraverseTimeToNext;
                }

                if (!string.IsNullOrWhiteSpace(branchDefinition.MapFeature))
                {
                    existingStation.mapFeature = ResolveMapFeature(branchDefinition.MapFeature);
                }
            }
        }

        private List<FusePassengerBranch> BuildBranchDefinitions()
        {
            var branchDefinitions = BranchDefinitions != null
                ? new List<FusePassengerBranch>(BranchDefinitions.Where(definition => definition != null))
                : new List<FusePassengerBranch>();

            if (!string.IsNullOrWhiteSpace(Branch))
            {
                foreach (var branchName in Branch.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (branchDefinitions.Any(definition => string.Equals(definition.Branch, branchName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    branchDefinitions.Add(new FusePassengerBranch
                    {
                        Branch = branchName,
                        TraverseTimeToNext = 0
                    });
                }
            }

            if (branchDefinitions.Count == 0)
            {
                branchDefinitions.Add(new FusePassengerBranch
                {
                    Branch = "Main",
                    TraverseTimeToNext = 0
                });
            }

            return branchDefinitions;
        }

        private static MapFeature ResolveMapFeature(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return null;
            }

            var manager = MapFeatureManager.Shared;
            return manager?.AvailableFeatures?.FirstOrDefault(feature =>
                string.Equals(feature.identifier, identifier, StringComparison.OrdinalIgnoreCase));
        }

        private string GetStopIdentifier()
        {
            return string.IsNullOrWhiteSpace(PassengerStopId) ? subIdentifier : PassengerStopId;
        }

        private static int RefreshPassengerStopCache()
        {
            var stops = UnityEngine.Object.FindObjectsOfType<PassengerStop>(true)
                .Where(stop => stop != null)
                .ToArray();

            var previousValue = AllPassengerStopsField?.GetValue(null) as PassengerStop[];
            AllPassengerStopsField?.SetValue(null, stops);

            if (FuseSettings.VerboseApplyReportDetails)
            {
                try
                {
                    var disabledCount = stops.Count(s => s != null && s.ProgressionDisabled);
                    var enabledCount = stops.Length - disabledCount;
                    FuseLog.Info(
                        $"FUSE diag PassengerStop._allPassengerStops cache refreshed " +
                        $"previousCount={previousValue?.Length ?? 0} newCount={stops.Length} " +
                        $"newEnabled={enabledCount} newDisabled={disabledCount}.");
                }
                catch (Exception ex)
                {
                    FuseLog.Warning($"FUSE diag cache refresh log failed: {ex.Message}");
                }
            }

            return stops.Length;
        }
    }
}
