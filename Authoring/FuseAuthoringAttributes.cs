using System;

namespace FUSE.Authoring
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class FuseEditableAttribute : Attribute
    {
        public FuseEditableAttribute()
        {
        }

        public FuseEditableAttribute(string label)
        {
            Label = label;
        }

        public string Label { get; }
        public string Group { get; set; }
        public int Order { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class FuseHiddenAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class FuseReadOnlyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class FuseRangeAttribute : Attribute
    {
        public FuseRangeAttribute(float min, float max)
        {
            Min = min;
            Max = max;
        }

        public float Min { get; }
        public float Max { get; }
        public float Step { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class FuseDropdownAttribute : Attribute
    {
        public FuseDropdownAttribute(params string[] values)
        {
            Values = values ?? new string[0];
        }

        public string[] Values { get; }
        public string SourceMember { get; set; }
        public bool AllowCustomValue { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class FuseReferenceAttribute : Attribute
    {
        public FuseReferenceAttribute(string targetKind)
        {
            TargetKind = targetKind ?? string.Empty;
        }

        public string TargetKind { get; }
        public bool AllowNull { get; set; }
    }
}
