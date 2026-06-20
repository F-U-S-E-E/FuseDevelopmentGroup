using FUSE.Authoring.Editor;
using FUSE.Editor.Bookmarks;
using FUSE.Editor.Screen;
using FUSE.Editor.Screen.UI;
using FUSE.Editor.Track.Tools;
using FUSE.Infrastructure;
using FUSE.Loading;
using Fuse.Core.Model;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using HarmonyLib;
using JetBrains.Annotations;
using RLD;
using System;
using System.Collections.Generic;
using System.Reflection;
using UI.CarEditor;
using UnityEngine;

namespace FUSE.Editor
{
    /// <summary>
    /// FUSE-side editor host. Persists across scene loads as a
    /// DontDestroyOnLoad MonoBehaviour. Owns the active editor screen
    /// (currently an IMGUI EDEN-inspired mockup) and the active mod
    /// selection. Entered via the main-menu FUSE Editor button: the
    /// FUSE-side patch sets <see cref="FuseEditorBridge.EditorSessionPending"/>
    /// and launches a sandbox session with the editor scene; we wait for
    /// <see cref="MapDidLoadEvent"/> and then spawn the screen on top of
    /// the loaded world.
    /// </summary>
    public class FuseEditor : MonoBehaviour
    {
        // The built-in car-editor controller installed by SceneDescriptor.Editor.
        // FUSE editing operates on FUSE entities, not the car definition
        // store, so the controller's IMGUI store browser would just get in
        // the way. AlinasMapMod takes the same approach.
        private const string DefinitionEditorModeControllerName = "Definition Editor Mode Controller";

        // Character.PlayerController.SetSelected toggles the player
        // avatar's grip on the FirstPerson camera. Reached through
        // AccessTools because the method is internal; cached at first
        // use to avoid repeating the lookup per coroutine tick.
        private static readonly MethodInfo PlayerControllerSetSelectedMethod =
            AccessTools.Method(typeof(global::Character.PlayerController), "SetSelected",
                new[] { typeof(bool), typeof(Camera) });

        // Watchdog window after the initial Strategy jump. If anything
        // re-engages FirstPerson in this window we re-deselect + re-jump
        // on each Update tick. 120 frames ≈ 2 s at 60 fps — generous
        // enough to outlast any post-spawn coroutine the game has,
        // short enough that we stop spending CPU once the camera state
        // has settled.
        private const int StrategyWatchdogFrames = 120;
        private int _strategyWatchdogFramesRemaining;

        static FuseEditor _instance;

        FuseEditorScreen _screen;
        FuseEditorEntitySelection _entitySelection;

        [CanBeNull]
        public FuseLoadedMod ActiveMod { get; private set; } = null;

        public bool ModSelected => ActiveMod != null;

        public bool IsInEditor => _screen != null;

        [CanBeNull]
        internal FuseEditorScreen Screen => _screen;

        [NotNull]
        internal FuseEditorEntitySelection EntitySelection => _entitySelection ??= new FuseEditorEntitySelection();

