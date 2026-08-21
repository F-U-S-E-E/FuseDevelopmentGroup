using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Map.Runtime.MapModifiers;
using Map.Runtime.MaskComponents;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Loading;
using TelegraphPoles;
using TMPro;
using UI.Map;
using UnityEngine;
using UnityEngine.UI;
using RuntimeSimpleGraph = SimpleGraph.Runtime.SimpleGraph;

namespace FUSE.Runtime.API
{
    public static class MapAPI
    {
        private const string MapMaskRootName = "FUSE Map Masks";
        private const string TelegraphRootName = "FUSE Telegraph Poles";
        private const string SpeedLimitCircleName = "FUSE Speed Limit Circle";

        private static readonly FieldInfo CanvasField = typeof(MapLabel).GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo PolePrefabsField = typeof(TelegraphPoleManager).GetField("polePrefabs", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo WirePrefabField = typeof(TelegraphPoleManager).GetField("wirePrefab", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo TelegraphRebuildMethod = typeof(TelegraphPoleManager).GetMethod("Rebuild", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Regex SpeedLimitTextPattern = new Regex(@"^\s*(?<mph>\d{1,3})\s*MPH\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SpeedLimitNumberPattern = new Regex(@"^\s*(?<mph>\d{1,3})\s*$", RegexOptions.Compiled);

        private static readonly Dictionary<string, FuseTelegraphPoleMovement[]> TelegraphPoleMovementClaims =
            new Dictionary<string, FuseTelegraphPoleMovement[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, Vector3> TelegraphPoleOriginalPositions = new Dictionary<int, Vector3>();

        // Direct ownership index for the standalone masks created by
        // DecoupleAttachedMapMasks. Visibility watchers run continuously, so walking every child
        // under "FUSE Map Masks" for every scenery turns the 2 Hz poll into O(watchers * masks).
        // The index makes the steady-state lookup proportional only to the number of masks owned
        // by the scenery (normally one). It is rebuilt once when the map's mask root changes and
        // kept lifecycle-safe by each marker unregistering itself on destroy.
        private static readonly Dictionary<string, List<FuseDecoupledMaskMarker>> DecoupledMasksByOwner =
            new Dictionary<string, List<FuseDecoupledMaskMarker>>(StringComparer.Ordinal);

        private static Transform _fallbackMapMaskRoot;
        private static Transform _fallbackTelegraphRoot;
        private static Transform _worldRoot;
        private static Transform _indexedDecoupledMaskRoot;
        private static Sprite _speedLimitCircleSprite;

        /// <summary>
        /// Registers or refreshes one package-owned terrain tile folder and
        /// mounts it into the active map store. This is primarily useful to
        /// authoring tools that create the first tile in a previously empty
        /// overlay folder while the game is running.
        /// </summary>
        public static int RegisterMapTileSource(
            string packageId,
            string packageFolder,
            string sourceId,
            string directory,
            string sourceFolder,
            int priority = 100)
        {
            RequireId(packageId, nameof(packageId));
            RequireId(sourceId, nameof(sourceId));
            if (string.IsNullOrWhiteSpace(packageFolder))
            {
                throw new ArgumentException(
                    "Package folder is required.",
                    nameof(packageFolder));
            }
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException(
                    "Map directory is required.",
                    nameof(directory));
            }
            if (string.IsNullOrWhiteSpace(sourceFolder))
            {
                throw new ArgumentException(
                    "Tile source folder is required.",
                    nameof(sourceFolder));
            }

            var fullPackageFolder = Path.GetFullPath(packageFolder);
            if (!Directory.Exists(fullPackageFolder))
            {
                throw new DirectoryNotFoundException(
                    "Package folder was not found: "
                    + fullPackageFolder);
            }

            var registered = FuseMapTileRegistry.RegisterTileSource(
                packageId,
                fullPackageFolder,
                sourceId,
                new FuseMapTileSource
                {
                    Directory = directory,
                    SourceFolder = sourceFolder,
                    Priority = priority
                });
            if (!registered)
            {
                throw new InvalidOperationException(
                    "FUSE rejected the map tile source. Keep sourceFolder "
                    + "inside the package directory.");
            }

            return FuseMapTileRegistry.MountForActiveMapIfLoaded(
                "MapAPI.RegisterMapTileSource");
        }

        public static MapLabel AddMapLabel(string id, FuseMapLabel definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetMapLabel(id) != null)
            {
                throw new InvalidOperationException($"Map label '{id}' already exists.");
            }

            var parent = GameObject.Find("Map Labels");
            if (parent == null)
            {
                throw new InvalidOperationException("Map Labels parent was not found.");
            }

            var template = parent.GetComponentInChildren<MapLabel>();
            if (template == null)
            {
                throw new InvalidOperationException("No MapLabel template was found.");
            }

            GameObject wrapper = null;
            try
            {
                wrapper = new GameObject(id);
                wrapper.transform.SetParent(parent.transform, false);

                var labelObject = UnityEngine.Object.Instantiate(template.gameObject, wrapper.transform, true);
                labelObject.name = "MapLabel";
                labelObject.transform.localPosition = Vector3.zero;

                var label = labelObject.GetComponent<MapLabel>();
                label.name = id;
                CanvasField?.SetValue(label, labelObject.GetComponent<Canvas>());
                ApplyMapLabelDefinition(label, definition);
                FuseMapLabelRuntimeIndex.Instance.Set(id, label);
                FuseApiPersistence.RecordDefinition(FuseDefinitionKind.MapLabel, id, definition);
                return label;
            }
            catch (Exception ex)
            {
                if (wrapper != null)
                {
                    UnityEngine.Object.Destroy(wrapper);
                }

                FuseMapLabelRuntimeIndex.Instance.Remove(id);
                FuseLog.Exception($"FUSE failed to create map label '{id}' and cleaned up the partial object", ex);
                throw;
            }
        }

        public static void UpdateMapLabel(string id, FuseMapLabel definition)
        {
            var label = RequireMapLabel(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyMapLabelDefinition(label, definition);
            FuseMapLabelRuntimeIndex.Instance.Set(id, label);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.MapLabel, id, definition);
        }

        public static void RemoveMapLabel(string id)
        {
            if (!TryRemoveMapLabel(id))
            {
                throw new InvalidOperationException($"Map label '{id}' was not found.");
            }
        }

        public static bool TryRemoveMapLabel(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            var label = GetMapLabel(id);
            GameObject wrapper;
            if (label != null)
            {
                wrapper = label.transform.parent != null ? label.transform.parent.gameObject : label.gameObject;
            }
            else
            {
                wrapper = FusePrefabResolver.ResolveScenePath(id) ?? GameObject.Find(id);
            }

            if (wrapper == null)
            {
                FuseLog.Info($"FUSE world removal skipped missing map label '{id}'.");
                return false;
            }

            var path = GetTransformPath(wrapper.transform);
            wrapper.SetActive(false);
            UnityEngine.Object.Destroy(wrapper);
            FuseMapLabelRuntimeIndex.Instance.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.MapLabel, id);
            FuseLog.Info($"FUSE removed map label '{id}' from '{path}'.");
            return true;
        }

        public static MapLabel GetMapLabel(string id)
        {
            if (FuseMapLabelRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (MapLabel)cached;
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            var labels = UnityEngine.Object.FindObjectsOfType<MapLabel>(true)
                .Where(label => label != null)
                .ToArray();
            var match = labels.FirstOrDefault(label => string.Equals(label.name, id, StringComparison.OrdinalIgnoreCase)) ??
                        SingleMapLabel(labels, label => label.transform?.parent != null &&
                                                       string.Equals(label.transform.parent.name, id, StringComparison.OrdinalIgnoreCase)) ??
                        SingleMapLabel(labels, label => string.Equals(label.text, id, StringComparison.OrdinalIgnoreCase)) ??
                        SingleMapLabel(labels, label =>
                        {
                            var text = label.GetComponentInChildren<TMP_Text>(true);
                            return text != null && string.Equals(text.text, id, StringComparison.OrdinalIgnoreCase);
                        });

            if (match != null)
            {
                FuseMapLabelRuntimeIndex.Instance.Set(id, match);
            }

            return match;
        }

        public static IEnumerable<MapLabel> GetAllMapLabels()
        {
            return UnityEngine.Object.FindObjectsOfType<MapLabel>();
        }

        private static MapLabel SingleMapLabel(IEnumerable<MapLabel> labels, Func<MapLabel, bool> predicate)
        {
            var matches = labels
                .Where(predicate)
                .Take(2)
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        public static FuseMapLabel GetMapLabelDefinition(string id)
        {
            return GetDefinition(GetMapLabel(id));
        }

        public static FuseMapLabel GetDefinition(MapLabel label)
        {
            if (label == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.MapLabel, label.name, out FuseMapLabel definition);
            definition = definition ?? new FuseMapLabel();
            definition.Text = label.text;
            var transform = label.transform.parent != null ? label.transform.parent : label.transform;
            definition.Position = transform.localPosition;
            definition.Rotation = transform.localEulerAngles;
            var text = label.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                definition.Size = text.fontSize;
                definition.Color = "#" + ColorUtility.ToHtmlStringRGBA(text.color);
            }

            return definition;
        }

        /// <summary>
        /// Creates a standalone, always-active map mask under the permanent
        /// <c>World/FUSE Map Masks</c> root. This is the preferred way to author a terrain
        /// mask: hosted here it is decoupled from any scenery's streaming, so it bakes once
        /// and stays applied through the player moving or teleporting. Masks authored instead
        /// as components on a scenery are re-homed here automatically on load by
        /// <see cref="DecoupleAttachedMapMasks"/> — the compatibility path for existing packs.
        /// </summary>
        public static GameObject AddMapMask(string id, FuseMapMask definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetMapMask(id) != null)
            {
                throw new InvalidOperationException($"Map mask '{id}' already exists.");
            }

            var root = new GameObject(id);
            root.transform.SetParent(GetOrCreateWorldRoot(MapMaskRootName, ref _fallbackMapMaskRoot), false);
            ApplyMapMaskDefinition(root, definition);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.MapMask, id, definition);
            return root;
        }

        public static void UpdateMapMask(string id, FuseMapMask definition)
        {
            var root = RequireMapMask(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyMapMaskDefinition(root, definition);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.MapMask, id, definition);
        }

        public static void RemoveMapMask(string id)
        {
            if (!TryRemoveMapMask(id))
            {
                throw new InvalidOperationException($"Map mask '{id}' was not found.");
            }
        }

        public static bool TryRemoveMapMask(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            var root = GetMapMask(id) ?? FusePrefabResolver.ResolveScenePath(id) ?? GameObject.Find(id);
            if (root == null)
            {
                FuseLog.Info($"FUSE world removal skipped missing map mask '{id}'.");
                return false;
            }

            var path = GetTransformPath(root.transform);
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.MapMask, id);
            FuseLog.Info($"FUSE removed map mask '{id}' from '{path}'.");
            return true;
        }

