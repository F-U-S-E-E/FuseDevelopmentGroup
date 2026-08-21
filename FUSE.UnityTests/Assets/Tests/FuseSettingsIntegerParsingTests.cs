using FUSE.Infrastructure;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.IO;

namespace FUSE.UnityTests
{
    public class FuseSettingsIntegerParsingTests
    {
        [Test]
        public void ReadInt_ReturnsDefaultForOutOfRangeIntegerToken()
        {
            var settings = JObject.Parse("{\"GraceMinimumDays\":2147483648}");

            var result = FuseSettings.ReadInt(settings, "GraceMinimumDays", 14);

            Assert.AreEqual(14, result);
        }

        [Test]
        public void SetInterchangeServiceHours_PreservesOvernightWindow()
        {
            var originalNotBefore = FuseSettings.InterchangeNotBeforeHour;
            var originalNotAfter = FuseSettings.InterchangeNotAfterHour;
            var settingsPath = FuseSettings.GetUserSettingsPath();
            var originalSettings = File.Exists(settingsPath)
                ? File.ReadAllBytes(settingsPath)
                : null;
            try
            {
                FuseSettings.PreviewInterchangeNotAfterHour(24f);
                FuseSettings.SetInterchangeNotBeforeHour(22f);
                FuseSettings.SetInterchangeNotAfterHour(6f);

                Assert.AreEqual(22f, FuseSettings.InterchangeNotBeforeHour);
                Assert.AreEqual(6f, FuseSettings.InterchangeNotAfterHour);
            }
            finally
            {
                FuseSettings.PreviewInterchangeNotBeforeHour(originalNotBefore);
                FuseSettings.PreviewInterchangeNotAfterHour(originalNotAfter);
                RestoreUserSettingsFile(settingsPath, originalSettings);
            }
        }

        private static void RestoreUserSettingsFile(string path, byte[] contents)
        {
            if (contents == null)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, contents);
        }
    }
}
