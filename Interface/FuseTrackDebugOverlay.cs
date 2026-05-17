using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FUSE.API;
using FUSE.Infrastructure;
using Model.Ops;
using Track;
using UnityEngine;

namespace FUSE.Interface
{
    internal sealed class FuseTrackDebugOverlay : MonoBehaviour
    {
        private const float PickScreenRadiusPixels = 12f;
        private const float RefreshInterval = 0.08f;
        private const int MaxSpanPathsToShow = 8;
        private const int MaxIndustriesToShow = 12;

        private static GameObject _host;

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private Texture2D _backgroundTexture;
        private float _nextRefreshAt;
        private string _cachedText;
        private bool _hasHover;
        private Vector2 _lastMouseGui;

        public static void Ensure()
        {
            if (_host != null)
            {
                return;
            }

            _host = new GameObject("FUSE Track Debug Overlay");
            DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideAndDontSave;
            _host.AddComponent<FuseTrackDebugOverlay>();
            FuseLog.Info("FUSE track debug overlay initialized.");
        }

        public static void Shutdown()
        {
            if (_host != null)
            {
                Destroy(_host);
                _host = null;
            }
        }

        private void OnGUI()
        {
            if (!FuseSettings.ShowTrackDebugOverlay)
            {
                _hasHover = false;
                _cachedText = null;
                return;
            }

            var evt = Event.current;
            if (evt == null)
            {
                return;
            }

            if (evt.type == EventType.MouseMove || evt.type == EventType.Layout || evt.type == EventType.Repaint)
            {
                _lastMouseGui = evt.mousePosition;
            }

            if (evt.type == EventType.Layout)
            {
                MaybeRefresh();
            }

            if (evt.type != EventType.Repaint || !_hasHover || string.IsNullOrEmpty(_cachedText))
            {
                return;
            }

            EnsureGuiStyles();

            var screenX = _lastMouseGui.x + 18f;
            var screenY = _lastMouseGui.y + 18f;

            var content = new GUIContent(_cachedText);
            var size = _labelStyle.CalcSize(content);
            var width = Mathf.Min(Mathf.Max(280f, size.x + 16f), Screen.width - 32f);
            var height = Mathf.Min(_labelStyle.CalcHeight(content, width - 16f) + 16f, Screen.height - 32f);

            if (screenX + width > Screen.width - 8f)
            {
                screenX = Mathf.Max(8f, Screen.width - width - 8f);
            }

            if (screenY + height > Screen.height - 8f)
            {
                screenY = Mathf.Max(8f, Screen.height - height - 8f);
            }

            var boxRect = new Rect(screenX, screenY, width, height);
            GUI.Box(boxRect, GUIContent.none, _boxStyle);
            GUI.Label(new Rect(screenX + 8f, screenY + 8f, width - 16f, height - 16f), content, _labelStyle);
        }

        private void OnDestroy()
        {
            if (_backgroundTexture != null)
            {
                Destroy(_backgroundTexture);
                _backgroundTexture = null;
            }
        }

        private void MaybeRefresh()
        {
            if (Time.unscaledTime < _nextRefreshAt)
            {
                return;
            }

            _nextRefreshAt = Time.unscaledTime + RefreshInterval;

            try
            {
                var mouseScreen = GuiToScreen(_lastMouseGui);
                var segment = FindSegmentNearCursor(mouseScreen);
                if (segment == null)
                {
                    _hasHover = false;
                    _cachedText = null;
                    return;
                }

                _cachedText = BuildSegmentReport(segment);
                _hasHover = !string.IsNullOrEmpty(_cachedText);
            }
            catch (Exception ex)
            {
                _hasHover = false;
                _cachedText = null;
                FuseLog.Warning($"FUSE track debug overlay refresh failed: {ex.GetBaseException().Message}");
            }
        }

        private static Vector2 GuiToScreen(Vector2 guiPosition)
        {
            return new Vector2(guiPosition.x, Screen.height - guiPosition.y);
        }

        private static TrackSegment FindSegmentNearCursor(Vector2 mouseScreen)
        {
            var camera = Camera.main ?? Camera.current;
            if (camera == null)
            {
                return null;
            }

            var graph = Graph.Shared;
            if (graph == null)
            {
                return null;
            }

            if (mouseScreen.x < 0f || mouseScreen.y < 0f ||
                mouseScreen.x > Screen.width || mouseScreen.y > Screen.height)
            {
                return null;
            }

            TrackSegment best = null;
            var bestDistance = PickScreenRadiusPixels;
            var bestDepth = float.MaxValue;

            foreach (var segment in graph.Segments)
            {
                if (segment == null || segment.a == null || segment.b == null)
                {
                    continue;
                }

                var worldA = segment.a.transform.position;
                var worldB = segment.b.transform.position;
                var screenA = camera.WorldToScreenPoint(worldA);
                var screenB = camera.WorldToScreenPoint(worldB);
                if (screenA.z <= 0f && screenB.z <= 0f)
                {
                    continue;
                }

                var distance = DistanceFromPointToSegment2D(
                    mouseScreen,
                    new Vector2(screenA.x, screenA.y),
                    new Vector2(screenB.x, screenB.y));
                if (distance > bestDistance)
                {
                    continue;
                }

                var depth = 0.5f * (Mathf.Max(0f, screenA.z) + Mathf.Max(0f, screenB.z));
                if (distance < bestDistance - 0.5f || depth < bestDepth)
                {
                    bestDistance = distance;
                    bestDepth = depth;
                    best = segment;
                }
            }

            return best;
        }

