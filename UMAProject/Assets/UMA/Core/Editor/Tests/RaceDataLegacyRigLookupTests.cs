using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// <see cref="RaceData.GetLegacyRig(out UMASlotRigConversionProfile, out RaceData, Func{string, RaceData})"/>: a race's
    /// own Legacy Rig and profile first, empty ones filled in from the races it is cross-compatible with. In-memory races
    /// with a name lookup of the test's own, plus two checks against the Global Library and the converter.
    /// </summary>
    public sealed class RaceDataLegacyRigLookupTests
    {
        private readonly List<Object> created = new List<Object>();
        private readonly Dictionary<string, RaceData> library = new Dictionary<string, RaceData>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in created)
            {
                if (o != null) Object.DestroyImmediate(o);
            }
            created.Clear();
            library.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            created.Add(o);
            return o;
        }

        private RaceData Find(string name) => library.TryGetValue(name, out RaceData race) ? race : null;

        private UMARigRestPoseAsset Rig(string sourceRace)
        {
            UMARigRestPoseAsset rig = Track(ScriptableObject.CreateInstance<UMARigRestPoseAsset>());
            rig.name = sourceRace + " Rig Rest Pose";
            rig.sourceRaceName = sourceRace;
            return rig;
        }

        private UMASlotRigConversionProfile Profile(string name)
        {
            UMASlotRigConversionProfile profile = Track(ScriptableObject.CreateInstance<UMASlotRigConversionProfile>());
            profile.name = name;
            return profile;
        }

        private RaceData Race(string name, UMARigRestPoseAsset rig, UMASlotRigConversionProfile profile, params string[] crossCompatibleWith)
        {
            RaceData race = Track(ScriptableObject.CreateInstance<RaceData>());
            race.name = name;   // raceName follows the asset name
            Assert.AreEqual(name, race.raceName);
            race.legacyRig = rig;
            race.legacyRigProfile = profile;
            race.SetCrossCompatibleRaces(new List<string>(crossCompatibleWith));
            library[name] = race;
            return race;
        }

        [Test]
        public void OwnLinksAreUsedWithoutLookingAnythingUp()
        {
            UMARigRestPoseAsset rig = Rig("HumanFemale");
            UMASlotRigConversionProfile profile = Profile("Own");
            Race("Human Female 3.0", Rig("HumanFemale"), Profile("Human"));
            RaceData elf = Race("ElfFemale30", rig, profile, "Human Female 3.0");

            UMARigRestPoseAsset found = elf.GetLegacyRig(out UMASlotRigConversionProfile foundProfile, out RaceData source,
                name => throw new AssertionException("A race with both links set must not look up " + name));

            Assert.That(found, Is.SameAs(rig));
            Assert.That(foundProfile, Is.SameAs(profile));
            Assert.That(source, Is.SameAs(elf));
        }

        [Test]
        public void EmptyLinksComeFromTheCrossCompatibleRace()
        {
            UMARigRestPoseAsset rig = Rig("HumanFemale");
            UMASlotRigConversionProfile profile = Profile("Human Female 3.0 Legacy Rig Profile");
            RaceData human = Race("Human Female 3.0", rig, profile);
            RaceData elf = Race("ElfFemale30", null, null, "Human Female 3.0");

            UMARigRestPoseAsset found = elf.GetLegacyRig(out UMASlotRigConversionProfile foundProfile, out RaceData source, Find);

            Assert.That(found, Is.SameAs(rig));
            Assert.That(foundProfile, Is.SameAs(profile));
            Assert.That(source, Is.SameAs(human));
        }

        [Test]
        public void OwnProfileIsKeptWhenTheLegacyRigIsInherited()
        {
            UMARigRestPoseAsset rig = Rig("HumanFemale");
            Race("Human Female 3.0", rig, Profile("Human"));
            UMASlotRigConversionProfile own = Profile("Sprite tuning");
            RaceData sprite = Race("SpriteFemale30", null, own, "Human Female 3.0");

            Assert.That(sprite.GetLegacyRig(out UMASlotRigConversionProfile foundProfile, out _, Find), Is.SameAs(rig));
            Assert.That(foundProfile, Is.SameAs(own));
        }

        [Test]
        public void OwnLegacyRigTakesAProfileOnlyFromARaceWithTheSameRig()
        {
            UMARigRestPoseAsset femaleRig = Rig("HumanFemale");
            Race("Other Rig Race", Rig("HumanMale"), Profile("Made for the male rig"));
            UMASlotRigConversionProfile sameRigProfile = Profile("Made for the female rig");
            Race("Human Female 3.0", femaleRig, sameRigProfile);
            RaceData mixed = Race("Mixed", femaleRig, null, "Other Rig Race", "Human Female 3.0");
            RaceData onlyOther = Race("Only Other", femaleRig, null, "Other Rig Race");

            Assert.That(mixed.GetLegacyRig(out UMASlotRigConversionProfile mixedProfile, out RaceData mixedSource, Find), Is.SameAs(femaleRig));
            Assert.That(mixedProfile, Is.SameAs(sameRigProfile));
            Assert.That(mixedSource, Is.SameAs(mixed), "the Legacy Rig is the race's own");

            Assert.That(onlyOther.GetLegacyRig(out UMASlotRigConversionProfile otherProfile, out _, Find), Is.SameAs(femaleRig));
            Assert.That(otherProfile, Is.Null, "a profile made for another rig must not be used");
        }

        [Test]
        public void LookupFollowsCrossCompatibleRacesInTurn()
        {
            UMARigRestPoseAsset rig = Rig("HumanMale");
            UMASlotRigConversionProfile profile = Profile("Human Male");
            RaceData human = Race("Human Male 3.0", rig, profile);
            Race("ElfMale30", null, null, "Human Male 3.0");
            RaceData highElf = Race("High Elf Male", null, null, "ElfMale30");

            Assert.That(highElf.GetLegacyRig(out UMASlotRigConversionProfile foundProfile, out RaceData source, Find), Is.SameAs(rig));
            Assert.That(foundProfile, Is.SameAs(profile));
            Assert.That(source, Is.SameAs(human));
        }

        [Test]
        public void FirstCrossCompatibleRaceWithALegacyRigWins()
        {
            UMARigRestPoseAsset femaleRig = Rig("HumanFemale");
            Race("No Rig", null, null);
            Race("Human Female 3.0", femaleRig, Profile("Female"));
            Race("Human Male 3.0", Rig("HumanMale"), Profile("Male"));
            RaceData both = Race("Both", null, null, "Missing Race", "No Rig", "Human Female 3.0", "Human Male 3.0");

            Assert.That(both.GetLegacyRig(out _, out _, Find), Is.SameAs(femaleRig));
        }

        [Test]
        public void RacesThatListEachOtherDoNotLoop()
        {
            RaceData a = Race("A", null, null, "B");
            Race("B", null, null, "A", "B");

            Assert.That(a.GetLegacyRig(out UMASlotRigConversionProfile profile, out RaceData source, Find), Is.Null);
            Assert.That(profile, Is.Null);
            Assert.That(source, Is.Null);
        }

        [Test]
        public void RaceWithNoLinksAndNoCrossCompatibleRacesHasNoLegacyRig()
        {
            RaceData alone = Race("HumanMale", null, null);

            Assert.That(alone.GetLegacyRig(out UMASlotRigConversionProfile profile, out RaceData source, Find), Is.Null);
            Assert.That(profile, Is.Null);
            Assert.That(source, Is.Null);
        }

        [Test]
        public void DefaultLookupFindsTheCrossCompatibleRaceInTheGlobalLibrary()
        {
            RaceData human = UMAAssetIndexer.Instance != null ? UMAAssetIndexer.Instance.GetRace("Human Male 3.0") : null;
            if (human == null || human.legacyRig == null)
            {
                Assert.Ignore("Human Male 3.0 with a Legacy Rig is not in the Global Library.");
            }
            RaceData derived = Race("LegacyRigLookupTest Male", null, null, human.raceName);

            Assert.That(derived.GetLegacyRig(out UMASlotRigConversionProfile profile), Is.SameAs(human.legacyRig));
            Assert.That(profile, Is.SameAs(human.legacyRigProfile));
        }

        [Test]
        public void ConverterUsesTheLegacyRigOfTheCrossCompatibleRace()
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            RaceData human = indexer != null ? indexer.GetRace("Human Male 3.0") : null;
            SlotDataAsset jacket = indexer != null ? indexer.GetAsset<SlotDataAsset>("M_Bard Jacket") : null;
            if (human == null || human.legacyRig == null || jacket == null || !jacket.isLegacySlot)
            {
                Assert.Ignore("Needs Human Male 3.0 with a Legacy Rig and the UMA 2 'M_Bard Jacket' slot in the Global Library.");
            }

            // A race built like Human Male 3.0 (same base recipe, so the same rig) that leaves both links empty.
            RaceData derived = Track(Object.Instantiate(human));
            derived.name = "LegacyRigLookupTest Male";
            derived._oldRaceName = derived.name;
            derived.legacyRig = null;
            derived.legacyRigProfile = null;
            derived.SetCrossCompatibleRaces(new List<string> { human.raceName });

            var onDerived = new[] { new SlotData(jacket) };
            var onHuman = new[] { new SlotData(jacket) };
            UMALegacySlotConverter.ClearCache();
            try
            {
                Assert.AreEqual(1, UMALegacySlotConverter.ConvertLegacySlots(onDerived, derived), "the jacket is converted through the inherited Legacy Rig");
                Assert.AreEqual(1, UMALegacySlotConverter.ConvertLegacySlots(onHuman, human));
                Assert.AreNotSame(jacket, onDerived[0].asset);
                Assert.IsFalse(onDerived[0].asset.isLegacySlot);

                Vector3[] a = onDerived[0].asset.meshData.vertices;
                Vector3[] b = onHuman[0].asset.meshData.vertices;
                Assert.AreEqual(b.Length, a.Length);
                float worst = 0f;
                for (int i = 0; i < a.Length; i++) worst = Mathf.Max(worst, (a[i] - b[i]).magnitude);
                Assert.That(worst, Is.LessThanOrEqualTo(1e-5f), "the same rig and profile must give the same converted jacket (metres)");
            }
            finally
            {
                UMALegacySlotConverter.ClearCache();
                foreach (SlotData slot in new[] { onDerived[0], onHuman[0] })
                {
                    if (slot.asset != null && slot.asset != jacket && !EditorUtility.IsPersistent(slot.asset)) Object.DestroyImmediate(slot.asset);
                }
            }
        }
    }
}
