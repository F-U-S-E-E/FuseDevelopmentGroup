using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using FUSE.Infrastructure;
using FUSE.Loading;
using HarmonyLib;
using Model;
using Model.Ops;
using RollingStock;
using UI;
using UI.Map;
using UI.Tags;
using TMPro;
using UnityEngine;

namespace FUSE.Patches
{
    internal static class FuseForYourConveniencePolicy
    {
        internal static bool IsActive()
        {
            return FuseLegacyCapabilityActivation.IsRequested(
                "Zamu.ForYourConvenience",
                "ForYourConvenience");
        }

        internal static string FormatSpeed(float metersPerSecond)
        {
            return Math.Abs(metersPerSecond * 2.23694f)
                .ToString("0.0", CultureInfo.InvariantCulture) + " MPH";
        }

        internal static string FormatLoad(float quantity, float capacity, string description)
        {
            var percentage = capacity > 0.001f
                ? Mathf.Clamp01(quantity / capacity) * 100f
                : 0f;
            return percentage.ToString("0", CultureInfo.InvariantCulture) + "% " +
                   (description ?? "Unknown load");
        }
    }

    [HarmonyPatch(typeof(Car), nameof(Car.Setup))]
    internal static class FuseForYourConvenienceCabooseMapIconPatch
    {
        private static readonly FieldInfo MapIconField = AccessTools.Field(typeof(Car), "MapIcon");

