using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Infrastructure;
using Model.Ops;
using UnityEngine;

namespace FUSE.Runtime.API
{
    // Runtime observation: by the time FUSE's OnMapDidLoad handler runs the legacy
    // package apply pass, some base-game IndustryComponents (e.g. Whittier Saw Mill's
    // logs unloader bound to span 'Pt0j') have already been destroyed by an earlier
    // phase. At the OpsController.Awake postfix moment those components are still alive
    // in the hierarchy. Without a record of that pre-apply state we lose the existing
    // spans those components carried, and partial legacy patches whose intent is to
    // append a couple of spans onto the original component end up materializing as
    // fresh standalone components with only the added spans.
    //
    // This snapshot captures each industry's components at OpsController.Awake postfix
    // so MaterializeMissingPartialComponent can recover the original trackSpans, type,
    // and load binding when the runtime component is no longer findable in the live
    // hierarchy and a partial patch is targeting that subId.
    internal static class FuseBaseGameIndustrySnapshot
    {
        internal sealed class ComponentSnapshot
        {
            public string IndustryId { get; set; }
            public string SubIdentifier { get; set; }
            public string ComponentTypeFullName { get; set; }
            public string LoadId { get; set; }
            public string Name { get; set; }
            public string[] TrackSpanIds { get; set; } = Array.Empty<string>();
        }

        private static readonly object SyncLock = new object();
        private static readonly Dictionary<string, Dictionary<string, ComponentSnapshot>> _byIndustry =
            new Dictionary<string, Dictionary<string, ComponentSnapshot>>(StringComparer.OrdinalIgnoreCase);

        public static void CaptureAll(string reason)
        {
            try
            {
                var industries = UnityEngine.Object.FindObjectsOfType<Industry>(true);
                var captured = 0;
                lock (SyncLock)
                {
                    _byIndustry.Clear();
                    foreach (var industry in industries)
                    {
                        if (industry == null || string.IsNullOrWhiteSpace(industry.identifier))
                        {
                            continue;
                        }

                        var components = new Dictionary<string, ComponentSnapshot>(StringComparer.OrdinalIgnoreCase);
                        foreach (var component in industry.GetComponentsInChildren<IndustryComponent>(true))
                        {
                            if (component == null || string.IsNullOrWhiteSpace(component.subIdentifier))
                            {
                                continue;
                            }

                            string runtimeLoadId = null;
                            try
                            {
                                if (component is IndustryLoader loader)
                                {
                                    runtimeLoadId = loader.load?.id;
                                }
                                else if (component is IndustryUnloader unloader)
                                {
                                    runtimeLoadId = unloader.load?.id;
                                }
                                else if (component is IndustryLoaderBase loaderBase)
                                {
                                    runtimeLoadId = loaderBase.load?.id;
                                }
                            }
                            catch
                            {
                                runtimeLoadId = null;
                            }

                            var spanIds = component.trackSpans?
                                .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id))
                                .Select(span => span.id)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToArray() ?? Array.Empty<string>();

                            components[component.subIdentifier] = new ComponentSnapshot
                            {
                                IndustryId = industry.identifier,
                                SubIdentifier = component.subIdentifier,
                                ComponentTypeFullName = component.GetType().FullName,
                                LoadId = runtimeLoadId,
                                Name = component.name,
                                TrackSpanIds = spanIds
                            };
                            captured++;
                        }

                        if (components.Count > 0)
                        {
                            _byIndustry[industry.identifier] = components;
                        }
                    }
                }

                FuseLog.Info(
                    $"FUSE base-game industry snapshot reason='{reason}' captured " +
                    $"industries={_byIndustry.Count} components={captured}.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE base-game industry snapshot failed reason='{reason}'", ex);
            }
        }

        public static ComponentSnapshot Find(string industryId, string subIdentifier)
        {
            if (string.IsNullOrWhiteSpace(industryId) || string.IsNullOrWhiteSpace(subIdentifier))
            {
                return null;
            }

            lock (SyncLock)
            {
                if (_byIndustry.TryGetValue(industryId, out var components) &&
                    components.TryGetValue(subIdentifier, out var snapshot))
                {
                    return snapshot;
                }
            }

            return null;
        }

        public static IEnumerable<ComponentSnapshot> FindByLoadId(string industryId, string loadId)
        {
            if (string.IsNullOrWhiteSpace(industryId) || string.IsNullOrWhiteSpace(loadId))
            {
                yield break;
            }

            Dictionary<string, ComponentSnapshot> components;
            lock (SyncLock)
            {
                if (!_byIndustry.TryGetValue(industryId, out components))
                {
                    yield break;
                }

                components = components.Values
                    .Where(snapshot => string.Equals(snapshot.LoadId, loadId, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(snapshot => snapshot.SubIdentifier, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var match in components.Values)
            {
                yield return match;
            }
        }

        public static void Clear()
        {
            lock (SyncLock)
            {
                _byIndustry.Clear();
            }
        }
    }
}
