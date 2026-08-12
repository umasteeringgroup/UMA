#if UNITY_EDITOR

using NUnit.Framework;

namespace UMA.Editors.Tests
{
    public sealed class UMARaceSmokeTests
    {
        [Test]
        [Category("UMA")]
        [Category("Smoke")]
        public void AllIndexedRacesPassSmokeTest()
        {
            UMATestReport report = UMARaceSmokeTestRunner.RunAllIndexed(
                new UMARaceSmokeTestOptions
                {
                    ValidateBaseRecipe = true,
                    GenerateTemporaryAvatar = true,
                    IncludePassMessages = false
                });
            Assert.That(report.HasErrors, Is.False, report.ToLogString());
        }
    }
}

#endif
