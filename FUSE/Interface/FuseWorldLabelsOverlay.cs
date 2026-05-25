using System;
using System.Collections.Generic;
using FUSE.Runtime.API;
using FUSE.Runtime.Cache;
using FUSE.Infrastructure;
using Track;
using UnityEngine;

namespace FUSE.Interface
{
    /// <summary>
    /// Always-on world-space label overlay. Where <see cref="FuseSceneryDebugOverlay"/>
    /// and <see cref="FuseTrackDebugOverlay"/> show a detailed tooltip for
    /// the single object under the cursor, this overlay paints a small
    /// color-coded label on every visible entity at once — scenery, scene
    /// clones, industries, and (optionally) track nodes / segments. The
    /// intent is a "what is everything I'm looking at" debug pass, useful
    /// for spotting unexpected mod content, missing assets, or stale FUSE
    /// claims at a glance.
    ///
    /// Performance notes:
    /// <list type="bullet">
    ///   <item>Entity enumeration runs on a refresh interval, not every
    ///         frame. Map scenes have ~hundreds of scenery instances and
    ///         can have ~thousands of track nodes; re-enumerating those
    ///         on every <c>OnGUI</c> would tank framerate.</item>
    ///   <item>Each enumerated entity is reduced to a tiny <c>Label</c>
    ///         record (world position, text, color) and stored in a flat
    ///         array. <c>OnGUI</c> walks that array, projects to screen,
    ///         and renders.</item>
    ///   <item>Track nodes/segments default to off precisely because their
    ///         density paves the screen. Authors who want them turn them
    ///         on individually from the Debug Overlays panel.</item>
    ///   <item>A per-frame cap (<c>MaxLabelsPerFrame</c>) is applied so a
    ///         dense yard doesn't catastrophically slow the GUI pass; the
    ///         closest labels win on tie.</item>
    /// </list>
    /// </summary>
    internal sealed class FuseWorldLabelsOverlay : MonoBehaviour
    {
        private const float RefreshInterval = 0.5f;
        private const int MaxLabelsPerFrame = 250;
        // The cull radius is measured from the camera transform, NOT from
        // the camera target. Railroader's RTS camera frequently sits 500–
        // 1500 m above the action, so a tight horizontal-feeling radius
        // like 600 m silently drops anchor-level industries (Whittier
        // sawmill, Andrews papermill, etc.) when the camera is zoomed
        // out for an overview. 2000 m comfortably covers any reasonable
        // RTS angle without paving the screen with far-away labels.
        private const float MaxDistanceMeters = 2000f;
        private const float SegmentMidpointMaxLengthMeters = 800f; // skip giant decorative segments

        // Color per kind. Hand-picked to be distinguishable from each
        // other AND legible against typical map terrain. RGB values are
        // intentionally bright (the labels render over a semi-opaque
        // black pill so saturated foregrounds read cleanly).
        private static readonly Color FuseSceneryColor = new Color(1.00f, 0.55f, 0.16f, 1f);     // orange
        private static readonly Color VanillaSceneryColor = new Color(0.78f, 0.78f, 0.78f, 1f); // light gray
        private static readonly Color SceneCloneColor = new Color(0.16f, 0.78f, 1.00f, 1f);     // cyan
        private static readonly Color IndustryColor = new Color(1.00f, 0.36f, 0.82f, 1f);       // pink
        private static readonly Color TrackNodeColor = new Color(0.49f, 0.88f, 0.55f, 1f);      // green
        private static readonly Color TrackSegmentColor = new Color(1.00f, 0.88f, 0.25f, 1f);   // yellow

        private static GameObject _host;

        private readonly List<Label> _labels = new List<Label>(256);
        private GUIStyle _labelStyle;
        private Texture2D _backgroundTexture;
        private float _nextRefreshAt;

        public static void Ensure()
        {
            if (_host != null)
            {
                return;
            }

            _host = new GameObject("FUSE World Labels Overlay");
            DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideAndDontSave;
            _host.AddComponent<FuseWorldLabelsOverlay>();
            FuseLog.Info("FUSE world-labels overlay initialized.");
        }

        public static void Shutdown()
        {
            if (_host != null)
            {
                Destroy(_host);
                _host = null;
            }
        }

        private void Update()
        {
            if (!FuseSettings.ShowWorldLabelsOverlay)
            {
                if (_labels.Count > 0)
                {
                    _labels.Clear();
                }

                return;
            }

            if (Time.unscaledTime < _nextRefreshAt)
            {
                return;
            }

            _nextRefreshAt = Time.unscaledTime + RefreshInterval;
            RebuildLabels();
        }

