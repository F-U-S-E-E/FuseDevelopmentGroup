using System;

namespace RAIL.Authoring
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class RailEditableAttribute : Attribute
    {
        public RailEditableAttribute()
        {
        }

        public RailEditableAttribute(string label)
        {
            Label = label;
        }

        public string Label { get; }
        public string Group { get; set; }
        public int Order { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class RailHiddenAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class RailReadOnlyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class RailRangeAttribute : Attribute
    {
        public RailRangeAttribute(float min, float max)
        {
            Min = min;
            Max = max;
        }

        public float Min { get; }
        public float Max { get; }
        public float Step { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class RailDropdownAttribute : Attribute
    {
        public RailDropdownAttribute(params string[] values)
        {
            Values = values ?? new string[0];
        }

        public string[] Values { get; }
        public string SourceMember { get; set; }
        public bool AllowCustomValue { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class RailReferenceAttribute : Attribute
    {
        public RailReferenceAttribute(string targetKind)
        {
            TargetKind = targetKind ?? string.Empty;
        }

        public string TargetKind { get; }
        public bool AllowNull { get; set; }
    }
}
