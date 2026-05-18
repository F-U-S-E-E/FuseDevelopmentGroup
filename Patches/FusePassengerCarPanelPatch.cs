using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FUSE.Infrastructure;
using Game;
using Game.Messages;
using Game.State;
using HarmonyLib;
using Model;
using Model.Ops;
using Model.Ops.Timetable;
using UI.Builder;
using UI.CarInspector;
using UI.Common;

namespace FUSE.Patches
{
    [HarmonyPatch(typeof(CarInspector), "PopulatePassengerCarPanel")]
    internal static class FusePassengerCarPanelPatch
    {
        private static readonly FieldInfo CarField = AccessTools.Field(typeof(CarInspector), "_car");
        private static readonly HashSet<string> LoggedMissingStops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] DefaultPassengerStopOrder =
        {
            "sylva",
            "dillsboro",
            "wilmot",
            "whittier",
            "ela",
            "bryson",
            "hemingway",
            "alarkajct",
            "almond",
            "nantahala",
            "topton",
            "rhodo",
            "andrews",
            "cochran",
            "alarka"
        };

        private static bool Prefix(CarInspector __instance, UIPanelBuilder builder)
        {
            var car = CarField?.GetValue(__instance) as Car;
            if (car == null)
            {
                return true;
            }

            try
            {
                PopulateSafePassengerCarPanel(builder, car);
                return false;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE passenger panel safety patch failed; falling back to base panel " +
                    $"car='{car.id ?? string.Empty}' message='{ex.Message}'.");
                return true;
            }
        }

        private static void PopulateSafePassengerCarPanel(UIPanelBuilder builder, Car car)
        {
            builder.AddField("Passengers", () => car.PassengerCountString(car.GetPassengerMarker()), UIPanelBuilder.Frequency.Fast);

            var stopsById = PassengerStop.FindAll()
                .Where(stop => stop != null && !string.IsNullOrWhiteSpace(stop.identifier))
                .GroupBy(stop => stop.identifier, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var orderedStops = new List<PassengerStop>();
            foreach (var stopId in DefaultPassengerStopOrder)
            {
                if (!stopsById.TryGetValue(stopId, out var stop) || stop == null)
                {
                    if (LoggedMissingStops.Add(stopId))
                    {
                        FuseLog.Info(
                            $"FUSE passenger panel skipped missing passenger stop id='{stopId}' " +
                            $"so available stops can still render.");
                    }

                    continue;
                }

                if (!stop.ProgressionDisabled)
                {
                    orderedStops.Add(stop);
                }
            }

            builder.VScrollView(viewBuilder =>
            {
                foreach (var stop in orderedStops)
                {
                    viewBuilder.HStack(row =>
                    {
                        row.AddToggle(
                            () => IsPassengerStopChecked(car, stop),
                            isOn => SetPassengerStopChecked(car, stop, isOn));
                        row.AddLocationField(
                            () => PassengerStopFieldName(car, stop),
                            stop,
                            () => JumpTo(stop));
                    });
                }
            });

            builder.Spacer(6f);
            builder.ButtonStrip(strip =>
            {
                strip.AddButtonCompact("<sprite name=Copy><sprite name=Coupled>", () => CopyStopsToCoupledCoaches(car))
                    .Tooltip("Copy to Coupled", "Copy passenger stops to coupled coaches.");

                if (car.TryGetTimetableTrain(out var train))
                {
                    strip.AddButtonCompact("Copy from Timetable", () => car.CopyStopsFromTimetable())
                        .Tooltip("Set from Timetable", "Copy the passenger stops from the timetable for " + train.Name + ".");
                    strip.AddToggle(
                        () => car.GetPassengerMarker()?.AutoDestinationsFromTimetable ?? false,
                        enabled =>
                        {
                            StateManager.ApplyLocal(new SetPassengerAutoDestinations(car.id, enabled));
                            if (enabled)
                            {
                                car.CopyStopsFromTimetable();
                            }
                        });
                    strip.AddLabel("Auto Dest")
                        .Tooltip(
                            "Timetable Auto Destinations",
                            "Automatically sets destinations from this train's timetable schedule, including for passenger transfers, while car is stopped at a station.");
                }
            });
        }

        private static string PassengerStopFieldName(Car car, PassengerStop stop)
        {
            var marker = car.GetPassengerMarker();
            if (!marker.HasValue)
            {
                return stop.name;
            }

            var count = marker.Value.CountPassengersForStop(stop.identifier);
            return count != 0 ? $"{stop.name} ({count})" : stop.name;
        }

        private static bool IsPassengerStopChecked(Car car, PassengerStop passengerStop)
        {
            var marker = car.GetPassengerMarker();
            return marker.HasValue && marker.Value.Destinations.Contains(passengerStop.identifier);
        }

        private static void SetPassengerStopChecked(Car car, PassengerStop passengerStop, bool isOn)
        {
            var marker = car.GetPassengerMarker() ?? PassengerMarker.Empty();
            var destinations = marker.Destinations;
            var identifier = passengerStop.identifier;
            var alreadySelected = destinations.Contains(identifier);

            if (isOn && !alreadySelected)
            {
                destinations.Add(identifier);
            }
            else if (!isOn && alreadySelected)
            {
                destinations.Remove(identifier);
            }

            StateManager.ApplyLocal(new SetPassengerDestinations(car.id, destinations.ToList()));
        }

        private static void CopyStopsToCoupledCoaches(Car car)
        {
            var marker = car.GetPassengerMarker() ?? PassengerMarker.Empty();
            var destinations = marker.Destinations.ToList();
            var copied = 0;

            foreach (var coupled in car.EnumerateCoupled())
            {
                if (!coupled.IsPassengerCar())
                {
                    continue;
                }

                StateManager.ApplyLocal(new SetPassengerDestinations(coupled.id, destinations));
                StateManager.ApplyLocal(new SetPassengerAutoDestinations(coupled.id, marker.AutoDestinationsFromTimetable));
                copied++;
            }

            var otherCount = Math.Max(0, copied - 1);
            Toast.Present($"Copied to {otherCount} other{(otherCount == 1 ? string.Empty : "s")}");
        }

        private static void JumpTo(IIndustryTrackDisplayable passengerStop)
        {
            if (passengerStop.TrackSpans == null || !passengerStop.TrackSpans.Any())
            {
                FuseLog.Warning($"FUSE passenger panel could not jump to stop '{passengerStop.DisplayName}' because it has no bound spans.");
                return;
            }

            CameraSelector.shared.ZoomToPoint(passengerStop.CenterPoint);
        }
    }
}
