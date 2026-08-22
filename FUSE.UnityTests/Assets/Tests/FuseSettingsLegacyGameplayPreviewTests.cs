using FUSE.Infrastructure;
using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Text;

namespace FUSE.UnityTests
{
    /// <summary>
    /// Slider previews update live legacy-gameplay values through the same
    /// clamps as their commit setters without writing a user override.
    /// </summary>
    public class FuseSettingsLegacyGameplayPreviewTests
    {
        private const string ControlledUserSettingsJson =
            "{\n" +
            "  \"InterchangeNotBeforeHour\": -901.0,\n" +
            "  \"InterchangeNotAfterHour\": -902.0,\n" +
            "  \"OutboundIndustryRerouteChance\": -903.0,\n" +
            "  \"OutboundIndustryFillFactor\": -904.0,\n" +
            "  \"OutboundIndustryPaymentMultiplier\": -905.0,\n" +
            "  \"UnrelatedSentinel\": \"preserve-me\"\n" +
            "}";

        [Test]
        public void PreviewInterchangeServiceHours_ClampLiveValues()
        {
            var originalNotBefore = FuseSettings.InterchangeNotBeforeHour;
            var originalNotAfter = FuseSettings.InterchangeNotAfterHour;
            string settingsPath;
            var originalSettingsFile = CaptureUserSettingsFile(out settingsPath);
            byte[] controlledSettingsFile = null;
            try
            {
                controlledSettingsFile = InstallControlledUserSettingsFile(settingsPath);

                FuseSettings.PreviewInterchangeNotBeforeHour(-1f);
                Assert.AreEqual(0f, FuseSettings.InterchangeNotBeforeHour);
                AssertUserSettingsFileUnchanged(
                    settingsPath,
                    controlledSettingsFile,
                    nameof(FuseSettings.PreviewInterchangeNotBeforeHour));

                FuseSettings.PreviewInterchangeNotAfterHour(float.PositiveInfinity);
                Assert.AreEqual(24f, FuseSettings.InterchangeNotAfterHour);
                AssertUserSettingsFileUnchanged(
                    settingsPath,
                    controlledSettingsFile,
                    nameof(FuseSettings.PreviewInterchangeNotAfterHour));

                FuseSettings.PreviewInterchangeNotBeforeHour(float.NaN);
                Assert.AreEqual(0f, FuseSettings.InterchangeNotBeforeHour);
                AssertUserSettingsFileUnchanged(
                    settingsPath,
                    controlledSettingsFile,
                    nameof(FuseSettings.PreviewInterchangeNotBeforeHour));
            }
            finally
            {
                try
                {
                    FuseSettings.PreviewInterchangeNotBeforeHour(originalNotBefore);
                    FuseSettings.PreviewInterchangeNotAfterHour(originalNotAfter);
                }
                finally
                {
                    RestoreUserSettingsFile(settingsPath, originalSettingsFile);
                }
            }
        }

        [Test]
        public void PreviewOutboundRoutingValues_ClampLiveValues()
        {
            var originalChance = FuseSettings.OutboundIndustryRerouteChance;
            var originalFillFactor = FuseSettings.OutboundIndustryFillFactor;
            var originalPaymentMultiplier = FuseSettings.OutboundIndustryPaymentMultiplier;
            string settingsPath;
            var originalSettingsFile = CaptureUserSettingsFile(out settingsPath);
            byte[] controlledSettingsFile = null;
            try
            {
                controlledSettingsFile = InstallControlledUserSettingsFile(settingsPath);

                FuseSettings.PreviewOutboundIndustryRerouteChance(float.NaN);
                Assert.AreEqual(
                    FuseSettings.DefaultOutboundIndustryRerouteChance,
                    FuseSettings.OutboundIndustryRerouteChance);
                AssertUserSettingsFileUnchanged(
                    settingsPath,
                    controlledSettingsFile,
                    nameof(FuseSettings.PreviewOutboundIndustryRerouteChance));

                FuseSettings.PreviewOutboundIndustryFillFactor(99f);
                Assert.AreEqual(3f, FuseSettings.OutboundIndustryFillFactor);
                AssertUserSettingsFileUnchanged(
                    settingsPath,
                    controlledSettingsFile,
                    nameof(FuseSettings.PreviewOutboundIndustryFillFactor));

                FuseSettings.PreviewOutboundIndustryPaymentMultiplier(-1f);
                Assert.AreEqual(0f, FuseSettings.OutboundIndustryPaymentMultiplier);
                AssertUserSettingsFileUnchanged(
                    settingsPath,
                    controlledSettingsFile,
                    nameof(FuseSettings.PreviewOutboundIndustryPaymentMultiplier));
            }
            finally
            {
                try
                {
                    FuseSettings.PreviewOutboundIndustryRerouteChance(originalChance);
                    FuseSettings.PreviewOutboundIndustryFillFactor(originalFillFactor);
                    FuseSettings.PreviewOutboundIndustryPaymentMultiplier(originalPaymentMultiplier);
                }
                finally
                {
                    RestoreUserSettingsFile(settingsPath, originalSettingsFile);
                }
            }
        }

        private static byte[] CaptureUserSettingsFile(out string path)
        {
            path = FuseSettings.GetUserSettingsPath();
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        private static byte[] InstallControlledUserSettingsFile(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var contents = Encoding.UTF8.GetBytes(ControlledUserSettingsJson);
            File.WriteAllBytes(path, contents);
            return contents;
        }

        private static void AssertUserSettingsFileUnchanged(
            string path,
            byte[] expectedContents,
            string previewMethod)
        {
            Assert.IsTrue(
                File.Exists(path),
                previewMethod + " changed whether the user-settings file exists.");
            CollectionAssert.AreEqual(
                expectedContents,
                File.ReadAllBytes(path),
                previewMethod + " changed the user-settings file contents.");
        }

        private static void RestoreUserSettingsFile(string path, byte[] originalContents)
        {
            if (originalContents == null)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(originalContents))
            {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, originalContents);
        }
    }
}
