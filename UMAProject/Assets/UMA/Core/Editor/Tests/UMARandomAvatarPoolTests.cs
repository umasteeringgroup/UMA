#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace UMA.Tests
{
    public class UMARandomAvatarPoolTests
    {
        private readonly List<Object> objects = new List<Object>();
        private Random.State randomState;
        private UMARandomAvatar controller;
        private RandomAvatar definition;
        private UMAWardrobeRecipe shirt, accessory;

        private T Keep<T>(T value) where T : Object { objects.Add(value); return value; }
        private GameObject InactiveObject(string name)
        {
            var go = Keep(new GameObject(name)); go.SetActive(false); return go;
        }
        private DynamicCharacterAvatar Avatar() => InactiveObject("Pool test avatar").AddComponent<DynamicCharacterAvatar>();
        private UMATextRecipe EmptyRecipe()
        {
            var recipe = Keep(ScriptableObject.CreateInstance<UMATextRecipe>());
            recipe.PackedSave(new UMAPackedRecipeBase.UMAPackRecipe { fColors = Array.Empty<UMAPackedRecipeBase.PackedOverlayColorDataV3>() });
            return recipe;
        }

        [SetUp]
        public void Setup()
        {
            randomState = Random.state;
            Random.InitState(719);
            controller = InactiveObject("Pool test controller").AddComponent<UMARandomAvatar>();
            var race = Keep(ScriptableObject.CreateInstance<RaceData>());
            race.name = "Pool test race";
            race.baseRaceRecipe = EmptyRecipe();
            definition = new RandomAvatar(race);
            definition.RandomDna.Add(new RandomDNA("height") { MinValue = .1f, MaxValue = .9f });
            var randomizer = Keep(ScriptableObject.CreateInstance<UMARandomizer>());
            randomizer.RandomAvatars.Add(definition);
            controller.Randomizers = new List<UMARandomizer> { randomizer };
            shirt = Keep(ScriptableObject.CreateInstance<UMAWardrobeRecipe>());
            shirt.name = "Pool shirt"; shirt.wardrobeSlot = "Chest";
            // RandomWardrobeSlot's editor constructor reads the serialized color list.
            shirt.recipeString = ((UMATextRecipe)race.baseRaceRecipe).recipeString;
            accessory = Keep(ScriptableObject.CreateInstance<UMAWardrobeRecipe>());
            accessory.name = "Pool accessory"; accessory.wardrobeSlot = "Accessories";
            accessory.Appended = true; accessory.recipeString = shirt.recipeString;
            definition.RandomWardrobeSlots.Add(new RandomWardrobeSlot(shirt, "Chest"));
            definition.RandomWardrobeSlots.Add(new RandomWardrobeSlot(accessory, "Accessories"));
            var table = Keep(ScriptableObject.CreateInstance<SharedColorTable>());
            table.colors = new[] { new OverlayColorData(3) { name = "Skin", color = Color.red }, new OverlayColorData(3) { name = "Skin", color = Color.blue } };
            foreach (var color in table.colors) color.SetColorProperty("_SkinTint", color.color);
            definition.SharedColors.Add(new RandomColors("Skin", table));
        }

        [TearDown]
        public void Cleanup()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear(); Random.state = randomState;
        }

        [TestCase(0)] [TestCase(-1)]
        public void UnlimitedRandomizesEveryCharacterWithoutSavingSetups(int limit)
        {
            Assert.That(controller.MaximumUniqueCharacters, Is.Zero);
            controller.MaximumUniqueCharacters = limit;
            var heights = new HashSet<float>();
            for (int i = 0; i < 12; i++) { var avatar = Avatar(); controller.Randomize(avatar); heights.Add(avatar.predefinedDNA.GetValue("height")); }
            Assert.That(heights.Count, Is.EqualTo(12));
            Assert.That(controller.UniqueCharacterSetupCount, Is.Zero);
        }

        [TestCase(1)] [TestCase(3)]
        public void RemainingCharactersUseOnlyTheFirstSetups(int limit)
        {
            controller.MaximumUniqueCharacters = limit;
            var signatures = new HashSet<string>();
            for (int i = 0; i < limit; i++) { var avatar = Avatar(); controller.Randomize(avatar); signatures.Add(Signature(avatar)); }
            var selected = new HashSet<string>();
            for (int i = 0; i < 30; i++)
            {
                var avatar = Avatar(); controller.Randomize(avatar);
                Assert.That(signatures.Contains(Signature(avatar)), Is.True);
                selected.Add(Signature(avatar));
            }
            Assert.That(controller.UniqueCharacterSetupCount, Is.EqualTo(limit));
            Assert.That(selected.Count, Is.EqualTo(limit), "Selection should reach all saved setups, not always use the last one.");
        }

        [Test]
        public void SnapshotSurvivesConsumedDnaEditsAndDestructionOfTheOriginalAvatar()
        {
            controller.MaximumUniqueCharacters = 1;
            var first = Avatar(); controller.Randomize(first);
            string expected = Signature(first);
            first.predefinedDNA.Clear(); // This also happens during a normal DCA build.
            first.characterColors.Colors[0].color = Color.green;
            first.characterColors.Colors[0].GetProperty<UMAColorProperty>("_SkinTint").Value = Color.green;
            first.AdditiveRecipes["Accessories"].Clear();
            first.WardrobeRecipes.Clear();
            Object.DestroyImmediate(first.gameObject);
            definition.RandomDna[0].MinValue = definition.RandomDna[0].MaxValue = .99f;
            var second = Avatar(); controller.Randomize(second);
            Assert.That(Signature(second), Is.EqualTo(expected));
            second.predefinedDNA.AddDNA("height", .98f);
            second.characterColors.Colors[0].color = Color.green;
            second.AdditiveRecipes["Accessories"].Clear();
            var third = Avatar(); controller.Randomize(third);
            Assert.That(Signature(third), Is.EqualTo(expected));
            Assert.That(third.characterColors.Colors[0], Is.Not.SameAs(second.characterColors.Colors[0]));
        }

        [Test]
        public void LimitChangesAndExplicitClearingAffectFutureCharactersOnly()
        {
            controller.MaximumUniqueCharacters = 3;
            var first = Avatar(); controller.Randomize(first);
            string original = Signature(first);
            controller.Randomize(Avatar()); controller.Randomize(Avatar());
            controller.MaximumUniqueCharacters = 1;
            var copy = Avatar(); controller.Randomize(copy);
            Assert.That(Signature(copy), Is.EqualTo(original));
            Assert.That(controller.UniqueCharacterSetupCount, Is.EqualTo(1));
            controller.ClearCharacterSetupPool();
            Assert.That(controller.UniqueCharacterSetupCount, Is.Zero);
            Assert.That(Signature(first), Is.EqualTo(original));
            controller.Randomize(Avatar());
            controller.MaximumUniqueCharacters = 0;
            controller.Randomize(Avatar());
            Assert.That(controller.UniqueCharacterSetupCount, Is.Zero);
        }

        [Test]
        public void RepeatInitialSequenceRecreatesTheSamePoolAndSelections()
        {
            controller.GenerateGrid = true; controller.GridXSize = controller.GridZSize = 0;
            controller.MaximumUniqueCharacters = 3;
            // Capture/restore the sequence through the public generation entry point;
            // the empty grid avoids scheduling live UMA builds in this unit test.
            controller.GenerateCharacters(false);
            string[] Capture() => Enumerable.Range(0, 12).Select(_ => { var avatar = Avatar(); controller.Randomize(avatar); return Signature(avatar); }).ToArray();
            string[] first = Capture();
            controller.GenerateCharacters(true);
            CollectionAssert.AreEqual(first, Capture());
            controller.DestroyGeneratedCharacters();
            Assert.That(controller.UniqueCharacterSetupCount, Is.Zero);
        }

        [Test]
        public void CharacterOnlyPreservesUnselectedWardrobeAndDoesNotUsePool()
        {
            controller.MaximumUniqueCharacters = 1;
            controller.Randomize(Avatar());
            var avatar = Avatar();
            avatar.WardrobeRecipes["Feet"] = shirt;
            var collection = Keep(ScriptableObject.CreateInstance<UMAWardrobeCollection>());
            avatar.WardrobeCollections["Existing"] = collection;
            controller.Randomize(avatar, true, false);
            Assert.That(avatar.WardrobeRecipes["Feet"], Is.SameAs(shirt));
            Assert.That(avatar.WardrobeCollections["Existing"], Is.SameAs(collection));
            Assert.That(avatar.predefinedDNA.GetValue("height"), Is.InRange(.1f, .9f));
        }

        [Test]
        public void WardrobeOnlyKeepsRaceDnaAndHairAndReplacesAdditiveItems()
        {
            var avatar = Avatar();
            avatar.RacePreset = definition.RaceName;
            avatar.predefinedDNA.AddDNA("height", .42f);
            avatar.WardrobeRecipes["Hair"] = shirt;
            controller.WardrobeRandomizers = new List<UMARandomizer> { null, controller.Randomizers[0] };
            controller.Randomizers = new List<UMARandomizer>();
            controller.Randomize(avatar, false, true);
            controller.Randomize(avatar, false, true);
            Assert.That(avatar.RacePreset, Is.EqualTo(definition.RaceName));
            Assert.That(avatar.predefinedDNA.GetValue("height"), Is.EqualTo(.42f));
            Assert.That(avatar.WardrobeRecipes["Hair"], Is.SameAs(shirt));
            Assert.That(avatar.AdditiveRecipes["Accessories"], Has.Count.EqualTo(1));
        }

        [Test]
        public void MissingMatchingRaceLeavesExistingAppearanceIntact()
        {
            var avatar = Avatar();
            avatar.RacePreset = "Unconfigured race";
            avatar.WardrobeRecipes["Chest"] = shirt;
            avatar.predefinedDNA.AddDNA("height", .42f);
            string before = Signature(avatar);
            controller.KeepExistingRace = true;
            controller.MaximumUniqueCharacters = 1;
            controller.Randomize(avatar);
            Assert.That(Signature(avatar), Is.EqualTo(before));
            Assert.That(controller.UniqueCharacterSetupCount, Is.Zero);
        }

        [Test]
        public void GlobalColorsRespectToggleAndLocalColorsOverrideThem()
        {
            var source = controller.Randomizers[0];
            definition.SharedColors.Clear();
            var table = Keep(ScriptableObject.CreateInstance<SharedColorTable>());
            table.colors = new[] { new OverlayColorData(3) { color = Color.green } };
            source.Global.SharedColors.Add(new RandomColors("Skin", table));
            source.useGlobalColors = false;
            var avatar = Avatar();
            controller.Randomize(avatar);
            Assert.That(avatar.characterColors.Colors, Is.Empty);
            source.useGlobalColors = true;
            controller.Randomize(avatar);
            Assert.That(avatar.characterColors.Colors.Single().color, Is.EqualTo(Color.green));
            var local = Keep(ScriptableObject.CreateInstance<SharedColorTable>());
            local.colors = new[] { new OverlayColorData(3) { color = Color.blue } };
            definition.SharedColors.Add(new RandomColors("Skin", local));
            controller.Randomize(avatar);
            Assert.That(avatar.characterColors.Colors.Single().color, Is.EqualTo(Color.blue));
        }

        [Test]
        public void NullEntriesEmptyTablesAndNoneWardrobeAreHandled()
        {
            var avatar = Avatar();
            avatar.RacePreset = definition.RaceName;
            avatar.WardrobeRecipes["Chest"] = shirt;
            controller.Randomizers.Insert(0, null);
            definition.RandomWardrobeSlots.Clear();
            definition.RandomWardrobeSlots.Add(null);
            definition.RandomWardrobeSlots.Add(new RandomWardrobeSlot(null, "Chest"));
            definition.RandomDna.Add(null);
            definition.SharedColors.Add(null);
            definition.SharedColors.Add(new RandomColors("Empty", Keep(ScriptableObject.CreateInstance<SharedColorTable>())));
            Assert.DoesNotThrow(() => controller.Randomize(avatar, false, true));
            Assert.That(avatar.WardrobeRecipes.ContainsKey("Chest"), Is.False);
            Assert.That(controller.GetRandomWardrobe(null), Is.Null);
            Assert.That(controller.GetRandomWardrobe(new List<RandomWardrobeSlot>()), Is.Null);
            Assert.That(controller.GetRandomWardrobe(new List<RandomWardrobeSlot> { new RandomWardrobeSlot(null, "Chest") { Chance = 0 } }), Is.Null);
        }

        [Test]
        public void WeightedSelectionDoesNotBiasTheLastCandidate()
        {
            var source = controller.Randomizers[0];
            var definitions = new[] { definition, new RandomAvatar(definition.raceData), new RandomAvatar(definition.raceData) };
            source.RandomAvatars = definitions.ToList();
            var slots = Enumerable.Range(0, 3).Select(_ => new RandomWardrobeSlot(null, "Chest")).ToList();
            int[] races = new int[3], wardrobe = new int[3];
            for (int i = 0; i < 9000; i++)
            {
                races[Array.IndexOf(definitions, source.GetRandomAvatar(definition.RaceName))]++;
                wardrobe[slots.IndexOf(controller.GetRandomWardrobe(slots))]++;
            }
            foreach (int count in races.Concat(wardrobe)) Assert.That(count, Is.InRange(2700, 3300));
            definitions[0].Chance = definitions[1].Chance = 0;
            for (int i = 0; i < 20; i++) Assert.That(source.GetRandomAvatar(), Is.SameAs(definitions[2]));
            Assert.That(source.GetRandomAvatar("Missing race"), Is.Null);
        }

        [Test]
        public void FullRerollClearsOldCollectionsAndAdditiveRecipes()
        {
            var avatar = Avatar();
            avatar.WardrobeCollections["Old"] = Keep(ScriptableObject.CreateInstance<UMAWardrobeCollection>());
            avatar.AdditiveRecipes["Old"] = new List<UMATextRecipe> { accessory };
            controller.Randomize(avatar);
            Assert.That(avatar.WardrobeCollections, Is.Empty);
            Assert.That(avatar.AdditiveRecipes.ContainsKey("Old"), Is.False);
        }

        [Test]
        public void ExistingAvatarStartupRegistersOnceAndUnsubscribesBeforeReroll()
        {
            controller.mode = UMARandomAvatar.Mode.UseExisting;
            controller.Randomizers.Clear(); // Exercise subscription lifetime without scheduling a UMA build.
            var avatar = Avatar();
            var data = avatar.gameObject.GetComponent<UMAData>() ?? avatar.gameObject.AddComponent<UMAData>();
            controller.ExistingDCAs = new List<DynamicCharacterAvatar> { avatar, null, avatar };
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var initialize = typeof(UMARandomAvatar).GetMethod("InitializeExistingAvatars", flags);
            var waiting = (HashSet<DynamicCharacterAvatar>)typeof(UMARandomAvatar).GetField("waitingForAvatars", flags).GetValue(controller);
            var initialized = (HashSet<DynamicCharacterAvatar>)typeof(UMARandomAvatar).GetField("initializedExistingAvatars", flags).GetValue(controller);
            initialize.Invoke(controller, null);
            Assert.That(waiting, Has.Count.EqualTo(1));
            avatar.CharacterCreated.Invoke(data);
            Assert.That(waiting, Is.Empty);
            Assert.That(initialized, Has.Count.EqualTo(1));
            initialize.Invoke(controller, null);
            avatar.CharacterCreated.Invoke(data);
            Assert.That(waiting, Is.Empty);
            Assert.That(initialized, Has.Count.EqualTo(1));
        }

        [Test]
        public void V2MigrationCopiesSettingsAndPreservesForwardingReference()
        {
            var legacy = InactiveObject("Legacy randomizer").AddComponent<UMARandomAvatarV2>();
            legacy.CharacterRandomizers = controller.Randomizers;
            legacy.WardrobeRandomizers = new List<UMARandomizer> { controller.Randomizers[0] };
            legacy.mode = UMARandomAvatarV2.Mode.UseExisting;
            legacy.KeepExistingRace = true;
            legacy.Generation.GridXSize = 7;
            legacy.Generation.GridRandomOffset = .75f;
            legacy.Generation.RandomAvatarGenerated = new UMARandomAvatarEvent();
            var method = typeof(UMA.Editors.UMARandomAvatarV2Editor).GetMethod("CopyToUnifiedController",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var unified = (UMARandomAvatar)method.Invoke(null, new object[] { legacy });
            Assert.That(unified.Randomizers, Is.EqualTo(legacy.CharacterRandomizers));
            Assert.That(unified.Randomizers, Is.Not.SameAs(legacy.CharacterRandomizers));
            Assert.That(unified.mode, Is.EqualTo(UMARandomAvatar.Mode.UseExisting));
            Assert.That(unified.KeepExistingRace, Is.True);
            Assert.That(unified.GridXSize, Is.EqualTo(7));
            Assert.That(unified.RandomOffset, Is.EqualTo(.75f));
            Assert.That(unified.RandomAvatarGenerated, Is.SameAs(legacy.Generation.RandomAvatarGenerated));
            Assert.That(legacy.enabled, Is.False);
            Assert.That(legacy.UnifiedController, Is.SameAs(unified));
            legacy.ToggleKeepExistingWardrobe(true);
            Assert.That(unified.KeepExistingWardrobe, Is.True);
        }

        [TestCase(false, true, false, false)]
        [TestCase(false, false, true, false)]
        [TestCase(false, false, false, true)]
        public void SelectiveRandomizationPreservesUncheckedCategories(bool race, bool dna, bool clothing, bool colors)
        {
            var avatar = Avatar();
            avatar.RacePreset = definition.RaceName;
            avatar.activeRace.data = definition.raceData;
            avatar.predefinedDNA.AddDNA("height", .99f);
            avatar.WardrobeRecipes["Feet"] = shirt;
            avatar.characterColors.SetRawColor("Skin", new OverlayColorData(3) { color = Color.green });
            string dnaBefore = JsonUtility.ToJson(avatar.predefinedDNA);
            string colorsBefore = JsonUtility.ToJson(avatar.characterColors);
            Assert.That(controller.RandomizeSelective(avatar, race, dna, clothing, colors), Is.True);
            Assert.That(avatar.RacePreset, Is.EqualTo(definition.RaceName));
            if (!dna) Assert.That(JsonUtility.ToJson(avatar.predefinedDNA), Is.EqualTo(dnaBefore));
            else Assert.That(avatar.predefinedDNA.GetValue("height"), Is.InRange(.1f, .9f));
            if (!colors) Assert.That(JsonUtility.ToJson(avatar.characterColors), Is.EqualTo(colorsBefore));
            else Assert.That(avatar.GetColor("Skin").color, Is.Not.EqualTo(Color.green));
            if (!clothing) Assert.That(avatar.WardrobeRecipes["Feet"], Is.SameAs(shirt));
            else Assert.That(avatar.WardrobeRecipes.ContainsKey("Feet"), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SelectiveNewDNAUpdatesTheLiveCollection(bool hasLegacyDNA)
        {
            var avatar = Avatar();
            var race = definition.raceData;
            var group = Keep(ScriptableObject.CreateInstance<DNAGroup>());
            var height = Keep(ScriptableObject.CreateInstance<DNA>());
            height.name = "height";
            group.dnaList.Add(height);
            race.useNewDNA = true;
            race.DNACollection = new DNACollection();
            race.DNACollection.DNAGroups.Add(group);
            avatar.activeRace.name = race.raceName;
            avatar.activeRace.data = race;
            avatar.umaRecipe = new UMAData.UMARecipe { raceData = race };
            avatar.dnaInstanceCollection = new DNAInstanceCollection();
            avatar.dnaInstanceCollection.Initialize(race.DNACollection);
            avatar.dnaInstanceCollection.dnaInstances.Add(new DNAInstance("height", .99f, group));
            if (hasLegacyDNA) avatar.predefinedDNA.AddDNA("height", .99f);
            Assert.That(controller.RandomizeSelective(avatar, false, true, false, false), Is.True);
            Assert.That(avatar.GetDNA()["height"].Value, Is.InRange(.1f, .9f));
            Assert.That(avatar.predefinedDNA == null || avatar.predefinedDNA.Count == 0, Is.True,
                "UMA 3 randomization must not retain a second copy in legacy predefined DNA.");
        }

        [Test]
        public void RandomizedWardrobePersistsRegularAndAdditiveDefaults()
        {
            var avatar = Avatar();
            avatar.RacePreset = definition.RaceName;
            avatar.activeRace.data = definition.raceData;
            controller.RandomizeSelective(avatar, false, false, true, false);
            var save = typeof(UMA.CharacterSystem.Editors.DynamicCharacterAvatarEditor).GetMethod("SaveRandomizedDefaultWardrobe",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            save.Invoke(null, new object[] { avatar });
            Assert.That(avatar.preloadWardrobeRecipes.loadDefaultRecipes, Is.True);
            CollectionAssert.AreEquivalent(new[] { shirt.name, accessory.name },
                avatar.preloadWardrobeRecipes.recipes.Select(item => item._recipeName));
            avatar.ClearSlots();
            Assert.That(avatar.preloadWardrobeRecipes.recipes, Has.Count.EqualTo(2), "Preview cleanup must not discard the saved outfit.");
        }

        private static string Signature(DynamicCharacterAvatar avatar) => avatar.RacePreset + "/" +
            JsonUtility.ToJson(avatar.predefinedDNA) + "/" + JsonUtility.ToJson(avatar.characterColors) + "/" +
            string.Join(",", avatar.WardrobeRecipes.Select(pair => pair.Key + ":" + pair.Value.name)) + "/" +
            string.Join(",", avatar.AdditiveRecipes.Select(pair => pair.Key + ":" + string.Join("+", pair.Value.Select(recipe => recipe.name))));
    }
}
#endif
