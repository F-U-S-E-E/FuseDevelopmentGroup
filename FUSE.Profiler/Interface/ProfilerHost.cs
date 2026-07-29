using System;
using FUSE.Profiler.Engine;
using FUSE.Profiler.Infrastructure;
using FUSE.Profiler.Instrumentation;
using UnityEngine;
using UnityEngine.UI;

namespace FUSE.Profiler.Interface
{
    /// <summary>
    /// The always-on scene host: a hidden DontDestroyOnLoad GameObject whose
    /// MonoBehaviour drives the frame clock, the hotkey, the main-thread
    /// patch pump, the cleanup countdown, and the window's OnGUI. Created
    /// unconditionally at mod load so nothing the UI does can starve the
    /// runtime. Also owns an invisible uGUI raycast blocker matched to the
    /// window rect so profiler clicks don't fall through into the game
    /// world.
    /// </summary>
    internal static class ProfilerHost
    {
        /// <summary>
        /// Per-frame budget for applying queued Harmony patches. Large
        /// sweeps intentionally trade a few choppy frames for progress.
        /// </summary>
        private const double PatchBudgetMsPerFrame = 25.0;

        private static GameObject _host;

        internal static KeyCode ToggleKey = KeyCode.F11;

        internal static bool IsRunning => _host != null;

        internal static void EnsureStarted()
        {
            if (_host != null)
            {
                return;
            }

            try
            {
                MethodInstrumenter.MainThreadId = Environment.CurrentManagedThreadId;
                _host = new GameObject("FUSE.Profiler.Host");
                UnityEngine.Object.DontDestroyOnLoad(_host);
                _host.hideFlags = HideFlags.HideAndDontSave;
                _host.AddComponent<ProfilerHostRunner>();
                ProfilerLog.Info("FUSE.Profiler host initialized.");
            }
            catch (Exception ex)
            {
                ProfilerLog.Exception("FUSE.Profiler host creation failed", ex);
            }
        }

        internal static void Shutdown()
        {
            if (_host == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(_host);
            _host = null;
        }

        private sealed class ProfilerHostRunner : MonoBehaviour
        {
            private Canvas _blockerCanvas;
            private RectTransform _blockerRect;

            private void Update()
            {
                if (ToggleKey != KeyCode.None && Input.GetKeyDown(ToggleKey))
                {
                    ProfilerRuntime.ToggleWindow();
                }

                MethodInstrumenter.DrainPatchQueue(PatchBudgetMsPerFrame);
                ProfilerRuntime.TickCleanup(Time.unscaledDeltaTime);
                UpdateClickBlocker();
            }

            // Frame cycles close in LateUpdate: all Update-phase and coroutine
            // work of this frame is inside the closing bucket; render-side
            // work lands in the next one. Consistent bucketing beats perfect
            // frame alignment.
            private void LateUpdate()
            {
                ProfilerSession.FrameBoundary(Time.unscaledDeltaTime);
            }

            private void OnGUI()
            {
                if (ProfilerRuntime.WindowVisible)
                {
                    ProfilerWindow.Draw();
                }
            }

            private void OnDestroy()
            {
                if (_blockerCanvas != null)
                {
                    Destroy(_blockerCanvas.gameObject);
                    _blockerCanvas = null;
                    _blockerRect = null;
                }
            }

            /// <summary>
            /// Keep an invisible, raycast-blocking uGUI image congruent with
            /// the IMGUI window. IMGUI draws above uGUI but does not
            /// participate in EventSystem raycasts, so without this a click
            /// on a profiler row also hits whatever game control or world
            /// object sits underneath.
            /// </summary>
            private void UpdateClickBlocker()
            {
                var visible = ProfilerRuntime.WindowVisible;
                if (_blockerCanvas == null)
                {
                    if (!visible)
                    {
                        return;
                    }

                    var canvasObject = new GameObject("FUSE.Profiler.ClickBlocker");
                    canvasObject.transform.SetParent(_host != null ? _host.transform : null, worldPositionStays: false);
                    _blockerCanvas = canvasObject.AddComponent<Canvas>();
                    _blockerCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    _blockerCanvas.sortingOrder = short.MaxValue;
                    canvasObject.AddComponent<GraphicRaycaster>();

                    var imageObject = new GameObject("Blocker");
                    imageObject.transform.SetParent(canvasObject.transform, worldPositionStays: false);
                    var image = imageObject.AddComponent<Image>();
                    image.color = new Color(0f, 0f, 0f, 0f);
                    image.raycastTarget = true;
                    _blockerRect = image.rectTransform;
                    _blockerRect.anchorMin = new Vector2(0f, 1f);
                    _blockerRect.anchorMax = new Vector2(0f, 1f);
                    _blockerRect.pivot = new Vector2(0f, 1f);
                }

                _blockerCanvas.enabled = visible;
                if (visible && _blockerRect != null)
                {
                    // IMGUI rects are top-left-origin screen points; the
                    // anchored rect mirrors that with a top-left anchor and a
                    // negative Y offset.
                    var rect = ProfilerWindow.CurrentWindowRect;
                    _blockerRect.anchoredPosition = new Vector2(rect.x, -rect.y);
                    _blockerRect.sizeDelta = new Vector2(rect.width, rect.height);
                }
            }
        }
    }
}
