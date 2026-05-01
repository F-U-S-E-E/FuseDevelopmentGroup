using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using RAIL.Data;
using RAIL.Infrastructure;
using RAIL.Serialization;
using RAIL.Validation;
using UnityEngine;

namespace RAIL.Authoring
{
    public abstract class RailAuthoringEntity
    {
        private readonly List<string> _dirtyReasons = new List<string>();
        private GameObject _runtimeGameObject;
        private Component _runtimeComponent;
        private bool _suppressDirtyTracking;

        protected RailAuthoringEntity(string id = null, string packageId = null)
        {
            Id = id ?? string.Empty;
            PackageId = packageId ?? string.Empty;
        }

        [RailEditable("Id", Group = "Identity", Order = -100)]
        [RailReadOnly]
        public string Id { get; protected set; }

        [RailEditable("Package", Group = "Identity", Order = -90)]
        [RailReadOnly]
        public string PackageId { get; protected set; }

        [RailEditable("Display Name", Group = "Identity", Order = -80)]
        public string DisplayName { get; set; }

        [RailHidden]
        public abstract string EntityKind { get; }

        [RailHidden]
        public bool IsDirty { get; private set; }

        [RailHidden]
        public int DirtyVersion { get; private set; }

        [RailHidden]
        public bool RebuildOnEditableChange { get; set; }

        [RailHidden]
        public bool QueueAutosaveOnEditableChange { get; set; }

        [RailHidden]
        public bool IsAutosaveQueued { get; private set; }

        [RailHidden]
        public string LastDirtyReason => _dirtyReasons.Count == 0 ? string.Empty : _dirtyReasons[_dirtyReasons.Count - 1];

        [RailHidden]
        public IReadOnlyList<string> DirtyReasons => _dirtyReasons;

        [RailHidden]
        public ValidationResult LastValidation { get; protected set; }

        [RailHidden]
        public GameObject RuntimeGameObject => _runtimeGameObject;

        [RailHidden]
        public Component RuntimeComponent => _runtimeComponent;

        [RailHidden]
        public bool HasRuntimeBinding => _runtimeGameObject != null || _runtimeComponent != null;

        [RailHidden]
        public RailModDefinition OwningDefinition { get; private set; }

        [RailHidden]
        public string DefinitionPath { get; private set; } = string.Empty;

        public virtual IEnumerable<RailEditableMember> GetEditableMembers()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return GetType()
                .GetMembers(flags)
                .Where(member => member.MemberType == MemberTypes.Field || member.MemberType == MemberTypes.Property)
                .Where(RailEditableMember.IsEditable)
                .Select(member => new RailEditableMember(member, this))
                .OrderBy(member => member.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(member => member.Order)
                .ThenBy(member => member.Label, StringComparer.OrdinalIgnoreCase);
        }

