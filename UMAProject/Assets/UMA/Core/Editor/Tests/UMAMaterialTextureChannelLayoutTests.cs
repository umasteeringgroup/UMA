#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMAMaterialTextureChannelLayoutTests
    {
        [Test]
        public void AutomaticMaskMapUsesHdrpPackingConvention()
        {
            UMAMaterial.MaterialChannel channel = Channel("_MaskMap");

            UMAMaterial.TextureChannelLayout layout = UMAMaterial.InferTextureChannelLayout(channel, null);

            Assert.That(layout.mode, Is.EqualTo(UMAMaterial.TextureChannelLayoutMode.Automatic));
            Assert.That(layout.red, Is.EqualTo(UMAMaterial.TextureChannelUsage.Metallic));
            Assert.That(layout.green, Is.EqualTo(UMAMaterial.TextureChannelUsage.AmbientOcclusion));
            Assert.That(layout.blue, Is.EqualTo(UMAMaterial.TextureChannelUsage.DetailMask));
            Assert.That(layout.alpha, Is.EqualTo(UMAMaterial.TextureChannelUsage.Smoothness));
        }

        [Test]
        public void AutomaticMetallicGlossMapStoresSmoothnessInAlpha()
        {
            UMAMaterial.TextureChannelLayout layout = UMAMaterial.InferTextureChannelLayout(
                Channel("_MetallicGlossMap"), null);

            Assert.That(layout.red, Is.EqualTo(UMAMaterial.TextureChannelUsage.Metallic));
            Assert.That(layout.green, Is.EqualTo(UMAMaterial.TextureChannelUsage.Unused));
            Assert.That(layout.blue, Is.EqualTo(UMAMaterial.TextureChannelUsage.Unused));
            Assert.That(layout.alpha, Is.EqualTo(UMAMaterial.TextureChannelUsage.Smoothness));
        }

        [Test]
        public void AutomaticHdrpDetailMapRetainsSplitDetailMeanings()
        {
            UMAMaterial.TextureChannelLayout layout = UMAMaterial.InferTextureChannelLayout(
                Channel("_DetailMap"), null);

            Assert.That(layout.red, Is.EqualTo(UMAMaterial.TextureChannelUsage.DetailAlbedo));
            Assert.That(layout.green, Is.EqualTo(UMAMaterial.TextureChannelUsage.DetailNormalY));
            Assert.That(layout.blue, Is.EqualTo(UMAMaterial.TextureChannelUsage.DetailSmoothness));
            Assert.That(layout.alpha, Is.EqualTo(UMAMaterial.TextureChannelUsage.DetailNormalX));
        }

        [Test]
        public void AutomaticStandaloneOcclusionUsesGreenComponent()
        {
            UMAMaterial.TextureChannelLayout layout = UMAMaterial.InferTextureChannelLayout(
                Channel("_OcclusionMap"), null);

            Assert.That(layout.red, Is.EqualTo(UMAMaterial.TextureChannelUsage.Unused));
            Assert.That(layout.green, Is.EqualTo(UMAMaterial.TextureChannelUsage.AmbientOcclusion));
            Assert.That(layout.blue, Is.EqualTo(UMAMaterial.TextureChannelUsage.Unused));
            Assert.That(layout.alpha, Is.EqualTo(UMAMaterial.TextureChannelUsage.Unused));
        }

        [Test]
        public void AutomaticNormalLayoutUsesChannelTypeWithoutPropertyHeuristic()
        {
            UMAMaterial.MaterialChannel channel = Channel("_ProjectSpecificTexture");
            channel.channelType = UMAMaterial.ChannelType.NormalMap;

            UMAMaterial.TextureChannelLayout layout = UMAMaterial.InferTextureChannelLayout(channel, null);

            Assert.That(layout.red, Is.EqualTo(UMAMaterial.TextureChannelUsage.Normal));
            Assert.That(layout.green, Is.EqualTo(UMAMaterial.TextureChannelUsage.Normal));
            Assert.That(layout.blue, Is.EqualTo(UMAMaterial.TextureChannelUsage.Normal));
            Assert.That(layout.alpha, Is.EqualTo(UMAMaterial.TextureChannelUsage.Unused));
        }

        [Test]
        public void BaseAlphaCanDescribeOpacityAndSmoothnessTogether()
        {
            Shader shader = Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null);
            Material material = new Material(shader);
            try
            {
                Assert.That(material.HasProperty("_SmoothnessTextureChannel"), Is.True);
                material.SetFloat("_SmoothnessTextureChannel", 1f);

                UMAMaterial.TextureChannelLayout layout = UMAMaterial.InferTextureChannelLayout(
                    Channel("_MainTex"), material);

                Assert.That((layout.alpha & UMAMaterial.TextureChannelUsage.Opacity) != 0, Is.True);
                Assert.That((layout.alpha & UMAMaterial.TextureChannelUsage.Smoothness) != 0, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void CustomLayoutOverridesAutomaticDetection()
        {
            UMAMaterial.MaterialChannel channel = Channel("_MaskMap");
            channel.textureChannelLayout = new UMAMaterial.TextureChannelLayout
            {
                mode = UMAMaterial.TextureChannelLayoutMode.Custom,
                red = UMAMaterial.TextureChannelUsage.Roughness,
                green = UMAMaterial.TextureChannelUsage.Metallic,
                blue = UMAMaterial.TextureChannelUsage.Custom,
                alpha = UMAMaterial.TextureChannelUsage.AmbientOcclusion
            };

            UMAMaterial.TextureChannelLayout layout = UMAMaterial.GetTextureChannelLayout(channel, null);

            Assert.That(layout.mode, Is.EqualTo(UMAMaterial.TextureChannelLayoutMode.Custom));
            Assert.That(layout.red, Is.EqualTo(UMAMaterial.TextureChannelUsage.Roughness));
            Assert.That(layout.green, Is.EqualTo(UMAMaterial.TextureChannelUsage.Metallic));
            Assert.That(layout.blue, Is.EqualTo(UMAMaterial.TextureChannelUsage.Custom));
            Assert.That(layout.alpha, Is.EqualTo(UMAMaterial.TextureChannelUsage.AmbientOcclusion));
        }

        private static UMAMaterial.MaterialChannel Channel(string property)
        {
            return new UMAMaterial.MaterialChannel
            {
                channelType = UMAMaterial.ChannelType.Texture,
                materialPropertyName = property,
                sourceTextureName = property
            };
        }
    }
}
#endif
