using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AssetPack.Runtime;
using Audio;
using KeyValue.Runtime;
using Model;
using Model.Database;
using Model.Definition;
using Model.Definition.Components;
using Model.Definition.Data;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Loading;
using RollingStock;
using RollingStock.Steam;
using UnityEngine;
using UnityEngine.Networking;

namespace FUSE.Runtime.API
{
    public static class FuseAudioAPI
    {
        public const string HornCustomKey = "fuse.horn.custom";
        public const string BellCustomKey = "fuse.bell.custom";

        // Strange-Customs era saves persist per-loco horn/bell selections
        // under these unprefixed keys (matching what SC's own observer
        // wrote at the time). FUSE's customize UI writes the new prefixed
        // form to avoid name collisions inside the FUSE config namespace,
        // but we MUST also read the legacy keys when restoring from an
        // existing save — otherwise every loco that had a custom horn
        // saved under SC silently falls back to default because nothing
        // is observing the SC key.
        public const string LegacyHornCustomKey = "horn.custom";
        public const string LegacyBellCustomKey = "bell.custom";

        // Reflective handles for WhistleController's private state. Used by
        // <see cref="TryConfigureWhistle"/>'s model-load branch to swap the
        // default whistle prefab on the loco cab for a FUSE-loaded one
        // (legacy SC-era whistle packs ship a <c>model</c> AssetReference
        // pointing at an asset pack — e.g. <c>audio.whistles01/3ChimeD</c> —
        // and the visible whistle on the boiler is supposed to come from
        // there). Going through reflection rather than another Harmony
        // patch keeps us out of the JIT-shared generic-method hazard that
        // <c>PrefabStore.DefinitionForIdentifier&lt;T&gt;</c> exposed
        // (patching the closed generic for one T poisons every other T,
        // breaking scenery / material / car loading wholesale).
        private static readonly FieldInfo WhistleControllerWhistleModelField =
            typeof(WhistleController).GetField("_whistleModel", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo WhistleControllerModelLoadReferenceField =
            typeof(WhistleController).GetField("_modelLoadReference", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo WhistleControllerLoadCtsField =
            typeof(WhistleController).GetField("_loadCancellationTokenSource", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly Dictionary<string, RegisteredWhistle> Whistles = new Dictionary<string, RegisteredWhistle>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, RegisteredHorn> Horns = new Dictionary<string, RegisteredHorn>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, RegisteredBell> Bells = new Dictionary<string, RegisteredBell>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AudioClip> ClipCache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<Action<AudioClip>>> ClipRequests = new Dictionary<string, List<Action<AudioClip>>>(StringComparer.OrdinalIgnoreCase);

        private static readonly FieldInfo HornSourceAField = typeof(HornPlayer).GetField("_sourceA", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo HornSourceBField = typeof(HornPlayer).GetField("_sourceB", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo BellPrepareClipsMethod = typeof(IntegerLoopingPlayer).GetMethod("PrepareClips", BindingFlags.Instance | BindingFlags.NonPublic);

        private static FuseAudioRuntimeHost _host;

        public static bool HasWhistles => Whistles.Count > 0;
        public static bool HasHorns => Horns.Count > 0;
        public static bool HasBells => Bells.Count > 0;

        public static void RegisterDefinition(string packageId, FuseAudioRoot audio, string packageFolder, FuseApplyTransaction transaction = null)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                transaction?.Warning("audio", string.Empty, "package id is missing");
                return;
            }

            ReleasePackage(packageId);

            if (audio == null)
            {
                return;
            }

            RegisterWhistles(packageId, audio, packageFolder, transaction);
            RegisterHorns(packageId, audio, packageFolder, transaction);
            RegisterBells(packageId, audio, packageFolder, transaction);
            AttachRuntimeControllers();

            FuseLog.Info(
                $"FUSE audio package='{packageId}' operation='register audio' " +
                $"whistles={CountPackage(Whistles, packageId)} horns={CountPackage(Horns, packageId)} bells={CountPackage(Bells, packageId)}.");
            FuseWhistleDefinitionStore.Sync(SnapshotWhistleStoreEntries());
        }

        public static void ReleasePackage(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return;
            }

            var whistles = RemovePackageEntries(Whistles, packageId);
            var horns = RemovePackageEntries(Horns, packageId);
            var bells = RemovePackageEntries(Bells, packageId);
            if (whistles + horns + bells > 0)
            {
                FuseLog.Info(
                    $"FUSE audio package='{packageId}' operation='release audio definitions' " +
                    $"whistles={whistles} horns={horns} bells={bells}.");
            }

            if (whistles > 0)
            {
                FuseWhistleDefinitionStore.Sync(SnapshotWhistleStoreEntries());
            }
        }

        public static IReadOnlyList<FuseAudioChoice> HornChoices()
        {
            return Horns.Values
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new FuseAudioChoice(entry.Id, entry.Name))
                .ToArray();
        }

        public static IReadOnlyList<FuseAudioChoice> BellChoices()
        {
            return Bells.Values
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new FuseAudioChoice(entry.Id, entry.Name))
                .ToArray();
        }

