using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using FUSE.Authoring.Data;
using FUSE.Authoring.Editor;
using FUSE.Infrastructure;
using FUSE.Loading;
using FUSE.Authoring.Serialization;
using FUSE.Authoring.Validation;
using UnityEngine;

namespace FUSE.Authoring.Entities
{
    public static class FuseAuthoringPersistenceService
    {
        private static readonly Dictionary<string, JObject> SavedEntitySnapshots =
            new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> AutosaveQueue =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, QueuedDefinitionSave> DefinitionAutosaveQueue =
            new Dictionary<string, QueuedDefinitionSave>(StringComparer.OrdinalIgnoreCase);

        public static JObject SaveEntity(FuseAuthoringEntity entity)
        {
            var resolved = RequireEntity(entity);
            var validation = resolved.Validate();
            NotifyValidation(resolved, validation);
            if (!validation.IsValid)
            {
                FuseLog.Warning($"FUSE authoring entity '{resolved.Id}' saved with {validation.Errors.Count} validation error(s).");
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

            FuseAuthoringRegistry.Register(resolved);
            FuseLog.Info($"FUSE authoring saved entity '{resolved.Id}' kind='{resolved.EntityKind}'.");
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

        public static void LoadEntity(FuseAuthoringEntity entity, JObject data)
        {
            var resolved = RequireEntity(entity);
            resolved.LoadAuthoringData(data);
            FuseAuthoringRegistry.Register(resolved);
            SavedEntitySnapshots[resolved.Id] = data != null ? (JObject)data.DeepClone() : new JObject();
            FuseLog.Info($"FUSE authoring loaded entity '{resolved.Id}' kind='{resolved.EntityKind}'.");
        }

        public static void LoadEntity(FuseAuthoringEntity entity, string json)
        {
            LoadEntity(entity, string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json));
        }

        public static void MarkDirty(FuseAuthoringEntity entity)
        {
            RequireEntity(entity).MarkDirty("marked dirty by editor");
        }

        public static void MarkDirty(FuseAuthoringEntity entity, string reason, bool queueAutosave = false)
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
            foreach (var entity in FuseAuthoringRegistry.AllEntities.ToArray())
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
                    FuseLog.Exception($"FUSE authoring failed to save dirty entity '{entity.Id}'", ex);
                }
            }

