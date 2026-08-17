using System;
using System.Linq;
using System.Reflection.Emit;
using Effects.Decals;
using FUSE.Infrastructure;
using FUSE.Patches;
using FUSE.Tests.Infrastructure;
using HarmonyLib;
using Xunit;

namespace FUSE.Tests.Patches
{
    [Collection(FuseRuntimeGuardCountersTestCollection.Name)]
    public sealed class FuseDecalVisibilityCallbackGuardTests
    {
        public FuseDecalVisibilityCallbackGuardTests()
        {
            FuseRuntimeGuardCounters.ResetForTests();
        }

        [Fact]
        public void SafeInvoke_NullCallback_IsANoOp()
        {
            FuseDecalVisibilityCallbackGuardPatch.InvokeVisibilityCallbackSafely(null, true);

            Assert.Equal(0, FuseRuntimeGuardCounters.DecalVisibilitySuppressed);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SafeInvoke_ForwardsVisibilityWithoutChangingHealthyBehavior(bool visible)
        {
            var calls = 0;
            var received = !visible;

            FuseDecalVisibilityCallbackGuardPatch.InvokeVisibilityCallbackSafely(
                value =>
                {
                    calls++;
                    received = value;
                },
                visible);

            Assert.Equal(1, calls);
            Assert.Equal(visible, received);
            Assert.Equal(0, FuseRuntimeGuardCounters.DecalVisibilitySuppressed);
        }

        [Fact]
        public void SafeInvoke_ContainsThrowingCallback_AndAllowsLaterCycles()
        {
            var calls = 0;
            Action<bool> callback = _ =>
            {
                calls++;
                throw new InvalidOperationException("third-party decal callback failed");
            };

            var first = Record.Exception(
                () => FuseDecalVisibilityCallbackGuardPatch.InvokeVisibilityCallbackSafely(callback, true));
            var second = Record.Exception(
                () => FuseDecalVisibilityCallbackGuardPatch.InvokeVisibilityCallbackSafely(callback, false));

            Assert.Null(first);
            Assert.Null(second);
            Assert.Equal(2, calls);
            Assert.Equal(2, FuseRuntimeGuardCounters.DecalVisibilitySuppressed);
            Assert.Equal(2, FuseRuntimeGuardCounters.GuardTotal);
        }

        [Fact]
        public void Rewrite_CurrentGameIl_ReplacesTheSingleVulnerableDelegateCall()
        {
            var target = AccessTools.Method(
                typeof(DecalCullingManager),
                "UpdateDecalVisibilityJob");
            Assert.NotNull(target);

            var actionInvoke = AccessTools.Method(
                typeof(Action<bool>),
                nameof(Action<bool>.Invoke));
            var safeInvoke = AccessTools.Method(
                typeof(FuseDecalVisibilityCallbackGuardPatch),
                nameof(FuseDecalVisibilityCallbackGuardPatch.InvokeVisibilityCallbackSafely));
            var original = PatchProcessor.GetOriginalInstructions(target);
            var vulnerableCalls = original
                .Where(instruction =>
                    instruction.opcode == OpCodes.Callvirt &&
                    Equals(instruction.operand, actionInvoke))
                .ToList();

            var vulnerableCall = Assert.Single(vulnerableCalls);
            var rewritten =
                FuseDecalVisibilityCallbackGuardPatch.RewriteVisibilityCallbackInvocation(original);

            Assert.True(FuseDecalVisibilityCallbackGuardPatch.RewriteInstalled);
            Assert.DoesNotContain(
                rewritten,
                instruction =>
                    instruction.opcode == OpCodes.Callvirt &&
                    Equals(instruction.operand, actionInvoke));
            var replacement = Assert.Single(
                rewritten,
                instruction =>
                    instruction.opcode == OpCodes.Call &&
                    Equals(instruction.operand, safeInvoke));
            Assert.Same(vulnerableCall, replacement);
        }

        [Fact]
        public void Rewrite_NoMatchingCall_LeavesInstructionsUntouched()
        {
            var returnInstruction = new CodeInstruction(OpCodes.Ret);

            var rewritten =
                FuseDecalVisibilityCallbackGuardPatch.RewriteVisibilityCallbackInvocation(
                    new[] { returnInstruction });

            Assert.False(FuseDecalVisibilityCallbackGuardPatch.RewriteInstalled);
            Assert.Same(returnInstruction, Assert.Single(rewritten));
        }

        [Fact]
        public void Rewrite_AmbiguousCalls_LeavesBothInstructionsUntouched()
        {
            var actionInvoke = AccessTools.Method(
                typeof(Action<bool>),
                nameof(Action<bool>.Invoke));
            var first = new CodeInstruction(OpCodes.Callvirt, actionInvoke);
            var second = new CodeInstruction(OpCodes.Callvirt, actionInvoke);

            var rewritten =
                FuseDecalVisibilityCallbackGuardPatch.RewriteVisibilityCallbackInvocation(
                    new[] { first, second });

            Assert.False(FuseDecalVisibilityCallbackGuardPatch.RewriteInstalled);
            Assert.Same(first, rewritten[0]);
            Assert.Same(second, rewritten[1]);
            Assert.All(rewritten, instruction => Assert.Equal(OpCodes.Callvirt, instruction.opcode));
        }
    }
}
