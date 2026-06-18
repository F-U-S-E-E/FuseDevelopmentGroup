using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Core;
using Helpers;
using KeyValue.Runtime;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using RollingStock.Controls;
using Track;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static partial class TurntableAPI
    {

        private static void ConfigureRoundhouse(GameObject root, FuseTurntable definition)
        {
            var existing = root.transform.Find("Roundhouse");
            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
            }

            var roundhouse = definition.Roundhouse;
            if (roundhouse == null || roundhouse.Stalls <= 0)
            {
                return;
            }

            var roundhouseRoot = new GameObject("Roundhouse");
            roundhouseRoot.transform.SetParent(root.transform, false);
            roundhouseRoot.transform.localPosition = new Vector3(0f, -0.48f, 0f);
            roundhouseRoot.transform.localEulerAngles = Vector3.zero;
            roundhouseRoot.transform.localScale = Vector3.one;

            if (roundhouseRoot.GetComponent<KeyValueObject>() == null)
            {
                roundhouseRoot.AddComponent<KeyValueObject>();
            }

            var global = roundhouseRoot.GetComponent<GlobalKeyValueObject>() ?? roundhouseRoot.AddComponent<GlobalKeyValueObject>();
            global.globalObjectId = GetDefinitionTurntableId(root) + ".roundhouse";

            var angleStep = 360f / Mathf.Max(definition.Subdivisions, 1);
            var startPrefab = FusePrefabResolver.Resolve(roundhouse.StartPrefab ?? "vanilla://roundhouseStart");
            var endPrefab = FusePrefabResolver.Resolve(roundhouse.EndPrefab ?? "vanilla://roundhouseEnd");
            var stallPrefab = FusePrefabResolver.Resolve(roundhouse.StallPrefab ?? "vanilla://roundhouseStall");

            if (roundhouse.Stalls < definition.Subdivisions)
            {
                var start = UnityEngine.Object.Instantiate(startPrefab, roundhouseRoot.transform);
                ApplyRoundhousePartTransform(start, angleStep * Vector3.up);
                PatchRoundhouseDoors(start, "stall-doors.0");

                var end = UnityEngine.Object.Instantiate(endPrefab, roundhouseRoot.transform);
                ApplyRoundhousePartTransform(end, angleStep * roundhouse.Stalls * Vector3.up);
                PatchRoundhouseDoors(end, "stall-doors." + (roundhouse.Stalls - 1));
            }

            var startIndex = roundhouse.Stalls < definition.Subdivisions ? 1 : 0;
            var endIndex = roundhouse.Stalls < definition.Subdivisions ? roundhouse.Stalls - 1 : roundhouse.Stalls;
            for (var index = startIndex; index < endIndex; index++)
            {
                var stall = UnityEngine.Object.Instantiate(stallPrefab, roundhouseRoot.transform);
                ApplyRoundhousePartTransform(stall, (index + 1) * angleStep * Vector3.up);
                PatchRoundhouseDoors(stall, "stall-doors." + index);
            }

            EnableRenderers(roundhouseRoot);
            roundhouseRoot.SetActive(true);
        }

        private static void ApplyRoundhousePartTransform(GameObject part, Vector3 localEulerAngles)
        {
            if (part == null)
            {
                return;
            }

            part.transform.localPosition = Vector3.zero;
            part.transform.localEulerAngles = localEulerAngles;
            part.transform.localScale = Vector3.one;
            part.SetActive(true);
        }

        private static void PatchRoundhouseDoors(GameObject instance, string key)
        {
            var toggle = instance.GetComponentInChildren<KeyValuePickableToggle>(true);
            var animator = instance.GetComponentInChildren<KeyValueBoolAnimator>(true);
            if (toggle != null)
            {
                toggle.key = key;
            }

            if (animator != null)
            {
                animator.key = key;
            }
        }
    }
}
