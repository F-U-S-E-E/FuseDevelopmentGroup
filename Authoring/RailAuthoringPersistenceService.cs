using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RAIL.Data;
using RAIL.Editor;
using RAIL.Infrastructure;
using RAIL.Loading;
using RAIL.Serialization;
using RAIL.Validation;
using UnityEngine;

namespace RAIL.Authoring
{
    public static class RailAuthoringPersistenceService
    {
        private static readonly Dictionary<string, JObject> SavedEntitySnapshots =
            new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> AutosaveQueue =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, QueuedDefinitionSave> DefinitionAutosaveQueue =
            new Dictionary<string, QueuedDefinitionSave>(StringComparer.OrdinalIgnoreCase);

        public static JObject SaveEntity(RailAuthoringEntity entity)
        {
            var resolved = RequireEntity(entity);
            var validation = resolved.Validate();
            NotifyValidation(resolved, validation);
            if (!validation.IsValid)
            {
                RailLog.Warning($"RAIL authoring entity '{resolved.Id}' saved with {validation.Errors.Count} validation error(s).");
            }

            var data = resolved.SaveAuthoringData();
            SavedEntitySnapshots[resolved.Id] = (JObject)data.DeepClone();
            var persistedToDefinition = SaveEntityToOwningDefinition(resolved);
            if (persistedToDefinition)
            {
                resolved.ClearDirty();
                resolved.ClearAutosaveQueued();
                AutosaveQueue.Remove(resolved.Id);
            }

            RailAuthoringRegistry.Register(resolved);
            RailLog.Info($"RAIL authoring saved entity '{resolved.Id}' kind='{resolved.EntityKind}'.");
            return data;
        }

        public static JObject SaveFromGameObject(GameObject gameObject)
        {
            return SaveEntity(ResolveEntity(gameObject));
        }

        public static JObject SaveFromComponent(Component component)
        {
            return SaveEntity(ResolveEntity(component));
        }

        public static JObject SaveEntity(GameObject gameObject)
        {
            return SaveFromGameObject(gameObject);
        }

        public static JObject SaveEntity(Component component)
        {
            return SaveFromComponent(component);
        }

        public static void LoadEntity(RailAuthoringEntity entity, JObject data)
        {
            var resolved = RequireEntity(entity);
            resolved.LoadAuthoringData(data);
            RailAuthoringRegistry.Register(resolved);
            SavedEntitySnapshots[resolved.Id] = data != null ? (JObject)data.DeepClone() : new JObject();
            RailLog.Info($"RAIL authoring loaded entity '{resolved.Id}' kind='{resolved.EntityKind}'.");
        }

        public static void LoadEntity(RailAuthoringEntity entity, string json)
        {
            LoadEntity(entity, string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json));
        }

        public static void MarkDirty(RailAuthoringEntity entity)
        {
            RequireEntity(entity).MarkDirty("marked dirty by editor");
        }

        public static void MarkDirty(RailAuthoringEntity entity, string reason, bool queueAutosave = false)
        {
            RequireEntity(entity).MarkDirty(reason, queueAutosave);
        }

        public static void MarkDirty(GameObject gameObject)
        {
            MarkDirty(ResolveEntity(gameObject));
        }

        public static void MarkDirty(Component component)
        {
            MarkDirty(ResolveEntity(component));
        }

        public static int SaveDirtyEntities()
        {
            var savedCount = 0;
            foreach (var entity in RailAuthoringRegistry.AllEntities.ToArray())
            {
                if (entity == null || !entity.IsDirty)
                {
                    continue;
                }

                try
                {
                    SaveEntity(entity);
                    savedCount++;
                }
                catch (Exception ex)
                {
                    RailLog.Exception($"RAIL authoring failed to save dirty entity '{entity.Id}'", ex);
                }
            }

            RailLog.Info($"RAIL authoring saved {savedCount} dirty entity/entities.");
            return savedCount;
        }

        public static void QueueAutosave(RailAuthoringEntity entity, string reason = null)
        {
            var resolved = RequireEntity(entity);
            if (string.IsNullOrWhiteSpace(resolved.Id))
            {
                return;
            }

            AutosaveQueue.Add(resolved.Id);
            RailAuthoringRegistry.Register(resolved);
            RailLog.Info($"RAIL authoring queued autosave for entity '{resolved.Id}' reason='{reason ?? string.Empty}'.");
        }