        public static GameObject GetMapMask(string id)
        {
            return !string.IsNullOrWhiteSpace(id)
                ? GameObject.Find("World/" + MapMaskRootName + "/" + id) ?? GameObject.Find(MapMaskRootName + "/" + id)
                : null;
        }

        public static IEnumerable<GameObject> GetAllMapMasks()
        {
            return GetChildren(GetOrCreateWorldRoot(MapMaskRootName, ref _fallbackMapMaskRoot));
        }

        public static FuseMapMask GetMapMaskDefinition(string id)
        {
            return GetMapMaskDefinition(GetMapMask(id));
        }

        public static FuseMapMask GetMapMaskDefinition(GameObject mapMask)
        {
            if (mapMask == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.MapMask, mapMask.name, out FuseMapMask definition);
            definition = definition ?? new FuseMapMask();
            var circle = mapMask.GetComponent<CircleMapMask>();
            if (circle != null)
            {
                definition.Type = "circle";
                definition.Center = mapMask.transform.position;
                definition.Radius = circle.radius;
                return definition;
            }

            var rectangle = mapMask.GetComponent<RectangleMapMask>();
            if (rectangle != null)
            {
                definition.Type = "rectangle";
                definition.Center = mapMask.transform.position;
                definition.Rotation = mapMask.transform.eulerAngles;
                definition.Size = new Vector3(rectangle.sizeX, 0f, rectangle.sizeZ);
                return definition;
            }

            var curves = mapMask.GetComponentsInChildren<CurveMapMask>(true);
            if (curves.Length > 0)
            {
                definition.Type = "curve";
                definition.Width = curves[0].radius;
                var points = new List<Vector3>();
                foreach (var curve in curves.OrderBy(curve => curve.name, StringComparer.OrdinalIgnoreCase))
                {
                    if (points.Count == 0)
                    {
                        points.Add(curve.positionA);
                    }

                    points.Add(curve.positionB);
                }

                definition.Points = points.ToArray();
            }

            return definition;
        }

        public static void RefreshAttachedMapMasks(GameObject root, string reason = null)
        {
            if (root == null)
            {
                return;
            }

            // During a bulk apply that ends in a single terrain rebuild, this
            // per-object refresh is redundant: the trailing rebuild re-evaluates every
            // live mask component at once, and mask components also self-apply on
            // OnEnable. Skip the GetComponentsInChildren + Rebuild churn here.
            //
            // We deliberately do NOT record an approximate footprint for the opt-in
            // targeted invalidation: at apply time the scenery model (and its mask
            // components) hasn't streamed in yet, so there is nothing to measure, and a
            // fixed-size box would under-cover large rectangle/curve masks and leave
            // dark, uncut terrain. Flag the footprint incomplete instead so the trailing
            // reload falls back to a full rebuild (FuseTerrainRefreshScope.BoundsComplete).
            if (FuseTerrainRefreshScope.IsDeferringMaskRefresh)
            {
                FuseTerrainRefreshScope.NoteDeferredRefresh(0);
                FuseTerrainRefreshScope.MarkBoundsIncomplete();
                return;
            }

            var refreshed = 0;
            foreach (var component in root.GetComponentsInChildren<MaskComponentBase>(true))
            {
                if (component == null || !component.isActiveAndEnabled)
                {
                    continue;
                }

                try
                {
                    component.Rebuild();
                    refreshed++;
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE map mask refresh failed root='{root.name}' operation='{reason ?? "unspecified"}' " +
                        $"component='{component.GetType().Name}': {ex.Message}");
                }
            }

            if (refreshed > 0)
            {
                FuseLog.Info(
                    $"FUSE refreshed {refreshed} attached map mask component(s) on '{root.name}' " +
                    $"after '{reason ?? "unspecified"}'.");
            }
        }

        /// <summary>
        /// Re-homes a scenery's terrain map masks from components welded inside its streamed
        /// model into standalone, always-active objects under the permanent
        /// <c>FUSE Map Masks</c> root.
        ///
        /// Design principle — a FUSE scenery is a STREAMED VISUAL MODEL plus a PERSISTENT
        /// WORLD-CONTRIBUTION. The <c>SceneryAssetInstance</c> streams its visual model in/out
        /// by camera distance, but a terrain mask is a world contribution that must outlive
        /// that streaming. Welded into the model it rides the cull lifecycle: it re-bakes
        /// terrain on every load (slow) and loses its flatten/cut when the model streams out
        /// or a teleport re-bakes terrain mid-stream, leaving the building buried in
        /// un-flattened ground. Hosted standalone it is baked once and always applied while
        /// the visual streams freely. Save-state already follows this shape (its
        /// KeyValueObject lives on the persistent scenery root, not the model — see
        /// FuseSceneryAnimationFixPatches). Effects that only matter up close — colliders,
        /// audio, lights, particles — are intentionally left in the streamed model: they are
        /// loaded whenever the player is near enough to perceive them, so a brief stream-in
        /// gap is acceptable rather than worth a permanent companion.
        ///
        /// Mechanism: called from <c>SceneryAPI.AddScenery</c> via
        /// <c>SceneryAssetInstance.OnDidLoadModels</c>, so it runs the moment the model (and
        /// its mask components) finish streaming in, and again on any reload. Idempotent — the
        /// standalone is created once (keyed by scenery id + mask index via
        /// <see cref="BuildDecoupledMaskId"/>) and the welded copy is disabled on every load
        /// so it never bakes on the model. Fail-safe — if a mask can't be cloned the welded
        /// original is left enabled rather than dropped. Cleaned up by
        /// <see cref="RemoveDecoupledMasksFor"/> on scenery removal/update. To decouple a
        /// future world-contribution type, mirror this shape: clone to the persistent root on
        /// load, disable the welded source, clean up by id on removal.
        /// </summary>
        internal static int DecoupleAttachedMapMasks(GameObject sceneryRoot, string id)
        {
            if (sceneryRoot == null || string.IsNullOrEmpty(id))
            {
                return 0;
            }

            // A freshly (re)loaded model can come back with an object-mask forceRenderingOff
            // hide stuck on, leaving the building invisible; clear that so it draws. Scoped to
            // forceRenderingOff only, so pack-authored disabled renderers stay disabled.
            ClearForcedRendererHides(sceneryRoot);

            var maskRoot = GetOrCreateWorldRoot(MapMaskRootName, ref _fallbackMapMaskRoot);
            if (maskRoot == null)
            {
                return 0;
            }

            var masks = sceneryRoot.GetComponentsInChildren<MapMaskBase>(true);
            var decoupled = 0;
            var union = default(Bounds);
            var haveUnion = false;
            for (var index = 0; index < masks.Length; index++)
            {
                var attached = masks[index];
                if (attached == null)
                {
                    continue;
                }

                // One of our own standalone masks (re-entrant call): leave it alone.
                if (attached.transform.IsChildOf(maskRoot))
                {
                    continue;
                }

                // The mask's GAME-space position, used for the rebake footprint below (and the
                // diag probe). Computed from rebase-invariant inputs rather than the absolute
                // transform: this hook runs while the floating-origin rebase (MoveWorld) races
                // the model stream-in, so a burst's absolute positions can mix offset states
                // and a union over them spans a whole origin block.
                var gamePosition = ComputeMaskGamePosition(
                    sceneryRoot.transform.localPosition,
                    sceneryRoot.transform.position,
                    attached.transform.position);

                // Ownership is tracked by a marker component (not the name), so a user-authored
                // mask that happens to share the generated name is never mistaken for our clone.
                if (FindDecoupledMask(maskRoot, id, index) == null)
                {
                    try
                    {
                        CloneMaskToStandalone(BuildDecoupledMaskId(id, index), attached, maskRoot, id, index, gamePosition);
                        decoupled++;
                    }
                    catch (Exception ex)
                    {
                        // Fail-safe: keep the welded mask working rather than lose the flatten.
                        FuseLog.Warning(
                            $"FUSE could not decouple map mask #{index} from scenery '{id}': " +
                            $"{ex.Message}; leaving it attached.");
                        continue;
                    }
                }

                // Stop the welded copy baking on the streamed model. Idempotent across
                // reloads: the standalone above already holds the flatten/cut.
                attached.enabled = false;

                // Union this mask's GAME-space footprint so the post-burst re-bake covers its
                // tiles. Game space, not world: a decouple burst can straddle a MoveWorld
                // rebase, so absolute world footprints captured across the burst mix offset
                // states and inflate the union by ~a full origin block (mass-invalidating
                // thousands of meters of terrain — the "everything loads slower" symptom).
                var maskBounds = MaskGameBounds(attached, gamePosition);
                if (haveUnion)
                {
                    union.Encapsulate(maskBounds);
                }
                else
                {
                    union = maskBounds;
                    haveUnion = true;
                }
            }

            if (decoupled > 0)
            {
                FuseLog.Info(
                    $"FUSE decoupled {decoupled} map mask(s) from scenery '{id}' into standalone " +
                    $"'{MapMaskRootName}' object(s); the terrain mask now survives the model streaming and teleports.");
            }

            // The single map-load terrain rebuild runs BEFORE these masks stream in with their
            // building model, and the game's own per-modifier invalidate is debounced and starved
            // behind the spawn tile-load backlog — so the freshly-registered flatten/cut modifiers
            // never re-bake an already-built tile (the spawn/roundhouse tile stays unmasked). Ask
            // the rebaker to re-bake the touched tiles once the decouple burst settles: targeted
            // (terrain-only, no scenery re-stream) and coalesced (one pass for the whole burst).
            // Only when something NEW was decoupled: a routine stream-in that merely reuses
            // existing standalones changed no modifiers, and masked buildings stream in and out
            // constantly now that they cull like ordinary scenery.
            if (decoupled > 0 && haveUnion)
            {
                FUSE.Runtime.Lifecycle.FuseDecoupledMaskTerrainRebaker.Request(union);
            }

            return decoupled;
        }

