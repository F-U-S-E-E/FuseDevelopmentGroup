using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FUSE.Authoring.Data;
using Model.Ops;
using Track;
using Model.Ops.Definition;
using Model;
using Game.Progression;
using Helpers;
using UI.Map;
using FUSE.Loading;
using FUSE.Authoring.Serialization;

namespace FUSE.Editor.Screen
{
    /// <summary>
    /// Interface for handling entity save and retrieval operations.
    /// Implementations should define how entities are persisted and retrieved.
    /// </summary>
    internal interface IEntityHandler
    {
        /// <summary>
        /// Saves an entity with the specified ID and object.
        /// </summary>
        /// <param name="entityId">The unique identifier for the entity</param>
        /// <param name="entityObject">The entity object (type/category)</param>
        /// <param name="currentMod">The current mod definition context</param>
        /// <returns>True if save was successful, false otherwise</returns>
        bool SaveEntity(string entityId, object entityObject, FuseLoadedMod currentMod);

        /// <summary>
        /// Applies an entity with the specified ID and object.
        /// </summary>
        /// <param name="entityId">The unique identifier for the entity</param>
        /// <param name="entityObject">The entity object (type/category)</param>
        /// <returns>True if apply was successful, false otherwise</returns>
        bool ApplyEntity(string entityId, object entityObject);

        /// <summary>
        /// Retrieves an entity by its ID and entity object.
        /// </summary>
        /// <param name="entityId">The unique identifier for the entity</param>
        /// <param name="entity">The entity object (type/category)</param>
        /// <returns>The entity data if found, null otherwise</returns>
        object GetEntity(string entityId, object entity);

        /// <summary>
        /// Checks if an entity handler supports the specified entity kind.
        /// </summary>
        /// <param name="entityKind">The entity kind to check</param>
        /// <returns>True if this handler can handle the given entity kind, false otherwise</returns>
        bool SupportsEntityObject(Type entityObjectType);

        bool SupportsEntity(Type entityType);
    }

    /// <summary>
    /// Default implementation of IEntityHandler that provides basic in-memory storage.
    /// Used as a fallback when no specific entity handler is registered.
    /// </summary>
    internal class DefaultEntityHandler : IEntityHandler
    {
        Type[] supportedEnityObjectTypes = new Type[] 
        { 
            // Tracks
            typeof(FuseNode), typeof(FuseSegment), typeof(FuseSpan), typeof(FuseArea),
            // Operations
            // typeof(FuseLoader),
            typeof(FuseLoad), typeof(FuseIndustry), typeof(FuseTurntable), typeof(FuseStation),
            // Progression
            // typeof(FuseSection),
            typeof(FuseProgression), typeof(FuseMapFeature),
            // World
            // typeof(FuseSpliney), typeof(FuseTelegraphPoles), typeof(FuseMapMask), typeof(FuseMapTileSource), typeof(FuseSceneClone)
            typeof(FuseScenery), typeof(FuseMapLabel)            
        };
        Type[] supportedEnityTypes = new Type[] 
        { 
            // Tracks
            typeof(TrackNode), typeof(TrackSegment), typeof(TrackSpan), typeof(Area),
            // Operations
            // typeof(Loader),
            typeof(Load), typeof(Industry), typeof(Turntable), typeof(StationAgent),
            // Progression
            // typeof(Section),
            typeof(Progression), typeof(MapFeature),
            // World
            // typeof(Spliney), typeof(TelegraphPoles), typeof(MapMask), typeof(MapTileSource), typeof(SceneClone)
            typeof(SceneryAssetInstance), typeof(MapLabel)            
        };

