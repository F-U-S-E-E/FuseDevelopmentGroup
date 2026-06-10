using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using Model.Definition.Data;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Runtime.Lifecycle;
using Track;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static class SceneryAPI
    {
        private static Transform _fallbackRoot;
        private static readonly Dictionary<string, string> LegacySceneryAssetAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "CabooseHouse", "ALWHouses_CabooseHouse" },
                { "CitySignDeep", "ALW_Sign_CitySignDeep" },
                { "CitySignEverett", "ALW_Sign_CitySignEverett" },
                { "CitySignWalkerWoody", "ALW_Sign_CitySignWalkerWoody" }
            };

        private static readonly HashSet<string> OptionalLegacySceneryAssets =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CLB_Block02",
                "CLB_Truss01_Straight2",
                "Interlocking_Tower",
                "rlw Spen"
            };

        public static SceneryAssetInstance AddScenery(string id, FuseScenery definition, Action onDeferredActivated = null)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetScenery(id) != null)
            {
                throw new InvalidOperationException($"Scenery '{id}' already exists.");
            }

            string assetIdentifier;
            if (!TryResolveAssetIdentifier(id, definition, out assetIdentifier))
            {
                FuseLog.Warning(
                    $"FUSE skipped AddScenery '{id}' because no valid asset identifier could be resolved " +
                    $"(AssetIdentifier='{definition.AssetIdentifier ?? string.Empty}', Model='{definition.Model ?? string.Empty}').");
                return null;
            }

            var gameObject = new GameObject(id);
            gameObject.SetActive(false);
            gameObject.transform.SetParent(GetSceneryRoot(), false);

            var scenery = gameObject.AddComponent<SceneryAssetInstance>();
            // Tag the GameObject as FUSE-owned so the runtime index can
            // tell our scenery apart from vanilla. Without this marker,
            // FuseSceneryRuntimeIndex.Rebuild had to scan every
            // SceneryAssetInstance in the scene and key by .name, which
            // silently collapsed vanilla siblings sharing a leaf name
            // (the four vanilla "Freight House" instances in Bryson /
            // Sylva / Dillsboro / Ela) into a single slot and let an
            // authoring entity with an ambiguous id like "freight house"
            // hijack one of them via UpdateScenery — turning a legacy
            // "add a copy" intent into a "move the vanilla original"
            // outcome. With the marker, vanilla scenery never enters
            // the index, GetScenery can only resolve ids FUSE itself
            // has previously claimed, and the ApplyToRuntime path
            // correctly falls into AddScenery for a brand-new entity.
            var marker = gameObject.AddComponent<FuseSceneryMarker>();
            marker.Id = id;
            // Tag mask-bearing scenery. Three behaviours key off this: (1) the load throttle
            // is bypassed so its first load — which registers the terrain flatten/cut — is
            // immediate, (2) the OnDidLoadModels hook below re-homes its welded map masks into
            // standalone, always-active objects the moment the model streams in
            // (MapAPI.DecoupleAttachedMapMasks) so the terrain mask survives the building
            // streaming out/in and teleport world-shifts, and (3) a visibility watcher
            // (FuseDecoupledMaskVisibilityWatcher) drops that standalone mask only while the
            // building is intentionally hidden (every renderer holder SetActive(false)) and
            // restores it when shown, so a hidden building never leaves a flat patch of ground
            // behind. The building MODEL itself culls like any other scenery — only its
            // terrain contribution is permanent.
            marker.IsMaskBearing = FuseSceneryDeferralClassifier.HasMaskComponent(assetIdentifier);
            if (marker.IsMaskBearing)
            {
                var sceneryRoot = gameObject;
                var sceneryId = id;
                scenery.OnDidLoadModels += _ =>
                {
                    try
                    {
                        MapAPI.DecoupleAttachedMapMasks(sceneryRoot, sceneryId);
                        // Keep the just-decoupled mask in step with the building's visibility: if the
                        // model streamed in already hidden, drop the mask now; otherwise watch for it
                        // being hidden/shown later.
                        FuseDecoupledMaskVisibilityWatcher.Ensure(sceneryRoot, sceneryId);
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Exception($"FUSE map-mask decouple-on-load failed for scenery '{sceneryId}'", ex);
                    }
                };
            }
            ApplyDefinition(scenery, definition, assetIdentifier);

            // Activate inline unless the deferred activator takes ownership of this
            // scenery (it leaves the GameObject inactive and flips it on after the
            // loading screen). Index registration and definition recording always run
            // now so GetScenery(id) resolves immediately, even while deferred.
            if (!FuseDeferredSceneryActivator.TryDefer(scenery, id, assetIdentifier, onDeferredActivated))
            {
                gameObject.SetActive(true);
                MapAPI.RefreshAttachedMapMasks(gameObject, $"scenery '{id}' add");
            }

            FuseSceneryRuntimeIndex.Instance.Set(id, scenery);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Scenery, id, definition);
            return scenery;
        }

        /// <summary>
        /// Internal marker MonoBehaviour attached to every scenery
        /// GameObject FUSE creates via <see cref="AddScenery"/>. The
        /// runtime index uses these (not raw SceneryAssetInstance scans)
        /// to decide which scenery is FUSE-tracked, so vanilla scenery
        /// with the same leaf name as a mod's authoring id can never be
        /// accidentally claimed.
        /// </summary>
        internal sealed class FuseSceneryMarker : MonoBehaviour
        {
            public string Id;

            // True when this scenery declares a map-mask component. Drives the mask
            // decouple/visibility lifecycle (DecoupleAttachedMapMasks + the visibility
            // watcher) and the load-throttle bypass so the first load — which registers the
            // terrain flatten/cut — is immediate. The building model itself culls like any
            // other scenery; only its terrain contribution is permanent. Set once at
            // AddScenery time.
            public bool IsMaskBearing;
        }

        public static void UpdateScenery(string id, FuseScenery definition)
        {
            var scenery = RequireScenery(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            string assetIdentifier;
            if (!TryResolveAssetIdentifier(id, definition, out assetIdentifier))
            {
                FuseLog.Warning(
                    $"FUSE skipped UpdateScenery '{id}' because no valid asset identifier could be resolved " +
                    $"(AssetIdentifier='{definition.AssetIdentifier ?? string.Empty}', Model='{definition.Model ?? string.Empty}'). " +
                    "Refusing to call SceneryAssetInstance.ReloadComponents with an unknown identifier.");
                return;
            }

            var modelChanged = !string.Equals(scenery.identifier, assetIdentifier, StringComparison.Ordinal);
            ApplyDefinition(scenery, definition, assetIdentifier);
            if (modelChanged && scenery.isActiveAndEnabled)
            {
                scenery.ReloadComponents();
            }

            MapAPI.RefreshAttachedMapMasks(scenery.gameObject, $"scenery '{id}' update");

            // Mask-bearing scenery: its terrain mask was decoupled to a standalone object at
            // the previous transform. Drop those and re-decouple from the (now-moved) welded
            // masks so the flatten follows the scenery. If the model isn't currently streamed
            // in, the re-decouple is a no-op and OnDidLoadModels redoes it on the next load.
            var marker = scenery.GetComponent<FuseSceneryMarker>();
            if (marker != null && marker.IsMaskBearing)
            {
                MapAPI.RemoveDecoupledMasksFor(id);
                MapAPI.DecoupleAttachedMapMasks(scenery.gameObject, id);
                // Re-attach/refresh the visibility watcher and apply the current hidden/shown state
                // to the freshly re-decoupled mask.
                FuseDecoupledMaskVisibilityWatcher.Ensure(scenery.gameObject, id);
            }

            FuseSceneryRuntimeIndex.Instance.Set(id, scenery);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Scenery, id, definition);
        }

        public static void RemoveScenery(string id)
        {
            if (!TryRemoveScenery(id))
            {
                throw new InvalidOperationException($"Scenery '{id}' was not found.");
            }
        }

        public static bool TryRemoveScenery(string id)
        {
            var root = FindRemovableSceneryObject(id);
            if (root == null)
            {
                FuseLog.Info($"FUSE world removal skipped missing scenery '{id}'.");
                return false;
            }

            var path = GetTransformPath(root.transform);
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
            FuseSceneryRuntimeIndex.Instance.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.Scenery, id);
            // Remove any terrain masks this scenery decoupled to standalone objects, so a
            // deleted building doesn't leave its ground permanently flattened.
            MapAPI.RemoveDecoupledMasksFor(id);
            FuseLog.Info($"FUSE removed scenery '{id}' from '{path}'.");
            return true;
        }

        public static SceneryAssetInstance GetScenery(string id)
        {
            if (FuseSceneryRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (SceneryAssetInstance)cached;
            }

            if (FuseCacheRegistry.IsReady)
            {
                return null;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<SceneryAssetInstance>().FirstOrDefault(instance => instance.name == id)
                : null;
        }

        public static IEnumerable<SceneryAssetInstance> GetAllScenery()
        {
            return UnityEngine.Object.FindObjectsOfType<SceneryAssetInstance>();
        }

        public static IEnumerable<string> GetAvailableSceneryModels()
        {
            return SceneryAssetManager.Shared?.GetSceneryDefinitionIdentifiers() ?? Enumerable.Empty<string>();
        }

        public static FuseScenery GetSceneryDefinition(string id)
        {
            return GetDefinition(GetScenery(id));
        }

        public static FuseScenery GetDefinition(SceneryAssetInstance scenery)
        {
            if (scenery == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.Scenery, scenery.name, out FuseScenery definition);
            definition = definition ?? new FuseScenery();
            // scenery.identifier is the asset identifier, never the display name.
            definition.AssetIdentifier = scenery.identifier;
            if (string.IsNullOrWhiteSpace(definition.Model))
            {
                definition.Model = scenery.identifier;
            }

            definition.Position = scenery.transform.localPosition;
            definition.Rotation = scenery.transform.localEulerAngles;
            definition.Scale = scenery.transform.localScale == default ? Vector3.one : scenery.transform.localScale;
            definition.AnchorSpanIds = definition.AnchorSpanIds ?? Array.Empty<string>();
            return definition;
        }

        /// <summary>
        /// Resolves the validated asset identifier for a scenery definition.
        /// Returns false if no identifier can be resolved against the active
        /// SceneryAssetManager registry; callers must skip rather than throw.
        /// </summary>
        public static bool TryResolveAssetIdentifier(string sceneryId, FuseScenery definition, out string assetIdentifier)
        {
            assetIdentifier = null;
            if (definition == null)
            {
                return false;
            }

            // Prefer the explicit asset identifier. The Model field may carry a display
            // name (e.g. "Camp 1", "Mess Hall") and must never be used directly.
            var candidate = NormalizeSceneryIdentifier(definition.AssetIdentifier);
            if (!string.IsNullOrWhiteSpace(candidate) && TryResolveKnownSceneryAssetIdentifier(candidate, out assetIdentifier))
            {
                return true;
            }

            // Backward-compat fallback: treat Model as an asset identifier ONLY if
            // the manager actually recognises it. Otherwise it is a display name.
            var modelCandidate = NormalizeSceneryIdentifier(definition.Model);
            if (!string.IsNullOrWhiteSpace(modelCandidate) && TryResolveKnownSceneryAssetIdentifier(modelCandidate, out assetIdentifier))
            {
                return true;
            }

            return false;
        }

        public static bool IsKnownOptionalLegacyAssetReference(FuseScenery definition)
        {
            if (definition == null)
            {
                return false;
            }

            return IsKnownOptionalLegacyAssetReference(definition.AssetIdentifier) ||
                   IsKnownOptionalLegacyAssetReference(definition.Model);
        }

        private static bool TryResolveKnownSceneryAssetIdentifier(string candidate, out string resolvedIdentifier)
        {
            return TryResolveKnownSceneryAssetIdentifier(candidate, out resolvedIdentifier, true);
        }

        private static bool TryResolveKnownSceneryAssetIdentifier(string candidate, out string resolvedIdentifier, bool allowLegacyAlias)
        {
            resolvedIdentifier = null;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            var manager = SceneryAssetManager.Shared;
            if (manager == null)
            {
                // Without the manager we cannot validate. Skip to be safe rather
                // than risk an UnknownIdentifierException out of ReloadComponents.
                return false;
            }

            try
            {
                SceneryDefinition definition;
                if (manager.TryGetSceneryDefinition(candidate, out definition) && definition != null)
                {
                    resolvedIdentifier = candidate;
                    return true;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE scenery asset direct lookup failed for '{candidate}'", ex);
            }

            if (allowLegacyAlias &&
                LegacySceneryAssetAliases.TryGetValue(NormalizeLegacyAssetKey(candidate), out var alias) &&
                !string.Equals(alias, candidate, StringComparison.OrdinalIgnoreCase) &&
                TryResolveKnownSceneryAssetIdentifier(alias, out resolvedIdentifier, false))
            {
                FuseLog.Info($"FUSE resolved legacy scenery asset alias '{candidate}' -> '{resolvedIdentifier}'.");
                return true;
            }

            return TryResolveFromKnownIdentifierIndex(manager, candidate, out resolvedIdentifier);
        }

        // Case-insensitive identifier -> canonical identifier, built lazily from one
        // GetSceneryDefinitionIdentifiers() call. That enumeration re-walks every
        // mounted store's container per call (plus the editor-menu postfix's
        // direct-store filter), so the miss fallback above used to pay ~200ms for
        // every unresolved scenery item on the loading-screen critical path — and
        // re-paid it for each repeat of the same missing identifier. First-wins
        // insertion in enumeration order reproduces the old linear scan's pick
        // exactly, and going through the manager (not PrefabStore._stores directly)
        // keeps the patched direct-only filtering applied. The index must never be
        // consulted before the exact-case probe and legacy-alias steps above.
        private static readonly object KnownSceneryIdentifierIndexLock = new object();
        private static Dictionary<string, string> _knownSceneryIdentifierIndex;
        private static int _knownSceneryIdentifierIndexGeneration;

        // The identifier population changes outside this class: direct-store
        // mounting (PrefabStore.Create postfix), asset-pack reset, legacy container
        // mixinto application, and map-load cache rebuilds all drop the index so the
        // next miss rebuilds it against the current catalog.
        internal static void InvalidateKnownSceneryIdentifierIndex()
        {
            lock (KnownSceneryIdentifierIndexLock)
            {
                _knownSceneryIdentifierIndex = null;
                _knownSceneryIdentifierIndexGeneration++;
            }
        }

        private static bool TryResolveFromKnownIdentifierIndex(SceneryAssetManager manager, string candidate, out string resolvedIdentifier)
        {
            resolvedIdentifier = null;
            Dictionary<string, string> index;
            lock (KnownSceneryIdentifierIndexLock)
            {
                index = _knownSceneryIdentifierIndex;
                if (index == null)
                {
                    var generationBeforeBuild = _knownSceneryIdentifierIndexGeneration;
                    // The try spans the enumeration too, not just the call:
                    // GetSceneryDefinitionIdentifiers may hand back a lazy
                    // enumerable whose real work (store.Container() walks) runs
                    // here in the foreach, so a throw can surface mid-iteration.
                    // On failure the partial local index is discarded — never
                    // published, never returned (we return false below).
                    try
                    {
                        var known = manager.GetSceneryDefinitionIdentifiers();
                        if (known == null)
                        {
                            return false;
                        }

                        index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var id in known)
                        {
                            if (!string.IsNullOrEmpty(id) && !index.ContainsKey(id))
                            {
                                index.Add(id, id);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Exception($"FUSE scenery asset registry enumeration failed while resolving '{candidate}'", ex);
                        return false;
                    }

                    // The enumeration itself touches store.Container(), whose postfix
                    // can apply legacy container mixintos and re-enter
                    // InvalidateKnownSceneryIdentifierIndex on this thread (the lock is
                    // reentrant). In that known case the list is actually complete —
                    // each container is mutated before the manager reads it — but a
                    // generation bump during the build means some mutation interleaved
                    // and we can't prove the list reflects it. Use the list for this
                    // lookup (the pre-cache code saw the same per-call snapshot) but
                    // don't publish it; the next miss rebuilds, costing at most one
                    // extra enumeration.
                    if (_knownSceneryIdentifierIndexGeneration == generationBeforeBuild)
                    {
                        _knownSceneryIdentifierIndex = index;
                    }
                }
            }

            return index.TryGetValue(candidate, out resolvedIdentifier);
        }

        private static bool IsKnownOptionalLegacyAssetReference(string value)
        {
            return OptionalLegacySceneryAssets.Contains(NormalizeLegacyAssetKey(value));
        }

        private static void ApplyDefinition(SceneryAssetInstance scenery, FuseScenery definition, string assetIdentifier)
        {
            // Only the validated asset identifier ever reaches scenery.identifier /
            // SceneryAssetManager.LoadScenery. Display names are kept on the definition.
            scenery.identifier = assetIdentifier;
            Vector3 position;
            Quaternion rotation;
            if (TryResolveSpanAnchor(definition, out position, out rotation))
            {
                scenery.transform.localPosition = position;
                scenery.transform.localRotation = rotation;
            }
            else
            {
                scenery.transform.localPosition = definition.Position;
                scenery.transform.localRotation = Quaternion.Euler(definition.Rotation);
            }

            scenery.transform.localScale = definition.Scale == default ? Vector3.one : definition.Scale;
        }

        private static bool TryResolveSpanAnchor(FuseScenery definition, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;
            var anchorSpanIds = definition?.AnchorSpanIds;
            if (anchorSpanIds == null || anchorSpanIds.Length == 0)
            {
                return false;
            }

            var points = new List<Vector3>();
            var tangents = new List<Vector3>();
            foreach (var spanId in anchorSpanIds)
            {
                if (string.IsNullOrWhiteSpace(spanId))
                {
                    continue;
                }

                var span = TrackAPI.GetSpan(spanId);
                if (span == null)
                {
                    FuseLog.Warning($"FUSE span-anchored scenery skipped missing span '{spanId}'.");
                    continue;
                }

                var spanPoints = span.GetPoints()?.ToArray();
                if (spanPoints == null || spanPoints.Length == 0)
                {
                    FuseLog.Warning($"FUSE span-anchored scenery skipped span '{spanId}' because it has no points.");
                    continue;
                }

                points.Add(span.GetCenterPoint());
                if (spanPoints.Length >= 2)
                {
                    var tangent = spanPoints[spanPoints.Length - 1] - spanPoints[0];
                    if (tangent.sqrMagnitude > 0.0001f)
                    {
                        tangents.Add(tangent);
                    }
                }
            }

            if (points.Count == 0)
            {
                return false;
            }

            position = Average(points) + definition.Position;
            var direction = tangents.Count > 0 ? Average(tangents) : Vector3.forward;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.forward;
            }

            rotation = Quaternion.LookRotation(direction.normalized, Vector3.up) * Quaternion.Euler(definition.Rotation);
            return true;
        }

        private static Vector3 Average(List<Vector3> values)
        {
            if (values == null || values.Count == 0)
            {
                return Vector3.zero;
            }

            var total = Vector3.zero;
            foreach (var value in values)
            {
                total += value;
            }

            return total / values.Count;
        }

        private static string NormalizeSceneryIdentifier(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return model;
            }

            var marker = model.IndexOf("://", StringComparison.Ordinal);
            if (marker < 0)
            {
                return model;
            }

            return model.Substring(marker + 3);
        }

        private static string NormalizeLegacyAssetKey(string value)
        {
            return (NormalizeSceneryIdentifier(value) ?? string.Empty).Trim();
        }

        private static SceneryAssetInstance RequireScenery(string id)
        {
            var scenery = GetScenery(id);
            if (scenery == null)
            {
                throw new InvalidOperationException($"Scenery '{id}' was not found.");
            }

            return scenery;
        }

        private static GameObject FindRemovableSceneryObject(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            var scenery = GetScenery(id);
            if (scenery != null)
            {
                return scenery.gameObject;
            }

            return FusePrefabResolver.ResolveScenePath(id) ?? GameObject.Find(id);
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var names = new Stack<string>();
            var cursor = transform;
            while (cursor != null)
            {
                names.Push(cursor.name);
                cursor = cursor.parent;
            }

            return string.Join("/", names.ToArray());
        }

        private static Transform GetSceneryRoot()
        {
            var existingRoot = GameObject.Find("World/Large Scenery") ?? GameObject.Find("Large Scenery");
            if (existingRoot != null)
            {
                return existingRoot.transform;
            }

            if (SceneryAssetManager.Shared != null)
            {
                return SceneryAssetManager.Shared.transform;
            }

            if (_fallbackRoot == null)
            {
                _fallbackRoot = new GameObject("FUSE Scenery").transform;
                UnityEngine.Object.DontDestroyOnLoad(_fallbackRoot.gameObject);
            }

            return _fallbackRoot;
        }

        private static void RequireId(string id, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("ID is required.", parameterName);
            }
        }
    }
}
