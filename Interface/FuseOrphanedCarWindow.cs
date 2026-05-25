using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Data;
using FUSE.Infrastructure;
using FUSE.Loading;
using UI;
using UI.Builder;
using UI.Common;
using UnityEngine;

namespace FUSE.Interface
{
    /// <summary>
    /// Popup window that lists every car the save load could not
    /// restore due to an orphan prototype identifier, alongside a
    /// picker per prototype-group of currently-loadable replacement
    /// car types. Modelled on the game's own Lost &amp; Found
    /// placement window: a static <c>Ensure</c> spins up a singleton
    /// MonoBehaviour host, and a static <c>ShowIfNeeded</c> is wired
    /// to the save-load completion hook so the window pops up
    /// automatically once the registry has entries to surface. No
    /// interaction with the FUSE Health window is required; this
    /// window stands alone and closes itself when every orphan has
    /// been replaced or the user dismisses it.
    /// </summary>
    internal sealed class FuseOrphanedCarWindow : MonoBehaviour
    {
        private const string WindowIdentifier = "FUSE.OrphanedCars";

        private static GameObject _host;
        private static FuseOrphanedCarWindow _instance;
        private Window _window;
        private UIPanel _panel;
        private readonly Dictionary<string, string> _selectionByPrototype =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string _lastAction = string.Empty;

        private Vector2Int DefaultSize => new Vector2Int(640, 520);
        private Vector2Int MaxSize => new Vector2Int(Screen.width, Screen.height);
        private Window.Sizing DefaultSizing => Window.Sizing.Resizable(DefaultSize, MaxSize);
        private Window.Position DefaultPosition => Window.Position.Center;

        /// <summary>
        /// Idempotent host creation. Mirrors the FUSE Health UI
        /// pattern so we live for the full session and can be
        /// re-shown across multiple save loads without re-creating
        /// the GameObject. Called once from FUSE startup.
        /// </summary>
        public static void Ensure()
        {
            if (_host != null)
            {
                return;
            }

            _host = new GameObject("FUSE Orphaned Cars Window");
            DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideAndDontSave;
            _instance = _host.AddComponent<FuseOrphanedCarWindow>();
            FuseLog.Info("FUSE orphaned-car window host initialized.");
        }

        /// <summary>
        /// Opens the window IF the orphan registry currently has any
        /// entries to surface. Safe to call from save-load completion
        /// hooks — runs only when there's something to show, and
        /// silently no-ops when the registry is empty (so saves
        /// without orphans never pop a window).
        /// </summary>
        public static void ShowIfNeeded()
        {
            if (_instance == null)
            {
                return;
            }
            if (FuseSaveCarFaultRegistry.Count == 0)
            {
                return;
            }
            _instance.OpenAndPopulate();
        }

        private void OpenAndPopulate()
        {
            try
            {
                if (!EnsureWindow())
                {
                    return;
                }

                RebuildWindow();
                _window.ShowWindow();
                FuseLog.Info(
                    $"FUSE orphaned-car window shown for {FuseSaveCarFaultRegistry.Count} car(s).");
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE orphaned-car window failed to open: {ex.GetBaseException().Message}");
            }
        }

        private bool EnsureWindow()
        {
            if (_window != null && _window.gameObject != null && _window.contentRectTransform != null)
            {
                return true;
            }

            if (!WindowCreatorHelper.CanCreateWindow)
            {
                FuseLog.Warning(
                    "FUSE orphaned-car window cannot open yet: ProgrammaticWindowCreator not available.");
                return false;
            }

            _window = WindowCreatorHelper.Shared.CreateWindow(
                WindowIdentifier, DefaultSize.x, DefaultSize.y, DefaultPosition);
            if (_window == null)
            {
                FuseLog.Warning(
                    "FUSE orphaned-car window could not be created from the base-game window prefab.");
                return false;
            }

            _window.Title = "FUSE: Orphaned Cars";
            return true;
        }

        private void RebuildWindow()
        {
            if (!EnsureWindow())
            {
                return;
            }

            if (_panel != null)
            {
                _panel.Dispose();
                _panel = null;
            }

            _panel = WindowCreatorHelper.Shared.PopulateWindow(_window, BuildContent);
            WindowPersistence.SetInitialPositionSize(
                _window, WindowIdentifier, DefaultSize, DefaultPosition, DefaultSizing);
        }

        private void BuildContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 140f;
            builder.Spacing = 6f;

            var faults = FuseSaveCarFaultRegistry.GetAll();
            if (faults.Count == 0)
            {
                builder.AddLabel(
                    "No orphan cars remain in this session. You can close this window.")
                    .TextWrap((TMPro.TextOverflowModes)1, (TMPro.TextWrappingModes)0);
                builder.Spacer(8f);
                builder.HStack(row =>
                {
                    row.AddButtonCompact("Close", () => _window.CloseWindow());
                }, 6f).Height(32f);
                return;
            }

