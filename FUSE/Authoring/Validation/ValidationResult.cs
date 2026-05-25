using System.Collections.Generic;

namespace FUSE.Validation
{
    public sealed class ValidationResult
    {
        public List<ValidationIssue> Errors { get; } = new List<ValidationIssue>();
        public List<ValidationIssue> Warnings { get; } = new List<ValidationIssue>();

        public bool IsValid => Errors.Count == 0;

        public void AddError(string field, string message, string code = null, object value = null)
        {
            Errors.Add(new ValidationIssue(field, message, code, value));
        }

        public void AddWarning(string field, string message, string code = null, object value = null)
        {
            Warnings.Add(new ValidationIssue(field, message, code, value));
        }

        public void Merge(ValidationResult other)
        {
            if (other == null)
            {
                return;
            }

            Errors.AddRange(other.Errors);
            Warnings.AddRange(other.Warnings);
        }
    }
}
