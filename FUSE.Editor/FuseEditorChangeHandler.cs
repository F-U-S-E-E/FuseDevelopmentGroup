using FUSE.Authoring.Data;
using FUSE.Editor.EditorHandler;
using FUSE.Loading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace FUSE.Editor
{
    public class FuseEditorChangeHandler : MonoBehaviour
    {
        public struct UndoEntry
        {
            public UndoEntry(EditorHandlerBase handler, object OldFuseData)
            {
                this.id = handler.ID;
                this.Entity = handler.Entity;
                this.OldFuseData = OldFuseData;
            }

            public string id;
            public object Entity;
            public object OldFuseData;
        }

        public struct RedoEntry
        {
            public RedoEntry(EditorHandlerBase handler, object NewFuseData)
            {
                this.id = handler.ID;
                this.Entity = handler.Entity;
                this.NewFuseData = NewFuseData;
            }
            public string id;
            public object Entity;
            public object NewFuseData;
        }

        public static FuseEditorChangeHandler Instance { get; private set; }

        List<EditorHandlerBase> queuedApplyHandlers = new List<EditorHandlerBase>();
        List<EditorHandlerBase> queuedSaveHandlers = new List<EditorHandlerBase>();
        List<UndoEntry> undoStack = new List<UndoEntry>();
        List<RedoEntry> redoStack = new List<RedoEntry>();

        void Start()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        public void ApplyChanges()
        {
            foreach (EditorHandlerBase handler in queuedApplyHandlers)
            {
                handler.ApplyData();
            }

            queuedApplyHandlers.Clear();
        }

        public void SaveChanges()
        {
            FuseModDefinition modDefinition = FuseEditor.Instance.ActiveMod.Definition;

            foreach (EditorHandlerBase handler in queuedSaveHandlers)
            {
                handler.SaveData(modDefinition);
            }

            FUSE.Authoring.Serialization.FuseSerializer.SaveJson(modDefinition, FuseEditor.Instance.ActiveMod.DefinitionPath);

            queuedSaveHandlers.Clear();
        }

        public void QueueChange(EditorHandlerBase handler, object OldFuseData)
        {
            // Implement logic to queue changes for later application
            undoStack.Add(new UndoEntry(handler, OldFuseData));

            if (!queuedApplyHandlers.Contains(handler))
            {
                queuedApplyHandlers.Add(handler);
            }
            if (!queuedSaveHandlers.Contains(handler))
            {
                queuedSaveHandlers.Add(handler);
            }
        }

        public bool TryGetQueuedChange(string id, Type EntityType, out EditorHandlerBase handler)
        {
            // Implement logic to retrieve queued changes for a specific handler
            foreach (EditorHandlerBase hand in queuedApplyHandlers)
            {
                if (hand.ID == id)
                {
                    handler = hand;
                    return true;
                }
            }

            foreach (EditorHandlerBase hand in queuedSaveHandlers)
            {
                if (hand.ID == id)
                {
                    handler = hand;
                    return true;
                }
            }

            handler = null;
            return false;
        }
    }
}