        /// <summary>
        /// Saves an entity to the in-memory cache.
        /// </summary>
        public bool SaveEntity(string entityId, object entityObject, FuseLoadedMod currentMod)
        {
            if (string.IsNullOrEmpty(entityId) || entityObject == null)
            {
                return false;
            }

            try
            {
                FuseModDefinition modDefinition = currentMod.Definition;

                Type type = entityObject.GetType();

                // Tracks
                if (type == typeof(FuseNode))
                    modDefinition.Tracks.Nodes[entityId] = (FuseNode)entityObject;
                else if (type == typeof(FuseSegment))
                    modDefinition.Tracks.Segments[entityId] = (FuseSegment)entityObject;
                else if (type == typeof(FuseSpan))
                    modDefinition.Tracks.Spans[entityId] = (FuseSpan)entityObject;
                else if (type == typeof(FuseArea))
                    modDefinition.Tracks.Areas[entityId] = (FuseArea)entityObject;
                // Operations
                else if (type == typeof(FuseLoad))
                    modDefinition.Operations.Loads[entityId] = (FuseLoad)entityObject;
                else if (type == typeof(FuseIndustry))
                    modDefinition.Operations.Industries[entityId] = (FuseIndustry)entityObject;
                /*
                else if (type == typeof(FuseLoader))
                    currentMod.Operations.Loaders[entityId] = (FuseLoader)entityObject;
                */
                else if (type == typeof(FuseTurntable))
                    modDefinition.Operations.Turntables[entityId] = (FuseTurntable)entityObject;
                else if (type == typeof(FuseStation))
                    modDefinition.Operations.Stations[entityId] = (FuseStation)entityObject;
                // Progression
                else if (type == typeof(FuseProgression))
                    modDefinition.Progression.Progressions[entityId] = (FuseProgression)entityObject;
                /*
                else if (type == typeof(FuseSection))
                    currentMod.Progression.Progressions.Values.FirstOrDefault()?.Sections[entityId] = (FuseSection)entityObject;
                */
                else if (type == typeof(FuseMapFeature))
                    modDefinition.Progression.MapFeatures[entityId] = (FuseMapFeature)entityObject;
                // World
                else if (type == typeof(FuseScenery))
                    modDefinition.World.Scenery[entityId] = (FuseScenery)entityObject;
                /*
                else if (type == typeof(FuseSpliney))
                    currentMod.World.Splineys[entityId] = (FuseSpliney)entityObject;
                else if (type == typeof(FuseTelegraphPoles))
                    currentMod.World.TelegraphPoles[entityId] = (FuseTelegraphPoles)entityObject;
                */
                else if (type == typeof(FuseMapLabel))
                    modDefinition.World.MapLabels[entityId] = (FuseMapLabel)entityObject;
                /*
                else if (type == typeof(FuseMapMask))
                    currentMod.World.MapMasks[entityId] = (FuseMapMask)entityObject;
                else if (type == typeof(FuseMapTileSource))
                    currentMod.World.MapTiles[entityId] = (FuseMapTileSource)entityObject;
                else if (type == typeof(FuseSceneClone))
                    currentMod.World.SceneClones[entityId] = (FuseSceneClone)entityObject;
                */
                else
                    return false;

                FuseSerializer.SaveJson(modDefinition, currentMod.DefinitionPath);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Retrieves an entity from the in-memory cache.
        /// </summary>
        public object GetEntity(string entityId, object entity)
        {
            if (string.IsNullOrEmpty(entityId) || entity == null)
            {
                return null;
            }

            Type type = entity.GetType();

            // Tracks
            if (type == typeof(TrackNode))
                return FUSE.Runtime.API.TrackAPI.GetDefinition((TrackNode)entity);
            if (type == typeof(TrackSegment))
                return FUSE.Runtime.API.TrackAPI.GetDefinition((TrackSegment)entity);
            if (type == typeof(TrackSpan))
                return FUSE.Runtime.API.TrackAPI.GetDefinition((TrackSpan)entity);
            if (type == typeof(Area))
                return FUSE.Runtime.API.TrackAPI.GetDefinition((Area)entity);
            // Operations
            if (type == typeof(Load))
                return FUSE.Runtime.API.LoadAPI.GetDefinition((Load)entity);
            if (type == typeof(Industry))
                return FUSE.Runtime.API.IndustryAPI.GetDefinition((Industry)entity);
            /*
            if (type == typeof(Loader))
                return FUSE.Runtime.API.LoaderAPI.GetDefinition((Loader)entity);
            */
            if (type == typeof(Turntable))
                return FUSE.Runtime.API.TurntableAPI.GetDefinition((Turntable)entity);
            if (type == typeof(StationAgent))
                return FUSE.Runtime.API.StationAPI.GetDefinition((StationAgent)entity);
            // Progression
            if (type == typeof(Progression))
                return FUSE.Runtime.API.ProgressionAPI.GetDefinition((Progression)entity);
            /*
            if (type == typeof(Section))
                return FUSE.Runtime.API.ProgressionAPI.GetDefinition((Section)entity);
            */
            if (type == typeof(MapFeature))
                return FUSE.Runtime.API.ProgressionAPI.GetDefinition((MapFeature)entity);
            // World
            if (type == typeof(SceneryAssetInstance))
                return FUSE.Runtime.API.SceneryAPI.GetDefinition((SceneryAssetInstance)entity);
            /*
            if (type == typeof(Spliney))
                return FUSE.Runtime.API.MapAPI.GetDefinition((Spliney)entity);
            if (type == typeof(TelegraphPoles))
                return FUSE.Runtime.API.MapAPI.GetDefinition((TelegraphPoles)entity);
            */
            if (type == typeof(MapLabel))
                return FUSE.Runtime.API.MapAPI.GetDefinition((MapLabel)entity);
            /*
            if (type == typeof(MapMask))
                return FUSE.Runtime.API.MapAPI.GetDefinition((MapMask)entity);
            if (type == typeof(MapTileSource))
                return FUSE.Runtime.API.MapAPI.GetDefinition((MapTileSource)entity);
            if (type == typeof(SceneClone))
                return FUSE.Runtime.API.MapAPI.GetDefinition((SceneClone)entity);
            */

            return null;
        }

        /// <summary>
        /// Supports all entity kinds by default.
        /// </summary>
        public bool SupportsEntityObject(Type entityObjectType)
        {
            return supportedEnityObjectTypes.Contains(entityObjectType);
        }

        public bool SupportsEntity(Type entityType)
        {
            return supportedEnityTypes.Contains(entityType);
        }

        /// <summary>
        /// Applies an entity from the in-memory cache.
        /// </summary>
        public bool ApplyEntity(string entityId, object entityObject)
        {
            if (string.IsNullOrEmpty(entityId) || entityObject == null)
            {
                return false;
            }

            try
            {
                Type type = entityObject.GetType();

                // Tracks
                if (type == typeof(FuseNode))
                    FUSE.Runtime.API.TrackAPI.UpdateNode(entityId, (FuseNode)entityObject);
                else if (type == typeof(FuseSegment))
                    FUSE.Runtime.API.TrackAPI.UpdateSegment(entityId, (FuseSegment)entityObject);
                else if (type == typeof(FuseSpan))
                    FUSE.Runtime.API.TrackAPI.UpdateSpan(entityId, (FuseSpan)entityObject);
                else if (type == typeof(FuseArea))
                    FUSE.Runtime.API.TrackAPI.UpdateArea(entityId, (FuseArea)entityObject);
                // Operations
                else if (type == typeof(FuseLoad))
                    FUSE.Runtime.API.LoadAPI.UpdateLoad(entityId, (FuseLoad)entityObject);
                else if (type == typeof(FuseIndustry))
                    FUSE.Runtime.API.IndustryAPI.UpdateIndustry(entityId, (FuseIndustry)entityObject);
                else if (type == typeof(FuseLoader))
                    FUSE.Runtime.API.LoaderAPI.UpdateLoader(entityId, (FuseLoader)entityObject);
                else if (type == typeof(FuseTurntable))
                    FUSE.Runtime.API.TurntableAPI.UpdateTurntable(entityId, (FuseTurntable)entityObject);
                else if (type == typeof(FuseStation))
                    FUSE.Runtime.API.StationAPI.UpdateStationAgent(entityId, (FuseStation)entityObject);
                // Progression
                else if (type == typeof(FuseProgression))
                    FUSE.Runtime.API.ProgressionAPI.UpdateProgression(entityId, (FuseProgression)entityObject);
                /*
                else if (type == typeof(FuseSection))
                    FUSE.Runtime.API.ProgressionAPI.UpdateSection(entityId, (FuseSection)entityObject);
                */
                else if (type == typeof(FuseMapFeature))
                    FUSE.Runtime.API.ProgressionAPI.UpdateMapFeature(entityId, (FuseMapFeature)entityObject);
                // World
                else if (type == typeof(FuseScenery))
                    FUSE.Runtime.API.SceneryAPI.UpdateScenery(entityId, (FuseScenery)entityObject);
                /*
                else if (type == typeof(FuseSpliney))
                    FUSE.Runtime.API.MapAPI.UpdateSpliney(entityId, (FuseSpliney)entityObject);
                else if (type == typeof(FuseTelegraphPoles))
                    FUSE.Runtime.API.MapAPI.UpdateTelegraphPoles(entityId, (FuseTelegraphPoles)entityObject);
                */
                else if (type == typeof(FuseMapLabel))
                    FUSE.Runtime.API.MapAPI.UpdateMapLabel(entityId, (FuseMapLabel)entityObject);
                /*
                else if (type == typeof(FuseMapMask))
                    FUSE.Runtime.API.MapAPI.UpdateMapMask(entityId, (FuseMapMask)entityObject);
                else if (type == typeof(FuseMapTileSource))
                    FUSE.Runtime.API.MapAPI.UpdateMapTileSource(entityId, (FuseMapTileSource)entityObject);
                else if (type == typeof(FuseSceneClone))
                    FUSE.Runtime.API.MapAPI.UpdateSceneClone(entityId, (FuseSceneClone)entityObject);
                */
                else
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Manages registration and retrieval of entity handlers.
    /// Uses a queue-based system for batching and throttling entity saves and applies.
    /// </summary>
    internal sealed class EntityHandlerRegistry
    {
        private readonly List<IEntityHandler> _handlers = new List<IEntityHandler>();
        private readonly IEntityHandler _defaultHandler;

        // Queue and throttling parameters
        private readonly Queue<SaveOperation> _saveQueue = new Queue<SaveOperation>();
        private readonly Queue<ApplyOperation> _applyQueue = new Queue<ApplyOperation>();
        private readonly int _maxItemsPerBatch = 10;
        private readonly int _throttleDelayMs = 3000; // 3 seconds
        private Task _processingTask;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly object _lockObject = new object();
        private bool _isProcessing = false;

        private class SaveOperation
        {
            public string EntityId { get; set; }
            public object EntityObject { get; set; }
            public FuseLoadedMod CurrentMod { get; set; }
        }

        private class ApplyOperation
        {
            public string EntityId { get; set; }
            public object EntityObject { get; set; }
        }

        public EntityHandlerRegistry()
        {
            _defaultHandler = new DefaultEntityHandler();
            _handlers.Add(_defaultHandler);
            _cancellationTokenSource = new CancellationTokenSource();
            StartProcessing();
        }

        private void StartProcessing()
        {
            lock (_lockObject)
            {
                if (_isProcessing)
                    return;

                _isProcessing = true;
                _cancellationTokenSource = new CancellationTokenSource();
                _processingTask = Task.Run(() => ProcessQueuesAsync(_cancellationTokenSource.Token));
            }
        }

        private async Task ProcessQueuesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Process save queue
                    lock (_lockObject)
                    {
                        int processed = 0;
                        while (_saveQueue.Count > 0 && processed < _maxItemsPerBatch && !cancellationToken.IsCancellationRequested)
                        {
                            var operation = _saveQueue.Dequeue();
                            ProcessSaveOperation(operation);
                            processed++;
                        }
                    }

                    // Process apply queue
                    lock (_lockObject)
                    {
                        int processed = 0;
                        while (_applyQueue.Count > 0 && processed < _maxItemsPerBatch && !cancellationToken.IsCancellationRequested)
                        {
                            var operation = _applyQueue.Dequeue();
                            ProcessApplyOperation(operation);
                            processed++;
                        }
                    }

                    // Wait before next batch
                    await Task.Delay(_throttleDelayMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Task was cancelled
                    break;
                }
                catch (Exception)
                {
                    // Log or handle exception, but continue processing
                    await Task.Delay(_throttleDelayMs, cancellationToken);
                }
            }
        }

        private void ProcessSaveOperation(SaveOperation operation)
        {
            if (operation?.EntityObject == null)
                return;

            Type objectType = operation.EntityObject.GetType();
            foreach (var handler in _handlers)
            {
                if (handler.SupportsEntityObject(objectType))
                {
                    handler.SaveEntity(operation.EntityId, operation.EntityObject, operation.CurrentMod);
                    break;
                }
            }
        }

        private void ProcessApplyOperation(ApplyOperation operation)
        {
            if (operation?.EntityObject == null)
                return;

            Type objectType = operation.EntityObject.GetType();
            foreach (var handler in _handlers)
            {
                if (handler.SupportsEntityObject(objectType))
                {
                    handler.ApplyEntity(operation.EntityId, operation.EntityObject);
                    break;
                }
            }
        }

        /// <summary>
        /// Registers a new entity handler. Handlers are queried in reverse registration order
        /// (most recently registered handlers are checked first).
        /// </summary>
        /// <param name="handler">The entity handler to register</param>
        public void RegisterHandler(IEntityHandler handler)
        {
            if (handler != null && handler != _defaultHandler)
            {
                _handlers.Insert(0, handler); // Insert at beginning for priority
            }
        }

        /// <summary>
        /// Unregisters an entity handler, if it exists.
        /// </summary>
        /// <param name="handler">The entity handler to unregister</param>
        /// <returns>True if the handler was found and removed, false otherwise</returns>
        public bool UnregisterHandler(IEntityHandler handler)
        {
            return handler != null && handler != _defaultHandler && _handlers.Remove(handler);
        }

        /// <summary>
        /// Queues an entity for saving. The entity will be saved asynchronously in batches.
        /// </summary>
        /// <param name="entityId">The unique identifier for the entity</param>
        /// <param name="entityObject">The entity object</param>
        /// <param name="currentMod">The current mod definition context</param>
        /// <returns>True if the operation was queued successfully</returns>
        public bool SaveEntity(string entityId, object entityObject, FuseLoadedMod currentMod)
        {
            if (string.IsNullOrEmpty(entityId) || entityObject == null)
                return false;

            lock (_lockObject)
            {
                _saveQueue.Enqueue(new SaveOperation
                {
                    EntityId = entityId,
                    EntityObject = entityObject,
                    CurrentMod = currentMod
                });
            }

            return true;
        }

        /// <summary>
        /// Retrieves an entity using the appropriate registered handler.
        /// </summary>
        /// <param name="entityId">The unique identifier for the entity</param>
        /// <param name="entity">The entity object</param>
        /// <returns>The entity data if found, null otherwise</returns>
        public object GetEntity(string entityId, object entity)
        {
            if (entity == null)
                return null;

            Type entityType = entity.GetType();
            foreach (var handler in _handlers)
            {
                if (handler.SupportsEntity(entityType))
                {
                    return handler.GetEntity(entityId, entity);
                }
            }

            return null;
        }

        /// <summary>
        /// Queues an entity for applying. The entity will be applied asynchronously in batches.
        /// </summary>
        /// <param name="entityId">The unique identifier for the entity</param>
        /// <param name="entityObject">The entity object</param>
        /// <returns>True if the operation was queued successfully</returns>
        public bool ApplyEntity(string entityId, object entityObject)
        {
            if (string.IsNullOrEmpty(entityId) || entityObject == null)
                return false;

            lock (_lockObject)
            {
                _applyQueue.Enqueue(new ApplyOperation
                {
                    EntityId = entityId,
                    EntityObject = entityObject
                });
            }

            return true;
        }

        /// <summary>
        /// Clears all registered handlers except the default one.
        /// </summary>
        public void ClearCustomHandlers()
        {
            _handlers.Clear();
            _handlers.Add(_defaultHandler);
        }

        /// <summary>
        /// Stops the queue processing and cleans up resources.
        /// </summary>
        public void Shutdown()
        {
            lock (_lockObject)
            {
                _isProcessing = false;
                _cancellationTokenSource?.Cancel();
            }

            try
            {
                _processingTask?.Wait(5000); // Wait up to 5 seconds for graceful shutdown
            }
            catch { }

            _cancellationTokenSource?.Dispose();
        }

        /// <summary>
        /// Gets the number of pending save operations.
        /// </summary>
        public int PendingSaveCount
        {
            get
            {
                lock (_lockObject)
                {
                    return _saveQueue.Count;
                }
            }
        }

        /// <summary>
        /// Gets the number of pending apply operations.
        /// </summary>
        public int PendingApplyCount
        {
            get
            {
                lock (_lockObject)
                {
                    return _applyQueue.Count;
                }
            }
        }
    }

    /// <summary>
    /// Manages the entity selection state for the FuseEditor. Supports both
    /// single and multi-selection with modifier key handling (Ctrl for toggle,
    /// Shift for range/add). Provides a clean interface for querying and
    /// modifying the current selection.
    /// </summary>
    internal sealed class FuseEditorEntitySelection
    {
        private readonly List<object> _selectedEntityObjects = new List<object>();
        private readonly List<string> _selectedEntityIds = new List<string>();

        public EntityHandlerRegistry EntityHandler = new EntityHandlerRegistry();

        /// <summary>
        /// Gets the underlying list of entity kinds currently selected.
        /// Suitable for passing to APIs that expect List&lt;object&gt;.
        /// </summary>
        public List<object> SelectedObjects => _selectedEntityObjects;

        /// <summary>
        /// Gets the underlying list of entity IDs currently selected.
        /// Suitable for passing to APIs that expect List&lt;string&gt;.
        /// </summary>
        public List<string> SelectedIds => _selectedEntityIds;

        /// <summary>
        /// Gets the count of currently selected entities.
        /// </summary>
        public int SelectionCount => _selectedEntityIds.Count;

        /// <summary>
        /// Gets the primary (first) selected entity kind.
        /// Returns null if no selection exists.
        /// </summary>
        public object PrimaryObject => _selectedEntityObjects.Count > 0 ? _selectedEntityObjects[0] : null;

        /// <summary>
        /// Gets the primary (first) selected entity ID.
        /// Returns null if no selection exists.
        /// </summary>
        public string PrimaryId => _selectedEntityIds.Count > 0 ? _selectedEntityIds[0] : null;

        /// <summary>
        /// Replaces the current selection with a single entity.
        /// </summary>
        /// <param name="entityKind">The entity kind (can be any object type)</param>
        /// <param name="entityId">The entity ID to select</param>
        public void SetSelectedEntity(object entityKind, string entityId)
        {
            ClearSelection();
            AddToSelection(entityKind, entityId);
        }

        /// <summary>
        /// Replaces the current selection with multiple entities.
        /// Clears any existing selections.
        /// </summary>
        /// <param name="entityObjects">List of entity kinds</param>
        /// <param name="entityIds">List of entity IDs (must match length of entityKinds)</param>
        public void SetSelectedEntities(IList<object> entityObjects, IList<string> entityIds)
        {
            if (entityObjects == null || entityIds == null || entityObjects.Count != entityIds.Count)
            {
                return;
            }

            ClearSelection();
            for (int i = 0; i < entityObjects.Count; i++)
            {
                AddToSelection(entityObjects[i], entityIds[i]);
            }
        }

        /// <summary>
        /// Adds an entity to the current selection without clearing existing selections.
        /// </summary>
        /// <param name="entityObject">The entity kind (can be any object type)</param>
        /// <param name="entityId">The entity ID to add</param>
        public void AddToSelection(object entityObject, string entityId)
        {
            if (string.IsNullOrEmpty(entityId))
            {
                return;
            }

            // Avoid duplicates
            for (int i = 0; i < _selectedEntityIds.Count; i++)
            {
                if (string.Equals(_selectedEntityIds[i], entityId, StringComparison.Ordinal) &&
                    Equals(_selectedEntityObjects[i], entityObject))
                {
                    return; // Already selected
                }
            }

            _selectedEntityObjects.Add(entityObject);
            _selectedEntityIds.Add(entityId);
        }

        /// <summary>
        /// Adds multiple entities to the current selection without clearing existing selections.
        /// </summary>
        /// <param name="entityObjects">List of entity kinds to add</param>
        /// <param name="entityIds">List of entity IDs to add (must match length of entityKinds)</param>
        public void AddToSelection(IList<object> entityObjects, IList<string> entityIds)
        {
            if (entityObjects == null || entityIds == null)
            {
                return;
            }

            if (entityObjects.Count != entityIds.Count)
            {
                return;
            }

            for (int i = 0; i < entityObjects.Count; i++)
            {
                AddToSelection(entityObjects[i], entityIds[i]);
            }
        }

        /// <summary>
        /// Removes an entity from the current selection.
        /// </summary>
        /// <param name="entityObject">The entity kind</param>
        /// <param name="entityId">The entity ID</param>
        /// <returns>True if the entity was found and removed, false otherwise</returns>
        public bool RemoveFromSelection(object entityObject, string entityId)
        {
            for (int i = 0; i < _selectedEntityIds.Count; i++)
            {
                if (string.Equals(_selectedEntityIds[i], entityId, StringComparison.Ordinal) &&
                    Equals(_selectedEntityObjects[i], entityObject))
                {
                    _selectedEntityIds.RemoveAt(i);
                    _selectedEntityObjects.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes multiple entities from the current selection.
        /// </summary>
        /// <param name="entityObjects">List of entity kinds to remove</param>
        /// <param name="entityIds">List of entity IDs to remove (must match length of entityKinds)</param>
        /// <returns>The number of entities that were successfully removed</returns>
        public int RemoveFromSelection(IList<object> entityObjects, IList<string> entityIds)
        {
            if (entityObjects == null || entityIds == null)
            {
                return 0;
            }

            if (entityObjects.Count != entityIds.Count)
            {
                return 0;
            }

            int removedCount = 0;
            for (int i = 0; i < entityObjects.Count; i++)
            {
                if (RemoveFromSelection(entityObjects[i], entityIds[i]))
                {
                    removedCount++;
                }
            }
            return removedCount;
        }

        /// <summary>
        /// Toggles the selection state of an entity. If selected, removes it;
        /// if not selected, adds it to the selection.
        /// </summary>
        /// <param name="entityObject">The entity kind</param>
        /// <param name="entityId">The entity ID</param>
        /// <returns>True if the entity is now selected, false if it was deselected</returns>
        public bool ToggleSelection(object entityObject, string entityId)
        {
            if (RemoveFromSelection(entityObject, entityId))
            {
                return false; // Was selected, now removed
            }
            else
            {
                AddToSelection(entityObject, entityId);
                return true; // Was not selected, now added
            }
        }

        /// <summary>
        /// Removes all entities from the current selection.
        /// </summary>
        public void ClearSelection()
        {
            _selectedEntityObjects.Clear();
            _selectedEntityIds.Clear();
        }

        /// <summary>
        /// Checks if a specific entity is currently selected.
        /// </summary>
        /// <param name="entityObject">The entity kind</param>
        /// <param name="entityId">The entity ID</param>
        /// <returns>True if the entity is selected, false otherwise</returns>
        public bool IsEntitySelected(object entityObject, string entityId)
        {
            for (int i = 0; i < _selectedEntityIds.Count; i++)
            {
                if (string.Equals(_selectedEntityIds[i], entityId, StringComparison.Ordinal) &&
                    Equals(_selectedEntityObjects[i], entityObject))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if any entities are currently selected.
        /// </summary>
        /// <returns>True if at least one entity is selected, false if the selection is empty</returns>
        public bool HasSelection => _selectedEntityIds.Count > 0;
    }
}
