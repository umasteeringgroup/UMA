using System.Collections.Generic;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The legacy content converter: the nearest-region lookup and seam smoothing on hand-made
    /// geometry, and the recipe edits on in-memory recipes and races.
    /// </summary>
    public sealed class UMALegacyContentConverterTests
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

        // --- region lookup -----------------------------------------------------------------------------

        /// <summary>A one-metre square in the XZ plane at the given offset, facing +y (or -y).</summary>
        private static void AddSquare(UMALegacyRegionSurface surface, Vector3 offset, int label, bool facingUp = true)
        {
            var positions = new[] { offset, offset + Vector3.forward, offset + Vector3.right, offset + new Vector3(1f, 0f, 1f) };
            int[] triangles = facingUp ? new[] { 0, 1, 2, 2, 1, 3 } : new[] { 0, 2, 1, 2, 3, 1 };
            surface.Add(positions, null, triangles, label);
        }

        [Test]
        public void APointTakesTheLabelOfTheNearestSurface()
        {
            var surface = new UMALegacyRegionSurface();
            AddSquare(surface, Vector3.zero, 0);
            AddSquare(surface, new Vector3(2f, 0f, 0f), 1);

            Assert.AreEqual(0, surface.Classify(new Vector3(0.5f, 0.01f, 0.5f), Vector3.up, out float d0));
            Assert.AreEqual(0.01f, d0, 1e-5f);
            Assert.AreEqual(1, surface.Classify(new Vector3(2.5f, 0.01f, 0.5f), Vector3.up, out _));
            // Between the two, a little nearer the second.
            Assert.AreEqual(1, surface.Classify(new Vector3(1.6f, 0f, 0.5f), Vector3.up, out float d1));
            Assert.AreEqual(0.4f, d1, 1e-5f);
            // Far outside the grid still finds the nearest.
            Assert.AreEqual(1, surface.Classify(new Vector3(10f, 0f, 0.5f), Vector3.up, out _));
        }

        [Test]
        public void ASurfaceFacingAwayCountsAsFurther()
        {
            var surface = new UMALegacyRegionSurface();
            AddSquare(surface, Vector3.zero, 0, facingUp: false);
            AddSquare(surface, new Vector3(0f, 0.03f, 0f), 1);
            // 1 cm above the down-facing square and 2 cm below the up-facing one: the facing one wins.
            Assert.AreEqual(1, surface.Classify(new Vector3(0.5f, 0.01f, 0.5f), Vector3.up, out _));
            surface.FacingPenalty = 0f;
            Assert.AreEqual(0, surface.Classify(new Vector3(0.5f, 0.01f, 0.5f), Vector3.up, out _));
        }

        [Test]
        public void TheGridFindsTheSameNearestDistanceAsABruteForceSearch()
        {
            var random = new System.Random(1234);
            float R() => (float)random.NextDouble();
            var positions = new Vector3[300];
            for (int i = 0; i < positions.Length; i++) positions[i] = new Vector3(R(), R(), R());
            var triangles = new int[positions.Length];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = i;
            var surface = new UMALegacyRegionSurface { FacingPenalty = 0f };
            surface.Add(positions, null, triangles, 0);

            for (int q = 0; q < 200; q++)
            {
                var p = new Vector3(R() * 1.6f - 0.3f, R() * 1.6f - 0.3f, R() * 1.6f - 0.3f);
                float brute = float.MaxValue;
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    brute = Mathf.Min(brute, (p - UMALegacyRegionSurface.ClosestPointOnTriangle(p, positions[t], positions[t + 1], positions[t + 2])).magnitude);
                }
                surface.Classify(p, Vector3.up, out float found);
                Assert.AreEqual(brute, found, 1e-5f, $"query {q} at {p}");
            }
        }

        [Test]
        public void SmoothingRemovesAStrayLabelButKeepsRegions()
        {
            // A 4 x 4 grid of quads (32 triangles): left half label 0, right half label 1, one stray 1 on the left.
            var positions = new List<Vector3>();
            for (int y = 0; y <= 4; y++)
                for (int x = 0; x <= 4; x++)
                    positions.Add(new Vector3(x * 0.1f, y * 0.1f, 0f));
            var triangles = new List<int>();
            var labels = new List<int>();
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    int a = y * 5 + x, b = a + 1, c = a + 5, d = a + 6;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                    int label = x < 2 ? 0 : 1;
                    labels.Add(label);
                    labels.Add(label);
                }
            }
            int stray = (1 * 4 + 0) * 2;   // first triangle of the quad at x=0, y=1
            labels[stray] = 1;
            var labelArrays = new List<int[]> { labels.ToArray() };
            UMALegacyRegionSurface.SmoothLabels(new List<Vector3[]> { positions.ToArray() }, new List<int[]> { triangles.ToArray() }, labelArrays, 3);

            Assert.AreEqual(0, labelArrays[0][stray], "the stray triangle joins its neighbours");
            int ones = 0;
            foreach (int label in labelArrays[0]) ones += label;
            Assert.AreEqual(16, ones, "the right half keeps its label");
        }

        // --- recipe edits ------------------------------------------------------------------------------

        private RaceData Race(string name, params string[] wardrobeSlots)
        {
            RaceData race = Track(ScriptableObject.CreateInstance<RaceData>());
            race.name = name;   // raceName follows the asset name
            Assert.AreEqual(name, race.raceName);
            race.wardrobeSlots = new List<string>(wardrobeSlots);
            return race;
        }

        private UMAWardrobeRecipe Recipe(string slot, string[] races, string[] hides)
        {
            UMAWardrobeRecipe recipe = Track(ScriptableObject.CreateInstance<UMAWardrobeRecipe>());
            recipe.name = "TestRecipe";
            recipe.wardrobeSlot = slot;
            recipe.compatibleRaces = new List<string>(races);
            recipe.Hides = new List<string>(hides);
            recipe.MeshHideAssets = new List<MeshHideAsset>();
            return recipe;
        }

        private (UMALegacyRaceMapping mapping, Dictionary<string, List<MeshHideAsset>> hides, MeshHideAsset torsoA, MeshHideAsset torsoB) Setup()
        {
            var mapping = new UMALegacyRaceMapping
            {
                legacyRace = Race("HumanMale", "Chest", "Cape"),
                newRace = Race("Human Male 3.0", "Chest"),
                legacySlots = new List<string> { "MaleTorso", "MaleFeet" },
                racesToRemove = new List<string> { "HumanMaleDCS" },
            };
            MeshHideAsset torsoA = Track(ScriptableObject.CreateInstance<MeshHideAsset>());
            MeshHideAsset torsoB = Track(ScriptableObject.CreateInstance<MeshHideAsset>());
            var hides = new Dictionary<string, List<MeshHideAsset>> { { "MaleTorso", new List<MeshHideAsset> { torsoA, torsoB } } };
            return (mapping, hides, torsoA, torsoB);
        }

        [Test]
        public void ARecipeGetsTheHidesForTheLegacySlotsItHidesTheNewRaceAndLosesTheRetiredRace()
        {
            var (mapping, hides, torsoA, torsoB) = Setup();
            UMAWardrobeRecipe recipe = Recipe("Chest", new[] { "HumanMale", "HumanMaleDCS" }, new[] { "MaleTorso", "HumanMale2_Torso" });
            var result = new UMALegacyContentConversionResult();

            Assert.IsTrue(UMALegacyContentConverter.UpdateRecipe(recipe, mapping, new UMALegacyContentConversionSettings(), hides, false, result));
            CollectionAssert.AreEqual(new[] { torsoA, torsoB }, recipe.MeshHideAssets);
            CollectionAssert.AreEqual(new[] { "HumanMale", "Human Male 3.0" }, recipe.compatibleRaces);
            Assert.AreEqual(2, result.HideReferencesAdded);
            Assert.AreEqual(1, result.RacesAdded);
            Assert.AreEqual(1, result.RacesRemoved);
            StringAssert.Contains("no mesh hide for HumanMale2_Torso", result.Lines[0]);

            // A second run changes nothing.
            Assert.IsFalse(UMALegacyContentConverter.UpdateRecipe(recipe, mapping, new UMALegacyContentConversionSettings(), hides, false, new UMALegacyContentConversionResult()));
            Assert.AreEqual(2, recipe.MeshHideAssets.Count);
        }

        [Test]
        public void APreviewChangesNothing()
        {
            var (mapping, hides, _, _) = Setup();
            UMAWardrobeRecipe recipe = Recipe("Chest", new[] { "HumanMale", "HumanMaleDCS" }, new[] { "MaleTorso" });
            var result = new UMALegacyContentConversionResult { Preview = true };

            Assert.IsTrue(UMALegacyContentConverter.UpdateRecipe(recipe, mapping, new UMALegacyContentConversionSettings(), hides, true, result));
            Assert.AreEqual(0, recipe.MeshHideAssets.Count);
            CollectionAssert.AreEqual(new[] { "HumanMale", "HumanMaleDCS" }, recipe.compatibleRaces);
            Assert.AreEqual(2, result.HideReferencesAdded);
        }

        [Test]
        public void OptionsTurnEachEditOff()
        {
            var (mapping, hides, _, _) = Setup();
            UMAWardrobeRecipe recipe = Recipe("Chest", new[] { "HumanMale", "HumanMaleDCS" }, new[] { "MaleTorso" });
            var settings = new UMALegacyContentConversionSettings { createMeshHides = false, addNewRace = false, removeRaces = false };

            Assert.IsFalse(UMALegacyContentConverter.UpdateRecipe(recipe, mapping, settings, hides, false, new UMALegacyContentConversionResult()));
            Assert.AreEqual(0, recipe.MeshHideAssets.Count);
            CollectionAssert.AreEqual(new[] { "HumanMale", "HumanMaleDCS" }, recipe.compatibleRaces);
        }

        [Test]
        public void AWardrobeSlotTheNewRaceLacksIsReported()
        {
            var (mapping, hides, _, _) = Setup();
            UMAWardrobeRecipe recipe = Recipe("Cape", new[] { "HumanMale" }, new string[0]);
            var result = new UMALegacyContentConversionResult();

            Assert.IsTrue(UMALegacyContentConverter.UpdateRecipe(recipe, mapping, new UMALegacyContentConversionSettings(), hides, false, result));
            Assert.AreEqual(1, result.Warnings.Count);
            StringAssert.Contains("'Cape'", result.Warnings[0]);
        }

        [Test]
        public void EyesMouthLashesBrowsAndHairAreNotBodySlots()
        {
            SlotDataAsset Slot(string name, params string[] tags)
            {
                SlotDataAsset slot = Track(ScriptableObject.CreateInstance<SlotDataAsset>());
                slot.name = name;   // slotName follows the asset name
                slot.tags = tags;
                return slot;
            }

            Assert.IsTrue(UMALegacyContentConverter.IsBodySlot(Slot("MaleTorso", "Torso")));
            Assert.IsTrue(UMALegacyContentConverter.IsBodySlot(Slot("M_High poly Head", "Head", "Face")));
            Assert.IsTrue(UMALegacyContentConverter.IsBodySlot(Slot("UMA30_Body_UDIM1001", "Head", "Face")));
            Assert.IsFalse(UMALegacyContentConverter.IsBodySlot(Slot("MaleEyes", "Eyes")));
            Assert.IsFalse(UMALegacyContentConverter.IsBodySlot(Slot("MaleInnerMouth", "InnerMouth", "Mouth", "Head")));
            Assert.IsFalse(UMALegacyContentConverter.IsBodySlot(Slot("UMA30_Eyelashes")));
            Assert.IsFalse(UMALegacyContentConverter.IsBodySlot(Slot("M_Eyebrow", "Eyebrows")));
        }
    }
}
