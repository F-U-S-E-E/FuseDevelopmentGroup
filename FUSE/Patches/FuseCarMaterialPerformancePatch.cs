using System;
using System.Collections.Generic;
using System.Reflection;
using AssetPack.Common;
using FUSE.Infrastructure;
using HarmonyLib;
using Model;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Removes allocation-heavy LINQ and repeated linear material searches from
    /// the stock car-model completion path without changing its material
    /// ownership or per-car customization semantics.
    /// </summary>
    [HarmonyPatch]
    internal static class FuseCarMaterialPerformancePatch
    {
        private static readonly AccessTools.FieldRef<Car, List<Material>> OwnedMaterialsRef =
            BindOwnedMaterials();

        private static MethodInfo TargetMethod()
        {
            return AccessTools.Method(
                typeof(Car),
                "MakeMaterialsUnique",
                new[]
                {
                    typeof(GameObject),
                    typeof(IReadOnlyCollection<Renderer>)
                });
        }

        private static bool Prefix(
            Car __instance,
            GameObject obj,
            IReadOnlyCollection<Renderer> renderers)
        {
            if (__instance == null ||
                obj == null ||
                renderers == null ||
                OwnedMaterialsRef == null)
            {
                return true;
            }

            MakeMaterialsUnique(__instance, obj, renderers, OwnedMaterialsRef(__instance));
            return false;
        }

        internal static void MakeMaterialsUnique(
            Car car,
            GameObject obj,
            IReadOnlyCollection<Renderer> renderers,
            List<Material> ownedMaterials)
        {
            var rendererCount = renderers.Count;
            var rendererSnapshot = new Renderer[rendererCount];
            var materialSnapshots = new Material[rendererCount][];
            var uniqueMaterials = new List<Material>();
            var seenMaterials = new HashSet<Material>();

            var rendererIndex = 0;
            foreach (var renderer in renderers)
            {
                rendererSnapshot[rendererIndex] = renderer;
                var materials = renderer.sharedMaterials;
                materialSnapshots[rendererIndex] = materials;

                for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material != null && seenMaterials.Add(material))
                    {
                        uniqueMaterials.Add(material);
                    }
                }

                rendererIndex++;
            }

            var replacements = new Dictionary<Material, Material>(uniqueMaterials.Count);
            for (var index = 0; index < uniqueMaterials.Count; index++)
            {
                var source = uniqueMaterials[index];
                var replacement = new Material(source);
                replacement.name = replacement.name + " (" + car.id + ")";
                replacements.Add(source, replacement);
                ownedMaterials.Add(replacement);
            }

            for (var index = 0; index < rendererIndex; index++)
            {
                var materials = materialSnapshots[index];
                for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material != null)
                    {
                        materials[materialIndex] = replacements[material];
                    }
                }

                rendererSnapshot[index].sharedMaterials = materials;
            }

            // Preserve the stock MaterialMap behavior exactly. In particular,
            // the destination remains the car's full owned-material list.
            var materialMap = obj.GetComponentInChildren<MaterialMap>();
            if (materialMap != null)
            {
                materialMap.ReplaceMaterials(uniqueMaterials, ownedMaterials);
            }
        }

        private static AccessTools.FieldRef<Car, List<Material>> BindOwnedMaterials()
        {
            try
            {
                return AccessTools.FieldRefAccess<Car, List<Material>>("_ownedMaterials");
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE car material optimization could not bind Car._ownedMaterials; " +
                    "the stock material path will remain active",
                    ex);
                return null;
            }
        }
    }

    /// <summary>
    /// Compacts the renderer array returned by Unity in place instead of
    /// wrapping it in Where().ToArray() for every body and truck model.
    /// </summary>
    [HarmonyPatch]
    internal static class FuseCarRendererCollectionPerformancePatch
    {
        private static MethodInfo TargetMethod()
        {
            return AccessTools.Method(
                typeof(Car),
                "GetRenderers",
                new[] { typeof(GameObject) });
        }

        private static bool Prefix(GameObject o, ref Renderer[] __result)
        {
            if (o == null)
            {
                return true;
            }

            var renderers = o.GetComponentsInChildren<Renderer>();
            var enabledCount = 0;
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer.enabled)
                {
                    renderers[enabledCount++] = renderer;
                }
            }

            if (enabledCount != renderers.Length)
            {
                Array.Resize(ref renderers, enabledCount);
            }

            __result = renderers;
            return false;
        }
    }
}
