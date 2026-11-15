#if UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The MatureFace add-on faces: each is an overlay with its own albedo and normal map, limited to the face
    /// by the shared face alpha mask, and a wardrobe recipe for the Face slot that applies it.
    /// </summary>
    public class UMAMatureFaceContentTests
    {
        const string Folder = "Assets/UMA/UMA3/Textures/AlternateFaces/";

        static OverlayDataAsset LoadOverlay(string name)
        {
            return AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(Folder + name + "_Overlay.asset");
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void OverlayHasItsOwnAlbedoAndNormalMapAndUsesTheFaceMask(int number)
        {
            var reference = LoadOverlay("DetailedFace");
            if (reference == null) Assert.Ignore("The UMA 3 content is not installed.");
            string face = "MatureFace_" + number;
            var overlay = LoadOverlay(face);
            Assert.That(overlay, Is.Not.Null, face + "_Overlay.asset");
            Assert.That(overlay.overlayName, Is.EqualTo(face + "_Overlay"));
            Assert.That(overlay.overlayType, Is.EqualTo(reference.overlayType));
            Assert.That(overlay.material, Is.SameAs(reference.material), "same UMAMaterial as the other face add-ons");
            Assert.That(overlay.alphaMask, Is.Not.Null);
            Assert.That(overlay.alphaMask, Is.SameAs(reference.alphaMask), "edges are masked with the shared face alpha mask");

            // Channels: albedo and normal are this face's own; the packed mask and tint protection stay the body's.
            Assert.That(overlay.textureList.Length, Is.EqualTo(reference.textureList.Length));
            string albedoPath = AssetDatabase.GetAssetPath(overlay.textureList[0]);
            string normalPath = AssetDatabase.GetAssetPath(overlay.textureList[1]);
            Assert.That(albedoPath, Is.EqualTo(Folder + face + ".png"));
            Assert.That(normalPath, Is.EqualTo(Folder + face + "_Normal.png"));
            for (int i = 2; i < overlay.textureList.Length; i++) Assert.That(overlay.textureList[i], Is.Null, "channel " + i);

            var albedo = (TextureImporter)AssetImporter.GetAtPath(albedoPath);
            var normal = (TextureImporter)AssetImporter.GetAtPath(normalPath);
            Assert.That(albedo.textureType, Is.EqualTo(TextureImporterType.Default));
            Assert.That(albedo.sRGBTexture, Is.True);
            Assert.That(normal.textureType, Is.EqualTo(TextureImporterType.NormalMap));
            Assert.That(overlay.textureList[0].width, Is.EqualTo(2048));
            Assert.That(overlay.textureList[1].width, Is.EqualTo(2048));

            // No two faces share a texture.
            for (int other = 1; other <= 3; other++)
            {
                if (other == number) continue;
                var otherOverlay = LoadOverlay("MatureFace_" + other);
                Assert.That(otherOverlay.textureList[0], Is.Not.SameAs(overlay.textureList[0]));
                Assert.That(otherOverlay.textureList[1], Is.Not.SameAs(overlay.textureList[1]));
            }
        }

        static readonly System.Collections.Generic.Dictionary<string, Color32[]> pixelCache = new System.Collections.Generic.Dictionary<string, Color32[]>();

        /// <summary>The pixels of a 2048 x 2048 source PNG in the faces folder, as stored in the file.</summary>
        static Color32[] SourcePixels(string fileName)
        {
            if (pixelCache.TryGetValue(fileName, out var cached)) return cached;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(ImageConversion.LoadImage(texture, System.IO.File.ReadAllBytes(Folder + fileName)), Is.True, fileName);
                Assert.That(texture.width, Is.EqualTo(2048), fileName);
                Assert.That(texture.height, Is.EqualTo(2048), fileName);
                var pixels = texture.GetPixels32();
                pixelCache[fileName] = pixels;
                return pixels;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>Mean color difference, in levels of 255, over the middle of the face (every second texel).</summary>
        static double MeanDifference(Color32[] a, Color32[] b)
        {
            double sum = 0;
            int count = 0;
            for (int y = 512; y < 1536; y += 2)
                for (int x = 614; x < 1434; x += 2)
                {
                    Color32 p = a[y * 2048 + x], q = b[y * 2048 + x];
                    sum += (System.Math.Abs(p.r - q.r) + System.Math.Abs(p.g - q.g) + System.Math.Abs(p.b - q.b)) / 3.0;
                    count++;
                }
            return sum / count;
        }

        [Test]
        public void FacesHaveSkinOfTheirOwn()
        {
            if (LoadOverlay("DetailedFace") == null) Assert.Ignore("The UMA 3 content is not installed.");
            var detailed = SourcePixels("DetailedFace.png");
            for (int a = 1; a <= 3; a++)
            {
                var face = SourcePixels("MatureFace_" + a + ".png");
                // Each face has its own skin tone, not only added lines and spots on the Detailed Face texture.
                Assert.That(MeanDifference(face, detailed), Is.GreaterThan(2.0), "MatureFace_" + a + " against the Detailed Face");
                for (int b = a + 1; b <= 3; b++)
                    Assert.That(MeanDifference(face, SourcePixels("MatureFace_" + b + ".png")), Is.GreaterThan(3.0), "MatureFace_" + a + " against MatureFace_" + b);
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void SkinChangesFadeOutBeforeTheEdgeOfTheFaceMask(int number)
        {
            if (LoadOverlay("DetailedFace") == null) Assert.Ignore("The UMA 3 content is not installed.");
            var detailed = SourcePixels("DetailedFace.png");
            var mask = SourcePixels("FaceAlphaMask.png");
            var face = SourcePixels("MatureFace_" + number + ".png");

            // Along the edge of the mask the face is still the texture it started from, so a face with a different
            // skin tone meets the head without a visible outline.
            double sum = 0;
            int count = 0;
            for (int y = 1; y < 2047; y++)
                for (int x = 1; x < 2047; x++)
                {
                    int i = y * 2048 + x;
                    if (mask[i].a < 128) continue;
                    if (mask[i - 1].a >= 128 && mask[i + 1].a >= 128 && mask[i - 2048].a >= 128 && mask[i + 2048].a >= 128) continue;
                    sum += (System.Math.Abs(face[i].r - detailed[i].r) + System.Math.Abs(face[i].g - detailed[i].g) + System.Math.Abs(face[i].b - detailed[i].b)) / 3.0;
                    count++;
                }
            Assert.That(count, Is.GreaterThan(1000), "the mask has an edge inside the texture");
            Assert.That(sum / count, Is.LessThan(0.25), "mean difference along the mask edge, in levels of 255");
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void RecipeAppliesTheOverlayInTheFaceSlotAndIsInTheLibrary(int number)
        {
            var reference = AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(Folder + "TeenFace_1_Wardrobe.asset");
            if (reference == null) Assert.Ignore("The UMA 3 content is not installed.");
            string face = "MatureFace_" + number;
            var recipe = AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(Folder + face + "_Wardrobe.asset");
            Assert.That(recipe, Is.Not.Null, face + "_Wardrobe.asset");
            Assert.That(recipe.wardrobeSlot, Is.EqualTo(reference.wardrobeSlot));
            Assert.That(recipe.wardrobeSlot, Is.EqualTo("Face"));
            Assert.That(recipe.DisplayValue, Is.EqualTo(face));
            Assert.That(recipe.compatibleRaces, Is.EquivalentTo(reference.compatibleRaces));
            Assert.That(recipe.recipeString, Does.Contain("\"" + face + "_Overlay\""));
            Assert.That(recipe.recipeString, Does.Not.Contain("TeenFace"));
            // Apart from the overlay it names, the recipe is the same as the other face recipes.
            Assert.That(recipe.recipeString.Replace(face + "_Overlay", "X"), Is.EqualTo(reference.recipeString.Replace("TeenFace_1_Overlay", "X")));
            Assert.That(recipe.wardrobeRecipeThumbs.Select(t => t.race), Is.EquivalentTo(recipe.compatibleRaces));

            var library = UMAAssetIndexer.Instance;
            Assert.That(library.GetAssetItem<OverlayDataAsset>(face + "_Overlay"), Is.Not.Null, "overlay is in the Global Library");
            Assert.That(library.GetAssetItem<UMAWardrobeRecipe>(face + "_Wardrobe"), Is.Not.Null, "recipe is in the Global Library");
        }
    }
}
#endif
