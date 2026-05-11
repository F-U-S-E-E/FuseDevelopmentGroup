using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Audio;
using KeyValue.Runtime;
using Model;
using Model.Definition;
using Model.Definition.Components;
using Model.Definition.Data;
using FUSE.Data;
using FUSE.Infrastructure;
using FUSE.Loading;
using RollingStock;
using RollingStock.Steam;
using UnityEngine;
using UnityEngine.Networking;

namespace FUSE.API
{
    public static class FuseAudioAPI
    {
        public const string HornCustomKey = "fuse.horn.custom";
        public const string BellCustomKey = "fuse.bell.custom";

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

        public static IEnumerable<TypedContainerItem<WhistleDefinition>> GetWhistleDefinitionItems()
        {
            foreach (var whistle in Whistles.Values.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
            {
                yield return new TypedContainerItem<WhistleDefinition>
                {
                    Identifier = whistle.Id,
                    Metadata = new ObjectMetadata
                    {
                        Name = string.IsNullOrWhiteSpace(whistle.Name) ? whistle.Id : whistle.Name,
                        Description = "FUSE loose-file whistle"
                    },
                    Definition = new WhistleDefinition
                    {
                        Audio = new AssetReference
                        {
                            // "rail" is Railroader's built-in asset-pack id, not the old RAIL mod name.
                            // FUSE intercepts these loose-file definitions before the base asset loader runs.
                            AssetPackIdentifier = "rail",
                            AssetIdentifier = whistle.Id
                        },
                        Model = ToAssetReference(whistle.Model)
                    }
                };
            }
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

            return true;
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
                FuseLog.Warning($"FUSE audio operation='update horn sources' warning='{ex.Message}'.");
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
                        FuseLog.Warning($"FUSE audio operation='clip callback' id='{label}' warning='{ex.Message}'.");
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

        private static AssetReference ToAssetReference(FuseAudioAssetReference reference)
        {
            return new AssetReference
            {
                AssetPackIdentifier = reference?.AssetPackIdentifier ?? string.Empty,
                AssetIdentifier = reference?.AssetIdentifier ?? string.Empty
            };
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
        private IDisposable _observer;
        private HornPlayer _player;
        private HornProfile _defaultProfile;

        public string CurrentHornId { get; private set; }

        public void Configure(Car car)
        {
            if (car == null)
            {
                return;
            }

            _player = car.GetComponentInChildren<HornPlayer>(true);
            _defaultProfile = _player != null ? _player.profile : null;
            if (car.KeyValueObject != null)
            {
                _observer?.Dispose();
                _observer = car.KeyValueObject.Observe(FuseAudioAPI.HornCustomKey, OnSelectionChanged, true);
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
            _observer?.Dispose();
            _observer = null;
        }

        private void OnSelectionChanged(Value value)
        {
            CurrentHornId = value.StringValue;
            FuseAudioAPI.ApplyHornSelection(this, _player, CurrentHornId);
        }
    }

    internal sealed class FuseBellRuntimeController : MonoBehaviour
    {
        private IDisposable _observer;
        private Bell _bell;
        private IndexedClipDescriptor _defaultClip;

        public string CurrentBellId { get; private set; }

        public void Configure(Car car)
        {
            if (car == null)
            {
                return;
            }

            _bell = car.GetComponentInChildren<Bell>(true);
            _defaultClip = _bell?.player != null ? _bell.player.indexedClip : null;
            if (car.KeyValueObject != null)
            {
                _observer?.Dispose();
                _observer = car.KeyValueObject.Observe(FuseAudioAPI.BellCustomKey, OnSelectionChanged, true);
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
            _observer?.Dispose();
            _observer = null;
        }

        private void OnSelectionChanged(Value value)
        {
            CurrentBellId = value.StringValue;
            FuseAudioAPI.ApplyBellSelection(this, _bell, CurrentBellId);
        }
    }
}
