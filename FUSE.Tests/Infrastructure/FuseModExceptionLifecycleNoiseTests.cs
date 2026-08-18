using FUSE.Infrastructure;
using Xunit;

namespace FUSE.Tests.Infrastructure
{
    public sealed class FuseModExceptionLifecycleNoiseTests
    {
        [Theory]
        [InlineData("GP38Scripts.TractionMotorAudio.OnEnable")]
        [InlineData("GP38Scripts.GP38SmokeController.Start")]
        public void BmanSharedLocomotiveLifecycle_IsRecoverableForEveryAsset(string frame)
        {
            var stack = "  at " + frame + " ()\n" +
                "  at Model.Car.HandleModelsLoaded (Model.Car car)";

            Assert.True(FuseModExceptionLogHook.IsKnownRecoverableLifecycleNoise(
                "NullReferenceException: Object reference not set to an instance of an object",
                stack));
        }

        [Theory]
        [InlineData("Audio.ExhaustAudioController.StopPlaying")]
        [InlineData("Audio.ExhaustAudioController.PlayNext")]
        public void ExhaustPreviewLifecycle_IsRecoverableOnlyDuringCarModelActivation(string frame)
        {
            var activationStack = "  at " + frame + " ()\n" +
                "  at Model.Car.HandleModelsLoaded_Patch1 (Model.Car car)";
            var unrelatedStack = "  at " + frame + " ()\n" +
                "  at Some.Other.Update ()";

            Assert.True(FuseModExceptionLogHook.IsKnownRecoverableLifecycleNoise(
                "NullReferenceException: Object reference not set to an instance of an object",
                activationStack));
            Assert.False(FuseModExceptionLogHook.IsKnownRecoverableLifecycleNoise(
                "NullReferenceException: Object reference not set to an instance of an object",
                unrelatedStack));
        }

        [Fact]
        public void PlacerLibraryMissingTenderProbe_DoesNotBecomeSessionHealthFailure()
        {
            const string stack =
                "  at Model.Database.PrefabStore.AssetPackContainingIdentifier (System.String identifier)\n" +
                "  at UI.Placer.PlacerWindow.ConfigureRow (UI.Placer.LibraryRow row)\n" +
                "  at UI.Placer.PlacerWindow.RebuildLibrary ()";

            Assert.True(FuseModExceptionLogHook.IsKnownRecoverableLifecycleNoise(
                "UnknownIdentifierException: Unknown identifier: lt-280-c48",
                stack));
        }

        [Fact]
        public void OtherExceptions_RemainHealthObservations()
        {
            Assert.False(FuseModExceptionLogHook.IsKnownRecoverableLifecycleNoise(
                "InvalidOperationException: failed",
                "  at SomeMod.Controller.Update ()"));
            Assert.False(FuseModExceptionLogHook.IsKnownRecoverableLifecycleNoise(
                "UnknownIdentifierException: Unknown identifier: missing",
                "  at SomeMod.Loader.Load ()"));
            Assert.False(FuseModExceptionLogHook.IsKnownRecoverableLifecycleNoise(null, null));
        }
    }
}
