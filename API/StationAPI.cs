using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Helpers;
using Model;
using Model.Ops;
using RAIL.Cache;
using RAIL.Data;
using RAIL.Infrastructure;
using TMPro;
using UI.Map;
using UnityEngine;
using UnityEngine.UI;

namespace RAIL.API
{
    public static class StationAPI
    {
        private static readonly FieldInfo AreaField = typeof(StationAgent).GetField("area", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo PassengerStopField = typeof(StationAgent).GetField("passengerStop", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SecondaryAreasField = typeof(StationAgent).GetField("secondaryAreas", BindingFlags.Instance | BindingFlags.NonPublic);
        private const float MapIconElevation = 100f;
        private static Transform _fallbackRoot;
        private static Sprite _stationIconSprite;

        public static StationAgent AddStationAgent(string id, RailStation definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetStationAgent(id) != null)
            {
                throw new InvalidOperationException($"Station agent '{id}' already exists.");
            }

            var gameObject = new GameObject(id);
            gameObject.transform.SetParent(GetStationRoot(), false);
            var stationAgent = ApplyDefinition(gameObject, id, definition);
            RailStationRuntimeIndex.Instance.Set(id, stationAgent);
            return stationAgent;
        }

        public static void UpdateStationAgent(string id, RailStation definition)
        {
            var agent = RequireStationAgent(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyDefinition(GetStationRootObject(agent, id), id, definition);
            RailStationRuntimeIndex.Instance.Set(id, RequireStationAgent(id));
        }

        public static void RemoveStationAgent(string id)
        {
            var agent = RequireStationAgent(id);
            var root = GetStationRootObject(agent, id);
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
            RailStationRuntimeIndex.Instance.Remove(id);
        }

        public static StationAgent GetStationAgent(string id)
        {
            if (RailStationRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (StationAgent)cached;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<StationAgent>(true).FirstOrDefault(agent => agent.name == id)
                : null;
        }

        public static IEnumerable<StationAgent> GetAllStationAgents()
        {
            return UnityEngine.Object.FindObjectsOfType<StationAgent>(true);
        }

        public static PassengerStop GetPassengerStop(string id)
        {
            return !string.IsNullOrWhiteSpace(id)
                ? PassengerStop.FindAll().FirstOrDefault(stop => stop.identifier == id)
                : null;
        }

        public static IEnumerable<PassengerStop> GetAllPassengerStops()
        {
            return PassengerStop.FindAll();
        }

        private static StationAgent ApplyDefinition(GameObject root, string id, RailStation definition)
        {
            root.transform.localPosition = definition.Position;
            root.transform.localRotation = Quaternion.Euler(definition.Rotation);

            var prefab = RailPrefabResolver.Resolve(definition.Prefab);
            if (prefab == null)
            {
                throw new InvalidOperationException($"Station prefab '{definition.Prefab}' was not found.");
            }

            var stop = GetPassengerStop(definition.PassengerStopId);
            if (stop == null)
            {
                throw new InvalidOperationException($"Passenger stop '{definition.PassengerStopId}' was not found.");
            }

            for (var index = root.transform.childCount - 1; index >= 0; index--)
            {
                UnityEngine.Object.Destroy(root.transform.GetChild(index).gameObject);
            }

            var instance = UnityEngine.Object.Instantiate(prefab, root.transform);
            instance.name = "prefab";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localEulerAngles = Vector3.zero;

            root.name = id;
            root.SetActive(false);

            var stationAgent = instance.GetComponentInChildren<StationAgent>(true);
            if (stationAgent == null)
            {
                throw new InvalidOperationException($"Station prefab '{definition.Prefab}' does not contain a StationAgent.");
            }

            stationAgent.name = id;
            var area = stop.GetComponentInParent<Area>(true);
            AreaField?.SetValue(stationAgent, area);
            PassengerStopField?.SetValue(stationAgent, stop);
            var secondaryAreas = SecondaryAreasField?.GetValue(stationAgent) as IList<Area>;
            secondaryAreas?.Clear();

            var stationLabel = area != null ? area.name : stop.TimetableName;
            if (!string.IsNullOrWhiteSpace(stationLabel))
            {
                foreach (var textMesh in instance.GetComponentsInChildren<TextMeshPro>(true))
                {
                    if (!textMesh.transform.parent.name.StartsWith("Sign-Station", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    textMesh.text = stationLabel;
                    var sign = textMesh.transform.Find("Sign-Station");
                    if (sign != null)
                    {
                        var localScale = sign.localScale;
                        localScale.y = 100f;
                        sign.localScale = localScale;
                    }
                }
            }

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
            }

            root.SetActive(true);
            instance.SetActive(true);
            ConfigureMapIcons(instance, stop, root.transform, id, definition.Prefab);
            return stationAgent;
        }

        private static void ConfigureMapIcons(GameObject instance, PassengerStop passengerStop, Transform stationRoot, string id, string prefab)
        {
            var icons = instance.GetComponentsInChildren<MapIcon>(true).ToList();
            if (icons.Count == 0)
            {
                var generated = CreateStationMapIcon(stationRoot, id);
                if (generated != null)
                {
                    icons.Add(generated);
                    RailLog.Info($"RAIL station '{id}' prefab '{prefab}' had no MapIcon; generated RAIL station map icon.");
                }
            }

            var mapLayer = LayerMask.NameToLayer("Map");
            foreach (var icon in icons)
            {
                if (icon == null)
                {
                    continue;
                }

                icon.gameObject.SetActive(true);
                if (mapLayer >= 0)
                {
                    SetLayerRecursive(icon.gameObject, mapLayer);
                }

                icon.transform.SetPositionAndRotation(
                    GetMapIconWorldPosition(passengerStop, stationRoot),
                    Quaternion.Euler(-90f, stationRoot.eulerAngles.y, 0f));
                icon.OnClick = () => CameraSelector.shared.JumpTo(passengerStop);
                MapBuilder.Shared?.Add(icon);
            }
        }

        private static Vector3 GetMapIconWorldPosition(PassengerStop passengerStop, Transform stationRoot)
        {
            try
            {
                return WorldTransformer.GameToWorld(passengerStop.CenterPoint) + Vector3.up * MapIconElevation;
            }
            catch (Exception ex)
            {
                RailLog.Warning($"RAIL could not place station map icon from passenger stop center; using station transform. {ex.Message}");
                return stationRoot.position + Vector3.up * MapIconElevation;
            }
        }

        private static GameObject GetStationRootObject(StationAgent agent, string id)
        {
            var stationRoot = GetStationRoot();
            var cursor = agent.transform;
            while (cursor != null && cursor.parent != stationRoot)
            {
                cursor = cursor.parent;
            }

            if (cursor != null)
            {
                return cursor.gameObject;
            }

            var namedRoot = stationRoot.Find(id);
            if (namedRoot != null)
            {
                return namedRoot.gameObject;
            }

            return agent.transform.parent != null ? agent.transform.parent.gameObject : agent.gameObject;
        }

        private static void SetLayerRecursive(GameObject gameObject, int layer)
        {
            gameObject.layer = layer;
            for (var index = 0; index < gameObject.transform.childCount; index++)
            {
                SetLayerRecursive(gameObject.transform.GetChild(index).gameObject, layer);
            }
        }

        private static MapIcon CreateStationMapIcon(Transform stationRoot, string id)
        {
            var iconObject = new GameObject("RAIL Station MapIcon", typeof(RectTransform));
            iconObject.transform.SetParent(stationRoot, false);
            var canvas = iconObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = MapBuilder.Shared != null ? MapBuilder.Shared.mapCamera : null;
            canvas.sortingOrder = 20;
            var icon = iconObject.AddComponent<MapIcon>();

            var rect = iconObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(36f, 36f);

            var imageObject = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(iconObject.transform, false);
            var imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.anchorMin = new Vector2(0.5f, 0.5f);
            imageRect.anchorMax = new Vector2(0.5f, 0.5f);
            imageRect.pivot = new Vector2(0.5f, 0.5f);
            imageRect.anchoredPosition = Vector2.zero;
            imageRect.sizeDelta = new Vector2(36f, 36f);

            var image = imageObject.GetComponent<Image>();
            image.sprite = GetStationIconSprite();
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;

            AddMapIconCollider(iconObject);
            iconObject.name = $"RAIL Station MapIcon - {id}";
            return icon;
        }

        private static void AddMapIconCollider(GameObject iconObject)
        {
            var boxColliderType = Type.GetType("UnityEngine.BoxCollider, UnityEngine.PhysicsModule");
            if (boxColliderType == null)
            {
                RailLog.Warning("RAIL station map icon click collider could not be created because UnityEngine.PhysicsModule was not available.");
                return;
            }

            var collider = iconObject.AddComponent(boxColliderType);
            boxColliderType.GetProperty("size")?.SetValue(collider, new Vector3(36f, 36f, 2f), null);
            boxColliderType.GetProperty("center")?.SetValue(collider, Vector3.zero, null);
        }

        private static Sprite GetStationIconSprite()
        {
            if (_stationIconSprite != null)
            {
                return _stationIconSprite;
            }

            const int size = 64;
            const float center = (size - 1) * 0.5f;
            const float outerRadius = 30f;
            const float innerRadius = 24f;
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                name = "RAIL Station Icon",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Mathf.Sqrt(Mathf.Pow(x - center, 2f) + Mathf.Pow(y - center, 2f));
                    var outerAlpha = Mathf.Clamp01(outerRadius - distance + 1f);
                    var innerAlpha = Mathf.Clamp01(distance - innerRadius + 1f);
                    var ringAlpha = Mathf.Min(outerAlpha, innerAlpha);
                    var fillAlpha = Mathf.Clamp01(innerRadius - distance + 1f);

                    var pixel = new Color32(0, 0, 0, 0);
                    if (ringAlpha > 0.01f)
                    {
                        pixel = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(ringAlpha * 255f));
                    }
                    else if (fillAlpha > 0.01f)
                    {
                        pixel = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(fillAlpha * 230f));
                    }

                    if (IsStationGlyphPixel(x, y))
                    {
                        pixel = new Color32(255, 255, 255, 255);
                    }

                    pixels[(y * size) + x] = pixel;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _stationIconSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            _stationIconSprite.name = "RAIL Station Icon";
            return _stationIconSprite;
        }

        private static bool IsStationGlyphPixel(int x, int y)
        {
            if (y >= 28 && y <= 40 && x >= 28 && x <= 36)
            {
                return false;
            }

            if (y >= 22 && y <= 40 && x >= 20 && x <= 44)
            {
                return true;
            }

            if (y >= 18 && y <= 22 && x >= 17 && x <= 47)
            {
                var centerOffset = Mathf.Abs(x - 32);
                return centerOffset <= y - 14;
            }

            if (y >= 40 && y <= 44 && x >= 18 && x <= 46)
            {
                return true;
            }

            return false;
        }

        private static StationAgent RequireStationAgent(string id)
        {
            var agent = GetStationAgent(id);
            if (agent == null)
            {
                throw new InvalidOperationException($"Station agent '{id}' was not found.");
            }

            return agent;
        }

        private static Transform GetStationRoot()
        {
            var world = GameObject.Find("World");
            if (world != null)
            {
                var existing = world.transform.Find("StationAgents");
                if (existing != null)
                {
                    return existing;
                }

                var stationRoot = new GameObject("StationAgents");
                stationRoot.transform.SetParent(world.transform, false);
                return stationRoot.transform;
            }

            if (_fallbackRoot == null)
            {
                _fallbackRoot = new GameObject("StationAgents").transform;
                UnityEngine.Object.DontDestroyOnLoad(_fallbackRoot.gameObject);
            }

            return _fallbackRoot;
        }

        private static void RequireId(string id, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("ID is required.", parameterName);
            }
        }
    }
}