        public RailEditableMember GetEditableMember(string name)
        {
            return GetEditableMembers().FirstOrDefault(member =>
                string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public void SetEditableValue(string memberName, object value)
        {
            var member = GetEditableMember(memberName);
            if (member == null)
            {
                throw new InvalidOperationException($"Editable member '{memberName}' was not found on entity '{Id}'.");
            }

            member.SetValue(value);
        }

        public void InitializeIdentity(string id, string packageId)
        {
            Id = id ?? string.Empty;
            PackageId = packageId ?? string.Empty;
        }

        public void BindDefinition(RailModDefinition definition, string definitionPath = null)
        {
            OwningDefinition = definition;
            DefinitionPath = definitionPath ?? DefinitionPath ?? string.Empty;
            if (definition != null && string.IsNullOrWhiteSpace(PackageId))
            {
                PackageId = definition.Id ?? string.Empty;
            }
        }

        public void BindRuntime(GameObject gameObject, Component component = null)
        {
            _runtimeGameObject = gameObject;
            _runtimeComponent = component;
            RailAuthoringRegistry.BindRuntime(this, gameObject, component);
            OnRuntimeBound();
        }

        public virtual ValidationResult Validate()
        {
            var result = new ValidationResult();
            if (string.IsNullOrWhiteSpace(Id))
            {
                result.AddError(nameof(Id), "Authoring entity id is required.", "rail.authoring.id.required");
            }

            LastValidation = result;
            return result;
        }

        public virtual JObject SaveAuthoringData()
        {
            var data = new JObject
            {
                ["entityType"] = GetType().FullName,
                ["entityKind"] = EntityKind,
                ["id"] = Id,
                ["packageId"] = PackageId,
                ["displayName"] = DisplayName
            };

            var properties = new JObject();
            foreach (var member in GetEditableMembers())
            {
                if (member.ReadOnly)
                {
                    continue;
                }

                properties[member.Name] = member.GetValue() == null
                    ? JValue.CreateNull()
                    : JToken.FromObject(member.GetValue(), RailSerializer.GetSerializer());
            }

            data["properties"] = properties;
            OnBeforeSave(data);
            return data;
        }

        public virtual void LoadAuthoringData(JObject data)
        {
            if (data == null)
            {
                return;
            }

            Id = (string)data["id"] ?? Id;
            PackageId = (string)data["packageId"] ?? PackageId;
            DisplayName = (string)data["displayName"] ?? DisplayName;

            var properties = data["properties"] as JObject;
            if (properties != null)
            {
                _suppressDirtyTracking = true;
                try
                {
                    foreach (var property in properties.Properties())
                    {
                        var member = GetEditableMember(property.Name);
                        if (member == null || member.ReadOnly)
                        {
                            continue;
                        }

                        try
                        {
                            member.SetValue(property.Value);
                        }
                        catch (Exception ex)
                        {
                            RailLog.Warning($"RAIL authoring entity '{Id}' could not load property '{property.Name}': {ex.Message}");
                        }
                    }
                }
                finally
                {
                    _suppressDirtyTracking = false;
                }
            }

            ClearDirty();
            OnAfterLoad(data);
        }

        public virtual void CaptureFromRuntime()
        {
        }

        public virtual object BuildRuntimeData()
        {
            return null;
        }

        public virtual bool SaveToDefinition(RailModDefinition definition)
        {
            return false;
        }

        public virtual void ApplyToRuntime()
        {
        }

        public virtual void RebuildRuntime()
        {
            OnBeforeBuild();
            ApplyToRuntime();
            OnAfterBuild();
            ClearDirty();
        }

        public void MarkDirty(string reason = null)
        {
            MarkDirty(reason, QueueAutosaveOnEditableChange);
        }

        public void MarkDirty(string reason, bool queueAutosave)
        {
            if (_suppressDirtyTracking)
            {
                return;
            }

            IsDirty = true;
            DirtyVersion++;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                _dirtyReasons.Add(reason);
            }

            RailAuthoringRegistry.Register(this);
            if (queueAutosave)
            {
                QueueAutosave(reason);
            }
        }

        public void ClearDirty()
        {
            IsDirty = false;
            _dirtyReasons.Clear();
        }

        public void QueueAutosave(string reason = null)
        {
            IsAutosaveQueued = true;
            RailAuthoringPersistenceService.QueueAutosave(this, reason);
        }

        public void ClearAutosaveQueued()
        {
            IsAutosaveQueued = false;
        }

        internal void OnEditableMemberChanged(RailEditableMember member, object previousValue, object newValue)
        {
            if (_suppressDirtyTracking || member == null)
            {
                return;
            }

            MarkDirty("editable member changed: " + member.Name, QueueAutosaveOnEditableChange);
            if (RebuildOnEditableChange)
            {
                RailAuthoringPersistenceService.RebuildEntity(this);
            }
        }

        protected virtual void OnBeforeSave(JObject data)
        {
        }

        protected virtual void OnAfterLoad(JObject data)
        {
        }

        protected virtual void OnBeforeBuild()
        {
        }

        protected virtual void OnAfterBuild()
        {
        }

        protected virtual void OnRuntimeBound()
        {
            RailLog.Info($"RAIL authoring entity '{Id}' bound runtime object='{_runtimeGameObject?.name ?? string.Empty}' component='{_runtimeComponent?.GetType().Name ?? string.Empty}'.");
        }
    }
}
