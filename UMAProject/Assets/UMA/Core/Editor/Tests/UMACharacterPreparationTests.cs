using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class UMACharacterPreparationTests
    {
        private readonly List<Object> objects = new List<Object>();

        private T Keep<T>(T value) where T : Object { objects.Add(value); return value; }

        private T Asset<T>(string name) where T : ScriptableObject
        {
            var asset = Keep(ScriptableObject.CreateInstance<T>());
            asset.name = name;
            return asset;
        }

        private RaceData MakeRace(out DNAGroup group)
        {
            var race = Asset<RaceData>("Preparation test race");
            race.useNewDNA = true;
            group = Asset<DNAGroup>("Body DNA");
            group.dnaList.Add(Asset<DNA>("height"));
            race.DNACollection = new DNACollection { DNAGroups = new List<DNAGroup> { group } };
            return race;
        }

        private DynamicCharacterAvatar MakeAvatar(RaceData race)
        {
            var root = Keep(new GameObject("Preparation test avatar"));
            root.SetActive(false);
            var avatar = root.AddComponent<DynamicCharacterAvatar>();
            avatar.activeRace.name = race.raceName;
            avatar.activeRace.data = race;
            var slot = Asset<SlotDataAsset>("Body");
            slot.meshData = new UMAMeshData { vertexCount = 1, vertices = new[] { Vector3.zero } };
            avatar.umaRecipe = new UMAData.UMARecipe
            {
                raceData = race,
                slotDataList = new[] { new SlotData(slot) },
                dnaInstanceCollection = new DNAInstanceCollection()
            };
            avatar.dnaInstanceCollection.Initialize(race.DNACollection);
            return avatar;
        }

        private static MeshModifier.Modifier Modifier(DynamicCharacterAvatar avatar, string name)
        {
            var modifier = new MeshModifier.Modifier { SlotName = "Body", DNAName = name, Scale = -1f };
            avatar.AddMeshModifier(modifier);
            return modifier;
        }

        private static void ApplyModifiers(DynamicCharacterAvatar avatar)
        {
            typeof(DynamicCharacterAvatar).GetMethod("ApplyDNAToModifiers", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(avatar, null);
        }

        [TearDown]
        public void Cleanup()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        [Test]
        public void MissingDnaPreservesValuesUsesOverridesAndTracksChangedDefinitions()
        {
            var race = MakeRace(out var group);
            var width = Asset<DNA>("width");
            width.defaultValue = 2f;
            group.dnaList.Add(width);
            race.GetDefaultDNA()["width"] = 0.35f;
            var recipe = new UMAData.UMARecipe { raceData = race, dnaInstanceCollection = new DNAInstanceCollection() };
            var height = new DNAInstance("height", 0.9f, group);
            recipe.dnaInstanceCollection.dnaInstances.Add(height);
            recipe.dnaInstanceCollection.dnaInstances.Add(new DNAInstance("obsolete", 0.2f, group));
            recipe.AddMissingDNAForRace();
            Assert.That(recipe.dnaInstanceCollection.dnaInstances, Has.Count.EqualTo(2));
            Assert.That(height.Value, Is.EqualTo(0.9f));
            Assert.That(recipe.dnaInstanceCollection.dnaInstances[1].Value, Is.EqualTo(0.35f));
            Assert.That(recipe.dnaInstanceCollection.dnaInstances[1].parentGroup, Is.SameAs(group));

            group.dnaList.RemoveAt(0);
            var depth = Asset<DNA>("depth");
            depth.defaultValue = -2f;
            group.dnaList.Add(depth);
            recipe.AddMissingDNAForRace();
            Assert.That(recipe.dnaInstanceCollection.dnaInstances.Exists(x => x.Name == "height"), Is.False);
            Assert.That(recipe.dnaInstanceCollection.dnaInstances.Find(x => x.Name == "depth").Value, Is.Zero);
            Assert.That(recipe.dnaInstanceCollection.dnaInstances.Find(x => x.Name == "width").Value, Is.EqualTo(0.35f));
        }

        [Test]
        public void InitializeDnaStillResetsExistingValuesAndPrunesChangedDefinitions()
        {
            var race = MakeRace(out var group);
            race.GetDefaultDNA()["height"] = 0.25f;
            var recipe = new UMAData.UMARecipe { raceData = race, dnaInstanceCollection = new DNAInstanceCollection() };
            var height = new DNAInstance("height", 0.9f, null);
            recipe.dnaInstanceCollection.dnaInstances.Add(height);
            recipe.InitializeDNA();
            Assert.That(height.Value, Is.EqualTo(0.25f));
            Assert.That(height.parentGroup, Is.SameAs(group));
            group.dnaList.Clear();
            var width = Asset<DNA>("width");
            width.defaultValue = 2f;
            group.dnaList.Add(width);
            recipe.InitializeDNA();
            Assert.That(recipe.dnaInstanceCollection.dnaInstances, Has.Count.EqualTo(1));
            Assert.That(recipe.dnaInstanceCollection.dnaInstances[0].Name, Is.EqualTo("width"));
            Assert.That(recipe.dnaInstanceCollection.dnaInstances[0].Value, Is.EqualTo(1f));
        }

        [Test]
        public void CompleteDnaDoesNotLoadDefaultsButMissingDnaResolvesThemOncePerPass()
        {
            var race = MakeRace(out var group);
            var source = Asset<CountingDefaultRecipe>("Counting defaults");
            race.baseRaceRecipe = source;
            var recipe = new UMAData.UMARecipe { raceData = race, dnaInstanceCollection = new DNAInstanceCollection() };
            recipe.dnaInstanceCollection.dnaInstances.Add(new DNAInstance("height", 0.75f, group));
            recipe.AddMissingDNAForRace();
            Assert.That(source.Loads, Is.Zero);
            group.dnaList.Add(Asset<DNA>("width"));
            group.dnaList.Add(Asset<DNA>("depth"));
            recipe.AddMissingDNAForRace();
            Assert.That(source.Loads, Is.EqualTo(1));
            Assert.That(recipe.dnaInstanceCollection.dnaInstances, Has.Count.EqualTo(3));
            recipe.AddMissingDNAForRace();
            Assert.That(source.Loads, Is.EqualTo(1));
        }

        [Test]
        public void NewDnaModifiersMatchGetDnaForCaseDuplicatesUnknownAndDisabledValues()
        {
            var avatar = MakeAvatar(MakeRace(out var group));
            avatar.dnaInstanceCollection.dnaInstances.AddRange(new[]
            {
                null, new DNAInstance("height", 0.2f, group) { enabled = false },
                new DNAInstance("height", 0.9f, group), new DNAInstance("Height", 0.4f, null),
                new DNAInstance("unknown", 0.8f, group)
            });
            var modifiers = new List<MeshModifier.Modifier>();
            foreach (string name in new[] { "height", "Height", "HEIGHT", "unknown" })
                modifiers.Add(Modifier(avatar, name));
            var expected = avatar.GetDNA();
            avatar.Modifiers["Body"].Add(null);
            ApplyModifiers(avatar);
            foreach (var modifier in modifiers)
                Assert.That(modifier.Scale, Is.EqualTo(expected.TryGetValue(modifier.DNAName, out var setter) ? setter.Value : -1f), modifier.DNAName);
        }

        [Test]
        public void ModifiersObserveLiveValuesAndDefinitionChangesWithoutARebuild()
        {
            var avatar = MakeAvatar(MakeRace(out var group));
            var instance = new DNAInstance("height", 0.2f, group);
            avatar.dnaInstanceCollection.dnaInstances.Add(instance);
            var modifier = Modifier(avatar, "height");
            ApplyModifiers(avatar);
            Assert.That(modifier.Scale, Is.EqualTo(0.2f));
            instance.Value = 0.7f;
            ApplyModifiers(avatar);
            Assert.That(modifier.Scale, Is.EqualTo(0.7f));
            group.dnaList.Clear();
            modifier.Scale = -1f;
            ApplyModifiers(avatar);
            Assert.That(modifier.Scale, Is.EqualTo(-1f));
            group.dnaList.Add(Asset<DNA>("HEIGHT"));
            ApplyModifiers(avatar);
            Assert.That(modifier.Scale, Is.EqualTo(0.7f));
            Assert.That(avatar.dnaInstanceCollection.dnaCollection.dnaDictionary.ContainsKey("HEIGHT"), Is.True,
                "Later DNA effects must see definitions changed by recipe callbacks.");
            Assert.That(avatar.dnaInstanceCollection.dnaCollection.dnaDictionary.ContainsKey("height"), Is.False);
        }

        [Test]
        public void LegacyModifiersKeepTheLastExactNameAcrossBlocksAndReadCurrentValues()
        {
            var race = MakeRace(out _);
            race.useNewDNA = false;
            var avatar = MakeAvatar(race);
            avatar.umaRecipe.AddDna(new TestLegacyDna { DNATypeHash = 74001, names = new[] { "height" }, Values = new[] { 0.2f } });
            var last = new TestLegacyDna { DNATypeHash = 74002, names = new[] { "height", "Height", "height" }, Values = new[] { 0.3f, 0.6f, 0.8f } };
            avatar.umaRecipe.AddDna(last);
            var height = Modifier(avatar, "height");
            var capital = Modifier(avatar, "Height");
            var unknown = Modifier(avatar, "HEIGHT");
            var expected = avatar.GetDNA();
            ApplyModifiers(avatar);
            Assert.That(height.Scale, Is.EqualTo(expected["height"].Value));
            Assert.That(capital.Scale, Is.EqualTo(expected["Height"].Value));
            Assert.That(unknown.Scale, Is.EqualTo(-1f));
            last.Values[2] = 0.45f;
            ApplyModifiers(avatar);
            Assert.That(height.Scale, Is.EqualTo(0.45f));
        }

        [Test]
        public void OverlayMatchingPreservesOrderColorIdentityAndLiveChanges()
        {
            var asset = Asset<OverlayDataAsset>("Overlay");
            var first = new OverlayData(asset);
            var second = new OverlayData(asset) { colorData = first.colorData, rect = first.rect };
            var other = new OverlayData(Asset<OverlayDataAsset>("Other overlay"));
            var left = new List<OverlayData> { first, other };
            var right = new List<OverlayData> { second, other };
            Assert.That(UMAData.UMARecipe.OverlayListsMatch(left, right), Is.True);
            Assert.That(UMAData.UMARecipe.OverlayListsMatch(left, new List<OverlayData> { other, second }), Is.False);
            second.TransparentMultiplier = Color.red;
            Assert.That(UMAData.UMARecipe.OverlayListsMatch(left, right), Is.False);
            second.TransparentMultiplier = first.TransparentMultiplier;
            second.colorData = new OverlayColorData();
            Assert.That(UMAData.UMARecipe.OverlayListsMatch(left, right), Is.False);
        }

        [Test]
        public void OverlayMatchingPreservesEmptyAndAsymmetricInvalidEntryBehavior()
        {
            var valid = new OverlayData(Asset<OverlayDataAsset>("Overlay"));
            var empty = new List<OverlayData>();
            Assert.That(UMAData.UMARecipe.OverlayListsMatch(null, empty), Is.False);
            Assert.That(UMAData.UMARecipe.OverlayListsMatch(empty, empty), Is.False);
            Assert.That(UMAData.UMARecipe.OverlayListsMatch(new List<OverlayData> { null }, new List<OverlayData> { valid }), Is.True);
            Assert.That(UMAData.UMARecipe.OverlayListsMatch(new List<OverlayData> { valid }, new List<OverlayData> { null }), Is.False);
            Assert.That(UMAData.UMARecipe.OverlayListsMatch(new List<OverlayData> { valid }, empty), Is.False);
        }

        public sealed class CountingDefaultRecipe : UMATextRecipe
        {
            public int Loads;
            public override UMAPackRecipe PackedLoad() { Loads++; return null; }
        }

        [Serializable]
        public sealed class TestLegacyDna : UMADnaBase
        {
            public string[] names;
            public override string[] Names => names;
            public override int Count => names.Length;
            public override float[] Values { get; set; }
            public override int DNATypeHash { get; set; }
        }
    }
}
