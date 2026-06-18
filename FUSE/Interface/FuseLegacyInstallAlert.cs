using System;
using System.Collections.Generic;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Interface
{
    // Full-screen blocking modal that warns the user when FUSE detects a
    // conflicting legacy Railloader install at startup (see
    // FuseLegacyInstallDetector). OnGUI is used because the game's Window
    // primitive (ProgrammaticWindowCreator) is null until a map is loaded;
    // OnGUI is the only UI surface that's alive from the splash screen
    // onward, which is when the user most needs to see this.
    internal sealed class FuseLegacyInstallAlert : MonoBehaviour
    {
        private const float DialogWidth = 720f;
        private const float DialogHeight = 460f;

        private static GameObject _host;
        private static FuseLegacyInstallAlert _instance;
        private static bool _dismissed;

        private IReadOnlyList<string> _conflicts = Array.Empty<string>();
        private Vector2 _scrollPosition;
        private Texture2D _dimTexture;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _pathStyle;
        private GUIStyle _instructionStyle;
        private bool _stylesReady;
        private bool _renderFailureLogged;

        internal static void Ensure(IReadOnlyList<string> conflicts)
        {
            if (conflicts == null || conflicts.Count == 0)
            {
                return;
            }

            if (_host != null)
            {
                if (_instance != null)
                {
                    _instance._conflicts = conflicts;
                }
                return;
            }

            _host = new GameObject("FUSE Legacy Install Alert");
            DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideAndDontSave;
            _instance = _host.AddComponent<FuseLegacyInstallAlert>();
            _instance._conflicts = conflicts;
            FuseLog.Info("FUSE legacy install alert displayed at startup.");
        }

        private void OnGUI()
        {
            if (_dismissed || _conflicts == null || _conflicts.Count == 0)
            {
                return;
            }

            try
            {
                EnsureStyles();
                DrawDim();
                DrawDialog();
            }
            catch (Exception ex)
            {
                if (!_renderFailureLogged)
                {
                    _renderFailureLogged = true;
                    FuseLog.Warning(
                        "FUSE legacy install alert OnGUI failed; alert will not render this session: "
                        + ex.GetBaseException().Message);
                }
            }
        }

        private void OnDestroy()
        {
            if (_dimTexture != null)
            {
                Destroy(_dimTexture);
                _dimTexture = null;
            }
        }

        private void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }

            _dimTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _dimTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.85f));
            _dimTexture.Apply();

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _titleStyle.normal.textColor = new Color(1f, 0.35f, 0.35f);

            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                wordWrap = true
            };
            _bodyStyle.normal.textColor = Color.white;

            _pathStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true
            };
            _pathStyle.normal.textColor = new Color(1f, 0.85f, 0.5f);

            _instructionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                wordWrap = true,
                fontStyle = FontStyle.Bold
            };
            _instructionStyle.normal.textColor = Color.white;

            _stylesReady = true;
        }

        private void DrawDim()
        {
            GUI.depth = -1000;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _dimTexture);
        }

        private void DrawDialog()
        {
            var width = Mathf.Min(DialogWidth, Screen.width - 40f);
            var height = Mathf.Min(DialogHeight, Screen.height - 40f);
            var rect = new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);

            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 16f, rect.y + 16f, rect.width - 32f, rect.height - 32f));

            GUILayout.Label("FUSE Cannot Run Safely", _titleStyle);
            GUILayout.Space(8f);
            GUILayout.Label(
                "FUSE found leftover files from the legacy Railloader mod loader in this " +
                "Railroader install. While these files are present, mods bind to the old " +
                "loader's types instead of FUSE's compatibility shim and will not load " +
                "correctly. You may see mods do nothing after loading a save.",
                _bodyStyle);
            GUILayout.Space(8f);
            GUILayout.Label("Conflicting files:", _instructionStyle);

            var listHeight = Mathf.Max(60f, rect.height - 300f);
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(listHeight));
            foreach (var path in _conflicts)
            {
                GUILayout.Label(path ?? string.Empty, _pathStyle);
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8f);
            GUILayout.Label(
                "Quit the game, delete the files listed above, then restart Railroader.",
                _instructionStyle);
            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Paths", GUILayout.Width(160f), GUILayout.Height(28f)))
            {
                CopyConflictsToClipboard();
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("I Understand", GUILayout.Width(160f), GUILayout.Height(28f)))
            {
                _dismissed = true;
                FuseLog.Info("FUSE legacy install alert dismissed by user.");
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private void CopyConflictsToClipboard()
        {
            try
            {
                GUIUtility.systemCopyBuffer = string.Join(Environment.NewLine, _conflicts);
                FuseLog.Info("FUSE legacy install alert copied conflicting paths to clipboard.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE legacy install alert could not copy paths to clipboard: "
                    + ex.GetBaseException().Message);
            }
        }
    }
}
