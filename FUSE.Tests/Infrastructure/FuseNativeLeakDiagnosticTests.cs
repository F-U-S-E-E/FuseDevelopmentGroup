using FUSE.Infrastructure;
using Unity.Collections;
using Xunit;

namespace FUSE.Tests.Infrastructure
{
    public sealed class FuseNativeLeakDiagnosticTests
    {
        [Fact]
        public void ShouldRestoreOriginalMode_WhenCurrentModeIsOwnedChange()
        {
            Assert.True(FuseNativeLeakDiagnostic.ShouldRestoreOriginalMode(
                ownsMode: true,
                restoreMode: NativeLeakDetectionMode.Enabled,
                lastAppliedMode: NativeLeakDetectionMode.EnabledWithStackTrace,
                currentMode: NativeLeakDetectionMode.EnabledWithStackTrace));
        }

        [Fact]
        public void ShouldRestoreOriginalMode_DoesNotClobberExternalChange()
        {
            Assert.False(FuseNativeLeakDiagnostic.ShouldRestoreOriginalMode(
                ownsMode: true,
                restoreMode: NativeLeakDetectionMode.Enabled,
                lastAppliedMode: NativeLeakDetectionMode.EnabledWithStackTrace,
                currentMode: NativeLeakDetectionMode.Disabled));
        }

        [Fact]
        public void ShouldRestoreOriginalMode_SkipsWhenFUSEDidNotChangeMode()
        {
            Assert.False(FuseNativeLeakDiagnostic.ShouldRestoreOriginalMode(
                ownsMode: true,
                restoreMode: NativeLeakDetectionMode.EnabledWithStackTrace,
                lastAppliedMode: NativeLeakDetectionMode.EnabledWithStackTrace,
                currentMode: NativeLeakDetectionMode.EnabledWithStackTrace));
        }

        [Fact]
        public void ShouldRestoreOriginalMode_SkipsWhenFUSEDoesNotOwnMode()
        {
            Assert.False(FuseNativeLeakDiagnostic.ShouldRestoreOriginalMode(
                ownsMode: false,
                restoreMode: NativeLeakDetectionMode.Enabled,
                lastAppliedMode: NativeLeakDetectionMode.EnabledWithStackTrace,
                currentMode: NativeLeakDetectionMode.EnabledWithStackTrace));
        }
    }
}
