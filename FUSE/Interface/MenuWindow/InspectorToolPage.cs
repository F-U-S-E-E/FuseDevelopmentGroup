using FUSE.Cache;
using FUSE.Registry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UI.Builder;
using UI.Common;
using UnityEngine;
using static FUSE.Interface.InterfaceUtils;

namespace FUSE.Interface.MenuWindow
{
    internal struct InspectorToolPage
    {
        private static string _inspectorSearchTerm = string.Empty;
        private static string _inspectorSelectedSignature = string.Empty;

        private sealed class InspectorTarget
        {
            public InspectorTarget(
                string kind,
                string id,
                object runtimeObject,
                GameObject gameObject,
                string scenePath,
                FuseClaimKind? claimKind)
            {
                Kind = kind ?? "Object";
                Id = id ?? string.Empty;
                RuntimeObject = runtimeObject;
                GameObject = gameObject;
                ScenePath = scenePath ?? string.Empty;
                ClaimKind = claimKind;
                Signature = Kind + "|" + Id + "|" + ScenePath + "|" + (runtimeObject == null ? "<null>" : runtimeObject.GetHashCode().ToString());
                DropdownLabel = Kind + " | " + BlankAs(Id, "(blank)") + " | " + BlankAs(ScenePath, "no scene path");
            }

            public string Kind { get; }
            public string Id { get; }
            public object RuntimeObject { get; }
            public GameObject GameObject { get; }
            public string ScenePath { get; }
            public FuseClaimKind? ClaimKind { get; }
            public string Signature { get; }
            public string DropdownLabel { get; }
        }

