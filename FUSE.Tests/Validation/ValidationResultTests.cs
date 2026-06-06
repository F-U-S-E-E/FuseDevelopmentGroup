using FUSE.Authoring.Validation;
using Xunit;

namespace FUSE.Tests.Validation
{
    public class ValidationResultTests
    {
        [Fact]
        public void NewResult_IsValid_WithNoErrorsOrWarnings()
        {
            var result = new ValidationResult();

            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
        }

        [Fact]
        public void AddError_MakesResultInvalid()
        {
            var result = new ValidationResult();

            result.AddError("field", "message");

            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
            Assert.Empty(result.Warnings);
        }

        [Fact]
        public void AddWarning_LeavesResultValid()
        {
            var result = new ValidationResult();

            result.AddWarning("field", "message");

            Assert.True(result.IsValid);
            Assert.Single(result.Warnings);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void AddError_PreservesAllFields()
        {
            var result = new ValidationResult();

            result.AddError("name", "is required", "REQ-001", "empty-string");

            var issue = Assert.Single(result.Errors);
            Assert.Equal("name", issue.Field);
            Assert.Equal("is required", issue.Message);
            Assert.Equal("REQ-001", issue.Code);
            Assert.Equal("empty-string", issue.Value);
        }

        [Fact]
        public void Merge_CombinesErrorsAndWarningsFromOther()
        {
            var a = new ValidationResult();
            a.AddError("a", "error-a");
            a.AddWarning("a", "warn-a");

            var b = new ValidationResult();
            b.AddError("b", "error-b");
            b.AddWarning("b", "warn-b");

            a.Merge(b);

            Assert.Equal(2, a.Errors.Count);
            Assert.Equal(2, a.Warnings.Count);
            Assert.Contains(a.Errors, e => e.Field == "b");
            Assert.Contains(a.Warnings, w => w.Field == "b");
        }

        [Fact]
        public void Merge_WithNull_IsNoOp()
        {
            var result = new ValidationResult();
            result.AddError("field", "message");

            result.Merge(null);

            Assert.Single(result.Errors);
        }
    }
}
