using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Game.Progression;
using Model.Ops;
using FUSE.API;
using FUSE.Data;
using FUSE.Infrastructure;
using FUSE.Registry;
using Track;
using UnityEngine;
using Helpers;

namespace FUSE.Loading
{
    public static class FuseWorldSuppressor
    {
        public const string SyntheticAreaFeaturePrefix = "fuse.suppression.area.";

        private static readonly FieldInfo MapFeatureManagerFeaturesField =
            typeof(MapFeatureManager).GetField("_features", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly Dictionary<string, PackageSuppressions> Packages =
            new Dictionary<string, PackageSuppressions>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, ScenePathSuppressionState> ScenePaths =
            new Dictionary<string, ScenePathSuppressionState>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, TrackGroupSuppressionState> TrackGroups =
            new Dictionary<string, TrackGroupSuppressionState>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, AreaSuppressionState> Areas =
            new Dictionary<string, AreaSuppressionState>(StringComparer.OrdinalIgnoreCase);

        private static bool _isApplyingGroupSuppressionDuringGraphRebuild;
        private static bool _isRebuildingForGroupSuppression;

        /// <summary>
        /// Registers post-load suppressions declared by a package and applies them
        /// immediately if the relevant runtime systems are available.
        ///
        /// Area suppression creates synthetic MapFeature entries named
        /// fuse.suppression.area.&lt;id&gt;. This keeps the runtime shape close to
        /// Railroader progression, but other mods that introspect MapFeatures will
        /// see these entries. Consumers can filter by SyntheticAreaFeaturePrefix.
        /// </summary>
        public static void ApplyDefinition(FuseModDefinition definition, FuseApplyTransaction transaction = null)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            {
                return;
            }

            var scenePaths = NormalizeIds(definition.World?.SuppressBaseScenePaths, definition.World?.SuppressScenePaths);
            var groups = NormalizeIds(definition.World?.SuppressBaseTrackGroups, definition.World?.SuppressGroups);
            var areas = NormalizeIds(definition.World?.SuppressBaseAreas, definition.World?.SuppressAreas);
            var old = Packages.TryGetValue(definition.Id, out var prior)
                ? prior
                : new PackageSuppressions(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

            Packages[definition.Id] = new PackageSuppressions(scenePaths, groups, areas);

            RegisterScenePathSuppressions(definition.Id, scenePaths, transaction, "definition apply");
            ClaimSuppressions(definition.Id, groups, FuseClaimKind.SuppressedTrackGroup, "track group", transaction);
            ClaimSuppressions(definition.Id, areas, FuseClaimKind.SuppressedArea, "area", transaction);

            RestoreUnusedScenePaths(old.ScenePaths.Except(scenePaths, StringComparer.OrdinalIgnoreCase));
            RestoreUnusedTrackGroups(old.TrackGroups.Except(groups, StringComparer.OrdinalIgnoreCase));
            RestoreUnusedAreas(old.Areas.Except(areas, StringComparer.OrdinalIgnoreCase));

            ApplyActiveScenePathSuppressions("definition apply", transaction);
            ApplyActiveTrackGroupSuppressions("definition apply", true);
            ApplyActiveAreaSuppressions("definition apply", transaction);
        }

        public static void RegisterEarlyScenePathSuppressionsFromLoadedDefinitions(string reason)
        {
            if (!FuseSettings.EnableExperimentalEarlyScenePathSuppression)
            {
                return;
            }

            foreach (var loaded in FuseModLoader.GetLoadedModsInOrder())
            {
                var definition = loaded?.Definition;
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                {
                    continue;
                }

                var scenePaths = NormalizeIds(definition.World?.SuppressBaseScenePaths, definition.World?.SuppressScenePaths);
                if (scenePaths.Length == 0)
                {
                    continue;
                }

                var old = Packages.TryGetValue(definition.Id, out var prior)
                    ? prior
                    : new PackageSuppressions(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
                Packages[definition.Id] = new PackageSuppressions(scenePaths, old.TrackGroups, old.Areas);
                RegisterScenePathSuppressions(definition.Id, scenePaths, null, reason ?? "early window");
                RestoreUnusedScenePaths(old.ScenePaths.Except(scenePaths, StringComparer.OrdinalIgnoreCase));
            }
        }

        public static void ApplyActiveScenePathSuppressions(string reason, FuseApplyTransaction transaction = null)
        {
            foreach (var path in GetClaimedIds(FuseClaimKind.SuppressedScenePath).ToArray())
            {
                TrySuppressScenePath(path, reason ?? "apply", transaction);
            }
        }

        public static bool HasActiveScenePathSuppressions
        {
            get
            {
                return GetClaimedIds(FuseClaimKind.SuppressedScenePath).Any();
            }
        }

        public static IReadOnlyCollection<string> GetActiveScenePathSuppressions()
        {
            return GetClaimedIds(FuseClaimKind.SuppressedScenePath)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static IReadOnlyCollection<string> GetActiveTrackGroupSuppressions()
        {
            return GetClaimedIds(FuseClaimKind.SuppressedTrackGroup)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static IReadOnlyCollection<string> GetActiveAreaSuppressions()
        {
            return GetClaimedIds(FuseClaimKind.SuppressedArea)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static int ActiveSuppressionCount =>
            GetActiveScenePathSuppressions().Count +
            GetActiveTrackGroupSuppressions().Count +
            GetActiveAreaSuppressions().Count;

        public static void ApplyTrackGroupSuppressionsAfterGraphLoad(string reason)
        {
            if (_isRebuildingForGroupSuppression)
            {
                return;
            }

            _isApplyingGroupSuppressionDuringGraphRebuild = true;
            try
            {
                ApplyActiveTrackGroupSuppressions(reason ?? "graph rebuild", true);
            }
            finally
            {
                _isApplyingGroupSuppressionDuringGraphRebuild = false;
            }
        }

        public static void ApplyActiveAreaSuppressions(string reason, FuseApplyTransaction transaction = null)
        {
            foreach (var areaId in GetClaimedIds(FuseClaimKind.SuppressedArea).ToArray())
            {
                TrySuppressArea(areaId, reason ?? "apply", transaction);
            }
        }

        public static void ReleasePackage(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return;
            }

            var claims = FuseRegistry.GetClaimsForPackage(packageId)
                .Where(pair => pair.Key == FuseClaimKind.SuppressedScenePath || pair.Key == FuseClaimKind.SuppressedTrackGroup || pair.Key == FuseClaimKind.SuppressedArea)
                .ToArray();

            Packages.Remove(packageId);

            foreach (var claim in claims)
            {
                try
                {
                    FuseRegistry.Release(claim.Key, claim.Value, packageId);
                }
                catch (Exception ex)
                {
                    FuseLog.Warning($"FUSE failed to release suppression claim kind='{claim.Key}' id='{claim.Value}' package='{packageId}': {ex.Message}");
                }
            }

            foreach (var path in claims.Where(claim => claim.Key == FuseClaimKind.SuppressedScenePath).Select(claim => claim.Value).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (FuseRegistry.GetSharedOwners(FuseClaimKind.SuppressedScenePath, path).Count == 0)
                {
                    FuseLog.Info($"FUSE released final scene-path suppression claim for '{path}' during package unload; target remains suppressed until map unload.");
                }
            }

            foreach (var groupId in claims.Where(claim => claim.Key == FuseClaimKind.SuppressedTrackGroup).Select(claim => claim.Value).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (FuseRegistry.GetSharedOwners(FuseClaimKind.SuppressedTrackGroup, groupId).Count == 0)
                {
                    RestoreTrackGroup(groupId, "package unload");
                }
            }

            foreach (var areaId in claims.Where(claim => claim.Key == FuseClaimKind.SuppressedArea).Select(claim => claim.Value).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (FuseRegistry.GetSharedOwners(FuseClaimKind.SuppressedArea, areaId).Count == 0)
                {
                    RestoreArea(areaId, "package unload");
                }
            }

            FuseLog.Info($"FUSE released {claims.Length} world suppression claim(s) for package '{packageId}'.");
        }

        public static void RestoreAll(string reason)
        {
            var scenePaths = ScenePaths.Keys.ToArray();
            foreach (var path in scenePaths)
            {
                RestoreScenePath(path, reason ?? "restore all");
            }

            var groups = TrackGroups.Keys.ToArray();
            foreach (var groupId in groups)
            {
                RestoreTrackGroup(groupId, reason ?? "restore all");
            }

            var areas = Areas.Keys.ToArray();
            foreach (var areaId in areas)
            {
                RestoreArea(areaId, reason ?? "restore all");
            }

            Packages.Clear();
            FuseLog.Info($"FUSE restored all world suppressions for '{reason ?? "unspecified"}'.");
        }

        private static void RegisterScenePathSuppressions(string packageId, string[] scenePaths, FuseApplyTransaction transaction, string reason)
        {
            if (scenePaths == null || scenePaths.Length == 0)
            {
                return;
            }

            ClaimSuppressions(packageId, scenePaths, FuseClaimKind.SuppressedScenePath, "scene path", transaction);
            FuseLog.Info($"FUSE registered {scenePaths.Length} scene-path suppression request(s) for package '{packageId}' during '{reason ?? "unspecified"}'.");
        }

        private static void ApplyActiveTrackGroupSuppressions(string reason, bool rebuildIfChanged)
        {
            var graph = Graph.Shared;
            if (graph == null)
            {
                if (GetClaimedIds(FuseClaimKind.SuppressedTrackGroup).Any())
                {
                    FuseLog.Warning($"FUSE cannot apply track-group suppressions for '{reason ?? "unspecified"}' because Graph.Shared is not available.");
                }

                return;
            }

            var changed = false;
            foreach (var groupId in GetClaimedIds(FuseClaimKind.SuppressedTrackGroup).ToArray())
            {
                changed = TrySuppressTrackGroup(graph, groupId, reason ?? "apply") || changed;
            }

            if (!changed || !rebuildIfChanged)
            {
                return;
            }

            RebuildAfterTrackGroupSuppression(graph, reason);
        }

        private static bool TrySuppressTrackGroup(Graph graph, string groupId, string reason)
        {
            if (graph == null || string.IsNullOrWhiteSpace(groupId))
            {
                return false;
            }

            try
            {
                if (!TrackGroups.ContainsKey(groupId))
                {
                    TrackGroups[groupId] = new TrackGroupSuppressionState
                    {
                        WasEnabled = graph.enabledGroupIds != null && graph.enabledGroupIds.Contains(groupId),
                        WasAvailable = graph.availableGroupIds != null && graph.availableGroupIds.Contains(groupId)
                    };
                }

                var known = (graph.groupIds != null && graph.groupIds.Contains(groupId)) ||
                            (graph.enabledGroupIds != null && graph.enabledGroupIds.Contains(groupId)) ||
                            (graph.availableGroupIds != null && graph.availableGroupIds.Contains(groupId));
                if (!known)
                {
                    FuseLog.Warning($"FUSE suppressing unknown track group '{groupId}' for '{reason}'. It may be created by another package later.");
                }

                var changed = false;
                changed = graph.SetGroupAvailable(groupId, false) || changed;
                changed = graph.SetGroupEnabled(groupId, false) || changed;
                if (changed)
                {
                    FuseLog.Info($"FUSE suppressed base track group '{groupId}' for '{reason}' owners={FuseRegistry.GetSharedOwners(FuseClaimKind.SuppressedTrackGroup, groupId).Count}.");
                }

                return changed;
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE failed to suppress track group '{groupId}' for '{reason}': {ex.Message}");
                return false;
            }
        }

        private static void TrySuppressScenePath(string path, string reason, FuseApplyTransaction transaction)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                var target = FusePrefabResolver.ResolveScenePath(path) ?? GameObject.Find(path);
                if (target == null)
                {
                    if (!ScenePaths.TryGetValue(path, out var missingState))
                    {
                        missingState = new ScenePathSuppressionState(path);
                        ScenePaths[path] = missingState;
                    }

                    if (!missingState.WarnedMissing)
                    {
                        missingState.WarnedMissing = true;
                        Warn(transaction, "suppressed scene path", path, $"target was not found for '{reason}'");
                    }

                    return;
                }

                if (!ScenePaths.TryGetValue(path, out var state))
                {
                    state = new ScenePathSuppressionState(path);
                    ScenePaths[path] = state;
                }

                if (state.Target != target)
                {
                    state.Target = target;
                    state.WasActive = target.activeSelf;
                    state.WarnedMissing = false;
                    state.ClearCapturedComponents();
                }

                if (target.TryGetComponent<SimpleCuller>(out SimpleCuller culler))
                {
                    UnityEngine.Object.Destroy(culler);
                }

                var changes = ForceSuppressSceneObject(state, target);
                var setInactive = false;
                if (target.activeSelf)
                {
                    target.SetActive(false);
                    setInactive = true;
                }

                if (setInactive || changes.HasChanges)
                {
                    FuseLog.Info($"FUSE suppressed base scene path '{path}' for '{reason}' owners={FuseRegistry.GetSharedOwners(FuseClaimKind.SuppressedScenePath, path).Count} inactive={setInactive} renderers={changes.Renderers} cullers={changes.CullingBehaviours}.");
                    transaction?.PostBind("suppressed scene path", path, $"set inactive={setInactive} renderers={changes.Renderers} cullers={changes.CullingBehaviours}");
                }

            }
            catch (Exception ex)
            {
                Warn(transaction, "suppressed scene path", path, $"suppression failed for '{reason}': {ex.Message}");
            }
        }

        private static ScenePathSuppressionChanges ForceSuppressSceneObject(ScenePathSuppressionState state, GameObject target)
        {
            var changes = new ScenePathSuppressionChanges();
            if (state == null || target == null)
            {
                return changes;
            }

            foreach (var renderer in target.GetComponentsInChildren<Renderer>(true) ?? Array.Empty<Renderer>())
            {
                if (renderer == null)
                {
                    continue;
                }

                state.GetOrAddRendererState(renderer);
                var changed = false;
                if (renderer.enabled)
                {
                    renderer.enabled = false;
                    changed = true;
                }

                if (!renderer.forceRenderingOff)
                {
                    renderer.forceRenderingOff = true;
                    changed = true;
                }

                if (changed)
                {
                    changes.Renderers++;
                }
            }

            foreach (var behaviour in target.GetComponentsInChildren<Behaviour>(true) ?? Array.Empty<Behaviour>())
            {
                if (behaviour == null || !IsRendererCuller(behaviour))
                {
                    continue;
                }

                state.GetOrAddCullingBehaviourState(behaviour);
                if (behaviour.enabled)
                {
                    behaviour.enabled = false;
                    changes.CullingBehaviours++;
                }
            }

            return changes;
        }

        private static bool IsRendererCuller(Behaviour behaviour)
        {
            var type = behaviour != null ? behaviour.GetType() : null;
            if (type == null)
            {
                return false;
            }

            var fullName = type.FullName ?? type.Name ?? string.Empty;
            return fullName.IndexOf("RendererCuller", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void RebuildAfterTrackGroupSuppression(Graph graph, string reason)
        {
            if (_isRebuildingForGroupSuppression)
            {
                return;
            }

            _isRebuildingForGroupSuppression = true;
            try
            {
                if (_isApplyingGroupSuppressionDuringGraphRebuild)
                {
                    graph.RebuildCollections();
                }
                else
                {
                    // Defer to any outer batch the caller may have opened so a
                    // sequence of group-suppression operations folds into one
                    // rebuild instead of one rebuild per group.
                    TrackAPI.RequestRebuild();
                }

                FuseLog.Info($"FUSE rebuilt track graph after group suppression for '{reason ?? "unspecified"}'.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE failed to rebuild graph after track-group suppression for '{reason ?? "unspecified"}': {ex.Message}");
            }
            finally
            {
                _isRebuildingForGroupSuppression = false;
            }
        }

        private static void TrySuppressArea(string areaId, string reason, FuseApplyTransaction transaction)
        {
            if (string.IsNullOrWhiteSpace(areaId))
            {
                return;
            }

            var area = TrackAPI.GetArea(areaId);
            if (area == null)
            {
                Warn(transaction, "suppressed area", areaId, $"area was not found for '{reason}'");
                return;
            }

            try
            {
                if (!Areas.TryGetValue(areaId, out var state))
                {
                    state = new AreaSuppressionState(areaId);
                    Areas[areaId] = state;
                }

                state.Area = area;
                EnsureSyntheticAreaFeature(state, area);

                var changed = false;
                foreach (var item in EnumerateAreaDisablables(area))
                {
                    if (item == null)
                    {
                        continue;
                    }

                    if (!state.OriginalProgressionDisabled.ContainsKey(item))
                    {
                        state.OriginalProgressionDisabled[item] = item.ProgressionDisabled;
                    }

                    if (!item.ProgressionDisabled)
                    {
                        item.ProgressionDisabled = true;
                        changed = true;
                    }
                }

                if (changed)
                {
                    NotifyIndustriesChanged("area suppression " + areaId);
                }

                FuseLog.Info($"FUSE suppressed base area '{areaId}' for '{reason}' owners={FuseRegistry.GetSharedOwners(FuseClaimKind.SuppressedArea, areaId).Count} syntheticFeature='{state.FeatureId}'.");
            }
            catch (Exception ex)
            {
                Warn(transaction, "suppressed area", areaId, $"suppression failed for '{reason}': {ex.Message}");
            }
        }

        private static void RestoreTrackGroup(string groupId, string reason)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return;
            }

            if (!TrackGroups.TryGetValue(groupId, out var state))
            {
                return;
            }

            var graph = Graph.Shared;
            if (graph == null)
            {
                TrackGroups.Remove(groupId);
                FuseLog.Warning($"FUSE dropped track-group suppression state for '{groupId}' during '{reason}' because Graph.Shared is not available.");
                return;
            }

            try
            {
                var changed = false;
                changed = graph.SetGroupEnabled(groupId, state.WasEnabled) || changed;
                changed = graph.SetGroupAvailable(groupId, state.WasAvailable) || changed;
                TrackGroups.Remove(groupId);
                if (changed)
                {
                    // Restoring multiple groups in a row used to rebuild after
                    // each one; routing through RequestRebuild lets callers
                    // wrap the loop in a single batch.
                    TrackAPI.RequestRebuild();
                }

                FuseLog.Info($"FUSE restored base track group '{groupId}' for '{reason}' enabled={state.WasEnabled} available={state.WasAvailable}.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE failed to restore track group '{groupId}' for '{reason}': {ex.Message}");
            }
        }

        private static void RestoreScenePath(string path, string reason)
        {
            if (string.IsNullOrWhiteSpace(path) || !ScenePaths.TryGetValue(path, out var state))
            {
                return;
            }

            try
            {
                var target = state.Target ?? FusePrefabResolver.ResolveScenePath(path) ?? GameObject.Find(path);
                if (target != null)
                {
                    state.Target = target;
                    RestoreSuppressedSceneObject(state);
                    target.SetActive(state.WasActive);
                    FuseLog.Info($"FUSE restored base scene path '{path}' for '{reason}' active={state.WasActive}.");
                }
                else
                {
                    FuseLog.Warning($"FUSE dropped scene-path suppression state for '{path}' during '{reason}' because the target no longer exists.");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE failed to restore scene path '{path}' for '{reason}': {ex.Message}");
            }
            finally
            {
                ScenePaths.Remove(path);
            }
        }

        private static void RestoreSuppressedSceneObject(ScenePathSuppressionState state)
        {
            if (state == null)
            {
                return;
            }

            foreach (var rendererState in state.RendererStates.ToArray())
            {
                var renderer = rendererState.Renderer;
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = rendererState.Enabled;
                renderer.forceRenderingOff = rendererState.ForceRenderingOff;
            }

            foreach (var behaviourState in state.CullingBehaviourStates.ToArray())
            {
                var behaviour = behaviourState.Behaviour;
                if (behaviour == null)
                {
                    continue;
                }

                behaviour.enabled = behaviourState.Enabled;
            }
        }

        private static void RestoreArea(string areaId, string reason)
        {
            if (string.IsNullOrWhiteSpace(areaId) || !Areas.TryGetValue(areaId, out var state))
            {
                return;
            }

            var restored = 0;
            foreach (var kvp in state.OriginalProgressionDisabled.ToArray())
            {
                try
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.ProgressionDisabled = kvp.Value;
                        restored++;
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Warning($"FUSE failed to restore progression-disabled state for area '{areaId}' item='{DescribeDisablable(kvp.Key)}': {ex.Message}");
                }
            }

            RemoveSyntheticAreaFeature(state);
            Areas.Remove(areaId);
            NotifyIndustriesChanged("area restore " + areaId);
            FuseLog.Info($"FUSE restored base area '{areaId}' for '{reason}' itemCount={restored} syntheticFeature='{state.FeatureId}'.");
        }

        private static void RestoreUnusedTrackGroups(IEnumerable<string> groupIds)
        {
            foreach (var groupId in groupIds ?? Enumerable.Empty<string>())
            {
                if (FuseRegistry.GetSharedOwners(FuseClaimKind.SuppressedTrackGroup, groupId).Count == 0)
                {
                    RestoreTrackGroup(groupId, "definition update");
                }
            }
        }

        private static void RestoreUnusedScenePaths(IEnumerable<string> paths)
        {
            foreach (var path in paths ?? Enumerable.Empty<string>())
            {
                if (FuseRegistry.GetSharedOwners(FuseClaimKind.SuppressedScenePath, path).Count == 0)
                {
                    FuseLog.Info($"FUSE scene-path suppression '{path}' no longer has owners after definition update; target remains suppressed until map unload.");
                }
            }
        }

        private static void RestoreUnusedAreas(IEnumerable<string> areaIds)
        {
            foreach (var areaId in areaIds ?? Enumerable.Empty<string>())
            {
                if (FuseRegistry.GetSharedOwners(FuseClaimKind.SuppressedArea, areaId).Count == 0)
                {
                    RestoreArea(areaId, "definition update");
                }
            }
        }

        private static void ClaimSuppressions(string packageId, IEnumerable<string> ids, FuseClaimKind kind, string label, FuseApplyTransaction transaction)
        {
            foreach (var id in ids ?? Enumerable.Empty<string>())
            {
                try
                {
                    if (FuseRegistry.TryClaim(kind, id, packageId, out var owner))
                    {
                        transaction?.PostBind("suppression claim", id, $"kind={kind} owners={FuseRegistry.GetSharedOwners(kind, id).Count}");
                    }
                    else
                    {
                        var message = string.IsNullOrWhiteSpace(owner)
                            ? "claim rejected"
                            : $"claim rejected by owner '{owner}'";
                        Warn(transaction, "suppressed " + label, id, message);
                    }
                }
                catch (Exception ex)
                {
                    Warn(transaction, "suppressed " + label, id, $"claim failed: {ex.Message}");
                }
            }
        }

        private static IReadOnlyCollection<string> GetClaimedIds(FuseClaimKind kind)
        {
            return FuseRegistry.GetClaimedIds(kind)
                .Where(id => FuseRegistry.GetSharedOwners(kind, id).Count > 0)
                .ToArray();
        }

        private static IEnumerable<IProgressionDisablable> EnumerateAreaDisablables(Area area)
        {
            if (area == null)
            {
                yield break;
            }

            IEnumerable<Industry> industries;
            try
            {
                industries = area.Industries?.Where(industry => industry != null).ToArray() ?? Array.Empty<Industry>();
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE could not enumerate industries for suppressed area '{area.identifier}': {ex.Message}");
                industries = Array.Empty<Industry>();
            }

            foreach (var industry in industries)
            {
                yield return industry;
            }

            PassengerStop[] passengerStops;
            try
            {
                passengerStops = area.GetComponentsInChildren<PassengerStop>(true) ?? Array.Empty<PassengerStop>();
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE could not enumerate passenger stops for suppressed area '{area.identifier}': {ex.Message}");
                passengerStops = Array.Empty<PassengerStop>();
            }

            foreach (var stop in passengerStops.Where(stop => stop != null))
            {
                yield return stop;
            }
        }

        private static void EnsureSyntheticAreaFeature(AreaSuppressionState state, Area area)
        {
            if (state == null || area == null)
            {
                return;
            }

            var manager = MapFeatureManager.Shared;
            if (manager == null)
            {
                FuseLog.Warning($"FUSE could not create synthetic suppression MapFeature '{state.FeatureId}' because MapFeatureManager.Shared is not available.");
                return;
            }

            try
            {
                var feature = state.Feature;
                if (feature == null)
                {
                    feature = FindFeature(manager, state.FeatureId);
                }

                if (feature == null)
                {
                    var featureObject = new GameObject(state.FeatureId);
                    featureObject.transform.SetParent(manager.transform, false);
                    feature = featureObject.AddComponent<MapFeature>();
                    state.CreatedFeatureObject = featureObject;
                }

                feature.identifier = state.FeatureId;
                feature.displayName = "FUSE Suppression: " + area.identifier;
                feature.description = "FUSE synthetic area suppression feature. Other mods can filter ids with prefix '" + SyntheticAreaFeaturePrefix + "'.";
                feature.defaultEnableInSandbox = false;
                feature.prerequisites = Array.Empty<MapFeature>();
                feature.trackGroupsEnableOnUnlock = Array.Empty<string>();
                feature.trackGroupsAvailableOnUnlock = Array.Empty<string>();
                feature.gameObjectsEnableOnUnlock = Array.Empty<GameObject>();
                feature.areasEnableOnUnlock = new[] { area };
                feature.unlockExcludeIndustries = Array.Empty<Industry>();
                feature.unlockIncludeIndustries = Array.Empty<Industry>();
                feature.unlockIncludeIndustryComponents = Array.Empty<IndustryComponent>();
                feature.Unlocked = false;

                state.Feature = feature;
                RegisterSyntheticFeature(manager, feature);
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE failed to create/register synthetic area MapFeature '{state.FeatureId}': {ex.Message}");
            }
        }

        private static MapFeature FindFeature(MapFeatureManager manager, string featureId)
        {
            try
            {
                return manager?.AvailableFeatures?.FirstOrDefault(feature =>
                    feature != null &&
                    string.Equals(feature.identifier, featureId, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private static void RegisterSyntheticFeature(MapFeatureManager manager, MapFeature feature)
        {
            if (manager == null || feature == null)
            {
                return;
            }

            if (MapFeatureManagerFeaturesField == null)
            {
                FuseLog.Warning($"FUSE cannot register synthetic MapFeature '{feature.identifier}' because MapFeatureManager._features was not found.");
                return;
            }

            var features = (MapFeature[])MapFeatureManagerFeaturesField.GetValue(manager) ?? Array.Empty<MapFeature>();
            if (features.Any(existing => existing == feature || (existing != null && string.Equals(existing.identifier, feature.identifier, StringComparison.OrdinalIgnoreCase))))
            {
                return;
            }

            MapFeatureManagerFeaturesField.SetValue(manager, features.Concat(new[] { feature }).ToArray());
        }

        private static void RemoveSyntheticAreaFeature(AreaSuppressionState state)
        {
            var feature = state?.Feature;
            if (feature == null)
            {
                return;
            }

            var manager = MapFeatureManager.Shared;
            try
            {
                if (manager != null && MapFeatureManagerFeaturesField != null)
                {
                    var features = (MapFeature[])MapFeatureManagerFeaturesField.GetValue(manager) ?? Array.Empty<MapFeature>();
                    MapFeatureManagerFeaturesField.SetValue(
                        manager,
                        features.Where(existing => existing != null && existing != feature && !string.Equals(existing.identifier, state.FeatureId, StringComparison.OrdinalIgnoreCase)).ToArray());
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE failed to unregister synthetic MapFeature '{state.FeatureId}': {ex.Message}");
            }

            try
            {
                if (state.CreatedFeatureObject != null)
                {
                    UnityEngine.Object.Destroy(state.CreatedFeatureObject);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE failed to destroy synthetic MapFeature object '{state.FeatureId}': {ex.Message}");
            }
        }

        private static string[] NormalizeIds(params IEnumerable<string>[] groups)
        {
            return (groups ?? Array.Empty<IEnumerable<string>>())
                .Where(group => group != null)
                .SelectMany(group => group)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string MakeFeatureId(string areaId)
        {
            return SyntheticAreaFeaturePrefix + Sanitize(areaId);
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            var chars = value.Trim().ToLowerInvariant().Select(ch =>
                char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-' ? ch : '-').ToArray();
            return new string(chars);
        }

        private static void Warn(FuseApplyTransaction transaction, string kind, string id, string message)
        {
            transaction?.Warning(kind, id, message);
            FuseLog.Warning($"FUSE {kind} '{id ?? string.Empty}': {message ?? string.Empty}");
        }

        private static void NotifyIndustriesChanged(string reason)
        {
            try
            {
                Messenger.Default.Send(default(IndustriesDidChange));
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE failed to send IndustriesDidChange after {reason}: {ex.Message}");
            }
        }

        private static string DescribeDisablable(IProgressionDisablable item)
        {
            if (item == null)
            {
                return "<null>";
            }

            var component = item as Component;
            return component != null ? component.name : item.ToString();
        }

        private sealed class ScenePathSuppressionChanges
        {
            public int Renderers { get; set; }
            public int CullingBehaviours { get; set; }

            public bool HasChanges => Renderers > 0 || CullingBehaviours > 0;
        }

        private sealed class PackageSuppressions
        {
            public PackageSuppressions(IEnumerable<string> scenePaths, IEnumerable<string> trackGroups, IEnumerable<string> areas)
            {
                ScenePaths = (scenePaths ?? Array.Empty<string>()).ToArray();
                TrackGroups = (trackGroups ?? Array.Empty<string>()).ToArray();
                Areas = (areas ?? Array.Empty<string>()).ToArray();
            }

            public string[] ScenePaths { get; }
            public string[] TrackGroups { get; }
            public string[] Areas { get; }
        }

        private sealed class ScenePathSuppressionState
        {
            public ScenePathSuppressionState(string path)
            {
                Path = path ?? string.Empty;
                WarnedMissing = false;
            }

            public string Path { get; }
            public GameObject Target { get; set; }
            public bool WasActive { get; set; }
            public bool WarnedMissing { get; set; }
            public List<RendererSuppressionState> RendererStates { get; } = new List<RendererSuppressionState>();
            public List<BehaviourSuppressionState> CullingBehaviourStates { get; } = new List<BehaviourSuppressionState>();

            public void ClearCapturedComponents()
            {
                RendererStates.Clear();
                CullingBehaviourStates.Clear();
            }

            public void GetOrAddRendererState(Renderer renderer)
            {
                if (renderer == null || RendererStates.Any(state => ReferenceEquals(state.Renderer, renderer)))
                {
                    return;
                }

                RendererStates.Add(new RendererSuppressionState(renderer));
            }

            public void GetOrAddCullingBehaviourState(Behaviour behaviour)
            {
                if (behaviour == null || CullingBehaviourStates.Any(state => ReferenceEquals(state.Behaviour, behaviour)))
                {
                    return;
                }

                CullingBehaviourStates.Add(new BehaviourSuppressionState(behaviour));
            }
        }

        private sealed class RendererSuppressionState
        {
            public RendererSuppressionState(Renderer renderer)
            {
                Renderer = renderer;
                Enabled = renderer != null && renderer.enabled;
                ForceRenderingOff = renderer != null && renderer.forceRenderingOff;
            }

            public Renderer Renderer { get; }
            public bool Enabled { get; }
            public bool ForceRenderingOff { get; }
        }

        private sealed class BehaviourSuppressionState
        {
            public BehaviourSuppressionState(Behaviour behaviour)
            {
                Behaviour = behaviour;
                Enabled = behaviour != null && behaviour.enabled;
            }

            public Behaviour Behaviour { get; }
            public bool Enabled { get; }
        }

        private sealed class TrackGroupSuppressionState
        {
            public bool WasEnabled { get; set; }
            public bool WasAvailable { get; set; }
        }

        private sealed class AreaSuppressionState
        {
            public AreaSuppressionState(string areaId)
            {
                AreaId = areaId ?? string.Empty;
                FeatureId = MakeFeatureId(AreaId);
            }

            public string AreaId { get; }
            public string FeatureId { get; }
            public Area Area { get; set; }
            public MapFeature Feature { get; set; }
            public GameObject CreatedFeatureObject { get; set; }
            public Dictionary<IProgressionDisablable, bool> OriginalProgressionDisabled { get; } =
                new Dictionary<IProgressionDisablable, bool>();
        }
    }
}
