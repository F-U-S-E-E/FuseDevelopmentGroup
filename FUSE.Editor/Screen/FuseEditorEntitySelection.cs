using FUSE.Authoring.Data;
using FUSE.Authoring.Serialization;
using FUSE.Editor.EditorHandler;
using FUSE.Editor.Overlays;
using FUSE.Infrastructure;
using FUSE.Loading;
using Game.Progression;
using Helpers;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Track;
using UI.Map;
using UnityEngine.InputSystem;
using EditorHandlerBase = FUSE.Editor.EditorHandler.EditorHandlerBase;

namespace FUSE.Editor.Screen
{
    /// <summary>
    /// Manages the entity selection state for the FuseEditor. Supports both
    /// single and multi-selection with modifier key handling (Ctrl for toggle,
    /// Shift for range/add). Provides a clean interface for querying and
    /// modifying the current selection.
    /// 
    /// Uses EditorHandler instances as the primary selection storage, with
    /// backward-compatible properties that extract entity objects and IDs.
    /// </summary>
    internal sealed class FuseEditorEntitySelection
    {
        private readonly List<EditorHandlerBase> _selectedHandlers = new List<EditorHandlerBase>();

        /// <summary>
        /// Gets the underlying list of selected EditorHandler instances.
        /// </summary>
        public List<EditorHandlerBase> SelectedHandlers => _selectedHandlers;

        /// <summary>
        /// Gets the underlying list of entity kinds currently selected.
        /// Suitable for passing to APIs that expect List&lt;object&gt;.
        /// Extracts entity objects from selected handlers.
        /// </summary>
        public List<object> SelectedObjects => _selectedHandlers.Select(h => h.Entity).ToList();

        /// <summary>
        /// Gets the underlying list of entity IDs currently selected.
        /// Suitable for passing to APIs that expect List&lt;string&gt;.
        /// Extracts IDs from selected handlers.
        /// </summary>
        public List<string> SelectedIds => _selectedHandlers.Select(h => h.ID).ToList();

        /// <summary>
        /// Gets the count of currently selected entities.
        /// </summary>
        public int SelectionCount => _selectedHandlers.Count;

        /// <summary>
        /// Gets the primary (first) selected EditorHandler.
        /// Returns null if no selection exists.
        /// </summary>
        public EditorHandlerBase PrimaryHandler => _selectedHandlers.Count > 0 ? _selectedHandlers[0] : null;

        /// <summary>
        /// Gets the primary (first) selected entity kind.
        /// Returns null if no selection exists.
        /// </summary>
        public object PrimaryObject => _selectedHandlers.Count > 0 ? _selectedHandlers[0].Entity : null;

        /// <summary>
        /// Gets the primary (first) selected entity ID.
        /// Returns null if no selection exists.
        /// </summary>
        public string PrimaryId => _selectedHandlers.Count > 0 ? _selectedHandlers[0].ID : null;

        /// <summary>
        /// Replaces the current selection with a single handler.
        /// </summary>
        /// <param name="handler">The EditorHandler to select</param>
        ///

        public Type SelectionRequestType;

        private EditorHandlerBase requestedSelection;

        private bool canSelect = true;

        public void SetSelectedHandler(EditorHandlerBase handler)
        {
            ClearSelection();
            if (handler != null)
            {
                AddToSelection(handler);
            }
        }

        /// <summary>
        /// Replaces the current selection with multiple handlers.
        /// Clears any existing selections.
        /// </summary>
        /// <param name="handlers">List of EditorHandlers to select</param>
        public void SetSelectedHandlers(IList<EditorHandlerBase> handlers)
        {
            if (handlers == null)
            {
                return;
            }

            ClearSelection();
            foreach (var handler in handlers)
            {
                if (handler != null)
                {
                    AddToSelection(handler);
                }
            }
        }

        /// <summary>
        /// Adds an EditorHandler to the current selection without clearing existing selections.
        /// </summary>
        /// <param name="handler">The EditorHandler to add</param>
        public void AddToSelection(EditorHandlerBase handler)
        {
            if (handler == null)
            {
                return;
            }

            // Avoid duplicates using Equals()
            foreach (var existingHandler in _selectedHandlers)
            {
                if (existingHandler.Equals(handler))
                {
                    return; // Already selected
                }
            }

            _selectedHandlers.Add(handler);
        }