        // GAME-space footprint of a map mask (centered on its computed game position), used only
        // to pick which terrain tiles the post-decouple re-bake must invalidate. Deliberately
        // generous: over-covering re-bakes a few harmless extra tiles, while under-covering would
        // leave uncut/​unflattened terrain.
        private static Bounds MaskGameBounds(MapMaskBase mask, Vector3 gameCenter)
        {
            var half = 8f;
            if (mask is CircleMapMask circle)
            {
                half = Mathf.Max(circle.radius, 1f) + 4f;
            }
            else if (mask is RectangleMapMask rectangle)
            {
                half = (Mathf.Max(rectangle.sizeX, rectangle.sizeZ) * 0.5f) + 4f;
            }
            else if (mask is CurveMapMask curve)
            {
                // Curve masks span between two authored endpoints, which are unbounded — a long
                // curve easily exceeds any flat half-extent. Cover both endpoints explicitly,
                // mapped to game space by the same translation-only delta the center uses.
                var t = mask.transform;
                var bounds = new Bounds(gameCenter, Vector3.zero);
                bounds.Encapsulate(gameCenter + (t.TransformPoint(curve.positionA) - t.position));
                bounds.Encapsulate(gameCenter + (t.TransformPoint(curve.positionB) - t.position));
                bounds.Expand(new Vector3(
                    2f * (Mathf.Max(curve.radius + curve.falloff, 16f) + 16f),
                    64f,
                    2f * (Mathf.Max(curve.radius + curve.falloff, 16f) + 16f)));
                return bounds;
            }

            return new Bounds(gameCenter, new Vector3(half * 2f, 64f, half * 2f));
        }

        private static GameObject CloneMaskToStandalone(string name, MapMaskBase source, Transform maskRoot, string ownerSceneryId, int sourceIndex, Vector3 gamePosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(maskRoot, false);
            go.SetActive(false);
            // Copy the welded mask's live transform. The mask and its scenery chain are
            // always in a mutually consistent floating-origin state with MapManager's
            // game<->world offset (rebases update both atomically), so OnEnable's World-space
            // registration lands on the correct game tile from either side of a rebase.
            go.transform.position = source.transform.position;
            go.transform.rotation = source.transform.rotation;
            // Match the source mask's WORLD scale, not identity. CircleMapMask/RectangleMapMask
            // descriptors are scale-independent (position + radius/size), so this is a no-op for
            // them; but CurveMapMask builds its footprint via transform.TransformPoint, which reads
            // lossyScale — forcing Vector3.one there shrinks/moves the curve off the building.
            go.transform.localScale = source.transform.lossyScale;

            // Ownership marker so reuse/cleanup never depend on the (cosmetic) GameObject name.
            var owner = go.AddComponent<FuseDecoupledMaskMarker>();
            owner.OwnerSceneryId = ownerSceneryId;
            owner.SourceIndex = sourceIndex;

            if (source is CircleMapMask sourceCircle)
            {
                var clone = go.AddComponent<CircleMapMask>();
                CopyCommonMaskFields(sourceCircle, clone);
                clone.radius = sourceCircle.radius;
            }
            else if (source is RectangleMapMask sourceRectangle)
            {
                var clone = go.AddComponent<RectangleMapMask>();
                CopyCommonMaskFields(sourceRectangle, clone);
                clone.sizeX = sourceRectangle.sizeX;
                clone.sizeZ = sourceRectangle.sizeZ;
                clone.degrees = sourceRectangle.degrees;
            }
            else if (source is CurveMapMask sourceCurve)
            {
                var clone = go.AddComponent<CurveMapMask>();
                CopyCommonMaskFields(sourceCurve, clone);
                clone.positionA = sourceCurve.positionA;
                clone.positionB = sourceCurve.positionB;
                clone.rotationA = sourceCurve.rotationA;
                clone.rotationB = sourceCurve.rotationB;
                clone.sizeA = sourceCurve.sizeA;
                clone.sizeB = sourceCurve.sizeB;
                clone.radiusNoise = sourceCurve.radiusNoise;
                clone.noiseScale = sourceCurve.noiseScale;
            }
            else
            {
                UnityEngine.Object.Destroy(go);
                throw new InvalidOperationException($"Unsupported map mask type '{source.GetType().Name}'.");
            }

            // OnEnable self-applies the modifier to the (persistent) terrain. Enable the
            // standalone BEFORE the caller disables the welded original so the flatten/cut
            // is never momentarily dropped. NOT registered with
            // WorldTransformer.AddObjectToMove: the modifier is stored offset-independently in
            // game space the moment it registers, the clone has nothing visual to keep aligned,
            // and its parent root already rides rebases — a move registration would
            // double-shift it on every world move.
            go.SetActive(true);
            RegisterDecoupledMask(maskRoot, owner);

            // Decisive probe (gated on the existing scenery diagnostics flag): the type + game
            // position + runtime world position + scale of each decoupled mask is exactly what's
            // needed to tell a curve-scale miss from a placement/offset miss from a
            // correctly-placed-but-inert mask. The Bryson roundhouse masks sit near game-space
            // (~4300-4330, 529, 5375-5500); compare gamePos.
            if (FuseSettings.EnableSceneryCullingDiagnostics)
            {
                var srcTransform = source.transform;
                var extra = source is RectangleMapMask rect
                    ? $" sizeX={rect.sizeX} sizeZ={rect.sizeZ} deg={rect.degrees}"
                    : source is CurveMapMask curve
                        ? $" curveA={curve.positionA} curveB={curve.positionB}"
                        : string.Empty;
                FuseLog.Info(
                    $"FUSE diag map-mask decouple id='{ownerSceneryId}' #{sourceIndex} type='{source.GetType().Name}' " +
                    $"gamePos={gamePosition} srcWorldPos={srcTransform.position} cloneWorldPos={go.transform.position} " +
                    $"srcLossyScale={srcTransform.lossyScale} setHeight={source.enableSetHeight} " +
                    $"cutTrees={source.enableCutTrees} mask='{source.maskName}' radius={source.radius}{extra}.");
            }

            return go;
        }

        /// <summary>
        /// GAME-space position of a welded mask, computed from rebase-invariant inputs only —
        /// used to anchor the post-decouple REBAKE footprint (<see cref="MaskGameBounds"/>).
        /// The floating-origin rebase (Helpers.WorldTransformer) is a pure translation applied
        /// to whole root objects, so: (a) the welded mask and its scenery root are in the same
        /// hierarchy and therefore always in the same rebase state — their world-position delta
        /// carries no offset in ANY state; and (b) the scenery root's LOCAL position under its
        /// container is the authored game position (SceneryAPI parents with
        /// <c>SetParent(parent, false)</c> and writes the definition position to localPosition),
        /// and a parent translation never changes a child's localPosition. Their sum is the
        /// same in every rebase state, so a decouple burst that straddles a MoveWorld can be
        /// unioned without mixing offset states (an absolute-position union across that
        /// boundary spans a whole origin block and mass-invalidates terrain).
        /// </summary>
        internal static Vector3 ComputeMaskGamePosition(
            Vector3 sceneryRootLocalPosition,
            Vector3 sceneryRootWorldPosition,
            Vector3 maskWorldPosition)
        {
            return sceneryRootLocalPosition + (maskWorldPosition - sceneryRootWorldPosition);
        }

