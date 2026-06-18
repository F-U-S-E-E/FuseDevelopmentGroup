using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FUSE.Runtime.API;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Runtime.Lifecycle;
using FUSE.Loading;
using FUSE.Authoring.Migrations;
using FUSE.Runtime.Registry;
using Model;
using Model.Ops;
using Newtonsoft.Json.Linq;
using Railloader;
using TMPro;
using Track;
using UI;
using UI.Builder;
using UI.Common;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FUSE.Interface
{
    internal sealed partial class FuseHealthUi : MonoBehaviour
    {

        private void BuildInspectorContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 170f;
            builder.Spacing = 6f;

            builder.AddSection("Object Inspector");
            AddWrappedField(
                builder,
                "Scope",
                "Read-only inspector for FUSE-indexed runtime objects and loaded Unity scene objects. Search by id, name, scene path, or component type.",
                52f);
            builder.AddField(
                "Search",
                builder.AddInputField(_inspectorSearchTerm ?? string.Empty, value =>
                {
                    _inspectorSearchTerm = value ?? string.Empty;
                })).Height(32f);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Search", RebuildWindow);
                row.AddButtonCompact("Clear", () =>
                {
                    _inspectorSearchTerm = string.Empty;
                    _inspectorSelectedSignature = string.Empty;
                    RebuildWindow();
                });
                row.AddButtonCompact("Copy Detail", () =>
                {
                    var target = ResolveSelectedInspectorTarget();
                    GUIUtility.systemCopyBuffer = BuildInspectorReport(target);
                    _lastAction = target == null
                        ? "No inspector target selected."
                        : "Copied inspector detail to clipboard.";
                    RebuildWindow();
                });
            }, 6f).Height(32f);

            var term = (_inspectorSearchTerm ?? string.Empty).Trim();
            if (term.Length < 2)
            {
                AddWrappedField(builder, "Hint", "Enter at least 2 characters, then Search.", 34f);
                return;
            }

            var targets = BuildInspectorTargets(term, 120);
            if (targets.Count == 0)
            {
                AddWrappedField(builder, "Results", "No matching runtime or scene objects.", 34f);
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
                        RebuildWindow();
                    }
                })).Height(32f);
            AddValueField(builder, "Matches", targets.Count.ToString());
            builder.Spacer(4f);

            BuildInspectorDetail(builder, targets[selectedIndex]);
            builder.Spacer(8f);
        }

        private InspectorTarget ResolveSelectedInspectorTarget()
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
                AddValueField(builder, "Target", "None");
                return;
            }

            builder.AddSection("Selected Object");
            AddValueField(builder, "Kind", target.Kind);
            AddWrappedField(builder, "Id", target.Id, 36f);
            AddWrappedField(builder, "Scene Path", BlankAs(target.ScenePath, "not bound to a scene object"), 58f);
            AddValueField(builder, "Runtime Type", target.RuntimeObject == null ? "<null>" : target.RuntimeObject.GetType().FullName);
            AddValueField(builder, "Registry Claim", DescribeRegistryClaim(target));

            var gameObject = target.GameObject;
            if (gameObject == null)
            {
                AddWrappedField(builder, "Unity Object", "No GameObject is bound to this runtime entry.", 36f);
                return;
            }

            AddValueField(builder, "Active", $"self={gameObject.activeSelf} hierarchy={gameObject.activeInHierarchy}");
            AddValueField(builder, "Layer/Tag", $"{LayerMask.LayerToName(gameObject.layer)} ({gameObject.layer}) | {gameObject.tag}");
            AddValueField(builder, "Parent", gameObject.transform.parent == null ? "none" : GetGameObjectPath(gameObject.transform.parent.gameObject));
            AddValueField(builder, "Children", gameObject.transform.childCount.ToString());
            AddValueField(builder, "Position", FormatVector3(gameObject.transform.position));
            AddValueField(builder, "Rotation", FormatVector3(gameObject.transform.rotation.eulerAngles));
            AddValueField(builder, "Scale", FormatVector3(gameObject.transform.lossyScale));
            AddWrappedField(builder, "Components", FormatComponentList(gameObject), 54f);
            AddWrappedField(builder, "Children Preview", FormatChildPreview(gameObject), 54f);
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

        private static string FormatRuntimeObject(object runtime)
        {
            if (runtime == null)
            {
                return "<null>";
            }

            if (runtime is GameObject gameObject)
            {
                return "GameObject " + GetGameObjectPath(gameObject);
            }

            if (runtime is Component component)
            {
                return component.GetType().Name + " " + GetGameObjectPath(component.gameObject);
            }

            return runtime.GetType().Name;
        }

        private static string FormatComponentList(GameObject gameObject)
        {
            try
            {
                if (gameObject == null)
                {
                    return "none";
                }

                var names = gameObject.GetComponents<Component>()
                    .Where(component => component != null)
                    .Select(component => component.GetType().Name)
                    .Take(6)
                    .ToArray();
                return names.Length == 0 ? "none" : string.Join(",", names);
            }
            catch
            {
                return "unavailable";
            }
        }

        private static string FormatChildPreview(GameObject gameObject)
        {
            try
            {
                if (gameObject == null || gameObject.transform.childCount == 0)
                {
                    return "none";
                }

                var names = new List<string>();
                for (var index = 0; index < gameObject.transform.childCount && names.Count < 8; index++)
                {
                    var child = gameObject.transform.GetChild(index);
                    if (child != null)
                    {
                        names.Add(child.name);
                    }
                }

                var suffix = gameObject.transform.childCount > names.Count
                    ? " +" + (gameObject.transform.childCount - names.Count)
                    : string.Empty;
                return names.Count == 0 ? "none" : string.Join(", ", names.ToArray()) + suffix;
            }
            catch
            {
                return "unavailable";
            }
        }

        private static string FormatVector3(Vector3 value)
        {
            return value.x.ToString("0.###") + ", " + value.y.ToString("0.###") + ", " + value.z.ToString("0.###");
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
    }
}