        /// <summary>
        /// Snapshot of the registered whistles in picker order, carrying the
        /// facts <see cref="FuseWhistleDefinitionStore"/> serializes into the
        /// generated direct store that vanilla's whistle dropdown enumerates.
        /// The generated definitions keep the audio reference empty on
        /// purpose — see <see cref="FuseWhistleDefinitionStore"/> — so
        /// vanilla never races <see cref="TryConfigureWhistle"/> +
        /// <see cref="LoadClip"/> on the clip.
        /// </summary>
        internal static IReadOnlyList<FuseWhistleStoreEntry> SnapshotWhistleStoreEntries()
        {
            return Whistles.Values
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new FuseWhistleStoreEntry(
                    entry.Id,
                    entry.Name,
                    entry.Model?.AssetPackIdentifier,
                    entry.Model?.AssetIdentifier))
                .ToArray();
        }

        public static bool TryConfigureWhistle(WhistleController controller, string whistleId)
        {
            if (controller == null || string.IsNullOrWhiteSpace(whistleId))
            {
                return false;
            }

            RegisteredWhistle whistle;
            if (!Whistles.TryGetValue(whistleId, out whistle))
            {
                return false;
            }

            LoadClip(whistle.ClipPath, $"whistle '{whistle.Id}'", clip =>
            {
                if (controller == null || controller.whistlePlayer == null || clip == null)
                {
                    return;
                }

                var player = controller.whistlePlayer;
                var profile = player.profile != null
                    ? UnityEngine.Object.Instantiate(player.profile)
                    : ScriptableObject.CreateInstance<WhistleProfile>();
                profile.name = "FUSE " + whistle.Name;
                profile.audioClip = clip;
                if (whistle.Definition.RampUpPitch.HasValue)
                {
                    profile.rampUpPitch = whistle.Definition.RampUpPitch.Value;
                }

                if (whistle.Definition.LerpSpeed.HasValue)
                {
                    profile.lerpSpeed = whistle.Definition.LerpSpeed.Value;
                }

                if (whistle.Definition.AirLerpSpeed.HasValue)
                {
                    profile.airLerpSpeed = whistle.Definition.AirLerpSpeed.Value;
                }

                player.profile = profile;
                player.Configure(clip);
                FuseLog.Info($"FUSE audio package='{whistle.PackageId}' operation='configure whistle' id='{whistle.Id}' clip='{whistle.ClipPath}'.");
            });

            // Swap the 3D whistle prefab on the loco cab too. Vanilla
            // Configure normally does this from the WhistleDefinition.Model
            // branch, but FuseWhistleControllerConfigurePatch suppresses
            // vanilla entirely when FUSE owns the audio (vanilla's
            // DefinitionForIdentifier walks asset-pack stores and throws
            // UnknownIdentifierException for FUSE whistle ids). Fire and
            // forget — model load is async, and the controller may also
            // be torn down by car removal before the load completes.
            if (whistle.Model != null && !string.IsNullOrWhiteSpace(whistle.Model.AssetIdentifier))
            {
                _ = LoadAndAttachWhistleModelAsync(controller, whistle);
            }

            return true;
        }