        private static void Postfix(Car __instance, Car.SetupPrefabs prefabs, bool isGhost)
        {
            if (!FuseForYourConveniencePolicy.IsActive() ||
                !FuseSettings.ForYourConvenienceShowCabooseIcons ||
                isGhost || __instance == null || __instance.CarType != "NE" ||
                prefabs.LocomotiveMapIcon == null || MapIconField == null ||
                MapIconField.GetValue(__instance) != null)
            {
                return;
            }

            try
            {
                var icon = UnityEngine.Object.Instantiate(prefabs.LocomotiveMapIcon, __instance.transform);
                icon.SetText(__instance.Ident.RoadNumber);
                icon.OnClick = () => CarPickable.HandleShowInspector(__instance);
                MapIconField.SetValue(__instance, icon);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE could not add the optional caboose map icon for '" +
                    __instance.DisplayName + "': " + ex.GetBaseException().Message);
            }
        }
    }

    [HarmonyPatch(typeof(TagController), "UpdateTag")]
    internal static class FuseForYourConvenienceCarTagPatch
    {
        private static void Postfix(Car car, TagCallout tagCallout)
        {
            if (!FuseForYourConveniencePolicy.IsActive() || car == null || tagCallout?.callout == null ||
                (!FuseSettings.ForYourConvenienceShowCarTagMph &&
                 !FuseSettings.ForYourConvenienceShowCarTagLoads))
            {
                return;
            }

            try
            {
                var text = new StringBuilder(tagCallout.callout.Text ?? string.Empty);
                if (FuseSettings.ForYourConvenienceShowCarTagMph)
                {
                    AppendLine(text, FuseForYourConveniencePolicy.FormatSpeed(car.velocity));
                }

                if (FuseSettings.ForYourConvenienceShowCarTagLoads && car.Definition?.LoadSlots != null)
                {
                    for (var slotIndex = 0; slotIndex < car.Definition.LoadSlots.Count; slotIndex++)
                    {
                        var info = car.GetLoadInfo(slotIndex);
                        if (!info.HasValue)
                        {
                            continue;
                        }

                        var load = CarPrototypeLibrary.instance?.LoadForId(info.Value.LoadId);
                        var description = load == null
                            ? info.Value.LoadId
                            : info.Value.LoadString(load);
                        AppendLine(
                            text,
                            FuseForYourConveniencePolicy.FormatLoad(
                                info.Value.Quantity,
                                car.Definition.LoadSlots[slotIndex].MaximumCapacity,
                                description));
                    }
                }

                tagCallout.callout.Text = text.ToString();
                tagCallout.callout.Layout();
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE could not append optional information to car tag '" +
                    car.DisplayName + "': " + ex.GetBaseException().Message);
            }
        }

        private static void AppendLine(StringBuilder builder, string value)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(value);
        }
    }

    [HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.Rebuild))]
    internal static class FuseForYourConvenienceStationMapPatch
    {
        private const float MaximumStationDistance = 250f;
        private static readonly string[] IdentityRoleSuffixes =
        {
            "stationagent",
            "station",
            "depot",
            "agent",
            "area"
        };
        private static readonly FieldInfo AreaIdField = AccessTools.Field(typeof(StationAgent), "areaId");
        private static readonly FieldInfo PassengerStopIdField = AccessTools.Field(typeof(StationAgent), "passengerStopId");

        private static void Postfix()
        {
            if (!FuseForYourConveniencePolicy.IsActive())
            {
                return;
            }

            try
            {
                var agents = UnityEngine.Object.FindObjectsOfType<StationAgent>(true)
                    .Where(agent => agent != null && agent.isActiveAndEnabled)
                    .Select(agent => new StationSnapshot(
                        agent,
                        NormalizeIdentity(AreaIdField?.GetValue(agent) as string),
                        NormalizeIdentity(PassengerStopIdField?.GetValue(agent) as string)))
                    .ToArray();
                if (agents == null || agents.Length == 0)
                {
                    return;
                }

                foreach (var icon in UnityEngine.Object.FindObjectsOfType<MapIcon>(true))
                {
                    if (icon == null || icon.OnClick != null || icon.GetComponentInParent<Car>() != null)
                    {
                        continue;
                    }

                    var nearest = FindNearest(
                        icon.transform.position,
                        NormalizeIdentity(BuildIconIdentity(icon)),
                        agents);
                    if (nearest != null)
                    {
                        icon.OnClick = () => nearest.Activate(default(PickableActivateEvent));
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE could not attach optional station-map actions: " +
                    ex.GetBaseException().Message);
            }
        }

        internal static bool IsStationIdentityMatch(
            string iconIdentity,
            string areaId,
            string passengerStopId)
        {
            var normalizedIcon = NormalizeIdentity(iconIdentity);
            if (normalizedIcon.Length < 3)
            {
                return false;
            }

            return IdentityMatches(normalizedIcon, NormalizeIdentity(areaId)) ||
                   IdentityMatches(normalizedIcon, NormalizeIdentity(passengerStopId));
        }

        private static bool IdentityMatches(string normalizedIcon, string normalizedStation)
        {
            return normalizedStation.Length >= 3 &&
                   string.Equals(normalizedIcon, normalizedStation, StringComparison.Ordinal);
        }

        private static string NormalizeIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                if (char.IsLetterOrDigit(value[index]))
                {
                    normalized.Append(char.ToLowerInvariant(value[index]));
                }
            }

            var result = normalized.ToString();
            for (var index = 0; index < IdentityRoleSuffixes.Length; index++)
            {
                var suffix = IdentityRoleSuffixes[index];
                if (result.Length > suffix.Length + 2 &&
                    result.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return result.Substring(0, result.Length - suffix.Length);
                }
            }

            return result;
        }

        private static string BuildIconIdentity(MapIcon icon)
        {
            var identity = new StringBuilder();
            var label = icon.GetComponentInChildren<TMP_Text>(true);
            if (label != null && !string.IsNullOrWhiteSpace(label.text))
            {
                return label.text;
            }

            for (var current = icon.transform; current != null; current = current.parent)
            {
                identity.Append(' ').Append(current.name);
            }

            return identity.ToString();
        }

        private static StationAgent FindNearest(
            Vector3 position,
            string iconIdentity,
            StationSnapshot[] agents)
        {
            StationAgent nearest = null;
            var best = MaximumStationDistance * MaximumStationDistance;
            for (var index = 0; index < agents.Length; index++)
            {
                var snapshot = agents[index];
                if (!IdentityMatches(iconIdentity, snapshot.AreaId) &&
                    !IdentityMatches(iconIdentity, snapshot.PassengerStopId))
                {
                    continue;
                }

                var candidate = snapshot.Agent;
                var offset = candidate.transform.position - position;
                offset.y = 0f;
                var distance = offset.sqrMagnitude;
                if (distance < best)
                {
                    nearest = candidate;
                    best = distance;
                }
            }

            return nearest;
        }

        private sealed class StationSnapshot
        {
            internal StationSnapshot(StationAgent agent, string areaId, string passengerStopId)
            {
                Agent = agent;
                AreaId = areaId;
                PassengerStopId = passengerStopId;
            }

            internal StationAgent Agent { get; }
            internal string AreaId { get; }
            internal string PassengerStopId { get; }
        }
    }
}
