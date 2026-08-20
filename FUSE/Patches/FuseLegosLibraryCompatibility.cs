using System;
using System.Reflection;
using FUSE.Infrastructure;
using HarmonyLib;
using Model.Definition;
using Newtonsoft.Json;

namespace FUSE.Patches
{
    /// <summary>
    /// Gives LegosLibraryOfStuff's definition clone operation the same Unity
    /// value converters used by the game. The library's default Newtonsoft
    /// clone walks calculated Vector2/Vector3 properties and can report a
    /// self-referencing loop, aborting the remainder of its catalog edits.
    /// </summary>
    internal static class FuseLegosLibraryCompatibility
    {
        private const string PatchTypeName =
            "LegosLibraryOfStuff.ContainerSerializationDeserializePatch";

        private static readonly MethodInfo SerializerSettingsMethod =
            AccessTools.Method(typeof(ContainerSerialization), "JsonSerializerSettings");

        private static bool _installed;
        private static int _cloneFailures;

        internal static string EnsureInstalled(Harmony harmony)
        {
            if (_installed)
            {
                return "installed";
            }

            if (harmony == null)
            {
                return "unavailable (no harmony)";
            }

            var patchType = AccessTools.TypeByName(PatchTypeName);
            if (patchType == null)
            {
                return "idle (not present)";
            }

            var cloneItem = AccessTools.DeclaredMethod(
                patchType,
                "CloneItem",
                new[] { typeof(ContainerItem) });
            if (cloneItem == null || SerializerSettingsMethod == null)
            {
                return "idle (surface changed)";
            }

            harmony.Patch(
                cloneItem,
                prefix: new HarmonyMethod(
                    typeof(FuseLegosLibraryCompatibility),
                    nameof(CloneItemPrefix)));
            _installed = true;
            return "installed";
        }

        private static bool CloneItemPrefix(ContainerItem item, ref ContainerItem __result)
        {
            if (item == null)
            {
                return true;
            }

            try
            {
                var settings = SerializerSettingsMethod.Invoke(null, null) as JsonSerializerSettings;
                if (settings == null)
                {
                    return true;
                }

                var json = JsonConvert.SerializeObject(item, Formatting.None, settings);
                var clone = JsonConvert.DeserializeObject<ContainerItem>(json, settings);
                if (clone == null)
                {
                    return true;
                }

                __result = clone;
                return false;
            }
            catch (Exception ex)
            {
                _cloneFailures++;
                if (FuseGuardLog.ShouldLog(_cloneFailures))
                {
                    FuseLog.Exception(
                        $"FUSE could not safely clone Lego definition '{item.Identifier}'; " +
                        "the library's original clone path will be attempted",
                        ex);
                }

                return true;
            }
        }
    }
}
