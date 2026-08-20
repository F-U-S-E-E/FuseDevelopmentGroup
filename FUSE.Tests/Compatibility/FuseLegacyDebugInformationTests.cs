using System;
using FUSE.Compatibility;
using GalaSoft.MvvmLight.Messaging;
using Railloader.Events;
using Xunit;

namespace FUSE.Tests.Compatibility
{
    public sealed class FuseLegacyDebugInformationTests
    {
        [Fact]
        public void Collect_DispatchesLegacyEventAndNormalizesMultilineContributions()
        {
            var recipient = new DebugRecipient();
            var lines = FuseLegacyDebugInformation.Collect(recipient.Handle);

            Assert.Contains("first", lines);
            Assert.Contains("second", lines);
            Assert.Contains("third", lines);
        }

        [Fact]
        public void Collect_BoundsOversizedLegacyContributions()
        {
            var recipient = new OversizedDebugRecipient();
            var lines = FuseLegacyDebugInformation.Collect(recipient.Handle);

            Assert.True(lines.Count <= FuseLegacyDebugInformation.MaximumLines + 1);
            Assert.Contains(lines, line => line.StartsWith("[FUSE truncated", StringComparison.Ordinal));
        }

        public sealed class DebugRecipient
        {
            public void Handle(WillCopyDebugInformation message)
            {
                message.AppendLine("first\r\nsecond\nthird");
            }
        }

        public sealed class OversizedDebugRecipient
        {
            public void Handle(WillCopyDebugInformation message)
            {
                for (var index = 0; index <= FuseLegacyDebugInformation.MaximumLines; index++)
                {
                    message.AppendLine("line " + index);
                }
            }
        }
    }
}
