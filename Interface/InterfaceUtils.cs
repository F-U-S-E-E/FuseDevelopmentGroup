using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UI.Builder;
using UnityEngine;

namespace FUSE.Interface
{
    internal static class InterfaceUtils
    {
        public static void AddValueField(UIPanelBuilder builder, string label, string value)
        {
            builder.AddField(label, value).Height(26f);
        }

        public static void AddWrappedField(UIPanelBuilder builder, string label, string value, float height)
        {
            builder.AddField(label, AddWrappedLabel(builder, value, height)).Height(height);
        }

        public static RectTransform AddWrappedLabel(UIPanelBuilder builder, string value, float height)
        {
            return builder.AddLabel(value ?? string.Empty, text =>
            {
                text.enableWordWrapping = true;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.alignment = TextAlignmentOptions.Left;
            }).Height(height);
        }

        public static int SafeCount(Func<int> count)
        {
            try
            {
                return Math.Max(0, count());
            }
            catch
            {
                return 0;
            }
        }

        public static string BlankAs(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        public static string GetGameObjectPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return "<null>";
            }

            try
            {
                var names = new Stack<string>();
                var current = gameObject.transform;
                while (current != null)
                {
                    names.Push(BlankAs(current.name, "(unnamed)"));
                    current = current.parent;
                }

                var sceneName = gameObject.scene.IsValid() ? BlankAs(gameObject.scene.name, "(scene)") : "(no scene)";
                return sceneName + "/" + string.Join("/", names.ToArray());
            }
            catch
            {
                return gameObject.name ?? "<unnamed>";
            }
        }

        public static string FormatRuntimeObject(object runtime)
        {
            if (runtime == null)
            {
                return "<null>";
            }

            if (runtime is GameObject gameObject)
            {
                return "GameObject " + GetGameObjectPath(gameObject);
            }

            if (runtime is Component component)
            {
                return component.GetType().Name + " " + GetGameObjectPath(component.gameObject);
            }

            return runtime.GetType().Name;
        }

        public static string FormatComponentList(GameObject gameObject)
        {
            try
            {
                if (gameObject == null)
                {
                    return "none";
                }

                var names = gameObject.GetComponents<Component>()
                    .Where(component => component != null)
                    .Select(component => component.GetType().Name)
                    .Take(6)
                    .ToArray();
                return names.Length == 0 ? "none" : string.Join(",", names);
            }
            catch
            {
                return "unavailable";
            }
        }

        public static string FormatChildPreview(GameObject gameObject)
        {
            try
            {
                if (gameObject == null || gameObject.transform.childCount == 0)
                {
                    return "none";
                }

                var names = new List<string>();
                for (var index = 0; index < gameObject.transform.childCount && names.Count < 8; index++)
                {
                    var child = gameObject.transform.GetChild(index);
                    if (child != null)
                    {
                        names.Add(child.name);
                    }
                }

                var suffix = gameObject.transform.childCount > names.Count
                    ? " +" + (gameObject.transform.childCount - names.Count)
                    : string.Empty;
                return names.Count == 0 ? "none" : string.Join(", ", names.ToArray()) + suffix;
            }
            catch
            {
                return "unavailable";
            }
        }

        public static string FormatVector3(Vector3 value)
        {
            return value.x.ToString("0.###") + ", " + value.y.ToString("0.###") + ", " + value.z.ToString("0.###");
        }

        public static bool MatchesSearch(string value, string term)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.IsNullOrWhiteSpace(term) &&
                   value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string InsertBreakHints(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .Replace("_", "_ ")
                .Replace("-", "- ")
                .Replace(".", ". ")
                .Replace("/", " / ")
                .Replace("\\", " \\ ")
                .Replace("|", " | ");
        }
    }
}
