using FUSE.Authoring.Editor;
using FUSE.Editor.Bookmarks;
using FUSE.Editor.Screen;
using FUSE.Editor.Screen.UI;
using FUSE.Editor.Track.Tools;
using FUSE.Infrastructure;
using FUSE.Loading;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using JetBrains.Annotations;
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

        static FuseEditor _instance;

        FuseEditorScreen _screen;

        [CanBeNull]
        public FuseLoadedMod ActiveMod { get; private set; } = null;

        public bool ModSelected => ActiveMod != null;

        public bool IsInEditor => _screen != null;

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

            // The game's own MapDidLoad subscriber races us at this same
            // event tick — fighting it from a SelectCamera retry loop
            // was unreliable because the avatar spawn flow keeps snapping
            // the camera back to FirstPerson for the first few frames.
            // CameraSelector.JumpToPoint queues into the selector's
            // pending-jump slot which the spawn flow honors, so we get
            // a single deterministic landing in Strategy at a known
            // in-world point. Wait one frame so the game's own
            // MapDidLoad handler finishes draining its pending jump
            // (if any) before we queue ours.
            StartCoroutine(JumpToStrategySpawnNextFrame());

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
        /// New editor sessions boot as a sandbox + spawn the player as
        /// the first-person engineer avatar at the world spawn — that's
        /// fine for gameplay but useless for editing. We hop to the
        /// default spawn point in Strategy view as part of the editor's
        /// MapDidLoad handoff so the modder lands in a free-moving
        /// overhead camera at a known-good in-world location instead
        /// of inside an avatar.
        /// </summary>
        /// <remarks>
        /// CameraSelector.JumpToPoint is the documented public
        /// API for "go here AND use this camera mode" — it parks the
        /// requested destination in the selector's pending-jump slot
        /// which the avatar's spawn flow honors. That replaces the
        /// earlier retry loop that fought the spawn flow frame-by-frame.
        /// </remarks>
        private static System.Collections.IEnumerator JumpToStrategySpawnNextFrame()
        {
            // Settle one frame so the game's own MapDidLoad subscribers
            // (which include the camera selector) finish their work
            // before we queue our jump.
            yield return null;

            var selector = global::CameraSelector.shared;
            if (selector == null)
            {
                FuseLog.Warning("FUSE editor: CameraSelector.shared was null one frame after MapDidLoad; cannot park Strategy camera at spawn.");
                yield break;
            }

            var spawn = ResolveDefaultSpawn();
            try
            {
                selector.JumpToPoint(spawn.position, spawn.rotation, global::CameraSelector.CameraIdentifier.Strategy);
            }
            catch (System.Exception ex)
            {
                FuseLog.Exception("FUSE editor: CameraSelector.JumpToPoint threw.", ex);
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
            // patch that calls ReturnToMainMenu) get a chance to clean up.
            if (_screen != null)
            {
                FuseEditorBridge.NotifyEditorExited();
                FuseEditorToolRegistry.Reset();
                Destroy(_screen.gameObject);
                _screen = null;
            }
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

            // Flush bookmark mutations to disk. The registry only writes
            // when dirty so this is free in steady state.
            FuseEditorBookmarkRegistry.SaveIfDirty();
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

            FuseEditorBridge.NotifyEditorExited();
            FuseEditorToolRegistry.Reset();

            // Flush any pending bookmark changes before tearing down the
            // registry; saves the user from losing recent additions on
            // exit. Then clear state for the next session.
            FuseEditorBookmarkRegistry.SaveIfDirty();
            FuseEditorBookmarkRegistry.Reset();

            // Drop the "Last saved" indicator so the next Enter starts
            // with the empty "—" placeholder rather than carrying a
            // stale timestamp from the previous session.
            FuseEditorSaveTracker.Reset();

            _screen.ExitRequested -= OnScreenExitRequested;
            Destroy(_screen.gameObject);
            _screen = null;

            FuseLog.Info("FUSE editor exited.");
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

            FuseLog.Info("FUSE editor screen spawned over loaded world.");
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
                controller.SetActive(false);
                FuseLog.Info("FUSE editor suppressed the built-in DefinitionEditorModeController.");
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
