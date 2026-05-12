#if UNITY_EDITOR

using System.Text;
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
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            Assert.NotNull(indexer, "UMAAssetIndexer.Instance is null.");

            RaceData[] races = indexer.GetAllRaces();
            Assert.NotNull(races, "UMAAssetIndexer.GetAllRaces returned null.");
            Assert.Greater(races.Length, 0, "UMAAssetIndexer.GetAllRaces returned no races.");

            StringBuilder failures = new StringBuilder();
            for (int i = 0; i < races.Length; i++)
            {
                RaceData race = races[i];
                if (race == null)
                {
                    failures.AppendLine("Indexed race " + i + " is null.");
                    continue;
                }

                UMATestReport report = UMARaceSmokeTestRunner.Run(race, new UMARaceSmokeTestOptions
                {
                    ValidateBaseRecipe = true,
                    GenerateTemporaryAvatar = true,
                    IncludePassMessages = false
                });

                if (report.HasErrors)
                {
                    failures.AppendLine(report.ToLogString());
                }
            }

            Assert.IsEmpty(failures.ToString());
        }
    }
}

#endif