        /// <summary>
        /// Adds multiple EditorHandlers to the current selection without clearing existing selections.
        /// </summary>
        /// <param name="handlers">List of EditorHandlers to add</param>
        public void AddToSelection(IList<EditorHandlerBase> handlers)
        {
            if (handlers == null)
            {
                return;
            }

            foreach (var handler in handlers)
            {
                AddToSelection(handler);
            }
        }

        /// <summary>
        /// Removes an EditorHandler from the current selection.
        /// </summary>
        /// <param name="handler">The EditorHandler to remove</param>
        /// <returns>True if the handler was found and removed, false otherwise</returns>
        public bool RemoveFromSelection(EditorHandlerBase handler)
        {
            if (handler == null)
            {
                return false;
            }

            for (int i = 0; i < _selectedHandlers.Count; i++)
            {
                if (_selectedHandlers[i].Equals(handler))
                {
                    _selectedHandlers.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes multiple EditorHandlers from the current selection.
        /// </summary>
        /// <param name="handlers">List of EditorHandlers to remove</param>
        /// <returns>The number of handlers that were successfully removed</returns>
        public int RemoveFromSelection(IList<EditorHandlerBase> handlers)
        {
            if (handlers == null)
            {
                return 0;
            }

            int removedCount = 0;
            foreach (var handler in handlers)
            {
                if (RemoveFromSelection(handler))
                {
                    removedCount++;
                }
            }
            return removedCount;
        }

        /// <summary>
        /// Toggles the selection state of an EditorHandler. If selected, removes it;
        /// if not selected, adds it to the selection.
        /// </summary>
        /// <param name="handler">The EditorHandler to toggle</param>
        /// <returns>True if the handler is now selected, false if it was deselected</returns>
        public bool ToggleSelection(EditorHandlerBase handler)
        {
            if (handler == null)
            {
                return false;
            }

            if (RemoveFromSelection(handler))
            {
                return false; // Was selected, now removed
            }
            else
            {
                AddToSelection(handler);
                return true; // Was not selected, now added
            }
        }

        /// <summary>
        /// Removes all entities from the current selection.
        /// </summary>
        public void ClearSelection()
        {
            _selectedHandlers.Clear();
        }

        /// <summary>
        /// Checks if a specific EditorHandler is currently selected.
        /// </summary>
        /// <param name="handler">The EditorHandler to check</param>
        /// <returns>True if the handler is selected, false otherwise</returns>
        public bool IsHandlerSelected(EditorHandlerBase handler)
        {
            if (handler == null)
            {
                return false;
            }

            foreach (var selectedHandler in _selectedHandlers)
            {
                if (selectedHandler.Equals(handler))
                {
                    return true;
                }
            }
            return false;
        }

        public void OnSelected(EditorHandlerBase handler)
        {
            if (!canSelect)
            {
                return;
            }
            if (SelectionRequestType != null && handler.Entity.GetType() == SelectionRequestType)
            {
                requestedSelection = handler;
            }
            else
            {
                if (Keyboard.current.shiftKey.isPressed)
                {
                    FuseEditor.Instance.EntitySelection.ToggleSelection(handler);
                }
                else
                {
                    FuseEditor.Instance.EntitySelection.ClearSelection();
                    FuseEditor.Instance.EntitySelection.AddToSelection(handler);
                }
            }
        }

        public bool RequestSelection(Type type)
        {
            if (type == null || SelectionRequestType != null)
            {
                return false;
            }
            SelectionRequestType = type;
            return true;
        }

        public bool TryGetRequestedSelection(Type selectionType, out EditorHandlerBase handler)
        {
            if (SelectionRequestType != null && SelectionRequestType == selectionType)
            {
                if (requestedSelection != null)
                {
                    handler = requestedSelection;
                    requestedSelection = null;
                    SelectionRequestType = null;
                    return true;
                }
                else
                {
                    handler = null;
                    return false;
                }
            }
            else
            {
                handler = null;
                return false;
            }
        }

        public void BlockSelecting()
        {
            canSelect = false;
        }

        public void AllowSelecting()
        {
            canSelect = true;
        }

        /// <summary>
        /// Checks if any entities are currently selected.
        /// </summary>
        /// <returns>True if at least one entity is selected, false if the selection is empty</returns>
        public bool HasSelection => _selectedHandlers.Count > 0;
    }
}
