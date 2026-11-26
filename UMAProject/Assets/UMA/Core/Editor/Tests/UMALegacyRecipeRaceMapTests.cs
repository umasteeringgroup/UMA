using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Which UMA 3 race the UMA 2 recipe library hook brings a UMA 2 race's recipes to
    /// (<see cref="UMALegacyRecipeLibraryHook.GetRaceMap(IEnumerable{RaceData}, List{string})"/>), on in-memory races.
    /// </summary>
    public sealed class UMALegacyRecipeRaceMapTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in created)
            {
                if (o != null) Object.DestroyImmediate(o);
            }
            created.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            created.Add(o);
            return o;
        }

        private UMARigRestPoseAsset Rig(string sourceRace)
        {
            UMARigRestPoseAsset rig = Track(ScriptableObject.CreateInstance<UMARigRestPoseAsset>());
            rig.name = sourceRace + " Rig Rest Pose";
            rig.sourceRaceName = sourceRace;
            return rig;
        }

        private RaceData Race(string name, UMARigRestPoseAsset legacyRig, params string[] crossCompatibleWith)
        {
            RaceData race = Track(ScriptableObject.CreateInstance<RaceData>());
            race.name = name;   // raceName follows the asset name
            Assert.AreEqual(name, race.raceName);
            race.legacyRig = legacyRig;
            race.SetCrossCompatibleRaces(new List<string>(crossCompatibleWith));
            return race;
        }

        [Test]
        public void OneRacePerLegacyRigMapsEachUma2Race()
        {
            RaceData female = Race("Human Female 3.0", Rig("HumanFemale"));
            RaceData male = Race("Human Male 3.0", Rig("HumanMale"));
            var problems = new List<string>();

            Dictionary<string, RaceData> map = UMALegacyRecipeLibraryHook.GetRaceMap(new[] { female, male }, problems);

            Assert.That(map.Count, Is.EqualTo(2));
            Assert.That(map["HumanFemale"], Is.SameAs(female));
            Assert.That(map["HumanMale"], Is.SameAs(male));
            Assert.That(problems, Is.Empty);
        }

        [Test]
        public void SharedLegacyRigGoesToTheRaceTheOthersAreCrossCompatibleWith()
        {
            UMARigRestPoseAsset femaleRig = Rig("HumanFemale");
            UMARigRestPoseAsset maleRig = Rig("HumanMale");
            // Derived races listed both before and after their base race, as the Global Library may order them.
            RaceData elfFemale = Race("ElfFemale30", femaleRig, "Human Female 3.0");
            RaceData female = Race("Human Female 3.0", femaleRig);
            RaceData spriteFemale = Race("SpriteFemale30", femaleRig, "Human Female 3.0");
            RaceData elfMale = Race("ElfMale30", maleRig, "Human Male 3.0");
            RaceData male = Race("Human Male 3.0", maleRig);
            var problems = new List<string>();

            Dictionary<string, RaceData> map = UMALegacyRecipeLibraryHook.GetRaceMap(
                new[] { elfFemale, female, spriteFemale, elfMale, male }, problems);

            Assert.That(map.Count, Is.EqualTo(2));
            Assert.That(map["HumanFemale"], Is.SameAs(female));
            Assert.That(map["HumanMale"], Is.SameAs(male));
            Assert.That(problems, Is.Empty);
        }

        [Test]
        public void SharedLegacyRigWithNoBaseRaceIsLeftOutAndReported()
        {
            UMARigRestPoseAsset rig = Rig("HumanFemale");
            RaceData female = Race("Human Female 3.0", rig);
            RaceData elfFemale = Race("ElfFemale30", rig, "Human Female 3.0");
            RaceData unrelated = Race("Goblin Female", rig);   // not cross-compatible with Human Female 3.0
            var problems = new List<string>();

            Dictionary<string, RaceData> map = UMALegacyRecipeLibraryHook.GetRaceMap(new[] { female, elfFemale, unrelated }, problems);

            Assert.That(map.ContainsKey("HumanFemale"), Is.False);
            Assert.That(problems.Count, Is.EqualTo(1));
            StringAssert.Contains("'HumanFemale'", problems[0]);
            StringAssert.Contains("'Goblin Female'", problems[0]);
        }

        [Test]
        public void TwoRacesCrossCompatibleWithEachOtherAreAmbiguous()
        {
            UMARigRestPoseAsset rig = Rig("HumanMale");
            RaceData a = Race("Human Male 3.0", rig, "Other Male");
            RaceData b = Race("Other Male", rig, "Human Male 3.0");
            var problems = new List<string>();

            Dictionary<string, RaceData> map = UMALegacyRecipeLibraryHook.GetRaceMap(new[] { a, b }, problems);

            Assert.That(map.ContainsKey("HumanMale"), Is.False);
            Assert.That(problems.Count, Is.EqualTo(1));
        }

        [Test]
        public void SameRaceListedTwiceIsNotAmbiguous()
        {
            // The Global Library can list one asset twice (duplicate entries with the same GUID).
            RaceData female = Race("Human Female 3.0", Rig("HumanFemale"));
            var problems = new List<string>();

            Dictionary<string, RaceData> map = UMALegacyRecipeLibraryHook.GetRaceMap(new[] { female, female, null }, problems);

            Assert.That(map["HumanFemale"], Is.SameAs(female));
            Assert.That(problems, Is.Empty);
        }

        [Test]
        public void RacesWithoutLegacyRigOrWithTheirOwnNameAsSourceAreIgnored()
        {
            RaceData none = Race("ElfFemale30", null, "Human Female 3.0");
            RaceData self = Race("HumanFemale", Rig("HumanFemale"));
            var problems = new List<string>();

            Dictionary<string, RaceData> map = UMALegacyRecipeLibraryHook.GetRaceMap(new[] { none, self }, problems);

            Assert.That(map, Is.Empty);
            Assert.That(problems, Is.Empty);
        }
    }
}