            builder.AddLabel(
                $"FUSE blocked {faults.Count} car(s) from loading because their car-type definitions " +
                "were unusable — typically because the legacy SCAssetPacks variant of the pack ships " +
                "an asset bundle that conflicts with the modern variant's bundle, and FUSE filtered " +
                "the duplicate to prevent Unity from refusing the load.")
                .TextWrap((TMPro.TextOverflowModes)1, (TMPro.TextWrappingModes)0);
            builder.AddLabel(
                "Pick a replacement car type per group and click Replace. The new cars will keep the " +
                "original car id, road number, location, and waybill — only the model/type changes.")
                .TextWrap((TMPro.TextOverflowModes)1, (TMPro.TextWrappingModes)0);
            builder.Spacer(6f);

            var availableReplacements = FuseSaveCarFaultReplacement.GetAvailablePrototypeIds();
            if (availableReplacements == null || availableReplacements.Length == 0)
            {
                builder.AddLabel(
                    "No replacement car types are currently loadable. Make sure your mod with the " +
                    "modern car definitions is installed and registered.")
                    .TextWrap((TMPro.TextOverflowModes)1, (TMPro.TextWrappingModes)0);
                return;
            }

            var groups = faults
                .GroupBy(fault => fault.MissingPrototypeId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

            builder.VScrollView(scroll =>
            {
                scroll.Spacing = 8f;
                foreach (var group in groups)
                {
                    var groupKey = string.IsNullOrEmpty(group.Key) ? "<unknown>" : group.Key;
                    var groupList = group.ToList();
                    BuildGroup(scroll, groupKey, groupList, availableReplacements);
                }
            });

            if (!string.IsNullOrEmpty(_lastAction))
            {
                builder.Spacer(6f);
                builder.AddLabel(_lastAction)
                    .TextWrap((TMPro.TextOverflowModes)1, (TMPro.TextWrappingModes)0);
            }

            builder.Spacer(6f);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Refresh", RebuildWindow);
                row.AddButtonCompact("Close", () => _window.CloseWindow());
            }, 6f).Height(32f);
        }

        private void BuildGroup(
            UIPanelBuilder builder,
            string groupKey,
            List<FuseSaveCarFault> groupFaults,
            string[] availableReplacements)
        {
            builder.AddSection($"Type: {groupKey} ({groupFaults.Count})");
            foreach (var fault in groupFaults.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                builder.AddField(
                    fault.DisplayName,
                    () => $"id={fault.CarId} at segment={fault.LocationSegmentId} dist={fault.LocationDistance:F1}",
                    0).Height(24f);
            }

            // Single button: pick a same-type random replacement per
            // car using the game's own interchange picker
            // (PrefabStore.Random with a CarTypeFilter built from
            // the orphan's legacy carType). One click rolls a fresh
            // replacement for every car in this prototype-group and
            // spawns them in place. The picker uses the game's
            // standard weight-distribution so the size mix mirrors
            // what the interchange would have spawned.
            builder.HStack(row =>
            {
                row.AddButton(
                    $"Pick same-type random replacement for {groupFaults.Count} car(s)",
                    () => ApplyRandomSameType(groupKey, groupFaults));
            }, 6f).Height(36f);
            builder.Spacer(4f);
        }

        private static readonly System.Random _replacementRng = new System.Random();

        private void ApplyRandomSameType(string groupKey, List<FuseSaveCarFault> groupFaults)
        {
            // Hand the replacement consist to the game's
            // ConsistPlacer so the player clicks a track span to
            // place the cars — same flow Lost &amp; Found uses
            // when a saved track location is no longer valid.
            // Direct in-place spawning at the orphan's saved
            // location collides with surviving cars on the same
            // track and derails them.
            //
            // Close the orphan window during placement so the
            // ConsistPlacer's overlay isn't fighting our panel
            // for input focus; the completion callback re-shows
            // us if any orphans still need handling.
            if (_window != null && _window.IsShown)
            {
                _window.CloseWindow();
            }

            var presented = FuseSaveCarFaultReplacement.TryPresentReplacementsViaConsistPlacer(
                groupFaults,
                _replacementRng,
                OnPlacementComplete);

            if (!presented)
            {
                _lastAction =
                    $"Could not present replacement consist for type '{groupKey}': no compatible " +
                    $"replacement candidates were found, or ConsistPlacer is not available right now. " +
                    $"See FUSE.log for details.";
                FuseLog.Warning($"FUSE orphaned-car window: {_lastAction}");
                if (_window != null)
                {
                    OpenAndPopulate();
                }
            }
        }

        private void OnPlacementComplete(bool placed, IReadOnlyList<string> picks)
        {
            var pickSummary = (picks == null || picks.Count == 0)
                ? ""
                : " — picked: " + string.Join(", ", picks.Distinct());

            _lastAction = placed
                ? $"Placed replacement consist{pickSummary}."
                : $"Replacement placement cancelled — orphans still pending.";
            FuseLog.Info($"FUSE orphaned-car window: {_lastAction}");

            // Re-evaluate whether any orphans remain and show the
            // window again only when there's still work to do. This
            // mirrors LostCarPlacerWindow's ShowIfNeeded recursion.
            if (FuseSaveCarFaultRegistry.Count > 0)
            {
                OpenAndPopulate();
            }
        }

        private void OnDisable()
        {
            _panel?.Dispose();
            _panel = null;
        }
    }
}