        public static void Build(UIPanelBuilder builder)
        {
            builder.AddTitle("Object Inspector", "");

            builder.AddLabel("This tool is a read-only inspector for FUSE-indexed runtime objects and loaded Unity scene objects.");

            builder.Spacer(16f);

            builder.AddLabel("Search by id, name, scene path, or component type.");

            builder.Spacer(16f);

            builder.AddField(
                "Search",
                builder.AddInputField(_inspectorSearchTerm ?? string.Empty, value =>
                {
                    _inspectorSearchTerm = value ?? string.Empty;
                    builder.Rebuild();
                }));

            builder.AddField("Hint", "Enter at least 2 characters, then Search.");

            builder.Spacer(8f);

            builder.HStack(row =>
            {
                row.AddButtonCompact("Search", builder.Rebuild);
                row.AddButtonCompact("Clear", () =>
                {
                    _inspectorSearchTerm = string.Empty;
                    _inspectorSelectedSignature = string.Empty;
                    builder.Rebuild();
                });
                row.AddButtonCompact("Copy Detail", () =>
                {
                    var target = ResolveSelectedInspectorTarget();
                    GUIUtility.systemCopyBuffer = BuildInspectorReport(target);
                    Toast.Present(target == null
                        ? "No inspector target selected."
                        : "Copied inspector detail to clipboard.");
                    builder.Rebuild();
                });
            }, 6f).Height(32f);

            builder.Spacer(16f);

            var term = (_inspectorSearchTerm ?? string.Empty).Trim();
            //if (term.Length < 2)
            //{
            //    builder.AddField("Hint", "Enter at least 2 characters, then Search.");
            //    return;
            //}

            var targets = BuildInspectorTargets(term, 120);
            if (targets.Count == 0)
            {
                builder.AddField("Results", "No matching runtime or scene objects.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_inspectorSelectedSignature) ||
                targets.All(target => !string.Equals(target.Signature, _inspectorSelectedSignature, StringComparison.OrdinalIgnoreCase)))
            {
                _inspectorSelectedSignature = targets[0].Signature;
            }

            var selectedIndex = Math.Max(0, targets.FindIndex(target =>
                string.Equals(target.Signature, _inspectorSelectedSignature, StringComparison.OrdinalIgnoreCase)));
            var labels = targets
                .Select(target => target.DropdownLabel)
                .ToList();
            builder.AddField(
                "Target",
                builder.AddDropdown(labels, selectedIndex, index =>
                {
                    if (index >= 0 && index < targets.Count)
                    {
                        _inspectorSelectedSignature = targets[index].Signature;
                        builder.Rebuild();
                    }
                })).Height(32f);
            builder.AddField("Matches", targets.Count.ToString());
            builder.Spacer(4f);

            BuildInspectorDetail(builder, targets[selectedIndex]);
            builder.Spacer(8f);
        }

        private static InspectorTarget ResolveSelectedInspectorTarget()
        {
            var targets = BuildInspectorTargets(_inspectorSearchTerm, 120);
            if (targets.Count == 0)
            {
                return null;
            }

            return targets.FirstOrDefault(target =>
                       string.Equals(target.Signature, _inspectorSelectedSignature, StringComparison.OrdinalIgnoreCase)) ??
                   targets[0];
        }

        private static List<InspectorTarget> BuildInspectorTargets(string rawTerm, int limit)
        {
            var results = new List<InspectorTarget>();
            var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var term = (rawTerm ?? string.Empty).Trim();
            if (term.Length < 2)
            {
                return results;
            }

            limit = Math.Max(1, limit);
            AddInspectorIndexTargets(results, signatures, "Track Node", FuseNodeRuntimeIndex.Instance, FuseClaimKind.Node, term, limit);
            AddInspectorIndexTargets(results, signatures, "Track Segment", FuseSegmentRuntimeIndex.Instance, FuseClaimKind.Segment, term, limit);
            AddInspectorIndexTargets(results, signatures, "Track Span", FuseSpanRuntimeIndex.Instance, FuseClaimKind.Span, term, limit);
            AddInspectorIndexTargets(results, signatures, "Area", FuseAreaRuntimeIndex.Instance, null, term, limit);
            AddInspectorIndexTargets(results, signatures, "Load", FuseLoadRuntimeIndex.Instance, null, term, limit);
            AddInspectorIndexTargets(results, signatures, "Industry", FuseIndustryRuntimeIndex.Instance, FuseClaimKind.Industry, term, limit);
            AddInspectorIndexTargets(results, signatures, "Industry Component", FuseIndustryComponentRuntimeIndex.Instance, null, term, limit);
            AddInspectorIndexTargets(results, signatures, "Loader", FuseLoaderRuntimeIndex.Instance, FuseClaimKind.Loader, term, limit);
            AddInspectorIndexTargets(results, signatures, "Station", FuseStationRuntimeIndex.Instance, FuseClaimKind.Station, term, limit);
            AddInspectorIndexTargets(results, signatures, "Scenery", FuseSceneryRuntimeIndex.Instance, FuseClaimKind.Scenery, term, limit);
            AddInspectorIndexTargets(results, signatures, "Spliney", FuseSplineyRuntimeIndex.Instance, null, term, limit);
            AddInspectorIndexTargets(results, signatures, "Map Label", FuseMapLabelRuntimeIndex.Instance, null, term, limit);
            AddInspectorIndexTargets(results, signatures, "Progression", FuseProgressionRuntimeIndex.Instance, null, term, limit);
            AddInspectorIndexTargets(results, signatures, "Map Feature", FuseMapFeatureRuntimeIndex.Instance, null, term, limit);
            AddInspectorSceneTargets(results, signatures, term, limit);
            return results;
        }

        private static void AddInspectorIndexTargets<TCache>(
            List<InspectorTarget> results,
            HashSet<string> signatures,
            string kind,
            FuseRuntimeIndex<TCache> index,
            FuseClaimKind? claimKind,
            string term,
            int limit)
            where TCache : FuseRuntimeIndex<TCache>
        {
            if (results == null || results.Count >= limit || index == null)
            {
                return;
            }

            foreach (var id in index.Ids.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                if (results.Count >= limit)
                {
                    return;
                }

                var runtime = index[id];
                var gameObject = ResolveGameObject(runtime);
                var path = gameObject == null ? string.Empty : GetGameObjectPath(gameObject);
                var detail = FormatRuntimeObject(runtime);
                if (!MatchesSearch(id, term) &&
                    !MatchesSearch(path, term) &&
                    !MatchesSearch(detail, term) &&
                    !MatchesSearch(FormatComponentList(gameObject), term))
                {
                    continue;
                }

                AddInspectorTarget(
                    results,
                    signatures,
                    new InspectorTarget(kind, id, runtime, gameObject, path, claimKind));
            }
        }

        private static void AddInspectorSceneTargets(
            List<InspectorTarget> results,
            HashSet<string> signatures,
            string term,
            int limit)
        {
            if (results == null || results.Count >= limit)
            {
                return;
            }

            GameObject[] objects;
            try
            {
                objects = Resources.FindObjectsOfTypeAll<GameObject>();
            }
            catch
            {
                return;
            }

            foreach (var gameObject in objects
                         .Where(IsLoadedSceneObject)
                         .OrderBy(GetGameObjectPath, StringComparer.OrdinalIgnoreCase))
            {
                if (results.Count >= limit)
                {
                    return;
                }

                var path = GetGameObjectPath(gameObject);
                var components = FormatComponentList(gameObject);
                if (!MatchesSearch(gameObject.name, term) &&
                    !MatchesSearch(path, term) &&
                    !MatchesSearch(components, term))
                {
                    continue;
                }

                AddInspectorTarget(
                    results,
                    signatures,
                    new InspectorTarget("Scene Object", gameObject.name, gameObject, gameObject, path, null));
            }
        }

        private static void AddInspectorTarget(
            List<InspectorTarget> results,
            HashSet<string> signatures,
            InspectorTarget target)
        {
            if (results == null || signatures == null || target == null || !signatures.Add(target.Signature))
            {
                return;
            }

            results.Add(target);
        }

        private static GameObject ResolveGameObject(object runtime)
        {
            if (runtime is GameObject gameObject)
            {
                return gameObject;
            }

            if (runtime is Component component)
            {
                return component.gameObject;
            }

            return null;
        }

        private static void BuildInspectorDetail(UIPanelBuilder builder, InspectorTarget target)
        {
            if (target == null)
            {
                builder.AddField("Target", "None");
                return;
            }

            builder.AddSection("Selected Object");
            builder.AddField("Kind", target.Kind);
            builder.AddField("Id", target.Id);
            builder.AddField("Scene Path", BlankAs(target.ScenePath, "not bound to a scene object"));
            builder.AddField("Runtime Type", target.RuntimeObject == null ? "<null>" : target.RuntimeObject.GetType().FullName);
            builder.AddField("Registry Claim", DescribeRegistryClaim(target));

            var gameObject = target.GameObject;
            if (gameObject == null)
            {
                builder.AddField("Unity Object", "No GameObject is bound to this runtime entry.");
                return;
            }

            builder.AddField("Active", $"self={gameObject.activeSelf} hierarchy={gameObject.activeInHierarchy}");
            builder.AddField("Layer/Tag", $"{LayerMask.LayerToName(gameObject.layer)} ({gameObject.layer}) | {gameObject.tag}");
            builder.AddField("Parent", gameObject.transform.parent == null ? "none" : GetGameObjectPath(gameObject.transform.parent.gameObject));
            builder.AddField("Children", gameObject.transform.childCount.ToString());
            builder.AddField("Position", FormatVector3(gameObject.transform.position));
            builder.AddField("Rotation", FormatVector3(gameObject.transform.rotation.eulerAngles));
            builder.AddField("Scale", FormatVector3(gameObject.transform.lossyScale));
            builder.AddField("Components", FormatComponentList(gameObject));
            builder.AddField("Children Preview", FormatChildPreview(gameObject));
        }

        private static string BuildInspectorReport(InspectorTarget target)
        {
            if (target == null)
            {
                return "FUSE Inspector\nNo target selected.";
            }

            var builder = new StringBuilder();
            builder.AppendLine("FUSE Inspector");
            builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("Kind: " + target.Kind);
            builder.AppendLine("Id: " + target.Id);
            builder.AppendLine("Scene Path: " + BlankAs(target.ScenePath, "not bound to a scene object"));
            builder.AppendLine("Runtime Type: " + (target.RuntimeObject == null ? "<null>" : target.RuntimeObject.GetType().FullName));
            builder.AppendLine("Registry Claim: " + DescribeRegistryClaim(target));

            var gameObject = target.GameObject;
            if (gameObject == null)
            {
                builder.AppendLine("GameObject: none");
                return builder.ToString().TrimEnd();
            }

            builder.AppendLine("Active Self: " + gameObject.activeSelf);
            builder.AppendLine("Active In Hierarchy: " + gameObject.activeInHierarchy);
            builder.AppendLine("Layer: " + LayerMask.LayerToName(gameObject.layer) + " (" + gameObject.layer + ")");
            builder.AppendLine("Tag: " + gameObject.tag);
            builder.AppendLine("Parent: " + (gameObject.transform.parent == null ? "none" : GetGameObjectPath(gameObject.transform.parent.gameObject)));
            builder.AppendLine("Children: " + gameObject.transform.childCount);
            builder.AppendLine("Position: " + FormatVector3(gameObject.transform.position));
            builder.AppendLine("Rotation: " + FormatVector3(gameObject.transform.rotation.eulerAngles));
            builder.AppendLine("Scale: " + FormatVector3(gameObject.transform.lossyScale));
            builder.AppendLine("Components: " + FormatComponentList(gameObject));
            builder.AppendLine("Children Preview: " + FormatChildPreview(gameObject));
            return builder.ToString().TrimEnd();
        }

        private static bool IsLoadedSceneObject(GameObject gameObject)
        {
            return gameObject != null && gameObject.scene.IsValid() && gameObject.scene.isLoaded;
        }

        private static string DescribeRegistryClaim(InspectorTarget target)
        {
            if (target == null || !target.ClaimKind.HasValue || string.IsNullOrWhiteSpace(target.Id))
            {
                return "not claim-tracked";
            }

            var kind = target.ClaimKind.Value;
            if (kind == FuseClaimKind.Industry ||
                kind == FuseClaimKind.Scenery ||
                kind == FuseClaimKind.SuppressedArea ||
                kind == FuseClaimKind.SuppressedScenePath ||
                kind == FuseClaimKind.SuppressedTrackGroup)
            {
                var owners = FuseRegistry.GetSharedOwners(kind, target.Id).ToArray();
                return owners.Length == 0 ? "shared | unclaimed" : "shared | " + string.Join(", ", owners);
            }

            var owner = FuseRegistry.GetExclusiveOwner(kind, target.Id);
            return string.IsNullOrWhiteSpace(owner) ? "exclusive | unclaimed" : "exclusive | " + owner;
        }
    }
}
