using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    // Play Mode: the race-change cache only exists while playing.
    //
    // Switching between two races, a catfolk that wears a tail and a human that does not, with ChangeRace: each
    // switch to the human swaps the ears and calls ClearWearableItems("Tail"); each switch back puts the tail on
    // with SetWearableItem. The tail must stay off on every visit to the human, whichever order the script makes
    // the wardrobe calls and ChangeRace in.
    //
    // Defect: when the race changes, the avatar caches the state of the race it leaves, so that coming back
    // restores it. That state was read from the wardrobe as it was at the time of the ChangeRace call. A script
    // that dresses the avatar for the next race first and then calls ChangeRace therefore cached the next race's
    // outfit under the old race's name: the second visit to the human found the catfolk outfit (tail included)
    // cached as the human's, and Keep Wardrobe filled the empty Tail slot from it. The same happened when the
    // script also called BuildCharacter before ChangeRace in the same frame. The cache now stores the wardrobe the
    // generator last built as each race; a build replaced before the generator starts it does not count.
    //
    // The project has no catfolk content, so stand-ins with the same relationship are used: Human Male 3.0 plays
    // the catfolk and ElfMale30 (cross-compatible with it) plays the human, a goatee in the Beard slot is the tail,
    // and two hair styles are the cat ears and the human ears.
    public sealed class DynamicCharacterAvatarRaceWardrobeTests
    {
        private const string CatfolkRace = "Human Male 3.0";
        private const string HumanRace = "ElfMale30";
        private const string TailSlot = "Beard";
        private const string TailRecipeName = "Beard_Goatee";
        private const string CatEarsRecipeName = "HairPonytail_Recipe";
        private const string HumanEarsRecipeName = "HairPigtails_Recipe";
        private const int MaxBuildFrames = 600;

        private const DynamicCharacterAvatar.ChangeRaceOptions DefaultOptions = DynamicCharacterAvatar.ChangeRaceOptions.keepBodyColors;
        private const DynamicCharacterAvatar.ChangeRaceOptions KeepWardrobe = DynamicCharacterAvatar.ChangeRaceOptions.keepBodyColors | DynamicCharacterAvatar.ChangeRaceOptions.keepWardrobe;

        private readonly List<Object> created = new List<Object>();
        private UMAWardrobeRecipe tail;
        private UMAWardrobeRecipe catEars;
        private UMAWardrobeRecipe humanEars;
        // Slots only the tail adds, so its presence in the built character can be checked as well as in the wardrobe.
        private readonly HashSet<string> tailOnlySlots = new HashSet<string>();

        [SetUp]
        public void SetUp()
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null)
            {
                Assert.Ignore("The UMA Global Library is not available.");
            }
            RaceData catfolk = indexer.GetRace(CatfolkRace);
            RaceData human = indexer.GetRace(HumanRace);
            tail = indexer.GetRecipe(TailRecipeName, false) as UMAWardrobeRecipe;
            catEars = indexer.GetRecipe(CatEarsRecipeName, false) as UMAWardrobeRecipe;
            humanEars = indexer.GetRecipe(HumanEarsRecipeName, false) as UMAWardrobeRecipe;
            if (catfolk == null || human == null || tail == null || catEars == null || humanEars == null)
            {
                Assert.Ignore("The UMA 3 sample races (" + CatfolkRace + ", " + HumanRace + ") or the recipes " + TailRecipeName + ", " + CatEarsRecipeName + " and " + HumanEarsRecipeName + " are not installed.");
            }
            if (indexer.Generator == null)
            {
                Assert.Ignore("No UMA generator is available.");
            }

            // The stand-ins must relate the way the reported content does.
            Assert.That(tail.wardrobeSlot, Is.EqualTo(TailSlot));
            Assert.That(tail.Appended, Is.False);
            Assert.That(catEars.wardrobeSlot, Is.EqualTo(humanEars.wardrobeSlot), "The two ears share one wardrobe slot.");
            Assert.That(catEars.wardrobeSlot, Is.Not.EqualTo(TailSlot));
            Assert.That(human.IsCrossCompatibleWith(tail.compatibleRaces), Is.True, "The tail can be kept on the human, as in the report.");
            Assert.That(catfolk.wardrobeSlots, Does.Contain(TailSlot));
            Assert.That(human.wardrobeSlots, Does.Contain(TailSlot));

            tailOnlySlots.Clear();
            UMAData.UMARecipe tailRecipe = tail.GetCachedRecipe();
            if (tailRecipe != null && tailRecipe.slotDataList != null)
            {
                foreach (SlotData slot in tailRecipe.slotDataList)
                {
                    if (slot != null && !string.IsNullOrEmpty(slot.slotName))
                    {
                        tailOnlySlots.Add(slot.slotName);
                    }
                }
            }
            tailOnlySlots.ExceptWith(SlotNames(catfolk.baseRaceRecipe));
            tailOnlySlots.ExceptWith(SlotNames(human.baseRaceRecipe));
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null)
                {
                    Object.Destroy(created[i]);
                }
            }
            created.Clear();
        }

        // Where the script calls ChangeRace relative to its wardrobe calls.
        public enum Order
        {
            // ChangeRace, then the wardrobe calls, then BuildCharacter.
            ChangeRaceThenDress,
            // The wardrobe calls, then ChangeRace, then BuildCharacter.
            DressThenChangeRace,
            // The wardrobe calls and BuildCharacter, then ChangeRace in the same frame.
            BuildThenChangeRace,
        }

        // Keep Wardrobe on, so a race change keeps the outfit the script put on before it. Before the fix the tail
        // came back on the second switch to the human when the script dressed the avatar before ChangeRace.
        [UnityTest]
        public IEnumerator KeepWardrobe_ClearedTailStaysOff([Values] Order order)
        {
            return SwitchRacesThreeTimes(KeepWardrobe, order);
        }

        [UnityTest]
        public IEnumerator DefaultOptions_ChangeRaceThenDress_ClearedTailStaysOff()
        {
            return SwitchRacesThreeTimes(DefaultOptions, Order.ChangeRaceThenDress);
        }

        // Without Keep Wardrobe a race change replaces the wardrobe with the new race's cached state. Coming back to
        // the catfolk must restore the catfolk's own outfit, not the human outfit the script put on just before
        // the first ChangeRace. Before the fix the catfolk came back with the human ears and no tail.
        [UnityTest]
        public IEnumerator DefaultOptions_ReturningRestoresTheRacesOwnOutfit([Values(Order.DressThenChangeRace, Order.BuildThenChangeRace)] Order order)
        {
            DynamicCharacterAvatar avatar = CreateAvatar(DefaultOptions);
            yield return BuildDressedCatfolk(avatar);

            yield return SwitchRace(avatar, toCatfolk: false, order);
            Assert.That(avatar.activeRace.name, Is.EqualTo(HumanRace));
            Assert.That(WearsTail(avatar), Is.False, "Without Keep Wardrobe the tail is not carried to the human.");

            // Back to the catfolk with ChangeRace alone: nothing is put on by the script.
            Assert.That(avatar.ChangeRace(CatfolkRace), Is.True, "ChangeRace(" + CatfolkRace + ") was refused.");
            avatar.BuildCharacter();
            yield return WaitForBuild(avatar, "the return to " + CatfolkRace);
            AssertOutfit(avatar, CatfolkRace, catEars, wearsTail: true, "the catfolk restored from its cache");
        }

        // The cache must still bring back what a race wore when the avatar left it: with Keep Wardrobe, an empty
        // slot is filled from the cached state of the race the avatar returns to.
        [UnityTest]
        public IEnumerator KeepWardrobe_ReturningRestoresTheTailIntoAnEmptySlot()
        {
            DynamicCharacterAvatar avatar = CreateAvatar(KeepWardrobe);
            yield return BuildDressedCatfolk(avatar);

            yield return SwitchRace(avatar, toCatfolk: false, Order.ChangeRaceThenDress);
            AssertOutfit(avatar, HumanRace, humanEars, wearsTail: false, "the human");

            Assert.That(avatar.ChangeRace(CatfolkRace), Is.True, "ChangeRace(" + CatfolkRace + ") was refused.");
            avatar.BuildCharacter();
            yield return WaitForBuild(avatar, "the return to " + CatfolkRace);
            Assert.That(avatar.activeRace.name, Is.EqualTo(CatfolkRace));
            Assert.That(WearsTail(avatar), Is.True, "The catfolk's cached tail fills its empty Tail slot. " + Describe(avatar));
            Assert.That(avatar.GetWardrobeItem(catEars.wardrobeSlot), Is.SameAs(humanEars), "Keep Wardrobe keeps the ears the avatar is wearing.");
        }

        private IEnumerator SwitchRacesThreeTimes(DynamicCharacterAvatar.ChangeRaceOptions options, Order order)
        {
            DynamicCharacterAvatar avatar = CreateAvatar(options);

            // 1. The catfolk starts with its ears and its tail (applied before the first build, like a default wardrobe).
            yield return BuildDressedCatfolk(avatar);

            // 2. Human: swap the ears and take the tail off.
            yield return SwitchRace(avatar, toCatfolk: false, order);
            AssertOutfit(avatar, HumanRace, humanEars, wearsTail: false, "2. first switch to human");

            // 3. Catfolk again: cat ears, and the tail with SetWearableItem.
            yield return SwitchRace(avatar, toCatfolk: true, order);
            AssertOutfit(avatar, CatfolkRace, catEars, wearsTail: true, "3. back to catfolk");

            // 4. Human again, with exactly the same calls as step 2.
            yield return SwitchRace(avatar, toCatfolk: false, order);
            AssertOutfit(avatar, HumanRace, humanEars, wearsTail: false, "4. second switch to human");
        }

        private DynamicCharacterAvatar CreateAvatar(DynamicCharacterAvatar.ChangeRaceOptions options)
        {
            var go = new GameObject("Race wardrobe test avatar");
            created.Add(go);
            go.SetActive(false);
            var avatar = go.AddComponent<DynamicCharacterAvatar>();
            avatar.editorTimeGeneration = false;
            avatar.loadBlendShapes = false;
            avatar.cacheCurrentState = true;
            avatar.defaultChangeRaceOptions = options;
            // Only what the steps put on: no default wardrobe refills a slot behind the test's back.
            avatar.preloadWardrobeRecipes.loadDefaultRecipes = false;
            avatar.preloadWardrobeRecipes.recipes.Clear();
            avatar.activeRace.name = CatfolkRace;
            go.SetActive(true);
            return avatar;
        }

        private IEnumerator BuildDressedCatfolk(DynamicCharacterAvatar avatar)
        {
            Assert.That(avatar.SetSlot(catEars), Is.True);
            avatar.SetWearableItem(tail);
            avatar.InitialStartup();
            yield return WaitForBuild(avatar, "the catfolk's first build");
            AssertOutfit(avatar, CatfolkRace, catEars, wearsTail: true, "1. catfolk");
        }

        // What a script does when the character switches between the two races.
        private IEnumerator SwitchRace(DynamicCharacterAvatar avatar, bool toCatfolk, Order order)
        {
            string race = toCatfolk ? CatfolkRace : HumanRace;
            if (order == Order.ChangeRaceThenDress)
            {
                Assert.That(avatar.ChangeRace(race), Is.True, "ChangeRace(" + race + ") was refused.");
            }
            if (toCatfolk)
            {
                avatar.SetSlot(catEars);
                avatar.SetWearableItem(tail);
            }
            else
            {
                avatar.SetSlot(humanEars);
                avatar.ClearWearableItems(TailSlot);
            }
            if (order == Order.DressThenChangeRace)
            {
                Assert.That(avatar.ChangeRace(race), Is.True, "ChangeRace(" + race + ") was refused.");
            }
            avatar.BuildCharacter();
            if (order == Order.BuildThenChangeRace)
            {
                // Same frame: the generator has not started the build above yet. ChangeRace builds the new race itself.
                Assert.That(avatar.ChangeRace(race), Is.True, "ChangeRace(" + race + ") was refused.");
            }
            yield return WaitForBuild(avatar, "the switch to " + race);
        }

        private static IEnumerator WaitForBuild(DynamicCharacterAvatar avatar, string what)
        {
            for (int frame = 0; frame < MaxBuildFrames; frame++)
            {
                yield return null;
                if (avatar.umaData != null && avatar.umaData.umaGenerator != null && !avatar.UpdatePending())
                {
                    yield break;
                }
            }
            Assert.Fail("The avatar did not finish building after " + what + " within " + MaxBuildFrames + " frames.");
        }

        private void AssertOutfit(DynamicCharacterAvatar avatar, string race, UMAWardrobeRecipe ears, bool wearsTail, string step)
        {
            Assert.That(avatar.activeRace.name, Is.EqualTo(race), step + ": race.");
            Assert.That(avatar.GetWardrobeItem(ears.wardrobeSlot), Is.SameAs(ears), step + ": ears. " + Describe(avatar));
            Assert.That(WearsTail(avatar), Is.EqualTo(wearsTail), step + ": tail in the wardrobe. " + Describe(avatar));
            if (tailOnlySlots.Count > 0)
            {
                HashSet<string> built = BuiltSlotNames(avatar);
                Assert.That(built.Overlaps(tailOnlySlots), Is.EqualTo(wearsTail), step + ": tail in the built character (slots " + string.Join(", ", tailOnlySlots) + ").");
            }
        }

        private bool WearsTail(DynamicCharacterAvatar avatar)
        {
            return avatar.GetWardrobeItem(TailSlot) == tail || avatar.GetAppendedWearableItems(TailSlot).Contains(tail);
        }

        private static string Describe(DynamicCharacterAvatar avatar)
        {
            var text = new StringBuilder("Wardrobe:");
            foreach (KeyValuePair<string, UMATextRecipe> pair in avatar.WardrobeRecipes)
            {
                text.Append(' ').Append(pair.Key).Append('=').Append(pair.Value != null ? pair.Value.name : "null");
            }
            foreach (KeyValuePair<string, List<UMATextRecipe>> pair in avatar.AdditiveRecipes)
            {
                text.Append(' ').Append(pair.Key).Append("+=").Append(pair.Value != null ? pair.Value.Count : 0);
            }
            return text.ToString();
        }

        private static HashSet<string> BuiltSlotNames(DynamicCharacterAvatar avatar)
        {
            var names = new HashSet<string>();
            SlotData[] slots = avatar.umaData != null && avatar.umaData.umaRecipe != null ? avatar.umaData.umaRecipe.slotDataList : null;
            if (slots != null)
            {
                foreach (SlotData slot in slots)
                {
                    if (slot != null && !string.IsNullOrEmpty(slot.slotName))
                    {
                        names.Add(slot.slotName);
                    }
                }
            }
            return names;
        }

        private static IEnumerable<string> SlotNames(UMARecipeBase recipe)
        {
            var names = new List<string>();
            UMAData.UMARecipe cached = recipe != null ? recipe.GetCachedRecipe() : null;
            if (cached != null && cached.slotDataList != null)
            {
                foreach (SlotData slot in cached.slotDataList)
                {
                    if (slot != null && !string.IsNullOrEmpty(slot.slotName))
                    {
                        names.Add(slot.slotName);
                    }
                }
            }
            return names;
        }
    }
}
