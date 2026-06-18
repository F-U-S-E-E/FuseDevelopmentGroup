using System;
using System.Collections.Generic;

namespace Fuse.Core.Authoring
{
    /// <summary>A reversible editor action (do/undo pair) with a display label.</summary>
    public sealed class UndoAction
    {
        public UndoAction(string label, Action redo, Action undo)
        {
            Label = label;
            Redo = redo;
            Undo = undo;
        }

        public string Label { get; }
        public Action Redo { get; }
        public Action Undo { get; }
    }

    /// <summary>
    /// Minimal undo/redo stack. <see cref="Execute"/> performs the action and
    /// records it; a new action clears the redo stack. Unity-free and reusable.
    /// </summary>
    public sealed class UndoService
    {
        private readonly Stack<UndoAction> _undo = new Stack<UndoAction>();
        private readonly Stack<UndoAction> _redo = new Stack<UndoAction>();

        /// <summary>Raised after any change to the undo/redo stacks (so UI can refresh command state).</summary>
        public event Action Changed;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public int UndoDepth => _undo.Count;
        public int RedoDepth => _redo.Count;

        public string NextUndoLabel => _undo.Count > 0 ? _undo.Peek().Label : null;
        public string NextRedoLabel => _redo.Count > 0 ? _redo.Peek().Label : null;

        public void Execute(UndoAction action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            action.Redo();
            _undo.Push(action);
            _redo.Clear();
            Changed?.Invoke();
        }

        public void Undo()
        {
            if (_undo.Count == 0)
            {
                return;
            }

            var action = _undo.Pop();
            action.Undo();
            _redo.Push(action);
            Changed?.Invoke();
        }

        public void Redo()
        {
            if (_redo.Count == 0)
            {
                return;
            }

            var action = _redo.Pop();
            action.Redo();
            _undo.Push(action);
            Changed?.Invoke();
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            Changed?.Invoke();
        }
    }
}