        private static float DistanceFromPointToSegment2D(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var lengthSquared = ab.sqrMagnitude;
            if (lengthSquared <= 0.0001f)
            {
                return Vector2.Distance(point, a);
            }

            var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
            var projection = a + ab * t;
            return Vector2.Distance(point, projection);
        }

        private static string BuildSegmentReport(TrackSegment segment)
        {
            var builder = new StringBuilder();
            builder.AppendLine("<b>Track Segment</b>");
            builder.Append("id: ").AppendLine(SafeId(segment.id));
            builder.Append("nodes: ")
                .Append(SafeId(segment.a != null ? segment.a.id : null))
                .Append("  ->  ")
                .AppendLine(SafeId(segment.b != null ? segment.b.id : null));

            string style;
            try
            {
                style = segment.style.ToString();
            }
            catch
            {
                style = "?";
            }

            string trackClass;
            try
            {
                trackClass = segment.trackClass.ToString();
            }
            catch
            {
                trackClass = "?";
            }

            builder.Append("style: ").Append(style)
                .Append("  class: ").Append(trackClass)
                .Append("  speedLimit: ").Append(segment.speedLimit)
                .Append("  priority: ").AppendLine(segment.priority.ToString());

            if (!string.IsNullOrWhiteSpace(segment.groupId))
            {
                builder.Append("group: ").AppendLine(segment.groupId);
            }

            float length;
            try
            {
                length = segment.GetLength();
            }
            catch
            {
                length = 0f;
            }

            if (length > 0f)
            {
                builder.Append("length: ").Append(length.ToString("0.0")).AppendLine(" m");
            }

            AppendNodeBlock(builder, "node A", segment.a);
            AppendNodeBlock(builder, "node B", segment.b);

            AppendIndustryInfluences(builder, segment);

            if (FuseSettings.ShowTrackDebugSpanPaths)
            {
                AppendSpanPaths(builder, segment);
            }

            return builder.ToString().TrimEnd();
        }

        private static void AppendNodeBlock(StringBuilder builder, string label, TrackNode node)
        {
            if (node == null)
            {
                return;
            }

            builder.Append(label).Append(": ").Append(SafeId(node.id));
            if (node.flipSwitchStand)
            {
                builder.Append("  (flipSwitchStand)");
            }

            builder.AppendLine();
        }

        private static void AppendIndustryInfluences(StringBuilder builder, TrackSegment segment)
        {
            var hits = CollectIndustryInfluences(segment);
            builder.AppendLine();
            builder.Append("<b>Industries influencing</b> (").Append(hits.Count).AppendLine(")");
            if (hits.Count == 0)
            {
                builder.AppendLine("  none");
                return;
            }

            foreach (var hit in hits.Take(MaxIndustriesToShow))
            {
                builder.Append("  - ")
                    .Append(hit.IndustryDisplay)
                    .Append("  [")
                    .Append(hit.ComponentKind)
                    .Append("]")
                    .Append("  via span ")
                    .AppendLine(SafeId(hit.SpanId));
            }

            if (hits.Count > MaxIndustriesToShow)
            {
                builder.Append("  + ").Append(hits.Count - MaxIndustriesToShow).AppendLine(" more");
            }
        }

        private static void AppendSpanPaths(StringBuilder builder, TrackSegment segment)
        {
            List<TrackSpan> spans;
            try
            {
                spans = TrackAPI.GetAllSpans()
                    .Where(span => SpanTouchesSegment(span, segment))
                    .ToList();
            }
            catch (Exception ex)
            {
                builder.AppendLine();
                builder.Append("<b>Spans</b>: error - ").AppendLine(ex.GetBaseException().Message);
                return;
            }

            builder.AppendLine();
            builder.Append("<b>Spans on this segment</b> (").Append(spans.Count).AppendLine(")");
            if (spans.Count == 0)
            {
                builder.AppendLine("  none");
                return;
            }

            var index = 0;
            foreach (var span in spans)
            {
                if (index++ >= MaxSpanPathsToShow)
                {
                    builder.Append("  + ").Append(spans.Count - MaxSpanPathsToShow).AppendLine(" more");
                    break;
                }

                builder.Append("  - ").Append(SafeId(span.id));
                float spanLength;
                try
                {
                    spanLength = span.Length;
                }
                catch
                {
                    spanLength = 0f;
                }

                if (spanLength > 0f)
                {
                    builder.Append("  (").Append(spanLength.ToString("0.0")).Append(" m)");
                }

                builder.AppendLine();
                builder.Append("      ").AppendLine(DescribeSpanEndpoint("upper", span.upper));
                builder.Append("      ").AppendLine(DescribeSpanEndpoint("lower", span.lower));

                var pathIds = CollectSpanSegmentPath(span);
                if (pathIds.Count > 0)
                {
                    builder.Append("      path: ").AppendLine(string.Join(" -> ", pathIds.ToArray()));
                }
            }
        }

