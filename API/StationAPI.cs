using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Model;
using Model.Ops;
using RAIL.Cache;
using RAIL.Data;
using TMPro;
using UnityEngine;

namespace RAIL.API
{
    public static class StationAPI
    {
        private static readonly FieldInfo AreaField = typeof(StationAgent).GetField("area", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo PassengerStopField = typeof(StationAgent).GetField("passengerStop", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SecondaryAreasField = typeof(StationAgent).GetField("secondaryAreas", BindingFlags.Instance | BindingFlags.NonPublic);
        private static Transform _fallbackRoot;

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
            StationAgentCache.Instance.Set(id, stationAgent);
            return stationAgent;
        }

        public static void UpdateStationAgent(string id, RailStation definition)
        {
            var agent = RequireStationAgent(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyDefinition(agent.transform.parent != null ? agent.transform.parent.gameObject : agent.gameObject, id, definition);
            StationAgentCache.Instance.Set(id, RequireStationAgent(id));
        }

        public static void RemoveStationAgent(string id)
        {
            var agent = RequireStationAgent(id);
            var root = agent.transform.parent != null ? agent.transform.parent.gameObject : agent.gameObject;
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
            StationAgentCache.Instance.Remove(id);
        }

        public static StationAgent GetStationAgent(string id)
        {
            if (StationAgentCache.Instance.TryGetValue(id, out var cached))
            {
                return (StationAgent)cached;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<StationAgent>().FirstOrDefault(agent => agent.name == id)
                : null;
        }

        public static IEnumerable<StationAgent> GetAllStationAgents()
        {
            return UnityEngine.Object.FindObjectsOfType<StationAgent>();
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
            return stationAgent;
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
