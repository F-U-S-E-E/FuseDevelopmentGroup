using System;
using System.Reflection;
using System.Threading;
using Audio;
using FUSE.Infrastructure;
using HarmonyLib;
using Model;
using Model.Physics;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Compatibility between Bman's shared GP38Scripts and Prime Mover Audio
    /// Replacer. Both mods postfix <see cref="PrimeMoverAudioPlayer.OnEnable"/>:
    /// the replacer enables the player after assigning its disk-loaded profile,
    /// then GP38Scripts replaces that profile with the locomotive's embedded
    /// default. Running last restores the selected profile and the active-notch
    /// clips. The selection stays in the replacer's own ACPM key and therefore
    /// continues to save normally.
    /// </summary>
    [HarmonyPatch(typeof(PrimeMoverAudioPlayer), "OnEnable")]
    internal static class FusePrimeMoverAudioSelectionCompatibilityPatch
    {
        private const string SelectionKey = "ACPM";
        private const string CustomAudioTypeName =
            "PrimeMoverAudioReplacer.AudioPatch.CustomPrimeMoverAudio";

        private static readonly FieldInfo LoopSourceAField =
            AccessTools.Field(typeof(PrimeMoverAudioPlayer), "_loopSourceA");
        private static readonly FieldInfo LoopSourceBField =
            AccessTools.Field(typeof(PrimeMoverAudioPlayer), "_loopSourceB");

        private static Type _customAudioType;
        private static FieldInfo _customProfileField;
        private static FieldInfo _primaryPlayerField;
        private static int _restoredCount;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PrimeMoverAudioPlayer __instance)
        {
            try
            {
                var car = __instance?.GetComponentInParent<Car>();
                if (car == null || car.KeyValueObject == null)
                {
                    return;
                }

                var selection = car.KeyValueObject[SelectionKey].StringValue;
                if (!HasCustomSelection(selection))
                {
                    SetEmbeddedExhaustEnabled(car, enabled: true);
                    return;
                }

                if (!ResolveOptionalSurface())
                {
                    return;
                }

                var customAudio = car.GetComponentInChildren(_customAudioType, true);
                if (customAudio == null ||
                    !ReferenceEquals(_primaryPlayerField.GetValue(customAudio), __instance) ||
                    !(_customProfileField.GetValue(customAudio) is PrimeMoverAudioProfile customProfile))
                {
                    return;
                }

                __instance.profile = customProfile;
                RestoreActiveNotchClip(
                    __instance,
                    customProfile,
                    LoopSourceAField,
                    resumePlayback: true);
                RestoreActiveNotchClip(
                    __instance,
                    customProfile,
                    LoopSourceBField,
                    resumePlayback: false);

                // Bman's locomotives carry a second embedded exhaust-loop
                // controller. It is independent of PrimeMoverAudioPlayer, so
                // leaving it active makes the built-in engine recording play
                // over the user's selected replacement. Restore it when ACPM
                // is cleared and suppress it only after a replacement profile
                // has actually loaded successfully.
                SetEmbeddedExhaustEnabled(car, enabled: false);

                var count = Interlocked.Increment(ref _restoredCount);
                if (FuseGuardLog.ShouldLog(count))
                {
                    FuseLog.Info(
                        $"FUSE preserved selected prime-mover audio '{selection}' on " +
                        $"'{car.DisplayName}' after GP38Scripts activation (#{count}).");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE could not preserve the selected prime-mover audio profile", ex);
            }
        }

        internal static bool HasCustomSelection(string selection) =>
            !string.IsNullOrWhiteSpace(selection);

        private static bool ResolveOptionalSurface()
        {
            if (_customAudioType != null &&
                _customProfileField != null &&
                _primaryPlayerField != null)
            {
                return true;
            }

            var type = AccessTools.TypeByName(CustomAudioTypeName);
            if (type == null)
            {
                return false;
            }

            var customProfile = AccessTools.Field(type, "_customProfile");
            var primaryPlayer = AccessTools.Field(type, "_primaryAudioPlayer");
            if (customProfile == null || primaryPlayer == null)
            {
                return false;
            }

            _customAudioType = type;
            _customProfileField = customProfile;
            _primaryPlayerField = primaryPlayer;
            return true;
        }

        private static void RestoreActiveNotchClip(
            PrimeMoverAudioPlayer player,
            PrimeMoverAudioProfile profile,
            FieldInfo sourceField,
            bool resumePlayback)
        {
            if (sourceField == null || profile?.notchLoops == null || profile.notchLoops.Length == 0)
            {
                return;
            }

            var notch = Math.Max(0, Math.Min(player.Notch, profile.notchLoops.Length - 1));
            if (sourceField.GetValue(player) is IAudioSource source)
            {
                source.clip = profile.notchLoops[notch];
                source.loop = true;
                if (resumePlayback)
                {
                    source.volume = profile.volume;
                    if (!source.isPlaying)
                    {
                        source.Play();
                    }
                }
                else
                {
                    source.volume = 0f;
                }
            }
        }

        private static void SetEmbeddedExhaustEnabled(Car car, bool enabled)
        {
            var exhaustType = AccessTools.TypeByName(
                FuseBmanLocomotiveAudioCompatibility.ExhaustTypeName);
            if (exhaustType == null)
            {
                return;
            }

            foreach (var component in car.GetComponentsInChildren(exhaustType, true))
            {
                if (component is Behaviour behaviour && behaviour.enabled != enabled)
                {
                    behaviour.enabled = enabled;
                }
            }
        }
    }

    /// <summary>
    /// The model prefab becomes a child of Car before Instantiate returns, but
    /// Car assigns AudioReparenter.BodyTransform immediately after that return.
    /// Model OnEnable callbacks can request pooled audio in the small gap. Infer
    /// the same model root from the requesting transform so every locomotive's
    /// early audio source can be parented correctly.
    /// </summary>
    [HarmonyPatch(typeof(AudioReparenter), nameof(AudioReparenter.Reparent))]
    internal static class FuseAudioReparenterEarlyBodyCompatibilityPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(AudioReparenter __instance, Transform originalParent)
        {
            if (__instance == null || __instance.BodyTransform != null || originalParent == null)
            {
                return;
            }

            var candidate = originalParent;
            while (candidate.parent != null && candidate.parent != __instance.transform)
            {
                candidate = candidate.parent;
            }

            if (candidate.parent == __instance.transform)
            {
                __instance.BodyTransform = candidate;
            }
        }
    }

    /// <summary>
    /// Repairs two null prefab fields shared by Bman's locomotive family. The
    /// guards are installed by name because GP38Scripts is optional.
    /// </summary>
    internal static class FuseBmanLocomotiveAudioCompatibility
    {
        internal const string ExhaustTypeName = "Audio.ExhaustAudioController";
        private const string SmokeTypeName = "GP38Scripts.GP38SmokeController";

        private static FieldInfo _sourceAField;
        private static FieldInfo _sourceBField;
        private static FieldInfo _parentCarField;
        private static bool _exhaustPatched;
        private static bool _smokePatched;

        internal static string EnsureInstalled(Harmony harmony)
        {
            if (_exhaustPatched && _smokePatched)
            {
                return "installed";
            }

            if (harmony == null)
            {
                return "unavailable (no harmony)";
            }

            var exhaustType = AccessTools.TypeByName(ExhaustTypeName);
            var smokeType = AccessTools.TypeByName(SmokeTypeName);
            if (exhaustType == null && smokeType == null)
            {
                return "idle (not present)";
            }

            if (!_exhaustPatched && exhaustType != null)
            {
                var onEnable = AccessTools.DeclaredMethod(exhaustType, "OnEnable", Type.EmptyTypes);
                var sourceA = AccessTools.Field(exhaustType, "sourceA");
                var sourceB = AccessTools.Field(exhaustType, "sourceB");
                if (onEnable != null && sourceA != null && sourceB != null)
                {
                    _sourceAField = sourceA;
                    _sourceBField = sourceB;
                    harmony.Patch(
                        onEnable,
                        prefix: new HarmonyMethod(
                            typeof(FuseBmanLocomotiveAudioCompatibility),
                            nameof(ExhaustOnEnablePrefix)));
                    _exhaustPatched = true;
                }
            }

            if (!_smokePatched && smokeType != null)
            {
                var start = AccessTools.DeclaredMethod(smokeType, "Start", Type.EmptyTypes);
                var parentCar = AccessTools.Field(smokeType, "parentCar");
                if (start != null && parentCar != null)
                {
                    _parentCarField = parentCar;
                    harmony.Patch(
                        start,
                        prefix: new HarmonyMethod(
                            typeof(FuseBmanLocomotiveAudioCompatibility),
                            nameof(SmokeStartPrefix)));
                    _smokePatched = true;
                }
            }

            if (_exhaustPatched || _smokePatched)
            {
                return _exhaustPatched && _smokePatched
                    ? "installed"
                    : "partial (surface changed)";
            }

            return "idle (surface changed)";
        }

        private static void ExhaustOnEnablePrefix(object __instance)
        {
            try
            {
                if (!(__instance is Component component))
                {
                    return;
                }

                EnsureAudioSource(component, _sourceAField);
                EnsureAudioSource(component, _sourceBField);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE could not initialize Bman's exhaust audio sources", ex);
            }
        }

        private static void EnsureAudioSource(Component component, FieldInfo field)
        {
            if (field != null && field.GetValue(component) == null)
            {
                var source = component.gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                field.SetValue(component, source);
            }
        }

        private static void SmokeStartPrefix(object __instance)
        {
            try
            {
                if (__instance is Component component &&
                    _parentCarField != null &&
                    _parentCarField.GetValue(component) == null)
                {
                    _parentCarField.SetValue(component, component.GetComponentInParent<Car>());
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE could not initialize Bman's smoke parent", ex);
            }
        }
    }
}
