using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using RAIL.Infrastructure;
using RAIL.Serialization;

namespace RAIL.Authoring
{
    public sealed class RailEditableMember
    {
        private readonly MemberInfo _member;

        internal RailEditableMember(MemberInfo member, RailAuthoringEntity owner)
        {
            _member = member ?? throw new ArgumentNullException(nameof(member));
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Editable = member.GetCustomAttribute<RailEditableAttribute>(true);
            Hidden = member.GetCustomAttribute<RailHiddenAttribute>(true) != null;
            ReadOnly = member.GetCustomAttribute<RailReadOnlyAttribute>(true) != null || !CanWrite(member);
            Range = member.GetCustomAttribute<RailRangeAttribute>(true);
            Dropdown = member.GetCustomAttribute<RailDropdownAttribute>(true);
            Reference = member.GetCustomAttribute<RailReferenceAttribute>(true);
        }

        public RailAuthoringEntity Owner { get; }
        public string Name => _member.Name;
        public string Label => string.IsNullOrWhiteSpace(Editable?.Label) ? SplitCamelCase(Name) : Editable.Label;
        public string Group => Editable?.Group ?? string.Empty;
        public int Order => Editable?.Order ?? 0;
        public Type ValueType => GetMemberType(_member);
        public bool Hidden { get; }
        public bool ReadOnly { get; }
        public RailRangeAttribute Range { get; }
        public RailDropdownAttribute Dropdown { get; }
        public RailReferenceAttribute Reference { get; }
        public RailEditableAttribute Editable { get; }

        public object GetValue()
        {
            var property = _member as PropertyInfo;
            if (property != null)
            {
                return property.GetValue(Owner, null);
            }

            return ((FieldInfo)_member).GetValue(Owner);
        }

        public void SetValue(object value)
        {
            if (ReadOnly)
            {
                throw new InvalidOperationException($"Authoring member '{Owner.Id}.{Name}' is read-only.");
            }

            var previous = GetValue();
            var converted = RailAuthoringValueConverter.ConvertValue(value, ValueType);
            if (ValuesEqual(previous, converted))
            {
                return;
            }

            var property = _member as PropertyInfo;
            if (property != null)
            {
                property.SetValue(Owner, converted, null);
            }
            else
            {
                ((FieldInfo)_member).SetValue(Owner, converted);
            }

            Owner.OnEditableMemberChanged(this, previous, converted);
        }

        internal static bool IsEditable(MemberInfo member)
        {
            return member.GetCustomAttribute<RailEditableAttribute>(true) != null &&
                   member.GetCustomAttribute<RailHiddenAttribute>(true) == null;
        }

        private static bool CanWrite(MemberInfo member)
        {
            var property = member as PropertyInfo;
            if (property != null)
            {
                return property.CanWrite && property.GetSetMethod(true) != null;
            }

            var field = member as FieldInfo;
            return field != null && !field.IsInitOnly && !field.IsLiteral;
        }

        private static Type GetMemberType(MemberInfo member)
        {
            var property = member as PropertyInfo;
            if (property != null)
            {
                return property.PropertyType;
            }

            return ((FieldInfo)member).FieldType;
        }

        private static string SplitCamelCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return string.Concat(value.Select((character, index) =>
                index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));
        }

        private static bool ValuesEqual(object left, object right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            if (left.Equals(right))
            {
                return true;
            }

            try
            {
                var serializer = RailSerializer.GetSerializer();
                return JToken.DeepEquals(
                    JToken.FromObject(left, serializer),
                    JToken.FromObject(right, serializer));
            }
            catch (Exception ex)
            {
                RailLog.Warning($"RAIL authoring could not compare values for editable member: {ex.Message}");
                return false;
            }
        }
    }
}