        private static async Task LoadAndAttachWhistleModelAsync(WhistleController controller, RegisteredWhistle whistle)
        {
            try
            {
                if (controller == null || whistle?.Model == null)
                {
                    return;
                }

                if (WhistleControllerWhistleModelField == null ||
                    WhistleControllerModelLoadReferenceField == null ||
                    WhistleControllerLoadCtsField == null)
                {
                    FuseLog.Warning(
                        $"FUSE audio could not attach whistle model for whistle '{whistle.Id}': " +
                        "WhistleController private fields not resolvable via reflection (game IL may have changed).");
                    return;
                }

                // Cancel and dispose anything an earlier whistle swap on
                // this controller might have started, then null the fields
                // so this load owns them cleanly. Mirrors the first three
                // statements of vanilla's async Configure.
                var prevCts = WhistleControllerLoadCtsField.GetValue(controller) as CancellationTokenSource;
                try { prevCts?.Cancel(); } catch { }

                var prevModel = WhistleControllerWhistleModelField.GetValue(controller) as GameObject;
                if (prevModel != null)
                {
                    UnityEngine.Object.Destroy(prevModel);
                }
                WhistleControllerWhistleModelField.SetValue(controller, null);

                var prevRef = WhistleControllerModelLoadReferenceField.GetValue(controller) as IDisposable;
                try { prevRef?.Dispose(); } catch { }
                WhistleControllerModelLoadReferenceField.SetValue(controller, null);

                var cts = new CancellationTokenSource();
                WhistleControllerLoadCtsField.SetValue(controller, cts);
                var token = cts.Token;

                var prefabStore = TrainController.Shared?.PrefabStore;
                if (prefabStore == null)
                {
                    FuseLog.Warning(
                        $"FUSE audio could not attach whistle model for whistle '{whistle.Id}': " +
                        "TrainController.Shared.PrefabStore was null.");
                    return;
                }

                // This generic method is GENERIC over T but we are CALLING
                // it, not patching it — there is no JIT-shared-IL hazard.
                // The result is a LoadedAssetReference<GameObject> we have
                // to keep alive (assigned into _modelLoadReference) so the
                // asset pack store does not free the prefab while the
                // instantiated copy is still on the cab.
                var loadedRef = await prefabStore.LoadAssetAsync<GameObject>(
                    whistle.Model.AssetPackIdentifier,
                    whistle.Model.AssetIdentifier,
                    token);

                if (controller == null || token.IsCancellationRequested || loadedRef?.Asset == null)
                {
                    try { loadedRef?.Dispose(); } catch { }
                    return;
                }

                var model = UnityEngine.Object.Instantiate(loadedRef.Asset, controller.transform, worldPositionStays: false);
                WhistleControllerWhistleModelField.SetValue(controller, model);
                WhistleControllerModelLoadReferenceField.SetValue(controller, loadedRef);

                FuseLog.Info(
                    $"FUSE audio package='{whistle.PackageId}' operation='attach whistle model' id='{whistle.Id}' " +
                    $"assetPack='{whistle.Model.AssetPackIdentifier}' asset='{whistle.Model.AssetIdentifier}'.");
            }
            catch (OperationCanceledException)
            {
                // Expected when another swap interrupts us.
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    $"FUSE audio failed to load/attach whistle model for whistle '{whistle?.Id ?? "<null>"}'",
                    ex);
            }
        }

        internal static void AttachRuntimeControllers()
        {
            try
            {
                foreach (var car in UnityEngine.Object.FindObjectsOfType<Car>(true))
                {
                    AttachRuntimeControllers(car);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE audio failed while attaching runtime controllers to loaded cars", ex);
            }
        }

        internal static void AttachRuntimeControllers(Car car)
        {
            if (car == null || car.Definition?.Components == null)
            {
                return;
            }

            if (car.Definition.Components.OfType<HornComponent>().Any())
            {
                var controller = car.GetComponent<FuseHornRuntimeController>() ?? car.gameObject.AddComponent<FuseHornRuntimeController>();
                controller.Configure(car);
            }

            if (car.Definition.Components.OfType<BellComponent>().Any())
            {
                var controller = car.GetComponent<FuseBellRuntimeController>() ?? car.gameObject.AddComponent<FuseBellRuntimeController>();
                controller.Configure(car);
            }
        }

        internal static void ApplyHornSelection(FuseHornRuntimeController controller, HornPlayer player, string hornId)
        {
            if (controller == null || player == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(hornId) || string.Equals(hornId, "default", StringComparison.OrdinalIgnoreCase))
            {
                controller.RestoreDefault();
                return;
            }

            RegisteredHorn horn;
            if (!Horns.TryGetValue(hornId, out horn))
            {
                FuseLog.Warning($"FUSE audio package='<runtime>' operation='configure horn' id='{hornId}' warning='unknown horn id'.");
                controller.RestoreDefault();
                return;
            }

            BuildHornProfile(horn, profile =>
            {
                if (controller == null || player == null || profile == null || !string.Equals(controller.CurrentHornId, horn.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                player.profile = profile;
                UpdateHornSources(player, profile);
                FuseLog.Info($"FUSE audio package='{horn.PackageId}' operation='configure horn' id='{horn.Id}'.");
            });
        }

        internal static void ApplyBellSelection(FuseBellRuntimeController controller, Bell bell, string bellId)
        {
            if (controller == null || bell?.player == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(bellId) || string.Equals(bellId, "default", StringComparison.OrdinalIgnoreCase))
            {
                controller.RestoreDefault();
                return;
            }

            RegisteredBell entry;
            if (!Bells.TryGetValue(bellId, out entry))
            {
                FuseLog.Warning($"FUSE audio package='<runtime>' operation='configure bell' id='{bellId}' warning='unknown bell id'.");
                controller.RestoreDefault();
                return;
            }

            LoadClip(entry.FilePath, $"bell '{entry.Id}'", clip =>
            {
                if (controller == null || bell == null || bell.player == null || clip == null ||
                    !string.Equals(controller.CurrentBellId, entry.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var descriptor = ScriptableObject.CreateInstance<IndexedClipDescriptor>();
                descriptor.name = "FUSE " + entry.Name;
                descriptor.clip = clip;
                descriptor.indexes = (entry.Definition.IndexTimes ?? Array.Empty<float>())
                    .Where(time => time >= 0f)
                    .Select(time => new IndexedClipDescriptor.Index { time = time })
                    .ToArray();

                bell.player.indexedClip = descriptor;
                BellPrepareClipsMethod?.Invoke(bell.player, null);
                FuseLog.Info($"FUSE audio package='{entry.PackageId}' operation='configure bell' id='{entry.Id}' clip='{entry.FilePath}'.");
            });
        }

        private static void RegisterWhistles(string packageId, FuseAudioRoot audio, string packageFolder, FuseApplyTransaction transaction)
        {
            foreach (var item in audio.Whistles ?? new Dictionary<string, FuseWhistleAudio>())
            {
                var id = NormalizeId(item.Key, "whistle");
                var definition = item.Value;
                if (definition == null)
                {
                    transaction?.Skipped("audio whistle", id, "definition missing");
                    continue;
                }

                var clip = ResolveAudioPath(packageFolder, definition.Clip);
                if (string.IsNullOrWhiteSpace(clip) || !File.Exists(clip))
                {
                    transaction?.Skipped("audio whistle", id, "clip missing");
                    FuseLog.Warning($"FUSE audio package='{packageId}' operation='register whistle' id='{id}' warning='clip missing' path='{clip ?? string.Empty}'.");
                    continue;
                }

                Whistles[id] = new RegisteredWhistle(packageId, id, SafeName(definition.Name, id), clip, definition.Model, definition);
                transaction?.Created("audio whistle", id);
            }
        }

        private static void RegisterHorns(string packageId, FuseAudioRoot audio, string packageFolder, FuseApplyTransaction transaction)
        {
            foreach (var item in audio.Horns ?? new Dictionary<string, FuseHornAudio>())
            {
                var id = NormalizeId(item.Key, "horn");
                var definition = item.Value;
                if (definition == null)
                {
                    transaction?.Skipped("audio horn", id, "definition missing");
                    continue;
                }

                var layerPaths = (definition.Layers ?? Array.Empty<FuseHornLayer>())
                    .Select(layer => layer == null ? string.Empty : ResolveAudioPath(packageFolder, layer.File))
                    .ToArray();
                var missing = layerPaths.Where(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)).ToArray();
                if (layerPaths.Length == 0 || missing.Length > 0)
                {
                    transaction?.Skipped("audio horn", id, "one or more layer clips are missing");
                    FuseLog.Warning($"FUSE audio package='{packageId}' operation='register horn' id='{id}' warning='layer clip missing' missing='{string.Join(", ", missing)}'.");
                    continue;
                }

                Horns[id] = new RegisteredHorn(packageId, id, SafeName(definition.Name, id), definition, layerPaths);
                transaction?.Created("audio horn", id);
            }
        }

        private static void RegisterBells(string packageId, FuseAudioRoot audio, string packageFolder, FuseApplyTransaction transaction)
        {
            foreach (var item in audio.Bells ?? new Dictionary<string, FuseBellAudio>())
            {
                var id = NormalizeId(item.Key, "bell");
                var definition = item.Value;
                if (definition == null)
                {
                    transaction?.Skipped("audio bell", id, "definition missing");
                    continue;
                }

                var file = ResolveAudioPath(packageFolder, definition.File);
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                {
                    transaction?.Skipped("audio bell", id, "clip missing");
                    FuseLog.Warning($"FUSE audio package='{packageId}' operation='register bell' id='{id}' warning='clip missing' path='{file ?? string.Empty}'.");
                    continue;
                }

                Bells[id] = new RegisteredBell(packageId, id, SafeName(definition.Name, id), definition, file);
                transaction?.Created("audio bell", id);
            }
        }

        private static void BuildHornProfile(RegisteredHorn horn, Action<HornProfile> callback)
        {
            var layers = horn.Definition.Layers ?? Array.Empty<FuseHornLayer>();
            if (layers.Length == 0)
            {
                callback(null);
                return;
            }

            var layerCount = Math.Max(2, layers.Length);
            var clips = new AudioClip[layerCount];
            var remaining = layerCount;
            for (var index = 0; index < layerCount; index++)
            {
                var targetIndex = index;
                var sourceIndex = Math.Min(index, layers.Length - 1);
                LoadClip(horn.LayerPaths[sourceIndex], $"horn '{horn.Id}' layer {sourceIndex}", clip =>
                {
                    clips[targetIndex] = clip;
                    remaining--;
                    if (remaining > 0)
                    {
                        return;
                    }

                    if (clips.Any(item => item == null))
                    {
                        callback(null);
                        return;
                    }

                    var profile = ScriptableObject.CreateInstance<HornProfile>();
                    profile.name = "FUSE " + horn.Name;
                    profile.layers = new HornProfile.Layer[layerCount];
                    for (var layerIndex = 0; layerIndex < layerCount; layerIndex++)
                    {
                        var sourceLayerIndex = Math.Min(layerIndex, layers.Length - 1);
                        profile.layers[layerIndex] = new HornProfile.Layer
                        {
                            clip = clips[sourceLayerIndex],
                            volumeCurve = CreateCurve(layers[sourceLayerIndex]?.Keyframes)
                        };
                    }

                    callback(profile);
                });
            }
        }

        private static AnimationCurve CreateCurve(FuseAudioKeyframe[] keyframes)
        {
            if (keyframes == null || keyframes.Length == 0)
            {
                return AnimationCurve.Constant(0f, 1f, 1f);
            }

            return new AnimationCurve(keyframes
                .OrderBy(keyframe => keyframe.T)
                .Select(keyframe => new Keyframe(keyframe.T, keyframe.Value))
                .ToArray());
        }

        private static void UpdateHornSources(HornPlayer player, HornProfile profile)
        {
            if (player == null || profile?.layers == null || profile.layers.Length < 2)
            {
                return;
            }

            try
            {
                var sourceA = HornSourceAField?.GetValue(player) as IAudioSource;
                var sourceB = HornSourceBField?.GetValue(player) as IAudioSource;
                if (sourceA != null)
                {
                    sourceA.clip = profile.layers[0].clip;
                }

                if (sourceB != null)
                {
                    sourceB.clip = profile.layers[1].clip;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE audio operation='update horn sources' warning=''.", ex);
            }
        }

        private static void LoadClip(string path, string label, Action<AudioClip> callback)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                FuseLog.Warning($"FUSE audio operation='load clip' id='{label}' warning='blank path'.");
                callback?.Invoke(null);
                return;
            }

            var fullPath = Path.GetFullPath(path);
            AudioClip cached;
            if (ClipCache.TryGetValue(fullPath, out cached) && cached != null)
            {
                callback?.Invoke(cached);
                return;
            }

            List<Action<AudioClip>> waiting;
            if (ClipRequests.TryGetValue(fullPath, out waiting))
            {
                waiting.Add(callback);
                return;
            }

            ClipRequests[fullPath] = new List<Action<AudioClip>> { callback };
            EnsureHost().StartCoroutine(LoadClipCoroutine(fullPath, label));
        }

        private static IEnumerator LoadClipCoroutine(string fullPath, string label)
        {
            AudioClip clip = null;
            if (!File.Exists(fullPath))
            {
                FuseLog.Warning($"FUSE audio operation='load clip' id='{label}' warning='file not found' path='{fullPath}'.");
            }
            else
            {
                var uri = new Uri(fullPath).AbsoluteUri;
                using (var request = UnityWebRequestMultimedia.GetAudioClip(uri, GetAudioType(fullPath)))
                {
                    yield return request.SendWebRequest();
                    if (request.isNetworkError || request.isHttpError)
                    {
                        FuseLog.Warning($"FUSE audio operation='load clip' id='{label}' warning='{request.error}' path='{fullPath}'.");
                    }
                    else
                    {
                        clip = DownloadHandlerAudioClip.GetContent(request);
                        if (clip != null)
                        {
                            clip.name = Path.GetFileNameWithoutExtension(fullPath);
                            ClipCache[fullPath] = clip;
                        }
                    }
                }
            }

            List<Action<AudioClip>> callbacks;
            if (ClipRequests.TryGetValue(fullPath, out callbacks))
            {
                ClipRequests.Remove(fullPath);
                foreach (var callback in callbacks)
                {
                    try
                    {
                        callback?.Invoke(clip);
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Exception($"FUSE audio operation='clip callback' id='{label}' warning=''.", ex);
                    }
                }
            }
        }

        private static AudioType GetAudioType(string path)
        {
            switch ((Path.GetExtension(path) ?? string.Empty).ToLowerInvariant())
            {
                case ".mp3":
                    return AudioType.MPEG;
                case ".ogg":
                    return AudioType.OGGVORBIS;
                case ".wav":
                    return AudioType.WAV;
                case ".aiff":
                case ".aif":
                    return AudioType.AIFF;
                default:
                    return AudioType.UNKNOWN;
            }
        }

        private static FuseAudioRuntimeHost EnsureHost()
        {
            if (_host != null)
            {
                return _host;
            }

            var go = new GameObject("FUSE Audio Runtime");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _host = go.AddComponent<FuseAudioRuntimeHost>();
            return _host;
        }

        private static string ResolveAudioPath(string packageFolder, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = value.Trim();
            if (text.StartsWith("file(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(")", StringComparison.Ordinal))
            {
                text = text.Substring(5, text.Length - 6);
            }

            text = text.Trim().Trim('"', '\'');
            if (Path.IsPathRooted(text))
            {
                return Path.GetFullPath(text);
            }

            return Path.GetFullPath(Path.Combine(packageFolder ?? string.Empty, text));
        }

        private static string NormalizeId(string id, string fallback)
        {
            return string.IsNullOrWhiteSpace(id) ? fallback : id.Trim();
        }

        private static string SafeName(string name, string id)
        {
            return string.IsNullOrWhiteSpace(name) ? id : name.Trim();
        }

        private static int CountPackage<T>(Dictionary<string, T> registry, string packageId) where T : RegisteredAudio
        {
            return registry.Values.Count(entry => string.Equals(entry.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
        }

        private static int RemovePackageEntries<T>(Dictionary<string, T> registry, string packageId) where T : RegisteredAudio
        {
            var removed = 0;
            foreach (var key in registry.Where(pair => string.Equals(pair.Value.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                registry.Remove(key);
                removed++;
            }

            return removed;
        }

        private abstract class RegisteredAudio
        {
            protected RegisteredAudio(string packageId, string id, string name)
            {
                PackageId = packageId;
                Id = id;
                Name = name;
            }

            public string PackageId { get; }
            public string Id { get; }
            public string Name { get; }
        }

        private sealed class RegisteredWhistle : RegisteredAudio
        {
            public RegisteredWhistle(string packageId, string id, string name, string clipPath, FuseAudioAssetReference model, FuseWhistleAudio definition)
                : base(packageId, id, name)
            {
                ClipPath = clipPath;
                Model = model;
                Definition = definition;
            }

            public string ClipPath { get; }
            public FuseAudioAssetReference Model { get; }
            public FuseWhistleAudio Definition { get; }
        }

        private sealed class RegisteredHorn : RegisteredAudio
        {
            public RegisteredHorn(string packageId, string id, string name, FuseHornAudio definition, string[] layerPaths)
                : base(packageId, id, name)
            {
                Definition = definition;
                LayerPaths = layerPaths;
            }

            public FuseHornAudio Definition { get; }
            public string[] LayerPaths { get; }
        }

        private sealed class RegisteredBell : RegisteredAudio
        {
            public RegisteredBell(string packageId, string id, string name, FuseBellAudio definition, string filePath)
                : base(packageId, id, name)
            {
                Definition = definition;
                FilePath = filePath;
            }

            public FuseBellAudio Definition { get; }
            public string FilePath { get; }
        }
    }

    public sealed class FuseAudioChoice
    {
        public FuseAudioChoice(string id, string name)
        {
            Id = id ?? string.Empty;
            Name = name ?? Id;
        }

        public string Id { get; }
        public string Name { get; }
    }

    internal sealed class FuseAudioRuntimeHost : MonoBehaviour
    {
    }

    internal sealed class FuseHornRuntimeController : MonoBehaviour
    {
        // Observe BOTH the FUSE-prefixed key (where the FUSE customize UI
        // writes new selections) and the legacy SC-era unprefixed key
        // (where existing saves persisted their selection). Whichever
        // observer fires re-evaluates the effective selection so a save
        // with only horn.custom set still applies the right horn at
        // load time, and a player who picks via the FUSE UI immediately
        // overrides any legacy value.
        private IDisposable _observerFuse;
        private IDisposable _observerLegacy;
        private Car _car;
        private HornPlayer _player;
        private HornProfile _defaultProfile;

        public string CurrentHornId { get; private set; }

        public void Configure(Car car)
        {
            if (car == null)
            {
                return;
            }

            _car = car;
            _player = car.GetComponentInChildren<HornPlayer>(true);
            _defaultProfile = _player != null ? _player.profile : null;
            if (car.KeyValueObject != null)
            {
                _observerFuse?.Dispose();
                _observerLegacy?.Dispose();
                _observerFuse = car.KeyValueObject.Observe(FuseAudioAPI.HornCustomKey, OnSelectionChanged, true);
                // Skip callInitial on the legacy observer — the FUSE-key
                // observer already triggered an evaluation a moment ago,
                // and ComputeEffectiveSelection reads both keys so we'd
                // just double-fire the same effective value.
                _observerLegacy = car.KeyValueObject.Observe(FuseAudioAPI.LegacyHornCustomKey, OnSelectionChanged, false);
            }
        }

        public void RestoreDefault()
        {
            if (_player != null && _defaultProfile != null)
            {
                _player.profile = _defaultProfile;
            }
        }

        private void OnDestroy()
        {
            _observerFuse?.Dispose();
            _observerLegacy?.Dispose();
            _observerFuse = null;
            _observerLegacy = null;
        }

        private void OnSelectionChanged(Value value)
        {
            // Re-read both keys rather than trusting the observer's value
            // argument: when only the legacy key was set in a save, the
            // FUSE-key observer fires first with an empty value (which
            // would otherwise restore default and clobber the legacy
            // selection a moment later).
            CurrentHornId = ComputeEffectiveSelection();
            FuseAudioAPI.ApplyHornSelection(this, _player, CurrentHornId);
        }

        private string ComputeEffectiveSelection()
        {
            if (_car?.KeyValueObject == null)
            {
                return string.Empty;
            }
            var preferred = _car.KeyValueObject[FuseAudioAPI.HornCustomKey].StringValue;
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                return preferred;
            }
            return _car.KeyValueObject[FuseAudioAPI.LegacyHornCustomKey].StringValue ?? string.Empty;
        }
    }

    internal sealed class FuseBellRuntimeController : MonoBehaviour
    {
        // See FuseHornRuntimeController above for why we observe both
        // the FUSE-prefixed and the legacy SC-era keys.
        private IDisposable _observerFuse;
        private IDisposable _observerLegacy;
        private Car _car;
        private Bell _bell;
        private IndexedClipDescriptor _defaultClip;

        public string CurrentBellId { get; private set; }

        public void Configure(Car car)
        {
            if (car == null)
            {
                return;
            }

            _car = car;
            _bell = car.GetComponentInChildren<Bell>(true);
            _defaultClip = _bell?.player != null ? _bell.player.indexedClip : null;
            if (car.KeyValueObject != null)
            {
                _observerFuse?.Dispose();
                _observerLegacy?.Dispose();
                _observerFuse = car.KeyValueObject.Observe(FuseAudioAPI.BellCustomKey, OnSelectionChanged, true);
                _observerLegacy = car.KeyValueObject.Observe(FuseAudioAPI.LegacyBellCustomKey, OnSelectionChanged, false);
            }
        }

        public void RestoreDefault()
        {
            if (_bell?.player != null && _defaultClip != null)
            {
                _bell.player.indexedClip = _defaultClip;
            }
        }

        private void OnDestroy()
        {
            _observerFuse?.Dispose();
            _observerLegacy?.Dispose();
            _observerFuse = null;
            _observerLegacy = null;
        }

        private void OnSelectionChanged(Value value)
        {
            CurrentBellId = ComputeEffectiveSelection();
            FuseAudioAPI.ApplyBellSelection(this, _bell, CurrentBellId);
        }

        private string ComputeEffectiveSelection()
        {
            if (_car?.KeyValueObject == null)
            {
                return string.Empty;
            }
            var preferred = _car.KeyValueObject[FuseAudioAPI.BellCustomKey].StringValue;
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                return preferred;
            }
            return _car.KeyValueObject[FuseAudioAPI.LegacyBellCustomKey].StringValue ?? string.Empty;
        }
    }
}
