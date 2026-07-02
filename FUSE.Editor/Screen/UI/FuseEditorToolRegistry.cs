using System;
using System.Collections.Generic;
using FUSE.Infrastructure;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Holds the live list of <see cref="IFuseEditorTool"/> instances
    /// plus which one is currently active. The viewport tool strip
    /// iterates this at render time so adding a tool means writing one
    /// implementation and calling <see cref="Register"/> at editor-enter
    /// time — no UI code changes.
    /// </summary>
    /// <remarks>
    /// Modelled after Axiom's <c>ToolManager</c> with the same semantics:
    /// activation fires <see cref="IFuseEditorTool.OnActivate"/> on the
    /// new tool and <see cref="IFuseEditorTool.OnDeactivate"/> on the
    /// previous one. Re-activating the already-active tool is a no-op so
    /// repeated button clicks don't tear down + respawn gizmos.
    ///
    /// Registry state is static, mirroring the singleton-per-session
    /// shape of the rest of the editor. xUnit tests serialise through
    /// <c>FuseEditorRegistryTestCollection</c> and call <see cref="Reset"/>
    /// in their setup.
    /// </remarks>
    internal static class FuseEditorToolRegistry
    {
        private static readonly List<IFuseEditorTool> Tools = new List<IFuseEditorTool>();
        private static IFuseEditorTool _active;

        /// <summary>
        /// All registered tools in registration order. The toolbar relies
        /// on this order being stable across calls so button positions
        /// don't shuffle between frames.
        /// </summary>
        public static IReadOnlyList<IFuseEditorTool> All => Tools;

        /// <summary>The current active tool, or <c>null</c> if none.</summary>
        public static IFuseEditorTool Active => _active;

        public static bool IsActive(IFuseEditorTool tool)
        {
            return tool != null && ReferenceEquals(_active, tool);
        }

        /// <summary>
        /// Looks up a registered tool by <see cref="IFuseEditorTool.Id"/>.
        /// Returns <c>null</c> when nothing matches — callers that
        /// dispatch tool changes by id (toolbar buttons, menu items)
        /// should treat null as "tool not registered yet" and skip
        /// the activation rather than throwing.
        /// </summary>
        public static IFuseEditorTool FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Tools.Count; i++)
            {
                if (string.Equals(Tools[i].Id, id, StringComparison.Ordinal))
                {
                    return Tools[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Adds <paramref name="tool"/> to the registry. Duplicate
        /// registrations (same <see cref="IFuseEditorTool.Id"/>) are
        /// dropped so an Enter → Exit → re-Enter cycle that re-registers
        /// the same set doesn't accumulate.
        /// </summary>
        public static void Register(IFuseEditorTool tool)
        {
            if (tool == null)
            {
                return;
            }

            for (int i = 0; i < Tools.Count; i++)
            {
                if (string.Equals(Tools[i].Id, tool.Id, StringComparison.Ordinal))
                {
                    return;
                }
            }

            Tools.Add(tool);
        }

        /// <summary>
        /// Transitions the active slot. Fires
        /// <see cref="IFuseEditorTool.OnDeactivate"/> on the outgoing
        /// tool and <see cref="IFuseEditorTool.OnActivate"/> on the
        /// incoming one. Re-activating the current tool is a no-op.
        /// Setting an unregistered tool registers it first.
        /// </summary>
        public static void SetActive(IFuseEditorTool tool)
        {
            if (ReferenceEquals(_active, tool))
            {
                return;
            }

            if (tool != null)
            {
                Register(tool); // idempotent
            }

            var previous = _active;
            _active = tool;

            if (previous != null)
            {
                try
                {
                    previous.OnDeactivate();
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE editor tool '{previous.Id}' OnDeactivate threw.", ex);
                }
            }

            if (_active != null)
            {
                try
                {
                    _active.OnActivate();
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE editor tool '{_active.Id}' OnActivate threw.", ex);
                }
            }
        }

        /// <summary>
        /// Drops out of the active slot, firing
        /// <see cref="IFuseEditorTool.OnDeactivate"/> on the outgoing tool.
        /// </summary>
        public static void Deactivate()
        {
            SetActive(null);
        }

        /// <summary>
        /// Empties the registry. Used by editor exit + tests. Fires
        /// <see cref="IFuseEditorTool.OnDeactivate"/> on the active tool
        /// first so it can release any resources before its instance
        /// goes out of scope.
        /// </summary>
        public static void Reset()
        {
            Deactivate();
            Tools.Clear();
        }

        /// <summary>
        /// Forwards a per-frame tick to the active tool. Called by
        /// <see cref="FuseEditor"/>'s <c>Update</c>. Inactive tools never
        /// tick — they have no scene presence and shouldn't observe
        /// input. Exceptions are caught and logged so a buggy tool
        /// doesn't break the entire editor frame.
        /// </summary>
        public static void TickActive()
        {
            if (_active == null)
            {
                return;
            }

            try
            {
                _active.Tick();
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor tool '{_active.Id}' Tick threw.", ex);
            }
        }

        public static void DrawActive()
        {
            if (_active == null)
            {
                return;
            }
            try
            {
                _active.Draw();
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor tool '{_active.Id}' Draw threw.", ex);
            }
        }
    }
}
