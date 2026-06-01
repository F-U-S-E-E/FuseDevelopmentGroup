using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FUSE.Loading
{
    internal static partial class FuseAssetPackRegistry
    {
        /// <summary>
        /// Returns <paramref name="sourceText"/> (an asset pack's Definitions.json) with
        /// only the components that fail <paramref name="canBind"/> removed from each
        /// object's <c>definition.components</c> array, recording every removed
        /// <c>kind</c> in <paramref name="droppedByKind"/>. When nothing is removed the
        /// original text is returned unchanged (preserving formatting).
        ///
        /// This is the resilient retry path's filter. FUSE no longer pre-strips by a
        /// hardcoded allow-list of base-game component kinds — that silently deleted the
        /// runtime-registered customization kinds (MaterialColorizerComponent,
        /// DefaultLivelryComponent, ComponentGroup, ...) library mods add, emptying the
        /// in-game Customize menu. Instead the loader deserializes verbatim and only falls
        /// back here when a kind genuinely cannot bind (its defining mod is absent),
        /// dropping that one component rather than the whole pack.
        ///
        /// The bind check is injected so this method is hermetically unit-testable without
        /// the game's component types.
        /// </summary>
        internal static string FilterUnbindableComponents(
            string sourceText, IDictionary<string, int> droppedByKind, Func<JObject, bool> canBind)
        {
            var root = JObject.Parse(sourceText);
            var objects = root["objects"] as JArray;
            if (objects == null)
            {
                return sourceText;
            }

            var removedAny = false;
            foreach (var objectToken in objects.OfType<JObject>())
            {
                var components = objectToken["definition"]?["components"] as JArray;
                if (components == null)
                {
                    continue;
                }

                for (var index = components.Count - 1; index >= 0; index--)
                {
                    var component = components[index] as JObject;
                    if (component != null && canBind(component))
                    {
                        continue;
                    }

                    var kind = GetStringProperty(component, "kind");
                    var key = string.IsNullOrWhiteSpace(kind) ? "<missing>" : kind;
                    droppedByKind[key] = droppedByKind.TryGetValue(key, out var count) ? count + 1 : 1;
                    components.RemoveAt(index);
                    removedAny = true;
                }
            }

            return removedAny
                ? root.ToString(Formatting.Indented)
                : sourceText;
        }

        private static string GetStringProperty(JObject obj, string propertyName)
        {
            if (obj == null)
            {
                return null;
            }

            return obj.TryGetValue(propertyName, StringComparison.OrdinalIgnoreCase, out var token)
                ? (string)token
                : null;
        }
    }
}
