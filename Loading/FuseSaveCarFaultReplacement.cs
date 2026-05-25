using System;
using System.Linq;
using System.Reflection;
using AssetPack.Runtime;
using FUSE.Data;
using FUSE.Infrastructure;
using HarmonyLib;

namespace FUSE.Loading
{
    /// <summary>
    /// Applies user-chosen prototype replacements to recorded
    /// <see cref="FuseSaveCarFault"/> entries. The strategy: take the
    /// boxed <c>Snapshot.Car</c> the game's loader was about to
    /// process when it threw, mutate the <c>prototypeId</c> field to
    /// the chosen replacement, and re-invoke
    /// <c>TrainController.AddCarInternal</c> with the modified
    /// snapshot plus the original properties dictionary. The result
    /// is a fully constructed Car in the world at the same location,
    /// with the same id, road number, waybill, content, and
    /// properties — just a different model.
    ///
    /// <para>All access to game internals is reflective so a host-
    /// side field/method rename produces a soft fail rather than a
    /// hard crash at FUSE load. Failure modes return false and log
    /// a warning; the registry record stays in place so the user
    /// can retry or pick a different replacement.</para>
    /// </summary>
    internal static class FuseSaveCarFaultReplacement
    {
        private static readonly object Sync = new object();
        private static bool _reflectionInitialized;
        private static Type _trainControllerType;
        private static MethodInfo _addCarInternalMethod;
        private static PropertyInfo _trainControllerSharedProperty;
        private static FieldInfo _snapshotCarPrototypeIdField;

