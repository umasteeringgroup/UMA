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

        private static string Signature(DynamicCharacterAvatar avatar) => avatar.RacePreset + "/" +
            JsonUtility.ToJson(avatar.predefinedDNA) + "/" + JsonUtility.ToJson(avatar.characterColors) + "/" +
            string.Join(",", avatar.WardrobeRecipes.Select(pair => pair.Key + ":" + pair.Value.name)) + "/" +
            string.Join(",", avatar.AdditiveRecipes.Select(pair => pair.Key + ":" + string.Join("+", pair.Value.Select(recipe => recipe.name))));
    }
}
#endif
