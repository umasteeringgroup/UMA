#if UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintMaterialCapabilityTests
    {
        [Test]
        public void UrpLitCompilesDocumentedPhysicalLayoutsAndImporterSettings()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null, "URP Lit shader is required by the Phase 2 contract tests.");
            Material material = new Material(shader);
            UMAMaterial uma = ScriptableObject.CreateInstance<UMAMaterial>();
            try
            {
                uma.material = material;
                uma.channels = new[]
                {
                    Channel(UMAMaterial.ChannelType.DiffuseTexture, "_BaseMap"),
                    Channel(UMAMaterial.ChannelType.NormalMap, "_BumpMap"),
                    Channel(UMAMaterial.ChannelType.Texture, "_MetallicGlossMap"),
                    Channel(UMAMaterial.ChannelType.Texture, "_OcclusionMap")
                };
                TexturePaintMaterialCapabilityDescriptor descriptor =
                    TexturePaintMaterialCapabilityService.Compile(uma, material, null, true);

                Assert.That(descriptor.pipeline, Is.EqualTo(TexturePaintMaterialPipeline.Universal));
                // The null graphics device used by headless EditMode tests does not expose
                // random-write render targets. Device diagnostics are expected there; the
                // material/shader contract itself must remain error-free.
                Assert.That(descriptor.Diagnostics.Any(item =>
                    item.severity == TexturePaintCapabilitySeverity.Error &&
                    (item.code.StartsWith("MAT") || item.code.StartsWith("CHN"))), Is.False,
                    descriptor.FailureSummary());
                Assert.That(descriptor.GetChannel(0).layout.red, Is.EqualTo(UMAMaterial.TextureChannelUsage.Albedo));
                Assert.That(descriptor.GetChannel(0).output.colorSpace,
                    Is.EqualTo(UMAMaterial.TextureChannelColorSpace.SRGB));
                Assert.That(descriptor.GetChannel(1).output.importerType,
                    Is.EqualTo(UMAMaterial.TextureChannelImporterType.NormalMap));
                Assert.That(descriptor.GetChannel(1).output.normalConvention,
                    Is.EqualTo(UMAMaterial.TextureChannelNormalConvention.OpenGL));
                Assert.That(descriptor.GetChannel(2).layout.red,
                    Is.EqualTo(UMAMaterial.TextureChannelUsage.Metallic));
                Assert.That(descriptor.GetChannel(2).layout.alpha,
                    Is.EqualTo(UMAMaterial.TextureChannelUsage.Smoothness));
                Assert.That(descriptor.GetChannel(2).requiresPacking, Is.True);
                Assert.That(descriptor.GetChannel(3).layout.green,
                    Is.EqualTo(UMAMaterial.TextureChannelUsage.AmbientOcclusion));
                Assert.That(descriptor.GetChannel(3).Components[1].neutralValue, Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(uma);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void UrpBaseAlphaInferenceReadsMaterialSmoothnessSource()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            Material material = new Material(shader);
            try
            {
                Assert.That(material.HasProperty("_SmoothnessTextureChannel"), Is.True);
                UMAMaterial.MaterialChannel channel = Channel(UMAMaterial.ChannelType.DiffuseTexture,
                    "_BaseMap");

                material.SetFloat("_SmoothnessTextureChannel", 0f);
                Assert.That(UMAMaterial.InferTextureChannelLayout(channel, material).alpha,
                    Is.EqualTo(UMAMaterial.TextureChannelUsage.Opacity));

                material.SetFloat("_SmoothnessTextureChannel", 1f);
                Assert.That(UMAMaterial.InferTextureChannelLayout(channel, material).alpha,
                    Is.EqualTo(UMAMaterial.TextureChannelUsage.Opacity |
                               UMAMaterial.TextureChannelUsage.Smoothness));
            }
            finally { Object.DestroyImmediate(material); }
        }

        [Test]
        public void HdrpLitInferenceMatchesMaskAndDetailShaderSource()
        {
            Shader shader = Shader.Find("HDRP/Lit");
            Assert.That(shader, Is.Not.Null, "HDRP Lit shader is required by the Phase 2 contract tests.");
            Material material = new Material(shader);
            UMAMaterial uma = ScriptableObject.CreateInstance<UMAMaterial>();
            try
            {
                UMAMaterial.MaterialChannel mask = Channel(UMAMaterial.ChannelType.Texture, "_MaskMap");
                UMAMaterial.TextureChannelLayout maskLayout =
                    UMAMaterial.InferTextureChannelLayout(mask, material);
                Assert.That(maskLayout.red, Is.EqualTo(UMAMaterial.TextureChannelUsage.Metallic));
                Assert.That(maskLayout.green, Is.EqualTo(UMAMaterial.TextureChannelUsage.AmbientOcclusion));
                Assert.That(maskLayout.blue, Is.EqualTo(UMAMaterial.TextureChannelUsage.DetailMask));
                Assert.That(maskLayout.alpha, Is.EqualTo(UMAMaterial.TextureChannelUsage.Smoothness));

                UMAMaterial.MaterialChannel detail = Channel(UMAMaterial.ChannelType.Texture, "_DetailMap");
                UMAMaterial.TextureChannelLayout detailLayout =
                    UMAMaterial.InferTextureChannelLayout(detail, material);
                Assert.That(detailLayout.red, Is.EqualTo(UMAMaterial.TextureChannelUsage.DetailAlbedo));
                Assert.That(detailLayout.green, Is.EqualTo(UMAMaterial.TextureChannelUsage.DetailNormalY));
                Assert.That(detailLayout.blue, Is.EqualTo(UMAMaterial.TextureChannelUsage.DetailSmoothness));
                Assert.That(detailLayout.alpha, Is.EqualTo(UMAMaterial.TextureChannelUsage.DetailNormalX));
                UMAMaterial.TextureChannelOutputSettings detailOutput =
                    UMAMaterial.InferTextureChannelOutputSettings(detail, material, null);
                Assert.That(detailOutput.colorSpace, Is.EqualTo(UMAMaterial.TextureChannelColorSpace.Linear));
                Assert.That(detailOutput.importerType, Is.EqualTo(UMAMaterial.TextureChannelImporterType.Default));

                uma.material = material;
                uma.channels = new[] { mask, detail };
                TexturePaintMaterialCapabilityDescriptor descriptor =
                    TexturePaintMaterialCapabilityService.Compile(uma, material, null, true);
                Assert.That(descriptor.pipeline, Is.EqualTo(TexturePaintMaterialPipeline.HighDefinition));
            }
            finally
            {
                Object.DestroyImmediate(uma);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void CustomLayoutAndOutputOverrideAutomaticInference()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            Material material = new Material(shader);
            UMAMaterial uma = ScriptableObject.CreateInstance<UMAMaterial>();
            try
            {
                uma.material = material;
                UMAMaterial.MaterialChannel channel = Channel(UMAMaterial.ChannelType.Texture,
                    "_MetallicGlossMap");
                channel.textureChannelLayout = new UMAMaterial.TextureChannelLayout
                {
                    mode = UMAMaterial.TextureChannelLayoutMode.Custom,
                    red = UMAMaterial.TextureChannelUsage.AmbientOcclusion,
                    green = UMAMaterial.TextureChannelUsage.Metallic,
                    blue = UMAMaterial.TextureChannelUsage.Roughness,
                    alpha = UMAMaterial.TextureChannelUsage.Unused
                };
                channel.textureChannelOutput = new UMAMaterial.TextureChannelOutputSettings
                {
                    mode = UMAMaterial.TextureChannelOutputMode.Custom,
                    encoding = UMAMaterial.TextureChannelOutputEncoding.ExrHalf,
                    importerType = UMAMaterial.TextureChannelImporterType.Default,
                    colorSpace = UMAMaterial.TextureChannelColorSpace.Linear,
                    alphaSource = UMAMaterial.TextureChannelAlphaSource.None,
                    compression = UMAMaterial.TextureChannelImportCompression.HighQuality,
                    normalConvention = UMAMaterial.TextureChannelNormalConvention.DirectX,
                    generateMipMaps = false,
                    filterMode = FilterMode.Point,
                    anisoLevel = 4,
                    maxTextureSize = 4096,
                    platformOverrides = new[]
                    {
                        new UMAMaterial.TextureChannelPlatformOverrideSettings
                        {
                            enabled = true,
                            platformName = "Standalone",
                            maxTextureSize = 2048,
                            compression = UMAMaterial.TextureChannelImportCompression.HighQuality
                        }
                    }
                };
                uma.channels = new[] { channel };

                TexturePaintMaterialCapabilityDescriptor descriptor =
                    TexturePaintMaterialCapabilityService.Compile(uma, material, null, true);
                TexturePaintMaterialChannelCapability compiled = descriptor.GetChannel(0);
                Assert.That(compiled.layout.red, Is.EqualTo(UMAMaterial.TextureChannelUsage.AmbientOcclusion));
                Assert.That(compiled.layout.green, Is.EqualTo(UMAMaterial.TextureChannelUsage.Metallic));
                Assert.That(compiled.layout.blue, Is.EqualTo(UMAMaterial.TextureChannelUsage.Roughness));
                Assert.That(compiled.output.encoding, Is.EqualTo(UMAMaterial.TextureChannelOutputEncoding.ExrHalf));
                Assert.That(compiled.output.maxTextureSize, Is.EqualTo(4096));
                Assert.That(compiled.output.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(compiled.output.normalConvention,
                    Is.EqualTo(UMAMaterial.TextureChannelNormalConvention.DirectX));
                Assert.That(compiled.output.platformOverrides, Has.Length.EqualTo(1));
                Assert.That(compiled.output.platformOverrides[0].platformName, Is.EqualTo("Standalone"));
                UMAMaterial.TextureChannelOutputSettings externalCopy = compiled.output;
                externalCopy.platformOverrides[0].platformName = "Changed";
                Assert.That(compiled.output.platformOverrides[0].platformName, Is.EqualTo("Standalone"),
                    "Capability output must not expose its internal platform array for mutation.");
            }
            finally
            {
                Object.DestroyImmediate(uma);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ConflictingMultiSemanticComponentFailsPreflight()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            Material material = new Material(shader);
            UMAMaterial uma = ScriptableObject.CreateInstance<UMAMaterial>();
            try
            {
                uma.material = material;
                UMAMaterial.MaterialChannel channel = Channel(UMAMaterial.ChannelType.DiffuseTexture, "_BaseMap");
                channel.textureChannelLayout = new UMAMaterial.TextureChannelLayout
                {
                    mode = UMAMaterial.TextureChannelLayoutMode.Custom,
                    red = UMAMaterial.TextureChannelUsage.Albedo,
                    green = UMAMaterial.TextureChannelUsage.Albedo,
                    blue = UMAMaterial.TextureChannelUsage.Albedo,
                    alpha = UMAMaterial.TextureChannelUsage.Opacity |
                            UMAMaterial.TextureChannelUsage.Smoothness
                };
                uma.channels = new[] { channel };

                TexturePaintMaterialCapabilityDescriptor descriptor =
                    TexturePaintMaterialCapabilityService.Compile(uma, material, null, true);
                Assert.That(descriptor.IsSupported, Is.False);
                Assert.That(descriptor.Diagnostics.Any(item => item.code == "CHN006"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(uma);
                Object.DestroyImmediate(material);
            }
        }

        private static UMAMaterial.MaterialChannel Channel(UMAMaterial.ChannelType type, string property)
        {
            return new UMAMaterial.MaterialChannel
            {
                channelType = type,
                textureFormat = RenderTextureFormat.ARGB32,
                materialPropertyName = property,
                sourceTextureName = property,
                DownSample = 1
            };
        }
    }
}
#endif
