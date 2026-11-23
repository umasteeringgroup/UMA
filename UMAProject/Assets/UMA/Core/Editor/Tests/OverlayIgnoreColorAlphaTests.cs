using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    /// <summary>
    /// An overlay marked Ignore Color Alpha keeps full strength on Diffuse Texture channels,
    /// so a base skin stays in place while another overlay that shares its color fades in above it.
    /// </summary>
    public class OverlayIgnoreColorAlphaTests
    {
        private OverlayDataAsset baseAsset;
        private OverlayDataAsset topAsset;
        private UMAMaterial material;

        [SetUp]
        public void SetUp()
        {
            baseAsset = ScriptableObject.CreateInstance<OverlayDataAsset>();
            topAsset = ScriptableObject.CreateInstance<OverlayDataAsset>();
            material = ScriptableObject.CreateInstance<UMAMaterial>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(baseAsset);
            Object.DestroyImmediate(topAsset);
            Object.DestroyImmediate(material);
        }

        private UMAData.MaterialFragment Fragment(UMAMaterial.ChannelType firstChannel, Color shared)
        {
            material.channels = new[]
            {
                new UMAMaterial.MaterialChannel { channelType = firstChannel, materialPropertyName = "_BaseMap" },
                new UMAMaterial.MaterialChannel { channelType = UMAMaterial.ChannelType.Texture, materialPropertyName = "_MaskMap" }
            };
            return new UMAData.MaterialFragment
            {
                umaMaterial = material,
                overlayData = new[] { new OverlayData(baseAsset), new OverlayData(topAsset) },
                // Both overlays use the same shared color, on both channels.
                channelMask = new[] { new[] { shared, shared }, new[] { shared, shared } }
            };
        }

        [Test]
        public void MarkedOverlayKeepsFullStrengthOnDiffuseChannel()
        {
            baseAsset.ignoreColorAlpha = true;
            var shared = new Color(0.8f, 0.6f, 0.4f, 0.25f);
            UMAData.MaterialFragment fragment = Fragment(UMAMaterial.ChannelType.DiffuseTexture, shared);

            Color baseColor = fragment.GetMultiplier(0, 0);
            Color topColor = fragment.GetMultiplier(1, 0);

            Assert.AreEqual(1f, baseColor.a, 1e-6f, "The marked overlay must not fade.");
            Assert.AreEqual(shared.r, baseColor.r, 1e-6f, "The shared tint must be kept.");
            Assert.AreEqual(shared.g, baseColor.g, 1e-6f);
            Assert.AreEqual(shared.b, baseColor.b, 1e-6f);
            Assert.AreEqual(shared.a, topColor.a, 1e-6f, "The unmarked overlay still fades with the color's alpha.");
        }

        [Test]
        public void MarkedOverlayKeepsAlphaOnDataChannels()
        {
            baseAsset.ignoreColorAlpha = true;
            var shared = new Color(1f, 1f, 1f, 0.25f);

            // Second channel of a material whose first channel is diffuse: alpha is data there.
            UMAData.MaterialFragment fragment = Fragment(UMAMaterial.ChannelType.DiffuseTexture, shared);
            Assert.AreEqual(shared.a, fragment.GetMultiplier(0, 1).a, 1e-6f);

            // A Texture-type first channel does not use the color's alpha as strength either.
            fragment = Fragment(UMAMaterial.ChannelType.Texture, shared);
            Assert.AreEqual(shared.a, fragment.GetMultiplier(0, 0).a, 1e-6f);
        }

        [Test]
        public void UnmarkedOverlaysAreUnchanged()
        {
            var shared = new Color(1f, 1f, 1f, 0.25f);
            UMAData.MaterialFragment fragment = Fragment(UMAMaterial.ChannelType.DiffuseTexture, shared);

            Assert.AreEqual(shared.a, fragment.GetMultiplier(0, 0).a, 1e-6f);
            Assert.AreEqual(shared.a, fragment.GetMultiplier(1, 0).a, 1e-6f);
        }
    }
}