        /// <summary>
        /// Returns the list of car identifiers currently in the
        /// PrefabStore that can be used as replacements. Pulls from
        /// the live <c>AllCarDefinitionInfos</c> getter (which the
        /// loser-filter postfix has already stripped of orphan-only
        /// definitions), so every name the picker shows is one the
        /// game can actually load right now.
        /// </summary>
        public static string[] GetAvailablePrototypeIds()
        {
            try
            {
                // DELIBERATELY avoid the game's
                // <c>PrefabStore.AllCarDefinitionInfos</c> getter —
                // it does <c>HashSet.Select(CarDefinitionInfoForIdentifier)</c>
                // lazily, and our filter Prefix on
                // <c>AssetPackContainingIdentifier</c> throws
                // <c>UnknownIdentifierException</c> for any loser-only
                // identifier in that hash set. Iterating the lazy
                // result would tear the whole enumeration down on the
                // first such identifier (e.g. the legacy
                // <c>spinecar1</c> car definition that only lives in
                // an SCAssetPacks-loser pack). Walk the stores
                // directly and skip losers BEFORE we touch their
                // containers, so the filtered lookup is never
                // triggered.
                var prefabStore = TryGetPrefabStore();
                if (prefabStore == null)
                {
                    return Array.Empty<string>();
                }

                var storesField = AccessTools.Field(typeof(Model.Database.PrefabStore), "_stores");
                if (storesField == null)
                {
                    return Array.Empty<string>();
                }

                if (!(storesField.GetValue(prefabStore) is System.Collections.IEnumerable storesEnumerable))
                {
                    return Array.Empty<string>();
                }

                var identifiers = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                foreach (var raw in storesEnumerable)
                {
                    if (!(raw is AssetPackRuntimeStore store))
                    {
                        continue;
                    }

                    // Drop loser stores up front so legacy
                    // SCAssetPacks-only car definitions never become
                    // replacement options — picking one would just
                    // re-orphan the car on next load.
                    string basePath = null;
                    try
                    {
                        var basePathProp = AccessTools.Property(typeof(AssetPackRuntimeStore), "BasePath");
                        basePath = basePathProp?.GetValue(store, null) as string;
                    }
                    catch
                    {
                        basePath = null;
                    }

                    if (!string.IsNullOrEmpty(basePath) && FuseAssetCollisionRegistry.IsLoserFolder(basePath))
                    {
                        continue;
                    }

                    Model.Definition.Container container;
                    try
                    {
                        container = store.Container();
                    }
                    catch
                    {
                        continue;
                    }

                    if (container?.Objects == null)
                    {
                        continue;
                    }

                    foreach (var item in container.Objects)
                    {
                        if (item == null || string.IsNullOrEmpty(item.Identifier))
                        {
                            continue;
                        }
                        if (item.Definition is Model.Definition.Data.CarDefinition)
                        {
                            identifiers.Add(item.Identifier);
                        }
                    }
                }

                return identifiers
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not enumerate available replacement car identifiers: {ex.GetBaseException().Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Picks a random replacement for <paramref name="fault"/>
        /// whose modern definition references the SAME asset prefab
        /// as the orphan's legacy definition. The orphan's
        /// <c>modelIdentifier</c> is the prefab name in the
        /// pack's Catalog (e.g. <c>"spinecar1"</c> for both legacy
        /// and modern TOFC piggyback variants), so every modern
        /// <c>CarDefinition</c> with the same <c>ModelIdentifier</c>
        /// renders with the same model, carries the same load class,
        /// has the same LoadModel components, and is structurally
        /// indistinguishable for the purposes of the existing
        /// waybill. That set is exactly what the interchange would
        /// have spawned had the legacy definition been filtered at
        /// the source. Returns the picked prototype identifier or
        /// null when no compatible replacement exists.
        /// </summary>
        public static string PickRandomSameTypeReplacement(FuseSaveCarFault fault, Random rnd)
        {
            if (fault == null)
            {
                return null;
            }

            try
            {
                var modelIdentifier = ReadLegacyModelIdentifierForOrphan(fault.MissingPrototypeId);
                if (string.IsNullOrEmpty(modelIdentifier))
                {
                    FuseLog.Warning(
                        $"FUSE could not determine modelIdentifier for orphan prototype " +
                        $"'{fault.MissingPrototypeId}' (car {fault.DisplayName}); cannot pick a " +
                        $"model-compatible replacement and refusing to fall back to a generic same-archetype " +
                        $"pick because that would produce a car the existing waybill cannot use.");
                    return null;
                }

                var prefabStore = TryGetPrefabStore();
                if (prefabStore == null)
                {
                    return null;
                }

                // Walk every non-loser store's container and collect
                // car identifiers whose CarDefinition.ModelIdentifier
                // matches the orphan's. We deliberately don't go
                // through PrefabStore.Random / AllCarDefinitionInfos
                // here because we want a strict equality match on
                // ModelIdentifier, not the carType-string prefix
                // matching the interchange uses for order fulfilment.
                var storesField = AccessTools.Field(typeof(Model.Database.PrefabStore), "_stores");
                if (storesField == null ||
                    !(storesField.GetValue(prefabStore) is System.Collections.IEnumerable storesEnumerable))
                {
                    return null;
                }

                var candidates = new System.Collections.Generic.List<string>();
                var basePathProp = AccessTools.Property(typeof(AssetPackRuntimeStore), "BasePath");
                foreach (var raw in storesEnumerable)
                {
                    if (!(raw is AssetPackRuntimeStore store))
                    {
                        continue;
                    }

                    string basePath = null;
                    try { basePath = basePathProp?.GetValue(store, null) as string; } catch { basePath = null; }
                    if (!string.IsNullOrEmpty(basePath) && FUSE.Loading.FuseAssetCollisionRegistry.IsLoserFolder(basePath))
                    {
                        // Modern siblings live in non-loser stores;
                        // loser stores are where the orphan itself
                        // came from.
                        continue;
                    }

                    Model.Definition.Container container;
                    try { container = store.Container(); } catch { continue; }
                    if (container?.Objects == null) continue;

                    foreach (var item in container.Objects)
                    {
                        if (item == null || string.IsNullOrEmpty(item.Identifier))
                        {
                            continue;
                        }
                        if (!(item.Definition is Model.Definition.Data.CarDefinition carDef))
                        {
                            continue;
                        }
                        if (string.Equals(carDef.ModelIdentifier, modelIdentifier, StringComparison.Ordinal))
                        {
                            candidates.Add(item.Identifier);
                        }
                    }
                }

                if (candidates.Count == 0)
                {
                    FuseLog.Warning(
                        $"FUSE found no modern car definitions referencing modelIdentifier='{modelIdentifier}' " +
                        $"(orphan {fault.DisplayName} / {fault.MissingPrototypeId}); skipping. The mod that " +
                        $"shipped the orphan's legacy definition may not have a modern sibling installed.");
                    return null;
                }

                var sampler = rnd ?? new Random();
                return candidates[sampler.Next(candidates.Count)];
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE same-model random pick failed for orphan '{fault.MissingPrototypeId}': " +
                    $"{ex.GetBaseException().Message}");
                return null;
            }
        }

        /// <summary>
        /// Same-type random pick + apply in one call. Convenience
        /// for the orphan-window button. Spawns the replacement
        /// car directly at the orphan's saved location.
        ///
        /// <para>WARNING: spawning at the saved location collides
        /// with surviving cars on the same track and can derail
        /// the existing consist. Prefer
        /// <see cref="TryPresentReplacementsViaConsistPlacer"/>
        /// which hands the replacement consist to the game's
        /// <c>ConsistPlacer</c> so the player picks a free track
        /// span — same flow the Lost &amp; Found window uses for
        /// cars whose saved location is no longer valid.</para>
        /// </summary>
        public static bool TryApplyRandomSameType(FuseSaveCarFault fault, Random rnd, out string pickedPrototypeId)
        {
            pickedPrototypeId = PickRandomSameTypeReplacement(fault, rnd);
            if (string.IsNullOrEmpty(pickedPrototypeId))
            {
                return false;
            }
            return TryApply(fault, pickedPrototypeId);
        }

        /// <summary>
        /// Same-model random pick for each fault in
        /// <paramref name="faults"/>, but hand the resulting
        /// descriptors to the game's <c>ConsistPlacer.Present</c>
        /// so the player can click a free section of track to
        /// place the replacement consist — same flow Lost &amp;
        /// Found uses. The orphan registry is cleared per car
        /// only after the player completes placement; on cancel,
        /// the records stay so the popup can re-show.
        ///
        /// <para><paramref name="onComplete"/> fires with
        /// <c>true</c> when the player placed the cars, <c>false</c>
        /// when they cancelled (Escape).</para>
        /// </summary>
        public static bool TryPresentReplacementsViaConsistPlacer(
            System.Collections.Generic.IReadOnlyList<FuseSaveCarFault> faults,
            Random rnd,
            Action<bool, System.Collections.Generic.IReadOnlyList<string>> onComplete)
        {
            if (faults == null || faults.Count == 0)
            {
                return false;
            }

            try
            {
                EnsureReflectionInitialized();
                var trainController = _trainControllerSharedProperty?.GetValue(null, null);
                if (trainController == null)
                {
                    FuseLog.Warning("FUSE replacement placement skipped: TrainController.Shared is null.");
                    return false;
                }

                var carDescriptorFromSnapshotMethod = AccessTools.Method(
                    _trainControllerType,
                    "CarDescriptorFromSnapshotCar");
                if (carDescriptorFromSnapshotMethod == null || _snapshotCarPrototypeIdField == null)
                {
                    FuseLog.Warning(
                        "FUSE replacement placement skipped: required reflection handles unresolved " +
                        "(CarDescriptorFromSnapshotCar or Snapshot.Car.prototypeId).");
                    return false;
                }

                // Build a descriptor per fault, with prototypeId
                // mutated on the boxed snapshot copy in place.
                // CarDescriptorFromSnapshotCar reads the snapshot
                // and resolves the prototypeId to the new TypedContainerItem<CarDefinition>,
                // so the resulting descriptor carries the modern
                // car type but the original road number / ident /
                // properties (waybill, load, content).
                var descriptors = new System.Collections.Generic.List<object>();
                var ids = new System.Collections.Generic.List<string>();
                var picks = new System.Collections.Generic.List<string>();
                var resolvedFaults = new System.Collections.Generic.List<FuseSaveCarFault>();
                var skippedFaults = new System.Collections.Generic.List<FuseSaveCarFault>();

                foreach (var fault in faults)
                {
                    if (fault == null || !fault.CanReplace)
                    {
                        skippedFaults.Add(fault);
                        continue;
                    }

                    var picked = PickRandomSameTypeReplacement(fault, rnd);
                    if (string.IsNullOrEmpty(picked))
                    {
                        skippedFaults.Add(fault);
                        continue;
                    }

                    object descriptor;
                    try
                    {
                        _snapshotCarPrototypeIdField.SetValue(fault.OriginalSnapshotCar, picked);
                        descriptor = carDescriptorFromSnapshotMethod.Invoke(
                            trainController,
                            new[] { fault.OriginalSnapshotCar, fault.OriginalSnapshotProperties });
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Warning(
                            $"FUSE could not build CarDescriptor for orphan {fault.DisplayName} " +
                            $"(picked='{picked}'): {ex.GetBaseException().Message}");
                        skippedFaults.Add(fault);
                        continue;
                    }

                    if (descriptor == null)
                    {
                        skippedFaults.Add(fault);
                        continue;
                    }

                    descriptors.Add(descriptor);
                    ids.Add(fault.CarId);
                    picks.Add(picked);
                    resolvedFaults.Add(fault);
                }

                if (descriptors.Count == 0)
                {
                    FuseLog.Warning(
                        "FUSE replacement placement skipped: no orphan produced a valid CarDescriptor; " +
                        "see prior warnings for per-fault failures.");
                    return false;
                }

                var consistPlacerType = AccessTools.TypeByName("ConsistPlacer");
                if (consistPlacerType == null)
                {
                    FuseLog.Warning(
                        "FUSE replacement placement skipped: ConsistPlacer type not found in the runtime.");
                    return false;
                }

                var instanceMethod = AccessTools.Method(consistPlacerType, "Instance");
                var consistPlacer = instanceMethod?.Invoke(null, null);
                if (consistPlacer == null)
                {
                    FuseLog.Warning(
                        "FUSE replacement placement skipped: ConsistPlacer.Instance() returned null (no scene-active placer).");
                    return false;
                }

                var presentMethod = AccessTools.Method(consistPlacerType, "Present");
                if (presentMethod == null)
                {
                    FuseLog.Warning(
                        "FUSE replacement placement skipped: ConsistPlacer.Present not found.");
                    return false;
                }

                // CarDescriptor is an internal/sealed type; cast the
                // List<object> we built into the proper generic List
                // by constructing a strongly-typed wrapper. Reflection
                // method invocation accepts an IEnumerable in the
                // declared parameter type, but Present's first param
                // is IEnumerable<CarDescriptor>. We coerce via
                // reflection by building a List<CarDescriptor>.
                var carDescriptorType = AccessTools.TypeByName("Model.CarDescriptor")
                                        ?? AccessTools.TypeByName("CarDescriptor");
                if (carDescriptorType == null && descriptors[0] != null)
                {
                    carDescriptorType = descriptors[0].GetType();
                }
                if (carDescriptorType == null)
                {
                    FuseLog.Warning("FUSE replacement placement skipped: CarDescriptor type not resolvable.");
                    return false;
                }

                var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(carDescriptorType);
                var descriptorList = (System.Collections.IList)Activator.CreateInstance(listType);
                foreach (var d in descriptors)
                {
                    descriptorList.Add(d);
                }

                // Build the ConsistPlacerDidPlace delegate via
                // reflection so this code compiles without a
                // direct reference to the delegate type.
                var didPlaceType = AccessTools.TypeByName("ConsistPlacerDidPlace");
                Delegate callback;
                if (didPlaceType != null)
                {
                    Action<bool> onPlaced = placed =>
                    {
                        HandleConsistPlacerCompletion(placed, resolvedFaults, picks, onComplete);
                    };
                    callback = Delegate.CreateDelegate(didPlaceType, onPlaced.Target, onPlaced.Method);
                }
                else
                {
                    callback = null;
                }

                presentMethod.Invoke(consistPlacer, new object[] { descriptorList, ids, callback });

                FuseLog.Info(
                    $"FUSE handed {descriptors.Count} replacement car(s) to ConsistPlacer for placement; " +
                    $"picks=[{string.Join(", ", picks.Distinct())}].");

                if (skippedFaults.Count > 0)
                {
                    FuseLog.Warning(
                        $"FUSE replacement placement: {skippedFaults.Count} fault(s) were skipped because no " +
                        $"compatible replacement could be picked; the orphan window will keep listing them.");
                }

                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE replacement placement failed: {ex.GetBaseException().Message}");
                return false;
            }
        }

        private static void HandleConsistPlacerCompletion(
            bool placed,
            System.Collections.Generic.IReadOnlyList<FuseSaveCarFault> resolvedFaults,
            System.Collections.Generic.IReadOnlyList<string> picks,
            Action<bool, System.Collections.Generic.IReadOnlyList<string>> onComplete)
        {
            try
            {
                if (placed)
                {
                    foreach (var fault in resolvedFaults)
                    {
                        if (fault != null)
                        {
                            FUSE.Loading.FuseSaveCarFaultRegistry.RemoveByCarId(fault.CarId);
                        }
                    }
                    FuseLog.Info(
                        $"FUSE replacement placement completed: {resolvedFaults.Count} car(s) placed.");
                }
                else
                {
                    FuseLog.Info(
                        $"FUSE replacement placement cancelled by user; {resolvedFaults.Count} orphan(s) remain in the registry.");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE replacement placement completion handler failed: {ex.GetBaseException().Message}");
            }

            try
            {
                onComplete?.Invoke(placed, picks);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE replacement placement onComplete callback failed: {ex.GetBaseException().Message}");
            }
        }

        /// <summary>
        /// Reads the orphan's <c>ModelIdentifier</c> (the prefab name
        /// the legacy definition pointed at) directly from the
        /// loser SCAssetPacks store that defined it, bypassing the
        /// filtered <c>AssetPackContainingIdentifier</c> lookup that
        /// would throw. This is the key the same-model replacement
        /// search uses to find structurally identical modern
        /// siblings. Returns null when the identifier is not found
        /// in any loser store's container.
        /// </summary>
        private static string ReadLegacyModelIdentifierForOrphan(string missingPrototypeId)
        {
            if (string.IsNullOrEmpty(missingPrototypeId))
            {
                return null;
            }

            var prefabStore = TryGetPrefabStore();
            if (prefabStore == null)
            {
                return null;
            }

            var storesField = AccessTools.Field(typeof(Model.Database.PrefabStore), "_stores");
            if (storesField == null)
            {
                return null;
            }
            if (!(storesField.GetValue(prefabStore) is System.Collections.IEnumerable storesEnumerable))
            {
                return null;
            }

            var basePathProp = AccessTools.Property(typeof(AssetPackRuntimeStore), "BasePath");
            foreach (var raw in storesEnumerable)
            {
                if (!(raw is AssetPackRuntimeStore store))
                {
                    continue;
                }

                string basePath = null;
                try { basePath = basePathProp?.GetValue(store, null) as string; } catch { basePath = null; }

                // Only look at loser stores — the orphan identifier
                // exists nowhere else by definition (winner stores
                // were checked by the filter and rejected).
                if (string.IsNullOrEmpty(basePath) || !FUSE.Loading.FuseAssetCollisionRegistry.IsLoserFolder(basePath))
                {
                    continue;
                }

                Model.Definition.Container container;
                try { container = store.Container(); } catch { continue; }
                if (container?.Objects == null) continue;

                foreach (var item in container.Objects)
                {
                    if (item == null) continue;
                    if (!string.Equals(item.Identifier, missingPrototypeId, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (item.Definition is Model.Definition.Data.CarDefinition carDef)
                    {
                        return carDef.ModelIdentifier;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Attempts to spawn a replacement for <paramref name="fault"/>
        /// using <paramref name="replacementPrototypeId"/> as the new
        /// car type. On success returns true, removes the registry
        /// record, and the new car is in the world; on failure
        /// returns false, leaves the registry record in place, and
        /// logs a warning. Idempotent on the registry side — re-
        /// invoking with the same fault when the car has already
        /// been re-spawned will be a no-op replacement at the game
        /// level but registry-removal is safe.
        /// </summary>
        public static bool TryApply(FuseSaveCarFault fault, string replacementPrototypeId)
        {
            if (fault == null || !fault.CanReplace ||
                string.IsNullOrEmpty(replacementPrototypeId))
            {
                return false;
            }

            try
            {
                EnsureReflectionInitialized();

                if (_addCarInternalMethod == null || _snapshotCarPrototypeIdField == null)
                {
                    FuseLog.Warning(
                        "FUSE replacement spawn skipped: AddCarInternal or Snapshot.Car.prototypeId reflection not initialized.");
                    return false;
                }

                var trainController = _trainControllerSharedProperty?.GetValue(null, null);
                if (trainController == null)
                {
                    FuseLog.Warning("FUSE replacement spawn skipped: TrainController.Shared is null.");
                    return false;
                }

                // The snapshot car is a STRUCT. The registry holds a
                // boxed copy. Mutate the boxed copy in place, then
                // pass it back to AddCarInternal. The game's loader
                // will unbox into a fresh local and process it as if
                // the save had declared the new prototype from the
                // start.
                var snapshotCarBox = fault.OriginalSnapshotCar;
                _snapshotCarPrototypeIdField.SetValue(snapshotCarBox, replacementPrototypeId);

                _addCarInternalMethod.Invoke(trainController, new[]
                {
                    snapshotCarBox,
                    fault.OriginalSnapshotProperties,
                    fault.OriginalSnapshotVersion
                });

                FuseSaveCarFaultRegistry.RemoveByCarId(fault.CarId);

                FuseLog.Info(
                    $"FUSE replaced orphan car '{fault.DisplayName}' id='{fault.CarId}' " +
                    $"missingPrototype='{fault.MissingPrototypeId}' with prototype='{replacementPrototypeId}'.");
                return true;
            }
            catch (TargetInvocationException tex)
            {
                FuseLog.Warning(
                    $"FUSE replacement spawn for '{fault.DisplayName}' (id='{fault.CarId}') " +
                    $"with prototype='{replacementPrototypeId}' failed: " +
                    $"{tex.InnerException?.Message ?? tex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE replacement spawn for '{fault.DisplayName}' (id='{fault.CarId}') " +
                    $"with prototype='{replacementPrototypeId}' failed: {ex.GetBaseException().Message}");
                return false;
            }
        }

        private static void EnsureReflectionInitialized()
        {
            if (_reflectionInitialized)
            {
                return;
            }

            lock (Sync)
            {
                if (_reflectionInitialized)
                {
                    return;
                }

                _trainControllerType = AccessTools.TypeByName("Model.TrainController")
                                       ?? AccessTools.TypeByName("RollingStock.TrainController")
                                       ?? AccessTools.TypeByName("TrainController");
                if (_trainControllerType != null)
                {
                    _addCarInternalMethod = _trainControllerType.GetMethod(
                        "AddCarInternal",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _trainControllerSharedProperty = _trainControllerType.GetProperty(
                        "Shared",
                        BindingFlags.Public | BindingFlags.Static);
                }

                // Snapshot.Car lives in the same assembly as
                // TrainController. Walk the AddCarInternal signature
                // to discover the snapshot car type — that's the
                // most resilient way to reach it across host
                // refactors that rename the containing namespace.
                if (_addCarInternalMethod != null)
                {
                    var parameters = _addCarInternalMethod.GetParameters();
                    if (parameters.Length > 0)
                    {
                        var snapshotCarType = parameters[0].ParameterType;
                        _snapshotCarPrototypeIdField = snapshotCarType.GetField(
                            "prototypeId",
                            BindingFlags.Public | BindingFlags.Instance);
                    }
                }

                _reflectionInitialized = true;
            }
        }

        private static object TryGetPrefabStore()
        {
            try
            {
                EnsureReflectionInitialized();
                var shared = _trainControllerSharedProperty?.GetValue(null, null);
                if (shared == null)
                {
                    return null;
                }
                var prefabStoreProp = _trainControllerType.GetProperty("PrefabStore",
                    BindingFlags.Public | BindingFlags.Instance);
                return prefabStoreProp?.GetValue(shared, null);
            }
            catch
            {
                return null;
            }
        }
    }
}