        public static int SaveQueuedAutosaves()
        {
            var entitySavedCount = SaveQueuedEntityAutosaves();
            var definitionSavedCount = SaveQueuedDefinitionAutosaves();
            RailLog.Info($"RAIL authoring saved {entitySavedCount} entity autosave(s) and {definitionSavedCount} definition autosave(s).");
            return entitySavedCount + definitionSavedCount;
        }

        private static int SaveQueuedEntityAutosaves()
        {
            var queuedIds = AutosaveQueue.ToArray();
            var savedCount = 0;
            foreach (var id in queuedIds)
            {
                if (!RailAuthoringRegistry.TryGet(id, out var entity) || entity == null)
                {
                    AutosaveQueue.Remove(id);
                    continue;
                }

                if (!entity.IsDirty)
                {
                    entity.ClearAutosaveQueued();
                    AutosaveQueue.Remove(id);
                    continue;
                }

                try
                {
                    SaveEntity(entity);
                    savedCount++;
                }
                catch (Exception ex)
                {
                    RailLog.Exception($"RAIL authoring autosave failed for entity '{id}'", ex);
                }
            }

            RailLog.Info($"RAIL authoring saved {savedCount} queued autosave entity/entities.");
            return savedCount;
        }

        public static bool SaveDefinitionObject(string packageId, string kind, string objectId, object definition, string reason = null)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                RailLog.Warning($"RAIL authoring definition save skipped '{kind}' '{objectId}' because package id was empty.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(objectId))
            {
                RailLog.Warning($"RAIL authoring definition save skipped '{kind}' because object id was empty.");
                return false;
            }

            if (definition == null)
            {
                RailLog.Warning($"RAIL authoring definition save skipped '{kind}' '{objectId}' because definition was null.");
                return false;
            }

            if (!RailModLoader.TryGetLoadedMod(packageId, out var loaded) || loaded?.Definition == null)
            {
                RailLog.Warning($"RAIL authoring definition save skipped '{kind}' '{objectId}' because package '{packageId}' is not loaded.");
                return false;
            }

            if (!TryApplyDefinitionObject(loaded.Definition, kind, objectId, definition))
            {
                RailLog.Warning($"RAIL authoring definition save skipped unsupported kind='{kind}' id='{objectId}' type='{definition.GetType().FullName}'.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(loaded.DefinitionPath))
            {
                RailLog.Warning($"RAIL authoring updated definition '{loaded.Definition.Id}' in memory for '{kind}' '{objectId}', but no definition path is available for disk write.");
                return false;
            }

            SaveDefinitionToPath(loaded.Definition, loaded.DefinitionPath);
            RailLog.Info($"RAIL authoring saved '{kind}' '{objectId}' to package '{loaded.Definition.Id}' reason='{reason ?? string.Empty}'.");
            return true;
        }

        public static void QueueDefinitionAutosave(string packageId, string kind, string objectId, object definition, string reason = null)
        {
            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(objectId) || definition == null)
            {
                return;
            }

            var key = packageId.Trim() + "\n" + kind.Trim() + "\n" + objectId.Trim();
            DefinitionAutosaveQueue[key] = new QueuedDefinitionSave
            {
                PackageId = packageId,
                Kind = kind,
                ObjectId = objectId,
                Definition = definition,
                Reason = reason
            };
            RailLog.Info($"RAIL authoring queued definition autosave kind='{kind}' id='{objectId}' package='{packageId}' reason='{reason ?? string.Empty}'.");
        }

        public static int SaveQueuedDefinitionAutosaves()
        {
            var queued = DefinitionAutosaveQueue.ToArray();
            var savedCount = 0;
            foreach (var pair in queued)
            {
                var save = pair.Value;
                if (save == null)
                {
                    DefinitionAutosaveQueue.Remove(pair.Key);
                    continue;
                }

                try
                {
                    if (SaveDefinitionObject(save.PackageId, save.Kind, save.ObjectId, save.Definition, save.Reason ?? "definition autosave"))
                    {
                        savedCount++;
                        DefinitionAutosaveQueue.Remove(pair.Key);
                    }
                }
                catch (Exception ex)
                {
                    RailLog.Exception($"RAIL authoring definition autosave failed kind='{save.Kind}' id='{save.ObjectId}' package='{save.PackageId}'", ex);
                }
            }

            RailLog.Info($"RAIL authoring saved {savedCount} queued definition autosave(s).");
            return savedCount;
        }

        public static bool RebuildEntity(RailAuthoringEntity entity)
        {
            var resolved = RequireEntity(entity);
            var validation = resolved.Validate();
            NotifyValidation(resolved, validation);
            if (!validation.IsValid)
            {
                RailLog.Warning($"RAIL authoring rebuild skipped entity '{resolved.Id}' because validation failed with {validation.Errors.Count} error(s).");
                return false;
            }

            resolved.RebuildRuntime();
            RailAuthoringRegistry.Register(resolved);
            RailLog.Info($"RAIL authoring rebuilt entity '{resolved.Id}' kind='{resolved.EntityKind}'.");
            return true;
        }

        public static bool RebuildEntity(GameObject gameObject)
        {
            return RebuildEntity(ResolveEntity(gameObject));
        }

        public static bool RebuildEntity(Component component)
        {
            return RebuildEntity(ResolveEntity(component));
        }

        public static void CaptureFromRuntime(RailAuthoringEntity entity)
        {
            var resolved = RequireEntity(entity);
            resolved.CaptureFromRuntime();
            RailAuthoringRegistry.Register(resolved);
        }

        public static void CaptureFromRuntime(GameObject gameObject)
        {
            CaptureFromRuntime(ResolveEntity(gameObject));
        }

        public static void CaptureFromRuntime(Component component)
        {
            CaptureFromRuntime(ResolveEntity(component));
        }

        /// <summary>
        /// Mutates the running runtime to match an authoring entity. Marked
        /// experimental: behavior across snapshot/turntable/save-load callbacks
        /// is not yet fully reliable.
        /// </summary>
        [Experimental("Runtime authoring mutation; behavior across snapshot/turntable callbacks not fully reliable.")]
        public static bool ApplyToRuntime(RailAuthoringEntity entity)
        {
            RailExperimentalLog.WarnFirstUse(
                "RAIL.Authoring.RailAuthoringPersistenceService.ApplyToRuntime",
                "runtime authoring mutation");

            var resolved = RequireEntity(entity);
            var validation = resolved.Validate();
            NotifyValidation(resolved, validation);
            if (!validation.IsValid)
            {
                RailLog.Warning(
                    $"RAIL authoring apply package='{resolved.PackageId ?? string.Empty}' " +
                    $"operation='authoring apply' kind='{resolved.EntityKind ?? "<unknown>"}' " +
                    $"id='{resolved.Id}' message='validation failed with {validation.Errors.Count} error(s)'.");
                return false;
            }

            resolved.ApplyToRuntime();
            RailAuthoringRegistry.Register(resolved);
            return true;
        }

        [Experimental("Runtime authoring mutation; behavior across snapshot/turntable callbacks not fully reliable.")]
        public static bool ApplyToRuntime(GameObject gameObject)
        {
            return ApplyToRuntime(ResolveEntity(gameObject));
        }

        [Experimental("Runtime authoring mutation; behavior across snapshot/turntable callbacks not fully reliable.")]
        public static bool ApplyToRuntime(Component component)
        {
            return ApplyToRuntime(ResolveEntity(component));
        }

        public static bool TryGetSavedSnapshot(string entityId, out JObject data)
        {
            if (!string.IsNullOrWhiteSpace(entityId) && SavedEntitySnapshots.TryGetValue(entityId, out var saved))
            {
                data = (JObject)saved.DeepClone();
                return true;
            }

            data = null;
            return false;
        }

        private static RailAuthoringEntity ResolveEntity(GameObject gameObject)
        {
            if (RailAuthoringRegistry.TryGet(gameObject, out var entity))
            {
                return entity;
            }

            throw new InvalidOperationException($"No RAIL authoring entity is registered for GameObject '{gameObject?.name ?? "<null>"}'.");
        }

        private static RailAuthoringEntity ResolveEntity(Component component)
        {
            if (RailAuthoringRegistry.TryGet(component, out var entity))
            {
                return entity;
            }

            throw new InvalidOperationException($"No RAIL authoring entity is registered for Component '{component?.GetType().FullName ?? "<null>"}'.");
        }

        private static RailAuthoringEntity RequireEntity(RailAuthoringEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            return entity;
        }

        private static void NotifyValidation(RailAuthoringEntity entity, ValidationResult validation)
        {
            RailEditorBridge.EditorProvider?.OnValidationCompleted(entity.Id, validation);
        }

        private static bool SaveEntityToOwningDefinition(RailAuthoringEntity entity)
        {
            if (!TryResolveOwningDefinition(entity, out var definition, out var definitionPath))
            {
                RailLog.Warning($"RAIL authoring saved entity '{entity.Id}' only to memory because no owning definition is loaded for package '{entity.PackageId}'.");
                return false;
            }

            entity.BindDefinition(definition, definitionPath);
            if (!entity.SaveToDefinition(definition))
            {
                RailLog.Warning($"RAIL authoring entity '{entity.Id}' kind='{entity.EntityKind}' did not update file schema; snapshot only.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(definitionPath))
            {
                RailLog.Warning($"RAIL authoring updated package '{definition.Id}' in memory for entity '{entity.Id}' but no definition path is available for disk write.");
                return false;
            }

            SaveDefinitionToPath(definition, definitionPath);
            RailLog.Info($"RAIL authoring wrote entity '{entity.Id}' into definition '{definition.Id}' path='{definitionPath}'.");
            return true;
        }

        private static bool TryResolveOwningDefinition(RailAuthoringEntity entity, out RailModDefinition definition, out string definitionPath)
        {
            definition = entity.OwningDefinition;
            definitionPath = entity.DefinitionPath ?? string.Empty;
            if (definition != null)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(entity.PackageId) &&
                RailModLoader.TryGetLoadedMod(entity.PackageId, out var loaded) &&
                loaded?.Definition != null)
            {
                definition = loaded.Definition;
                definitionPath = loaded.DefinitionPath;
                return true;
            }

            return false;
        }

        private static bool TryApplyDefinitionObject(RailModDefinition package, string kind, string objectId, object definition)
        {
            if (package == null || string.IsNullOrWhiteSpace(objectId) || definition == null)
            {
                return false;
            }

            if (definition is RailNode node)
            {
                package.Tracks = package.Tracks ?? new RailTrackDefinition();
                package.Tracks.Nodes = package.Tracks.Nodes ?? new Dictionary<string, RailNode>();
                package.Tracks.Nodes[objectId] = node;
                return true;
            }

            if (definition is RailSegment segment)
            {
                package.Tracks = package.Tracks ?? new RailTrackDefinition();
                package.Tracks.Segments = package.Tracks.Segments ?? new Dictionary<string, RailSegment>();
                package.Tracks.Segments[objectId] = segment;
                return true;
            }

            if (definition is RailSpan span)
            {
                package.Tracks = package.Tracks ?? new RailTrackDefinition();
                package.Tracks.Spans = package.Tracks.Spans ?? new Dictionary<string, RailSpan>();
                package.Tracks.Spans[objectId] = span;
                return true;
            }

            if (definition is RailArea area)
            {
                package.Tracks = package.Tracks ?? new RailTrackDefinition();
                package.Tracks.Areas = package.Tracks.Areas ?? new Dictionary<string, RailArea>();
                package.Tracks.Areas[objectId] = area;
                return true;
            }

            if (definition is RailLoad load)
            {
                package.Operations = package.Operations ?? new RailOperationsDefinition();
                package.Operations.Loads = package.Operations.Loads ?? new Dictionary<string, RailLoad>();
                package.Operations.Loads[objectId] = load;
                return true;
            }

            if (definition is RailIndustry industry)
            {
                package.Operations = package.Operations ?? new RailOperationsDefinition();
                package.Operations.Industries = package.Operations.Industries ?? new Dictionary<string, RailIndustry>();
                package.Operations.Industries[objectId] = industry;
                return true;
            }

            if (definition is RailIndustryComponent component)
            {
                var separator = objectId.LastIndexOf('/');
                if (separator <= 0 || separator >= objectId.Length - 1)
                {
                    RailLog.Warning($"RAIL authoring cannot save industry component '{objectId}' because it is not in 'industryId/componentId' form.");
                    return false;
                }

                var industryId = objectId.Substring(0, separator);
                var subId = objectId.Substring(separator + 1);
                package.Operations = package.Operations ?? new RailOperationsDefinition();
                package.Operations.Industries = package.Operations.Industries ?? new Dictionary<string, RailIndustry>();
                if (!package.Operations.Industries.TryGetValue(industryId, out var parentIndustry) || parentIndustry == null)
                {
                    parentIndustry = new RailIndustry { Name = industryId };
                    package.Operations.Industries[industryId] = parentIndustry;
                }

                parentIndustry.Components = parentIndustry.Components ?? new Dictionary<string, RailIndustryComponent>();
                parentIndustry.Components[subId] = component;
                return true;
            }

            if (definition is RailLoader loader)
            {
                package.Operations = package.Operations ?? new RailOperationsDefinition();
                package.Operations.Loaders = package.Operations.Loaders ?? new Dictionary<string, RailLoader>();
                package.Operations.Loaders[objectId] = loader;
                return true;
            }

            if (definition is RailTurntable turntable)
            {
                package.Operations = package.Operations ?? new RailOperationsDefinition();
                package.Operations.Turntables = package.Operations.Turntables ?? new Dictionary<string, RailTurntable>();
                package.Operations.Turntables[objectId] = turntable;
                return true;
            }

            if (definition is RailStation station)
            {
                package.Operations = package.Operations ?? new RailOperationsDefinition();
                package.Operations.Stations = package.Operations.Stations ?? new Dictionary<string, RailStation>();
                package.Operations.Stations[objectId] = station;
                return true;
            }

            if (definition is RailScenery scenery)
            {
                package.World = package.World ?? new RailWorldDefinition();
                package.World.Scenery = package.World.Scenery ?? new Dictionary<string, RailScenery>();
                package.World.Scenery[objectId] = scenery;
                return true;
            }

            if (definition is RailSpliney spliney)
            {
                package.World = package.World ?? new RailWorldDefinition();
                package.World.Splineys = package.World.Splineys ?? new Dictionary<string, RailSpliney>();
                package.World.Splineys[objectId] = spliney;
                return true;
            }

            if (definition is RailTelegraphPoles telegraphPoles)
            {
                package.World = package.World ?? new RailWorldDefinition();
                package.World.TelegraphPoles = package.World.TelegraphPoles ?? new Dictionary<string, RailTelegraphPoles>();
                package.World.TelegraphPoles[objectId] = telegraphPoles;
                return true;
            }

            if (definition is RailMapLabel mapLabel)
            {
                package.World = package.World ?? new RailWorldDefinition();
                package.World.MapLabels = package.World.MapLabels ?? new Dictionary<string, RailMapLabel>();
                package.World.MapLabels[objectId] = mapLabel;
                return true;
            }

            if (definition is RailMapMask mapMask)
            {
                package.World = package.World ?? new RailWorldDefinition();
                package.World.MapMasks = package.World.MapMasks ?? new Dictionary<string, RailMapMask>();
                package.World.MapMasks[objectId] = mapMask;
                return true;
            }

            if (definition is RailSceneClone sceneClone)
            {
                package.World = package.World ?? new RailWorldDefinition();
                package.World.SceneClones = package.World.SceneClones ?? new Dictionary<string, RailSceneClone>();
                package.World.SceneClones[objectId] = sceneClone;
                return true;
            }

            if (definition is RailMapFeature mapFeature)
            {
                package.Progression = package.Progression ?? new RailProgressionRoot();
                package.Progression.MapFeatures = package.Progression.MapFeatures ?? new Dictionary<string, RailMapFeature>();
                package.Progression.MapFeatures[objectId] = mapFeature;
                return true;
            }

            if (definition is RailProgression progression)
            {
                package.Progression = package.Progression ?? new RailProgressionRoot();
                package.Progression.Progressions = package.Progression.Progressions ?? new Dictionary<string, RailProgression>();
                package.Progression.Progressions[objectId] = progression;
                return true;
            }

            return false;
        }

        private static void SaveDefinitionToPath(RailModDefinition definition, string definitionPath)
        {
            var extension = Path.GetExtension(definitionPath).ToLowerInvariant();
            switch (extension)
            {
                case ".json":
                    RailSerializer.SaveJson(definition, definitionPath);
                    break;
                case ".bson":
                    RailSerializer.SaveBson(definition, definitionPath);
                    break;
                default:
                    throw new InvalidOperationException($"RAIL authoring cannot save unknown definition format '{extension}' for '{definitionPath}'.");
            }
        }

        private sealed class QueuedDefinitionSave
        {
            public string PackageId;
            public string Kind;
            public string ObjectId;
            public object Definition;
            public string Reason;
        }
    }
}
