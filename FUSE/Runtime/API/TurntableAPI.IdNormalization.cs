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

        private static string GetDefinitionTurntableId(Turntable turntable)
        {
            return NormalizeDefinitionTurntableId(turntable != null ? turntable.id : null);
        }

        private static string GetDefinitionTurntableId(GameObject root)
        {
            if (root == null)
            {
                return string.Empty;
            }

            var turntable = root.GetComponent<Turntable>();
            if (turntable != null)
            {
                return GetDefinitionTurntableId(turntable);
            }

            var name = root.name ?? string.Empty;
            return name.StartsWith("Turntable-", StringComparison.OrdinalIgnoreCase)
                ? name.Substring("Turntable-".Length)
                : NormalizeDefinitionTurntableId(name);
        }

        private static string NormalizeDefinitionTurntableId(string runtimeTurntableId)
        {
            if (string.IsNullOrWhiteSpace(runtimeTurntableId))
            {
                return string.Empty;
            }

            return runtimeTurntableId.EndsWith(".turntable", StringComparison.OrdinalIgnoreCase)
                ? runtimeTurntableId.Substring(0, runtimeTurntableId.Length - ".turntable".Length)
                : runtimeTurntableId;
        }

        private static string ToRuntimeTurntableId(string definitionTurntableId)
        {
            if (string.IsNullOrWhiteSpace(definitionTurntableId))
            {
                return string.Empty;
            }

            return definitionTurntableId.EndsWith(".turntable", StringComparison.OrdinalIgnoreCase)
                ? definitionTurntableId
                : definitionTurntableId + ".turntable";
        }
    }
}
