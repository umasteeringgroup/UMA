#if UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Saving generated textures (Save as prefab, Save Textures, glTF export): colour is written sRGB-encoded,
    /// linear data (metallic, occlusion, normals, masks) is written unchanged. UMA atlases are Linear render
    /// textures for every channel, so the channel's role decides, not the texture's own flag.
    /// </summary>
    public sealed class UMAGeneratedTextureReadbackTests
    {
        private const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Static;

        private static Texture2D ReadBack(Texture source, bool linearData)
        {
            MethodInfo method = typeof(UMAAvatarLoadSaveMenuItems).GetMethod("GetReadableTexture", Internal, null,
                new[] { source is RenderTexture ? typeof(RenderTexture) : typeof(Texture2D), typeof(bool) }, null);
            Assert.IsNotNull(method, "GetReadableTexture");
            return (Texture2D)method.Invoke(null, new object[] { source, linearData });
        }

        private static Texture2D Solid(bool linear, Color32 color)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, linear);
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static void AssertNear(Color32 expected, Color32 actual, string what)
        {
            Assert.That(actual.r, Is.InRange(expected.r - 1, expected.r + 1), what + " red");
            Assert.That(actual.g, Is.InRange(expected.g - 1, expected.g + 1), what + " green");
            Assert.That(actual.b, Is.InRange(expected.b - 1, expected.b + 1), what + " blue");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AnAtlasComesBackWithTheBytesItsSourceHad(bool linearData)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear)
            {
                Assert.Ignore("Colour-space conversion only happens in a Linear project.");
            }

            var expected = new Color32(64, 128, 200, 255);
            Texture2D source = Solid(linearData, expected);
            var atlas = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Texture2D fromAtlas = null, fromTexture = null;
            try
            {
                atlas.Create();
                Graphics.Blit(source, atlas); // what the atlas shader does with an overlay texture
                fromAtlas = ReadBack(atlas, linearData);
                fromTexture = ReadBack(source, linearData);
                AssertNear(expected, fromAtlas.GetPixels32()[5], linearData ? "data atlas" : "colour atlas");
                AssertNear(expected, fromTexture.GetPixels32()[5], linearData ? "data texture" : "colour texture");
            }
            finally
            {
                atlas.Release();
                Object.DestroyImmediate(atlas);
                Object.DestroyImmediate(source);
                if (fromAtlas != null) Object.DestroyImmediate(fromAtlas);
                if (fromTexture != null) Object.DestroyImmediate(fromTexture);
            }
        }

        [TestCase("_BaseMap", false)]
        [TestCase("_MainTex", false)]
        [TestCase("_MetallicGlossMap", true)]
        [TestCase("_MaskMap", true)]
        [TestCase("_OcclusionMap", true)]
        public void TheChannelsColorSpaceDecidesTheReadback(string property, bool expectLinear)
        {
            var umaMaterial = ScriptableObject.CreateInstance<UMAMaterial>();
            try
            {
                umaMaterial.channels = new[]
                {
                    new UMAMaterial.MaterialChannel { channelType = UMAMaterial.ChannelType.Texture, materialPropertyName = property }
                };
                object[] args = { umaMaterial, property, false };
                bool found = (bool)typeof(UMAAvatarLoadSaveMenuItems).GetMethod("TryGetChannelIsLinear", Internal).Invoke(null, args);
                Assert.IsTrue(found, "the channel writing " + property);
                Assert.AreEqual(expectLinear, (bool)args[2], property);
            }
            finally
            {
                Object.DestroyImmediate(umaMaterial);
            }
        }

        [TestCase("_BumpMap", true)]
        [TestCase("_MetallicGlossMap", true)]
        [TestCase("_OcclusionMap", true)]
        [TestCase("_MainTex", false)]
        [TestCase("_EmissionMap", false)]
        public void WithoutAnUMAMaterialThePropertyNameDecides(string property, bool expectLinear)
        {
            bool linear = (bool)typeof(UMAAvatarLoadSaveMenuItems).GetMethod("IsLinearDataPropertyName", Internal).Invoke(null, new object[] { property });
            Assert.AreEqual(expectLinear, linear, property);
        }
    }
}
#endif