            FuseLog.Info($"FUSE authoring saved {savedCount} dirty entity/entities.");
            return savedCount;
        }

        public static void QueueAutosave(FuseAuthoringEntity entity, string reason = null)
        {
            var resolved = RequireEntity(entity);
            if (string.IsNullOrWhiteSpace(resolved.Id))
            {
                return;
            }

            AutosaveQueue.Add(resolved.Id);
            FuseAuthoringRegistry.Register(resolved);
            FuseLog.Info($"FUSE authoring queued autosave for entity '{resolved.Id}' reason='{reason ?? string.Empty}'.");
        }

        public static int SaveQueuedAutosaves()
        {
            var entitySavedCount = SaveQueuedEntityAutosaves();
            var definitionSavedCount = SaveQueuedDefinitionAutosaves();
            FuseLog.Info($"FUSE authoring saved {entitySavedCount} entity autosave(s) and {definitionSavedCount} definition autosave(s).");
            return entitySavedCount + definitionSavedCount;
        }

        private static int SaveQueuedEntityAutosaves()
        {
            var queuedIds = AutosaveQueue.ToArray();
            var savedCount = 0;
            foreach (var id in queuedIds)
            {
                if (!FuseAuthoringRegistry.TryGet(id, out var entity) || entity == null)
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
                    FuseLog.Exception($"FUSE authoring autosave failed for entity '{id}'", ex);
                }
            }

            FuseLog.Info($"FUSE authoring saved {savedCount} queued autosave entity/entities.");
            return savedCount;
        }

        public static bool SaveDefinitionObject(string packageId, string kind, string objectId, object definition, string reason = null)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                FuseLog.Warning($"FUSE authoring definition save skipped '{kind}' '{objectId}' because package id was empty.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(objectId))
            {
                FuseLog.Warning($"FUSE authoring definition save skipped '{kind}' because object id was empty.");
                return false;
            }

            if (definition == null)
            {
                FuseLog.Warning($"FUSE authoring definition save skipped '{kind}' '{objectId}' because definition was null.");
                return false;
            }

            if (!FuseModLoader.TryGetLoadedMod(packageId, out var loaded) || loaded?.Definition == null)
            {
                FuseLog.Warning($"FUSE authoring definition save skipped '{kind}' '{objectId}' because package '{packageId}' is not loaded.");
                return false;
            }

            if (!TryApplyDefinitionObject(loaded.Definition, kind, objectId, definition))
            {
                FuseLog.Warning($"FUSE authoring definition save skipped unsupported kind='{kind}' id='{objectId}' type='{definition.GetType().FullName}'.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(loaded.DefinitionPath))
            {
                FuseLog.Warning($"FUSE authoring updated definition '{loaded.Definition.Id}' in memory for '{kind}' '{objectId}', but no definition path is available for disk write.");
                return false;
            }

            SaveDefinitionToPath(loaded.Definition, loaded.DefinitionPath);
            FuseLog.Info($"FUSE authoring saved '{kind}' '{objectId}' to package '{loaded.Definition.Id}' reason='{reason ?? string.Empty}'.");
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
            FuseLog.Info($"FUSE authoring queued definition autosave kind='{kind}' id='{objectId}' package='{packageId}' reason='{reason ?? string.Empty}'.");
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
                    FuseLog.Exception($"FUSE authoring definition autosave failed kind='{save.Kind}' id='{save.ObjectId}' package='{save.PackageId}'", ex);
                }
            }

            FuseLog.Info($"FUSE authoring saved {savedCount} queued definition autosave(s).");
            return savedCount;
        }

        public static bool RebuildEntity(FuseAuthoringEntity entity)
        {
            var resolved = RequireEntity(entity);
            var validation = resolved.Validate();
            NotifyValidation(resolved, validation);
            if (!validation.IsValid)
            {
                FuseLog.Warning($"FUSE authoring rebuild skipped entity '{resolved.Id}' because validation failed with {validation.Errors.Count} error(s).");
                return false;
            }

            resolved.RebuildRuntime();
            FuseAuthoringRegistry.Register(resolved);
            FuseLog.Info($"FUSE authoring rebuilt entity '{resolved.Id}' kind='{resolved.EntityKind}'.");
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

        public static void CaptureFromRuntime(FuseAuthoringEntity entity)
        {
            var resolved = RequireEntity(entity);
            resolved.CaptureFromRuntime();
            FuseAuthoringRegistry.Register(resolved);
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
        public static bool ApplyToRuntime(FuseAuthoringEntity entity)
        {
            FuseExperimentalLog.WarnFirstUse(
                "FUSE.Authoring.Entities.FuseAuthoringPersistenceService.ApplyToRuntime",
                "runtime authoring mutation");

            return ApplyToRuntimeCore(entity);
        }

        internal static bool ApplyPackageEntityToRuntime(FuseAuthoringEntity entity)
        {
            return ApplyToRuntimeCore(entity);
        }

        private static bool ApplyToRuntimeCore(FuseAuthoringEntity entity)
        {
            var resolved = RequireEntity(entity);
            var validation = resolved.Validate();
            NotifyValidation(resolved, validation);
            if (!validation.IsValid)
            {
                FuseLog.Warning(
                    $"FUSE authoring apply package='{resolved.PackageId ?? string.Empty}' " +
                    $"operation='authoring apply' kind='{resolved.EntityKind ?? "<unknown>"}' " +
                    $"id='{resolved.Id}' message='validation failed with {validation.Errors.Count} error(s)'.");
                return false;
            }

            resolved.ApplyToRuntime();
            FuseAuthoringRegistry.Register(resolved);
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

        private static FuseAuthoringEntity ResolveEntity(GameObject gameObject)
        {
            if (FuseAuthoringRegistry.TryGet(gameObject, out var entity))
            {
                return entity;
            }

            throw new InvalidOperationException($"No FUSE authoring entity is registered for GameObject '{gameObject?.name ?? "<null>"}'.");
        }

        private static FuseAuthoringEntity ResolveEntity(Component component)
        {
            if (FuseAuthoringRegistry.TryGet(component, out var entity))
            {
                return entity;
            }

            throw new InvalidOperationException($"No FUSE authoring entity is registered for Component '{component?.GetType().FullName ?? "<null>"}'.");
        }

        private static FuseAuthoringEntity RequireEntity(FuseAuthoringEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            return entity;
        }

        private static void NotifyValidation(FuseAuthoringEntity entity, ValidationResult validation)
        {
            FuseEditorBridge.EditorProvider?.OnValidationCompleted(entity.Id, validation);
        }

        private static bool SaveEntityToOwningDefinition(FuseAuthoringEntity entity)
        {
            if (!TryResolveOwningDefinition(entity, out var definition, out var definitionPath))
            {
                FuseLog.Warning($"FUSE authoring saved entity '{entity.Id}' only to memory because no owning definition is loaded for package '{entity.PackageId}'.");
                return false;
            }

            entity.BindDefinition(definition, definitionPath);
            if (!entity.SaveToDefinition(definition))
            {
                FuseLog.Warning($"FUSE authoring entity '{entity.Id}' kind='{entity.EntityKind}' did not update file schema; snapshot only.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(definitionPath))
            {
                FuseLog.Warning($"FUSE authoring updated package '{definition.Id}' in memory for entity '{entity.Id}' but no definition path is available for disk write.");
                return false;
            }

            SaveDefinitionToPath(definition, definitionPath);
            FuseLog.Info($"FUSE authoring wrote entity '{entity.Id}' into definition '{definition.Id}' path='{definitionPath}'.");
            return true;
        }

        private static bool TryResolveOwningDefinition(FuseAuthoringEntity entity, out FuseModDefinition definition, out string definitionPath)
        {
            definition = entity.OwningDefinition;
            definitionPath = entity.DefinitionPath ?? string.Empty;
            if (definition != null)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(entity.PackageId) &&
                FuseModLoader.TryGetLoadedMod(entity.PackageId, out var loaded) &&
                loaded?.Definition != null)
            {
                definition = loaded.Definition;
                definitionPath = loaded.DefinitionPath;
                return true;
            }

            return false;
        }

        private static bool TryApplyDefinitionObject(FuseModDefinition package, string kind, string objectId, object definition)
        {
            if (package == null || string.IsNullOrWhiteSpace(objectId) || definition == null)
            {
                return false;
            }

            if (definition is FuseNode node)
            {
                package.Tracks = package.Tracks ?? new FuseTrackDefinition();
                package.Tracks.Nodes = package.Tracks.Nodes ?? new Dictionary<string, FuseNode>();
                package.Tracks.Nodes[objectId] = node;
                return true;
            }

            if (definition is FuseSegment segment)
            {
                package.Tracks = package.Tracks ?? new FuseTrackDefinition();
                package.Tracks.Segments = package.Tracks.Segments ?? new Dictionary<string, FuseSegment>();
                package.Tracks.Segments[objectId] = segment;
                return true;
            }

            if (definition is FuseSpan span)
            {
                package.Tracks = package.Tracks ?? new FuseTrackDefinition();
                package.Tracks.Spans = package.Tracks.Spans ?? new Dictionary<string, FuseSpan>();
                package.Tracks.Spans[objectId] = span;
                return true;
            }

            if (definition is FuseArea area)
            {
                package.Tracks = package.Tracks ?? new FuseTrackDefinition();
                package.Tracks.Areas = package.Tracks.Areas ?? new Dictionary<string, FuseArea>();
                package.Tracks.Areas[objectId] = area;
                return true;
            }

            if (definition is FuseLoad load)
            {
                package.Operations = package.Operations ?? new FuseOperationsDefinition();
                package.Operations.Loads = package.Operations.Loads ?? new Dictionary<string, FuseLoad>();
                package.Operations.Loads[objectId] = load;
                return true;
            }

            if (definition is FuseIndustry industry)
            {
                package.Operations = package.Operations ?? new FuseOperationsDefinition();
                package.Operations.Industries = package.Operations.Industries ?? new Dictionary<string, FuseIndustry>();
                package.Operations.Industries[objectId] = industry;
                return true;
            }

            if (definition is FuseIndustryComponent component)
            {
                var separator = objectId.LastIndexOf('/');
                if (separator <= 0 || separator >= objectId.Length - 1)
                {
                    FuseLog.Warning($"FUSE authoring cannot save industry component '{objectId}' because it is not in 'industryId/componentId' form.");
                    return false;
                }

                var industryId = objectId.Substring(0, separator);
                var subId = objectId.Substring(separator + 1);
                package.Operations = package.Operations ?? new FuseOperationsDefinition();
                package.Operations.Industries = package.Operations.Industries ?? new Dictionary<string, FuseIndustry>();
                if (!package.Operations.Industries.TryGetValue(industryId, out var parentIndustry) || parentIndustry == null)
                {
                    parentIndustry = new FuseIndustry { Name = industryId };
                    package.Operations.Industries[industryId] = parentIndustry;
                }

                parentIndustry.Components = parentIndustry.Components ?? new Dictionary<string, FuseIndustryComponent>();
                parentIndustry.Components[subId] = component;
                return true;
            }

            if (definition is FuseLoader loader)
            {
                package.Operations = package.Operations ?? new FuseOperationsDefinition();
                package.Operations.Loaders = package.Operations.Loaders ?? new Dictionary<string, FuseLoader>();
                package.Operations.Loaders[objectId] = loader;
                return true;
            }

            if (definition is FuseTurntable turntable)
            {
                package.Operations = package.Operations ?? new FuseOperationsDefinition();
                package.Operations.Turntables = package.Operations.Turntables ?? new Dictionary<string, FuseTurntable>();
                package.Operations.Turntables[objectId] = turntable;
                return true;
            }

            if (definition is FuseStation station)
            {
                package.Operations = package.Operations ?? new FuseOperationsDefinition();
                package.Operations.Stations = package.Operations.Stations ?? new Dictionary<string, FuseStation>();
                package.Operations.Stations[objectId] = station;
                return true;
            }

            if (definition is FuseScenery scenery)
            {
                package.World = package.World ?? new FuseWorldDefinition();
                package.World.Scenery = package.World.Scenery ?? new Dictionary<string, FuseScenery>();
                package.World.Scenery[objectId] = scenery;
                return true;
            }

            if (definition is FuseSpliney spliney)
            {
                package.World = package.World ?? new FuseWorldDefinition();
                package.World.Splineys = package.World.Splineys ?? new Dictionary<string, FuseSpliney>();
                package.World.Splineys[objectId] = spliney;
                return true;
            }

            if (definition is FuseTelegraphPoles telegraphPoles)
            {
                package.World = package.World ?? new FuseWorldDefinition();
                package.World.TelegraphPoles = package.World.TelegraphPoles ?? new Dictionary<string, FuseTelegraphPoles>();
                package.World.TelegraphPoles[objectId] = telegraphPoles;
                return true;
            }

            if (definition is FuseMapLabel mapLabel)
            {
                package.World = package.World ?? new FuseWorldDefinition();
                package.World.MapLabels = package.World.MapLabels ?? new Dictionary<string, FuseMapLabel>();
                package.World.MapLabels[objectId] = mapLabel;
                return true;
            }

            if (definition is FuseMapMask mapMask)
            {
                package.World = package.World ?? new FuseWorldDefinition();
                package.World.MapMasks = package.World.MapMasks ?? new Dictionary<string, FuseMapMask>();
                package.World.MapMasks[objectId] = mapMask;
                return true;
            }

            if (definition is FuseSceneClone sceneClone)
            {
                package.World = package.World ?? new FuseWorldDefinition();
                package.World.SceneClones = package.World.SceneClones ?? new Dictionary<string, FuseSceneClone>();
                package.World.SceneClones[objectId] = sceneClone;
                return true;
            }

            if (definition is FuseMapFeature mapFeature)
            {
                package.Progression = package.Progression ?? new FuseProgressionRoot();
                package.Progression.MapFeatures = package.Progression.MapFeatures ?? new Dictionary<string, FuseMapFeature>();
                package.Progression.MapFeatures[objectId] = mapFeature;
                return true;
            }

            if (definition is FuseProgression progression)
            {
                package.Progression = package.Progression ?? new FuseProgressionRoot();
                package.Progression.Progressions = package.Progression.Progressions ?? new Dictionary<string, FuseProgression>();
                package.Progression.Progressions[objectId] = progression;
                return true;
            }

            return false;
        }

        private static void SaveDefinitionToPath(FuseModDefinition definition, string definitionPath)
        {
            var extension = Path.GetExtension(definitionPath).ToLowerInvariant();
            switch (extension)
            {
                case ".json":
                    FuseSerializer.SaveJson(definition, definitionPath);
                    break;
                case ".bson":
                    FuseSerializer.SaveBson(definition, definitionPath);
                    break;
                default:
                    throw new InvalidOperationException($"FUSE authoring cannot save unknown definition format '{extension}' for '{definitionPath}'.");
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
