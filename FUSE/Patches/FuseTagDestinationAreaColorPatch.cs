using System;
using FUSE.Infrastructure;
using HarmonyLib;
using Helpers;
using Model;
using Model.Ops;
using UI.Tags;
using UnityEngine;

namespace FUSE.Patches
{
    [HarmonyPatch(typeof(TagController), "UpdateTag")]
    internal static class FuseTagDestinationAreaColorPatch
    {
        private static void Postfix(Car car, TagCallout tagCallout, OpsController opsController)
        {
            try
            {
                if (car == null || tagCallout?.colorImages == null || opsController == null)
                {
                    return;
                }

                string destinationName;
                bool isAtDestination;
                Vector3 destinationPosition;
                OpsCarPosition destination;
                if (!opsController.TryGetDestinationInfo(
                        car,
                        out destinationName,
                        out isAtDestination,
                        out destinationPosition,
                        out destination))
                {
                    return;
                }

                var destinationArea = opsController.AreaForCarPosition(destination);
                if (!IsTransparent(destinationArea?.tagColor ?? default(Color)))
                {
                    return;
                }

                var fallbackArea = FindNearestVisibleArea(opsController, destinationPosition, destinationArea);
                if (fallbackArea == null)
                {
                    return;
                }

                var color = fallbackArea.tagColor;
                if (isAtDestination)
                {
                    color *= 0.5f;
                }

                foreach (var image in tagCallout.colorImages)
                {
                    if (image != null)
                    {
                        image.color = color;
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE failed to apply fallback destination area color to car tag.", ex);
            }
        }

        private static Area FindNearestVisibleArea(OpsController opsController, Vector3 destinationPosition, Area excludedArea)
        {
            Area nearestArea = null;
            var nearestDistance = float.MaxValue;

            foreach (var area in opsController.Areas)
            {
                if (area == null || area == excludedArea || IsTransparent(area.tagColor))
                {
                    continue;
                }

                var areaPosition = WorldTransformer.WorldToGame(area.transform.position);
                var distance = Vector3.Distance(destinationPosition, areaPosition) - area.radius;
                if (distance <= nearestDistance)
                {
                    nearestArea = area;
                    nearestDistance = distance;
                }
            }

            return nearestArea;
        }

        private static bool IsTransparent(Color color)
        {
            return color.a <= 0.001f;
        }
    }
}