        private static void CopyCommonMaskFields(MapMaskBase source, MapMaskBase destination)
        {
            destination.radius = source.radius;
            destination.falloff = source.falloff;
            destination.enableSetHeight = source.enableSetHeight;
            destination.enableCutTrees = source.enableCutTrees;
            destination.enableMaskModifier = source.enableMaskModifier;
            destination.maskName = source.maskName;
            destination.order = source.order;
        }

        private static void ClearForcedRendererHides(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            // Only clear forceRenderingOff -- a runtime culling flag that can stick "on" and
            // leave a loaded building invisible. Deliberately do NOT touch renderer.enabled:
            // a pack author may have intentionally disabled specific sub-mesh renderers, and
            // forcing them on would override that intent.
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.forceRenderingOff = false;
            }
        }

        // Decoupled masks are NAMED "<sceneryId>__mask<NN>" for readability only. Ownership
        // (reuse on reload + cleanup on removal/update) is tracked by the
        // FuseDecoupledMaskMarker component, never the name, so a user-authored mask that
        // happens to share the generated name is never reused or destroyed by mistake.
        internal const string DecoupledMaskInfix = "__mask";

        /// <summary>Pure: the standalone id for a scenery's decoupled mask at <paramref name="index"/>.</summary>
        internal static string BuildDecoupledMaskId(string sceneryId, int index)
        {
            return sceneryId + DecoupledMaskInfix + index.ToString("D2");
        }

        /// <summary>
        /// Inert ownership marker on a standalone mask that
        /// <see cref="DecoupleAttachedMapMasks"/> cloned from a scenery's welded mask.
        /// Ownership decisions (reuse on reload, cleanup on removal/update) read this marker,
        /// not the GameObject name, so a user-authored mask that shares the generated name is
        /// never reused or destroyed by mistake.
        /// </summary>
        internal sealed class FuseDecoupledMaskMarker : MonoBehaviour
        {
            public string OwnerSceneryId;
            public int SourceIndex;

            private void OnDestroy()
            {
                UnregisterDecoupledMask(this);
            }
        }

        /// <summary>
        /// The standalone mask this scenery already decoupled for the welded mask at
        /// <paramref name="sourceIndex"/> (matched by ownership marker), or null if it has
        /// not been cloned yet.
        /// </summary>
        private static GameObject FindDecoupledMask(Transform maskRoot, string sceneryId, int sourceIndex)
        {
            if (maskRoot == null || string.IsNullOrEmpty(sceneryId))
            {
                return null;
            }

            EnsureDecoupledMaskIndex(maskRoot);
            if (!DecoupledMasksByOwner.TryGetValue(sceneryId, out var masks))
            {
                return null;
            }

            for (var i = masks.Count - 1; i >= 0; i--)
            {
                var marker = masks[i];
                if (marker == null)
                {
                    masks.RemoveAt(i);
                    continue;
                }

                if (marker.SourceIndex == sourceIndex)
                {
                    return marker.gameObject;
                }
            }

            if (masks.Count == 0)
            {
                DecoupledMasksByOwner.Remove(sceneryId);
            }

            return null;
        }

        private static void EnsureDecoupledMaskIndex(Transform maskRoot)
        {
            // Keep the Transform itself rather than only its instance id: Unity can recycle ids
            // after destruction, while its object equality cleanly distinguishes the next root.
            if (maskRoot != null && _indexedDecoupledMaskRoot == maskRoot)
            {
                return;
            }

            DecoupledMasksByOwner.Clear();
            _indexedDecoupledMaskRoot = maskRoot;
            if (maskRoot == null)
            {
                return;
            }

            // This is the only full-root scan. It runs once per mask-root lifetime so an index can
            // recover after a Unity domain/map lifecycle transition without assuming static state
            // survived in lockstep with scene objects.
            for (var i = 0; i < maskRoot.childCount; i++)
            {
                var child = maskRoot.GetChild(i);
                var marker = child != null ? child.GetComponent<FuseDecoupledMaskMarker>() : null;
                RegisterDecoupledMaskCore(marker);
            }
        }

        private static void RegisterDecoupledMask(Transform maskRoot, FuseDecoupledMaskMarker marker)
        {
            EnsureDecoupledMaskIndex(maskRoot);
            RegisterDecoupledMaskCore(marker);
        }

        private static void RegisterDecoupledMaskCore(FuseDecoupledMaskMarker marker)
        {
            if (marker == null || string.IsNullOrEmpty(marker.OwnerSceneryId))
            {
                return;
            }

            if (!DecoupledMasksByOwner.TryGetValue(marker.OwnerSceneryId, out var masks))
            {
                masks = new List<FuseDecoupledMaskMarker>(1);
                DecoupledMasksByOwner.Add(marker.OwnerSceneryId, masks);
            }

            for (var i = 0; i < masks.Count; i++)
            {
                if (masks[i] == marker)
                {
                    return;
                }
            }

            masks.Add(marker);
        }

        private static void UnregisterDecoupledMask(FuseDecoupledMaskMarker marker)
        {
            // OnDestroy runs while Unity is invalidating the object, so read the plain owner field
            // and remove by reference without relying on Unity's overloaded null equality.
            if (ReferenceEquals(marker, null) || string.IsNullOrEmpty(marker.OwnerSceneryId))
            {
                return;
            }

            if (!DecoupledMasksByOwner.TryGetValue(marker.OwnerSceneryId, out var masks))
            {
                return;
            }

            for (var i = masks.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(masks[i], marker) || masks[i] == null)
                {
                    masks.RemoveAt(i);
                }
            }

            if (masks.Count == 0)
            {
                DecoupledMasksByOwner.Remove(marker.OwnerSceneryId);
            }
        }

        /// <summary>
        /// Destroys the standalone masks that <see cref="DecoupleAttachedMapMasks"/> created
        /// for scenery <paramref name="sceneryId"/>. Call when the scenery is removed (so a
        /// deleted building doesn't leave its terrain permanently flattened) or when its
        /// definition/position changes (so the next load re-decouples at the new transform
        /// instead of leaving a stale mask behind). Like <see cref="TryRemoveMapMask"/>, the
        /// terrain reverts on the next rebuild rather than being force-rebuilt here.
        /// </summary>
        internal static int RemoveDecoupledMasksFor(string sceneryId)
        {
            if (string.IsNullOrEmpty(sceneryId))
            {
                return 0;
            }

            var root = GetOrCreateWorldRoot(MapMaskRootName, ref _fallbackMapMaskRoot);
            if (root == null)
            {
                return 0;
            }

            EnsureDecoupledMaskIndex(root);
            if (!DecoupledMasksByOwner.TryGetValue(sceneryId, out var ownedMasks))
            {
                return 0;
            }

            // Collect before destroying because Destroy is deferred until the end of the frame.
            // Drop the owner index now so a same-frame re-decouple cannot reuse a destroy-pending
            // clone. Match by ownership marker (not name) so user-authored masks remain untouched.
            var doomed = new List<GameObject>();
            for (var i = 0; i < ownedMasks.Count; i++)
            {
                var marker = ownedMasks[i];
                if (marker != null)
                {
                    doomed.Add(marker.gameObject);
                }
            }

            DecoupledMasksByOwner.Remove(sceneryId);

            foreach (var go in doomed)
            {
                if (go == null)
                {
                    continue;
                }

                go.SetActive(false);
                // Detach before the (end-of-frame) Destroy. A same-frame re-decouple — the
                // UpdateScenery/reload-reapply path calls RemoveDecoupledMasksFor and then
                // DecoupleAttachedMapMasks in one frame — must never find this destroy-pending
                // clone via FindDecoupledMask: "reusing" it skips the re-clone, the welded mask
                // is disabled regardless, and when the Destroy lands the scenery has NO mask at
                // all — a flatten loss no terrain rebuild can recover.
                go.transform.SetParent(null);
                UnityEngine.Object.Destroy(go);
            }

            if (doomed.Count > 0)
            {
                FuseLog.Info($"FUSE removed {doomed.Count} decoupled map mask(s) for scenery '{sceneryId}'.");
            }

            return doomed.Count;
        }

        // --- Visibility-driven decoupled-mask lifecycle ---
        //
        // DecoupleAttachedMapMasks re-homes a building's terrain mask onto a standalone,
        // always-active object so the flatten/cut survives the model streaming out/in and
        // teleports. But "always active" is wrong when the building is INTENTIONALLY hidden — a
        // pack disables its renderers (renderer.enabled = false / a child SetActive(false)) while
        // the scenery GameObject stays active. The standalone mask then keeps flattening the
        // ground under a building that is not drawn, leaving a bare flat patch (e.g. Whittier).
        //
        // FuseDecoupledMaskVisibilityWatcher polls each mask-bearing scenery and calls
        // SetDecoupledMasksActive to drop the standalone mask while the building is hidden and
        // restore it when shown. The decision (ClassifyMaskVisibility) is careful to KEEP the
        // mask when the building is merely streamed out or culled — dropping it only for a real
        // renderer-level hide — so this never undoes the point of the decouple.

        /// <summary>
        /// Whether a mask-bearing scenery's building is currently drawing, and therefore whether
        /// its decoupled terrain mask should apply.
        /// </summary>
        internal enum DecoupledMaskVisibility
        {
            /// <summary>At least one renderer would draw (enabled and active). Keep the mask applied.</summary>
            Visible,

            /// <summary>The model is loaded but a pack has disabled/deactivated every renderer (an
            /// intentional hide). Drop the mask so the ground is not flattened under nothing.</summary>
            Hidden,

            /// <summary>No renderers to inspect — the model is streamed out (or not yet loaded), so
            /// visible and hidden are indistinguishable. Keep the last known state (and, by default,
            /// keep the mask): a streamed-out building must retain its terrain contribution.</summary>
            Indeterminate
        }

        /// <summary>
        /// A Unity-free snapshot of the three renderer flags the visibility decision can read, so
        /// <see cref="ClassifyMaskVisibility"/> is unit-testable without a live game.
        /// </summary>
        internal readonly struct SceneryRendererVisibility
        {
            public SceneryRendererVisibility(bool enabled, bool activeInHierarchy, bool forceRenderingOff)
            {
                Enabled = enabled;
                ActiveInHierarchy = activeInHierarchy;
                ForceRenderingOff = forceRenderingOff;
            }

            /// <summary>Renderer.enabled — a pack clears this to hide a sub-mesh.</summary>
            public bool Enabled { get; }

            /// <summary>Renderer.gameObject.activeInHierarchy — false when a pack deactivates the holder.</summary>
            public bool ActiveInHierarchy { get; }

            /// <summary>
            /// Renderer.forceRenderingOff — the GAME CULLER's hide flag (resident band 2),
            /// deliberately NOT consulted by the decision: a culled-but-resident building must keep
            /// its mask. Carried on the snapshot only so the "culled, not hidden" case is explicit
            /// in tests.
            /// </summary>
            public bool ForceRenderingOff { get; }
        }

        /// <summary>
        /// Pure visibility decision for a mask-bearing scenery, mirroring the renderer-presence
        /// audit (<c>FusePrefabSanitizer.ValidateRendererPresence</c> /
        /// <c>AuditsToolPage</c>): a renderer "would draw" when it is
        /// <c>enabled &amp;&amp; activeInHierarchy</c>.
        ///
        /// Crucially this separates an INTENTIONAL hide from the game culler streaming the model
        /// out — the two states look alike but need opposite mask handling:
        /// <list type="bullet">
        /// <item>No renderers =&gt; the model is streamed out / not loaded =&gt;
        /// <see cref="DecoupledMaskVisibility.Indeterminate"/> (keep the mask; a streamed-out
        /// building must retain its terrain contribution — the reason the mask was decoupled).</item>
        /// <item>Any renderer enabled &amp; active =&gt; <see cref="DecoupledMaskVisibility.Visible"/>.
        /// <c>forceRenderingOff</c> is ignored on purpose: the culler parks a resident model with
        /// <c>forceRenderingOff = true</c> while leaving <c>enabled</c>/active set, so a culled
        /// building still reads Visible and KEEPS its mask.</item>
        /// <item>At least one holder active but none drawing — the culler set
        /// <c>renderer.enabled = false</c> for the resident distance band / off-screen =&gt;
        /// <see cref="DecoupledMaskVisibility.Indeterminate"/> (KEEP: the culler owns
        /// <c>renderer.enabled</c>, so disabled-but-active is a cull, not a hide).</item>
        /// <item>EVERY holder inactive — a pack/progression <c>SetActive(false)</c>, the one
        /// hide the culler never performs =&gt; <see cref="DecoupledMaskVisibility.Hidden"/>
        /// (drop the mask so a hidden building leaves no flat patch).</item>
        /// </list>
        /// </summary>
        internal static DecoupledMaskVisibility ClassifyMaskVisibility(IReadOnlyList<SceneryRendererVisibility> renderers)
        {
            if (renderers == null || renderers.Count == 0)
            {
                return DecoupledMaskVisibility.Indeterminate;
            }

            var anyActiveHolder = false;
            for (var index = 0; index < renderers.Count; index++)
            {
                var renderer = renderers[index];
                if (renderer.Enabled && renderer.ActiveInHierarchy)
                {
                    return DecoupledMaskVisibility.Visible;
                }

                if (renderer.ActiveInHierarchy)
                {
                    anyActiveHolder = true;
                }
            }

            // No renderer is currently drawing. The game's culler OWNS renderer.enabled — it
            // rewrites enabled = (isVisible && distanceBand < 2) on every CullingSphereStateChanged,
            // so a renderer disabled while its holder is still ACTIVE was turned off by the culler
            // (resident-but-invisible distance band, or off-screen), NOT by a pack hiding the
            // building. Keep the decoupled mask there — that is the whole reason it was decoupled.
            // Only when EVERY holder is inactive (a pack/progression SetActive(false) — the one
            // intentional-hide mechanism the culler never performs) do we drop it, so a genuinely
            // hidden building leaves no flat patch behind.
            return anyActiveHolder
                ? DecoupledMaskVisibility.Indeterminate
                : DecoupledMaskVisibility.Hidden;
        }

        /// <summary>
        /// Folds a freshly captured visibility into a watcher's retained state.
        /// <see cref="DecoupledMaskVisibility.Indeterminate"/> (no renderers — the model is streamed
        /// out / not loaded) holds the last decisive Visible/Hidden value: a building hidden THEN
        /// streamed out keeps its mask dropped, and a visible one keeps it applied. A decisive
        /// reading replaces it. The returned value is both the effective decision and the new
        /// retained value (they are always equal), so a caller stores it and applies the mask when
        /// it is <see cref="DecoupledMaskVisibility.Visible"/>. Pure, so the retention behaviour is
        /// unit-tested without a live game (see FUSE.Tests MapApiMaskVisibilityTests).
        /// </summary>
        internal static DecoupledMaskVisibility ResolveEffectiveMaskVisibility(
            DecoupledMaskVisibility captured,
            DecoupledMaskVisibility lastDecisive)
        {
            return captured == DecoupledMaskVisibility.Indeterminate ? lastDecisive : captured;
        }

        /// <summary>
        /// Activates or deactivates the standalone masks <see cref="DecoupleAttachedMapMasks"/>
        /// created for scenery <paramref name="sceneryId"/> (matched by ownership marker, never the
        /// name, so a user-authored mask is never touched). Deactivating reverts the flatten/cut on
        /// the next terrain rebuild — like <see cref="RemoveDecoupledMasksFor"/> but reversible:
        /// re-activating re-applies it without re-cloning. Driven by
        /// <see cref="FuseDecoupledMaskVisibilityWatcher"/> as the building hides/shows. Returns the
        /// number of standalone masks whose active state actually changed (0 = already in the
        /// requested state, the steady-state path — no log, no terrain churn).
        /// </summary>
        internal static int SetDecoupledMasksActive(string sceneryId, bool active)
        {
            if (string.IsNullOrEmpty(sceneryId))
            {
                return 0;
            }

            var root = GetOrCreateWorldRoot(MapMaskRootName, ref _fallbackMapMaskRoot);
            if (root == null)
            {
                return 0;
            }

            EnsureDecoupledMaskIndex(root);
            if (!DecoupledMasksByOwner.TryGetValue(sceneryId, out var masks))
            {
                return 0;
            }

            var changed = 0;
            for (var i = masks.Count - 1; i >= 0; i--)
            {
                var marker = masks[i];
                if (marker == null)
                {
                    masks.RemoveAt(i);
                    continue;
                }

                var go = marker.gameObject;
                if (go.activeSelf == active)
                {
                    continue;
                }

                go.SetActive(active);
                changed++;
            }

            if (masks.Count == 0)
            {
                DecoupledMasksByOwner.Remove(sceneryId);
            }

            if (changed > 0)
            {
                FuseLog.Info(
                    $"FUSE {(active ? "restored" : "dropped")} {changed} decoupled map mask(s) for scenery " +
                    $"'{sceneryId}' (building {(active ? "shown" : "hidden")}).");
            }

            return changed;
        }

        public static GameObject AddTelegraphPoles(string id, FuseTelegraphPoles definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetTelegraphPoles(id) != null)
            {
                throw new InvalidOperationException($"Telegraph pole set '{id}' already exists.");
            }

            var root = new GameObject(id);
            root.transform.SetParent(GetOrCreateWorldRoot(TelegraphRootName, ref _fallbackTelegraphRoot), false);
            ApplyTelegraphPolesDefinition(root, definition);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TelegraphPoles, id, definition);
            return root;
        }

        public static void UpdateTelegraphPoles(string id, FuseTelegraphPoles definition)
        {
            var root = RequireTelegraphPoles(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyTelegraphPolesDefinition(root, definition);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TelegraphPoles, id, definition);
        }

        public static void RemoveTelegraphPoles(string id)
        {
            if (!TryRemoveTelegraphPoles(id))
            {
                throw new InvalidOperationException($"Telegraph pole set '{id}' was not found.");
            }
        }

        public static bool TryRemoveTelegraphPoles(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            var root = GetTelegraphPoles(id) ?? FusePrefabResolver.ResolveScenePath(id) ?? GameObject.Find(id);
            if (root == null)
            {
                FuseLog.Info($"FUSE world removal skipped missing telegraph pole set '{id}'.");
                return false;
            }

            var path = GetTransformPath(root.transform);
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.TelegraphPoles, id);
            FuseLog.Info($"FUSE removed telegraph pole set '{id}' from '{path}'.");
            return true;
        }

        public static GameObject GetTelegraphPoles(string id)
        {
            return !string.IsNullOrWhiteSpace(id)
                ? GameObject.Find("World/" + TelegraphRootName + "/" + id) ?? GameObject.Find(TelegraphRootName + "/" + id)
                : null;
        }

        public static IEnumerable<GameObject> GetAllTelegraphPoles()
        {
            return GetChildren(GetOrCreateWorldRoot(TelegraphRootName, ref _fallbackTelegraphRoot));
        }

        public static FuseTelegraphPoles GetTelegraphPolesDefinition(string id)
        {
            return GetTelegraphPolesDefinition(GetTelegraphPoles(id));
        }

        public static FuseTelegraphPoles GetTelegraphPolesDefinition(GameObject telegraphPoles)
        {
            if (telegraphPoles == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.TelegraphPoles, telegraphPoles.name, out FuseTelegraphPoles definition);
            definition = definition ?? new FuseTelegraphPoles();
            definition.Points = telegraphPoles.GetComponentsInChildren<TelegraphPole>(true)
                .OrderBy(pole => pole.name, StringComparer.OrdinalIgnoreCase)
                .Select(pole => pole.transform.position)
                .ToArray();
            return definition;
        }

        public static void ApplyTelegraphPoleMovements(string packageId, FuseTelegraphPoleMovement[] movements)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                throw new ArgumentException("Package id is required.", nameof(packageId));
            }

            var normalized = (movements ?? Array.Empty<FuseTelegraphPoleMovement>())
                .Where(movement => movement != null && movement.PoleIndices != null && movement.PoleIndices.Length > 0)
                .ToArray();

            if (normalized.Length == 0)
            {
                ReleaseTelegraphPoleMovements(packageId);
                return;
            }

            TelegraphPoleMovementClaims[packageId] = normalized;
            ReapplyTelegraphPoleMovements($"package '{packageId}' apply");
        }

        public static bool HasTelegraphPoleMovementClaim(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) && TelegraphPoleMovementClaims.ContainsKey(packageId);
        }

        public static void ReleaseTelegraphPoleMovements(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId) || !TelegraphPoleMovementClaims.Remove(packageId))
            {
                return;
            }

            ReapplyTelegraphPoleMovements($"package '{packageId}' unload");
        }

        public static void RestoreAllTelegraphPoleMovements(string reason)
        {
            if (TelegraphPoleOriginalPositions.Count == 0 && TelegraphPoleMovementClaims.Count == 0)
            {
                return;
            }

            var manager = FindTelegraphPoleManager();
            var graph = manager != null ? manager.GetComponent<RuntimeSimpleGraph>() : null;
            var restored = 0;
            var touched = new HashSet<int>();
            if (graph != null)
            {
                foreach (var entry in TelegraphPoleOriginalPositions.ToArray())
                {
                    var node = graph.NodeForId(entry.Key);
                    if (node == null)
                    {
                        continue;
                    }

                    node.position = entry.Value;
                    touched.Add(entry.Key);
                    restored++;
                }

                NotifyTelegraphNodesChanged(manager, graph, touched);
            }

            TelegraphPoleOriginalPositions.Clear();
            TelegraphPoleMovementClaims.Clear();
            FuseLog.Info($"FUSE restored telegraph pole movements for '{reason ?? "unspecified"}' restored={restored}.");
        }

        private static void ReapplyTelegraphPoleMovements(string reason)
        {
            var manager = FindTelegraphPoleManager();
            if (manager == null)
            {
                FuseLog.Warning($"FUSE telegraph pole movement skipped for '{reason}' because TelegraphPoleManager was not found.");
                return;
            }

            var graph = manager.GetComponent<RuntimeSimpleGraph>();
            if (graph == null)
            {
                FuseLog.Warning($"FUSE telegraph pole movement skipped for '{reason}' because TelegraphPoleManager has no SimpleGraph.");
                return;
            }

            var aggregate = new Dictionary<int, Vector3>();
            foreach (var package in TelegraphPoleMovementClaims)
            {
                foreach (var movement in package.Value ?? Array.Empty<FuseTelegraphPoleMovement>())
                {
                    if (movement?.PoleIndices == null)
                    {
                        continue;
                    }

                    foreach (var poleIndex in movement.PoleIndices)
                    {
                        if (poleIndex < 0)
                        {
                            FuseLog.Warning($"FUSE telegraph pole movement skipped invalid pole index package='{package.Key}' poleIndex={poleIndex}.");
                            continue;
                        }

                        aggregate[poleIndex] = aggregate.TryGetValue(poleIndex, out var existing)
                            ? existing + movement.Offset
                            : movement.Offset;
                    }
                }
            }

            var touched = new HashSet<int>();
            var moved = 0;
            var restored = 0;
            foreach (var original in TelegraphPoleOriginalPositions.Keys.ToArray())
            {
                if (aggregate.ContainsKey(original))
                {
                    continue;
                }

                var node = graph.NodeForId(original);
                if (node == null)
                {
                    TelegraphPoleOriginalPositions.Remove(original);
                    continue;
                }

                node.position = TelegraphPoleOriginalPositions[original];
                TelegraphPoleOriginalPositions.Remove(original);
                touched.Add(original);
                restored++;
            }

            foreach (var movement in aggregate)
            {
                var node = graph.NodeForId(movement.Key);
                if (node == null)
                {
                    FuseLog.Warning($"FUSE telegraph pole movement skipped missing base pole node package='<aggregate>' poleIndex={movement.Key}.");
                    continue;
                }

                if (!TelegraphPoleOriginalPositions.TryGetValue(movement.Key, out var originalPosition))
                {
                    originalPosition = node.position;
                    TelegraphPoleOriginalPositions[movement.Key] = originalPosition;
                }

                node.position = originalPosition + movement.Value;
                touched.Add(movement.Key);
                moved++;
            }

            NotifyTelegraphNodesChanged(manager, graph, touched);
            FuseLog.Info($"FUSE applied telegraph pole movements for '{reason}' moved={moved} restored={restored} activePackages={TelegraphPoleMovementClaims.Count}.");
        }

        private static TelegraphPoleManager FindTelegraphPoleManager()
        {
            return UnityEngine.Object.FindObjectsOfType<TelegraphPoleManager>(true).FirstOrDefault();
        }

        private static void NotifyTelegraphNodesChanged(TelegraphPoleManager manager, RuntimeSimpleGraph graph, HashSet<int> touched)
        {
            if (graph == null || touched == null || touched.Count == 0)
            {
                return;
            }

            try
            {
                graph.NotifyDidChangeNodes(touched);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE telegraph pole movement could not notify node changes", ex);
            }

            if (manager == null || !manager.isActiveAndEnabled || TelegraphRebuildMethod == null)
            {
                return;
            }

            try
            {
                TelegraphRebuildMethod.Invoke(manager, Array.Empty<object>());
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE telegraph pole movement could not force telegraph manager rebuild: {ex.GetBaseException().Message}");
            }
        }

        private static void ApplyMapLabelDefinition(MapLabel label, FuseMapLabel definition)
        {
            if (label.transform.parent != null)
            {
                label.transform.parent.localPosition = definition.Position;
                label.transform.parent.localRotation = Quaternion.Euler(definition.Rotation);
            }

            label.text = string.IsNullOrWhiteSpace(definition.Text) ? label.name : definition.Text;
            var isSpeedLimit = TryGetSpeedLimitMph(definition, label.text, out var speedLimitMph);

            var text = label.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                text.text = isSpeedLimit ? speedLimitMph.ToString() : label.text;
                if (definition.Size.HasValue)
                {
                    text.fontSize = definition.Size.Value;
                }

                if (!string.IsNullOrWhiteSpace(definition.Color) && ColorUtility.TryParseHtmlString(definition.Color, out var color))
                {
                    text.color = color;
                }

                if (isSpeedLimit)
                {
                    ConfigureSpeedLimitLabel(text, speedLimitMph);
                }
                else
                {
                    RemoveSpeedLimitCircle(text);
                    ConfigureMapLabelText(text, label.text);
                }
            }
        }

        private static bool TryGetSpeedLimitMph(FuseMapLabel definition, string text, out int speedLimitMph)
        {
            speedLimitMph = 0;
            if (definition?.SpeedLimitMph is int explicitSpeed && explicitSpeed > 0)
            {
                speedLimitMph = explicitSpeed;
                return true;
            }

            var style = definition?.Style ?? string.Empty;
            if (style.Equals("speedLimit", StringComparison.OrdinalIgnoreCase) ||
                style.Equals("speed-limit", StringComparison.OrdinalIgnoreCase))
            {
                var numberMatch = SpeedLimitNumberPattern.Match(text ?? string.Empty);
                if (numberMatch.Success && int.TryParse(numberMatch.Groups["mph"].Value, out speedLimitMph))
                {
                    return true;
                }
            }

            var mphMatch = SpeedLimitTextPattern.Match(text ?? string.Empty);
            return mphMatch.Success && int.TryParse(mphMatch.Groups["mph"].Value, out speedLimitMph);
        }

        private static void ConfigureMapLabelText(TMP_Text text, string value)
        {
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;

            var rect = text.GetComponent<RectTransform>();
            if (rect != null)
            {
                var fontSize = Mathf.Max(text.fontSize, 12f);
                var preferred = text.GetPreferredValues(value ?? string.Empty);
                var estimatedWidth = ((value?.Length ?? 0) + 2) * fontSize * 0.75f;
                var width = Mathf.Max(256f, preferred.x + 32f, estimatedWidth);
                var height = Mathf.Max(64f, preferred.y + 16f, fontSize * 2f);

                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }

            text.ForceMeshUpdate();
        }

        private static void ConfigureSpeedLimitLabel(TMP_Text text, int speedLimitMph)
        {
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = Mathf.Clamp(text.fontSize > 0f ? text.fontSize * 0.72f : 10f, 8f, 11f);

            var rect = text.GetComponent<RectTransform>();
            var diameter = Mathf.Max(23f, text.fontSize * 2.2f);
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, diameter);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, diameter);
            }

            var circle = GetOrCreateSpeedLimitCircle(text);
            if (circle != null)
            {
                circle.gameObject.SetActive(true);
                circle.color = text.color.a > 0.01f ? text.color : Color.white;
                circle.sprite = GetSpeedLimitCircleSprite();
                circle.type = Image.Type.Simple;
                circle.preserveAspect = true;
                circle.raycastTarget = false;

                var circleRect = circle.GetComponent<RectTransform>();
                if (circleRect != null)
                {
                    circleRect.anchorMin = new Vector2(0.5f, 0.5f);
                    circleRect.anchorMax = new Vector2(0.5f, 0.5f);
                    circleRect.pivot = new Vector2(0.5f, 0.5f);
                    circleRect.anchoredPosition = Vector2.zero;
                    circleRect.localScale = Vector3.one;
                    circleRect.rotation = text.transform.rotation;
                    circleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, diameter);
                    circleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, diameter);
                    circleRect.SetAsFirstSibling();
                }
            }

            text.text = speedLimitMph.ToString();
            text.ForceMeshUpdate();
        }

        private static Image GetOrCreateSpeedLimitCircle(TMP_Text text)
        {
            if (text == null)
            {
                return null;
            }

            var parent = text.transform.parent ?? text.transform;
            var existing = parent.Find(SpeedLimitCircleName);
            if (existing != null)
            {
                return existing.GetComponent<Image>() ?? existing.gameObject.AddComponent<Image>();
            }

            var circleObject = new GameObject(SpeedLimitCircleName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            circleObject.transform.SetParent(parent, false);
            circleObject.transform.SetSiblingIndex(Mathf.Max(0, text.transform.GetSiblingIndex()));
            return circleObject.GetComponent<Image>();
        }

        private static void RemoveSpeedLimitCircle(TMP_Text text)
        {
            var parent = text?.transform.parent;
            if (parent == null)
            {
                return;
            }

            var existing = parent.Find(SpeedLimitCircleName);
            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
            }
        }

        private static Sprite GetSpeedLimitCircleSprite()
        {
            if (_speedLimitCircleSprite != null)
            {
                return _speedLimitCircleSprite;
            }

            const int size = 64;
            const float center = (size - 1) * 0.5f;
            const float outerRadius = 30f;
            const float innerRadius = 25f;
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                name = "FUSE Speed Limit Circle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Mathf.Sqrt(Mathf.Pow(x - center, 2f) + Mathf.Pow(y - center, 2f));
                    var outerAlpha = Mathf.Clamp01(outerRadius - distance + 1f);
                    var innerAlpha = Mathf.Clamp01(distance - innerRadius + 1f);
                    var ringAlpha = Mathf.Min(outerAlpha, innerAlpha);
                    var fillAlpha = Mathf.Clamp01(innerRadius - distance + 1f);

                    if (ringAlpha > 0.01f)
                    {
                        var byteAlpha = (byte)Mathf.RoundToInt(ringAlpha * 255f);
                        pixels[(y * size) + x] = new Color32(255, 255, 255, byteAlpha);
                    }
                    else if (fillAlpha > 0.01f)
                    {
                        var byteAlpha = (byte)Mathf.RoundToInt(fillAlpha * 230f);
                        pixels[(y * size) + x] = new Color32(0, 0, 0, byteAlpha);
                    }
                    else
                    {
                        pixels[(y * size) + x] = new Color32(0, 0, 0, 0);
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _speedLimitCircleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            _speedLimitCircleSprite.name = "FUSE Speed Limit Circle";
            return _speedLimitCircleSprite;
        }

        private static void ApplyMapMaskDefinition(GameObject root, FuseMapMask definition)
        {
            var wasActive = root.activeSelf;
            if (wasActive)
            {
                root.SetActive(false);
            }

            ClearComponents<MapMaskBase>(root);
            DestroyChildren(root.transform);

            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var type = (definition.Type ?? string.Empty).Trim().ToLowerInvariant();
            switch (type)
            {
                case "circle":
                    if (!definition.Radius.HasValue || definition.Radius.Value <= 0f)
                    {
                        throw new InvalidOperationException("Circle map masks require a positive radius.");
                    }

                    root.transform.position = definition.Center;
                    var circle = root.AddComponent<CircleMapMask>();
                    ConfigureDefaultMapMask(circle, definition);
                    circle.radius = definition.Radius.Value;
                    break;

                case "rectangle":
                    if (!definition.Size.HasValue || definition.Size.Value.x <= 0f || definition.Size.Value.z <= 0f)
                    {
                        throw new InvalidOperationException("Rectangle map masks require a positive size.");
                    }

                    root.transform.position = definition.Center;
                    root.transform.rotation = Quaternion.Euler(definition.Rotation);
                    var rectangle = root.AddComponent<RectangleMapMask>();
                    ConfigureDefaultMapMask(rectangle, definition);
                    rectangle.radius = 0f;
                    rectangle.falloff = definition.Falloff ?? 10f;
                    rectangle.sizeX = definition.Size.Value.x;
                    rectangle.sizeZ = definition.Size.Value.z;
                    rectangle.degrees = 0f;
                    break;

                case "curve":
                    if (definition.Points == null || definition.Points.Length < 2)
                    {
                        throw new InvalidOperationException("Curve map masks require at least two points.");
                    }

                    var width = definition.Width.GetValueOrDefault(8f);
                    for (var index = 0; index < definition.Points.Length - 1; index++)
                    {
                        var pointA = definition.Points[index];
                        var pointB = definition.Points[index + 1];
                        if ((pointB - pointA).sqrMagnitude <= 0.0001f)
                        {
                            continue;
                        }

                        var segment = new GameObject("segment-" + index.ToString("D3"));
                        segment.transform.SetParent(root.transform, false);
                        segment.transform.position = Vector3.zero;
                        segment.transform.rotation = Quaternion.identity;

                        var curve = segment.AddComponent<CurveMapMask>();
                        ConfigureDefaultMapMask(curve, definition);
                        curve.radius = width;
                        curve.falloff = definition.Falloff ?? 10f;
                        curve.positionA = pointA;
                        curve.positionB = pointB;
                        curve.rotationA = Quaternion.LookRotation((pointB - pointA).normalized, Vector3.up).eulerAngles;
                        curve.rotationB = curve.rotationA;
                        curve.sizeA = 1f;
                        curve.sizeB = 1f;
                        curve.radiusNoise = 0f;
                        curve.noiseScale = 1f;
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Unknown map mask type '{definition.Type}'.");
            }

            root.SetActive(wasActive);
            if (wasActive)
            {
                RefreshAttachedMapMasks(root, $"map mask definition '{root.name}'");
            }
        }

        private static void ApplyTelegraphPolesDefinition(GameObject root, FuseTelegraphPoles definition)
        {
            if (definition.Points == null || definition.Points.Length < 2)
            {
                throw new InvalidOperationException("Telegraph pole sets require at least two points.");
            }

            DestroyChildren(root.transform);

            var poleTemplate = ResolveTelegraphPolePrefab(definition);
            var wireTemplate = ResolveTelegraphWirePrefab(definition);
            if (poleTemplate == null)
            {
                throw new InvalidOperationException("A telegraph pole prefab could not be resolved.");
            }

            if (wireTemplate == null)
            {
                throw new InvalidOperationException("A telegraph wire prefab could not be resolved.");
            }

            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var positions = SamplePolyline(definition.Points, Mathf.Max(definition.Spacing.GetValueOrDefault(40f), 1f));
            var poles = new List<TelegraphPole>(positions.Count);
            for (var index = 0; index < positions.Count; index++)
            {
                var tangent = GetTangent(positions, index);
                if (tangent.sqrMagnitude <= 0.0001f)
                {
                    tangent = Vector3.forward;
                }

                var rotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);
                var pole = UnityEngine.Object.Instantiate(poleTemplate, positions[index], rotation, root.transform);
                pole.name = "pole-" + index.ToString("D3");
                pole.localBasePosition = Vector3.zero;
                poles.Add(pole);
            }

            var wireIndex = 0;
            for (var index = 0; index < poles.Count - 1; index++)
            {
                wireIndex = CreateWiresBetween(root.transform, poles[index], poles[index + 1], wireTemplate, wireIndex);
            }

            root.SetActive(true);
        }

        private static void ConfigureDefaultMapMask(MapMaskBase mask, FuseMapMask definition = null)
        {
            mask.enableCutTrees = definition?.EnableCutTrees ?? false;
            mask.enableMaskModifier = definition?.EnableMaskModifier ?? false;
            mask.enableSetHeight = definition?.EnableSetHeight ?? false;

            // Convert from the FuseMapMask MaskName (from FUSE.Authoring.Data) into the Map runtime MaskName
            if (definition?.MaskName.HasValue == true)
            {
                var sourceName = definition.MaskName.Value.ToString();
                if (Enum.TryParse<Map.Runtime.MapModifiers.MaskName>(sourceName, true, out var mapped))
                {
                    mask.maskName = mapped;
                }
                else
                {
                    // Fallback if names don't match
                    mask.maskName = Map.Runtime.MapModifiers.MaskName.Object;
                }
            }
            else
            {
                mask.maskName = Map.Runtime.MapModifiers.MaskName.Object;
            }

            mask.order = definition?.Order ?? 0;
            mask.falloff = definition?.Falloff ?? 10f;
        }

        private static int CreateWiresBetween(Transform parent, TelegraphPole a, TelegraphPole b, TelegraphWire wireTemplate, int wireIndex)
        {
            if (a.rows == null || b.rows == null || a.rows.Length == 0 || b.rows.Length == 0)
            {
                return wireIndex;
            }

            var sameDirection = Vector3.Dot(a.transform.forward, b.transform.forward) > 0f;
            var maxConnections = Mathf.Min(a.CountPoints(), b.CountPoints());
            var rowA = a.rows.Length - 1;
            var rowB = b.rows.Length - 1;
            var pointA = 0;
            var pointB = 0;

            for (var index = 0; index < maxConnections && rowA >= 0 && rowB >= 0; index++)
            {
                var rowPointsA = a.rows[rowA].points;
                var rowPointsB = b.rows[rowB].points;
                if (rowPointsA == null || rowPointsB == null || rowPointsA.Length == 0 || rowPointsB.Length == 0)
                {
                    break;
                }

                var bPointIndex = sameDirection ? pointB : (rowPointsB.Length - 1 - pointB);
                var positionA = a.transform.TransformPoint(rowPointsA[pointA]);
                var positionB = b.transform.TransformPoint(rowPointsB[bPointIndex]);

                var wire = UnityEngine.Object.Instantiate(wireTemplate, parent);
                wire.name = "wire-" + wireIndex.ToString("D3");
                wire.Configure(positionA, positionB);
                wireIndex++;

                pointA++;
                pointB++;

                if (pointA >= rowPointsA.Length)
                {
                    rowA--;
                    pointA = 0;
                }

                if (pointB >= rowPointsB.Length)
                {
                    rowB--;
                    pointB = 0;
                }
            }

            return wireIndex;
        }

        private static TelegraphPole ResolveTelegraphPolePrefab(FuseTelegraphPoles definition)
        {
            if (!string.IsNullOrWhiteSpace(definition.PolePrefab))
            {
                var prefab = FusePrefabResolver.Resolve(definition.PolePrefab);
                if (prefab == null)
                {
                    return null;
                }

                return prefab.GetComponent<TelegraphPole>() ?? prefab.GetComponentInChildren<TelegraphPole>(true);
            }

            var manager = UnityEngine.Object.FindObjectsOfType<TelegraphPoleManager>(true).FirstOrDefault();
            var prefabs = PolePrefabsField?.GetValue(manager) as IEnumerable<TelegraphPole>;
            if (prefabs == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(definition.Profile))
            {
                var match = prefabs.FirstOrDefault(prefab => prefab != null && prefab.name.IndexOf(definition.Profile, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null)
                {
                    return match;
                }
            }

            return prefabs.FirstOrDefault(prefab => prefab != null);
        }

        private static TelegraphWire ResolveTelegraphWirePrefab(FuseTelegraphPoles definition)
        {
            if (!string.IsNullOrWhiteSpace(definition.WirePrefab))
            {
                var prefab = FusePrefabResolver.Resolve(definition.WirePrefab);
                if (prefab == null)
                {
                    return null;
                }

                return prefab.GetComponent<TelegraphWire>() ?? prefab.GetComponentInChildren<TelegraphWire>(true);
            }

            var manager = UnityEngine.Object.FindObjectsOfType<TelegraphPoleManager>(true).FirstOrDefault();
            return WirePrefabField?.GetValue(manager) as TelegraphWire;
        }

        private static List<Vector3> SamplePolyline(Vector3[] sourcePoints, float spacing)
        {
            var points = new List<Vector3>();
            if (sourcePoints == null || sourcePoints.Length == 0)
            {
                return points;
            }

            points.Add(sourcePoints[0]);
            var carry = 0f;
            for (var index = 0; index < sourcePoints.Length - 1; index++)
            {
                var start = sourcePoints[index];
                var end = sourcePoints[index + 1];
                var delta = end - start;
                var length = delta.magnitude;
                if (length <= 0.0001f)
                {
                    continue;
                }

                var consumed = 0f;
                while (carry + (length - consumed) >= spacing)
                {
                    var nextStep = spacing - carry;
                    consumed += nextStep;
                    points.Add(Vector3.Lerp(start, end, consumed / length));
                    carry = 0f;
                }

                carry += length - consumed;
            }

            if ((points[points.Count - 1] - sourcePoints[sourcePoints.Length - 1]).sqrMagnitude > 0.0001f)
            {
                points.Add(sourcePoints[sourcePoints.Length - 1]);
            }

            return points;
        }

        private static Vector3 GetTangent(List<Vector3> positions, int index)
        {
            if (positions == null || positions.Count == 0)
            {
                return Vector3.zero;
            }

            if (positions.Count == 1)
            {
                return Vector3.forward;
            }

            if (index <= 0)
            {
                return positions[1] - positions[0];
            }

            if (index >= positions.Count - 1)
            {
                return positions[index] - positions[index - 1];
            }

            return positions[index + 1] - positions[index - 1];
        }

        private static MapLabel RequireMapLabel(string id)
        {
            var label = GetMapLabel(id);
            if (label == null)
            {
                throw new InvalidOperationException($"Map label '{id}' was not found.");
            }

            return label;
        }

        private static GameObject RequireMapMask(string id)
        {
            var mapMask = GetMapMask(id);
            if (mapMask == null)
            {
                throw new InvalidOperationException($"Map mask '{id}' was not found.");
            }

            return mapMask;
        }

        private static GameObject RequireTelegraphPoles(string id)
        {
            var telegraph = GetTelegraphPoles(id);
            if (telegraph == null)
            {
                throw new InvalidOperationException($"Telegraph pole set '{id}' was not found.");
            }

            return telegraph;
        }

        private static Transform GetOrCreateWorldRoot(string name, ref Transform fallbackRoot)
        {
            // GameObject.Find("World") is a scene-wide search. Cache the Transform for the map
            // lifetime; Unity's destroyed-object null semantics automatically invalidate it on
            // unload so the next map can be discovered safely.
            if (_worldRoot == null)
            {
                var worldObject = GameObject.Find("World");
                _worldRoot = worldObject != null ? worldObject.transform : null;
            }

            if (_worldRoot != null)
            {
                if (fallbackRoot != null && fallbackRoot.parent == _worldRoot)
                {
                    return fallbackRoot;
                }

                var existing = _worldRoot.Find(name);
                if (existing != null)
                {
                    fallbackRoot = existing;
                    return existing;
                }

                var root = new GameObject(name);
                root.transform.SetParent(_worldRoot, false);
                fallbackRoot = root.transform;
                return fallbackRoot;
            }

            if (fallbackRoot == null)
            {
                fallbackRoot = new GameObject(name).transform;
                UnityEngine.Object.DontDestroyOnLoad(fallbackRoot.gameObject);
            }

            return fallbackRoot;
        }

        private static IEnumerable<GameObject> GetChildren(Transform root)
        {
            if (root == null)
            {
                return Enumerable.Empty<GameObject>();
            }

            var children = new List<GameObject>(root.childCount);
            for (var index = 0; index < root.childCount; index++)
            {
                children.Add(root.GetChild(index).gameObject);
            }

            return children;
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

        private static void DestroyChildren(Transform transform)
        {
            for (var index = transform.childCount - 1; index >= 0; index--)
            {
                UnityEngine.Object.Destroy(transform.GetChild(index).gameObject);
            }
        }

        private static void ClearComponents<T>(GameObject gameObject) where T : Component
        {
            foreach (var component in gameObject.GetComponents<T>())
            {
                UnityEngine.Object.Destroy(component);
            }
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
