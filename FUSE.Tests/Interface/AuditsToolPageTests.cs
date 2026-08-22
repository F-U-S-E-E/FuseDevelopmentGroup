using FUSE.Interface.MenuWindow;
using Xunit;

namespace FUSE.Tests.Interface
{
    public sealed class AuditsToolPageTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Base_game_span_without_package_owner_is_not_audited(string owner)
        {
            Assert.False(AuditsToolPage.ShouldAuditTrackSpanOwner(owner));
        }

        [Fact]
        public void Fuse_owned_span_remains_eligible_for_validation()
        {
            Assert.True(AuditsToolPage.ShouldAuditTrackSpanOwner("Katers.SylvaInterchange.FUSE"));
        }

        [Theory]
        [InlineData("shared-extension", false)]
        [InlineData("SHARED-EXTENSION", false)]
        [InlineData("ownership-conflict", true)]
        [InlineData("", true)]
        [InlineData(null, true)]
        public void Only_actionable_registry_records_are_audit_findings(string classification, bool expected)
        {
            Assert.Equal(expected, AuditsToolPage.ShouldAuditConflictRecord(classification));
        }
    }
}
