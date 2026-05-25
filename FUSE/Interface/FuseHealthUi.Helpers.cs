using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FUSE.Runtime.API;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Runtime.Lifecycle;
using FUSE.Loading;
using FUSE.Authoring.Migrations;
using FUSE.Runtime.Registry;
using Model;
using Model.Ops;
using Newtonsoft.Json.Linq;
using Railloader;
using TMPro;
using Track;
using UI;
using UI.Builder;
using UI.Common;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FUSE.Interface
{
    internal sealed partial class FuseHealthUi : MonoBehaviour
    {

        private static void AddReadinessRow(UIPanelBuilder builder, string label, bool ok, string okText, string problemText)
        {
            AddValueField(builder, label, ok ? "OK | " + okText : "Review | " + problemText);
        }

        private static void AddValueField(UIPanelBuilder builder, string label, string value)
        {
            builder.AddField(label, value).Height(26f);
        }

        private static string BlankAs(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static int SafeCount(Func<int> count)
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

        private static bool IsLoadedSceneObject(GameObject gameObject)
        {
            return gameObject != null && gameObject.scene.IsValid() && gameObject.scene.isLoaded;
        }

        private static string GetGameObjectPath(GameObject gameObject)
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

        private static bool MatchesSearch(string value, string term)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.IsNullOrWhiteSpace(term) &&
                   value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string InsertBreakHints(string value)
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

        private static void AddWrappedField(UIPanelBuilder builder, string label, string value, float height)
        {
            builder.AddField(label, AddWrappedLabel(builder, value, height)).Height(height);
        }

        private static RectTransform AddWrappedLabel(UIPanelBuilder builder, string value, float height)
        {
            return builder.AddLabel(value ?? string.Empty, text =>
            {
                text.enableWordWrapping = true;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.alignment = TextAlignmentOptions.Left;
            }).Height(height);
        }

        private static void AddCountField(UIPanelBuilder builder, string label, JObject counts, string key)
        {
            builder.AddField(label, () => ReadInt(counts[key]).ToString(), 0).Height(24f);
        }

        private static void AddSettingToggle(UIPanelBuilder builder, string label, string value, string buttonText, Action toggle)
        {
            builder.HStack(row =>
            {
                row.AddLabel(label, text =>
                {
                    text.enableWordWrapping = false;
                    text.overflowMode = TextOverflowModes.Ellipsis;
                    text.alignment = TextAlignmentOptions.Right;
                });
                row.AddLabel(value, text =>
                {
                    text.enableWordWrapping = false;
                    text.overflowMode = TextOverflowModes.Ellipsis;
                    text.alignment = TextAlignmentOptions.Left;
                });
                row.AddButtonCompact(buttonText, toggle);
            }, 8f).Height(30f);
        }

        private static int AddProblemSummary(UIPanelBuilder builder, JObject report, string parentKey, string key, string label, bool showZero)
        {
            JToken token = string.IsNullOrWhiteSpace(parentKey) ? report[key] : report[parentKey]?[key];
            var count = token is JArray array ? array.Count : 0;
            if (count == 0 && !showZero)
            {
                return 0;
            }

            builder.AddField(label, () => count == 0 ? "0" : count + " - see /fuse.report", 0).Height(24f);
            return 1;
        }

        private static bool HasReportProblems(JObject report)
        {
            if (report == null)
            {
                return false;
            }

            if (ReadBool(report["hasProblems"], false))
            {
                return true;
            }

            return CountArray(report["packages"]?["faults"]) > 0 ||
                   CountArray(report["conflicts"]) > 0 ||
                   CountArray(report["unknownSceneryAssets"]) > 0 ||
                   CountArray(report["graphPostBindIssues"]) > 0 ||
                   CountArray(report["progressionTransferSkips"]) > 0 ||
                   CountArray(report["notices"]) > 0;
        }

        private static int CountArray(JToken token)
        {
            return token is JArray array ? array.Count : 0;
        }

        private static long ReadProfilerMetric(Func<long> metric)
        {
            try
            {
                return metric();
            }
            catch
            {
                return 0L;
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0L)
            {
                return "n/a";
            }

            return (bytes / 1048576f).ToString("0.0") + " MB";
        }

        private void RunAction(string actionName, Func<string> action)
        {
            try
            {
                _lastAction = action();
                RebuildWindow();
            }
            catch (Exception ex)
            {
                _lastAction = $"FUSE {actionName} failed: {ex.GetBaseException().Message}";
                FuseLog.Exception($"FUSE health page action failed operation='{actionName}'", ex);
                RebuildWindow();
            }
        }

        private static JObject LoadReportJson()
        {
            try
            {
                return JObject.Parse(FuseLoadReport.GetLastJsonReport());
            }
            catch
            {
                return new JObject
                {
                    ["summary"] = FuseLoadReport.LastSummary,
                    ["hasProblems"] = false,
                    ["counts"] = new JObject()
                };
            }
        }

        private static Sprite LoadIconSprite()
        {
            if (_iconSprite != null)
            {
                return _iconSprite;
            }

            try
            {
                var path = Path.Combine(FusePlugin.ModEntry?.Path ?? string.Empty, "assets", "fuse_icon.png");
                if (!File.Exists(path))
                {
                    FuseLog.Warning("FUSE health icon was not found; using an empty base-game image component.");
                    return null;
                }

                var bytes = File.ReadAllBytes(path);
                var texture = new Texture2D(36, 36, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes))
                {
                    Destroy(texture);
                    FuseLog.Warning("FUSE health icon could not be decoded.");
                    return null;
                }

                _iconSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                return _iconSprite;
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE health icon load failed: {ex.GetBaseException().Message}");
                return null;
            }
        }

        private static string ReadVersion()
        {
            try
            {
                var infoPath = Path.Combine(FusePlugin.ModEntry?.Path ?? string.Empty, "Info.json");
                if (!File.Exists(infoPath))
                {
                    return "unknown";
                }

                var info = JObject.Parse(File.ReadAllText(infoPath));
                return ReadString(info["Version"], "unknown");
            }
            catch
            {
                return "unknown";
            }
        }

        private static string ReadString(JToken token, string fallback)
        {
            var value = token == null ? string.Empty : token.ToString();
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static int ReadInt(JToken token)
        {
            return token != null && int.TryParse(token.ToString(), out var value) ? value : 0;
        }

        private static bool ReadBool(JToken token, bool fallback)
        {
            return token != null && bool.TryParse(token.ToString(), out var value) ? value : fallback;
        }
    }
}
