using FUSE.Validation;
using Xunit;

namespace FUSE.Tests.Validation
{
    public class ValidationIssueTests
    {
        [Fact]
        public void Constructor_NormalizesNullStringsToEmpty()
        {
            var issue = new ValidationIssue(null, null, null, null);

            Assert.Equal(string.Empty, issue.Field);
            Assert.Equal(string.Empty, issue.Message);
            Assert.Equal(string.Empty, issue.Code);
            Assert.Null(issue.Value);
        }

        [Fact]
        public void Constructor_DefaultCodeIsEmpty()
        {
            var issue = new ValidationIssue("field", "message");

            Assert.Equal(string.Empty, issue.Code);
        }

        [Fact]
        public void Constructor_PreservesNonStringValue()
        {
            var sentinel = new object();

            var issue = new ValidationIssue("field", "message", value: sentinel);

            Assert.Same(sentinel, issue.Value);
        }
    }
}
