#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The UMA 3 face add-ons whose albedo shows relief (wrinkles, scars, blemishes) carry that relief in a normal
    /// map of their own, so it also catches the light.
    /// </summary>
    public class UMAFaceNormalMapContentTests
    {
        const string Folder = "Assets/UMA/UMA3/Textures/AlternateFaces/";

        static OverlayDataAsset LoadOverlay(string name)
        {
            return AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(Folder + name + "_Overlay.asset");
        }

        [TestCase("Aged_U")]
        [TestCase("Aged_U2")]
        [TestCase("Scar_M")]
        [TestCase("Adult_M")]
        [TestCase("Scar3_U")]
        [TestCase("Scar4_U")]
        [TestCase("TeenFace_1")]
        [TestCase("TeenFace_2")]
        public void FaceWithReliefHasItsOwnNormalMap(string face)
        {
            if (LoadOverlay("DetailedFace") == null) Assert.Ignore("The UMA 3 content is not installed.");
            var overlay = LoadOverlay(face);
            Assert.That(overlay, Is.Not.Null, face + "_Overlay.asset");
            Assert.That(overlay.textureList.Length, Is.GreaterThan(1));
            Assert.That(overlay.textureList[0], Is.Not.Null, "albedo");

            string normalPath = AssetDatabase.GetAssetPath(overlay.textureList[1]);
            Assert.That(normalPath, Is.EqualTo(Folder + face + "_Normal.png"));
            var importer = (TextureImporter)AssetImporter.GetAtPath(normalPath);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.NormalMap));
            Assert.That(importer.sRGBTexture, Is.False);
            // The size of the body normal map it is built on, whatever the size of the albedo.
            Assert.That(overlay.textureList[1].width, Is.EqualTo(2048));
            Assert.That(overlay.textureList[1].height, Is.EqualTo(2048));

            // The packed mask and tint protection stay the body's.
            for (int i = 2; i < overlay.textureList.Length; i++) Assert.That(overlay.textureList[i], Is.Null, "channel " + i);
        }

        [TestCase("DetailedFace")]
        [TestCase("Adult_F")]
        [TestCase("Freckled_Light_U")]
        [TestCase("Freckled_More_U")]
        [TestCase("LightMakeup_F")]
        [TestCase("LightMakeup2_F")]
        [TestCase("MoreMakeup_F")]
        [TestCase("MoreMakeup2_F")]
        public void FaceWithPigmentOrMakeupOnlyKeepsTheBodyNormalMap(string face)
        {
            if (LoadOverlay("DetailedFace") == null) Assert.Ignore("The UMA 3 content is not installed.");
            var overlay = LoadOverlay(face);
            Assert.That(overlay, Is.Not.Null, face + "_Overlay.asset");
            // Freckles, makeup and lip liner are color, not relief: a normal map made from them would emboss them.
            Assert.That(overlay.textureList.Length < 2 || overlay.textureList[1] == null, Is.True, "no normal map of its own");
        }
    }
}
#endif