        private static IReadOnlyList<string> CollectSpanSegmentPath(TrackSpan span)
        {
            if (span == null || !span.IsValid)
            {
                return Array.Empty<string>();
            }

            try
            {
                var points = span.GetPoints();
                if (points == null || points.Count == 0)
                {
                    return Array.Empty<string>();
                }
            }
            catch
            {
                return Array.Empty<string>();
            }

            var ids = new List<string>(4);
            var upperSegmentId = span.upper.HasValue && span.upper.Value.segment != null ? span.upper.Value.segment.id : null;
            var lowerSegmentId = span.lower.HasValue && span.lower.Value.segment != null ? span.lower.Value.segment.id : null;
            if (!string.IsNullOrWhiteSpace(upperSegmentId))
            {
                ids.Add(upperSegmentId);
            }

            if (!string.IsNullOrWhiteSpace(lowerSegmentId) &&
                !string.Equals(upperSegmentId, lowerSegmentId, StringComparison.OrdinalIgnoreCase))
            {
                ids.Add(lowerSegmentId);
            }

            return ids;
        }

        private static string DescribeSpanEndpoint(string label, Location? location)
        {
            if (!location.HasValue)
            {
                return label + ": <none>";
            }

            var value = location.Value;
            var segmentId = value.segment != null ? value.segment.id : "<null>";
            var end = value.EndIsA ? "A" : "B";
            return label + ": " + segmentId + " end=" + end + " distance=" + value.distance.ToString("0.0");
        }

        private static List<IndustryHit> CollectIndustryInfluences(TrackSegment segment)
        {
            var hits = new List<IndustryHit>();
            if (segment == null || string.IsNullOrWhiteSpace(segment.id))
            {
                return hits;
            }

            IEnumerable<Industry> industries;
            try
            {
                industries = IndustryAPI.GetAllIndustries();
            }
            catch
            {
                return hits;
            }

            foreach (var industry in industries)
            {
                if (industry == null)
                {
                    continue;
                }

                IndustryComponent[] components;
                try
                {
                    components = industry.GetComponentsInChildren<IndustryComponent>(true);
                }
                catch
                {
                    continue;
                }

                foreach (var component in components)
                {
                    if (component == null || component.trackSpans == null)
                    {
                        continue;
                    }

                    foreach (var span in component.trackSpans)
                    {
                        if (!SpanTouchesSegment(span, segment))
                        {
                            continue;
                        }

                        hits.Add(new IndustryHit(
                            DescribeIndustry(industry),
                            DescribeComponentKind(component),
                            span != null ? span.id : null));
                    }
                }
            }

            return hits;
        }

        private static bool SpanTouchesSegment(TrackSpan span, TrackSegment segment)
        {
            if (span == null || segment == null || string.IsNullOrWhiteSpace(segment.id))
            {
                return false;
            }

            var upper = span.upper;
            var lower = span.lower;
            if (upper.HasValue && upper.Value.segment != null &&
                string.Equals(upper.Value.segment.id, segment.id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (lower.HasValue && lower.Value.segment != null &&
                string.Equals(lower.Value.segment.id, segment.id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static string DescribeIndustry(Industry industry)
        {
            if (industry == null)
            {
                return "<null>";
            }

            var name = string.IsNullOrWhiteSpace(industry.name) ? industry.identifier : industry.name;
            return string.IsNullOrWhiteSpace(industry.identifier) ? name : name + " (" + industry.identifier + ")";
        }

        private static string DescribeComponentKind(IndustryComponent component)
        {
            if (component == null)
            {
                return "?";
            }

            var type = component.GetType().Name;
            var subId = component.subIdentifier;
            return string.IsNullOrWhiteSpace(subId) ? type : type + ":" + subId;
        }

        private static string SafeId(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? "<none>" : id;
        }

        private void EnsureGuiStyles()
        {
            if (_boxStyle != null && _labelStyle != null)
            {
                return;
            }

            if (_backgroundTexture == null)
            {
                _backgroundTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                var pixels = new Color[4];
                for (var index = 0; index < pixels.Length; index++)
                {
                    pixels[index] = new Color(0f, 0f, 0f, 0.78f);
                }

                _backgroundTexture.SetPixels(pixels);
                _backgroundTexture.Apply();
            }

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _backgroundTexture, textColor = Color.white },
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(8, 8, 8, 8)
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                fontSize = 12,
                normal = { textColor = Color.white }
            };
        }

        private readonly struct IndustryHit
        {
            public IndustryHit(string industryDisplay, string componentKind, string spanId)
            {
                IndustryDisplay = industryDisplay;
                ComponentKind = componentKind;
                SpanId = spanId;
            }

            public string IndustryDisplay { get; }

            public string ComponentKind { get; }

            public string SpanId { get; }
        }
    }
}
