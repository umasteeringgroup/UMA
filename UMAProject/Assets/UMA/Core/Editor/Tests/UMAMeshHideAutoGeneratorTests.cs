using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Automatic Mesh Hide Assets: the occlusion test on small hand-made geometry, and the whole
    /// generator on HumanMale30 wearing a copy of the UMA 2 Bard jacket recipe.
    /// </summary>
    public sealed class UMAMeshHideAutoGeneratorTests
    {
        private const string RacePath = "Assets/UMA/UMA3/Races/HumanMale30.asset";
        private const string RecipePath = "Assets/Content/UMA RPG Starter 1/Wardrobe/Bard/BardShirt.asset";
        private const string TempFolder = "Assets/UMA_MeshHideTestTemp";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        // --- the occlusion test on its own -------------------------------------------------------

        /// <summary>A one-metre square at the given height. Facing up, its surface normal is +y.</summary>
        private static UMAMeshHideOcclusion.OccluderSet Sheet(float height, bool facingUp = true)
        {
            var positions = new[]
            {
                new Vector3(0f, height, 0f), new Vector3(0f, height, 1f), new Vector3(1f, height, 0f), new Vector3(1f, height, 1f),
            };
            int[] triangles = facingUp ? new[] { 0, 1, 2, 2, 1, 3 } : new[] { 0, 2, 1, 2, 3, 1 };
            var set = new UMAMeshHideOcclusion.OccluderSet();
            set.Add(positions, triangles);
            Assert.AreEqual(2, set.TriangleCount);
            return set;
        }

        // Skin lying flat at height zero, facing up: three vertices under the sheet, three beside it.
        private static readonly Vector3[] SkinPositions =
        {
            new Vector3(0.25f, 0f, 0.25f), new Vector3(0.75f, 0f, 0.25f), new Vector3(0.25f, 0f, 0.75f),
            new Vector3(2f, 0f, 0.5f), new Vector3(2.5f, 0f, 0.5f), new Vector3(2f, 0f, 1f),
        };

        private static Vector3[] SkinNormals()
        {
            var normals = new Vector3[SkinPositions.Length];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            return normals;
        }

        private static bool[] Covered(UMAMeshHideOcclusion.OccluderSet occluders, UMAMeshHideOcclusionSettings settings = null)
        {
            return UMAMeshHideOcclusion.FindCoveredVertices(SkinPositions, SkinNormals(), occluders, settings ?? new UMAMeshHideOcclusionSettings());
        }

        [Test]
        public void ClothingLyingOverTheSkinCoversOnlyWhatIsUnderIt()
        {
            CollectionAssert.AreEqual(new[] { true, true, true, false, false, false }, Covered(Sheet(0.03f)));
        }

        [Test]
        public void ClothingFurtherAwayThanTheOutwardDistanceDoesNotCount()
        {
            CollectionAssert.AreEqual(new bool[6], Covered(Sheet(0.2f)), "0.2 m is beyond the default 0.1 m");
            CollectionAssert.AreEqual(new[] { true, true, true, false, false, false },
                Covered(Sheet(0.2f), new UMAMeshHideOcclusionSettings { outwardDistance = 0.3f }));
        }

        [Test]
        public void SkinPokingThroughClothingIsFoundByTheInwardRay()
        {
            CollectionAssert.AreEqual(new[] { true, true, true, false, false, false }, Covered(Sheet(-0.01f)), "1 cm under the skin");
            CollectionAssert.AreEqual(new bool[6], Covered(Sheet(-0.05f)), "5 cm under the skin is beyond the default 2 cm");
        }

        [Test]
        public void ASurfaceFacingTheSkinIsNotClothingLyingOverIt()
        {
            // The sheet faces down, toward the skin: like the side of a vest facing the inside of a bare arm.
            CollectionAssert.AreEqual(new bool[6], Covered(Sheet(0.03f, facingUp: false)));
            CollectionAssert.AreEqual(new[] { true, true, true, false, false, false },
                Covered(Sheet(0.03f, facingUp: false), new UMAMeshHideOcclusionSettings { requireSameFacing = false }));
        }

        [Test]
        public void TheThreeStrategiesTurnCoveredVerticesIntoHiddenTriangles()
        {
            int[] triangles = { 0, 1, 2, 2, 1, 3, 3, 4, 5 };
            bool[] covered = { true, true, true, false, false, false };   // all three, two, none

            BitArray strict = UMAMeshHideOcclusion.FindHiddenTriangles(triangles, covered, MeshHideAsset.TriangleHideStrategy.Strict, out int strictCount);
            BitArray weighted = UMAMeshHideOcclusion.FindHiddenTriangles(triangles, covered, MeshHideAsset.TriangleHideStrategy.Weighted, out int weightedCount);
            UMAMeshHideOcclusion.FindHiddenTriangles(triangles, covered, MeshHideAsset.TriangleHideStrategy.Conservative, out int conservativeCount);

            Assert.AreEqual(1, strictCount);
            Assert.IsTrue(strict[0]);
            Assert.IsFalse(strict[1]);
            Assert.AreEqual(2, weightedCount);
            Assert.IsTrue(weighted[1]);
            Assert.AreEqual(2, conservativeCount);
            Assert.AreEqual(3, strict.Count, "one flag per triangle");

            covered[1] = false;
            covered[2] = false;   // only vertex 0 is covered now: triangle 0 has one covered vertex, the others none
            UMAMeshHideOcclusion.FindHiddenTriangles(triangles, covered, MeshHideAsset.TriangleHideStrategy.Weighted, out weightedCount);
            UMAMeshHideOcclusion.FindHiddenTriangles(triangles, covered, MeshHideAsset.TriangleHideStrategy.Conservative, out conservativeCount);
            Assert.AreEqual(0, weightedCount);
            Assert.AreEqual(1, conservativeCount);
        }

        // --- the generator on real data -----------------------------------------------------------

        private static int CountBits(BitArray[] masks)
        {
            int count = 0;
            if (masks == null) return 0;
            foreach (BitArray mask in masks)
            {
                if (mask == null) continue;
                for (int i = 0; i < mask.Count; i++)
                {
                    if (mask[i]) count++;
                }
            }
            return count;
        }

        [Test]
        public void TheJacketRecipeGetsHidesForTheBodyItCovers()
        {
            var race = AssetDatabase.LoadAssetAtPath<RaceData>(RacePath);
            Assert.IsNotNull(race, RacePath);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UMATextRecipe>(RecipePath), RecipePath);
            Assert.IsTrue(UMARigRestPose.TryFromRace(race, out UMARigRestPose rig, out string reason), reason);

            // Work on a copy, so the project's own recipe is not touched.
            AssetDatabase.CreateFolder("Assets", "UMA_MeshHideTestTemp");
            string copyPath = TempFolder + "/JacketCopy.asset";
            Assert.IsTrue(AssetDatabase.CopyAsset(RecipePath, copyPath));
            var recipe = AssetDatabase.LoadAssetAtPath<UMATextRecipe>(copyPath);
            recipe.MeshHideAssets = new List<MeshHideAsset>();
            recipe.MeshHideAssetCollections = new List<MeshHideAssetCollection>();

            var messages = new List<string>();
            List<UMAMeshHideAutoGenerator.BodySlot> body = UMAMeshHideAutoGenerator.CollectBodySlots(race, rig, messages);
            Assert.That(body.Count, Is.GreaterThanOrEqualTo(5), string.Join(" | ", messages));
            int bakedBodySlots = 0;
            foreach (UMAMeshHideAutoGenerator.BodySlot slot in body)
            {
                Assert.IsTrue(EditorUtility.IsPersistent(slot.BaseSlot), slot.BaseSlot.slotName + " must be the project's base slot");
                StringAssert.DoesNotContain("_baked_", slot.BaseSlot.slotName);
                if (slot.BuiltSlotName.Contains("_baked_")) bakedBodySlots++;
            }
            Assert.That(bakedBodySlots, Is.GreaterThan(0), "HumanMale30 builds its body from baked slots; the test relies on that");

            var settings = new UMAMeshHideOcclusionSettings();
            UMAMeshHideAutoGenerator.RecipeResult result = UMAMeshHideAutoGenerator.Generate(recipe, race, rig, body, null, settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[UMAMeshHideAutoGeneratorTests] " + result.Summary() + " | occluder triangles " + result.OccluderTriangles + " | " + string.Join(" | ", result.Messages));

            Assert.That(result.OccluderTriangles, Is.GreaterThan(1000), "the jacket's triangles");
            int bodyTriangles = 0;
            foreach (UMAMeshHideAutoGenerator.SlotResult slot in result.Slots) bodyTriangles += slot.TotalTriangles;
            Assert.That(result.HiddenTriangles, Is.InRange(bodyTriangles / 50, bodyTriangles * 4 / 5), "a jacket covers part of the body, not none of it and not nearly all");

            var created = new List<MeshHideAsset>();
            foreach (UMAMeshHideAutoGenerator.SlotResult slot in result.Slots)
            {
                if (slot.SlotName.Contains("Eye")) Assert.AreEqual(0, slot.HiddenTriangles, "a jacket does not cover the eyes");
                if (slot.HiddenTriangles == 0)
                {
                    Assert.IsNull(slot.Asset, slot.SlotName + ": no asset for a slot with nothing to hide");
                    continue;
                }
                Assert.IsNotNull(slot.Asset, slot.SlotName);
                StringAssert.StartsWith(TempFolder + "/" + UMAMeshHideAutoGenerator.FolderName + "/", AssetDatabase.GetAssetPath(slot.Asset));
                Assert.AreEqual(slot.SlotName, slot.Asset.AssetSlotName, "the hide names the base slot");
                StringAssert.DoesNotContain("_baked_", slot.Asset.AssetSlotName);
                Assert.AreEqual(slot.HiddenTriangles, slot.Asset.HiddenCount);
                CollectionAssert.Contains(recipe.MeshHideAssets, slot.Asset, "the hide is on the recipe");
                created.Add(slot.Asset);
            }
            Assert.That(created.Count, Is.GreaterThan(0));
            Assert.AreEqual(created.Count, recipe.MeshHideAssets.Count);

            // A HumanMale30 character is built from baked body slots. The avatar's own pass must give
            // those slots the masks, although the hides name the base slots.
            var avatarObject = new GameObject("MeshHideTestAvatar") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var avatar = avatarObject.AddComponent<DynamicCharacterAvatar>();
                avatar.editorTimeGeneration = false;
                avatar.showPlaceholder = false;
                avatar.activeRace.name = race.raceName;
                avatar.activeRace.data = race;
                avatar.predefinedDNA = new UMAPredefinedDNA();
                avatar.GenerateNow();
                avatar.enabled = false;
                UMAData.UMARecipe built = avatar.umaData.umaRecipe;
                var dictionary = new Dictionary<string, List<MeshHideAsset>>();
                MethodInfo process = typeof(DynamicCharacterAvatar).GetMethod("ProcessMeshHides", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(process, "DynamicCharacterAvatar.ProcessMeshHides");
                process.Invoke(null, new object[] { dictionary, new List<UMATextRecipe> { recipe }, built });
                Assert.AreEqual(created.Count, dictionary.Count, "every hide found its slot in the character");
                built.MeshHideDictionary = dictionary;
                built.UpdateMeshHideMasks();
                int masked = 0;
                foreach (SlotData slot in built.slotDataList)
                {
                    if (slot == null || slot.meshHideMask == null) continue;
                    StringAssert.Contains("_baked_", slot.slotName, "the masked slots are the baked copies");
                    masked += CountBits(slot.meshHideMask);
                }
                Assert.AreEqual(result.HiddenTriangles, masked, "the character hides exactly the triangles the generator chose");
                try { avatar.CleanupGeneratedData(); } catch { }
            }
            finally
            {
                Object.DestroyImmediate(avatarObject);
            }

            // Running again updates the same assets and does not add them twice.
            var guids = new List<string>();
            foreach (MeshHideAsset asset in created) guids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)));
            settings.strategy = MeshHideAsset.TriangleHideStrategy.Conservative;
            UMAMeshHideAutoGenerator.RecipeResult conservative = UMAMeshHideAutoGenerator.Generate(recipe, race, rig, body, null, settings);
            AssetDatabase.SaveAssets();
            Assert.That(conservative.HiddenTriangles, Is.GreaterThan(result.HiddenTriangles), "Conservative hides more than Strict");
            foreach (MeshHideAsset asset in created)
            {
                CollectionAssert.Contains(guids, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)), "the asset kept its identity");
            }
            var unique = new HashSet<MeshHideAsset>(recipe.MeshHideAssets);
            Assert.AreEqual(unique.Count, recipe.MeshHideAssets.Count, "no hide is on the recipe twice");
            Assert.That(recipe.MeshHideAssets.Count, Is.GreaterThanOrEqualTo(created.Count));
        }

        // --- painted corrections and locks ---------------------------------------------------------

        [Test]
        public void PaintedMarksAreAppliedOverComputedFlags()
        {
            var hide = ScriptableObject.CreateInstance<MeshHideAsset>();
            try
            {
                hide.SaveSelectionForLOD(new[] { new BitArray(4), new BitArray(2) }, 0);
                Assert.IsTrue(hide.SetAutoCorrection(0, 3, true));
                Assert.IsTrue(hide.SetAutoCorrection(1, 1, true));
                Assert.IsTrue(hide.triangleFlags[0][3]);
                Assert.IsTrue(hide.triangleFlags[1][1]);
                Assert.AreEqual(2, hide.autoForceHidden.Count);
                Assert.IsFalse(hide.SetAutoCorrection(2, 0, true), "no such submesh");
                Assert.IsFalse(hide.SetAutoCorrection(0, 4, true), "no such triangle");

                // Painting the other way moves the mark from one list to the other.
                Assert.IsTrue(hide.SetAutoCorrection(1, 1, false));
                Assert.IsFalse(hide.triangleFlags[1][1]);
                Assert.AreEqual(1, hide.autoForceHidden.Count);
                Assert.AreEqual(1, hide.autoKeepVisible.Count);
                hide.SetAutoCorrection(1, 1, false);
                Assert.AreEqual(1, hide.autoKeepVisible.Count, "a mark is stored once");

                MeshHideAsset.UnpackTriangle(MeshHideAsset.PackTriangle(3, 123456), out int submesh, out int triangle);
                Assert.AreEqual(3, submesh);
                Assert.AreEqual(123456, triangle);

                // What a generator pass computes: everything hidden in submesh 1, nothing in submesh 0.
                var computed = new[] { new BitArray(4), new BitArray(2, true) };
                hide.ApplyAutoCorrections(computed);
                Assert.IsTrue(computed[0][3], "always hide");
                Assert.IsFalse(computed[0][0]);
                Assert.IsFalse(computed[1][1], "never hide");
                Assert.IsTrue(computed[1][0]);

                // Marks that no longer fit the mesh are passed over.
                hide.ApplyAutoCorrections(new[] { new BitArray(2) });
            }
            finally
            {
                Object.DestroyImmediate(hide);
            }
        }

        /// <summary>A copy of the jacket recipe in the temp folder, with hides generated for it.</summary>
        private static UMATextRecipe MakeRecipeWithHides(out RaceData race, out UMARigRestPose rig, out List<UMAMeshHideAutoGenerator.BodySlot> body,
            out UMAMeshHideAutoGenerator.RecipeResult result)
        {
            race = AssetDatabase.LoadAssetAtPath<RaceData>(RacePath);
            Assert.IsNotNull(race, RacePath);
            Assert.IsTrue(UMARigRestPose.TryFromRace(race, out rig, out string reason), reason);
            AssetDatabase.CreateFolder("Assets", "UMA_MeshHideTestTemp");
            string copyPath = TempFolder + "/JacketCopy.asset";
            Assert.IsTrue(AssetDatabase.CopyAsset(RecipePath, copyPath));
            var recipe = AssetDatabase.LoadAssetAtPath<UMATextRecipe>(copyPath);
            recipe.MeshHideAssets = new List<MeshHideAsset>();
            recipe.MeshHideAssetCollections = new List<MeshHideAssetCollection>();
            body = UMAMeshHideAutoGenerator.CollectBodySlots(race, rig);
            result = UMAMeshHideAutoGenerator.Generate(recipe, race, rig, body, null, new UMAMeshHideOcclusionSettings());
            AssetDatabase.SaveAssets();
            Assert.That(result.HiddenTriangles, Is.GreaterThan(100));
            return recipe;
        }

        private static bool[][] Snapshot(BitArray[] flags)
        {
            var copy = new bool[flags.Length][];
            for (int s = 0; s < flags.Length; s++)
            {
                copy[s] = new bool[flags[s].Count];
                flags[s].CopyTo(copy[s], 0);
            }
            return copy;
        }

        [Test]
        public void PaintedMarksSurviveRegenerationAndALockedHideIsLeftAlone()
        {
            UMATextRecipe recipe = MakeRecipeWithHides(out RaceData race, out UMARigRestPose rig, out List<UMAMeshHideAutoGenerator.BodySlot> body,
                out UMAMeshHideAutoGenerator.RecipeResult first);

            // The hide with the most hidden triangles, and in it one hidden and one visible triangle.
            UMAMeshHideAutoGenerator.SlotResult chosen = null;
            foreach (UMAMeshHideAutoGenerator.SlotResult slot in first.Slots)
            {
                if (slot.Asset != null && (chosen == null || slot.HiddenTriangles > chosen.HiddenTriangles)) chosen = slot;
            }
            Assert.IsNotNull(chosen);
            MeshHideAsset hide = chosen.Asset;
            BitArray flags = hide.triangleFlags[0];
            int hiddenTriangle = -1, visibleTriangle = -1;
            for (int t = 0; t < flags.Count && (hiddenTriangle < 0 || visibleTriangle < 0); t++)
            {
                if (flags[t] && hiddenTriangle < 0) hiddenTriangle = t;
                if (!flags[t] && visibleTriangle < 0) visibleTriangle = t;
            }
            Assert.That(hiddenTriangle, Is.GreaterThanOrEqualTo(0));
            Assert.That(visibleTriangle, Is.GreaterThanOrEqualTo(0));

            // Paint the hidden one visible and the visible one hidden, then generate again.
            Assert.IsTrue(hide.SetAutoCorrection(0, hiddenTriangle, false));
            Assert.IsTrue(hide.SetAutoCorrection(0, visibleTriangle, true));
            UMAMeshHideAutoGenerator.RecipeResult second = UMAMeshHideAutoGenerator.Generate(recipe, race, rig, body, null, new UMAMeshHideOcclusionSettings());
            UMAMeshHideAutoGenerator.SlotResult again = second.Slots.Find(s => s.SlotName == chosen.SlotName);
            Assert.AreSame(hide, again.Asset);
            Assert.AreEqual(2, again.Corrections);
            Assert.IsFalse(hide.triangleFlags[0][hiddenTriangle], "the generator would hide it; the never-hide mark wins");
            Assert.IsTrue(hide.triangleFlags[0][visibleTriangle], "the generator would show it; the always-hide mark wins");
            Assert.AreEqual(chosen.HiddenTriangles, again.HiddenTriangles, "one fewer and one more");
            Assert.AreEqual(again.HiddenTriangles, hide.HiddenCount);
            StringAssert.Contains("2 painted", second.Summary());
            Assert.IsFalse(EditorUtility.IsDirty(hide), "a regenerated hide is saved at once");
            Assert.AreEqual(chosen.SlotName, hide.AssetSlotName);

            // A strategy that hides more still leaves the never-hide triangle alone.
            var conservative = new UMAMeshHideOcclusionSettings { strategy = MeshHideAsset.TriangleHideStrategy.Conservative };
            UMAMeshHideAutoGenerator.Generate(recipe, race, rig, body, null, conservative);
            Assert.IsFalse(hide.triangleFlags[0][hiddenTriangle]);
            Assert.That(hide.HiddenCount, Is.GreaterThan(again.HiddenTriangles));

            // Locked: nothing about the hide changes, whatever the settings, and it stays on the recipe.
            hide.autoGenerationLocked = true;
            bool[][] before = Snapshot(hide.triangleFlags);
            UMAMeshHideAutoGenerator.RecipeResult locked = UMAMeshHideAutoGenerator.Generate(recipe, race, rig, body, null, new UMAMeshHideOcclusionSettings());
            UMAMeshHideAutoGenerator.SlotResult lockedSlot = locked.Slots.Find(s => s.SlotName == chosen.SlotName);
            Assert.IsTrue(lockedSlot.Locked);
            Assert.AreSame(hide, lockedSlot.Asset);
            CollectionAssert.AreEqual(before, Snapshot(hide.triangleFlags), "a locked hide keeps the Conservative result it had");
            CollectionAssert.Contains(recipe.MeshHideAssets, hide);
            StringAssert.Contains("locked", locked.Summary());

            // Even with nothing covering the body, a locked hide is neither emptied nor taken off.
            var nothing = new UMAMeshHideOcclusionSettings { outwardDistance = 0f, inwardDistance = 0f };
            UMAMeshHideAutoGenerator.Generate(recipe, race, rig, body, null, nothing);
            CollectionAssert.AreEqual(before, Snapshot(hide.triangleFlags));
            CollectionAssert.Contains(recipe.MeshHideAssets, hide);

            // Unlocked, the same settings empty it except for the always-hide mark, which keeps it on the recipe.
            hide.autoGenerationLocked = false;
            UMAMeshHideAutoGenerator.Generate(recipe, race, rig, body, null, nothing);
            Assert.AreEqual(1, hide.HiddenCount, "only the triangle painted hidden");
            Assert.IsTrue(hide.triangleFlags[0][visibleTriangle]);
            CollectionAssert.Contains(recipe.MeshHideAssets, hide);
        }

        /// <summary>A ray in avatar space that meets the given triangle of a body part before anything else, or null.</summary>
        private static Ray? RayOnto(UMAMeshHidePainter painter, UMAMeshHidePainter.BodyPart part, int submesh, int triangle)
        {
            int[] triangles = part.Triangles[submesh];
            int a = triangles[triangle * 3], b = triangles[triangle * 3 + 1], c = triangles[triangle * 3 + 2];
            Vector3 centre = (part.Positions[a] + part.Positions[b] + part.Positions[c]) / 3f;
            Vector3 normal = (part.Normals[a] + part.Normals[b] + part.Normals[c]).normalized;
            var ray = new Ray(centre + normal * 0.05f, -normal);
            if (painter.Raycast(ray, out UMAMeshHidePainter.Hit hit) && hit.Part == part && hit.Submesh == submesh && hit.Triangle == triangle)
            {
                return ray;
            }
            return null;
        }

        /// <summary>The first triangle of a part in the wanted state that a ray from outside can reach.</summary>
        private static Ray FindRay(UMAMeshHidePainter painter, UMAMeshHidePainter.BodyPart part, bool hidden, out int submesh, out int triangle, int skip = 0)
        {
            for (submesh = 0; submesh < part.Hidden.Length; submesh++)
            {
                for (triangle = 0; triangle < part.Hidden[submesh].Count; triangle++)
                {
                    if (part.Hidden[submesh][triangle] != hidden)
                    {
                        continue;
                    }
                    Ray? ray = RayOnto(painter, part, submesh, triangle);
                    if (ray.HasValue && skip-- <= 0)
                    {
                        return ray.Value;
                    }
                }
            }
            Assert.Fail("no reachable " + (hidden ? "hidden" : "visible") + " triangle on " + part.BaseSlotName);
            submesh = triangle = -1;
            return default;
        }

        [Test]
        public void TheOverlayShowsWhatIsHiddenAndTheBrushCorrectsIt()
        {
            UMATextRecipe recipe = MakeRecipeWithHides(out RaceData race, out UMARigRestPose rig, out List<UMAMeshHideAutoGenerator.BodySlot> body,
                out UMAMeshHideAutoGenerator.RecipeResult generated);
            int hidesOnRecipe = recipe.MeshHideAssets.Count;

            var preview = new UMARigProfilePreview();
            var painter = new UMAMeshHidePainter();
            try
            {
                Assert.IsTrue(preview.Build(race, null, new List<UMATextRecipe> { recipe }, null), string.Join(" | ", preview.Messages));
                Assert.AreEqual(1, preview.WornRecipes.Count);
                Assert.IsTrue(painter.Rebuild(preview));
                Assert.AreEqual(body.Count, painter.Parts.Count, "the painter sees the same body slots the generator does");
                Assert.AreEqual(generated.HiddenTriangles, painter.HiddenTriangles);
                Assert.AreEqual(preview.HiddenTriangles, painter.HiddenTriangles, "the overlay marks exactly what the preview character hides");
                Assert.AreEqual(hidesOnRecipe, painter.Hides.Count);

                // The overlay lies on the body: the painter reads the character's bones as they stand,
                // and in the rest pose that is where the generator's rest bake puts the same slots.
                foreach (UMAMeshHidePainter.BodyPart part in painter.Parts)
                {
                    UMAMeshHideAutoGenerator.BodySlot same = body.Find(b => b.BuiltSlotName == part.BuiltSlot.slotName);
                    Assert.IsNotNull(same, part.BuiltSlot.slotName);
                    Assert.AreEqual(same.Positions.Length, part.Positions.Length);
                    float worst = 0f;
                    for (int v = 0; v < part.Positions.Length; v++)
                    {
                        worst = Mathf.Max(worst, Vector3.Distance(same.Positions[v], part.Positions[v]));
                    }
                    Assert.That(worst, Is.LessThan(1e-3f), part.BaseSlotName + " drawn where the character's skeleton puts it");
                }
                Mesh overlay = painter.GetOverlayMesh();
                Assert.IsNotNull(overlay);
                Assert.AreEqual(painter.HiddenTriangles * 3, (int)overlay.GetIndexCount(0), "one overlay triangle per hidden triangle");

                // Un-hide the one triangle under the cursor.
                UMAMeshHidePainter.BodyPart covered = null;
                foreach (UMAMeshHidePainter.BodyPart part in painter.Parts)
                {
                    if (part.Hides.Count == 1 && (covered == null || part.Hides[0].Asset.HiddenCount > covered.Hides[0].Asset.HiddenCount)) covered = part;
                }
                Assert.IsNotNull(covered);
                MeshHideAsset hide = covered.Hides[0].Asset;
                Ray ray = FindRay(painter, covered, true, out int submesh, out int triangle);
                int hiddenBefore = painter.HiddenTriangles;
                Assert.AreEqual(1, painter.Paint(ray, 0f, false, null));
                Assert.AreEqual(hiddenBefore - 1, painter.HiddenTriangles);
                Assert.IsFalse(hide.triangleFlags[submesh][triangle]);
                CollectionAssert.Contains(hide.autoKeepVisible, MeshHideAsset.PackTriangle(submesh, triangle));
                Assert.AreEqual(0, painter.Paint(ray, 0f, false, null), "already visible");
                Assert.AreEqual((hiddenBefore - 1) * 3, (int)painter.GetOverlayMesh().GetIndexCount(0), "the overlay follows the brush");

                // Hide it again: the mark changes sides.
                Assert.AreEqual(1, painter.Paint(ray, 0f, true, recipe));
                Assert.IsTrue(hide.triangleFlags[submesh][triangle]);
                CollectionAssert.DoesNotContain(hide.autoKeepVisible, MeshHideAsset.PackTriangle(submesh, triangle));
                CollectionAssert.Contains(hide.autoForceHidden, MeshHideAsset.PackTriangle(submesh, triangle));
                Assert.AreEqual(hiddenBefore, painter.HiddenTriangles);

                // A brush with a radius takes the neighbours too.
                int cleared = painter.Paint(ray, 0.03f, false, null);
                Assert.That(cleared, Is.GreaterThan(1), "a 3 cm brush covers more than one triangle");
                Assert.AreEqual(hiddenBefore - cleared, painter.HiddenTriangles);
                // A brush with a radius can reach over a seam into the next body slot's hide.
                Assert.That(painter.CommitStroke(), Is.InRange(1, painter.Hides.Count), "the stroke changed the hide under the cursor, and perhaps a neighbour");
                Assert.AreEqual(0, painter.CommitStroke(), "and the stroke is over");
                Assert.IsFalse(EditorUtility.IsDirty(hide), "a finished stroke is saved");

                // The preview character shows the result, and generating again keeps it.
                preview.Refresh();
                Assert.AreEqual(painter.HiddenTriangles, preview.HiddenTriangles);
                UMAMeshHideAutoGenerator.Generate(recipe, race, rig, body, null, new UMAMeshHideOcclusionSettings());
                painter.ReloadHides(preview);
                Assert.AreEqual(hiddenBefore - cleared, painter.HiddenTriangles, "regenerating keeps what was painted");
                Assert.IsFalse(hide.triangleFlags[submesh][triangle]);

                // Painting hidden on a slot the generator found nothing on creates that slot's hide.
                UMAMeshHidePainter.BodyPart bare = null;
                foreach (UMAMeshHidePainter.BodyPart part in painter.Parts)
                {
                    if (part.Hides.Count == 0 && part.BaseSlot != null && !part.BaseSlotName.Contains("Eye")) bare = part;
                }
                if (bare != null)
                {
                    Ray bareRay = FindRay(painter, bare, false, out int bareSubmesh, out int bareTriangle);
                    Assert.AreEqual(1, painter.Paint(bareRay, 0f, true, recipe));
                    Assert.AreEqual(1, bare.Hides.Count);
                    MeshHideAsset made = bare.Hides[0].Asset;
                    StringAssert.StartsWith(TempFolder + "/" + UMAMeshHideAutoGenerator.FolderName + "/", AssetDatabase.GetAssetPath(made));
                    Assert.AreEqual(bare.BaseSlotName, made.AssetSlotName);
                    Assert.AreEqual(1, made.HiddenCount);
                    Assert.IsTrue(made.triangleFlags[bareSubmesh][bareTriangle]);
                    CollectionAssert.Contains(recipe.MeshHideAssets, made);
                    painter.CommitStroke();
                }

                // A locked hide is not changed by the brush.
                hide.autoGenerationLocked = true;
                Ray lockedRay = FindRay(painter, covered, true, out int lockedSubmesh, out int lockedTriangle);
                int lockedBefore = painter.HiddenTriangles;
                // One triangle, so the brush cannot reach a neighbouring slot's hide, which is not locked.
                Assert.AreEqual(0, painter.Paint(lockedRay, 0f, false, null));
                Assert.AreEqual(lockedBefore, painter.HiddenTriangles);
                Assert.IsTrue(hide.triangleFlags[lockedSubmesh][lockedTriangle]);
                hide.autoGenerationLocked = false;

                // Taking the clothing off leaves the body, with its hides, and putting it back restores it.
                int withClothing = preview.Avatar.umaData.umaRecipe.slotDataList.Length;
                preview.SetShowClothing(false);
                Assert.AreEqual(body.Count, preview.Avatar.umaData.umaRecipe.slotDataList.Length, "only the body is left");
                Assert.That(preview.HiddenTriangles, Is.GreaterThan(0), "the hides still apply to the bare body");
                Assert.IsTrue(painter.Rebuild(preview));
                Assert.AreEqual(preview.HiddenTriangles, painter.HiddenTriangles);
                preview.SetShowClothing(true);
                Assert.AreEqual(withClothing, preview.Avatar.umaData.umaRecipe.slotDataList.Length);
            }
            finally
            {
                painter.Dispose();
                preview.Dispose();
            }
        }
    }
}
