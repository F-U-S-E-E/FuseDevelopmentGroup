using System;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Interface
{
    // FUSE-owned enhanced loading screen (issue #83). A DontDestroyOnLoad host
    // MonoBehaviour created from FusePlugin.Load (mirroring FuseTrackDebugOverlay /
    // FuseWorldLabelsOverlay), it replaces the bare stock "Loading…" screen with a
    // staged progress bar and a human-readable "current step" label, and stays up
    // until FUSE's own post-load pipeline finishes.
    //
    // Driving signals arrive from FuseLoadingScreenPatches (Harmony) and
    // FuseLifecycle; this class only owns the visuals and the show/hide gate (the
    // gate itself lives in the Unity-free FuseLoadingScreenState so it is testable).
    //
    // THE HARD CONSTRAINT — read before "fixing" the missing animation: Unity UI
    // repaints only between frames on the main thread. The heaviest load phases
    // (the save restore and FUSE's mod-apply) block the main thread for their whole
    // duration, so the screen CANNOT animate or update *during* a phase — only
    // *between* phases. That is why the progress bar deliberately switches to a
    // static full band (not a spinner) once a synchronous phase begins: a frozen
    // frame must look intentional, never "hung". Do not add motion that implies
    // progress during a synchronous phase; it will appear broken.
    //
    // Phase 1 here is the IMGUI (OnGUI) renderer, alive from the persistent scene
    // onward with no dependency on game UI internals (same rationale as
    // FuseLegacyInstallAlert). Phase 2 (a code-built uGUI/TMP canvas) can replace
    // the renderer without touching the controller API or any driver patch.
    internal sealed class FuseLoadingScreen : MonoBehaviour
    {
        private const float ContentWidth = 720f;
        private const float TipIntervalSeconds = 6f;

        private static GameObject _host;
        private static FuseLoadingScreen _instance;

        private readonly FuseLoadingScreenState _state = new FuseLoadingScreenState();

        private GameObject _stockLoadingScreen;
        private bool _renderedOnce;
        private float _loadBeganAt;
        private bool _renderingDisabled;
        private bool _renderFailureLogged;

        private bool _stylesReady;
        private Texture2D _backgroundTexture;
        private Texture2D _barTrackTexture;
        private Texture2D _barFillTexture;
        private Texture2D _barSyncTexture;
        private GUIStyle _brandStyle;
        private GUIStyle _stepStyle;
        private GUIStyle _detailStyle;
        private GUIStyle _percentStyle;
        private GUIStyle _tipStyle;

        private static readonly string[] Tips =
        {
            "FUSE keeps the screen up until every mod has finished applying.",
            "A frozen step label is normal: the world is being assembled on the main thread.",
            "Larger railroads take longer to restore — every car is rebuilt individually.",
            "Mods, track, scenery, and terrain are all reapplied after the save loads.",
            "You can turn this screen off in the FUSE settings to fall back to the stock one.",
        };

        // Whether the FUSE loading screen is currently owning the visuals. Read by
        // the frame-spike diagnostic to exclude load-phase frames, whose synchronous
        // main-thread stalls are by design and would drown gameplay spikes.
        internal static bool IsShowing => _instance != null && _instance._state.Active;

        public static void Ensure()
        {
            if (_host != null)
            {
                return;
            }

            _host = new GameObject("FUSE Loading Screen");
            DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideAndDontSave;
            _instance = _host.AddComponent<FuseLoadingScreen>();
            FuseLog.Info("FUSE enhanced loading screen initialized.");
        }

        public static void Shutdown()
        {
            if (_host != null)
            {
                Destroy(_host);
                _host = null;
            }

            _instance = null;
        }

        // MapWillLoadEvent → begin a fresh load. Gated here so a single switch falls
        // the whole feature back to the untouched stock screen.
        public static void BeginLoad(string reason)
        {
            if (_instance == null || !FuseSettings.EnableEnhancedLoadingScreen)
            {
                return;
            }

            _instance.BeginLoadInstance(reason);
        }

        // Captured from the PersistentLoader.ShowLoadingScreen postfix so we can hide
        // the stock visuals once — and only once — our own first frame has drawn.
        public static void CaptureStockLoadingScreen(GameObject stockLoadingScreen)
        {
            if (_instance == null)
            {
                return;
            }

            _instance._stockLoadingScreen = stockLoadingScreen;
        }

        // PersistentLoader.ShowProgress postfix. Drives the determinate bar and the
        // async-phase step label (mapped from the game's flavor string).
        public static void SetProgressFromGame(int intProgress, string gameText)
        {
            if (_instance == null)
            {
                return;
            }

            var fraction = intProgress / 100f;
            _instance._state.SetProgress(fraction, Now);
            var step = FuseLoadingLabels.MapProgressFlavor(gameText, fraction);
            // The async scene-load ticks are determinate (syncPhase:false); only the
            // ~95% hand-off into the blocking save restore flips the bar to its band.
            _instance._state.SetStep(step.Title, step.Detail, step.SyncHandoff, Now);
        }

        // Set the label for a synchronous (main-thread-blocking) phase. Every caller
        // is at the boundary of a blocking call, so this always marks the sync phase
        // (which switches the bar to its static treatment).
        public static void SetStep(string title, string detail = null)
        {
            if (_instance == null)
            {
                return;
            }

            _instance._state.SetStep(title, detail, syncPhase: true, Now);
        }

        // PersistentLoader.ShowLoadingScreen(false).
        public static void NotifyGameScreenHidden()
        {
            if (_instance == null)
            {
                return;
            }

            _instance._state.NotifyGameScreenHidden(Now);
        }

        // End of FuseLifecycle.OnMapDidLoad (runs on all paths, including non-host
        // multiplayer clients, via its finally block).
        public static void NotifyFusePipelineComplete()
        {
            if (_instance == null)
            {
                return;
            }

            _instance._state.NotifyFusePipelineComplete(Now);
        }

        // Load failure / return-to-menu: hide immediately rather than waiting on the
        // FUSE-pipeline flag (which never fires on those paths).
        public static void Abort(string reason)
        {
            if (_instance == null)
            {
                return;
            }

            _instance.AbortInstance(reason);
        }

        private static float Now => Time.realtimeSinceStartup;

        private void BeginLoadInstance(string reason)
        {
            _state.BeginLoad(Now);
            _renderedOnce = false;
            _loadBeganAt = Now;
            FuseLog.Info($"FUSE enhanced loading screen shown ({reason}).");
        }

        private void AbortInstance(string reason)
        {
            if (!_state.Active)
            {
                return;
            }

            _state.Abort();
            RestoreStockLoadingScreenIfHidden();
            FuseLog.Info($"FUSE enhanced loading screen aborted ({reason}).");
        }

        private void Update()
        {
            // Capture the pre-update state: UpdateVisibility flips Active off exactly
            // once, so wasActive distinguishes "the gate just closed" from "still
            // hidden", and the gate flags read before the flip classify the close
            // reason even if a future refactor resets them on close.
            var wasActive = _state.Active;
            var gameScreenHidden = _state.GameScreenHidden;
            var fusePipelineDone = _state.FusePipelineDone;

            if (!_state.UpdateVisibility(Now))
            {
                // We never re-show the stock screen on a normal hide — the game has
                // already hidden it. Abort paths close the state synchronously and
                // log on their own, so they never surface as a transition here.
                if (wasActive)
                {
                    // Both flags set means the two-flag gate released; anything else
                    // can only be the watchdog backstop. renderFailed marks the OnGUI
                    // fallback where the stock screen carried the load, so this line
                    // can't read as a healthy FUSE-screen session in that case.
                    var watchdog = !(gameScreenHidden && fusePipelineDone);
                    FuseLog.Info(
                        $"FUSE enhanced loading screen hidden (gameScreenHidden={gameScreenHidden}, fusePipelineDone={fusePipelineDone}, watchdog={watchdog}, renderFailed={_renderingDisabled}) after {Now - _loadBeganAt:0.0}s.");
                }

                return;
            }

            // Hide the stock screen only after our own first frame has drawn, so a
            // render failure leaves the stock screen visible instead of a blank one.
            if (!_renderingDisabled && _renderedOnce && _stockLoadingScreen != null && _stockLoadingScreen.activeSelf)
            {
                try
                {
                    _stockLoadingScreen.SetActive(false);
                }
                catch (Exception ex)
                {
                    FuseLog.Warning("FUSE enhanced loading screen could not hide the stock screen: " + ex.GetBaseException().Message);
                }
            }
        }

        private void OnGUI()
        {
            if (_renderingDisabled || !_state.Active)
            {
                return;
            }

            var evt = Event.current;
            if (evt == null || evt.type != EventType.Repaint)
            {
                return;
            }

            try
            {
                EnsureStyles();
                DrawScreen();
                _renderedOnce = true;
            }
            catch (Exception ex)
            {
                HandleRenderFailure(ex);
            }
        }

        private void OnDestroy()
        {
            DestroyTexture(ref _backgroundTexture);
            DestroyTexture(ref _barTrackTexture);
            DestroyTexture(ref _barFillTexture);
            DestroyTexture(ref _barSyncTexture);
        }

        private void HandleRenderFailure(Exception ex)
        {
            _renderingDisabled = true;
            if (_state.GameScreenHidden)
            {
                // The game has already torn down its own screen and our renderer is
                // dead — nothing is left to cover the world, so stop gating and let
                // it show rather than holding a blank screen until the watchdog.
                _state.Abort();
            }
            else
            {
                // The game still wants a loading screen up; restore the stock one.
                RestoreStockLoadingScreenIfHidden();
            }

            if (!_renderFailureLogged)
            {
                _renderFailureLogged = true;
                FuseLog.Warning(
                    "FUSE enhanced loading screen OnGUI failed; falling back to the stock screen for this session: "
                    + ex.GetBaseException().Message);
            }
        }

        private void RestoreStockLoadingScreenIfHidden()
        {
            if (_stockLoadingScreen == null || _state.GameScreenHidden)
            {
                return;
            }

            try
            {
                if (!_stockLoadingScreen.activeSelf)
                {
                    _stockLoadingScreen.SetActive(true);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning("FUSE enhanced loading screen could not restore the stock screen: " + ex.GetBaseException().Message);
            }
        }

        private void DrawScreen()
        {
            var width = Screen.width;
            var height = Screen.height;

            GUI.depth = -1000;
            GUI.DrawTexture(new Rect(0f, 0f, width, height), _backgroundTexture);

            var columnWidth = Mathf.Min(ContentWidth, width - 80f);
            var columnX = (width - columnWidth) * 0.5f;
            var centerY = height * 0.5f;

            GUI.Label(new Rect(columnX, centerY - 132f, columnWidth, 26f), "FUSE", _brandStyle);

            var title = string.IsNullOrEmpty(_state.StepTitle) ? "Loading…" : _state.StepTitle;
            GUI.Label(new Rect(columnX, centerY - 96f, columnWidth, 48f), title, _stepStyle);

            if (!string.IsNullOrEmpty(_state.StepDetail))
            {
                GUI.Label(new Rect(columnX, centerY - 46f, columnWidth, 28f), _state.StepDetail, _detailStyle);
            }

            DrawBar(new Rect(columnX, centerY + 8f, columnWidth, 12f), centerY, columnX, columnWidth);

            GUI.Label(new Rect(columnX, centerY + 64f, columnWidth, 24f), CurrentTip(), _tipStyle);
        }

        private void DrawBar(Rect rect, float centerY, float columnX, float columnWidth)
        {
            GUI.DrawTexture(rect, _barTrackTexture);

            if (_state.InSyncPhase)
            {
                // Synchronous phase: the main thread is blocked. A partial fill would
                // read as "stuck at N%", so show a full muted band — intentional, not
                // stalled — and let the step label carry the meaning. No percentage.
                GUI.DrawTexture(rect, _barSyncTexture);
            }
            else
            {
                var fillWidth = rect.width * Mathf.Clamp01(_state.Progress);
                if (fillWidth > 0f)
                {
                    GUI.DrawTexture(new Rect(rect.x, rect.y, fillWidth, rect.height), _barFillTexture);
                }

                var percent = Mathf.RoundToInt(Mathf.Clamp01(_state.Progress) * 100f);
                GUI.Label(new Rect(columnX, centerY + 24f, columnWidth, 22f), percent + "%", _percentStyle);
            }
        }

        private static string CurrentTip()
        {
            if (Tips.Length == 0)
            {
                return string.Empty;
            }

            var index = Mathf.Abs((int)(Time.realtimeSinceStartup / TipIntervalSeconds)) % Tips.Length;
            return Tips[index];
        }

        private void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }

            _backgroundTexture = SolidTexture(new Color(0.06f, 0.07f, 0.09f, 1f));
            _barTrackTexture = SolidTexture(new Color(1f, 1f, 1f, 0.10f));
            _barFillTexture = SolidTexture(new Color(0.36f, 0.62f, 0.92f, 1f));
            _barSyncTexture = SolidTexture(new Color(0.36f, 0.62f, 0.92f, 0.35f));

            _brandStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _brandStyle.normal.textColor = new Color(0.45f, 0.6f, 0.85f);

            _stepStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            _stepStyle.normal.textColor = Color.white;

            _detailStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            _detailStyle.normal.textColor = new Color(0.78f, 0.82f, 0.88f);

            _percentStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleRight
            };
            _percentStyle.normal.textColor = new Color(0.7f, 0.76f, 0.84f);

            _tipStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            _tipStyle.normal.textColor = new Color(0.55f, 0.6f, 0.68f);

            _stylesReady = true;
        }

        private static Texture2D SolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture != null)
            {
                Destroy(texture);
                texture = null;
            }
        }
    }
}
