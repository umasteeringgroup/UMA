using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintWardrobeRecipeTests
    {
        private static readonly FieldInfo IndexerField = typeof(UMAAssetIndexer).GetField(
            "theIndexer", BindingFlags.Static | BindingFlags.NonPublic);
        private readonly List<Object> owned = new List<Object>();
        private object previousIndexer;
        private UMAAssetIndexer indexer;
        private UMAWardrobeRecipe recipe;
        private SlotData first, second;

        [SetUp]
        public void SetUp()
        {
            previousIndexer = IndexerField.GetValue(null);
            indexer = Own(ScriptableObject.CreateInstance<UMAAssetIndexer>());
            indexer.SerializedItems.Clear();
            IndexerField.SetValue(null, indexer);
            first = Slot("RecipeTop", Color.green);
            first.AddOverlay(Overlay("TopDetail", first.material, Color.red));
            second = Slot("RecipeBottom", Color.yellow);
            indexer.DoInitialDictionaryLoad();
            recipe = Own(ScriptableObject.CreateInstance<UMAWardrobeRecipe>());
            recipe.name = "Painter Recipe Test";
            recipe.Save(new UMAData.UMARecipe { slotDataList = new[] { first, second } });
        }

        [TearDown]
        public void TearDown()
        {
            IndexerField.SetValue(null, previousIndexer);
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [Test]
        public void RecipeLaunchLoadsAllSlotsAndOrderedOverlaysWithoutChangingSource()
        {
            string original = recipe.recipeString;
            Assert.That(TexturePaintWardrobeRecipeMenu.TryCreateContext(recipe, out var context, out string error), Is.True, error);
            Assert.That(context.kind, Is.EqualTo(TexturePaintLaunchKind.WardrobeRecipe));
            Assert.That(context.IsStandalone, Is.True);
            Assert.That(context.members.Count, Is.EqualTo(2));
            Assert.That(context.members[0].slot, Is.SameAs(first.asset));
            Assert.That(context.members[1].slot, Is.SameAs(second.asset));
            var slots = MeshReconstructor.LoadWardrobeSlots(recipe, out _);
            Assert.That(slots[0].OverlayCount, Is.EqualTo(2));
            Assert.That(slots[0].GetOverlay(0).asset, Is.SameAs(first.GetOverlay(0).asset));
            Assert.That(slots[0].GetOverlay(1).asset, Is.SameAs(first.GetOverlay(1).asset));
            slots[0].GetOverlay(0).colorData.color = Color.black;
            Assert.That(MeshReconstructor.LoadWardrobeSlots(recipe, out _)[0].GetOverlay(0).colorData.color, Is.EqualTo(Color.white));
            Assert.That(recipe.recipeString, Is.EqualTo(original));
            Assert.That(context.Clone().wardrobeRecipe, Is.SameAs(recipe));
        }

        [Test]
        public void RecipeReconstructionCompositesEveryOverlayAndSupportsDifferentMaterials()
        {
            // Only the assigned submesh belongs in the recipe preview.
            first.asset.meshData.subMeshCount = 2;
            first.asset.meshData.submeshes = new[]
            {
                new SubMeshTriangles(new[] { 0, 1, 2 }), new SubMeshTriangles(new[] { 2, 1, 0 })
            };
            first.asset.subMeshIndex = 1;
            Assert.That(TexturePaintWardrobeRecipeMenu.TryCreateContext(recipe, out var context, out string error), Is.True, error);
            using (var result = MeshReconstructor.ReconstructSlotGroup(context))
            {
                Assert.That(result.surfaces.Count, Is.EqualTo(2));
                Assert.That(result.logicalTargets.Targets.Count, Is.EqualTo(2));
                Assert.That(result.surfaces[0].umaMaterial, Is.SameAs(first.material));
                Assert.That(result.surfaces[1].umaMaterial, Is.SameAs(second.material));
                Assert.That(result.surfaces[0].generatedMaterial.materialFragments[0].overlayList.Count, Is.EqualTo(2));
                Assert.That(result.surfaces[0].slots[0].OverlayCount, Is.EqualTo(2));
                Assert.That(result.surfaces[0].mesh.subMeshCount, Is.EqualTo(1));
                CollectionAssert.AreEqual(new[] { 2, 1, 0 }, result.surfaces[0].mesh.GetTriangles(0));
                AssertColor(result.surfaces[0].sourceTextures[0], Color.red);
                AssertColor(result.surfaces[1].sourceTextures[0], Color.yellow);
            }
            Assert.That(first.asset.meshData.vertices.Length, Is.EqualTo(3));
            Assert.That(first.GetOverlay(0).asset.textureList[0] != null, Is.True);
        }

        [Test]
        public void EmptyRecipeReportsNoPaintableSlots()
        {
            recipe.Save(new UMAData.UMARecipe { slotDataList = Array.Empty<SlotData>() });
            Assert.That(TexturePaintWardrobeRecipeMenu.TryCreateContext(recipe, out _, out string error), Is.False);
            Assert.That(error, Does.Contain("no paintable slots"));
        }

        [Test]
        public void RecoverySeparatesRecipesUsingTheSameSlots()
        {
            Assert.That(TexturePaintWardrobeRecipeMenu.TryCreateContext(recipe, out var firstContext, out _), Is.True);
            firstContext.wardrobeRecipeGuid = "first-recipe";
            var secondContext = firstContext.Clone();
            secondContext.wardrobeRecipeGuid = "second-recipe";
            Assert.That(TexturePaintRecoveryStore.GetContextKey(firstContext),
                Is.Not.EqualTo(TexturePaintRecoveryStore.GetContextKey(secondContext)));
        }

        [Test]
        public void PlaceholderUsesEveryMatchingBaseSlotAndItsOwnOverlayMaterial()
        {
            first.tags = new[] { "Face" };
            second.tags = new[] { "Face" };
            var unrelated = Slot("UnrelatedBody", Color.blue);
            unrelated.tags = new[] { "Body" };
            var race = Race("Preview Race", first, second, unrelated);
            var wildcard = SlotData.CreatePlaceholder("Face overlays", new[] { "Face" });
            wildcard.AddOverlay(first.GetOverlay(0));
            wildcard.AddOverlay(first.GetOverlay(1));
            recipe.Save(new UMAData.UMARecipe { slotDataList = new[] { wildcard } });
            recipe.compatibleRaces = new List<string> { race.raceName };
            indexer.DoInitialDictionaryLoad();
            string original = recipe.recipeString;
            string originalBase = ((UMATextRecipe)race.baseRaceRecipe).recipeString;

            Assert.That(TexturePaintWardrobeRecipeMenu.TryCreateContext(recipe, out var context, out string error), Is.True, error);
            Assert.That(context.wardrobePreviewRace, Is.SameAs(race));
            Assert.That(context.Clone().wardrobePreviewRace, Is.SameAs(race));
            Assert.That(context.members.Count, Is.EqualTo(2));
            var slots = MeshReconstructor.LoadWardrobeSlots(recipe, out _);
            foreach (var slot in slots)
            {
                Assert.That(slot.isPlaceholderSlot, Is.False);
                Assert.That(slot.OverlayCount, Is.EqualTo(2));
                Assert.That(slot.material, Is.SameAs(first.material));
                Assert.That(slot.GetOverlay(1).asset, Is.SameAs(first.GetOverlay(1).asset));
            }
            using (var result = MeshReconstructor.ReconstructSlotGroup(context))
            {
                Assert.That(result.surfaces.Count, Is.EqualTo(2));
                AssertColor(result.surfaces[1].sourceTextures[0], Color.red);
            }
            slots[0].GetOverlay(0).colorData.color = Color.black;
            Assert.That(slots[1].GetOverlay(0).colorData.color, Is.EqualTo(Color.white));
            Assert.That(recipe.recipeString, Is.EqualTo(original));
            Assert.That(((UMATextRecipe)race.baseRaceRecipe).recipeString, Is.EqualTo(originalBase));
        }

        [Test]
        public void WildcardRaceFilterSkipsEarlierCompatibleRace()
        {
            first.tags = new[] { "Face" };
            var wrong = Race("Wrong Race", first);
            var right = Race("Right Race", first);
            var wildcard = SlotData.CreatePlaceholder("Face", new[] { "Face" }, new[] { right.raceName });
            wildcard.AddOverlay(first.GetOverlay(0));
            recipe.Save(new UMAData.UMARecipe { slotDataList = new[] { wildcard } });
            recipe.compatibleRaces = new List<string> { wrong.raceName, right.raceName };
            indexer.DoInitialDictionaryLoad();

            MeshReconstructor.LoadWardrobeSlots(recipe, out _, out var resolvedRace);
            Assert.That(resolvedRace, Is.SameAs(right));
            Assert.Throws<InvalidOperationException>(() =>
                MeshReconstructor.LoadWardrobeSlots(recipe, out _, out _, wrong));
        }

        [Test]
        public void WildcardMatchesRecipeGeometryWithoutAddingUnrelatedSlots()
        {
            first.tags = new[] { "Face" };
            second.tags = new[] { "Body" };
            var wildcard = SlotData.CreatePlaceholder("Face detail", new[] { "Face" });
            wildcard.AddOverlay(second.GetOverlay(0));
            recipe.Save(new UMAData.UMARecipe { slotDataList = new[] { first, second, wildcard } });
            var slots = MeshReconstructor.LoadWardrobeSlots(recipe, out _, out var race);
            Assert.That(race, Is.Null);
            Assert.That(slots.Count, Is.EqualTo(2));
            Assert.That(slots[0].OverlayCount, Is.EqualTo(3));
            Assert.That(slots[1].OverlayCount, Is.EqualTo(1));
        }

        [Test]
        public void UnmatchedWildcardExplainsMissingPreviewMesh()
        {
            var wildcard = SlotData.CreatePlaceholder("Face", new[] { "Missing tag" });
            wildcard.AddOverlay(first.GetOverlay(0));
            recipe.Save(new UMAData.UMARecipe { slotDataList = new[] { wildcard } });
            Assert.That(TexturePaintWardrobeRecipeMenu.TryCreateContext(recipe, out _, out string error), Is.False);
            Assert.That(error, Does.Contain("no matching preview mesh"));
            Assert.That(error, Does.Contain("wildcard tags and race filters"));
        }

        private RaceData Race(string name, params SlotData[] slots)
        {
            var baseRecipe = Own(ScriptableObject.CreateInstance<UMATextRecipe>());
            baseRecipe.Save(new UMAData.UMARecipe { slotDataList = slots });
            var race = Own(ScriptableObject.CreateInstance<RaceData>());
            race.name = name;
            race.baseRaceRecipe = baseRecipe;
            indexer.SerializedItems.Add(new AssetItem(typeof(RaceData), race.raceName, "", race));
            return race;
        }

        private SlotData Slot(string name, Color color)
        {
            var material = Own(ScriptableObject.CreateInstance<UMAMaterial>());
            material.name = name + " Material";
            material.material = Own(new Material(Shader.Find("Standard")));
            material.channels = new[] { new UMAMaterial.MaterialChannel
            {
                channelType = UMAMaterial.ChannelType.DiffuseTexture,
                textureFormat = RenderTextureFormat.ARGB32,
                materialPropertyName = "_MainTex", sourceTextureName = "_MainTex"
            }};
            var asset = Own(ScriptableObject.CreateInstance<SlotDataAsset>());
            asset.name = name;
            asset.meshData = new UMAMeshData
            {
                vertexCount = 3, subMeshCount = 1,
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up },
                submeshes = new[] { new SubMeshTriangles(new[] { 0, 1, 2 }) }
            };
            indexer.SerializedItems.Add(new AssetItem(typeof(SlotDataAsset), asset.slotName, "", asset));
            var slot = new SlotData(asset);
            slot.AddOverlay(Overlay(name + " Base", material, color));
            return slot;
        }

        private OverlayData Overlay(string name, UMAMaterial material, Color color)
        {
            var texture = Own(new Texture2D(32, 32, TextureFormat.RGBA32, false));
            var pixels = new Color[32 * 32];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply();
            var asset = Own(ScriptableObject.CreateInstance<OverlayDataAsset>());
            asset.name = name;
            asset.material = material;
            asset.textureList = new Texture[] { texture };
            indexer.SerializedItems.Add(new AssetItem(typeof(OverlayDataAsset), asset.overlayName, "", asset));
            return new OverlayData(asset);
        }

        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        private static void AssertColor(Texture source, Color expected)
        {
            // Source textures are sRGB; the composite/readback target stores linear values.
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) expected = expected.linear;
            var previous = RenderTexture.active;
            var readback = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = (RenderTexture)source;
                readback.ReadPixels(new Rect(source.width / 2, source.height / 2, 1, 1), 0, 0);
                readback.Apply();
                var actual = readback.GetPixel(0, 0);
                Assert.That(actual.r, Is.EqualTo(expected.r).Within(.03f));
                Assert.That(actual.g, Is.EqualTo(expected.g).Within(.03f));
                Assert.That(actual.b, Is.EqualTo(expected.b).Within(.03f));
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(readback); }
        }
    }
}
