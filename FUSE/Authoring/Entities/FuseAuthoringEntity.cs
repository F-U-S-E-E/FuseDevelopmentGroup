using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Authoring.Serialization;
using FUSE.Authoring.Validation;
using UnityEngine;

namespace FUSE.Authoring.Entities
{
    public abstract class FuseAuthoringEntity
    {
        private readonly List<string> _dirtyReasons = new List<string>();
        private GameObject _runtimeGameObject;
        private Component _runtimeComponent;
        private bool _suppressDirtyTracking;

        protected FuseAuthoringEntity(string id = null, string packageId = null)
        {
            Id = id ?? string.Empty;
            PackageId = packageId ?? string.Empty;
        }

        [FuseEditable("Id", Group = "Identity", Order = -100)]
        [FuseReadOnly]
        public string Id { get; protected set; }

        [FuseEditable("Package", Group = "Identity", Order = -90)]
        [FuseReadOnly]
        public string PackageId { get; protected set; }

        [FuseEditable("Display Name", Group = "Identity", Order = -80)]
        public string DisplayName { get; set; }

        [FuseHidden]
        public abstract string EntityKind { get; }

        [FuseHidden]
        public bool IsDirty { get; private set; }

        [FuseHidden]
        public int DirtyVersion { get; private set; }

        [FuseHidden]
        public bool RebuildOnEditableChange { get; set; }

        [FuseHidden]
        public bool QueueAutosaveOnEditableChange { get; set; }

        [FuseHidden]
        public bool IsAutosaveQueued { get; private set; }

        [FuseHidden]
        public string LastDirtyReason => _dirtyReasons.Count == 0 ? string.Empty : _dirtyReasons[_dirtyReasons.Count - 1];

        [FuseHidden]
        public IReadOnlyList<string> DirtyReasons => _dirtyReasons;

        [FuseHidden]
        public ValidationResult LastValidation { get; protected set; }

        [FuseHidden]
        public GameObject RuntimeGameObject => _runtimeGameObject;

        [FuseHidden]
        public Component RuntimeComponent => _runtimeComponent;

        [FuseHidden]
        public bool HasRuntimeBinding => _runtimeGameObject != null || _runtimeComponent != null;

        [FuseHidden]
        public FuseModDefinition OwningDefinition { get; private set; }

        [FuseHidden]
        public string DefinitionPath { get; private set; } = string.Empty;

        public virtual IEnumerable<FuseEditableMember> GetEditableMembers()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return GetType()
                .GetMembers(flags)
                .Where(member => member.MemberType == MemberTypes.Field || member.MemberType == MemberTypes.Property)
                .Where(FuseEditableMember.IsEditable)
                .Select(member => new FuseEditableMember(member, this))
                .OrderBy(member => member.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(member => member.Order)
                .ThenBy(member => member.Label, StringComparer.OrdinalIgnoreCase);
        }

        public FuseEditableMember GetEditableMember(string name)
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

        public void BindDefinition(FuseModDefinition definition, string definitionPath = null)
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
            FuseAuthoringRegistry.BindRuntime(this, gameObject, component);
            OnRuntimeBound();
        }

        public virtual ValidationResult Validate()
        {
            var result = new ValidationResult();
            if (string.IsNullOrWhiteSpace(Id))
            {
                result.AddError(nameof(Id), "Authoring entity id is required.", "fuse.authoring.id.required");
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
                    : JToken.FromObject(member.GetValue(), FuseSerializer.GetSerializer());
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
                            FuseLog.Exception($"FUSE authoring entity '{Id}' could not load property '{property.Name}'", ex);
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

        public virtual bool SaveToDefinition(FuseModDefinition definition)
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

            FuseAuthoringRegistry.Register(this);
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
            FuseAuthoringPersistenceService.QueueAutosave(this, reason);
        }

        public void ClearAutosaveQueued()
        {
            IsAutosaveQueued = false;
        }

        internal void OnEditableMemberChanged(FuseEditableMember member, object previousValue, object newValue)
        {
            if (_suppressDirtyTracking || member == null)
            {
                return;
            }

            MarkDirty("editable member changed: " + member.Name, QueueAutosaveOnEditableChange);
            if (RebuildOnEditableChange)
            {
                FuseAuthoringPersistenceService.RebuildEntity(this);
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
            if (FuseSettings.VerboseApplyReportDetails)
            {
                FuseLog.Info($"FUSE authoring entity '{Id}' bound runtime object='{_runtimeGameObject?.name ?? string.Empty}' component='{_runtimeComponent?.GetType().Name ?? string.Empty}'.");
            }
        }
    }
}