        public static FuseEditor Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = GameObject.FindObjectOfType<FuseEditor>();
                }
                return _instance;
            }
        }

        public static void OnFuseLoad()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("Fuse Editor");
            DontDestroyOnLoad(go);

            _instance = go.AddComponent<FuseEditor>();

            Messenger.Default.Register<MapDidLoadEvent>(_instance, _instance.OnMapLoad);
            Messenger.Default.Register<MapDidUnloadEvent>(_instance, _instance.OnMapUnload);
        }

        public static void OnFuseUnload()
        {
            if (_instance != null)
            {
                // Drop the Messenger subscriptions registered in
                // OnFuseLoad before destroying the host. Without this,
                // a FUSE reload (UMM hot-reload) would leave the old
                // recipient registered against a destroyed
                // MonoBehaviour, and the next MapDidLoad / MapDidUnload
                // would dispatch into a dead Unity object.
                Messenger.Default.Unregister(_instance);
                GameObject.Destroy(_instance.gameObject);
                _instance = null;
            }
        }

        public void OnMapLoad(MapDidLoadEvent _)
        {
            // Only respond to map loads when a pending editor-session was
            // explicitly requested from the main-menu button. Without
            // this gate every normal sandbox / company load would
            // trigger the editor UI.
            if (!FuseEditorBridge.EditorSessionPending)
            {
                return;
            }

            FuseEditorBridge.EditorSessionPending = false;

            try
            {
                SuppressBuiltInEditorModeController();
            }
            catch (System.Exception ex)
            {
                FuseLog.Exception("FUSE failed to suppress the built-in DefinitionEditorModeController.", ex);
            }
            try
            {
                SetupRLDGizmoComponents();
            }
            catch (System.Exception ex)
            {
                FuseLog.Exception("FUSE failed to set up RLD gizmo components.", ex);
            }

            // Avatar wrestling: the previous JumpToPoint-only approach
            // (queue Strategy into _pendingJump and yield one frame)
            // wasn't sufficient — the game's own MapDidLoad spawns the
            // player avatar shortly after, and the avatar's
            // PlayerController.SetSelected(true, FirstPersonCamera)
            // re-engages FirstPerson, overriding whatever we queued.
            // Strategy: poll until the avatar exists, deselect it
            // (release its FirstPerson grip), then JumpToPoint. A
            // watchdog in Update reapplies for ~2 sec in case anything
            // else tries to re-engage FirstPerson.
            StartCoroutine(EnterStrategyViewCoroutine());

            try
            {
                SuppressGameplayHud();
            }
            catch (System.Exception ex)
            {
                FuseLog.Exception("FUSE failed to suppress gameplay HUD elements for the editor session.", ex);
            }

            SpawnScreenIfNeeded();
        }

        /// <summary>
        /// Hides the gameplay HUD pieces that overlap our editor panels:
        /// the top-right action strip and the bottom-left locomotive
        /// controls that appear when a loco is selected. Scene unload
        /// on Exit destroys these objects naturally, so we don't track
        /// and restore — a new gameplay session reinstantiates them.
        /// </summary>
        /// <remarks>
        /// Specific types over a blanket canvas-disable so we don't
        /// accidentally hide things we still need (the pause menu
        /// canvas, the FUSE editor's own IMGUI screen, third-party mod
        /// UIs). FindObjectsOfType handles the per-instance case
        /// without forcing us to know whether the type is a singleton.
        /// </remarks>
        private static void SuppressGameplayHud()
        {
            SuppressAll<global::UI.TopRightArea>("UI.TopRightArea");
            SuppressAll<global::UI.LocomotiveControlsHoverArea>("UI.LocomotiveControlsHoverArea");
        }

        private static void SuppressAll<T>(string label) where T : Component
        {
            var components = UnityEngine.Object.FindObjectsOfType<T>(includeInactive: false);
            if (components == null || components.Length == 0)
            {
                return;
            }

            var disabledCount = 0;
            foreach (var component in components)
            {
                if (component == null || component.gameObject == null)
                {
                    continue;
                }

                if (component.gameObject.activeSelf)
                {
                    component.gameObject.SetActive(false);
                    disabledCount++;
                }
            }

            if (disabledCount > 0)
            {
                FuseLog.Info($"FUSE editor: suppressed {disabledCount} {label} instance(s) for editor mode.");
            }
        }

        /// <summary>
        /// New editor sessions boot as a sandbox + spawn the player
        /// as the first-person engineer avatar at the world spawn —
        /// that's fine for gameplay but useless for editing. We need
        /// the modder to land in the overhead Strategy view, not
        /// stuck behind the avatar's eyes.
        /// </summary>
        /// <remarks>
        /// Strategy:
        /// <list type="number">
        ///   <item>Poll up to <see cref="StrategyWatchdogFrames"/>
        ///     frames for the player avatar to actually exist
        ///     (CameraSelector.shared.localAvatar non-null AND a
        ///     PlayerController instance findable in the scene).</item>
        ///   <item>Once found, call PlayerController.SetSelected(false,
        ///     null) to release its grip on the FirstPerson camera —
        ///     reflection because the method is internal.</item>
        ///   <item>Call CameraSelector.JumpToPoint(spawn, Strategy)
        ///     for the actual transition.</item>
        ///   <item>Arm the watchdog so Update re-applies for ~2 s if
        ///     anything else re-engages FirstPerson during the
        ///     spawn-flow window.</item>
        /// </list>
        /// </remarks>
        private System.Collections.IEnumerator EnterStrategyViewCoroutine()
        {
            // Yield one frame first so the game's own MapDidLoad
            // subscribers run before we start polling — the selector
            // itself subscribes to MapDidLoad and we want it set up.
            yield return null;

            global::Character.PlayerController controller = null;
            int frameCount = 0;
            while (frameCount < StrategyWatchdogFrames)
            {
                var selector = global::CameraSelector.shared;
                if (selector != null && selector.localAvatar != null)
                {
                    // Selector knows about the avatar — the spawn
                    // flow has progressed far enough that
                    // PlayerController should exist too.
                    controller = UnityEngine.Object.FindObjectOfType<global::Character.PlayerController>();
                    if (controller != null)
                    {
                        break;
                    }
                }
                frameCount++;
                yield return null;
            }

            if (controller == null)
            {
                FuseLog.Warning(
                    $"FUSE editor: player avatar / PlayerController not detected after {StrategyWatchdogFrames} frames; " +
                    "skipping Strategy view transition. Press F2 (or game's camera key) to switch manually.");
                yield break;
            }

            FuseLog.Info(
                $"FUSE editor: detected player avatar after {frameCount} frame(s); releasing FirstPerson grip.");
            TryDeselectPlayerAvatar(controller);

            var spawn = ResolveDefaultSpawn();
            var jumpSelector = global::CameraSelector.shared;
            if (jumpSelector != null)
            {
                try
                {
                    jumpSelector.JumpToPoint(spawn.position, spawn.rotation, global::CameraSelector.CameraIdentifier.Strategy);
                    FuseLog.Info(
                        $"FUSE editor: jumped to Strategy view at spawn (x={spawn.position.x:0.0}, y={spawn.position.y:0.0}, z={spawn.position.z:0.0}).");
                }
                catch (System.Exception ex)
                {
                    FuseLog.Exception("FUSE editor: CameraSelector.JumpToPoint threw.", ex);
                }
            }
            else
            {
                FuseLog.Warning("FUSE editor: CameraSelector.shared was null after avatar detect; cannot jump to Strategy view.");
            }

            // Arm the watchdog. Update() will re-deselect + re-jump
            // every frame the camera isn't in Strategy for the next
            // window — robust against late spawn-flow ticks that
            // would otherwise re-engage FirstPerson.
            _strategyWatchdogFramesRemaining = StrategyWatchdogFrames;
        }

        /// <summary>
        /// Releases the supplied avatar's grip on the FirstPerson
        /// camera by calling its <c>SetSelected(false, null)</c>.
        /// Uses reflection because the method is internal in
        /// Railroader's Character namespace.
        /// </summary>
        private static void TryDeselectPlayerAvatar(global::Character.PlayerController controller)
        {
            if (controller == null || PlayerControllerSetSelectedMethod == null)
            {
                return;
            }

            try
            {
                PlayerControllerSetSelectedMethod.Invoke(controller, new object[] { false, null });
            }
            catch (System.Exception ex)
            {
                FuseLog.Exception("FUSE editor: PlayerController.SetSelected(false, null) reflection invoke threw.", ex);
            }
        }

        /// <summary>
        /// Picks a position/rotation pair to land the editor camera at.
        /// Uses the map's default spawn point — the same place the
        /// game would put the first-person avatar — which gives the
        /// editor a known-good in-world landing spot regardless of
        /// which map loaded. Falls back to world origin if the spawn
        /// data isn't ready yet.
        /// </summary>
        private static (Vector3 position, Quaternion rotation) ResolveDefaultSpawn()
        {
            try
            {
                var defaultSpawn = global::Character.SpawnPoint.Default;
                if (defaultSpawn != null)
                {
                    // GamePositionRotation returns a (Vector3, Quaternion)
                    // value tuple — destructure to keep this readable.
                    var (pos, rot) = defaultSpawn.GamePositionRotation;
                    return (pos, rot);
                }
            }
            catch (System.Exception)
            {
                // Static accessor can throw on early MapDidLoad — fall
                // through to origin below.
            }

            return (Vector3.zero, Quaternion.identity);
        }

        public void OnMapUnload(MapDidUnloadEvent _)
        {
            // Map unload during an active editor session is treated as an
            // implicit exit so EditorExited subscribers (the FUSE-side
            // patch that calls ReturnToMainMenu) get a chance to clean
            // up. Routes through the same teardown as Exit() so bookmark
            // flush + per-session state reset happen here too — the old
            // inline version skipped them and could leak a dirty
            // bookmark set across into the next mod's session.
            TeardownEditorSession();
        }

        private void Update()
        {
            if (_screen == null)
            {
                return;
            }

            // Drive the active tool's per-frame tick (e.g. FusePlaceTool's
            // viewport-click detection). Cheap when no tool is active or
            // when the active tool's Tick is a no-op.
            FuseEditorToolRegistry.TickActive();

            // Flush bookmark mutations to disk, debounced so a held key
            // in the rename field doesn't write JSON on every change.
            TickBookmarkAutoSave();

            // Camera watchdog: while the post-entry window is open, if
            // anything re-engages FirstPerson, re-deselect the avatar
            // and re-apply Strategy. The window decrements every tick
            // and stops re-applying once it hits zero, so steady-state
            // cost is a single int compare per frame.
            TickStrategyWatchdog();

            // Update marker visibility based on camera distance
            // (culls distant markers to improve performance)
            Track.FuseNodeEditorController.UpdateMarkerVisibility();
        }

        private void TickStrategyWatchdog()
        {
            if (_strategyWatchdogFramesRemaining <= 0)
            {
                return;
            }
            _strategyWatchdogFramesRemaining--;

            var selector = global::CameraSelector.shared;
            if (selector == null)
            {
                return;
            }

            if (selector.CurrentCameraIdentifier == global::CameraSelector.CameraIdentifier.Strategy)
            {
                // Camera is in the expected state; nothing to do this
                // frame. Keep counting down so the watchdog still
                // covers a later re-engagement attempt within the
                // window.
                return;
            }

            // Camera drifted away from Strategy. Most common cause is
            // a late spawn-flow tick re-selecting the avatar. Release
            // it again and re-park the camera.
            var controller = UnityEngine.Object.FindObjectOfType<global::Character.PlayerController>();
            if (controller != null)
            {
                TryDeselectPlayerAvatar(controller);
            }

            try
            {
                var spawn = ResolveDefaultSpawn();
                selector.JumpToPoint(spawn.position, spawn.rotation, global::CameraSelector.CameraIdentifier.Strategy);
                // Log only on watchdog reapply — steady state stays quiet,
                // but if this fires past frame 1 it's a useful diagnostic.
                FuseLog.Info(
                    $"FUSE editor: watchdog re-applied Strategy view (camera was '{selector.CurrentCameraIdentifier}', " +
                    $"frames remaining {_strategyWatchdogFramesRemaining}).");
            }
            catch (System.Exception ex)
            {
                FuseLog.Exception("FUSE editor: watchdog JumpToPoint reapply threw.", ex);
            }
        }

        public void Enter()
        {
            // Pre-scene-load entry path: the patch hasn't actually
            // launched the session yet. Just log and let MapDidLoad open
            // the screen once the editor scene comes online.
            if (FuseEditorBridge.EditorSessionPending)
            {
                FuseLog.Info("FUSE editor entry requested; waiting for editor scene to load.");
                return;
            }

            // Post-scene-load entry path: a map is already loaded and the
            // user re-summoned the editor. Spawn immediately.
            SpawnScreenIfNeeded();
        }

        public void Exit()
        {
            if (_screen == null)
            {
                return;
            }

            TeardownEditorSession();
            FuseLog.Info("FUSE editor exited.");
        }

        /// <summary>
        /// Single teardown path shared by the explicit <see cref="Exit"/>
        /// and the implicit map-unload exit. Notifies EditorExited
        /// subscribers, resets the tool registry, flushes + clears
        /// per-session bookmark / save-tracker / watchdog state, and
        /// destroys the screen. Idempotent: a null screen is a no-op.
        /// </summary>
        private void TeardownEditorSession()
        {
            if (_screen == null)
            {
                return;
            }

            FuseEditorBridge.NotifyEditorExited();
            FuseEditorToolRegistry.Reset();

            // Flush any pending bookmark changes before tearing down the
            // registry; saves the user from losing recent additions on
            // exit / map unload. Then clear state for the next session.
            FuseEditorBookmarkRegistry.SaveIfDirty();
            FuseEditorBookmarkRegistry.Reset();
            _lastBookmarkRevision = 0;

            // Drop the "Last saved" indicator so the next Enter starts
            // with the empty "—" placeholder rather than carrying a
            // stale timestamp from the previous session.
            FuseEditorSaveTracker.Reset();

            // Stop the camera watchdog so it can't tick against a stale
            // session if the host survives into the next map.
            _strategyWatchdogFramesRemaining = 0;

            _screen.ExitRequested -= OnScreenExitRequested;
            Destroy(_screen.gameObject);
            _screen = null;
        }

        // Bookmark auto-save debounce. The registry marks itself dirty
        // on every mutation and bumps its Revision; we wait for the
        // revision to stop advancing for a short settle window before
        // writing, so a held key in the rename field writes once rather
        // than every keystroke. Teardown still flushes immediately, so
        // nothing is lost on exit.
        private const float BookmarkSaveDebounceSeconds = 0.4f;
        private int _lastBookmarkRevision;
        private float _bookmarkSettleDeadline;

        private void TickBookmarkAutoSave()
        {
            if (!FuseEditorBookmarkRegistry.IsDirty)
            {
                return;
            }

            var revision = FuseEditorBookmarkRegistry.Revision;
            if (revision != _lastBookmarkRevision)
            {
                // Still changing — push the settle deadline out and
                // wait for the edits to stop before hitting disk.
                _lastBookmarkRevision = revision;
                _bookmarkSettleDeadline = Time.realtimeSinceStartup + BookmarkSaveDebounceSeconds;
                return;
            }

            if (Time.realtimeSinceStartup >= _bookmarkSettleDeadline)
            {
                FuseEditorBookmarkRegistry.SaveIfDirty();
            }
        }

        private void SpawnScreenIfNeeded()
        {
            if (_screen != null)
            {
                return;
            }

            var go = new GameObject("FUSE-EditorScreen");
            go.transform.SetParent(transform, worldPositionStays: false);
            _screen = go.AddComponent<FuseEditorScreen>();
            _screen.ExitRequested += OnScreenExitRequested;

            RegisterDefaultTools();
            EnsureScratchModActive();

            FuseLog.Info("FUSE editor screen spawned over loaded world.");
        }

        /// <summary>
        /// Activates the auto-scaffolded "Untitled" scratch mod when
        /// no other mod is the active one on editor entry. The user
        /// drops directly into an editable project instead of being
        /// gated behind the mod browser.
        /// </summary>
        /// <remarks>
        /// Scratch mod has a stable id (<c>local.untitled-fuse-editor-scratch</c>)
        /// so subsequent entries reuse the same folder rather than
        /// accumulating untitled-NN orphans. The user can promote it
        /// to a real mod via the mod browser, or open a different mod
        /// via Scenario → Open Mod.
        /// </remarks>
        private void EnsureScratchModActive()
        {
            if (ActiveMod != null)
            {
                return;
            }

            var modsRoot = ResolveModsRootPath();
            if (string.IsNullOrEmpty(modsRoot))
            {
                FuseLog.Warning("FUSE editor: could not resolve mods root for scratch mod scaffold.");
                return;
            }

            var scratch = Mods.FuseEditorModCatalog.EnsureScratchMod(modsRoot);
            if (scratch != null)
            {
                SetActiveMod(scratch);
                FuseLog.Info($"FUSE editor: activated scratch mod '{scratch.Definition?.Id}'.");
            }
        }

        private static string ResolveModsRootPath()
        {
            // Same heuristic the mod-browser uses: walk up from a
            // known mod folder to the parent. FuseModLoader exposes
            // loaded mods via GetLoadedModsInOrder; the parent of
            // any of them is the mods root.
            try
            {
                var loaded = FuseModLoader.GetLoadedModsInOrder();
                if (loaded != null)
                {
                    for (int i = 0; i < loaded.Count; i++)
                    {
                        var folder = loaded[i]?.FolderPath;
                        if (!string.IsNullOrEmpty(folder))
                        {
                            var parent = System.IO.Directory.GetParent(folder);
                            if (parent != null) return parent.FullName;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                FuseLog.Exception("FUSE editor: failed to resolve mods root path.", ex);
            }
            return null;
        }

        /// <summary>
        /// Populates <see cref="FuseEditorToolRegistry"/> with the
        /// concrete tools that ship today and activates Select by default
        /// so markers appear without the user having to click a toolbar
        /// button first. <see cref="FuseEditorToolRegistry.Register"/> is
        /// idempotent on <see cref="IFuseEditorTool.Id"/>, so an
        /// Exit-then-Enter cycle is safe; Reset() in <see cref="Exit"/>
        /// keeps the list from accumulating.
        /// </summary>
        private static void RegisterDefaultTools()
        {
            var select = new FuseSelectTool();
            FuseEditorToolRegistry.Register(select);
            FuseEditorToolRegistry.Register(new FuseMoveTool());
            FuseEditorToolRegistry.Register(new FuseRotateTool());
            FuseEditorToolRegistry.Register(new FuseScaleTool());
            FuseEditorToolRegistry.Register(new FusePlaceTool());
            FuseEditorToolRegistry.SetActive(select);
        }

        private static void SuppressBuiltInEditorModeController()
        {
            // The editor scene installs a "Definition Editor Mode Controller"
            // GameObject whose OnGUI draws the in-game car editor's store
            // browser. We don't want that on top of the FUSE editor surface.
            var controller = GameObject.Find(DefinitionEditorModeControllerName);
            if (controller != null)
            {
                // Before suppressing, set up the RLD gizmo system components
                // that are attached to this controller

                controller.SetActive(false);
                FuseLog.Info("FUSE editor suppressed the built-in DefinitionEditorModeController.");
            }
        }

        /// <summary>
        /// Sets up the RLD gizmo system components attached to the
        /// DefinitionEditorModeController. The controller has rtFocusCamera
        /// and rldApp components that need to be initialized before the
        /// move/rotate gizmos can work properly.
        /// </summary>
        private static void SetupRLDGizmoComponents()
        {
            try
            {
                // Find the rtFocusCamera component and set it to use Camera.main
                var rtFocusCamera = GameObject.FindAnyObjectByType<RTFocusCamera>(FindObjectsInactive.Include);
                if (rtFocusCamera != null)
                {
                    rtFocusCamera.SetTargetCamera(Camera.main);
                }

                // Find the rldApp component and activate its GameObject
                var rldApp = GameObject.FindAnyObjectByType<RLDApp>(FindObjectsInactive.Include);
                if (rldApp != null)
                {
                    rldApp.gameObject.SetActive(true);
                    FuseLog.Info("FUSE editor: Activated RLD App GameObject.");
                }
            }
            catch (System.Exception ex)
            {
                FuseLog.Exception("FUSE editor: Failed to set up RLD gizmo components.", ex);
            }
        }

        private void OnScreenExitRequested()
        {
            Exit();
        }

        public void SetActiveMod(FuseLoadedMod mod)
        {
            if (FuseModLoader.IsApplied(mod.Definition.Id))
            {
                ActiveMod = mod;
                // Load this mod's saved bookmarks. LoadForMod is safe
                // when the file doesn't exist yet — that just leaves an
                // empty list ready for Add.
                FuseEditorBookmarkRegistry.LoadForMod(mod.FolderPath);
            }
            else
            {
                FuseLog.Info($"Unable to edit mod: {mod.Definition.Id}, mod is not loaded");
            }
        }
    }
}
