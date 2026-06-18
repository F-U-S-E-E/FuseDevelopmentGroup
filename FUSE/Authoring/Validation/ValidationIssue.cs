namespace FUSE.Authoring.Validation
{
    public sealed class ValidationIssue
    {
        public ValidationIssue(string field, string message, string code = null, object value = null)
        {
            Field = field ?? string.Empty;
            Message = message ?? string.Empty;
            Code = code ?? string.Empty;
            Value = value;
        }

        public string Field { get; }
        public string Message { get; }
        public string Code { get; }
        public object Value { get; }
    }
}