        private void OnGUI()
        {
            if (!FuseSettings.ShowWorldLabelsOverlay)
            {
                return;
            }

            var evt = Event.current;
            if (evt == null || evt.type != EventType.Repaint)
            {
                return;
            }

            EnsureGuiStyles();

            // Always paint the legend whenever the overlay is on, even
            // before the first refresh has populated _labels. The legend
            // doubles as a "yes, the overlay is active" confirmation
            // signal so users aren't left wondering whether the toggle
            // took effect on an empty scene.
            DrawLegend();

            if (_labels.Count == 0)
            {
                return;
            }

            var camera = Camera.main ?? Camera.current;
            if (camera == null)
            {
                return;
            }

            var screenHeight = Screen.height;
            var screenWidth = Screen.width;
            var painted = 0;
            for (var index = 0; index < _labels.Count && painted < MaxLabelsPerFrame; index++)
            {
                var label = _labels[index];
                var screen = camera.WorldToScreenPoint(label.WorldPosition);
                if (screen.z <= 0f)
                {
                    continue;
                }

                if (screen.x < 0f || screen.x > screenWidth || screen.y < 0f || screen.y > screenHeight)
                {
                    continue;
                }

                // Unity GUI Y is top-down; world-to-screen Y is bottom-up.
                var guiX = screen.x;
                var guiY = screenHeight - screen.y;

                _labelStyle.normal.textColor = label.Color;
                var content = new GUIContent(label.Text);
                var size = _labelStyle.CalcSize(content);

                // Pill background then text. Slight upward bias so the
                // label sits "above" the entity centroid rather than
                // burying it.
                var rect = new Rect(
                    guiX - size.x * 0.5f,
                    guiY - size.y - 6f,
                    size.x + 8f,
                    size.y + 4f);
                GUI.DrawTexture(rect, _backgroundTexture);
                GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, rect.width - 8f, rect.height - 4f), content, _labelStyle);
                painted++;
            }
        }

        private void OnDestroy()
        {
            if (_backgroundTexture != null)
            {
                Destroy(_backgroundTexture);
                _backgroundTexture = null;
            }
        }

        private void RebuildLabels()
        {
            _labels.Clear();
            var camera = Camera.main ?? Camera.current;
            if (camera == null)
            {
                return;
            }

            var camPos = camera.transform.position;
            var maxDistSq = MaxDistanceMeters * MaxDistanceMeters;

            try
            {
                if (FuseSettings.WorldLabelsShowScenery)
                {
                    CollectScenery(camPos, maxDistSq);
                }

                if (FuseSettings.WorldLabelsShowSceneClones)
                {
                    CollectSceneClones(camPos, maxDistSq);
                }

                if (FuseSettings.WorldLabelsShowIndustries)
                {
                    CollectIndustries(camPos, maxDistSq);
                }

                if (FuseSettings.WorldLabelsShowTrackNodes)
                {
                    CollectTrackNodes(camPos, maxDistSq);
                }

                if (FuseSettings.WorldLabelsShowTrackSegments)
                {
                    CollectTrackSegments(camPos, maxDistSq);
                }
            }
            catch (Exception ex)
            {
                _labels.Clear();
                FuseLog.Warning($"FUSE world-labels overlay refresh failed: {ex.GetBaseException().Message}");
                return;
            }

            // Sort by distance so when the per-frame cap kicks in, the
            // nearest labels survive — that's what the player can read
            // anyway, and it keeps far-away dense clusters from
            // crowding out the nearby ones.
            _labels.Sort(CompareByDistance);
        }

        private void CollectScenery(Vector3 camPos, float maxDistSq)
        {
            foreach (var scenery in SceneryAPI.GetAllScenery())
            {
                if (scenery == null)
                {
                    continue;
                }

                var pos = ResolveAnchor(scenery.gameObject, scenery.transform);
                if ((pos - camPos).sqrMagnitude > maxDistSq)
                {
                    continue;
                }

                var isFuse = FuseSceneryRuntimeIndex.Instance.TryGetValue(scenery.name, out _);
                var color = isFuse ? FuseSceneryColor : VanillaSceneryColor;
                var prefix = isFuse ? "F" : "V";
                _labels.Add(new Label
                {
                    Text = $"[{prefix}] {SafeId(scenery.name)}",
                    WorldPosition = pos,
                    Color = color,
                    DistanceSq = (pos - camPos).sqrMagnitude
                });
            }
        }

        private void CollectSceneClones(Vector3 camPos, float maxDistSq)
        {
            foreach (var clone in SceneCloneAPI.GetAllSceneClones())
            {
                if (clone == null)
                {
                    continue;
                }

                var pos = ResolveAnchor(clone, clone.transform);
                if ((pos - camPos).sqrMagnitude > maxDistSq)
                {
                    continue;
                }

                var id = clone.name;
                if (SceneCloneAPI.TryGetSceneCloneInfo(clone, out var cloneId, out _) && !string.IsNullOrWhiteSpace(cloneId))
                {
                    id = cloneId;
                }

                _labels.Add(new Label
                {
                    Text = $"[C] {SafeId(id)}",
                    WorldPosition = pos,
                    Color = SceneCloneColor,
                    DistanceSq = (pos - camPos).sqrMagnitude
                });
            }
        }

        private void CollectIndustries(Vector3 camPos, float maxDistSq)
        {
            foreach (var industry in IndustryAPI.GetAllIndustries())
            {
                if (industry == null)
                {
                    continue;
                }

                var pos = ResolveAnchor(industry.gameObject, industry.transform);
                if ((pos - camPos).sqrMagnitude > maxDistSq)
                {
                    continue;
                }

                _labels.Add(new Label
                {
                    Text = $"[I] {SafeId(industry.identifier ?? industry.name)}",
                    WorldPosition = pos,
                    Color = IndustryColor,
                    DistanceSq = (pos - camPos).sqrMagnitude
                });
            }
        }

        /// <summary>
        /// Picks the best label position for an entity whose root
        /// transform may not be where the visible mesh actually sits.
        /// Many Railroader industries hang under an "Industries" container
        /// whose own transform is at world (0, 0, 0); using
        /// <c>industry.transform.position</c> directly would render the
        /// "Whittier Sawmill" label at the map origin, miles from the
        /// actual sawmill. The label belongs on what the player sees, so
        /// we walk the child renderers, aggregate their world-space
        /// bounds, and return the center of that AABB. If there are no
        /// renderers at all we fall back to the transform — better an
        /// origin-pinned label than no label.
        /// </summary>
        private static Vector3 ResolveAnchor(GameObject root, Transform fallback)
        {
            if (root == null)
            {
                return fallback != null ? fallback.position : Vector3.zero;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(false);
            if (renderers == null || renderers.Length == 0)
            {
                return fallback != null ? fallback.position : root.transform.position;
            }

            var found = false;
            var bounds = default(Bounds);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!found)
            {
                return fallback != null ? fallback.position : root.transform.position;
            }

            // Push the anchor up to the top of the AABB so the floating
            // label sits above the building rather than buried inside it.
            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }

        /// <summary>
        /// Paints a compact color key in the top-left corner of the screen
        /// listing every label kind the user currently has enabled. The
        /// row order matches the in-world prefix tag — [F], [V], [C],
        /// [I], [N], [S] — so users can map screen-space colors back to
        /// what the entity is. Disabled kinds are skipped so the legend
        /// only shows colors the user will actually see in this session.
        /// </summary>
        private void DrawLegend()
        {
            // Build the entry list first so we can size the panel
            // exactly. Order is intentional: scenery -> scene clones ->
            // industries -> track nodes -> track segments, matching the
            // tag prefixes in the label text.
            var entries = new List<KeyValuePair<string, Color>>(6);
            if (FuseSettings.WorldLabelsShowScenery)
            {
                entries.Add(new KeyValuePair<string, Color>("[F] FUSE Scenery", FuseSceneryColor));
                entries.Add(new KeyValuePair<string, Color>("[V] Vanilla Scenery", VanillaSceneryColor));
            }

            if (FuseSettings.WorldLabelsShowSceneClones)
            {
                entries.Add(new KeyValuePair<string, Color>("[C] Scene Clone", SceneCloneColor));
            }

            if (FuseSettings.WorldLabelsShowIndustries)
            {
                entries.Add(new KeyValuePair<string, Color>("[I] Industry", IndustryColor));
            }

            if (FuseSettings.WorldLabelsShowTrackNodes)
            {
                entries.Add(new KeyValuePair<string, Color>("[N] Track Node", TrackNodeColor));
            }

            if (FuseSettings.WorldLabelsShowTrackSegments)
            {
                entries.Add(new KeyValuePair<string, Color>("[S] Track Segment", TrackSegmentColor));
            }

            if (entries.Count == 0)
            {
                // User toggled the master switch on but disabled every
                // per-kind sub-toggle. Show a hint rather than an empty
                // bordered pill so they know to enable a kind.
                entries.Add(new KeyValuePair<string, Color>("World Labels: no kinds enabled", Color.white));
            }

            // Layout constants. Keep them local so changes to the legend
            // don't ripple into the main label rendering above.
            const float legendX = 12f;
            const float legendY = 12f;
            const float rowHeight = 18f;
            const float swatchSize = 12f;
            const float padding = 8f;
            const float swatchTextGap = 6f;
            const float titleHeight = 18f;

            // Width is sized to the widest entry so single-line entries
            // don't truncate. Title row sits at the top.
            var titleContent = new GUIContent("World Labels");
            var titleWidth = _labelStyle.CalcSize(titleContent).x;
            var contentWidth = titleWidth;
            for (var index = 0; index < entries.Count; index++)
            {
                var rowContent = new GUIContent(entries[index].Key);
                var w = _labelStyle.CalcSize(rowContent).x + swatchSize + swatchTextGap;
                if (w > contentWidth)
                {
                    contentWidth = w;
                }
            }

            var panelWidth = contentWidth + padding * 2f;
            var panelHeight = titleHeight + entries.Count * rowHeight + padding * 2f;
            var panelRect = new Rect(legendX, legendY, panelWidth, panelHeight);

            GUI.DrawTexture(panelRect, _backgroundTexture);

            // Title row in white, slightly bolder via the existing label
            // style. Rows below are colored swatches + colored text.
            _labelStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(panelRect.x + padding, panelRect.y + padding * 0.5f, contentWidth, titleHeight),
                titleContent, _labelStyle);

            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var rowY = panelRect.y + padding + titleHeight + index * rowHeight;
                var swatchRect = new Rect(panelRect.x + padding, rowY + (rowHeight - swatchSize) * 0.5f,
                    swatchSize, swatchSize);

                // Draw the color swatch. Reuse the background texture for
                // the pixel surface (it's already a 2x2 white-ish texture)
                // and tint it with GUI.color so we don't allocate per-row.
                var previousGuiColor = GUI.color;
                GUI.color = entry.Value;
                GUI.DrawTexture(swatchRect, Texture2D.whiteTexture);
                GUI.color = previousGuiColor;

                _labelStyle.normal.textColor = entry.Value;
                var textRect = new Rect(
                    swatchRect.xMax + swatchTextGap,
                    rowY,
                    contentWidth - swatchSize - swatchTextGap,
                    rowHeight);
                GUI.Label(textRect, entry.Key, _labelStyle);
            }
        }

        private void CollectTrackNodes(Vector3 camPos, float maxDistSq)
        {
            foreach (var node in TrackAPI.GetAllNodes())
            {
                if (node == null)
                {
                    continue;
                }

                var pos = node.transform.position;
                if ((pos - camPos).sqrMagnitude > maxDistSq)
                {
                    continue;
                }

                _labels.Add(new Label
                {
                    Text = $"[N] {SafeId(node.id ?? node.name)}",
                    WorldPosition = pos,
                    Color = TrackNodeColor,
                    DistanceSq = (pos - camPos).sqrMagnitude
                });
            }
        }

        private void CollectTrackSegments(Vector3 camPos, float maxDistSq)
        {
            foreach (var segment in TrackAPI.GetAllSegments())
            {
                if (segment == null || segment.a == null || segment.b == null)
                {
                    continue;
                }

                var endA = segment.a.transform.position;
                var endB = segment.b.transform.position;
                var midpoint = (endA + endB) * 0.5f;
                if ((midpoint - camPos).sqrMagnitude > maxDistSq)
                {
                    continue;
                }

                // Decorative giant spans (e.g. a single span representing a
                // whole branch line) would put their midpoint nowhere
                // useful; skip them rather than drop a misleading dot.
                if ((endA - endB).sqrMagnitude > SegmentMidpointMaxLengthMeters * SegmentMidpointMaxLengthMeters)
                {
                    continue;
                }

                _labels.Add(new Label
                {
                    Text = $"[S] {SafeId(segment.id ?? segment.name)}",
                    WorldPosition = midpoint,
                    Color = TrackSegmentColor,
                    DistanceSq = (midpoint - camPos).sqrMagnitude
                });
            }
        }

        private static int CompareByDistance(Label left, Label right)
        {
            return left.DistanceSq.CompareTo(right.DistanceSq);
        }

        private static string SafeId(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? "<unnamed>" : id;
        }

        private void EnsureGuiStyles()
        {
            if (_labelStyle != null && _backgroundTexture != null)
            {
                return;
            }

            if (_backgroundTexture == null)
            {
                _backgroundTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                var pixels = new Color[4];
                for (var index = 0; index < pixels.Length; index++)
                {
                    pixels[index] = new Color(0f, 0f, 0f, 0.70f);
                }

                _backgroundTexture.SetPixels(pixels);
                _backgroundTexture.Apply();
            }

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 11,
                    richText = false,
                    wordWrap = false
                };
            }
        }

        private struct Label
        {
            public string Text;
            public Vector3 WorldPosition;
            public Color Color;
            public float DistanceSq;
        }
    }
}
