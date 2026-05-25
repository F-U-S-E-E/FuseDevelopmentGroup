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

        private static void ConfigureExternalController(
            GameObject root,
            Turntable turntable,
            Transform bridgeRoot,
            FuseTurntable definition)
        {
            var controllerTypeName = definition?.Visuals?.ControllerType;
            if (string.IsNullOrWhiteSpace(controllerTypeName))
            {
                RemoveCustomControllers(root, null);
                return;
            }

            var controllerType = ResolveControllerType(controllerTypeName);
            if (controllerType == null)
            {
                FuseLog.Warning($"FUSE turntable '{GetDefinitionTurntableId(turntable)}' could not resolve controller type '{controllerTypeName}'.");
                RemoveCustomControllers(root, null);
                return;
            }

            if (!typeof(MonoBehaviour).IsAssignableFrom(controllerType))
            {
                FuseLog.Warning($"FUSE turntable controller type '{controllerTypeName}' is not a Unity MonoBehaviour.");
                RemoveCustomControllers(root, null);
                return;
            }

            RemoveCustomControllers(root, controllerType);
            var behaviour = root.GetComponent(controllerType) as MonoBehaviour;
            if (behaviour == null)
            {
                behaviour = root.AddComponent(controllerType) as MonoBehaviour;
            }

            if (behaviour is IFuseTurntableController typedController)
            {
                typedController.Configure(turntable, bridgeRoot, definition);
                return;
            }

            var configure = controllerType.GetMethod(
                "Configure",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Turntable), typeof(Transform), typeof(FuseTurntable) },
                null);
            if (configure != null)
            {
                configure.Invoke(behaviour, new object[] { turntable, bridgeRoot, definition });
                return;
            }

            FuseLog.Warning(
                $"FUSE turntable controller '{controllerType.FullName}' does not implement IFuseTurntableController " +
                "and does not expose Configure(Turntable, Transform, FuseTurntable).");
        }

        private static Type ResolveControllerType(string typeName)
        {
            var direct = Type.GetType(typeName, false, true);
            if (direct != null)
            {
                return direct;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try
                {
                    type = assembly.GetType(typeName, false, true);
                }
                catch
                {
                    continue;
                }

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
