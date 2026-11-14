#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public class UMARealisticSkinSSSTests
    {
        const string ShaderPath = "Assets/UMA/SRP/Shaders/Skin/UMA3SkinRealisticSSS.shader";
        const string ExamplePath = "Assets/UMA/SRP/Shaders/Skin/Examples/UMA3_SkinRealisticSSS_Natural";

        // Material colors pass through a color-space conversion, so they are compared with a tolerance.
        static void AssertColor(Color actual, Color expected)
        {
            float error = Mathf.Max(Mathf.Abs(actual.r - expected.r), Mathf.Abs(actual.g - expected.g),
                Mathf.Abs(actual.b - expected.b), Mathf.Abs(actual.a - expected.a));
            Assert.That(error, Is.LessThan(1e-6f), $"expected {expected.ToString("F9")}, was {actual.ToString("F9")}");
        }

        [Test]
        public void EveryRealisticSkinInputExistsWithTheSameTypeAndDefault()
        {
            var realistic = Shader.Find(UMARealisticSkinUtility.ShaderName);
            var sss = Shader.Find(UMARealisticSkinUtility.SSSShaderName);
            Assert.That(realistic, Is.Not.Null);
            Assert.That(sss, Is.Not.Null);
            for (int i = 0; i < realistic.GetPropertyCount(); i++)
            {
                string name = realistic.GetPropertyName(i);
                int index = sss.FindPropertyIndex(name);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), name);
                var type = realistic.GetPropertyType(i);
                Assert.That(sss.GetPropertyType(index), Is.EqualTo(type), name);
                if (type == ShaderPropertyType.Range)
                {
                    Assert.That(sss.GetPropertyRangeLimits(index), Is.EqualTo(realistic.GetPropertyRangeLimits(i)), name);
                    Assert.That(sss.GetPropertyDefaultFloatValue(index), Is.EqualTo(realistic.GetPropertyDefaultFloatValue(i)), name);
                }
                else if (type == ShaderPropertyType.Vector || type == ShaderPropertyType.Color)
                    Assert.That(sss.GetPropertyDefaultVectorValue(index), Is.EqualTo(realistic.GetPropertyDefaultVectorValue(i)), name);
            }
        }

        [Test]
        public void SwitchingAMaterialToTheSSSShaderKeepsItsInputs()
        {
            var realistic = Shader.Find(UMARealisticSkinUtility.ShaderName);
            var sss = Shader.Find(UMARealisticSkinUtility.SSSShaderName);
            var material = new Material(realistic);
            try
            {
                material.SetColor("_Base_Color", new Color(.4f, .2f, .1f));
                material.SetFloat("_ScatterRadiusMM", 3.5f);
                material.SetVector("_Smoothness_Remap", new Vector4(.12f, .62f, 0, 0));
                material.SetTexture("_MaskMap", Texture2D.grayTexture);
                material.shader = sss;
                AssertColor(material.GetColor("_Base_Color"), new Color(.4f, .2f, .1f));
                Assert.That(material.GetFloat("_ScatterRadiusMM"), Is.EqualTo(3.5f));
                Assert.That(material.GetVector("_Smoothness_Remap"), Is.EqualTo(new Vector4(.12f, .62f, 0, 0)));
                Assert.That(material.GetTexture("_MaskMap"), Is.EqualTo(Texture2D.grayTexture));
                Assert.That(material.renderQueue, Is.EqualTo(2000));
            }
            finally { Object.DestroyImmediate(material); }
        }

        [Test]
        public void SSSCopyPreservesInputsWithoutChangingSource()
        {
            var source = new Material(Shader.Find(UMARealisticSkinUtility.ShaderName));
            Material copy = null;
            try
            {
                source.SetColor("_SubsurfaceColor", new Color(.9f, .3f, .2f));
                source.SetFloat("_TransmissionStrength", .2f);
                source.SetTextureScale("_BaseMap", new Vector2(2, 3));
                string before = EditorJsonUtility.ToJson(source);
                copy = UMARealisticSkinUtility.CreateSSSMaterialCopy(source);
                Assert.That(EditorJsonUtility.ToJson(source), Is.EqualTo(before));
                Assert.That(copy.shader.name, Is.EqualTo(UMARealisticSkinUtility.SSSShaderName));
                Assert.That(copy.name, Is.EqualTo(source.name + "_RealisticSSS"));
                AssertColor(copy.GetColor("_SubsurfaceColor"), source.GetColor("_SubsurfaceColor"));
                Assert.That(copy.GetFloat("_TransmissionStrength"), Is.EqualTo(.2f));
                Assert.That(copy.GetTextureScale("_BaseMap"), Is.EqualTo(new Vector2(2, 3)));
                Assert.That(copy.shaderKeywords, Is.Empty);
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(copy); }
            var unrelated = new Material(Shader.Find("Hidden/InternalErrorShader"));
            try { Assert.Throws<ArgumentException>(() => UMARealisticSkinUtility.CreateSSSMaterialCopy(unrelated)); }
            finally { Object.DestroyImmediate(unrelated); }
            Assert.Throws<ArgumentNullException>(() => UMARealisticSkinUtility.CreateSSSMaterialCopy(null));
        }

        [Test]
        public void LookupTextureIsBoundToTheShaderAndImportedAsLinearData()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(UMASkinScatterLUTGenerator.LUTPath);
            Assert.That(texture, Is.Not.Null, UMASkinScatterLUTGenerator.LUTPath);
            Assert.That(texture.width, Is.EqualTo(UMASkinScatterLUTGenerator.Width));
            Assert.That(texture.height, Is.EqualTo(UMASkinScatterLUTGenerator.Height));
            Assert.That(texture.mipmapCount, Is.EqualTo(1));
            Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
            var textureImporter = (TextureImporter)AssetImporter.GetAtPath(UMASkinScatterLUTGenerator.LUTPath);
            Assert.That(textureImporter.sRGBTexture, Is.False);
            Assert.That(textureImporter.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            var shaderImporter = (ShaderImporter)AssetImporter.GetAtPath(ShaderPath);
            Assert.That(shaderImporter, Is.Not.Null, ShaderPath);
            Assert.That(shaderImporter.GetNonModifiableTexture("_SkinScatterLUT"), Is.EqualTo(texture));
            // The shader repeats the texture's size and marker as constants.
            string input = System.IO.File.ReadAllText("Assets/UMA/SRP/Shaders/Skin/UMA3SkinSSSInput.hlsl");
            StringAssert.Contains("#define SKIN_LOOKUP_SIZE float2(256.0, 64.0)", input);
            StringAssert.Contains("#define SKIN_LOOKUP_MARKER 0.25", input);
            Assert.That(UMASkinScatterLUTGenerator.Width, Is.EqualTo(256));
            Assert.That(UMASkinScatterLUTGenerator.Height, Is.EqualTo(64));
            Assert.That(UMASkinScatterLUTGenerator.Marker, Is.EqualTo(0.25f));
        }

        [Test]
        public void LookupValuesAreLambertWithoutScatteringAndConserveEnergyWithIt()
        {
            const int width = 33, height = 9;
            var pixels = UMASkinScatterLUTGenerator.Compute(width, height);
            for (int y = 0; y < height; y++)
            {
                double diffuseSum = 0;
                for (int x = 0; x < width; x++)
                {
                    var pixel = pixels[y * width + x];
                    float cosine = 2f * x / (width - 1) - 1f;
                    if (y == 0) Assert.That(pixel.r, Is.EqualTo(Mathf.Max(0, cosine)).Within(1e-5f), "row 0 is plain N.L");
                    Assert.That(pixel.r, Is.InRange(0f, 1f));
                    Assert.That(pixel.g, Is.InRange(0f, 1f));
                    Assert.That(pixel.b, Is.EqualTo(UMASkinScatterLUTGenerator.Marker));
                    if (x > 0)
                    {
                        Assert.That(pixel.r, Is.GreaterThanOrEqualTo(pixels[y * width + x - 1].r - 1e-5f), "diffuse rises with N.L");
                        Assert.That(pixel.g, Is.GreaterThanOrEqualTo(pixels[y * width + x - 1].g - 1e-5f), "shadow rises with attenuation");
                    }
                    // Trapezoid rule over N.L in [-1, 1].
                    diffuseSum += pixel.r * (x == 0 || x == width - 1 ? 0.5 : 1.0);
                }
                // Scattering moves light around the sphere; it must not add or remove any.
                // The mean of max(0, N.L) over a sphere is 1/4.
                Assert.That(diffuseSum / (width - 1), Is.EqualTo(0.25).Within(0.004), "row " + y);
                Assert.That(pixels[y * width].g, Is.EqualTo(0f), "full shadow stays dark");
                Assert.That(pixels[y * width + width - 1].g, Is.EqualTo(1f).Within(1e-5f), "full light stays lit");
            }
            // More scattering reaches further past the terminator and further into a shadow edge.
            int terminator = (width - 1) / 2, quarter = (width - 1) / 4;
            for (int y = 1; y < height; y++)
            {
                Assert.That(pixels[y * width + terminator].r, Is.GreaterThan(pixels[(y - 1) * width + terminator].r));
                Assert.That(pixels[y * width + quarter].g, Is.GreaterThan(pixels[(y - 1) * width + quarter].g));
            }
        }

        [Test]
        public void ExampleKeepsUmaAtlasChannelsAndReferences()
        {
            var original = AssetDatabase.LoadAssetAtPath<UMAMaterial>("Assets/UMA/SRP/Shaders/Skin/Examples/UMA3_SkinRealistic_Natural.asset");
            var uma = AssetDatabase.LoadAssetAtPath<UMAMaterial>(ExamplePath + ".asset");
            Assert.That(original, Is.Not.Null);
            Assert.That(uma, Is.Not.Null);
            Assert.That(uma.material, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Material>(ExamplePath + ".mat")));
            Assert.That(uma.material.shader.name, Is.EqualTo(UMARealisticSkinUtility.SSSShaderName));
            Assert.That(uma.ShaderName, Is.EqualTo(UMARealisticSkinUtility.SSSShaderName));
            Assert.That(uma.MaterialName, Is.EqualTo(uma.material.name));
            Assert.That(uma.channels.Length, Is.EqualTo(original.channels.Length));
            for (int i = 0; i < uma.channels.Length; i++)
                Assert.That(JsonUtility.ToJson(uma.channels[i]), Is.EqualTo(JsonUtility.ToJson(original.channels[i])));
            foreach (string property in new[] { "_BaseMap", "_BumpMap", "_MaskMap", "_Skinmask", "_DetailNormalMap" })
                Assert.That(uma.material.GetTexture(property), Is.EqualTo(original.material.GetTexture(property)), property);
            Assert.That(uma.materialType, Is.EqualTo(original.materialType));
            Assert.That(uma.material.GetFloat("_SkinDebug"), Is.Zero);
        }

        [Test]
        public void ShaderPassesAndLightingVariantsCompile()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Requires a graphics device: do not pass -nographics.");
            var previousDefault = GraphicsSettings.defaultRenderPipeline;
            var previousQuality = QualitySettings.renderPipeline;
            var pipeline = AssetDatabase.FindAssets("t:RenderPipelineAsset")
                .Select(g => AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(p => p != null && p.GetType().Name == "UniversalRenderPipelineAsset");
            if (pipeline == null) Assert.Ignore("Requires a URP pipeline asset.");
            var cameraObject = new GameObject("Skin SSS shader test camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>(); camera.cullingMask = 0;
            var target = new RenderTexture(16, 16, 24);
            Material material = null;
            var variants = new ShaderVariantCollection();
            bool oldAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;
                // Selecting the asset alone does not instantiate URP in a batch-mode test.
                // Without a render, FindPass resolves the built-in fallback instead.
                target.Create();
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                var shader = Shader.Find(UMARealisticSkinUtility.SSSShaderName);
                Assert.That(shader, Is.Not.Null);
                material = new Material(shader);
                foreach (var keywords in new[]{Array.Empty<string>(),
                    new[]{"_MAIN_LIGHT_SHADOWS","_ADDITIONAL_LIGHTS","_ADDITIONAL_LIGHT_SHADOWS","_SHADOWS_SOFT"},
                    new[]{"_MAIN_LIGHT_SHADOWS_CASCADE","_ADDITIONAL_LIGHTS","_ADDITIONAL_LIGHT_SHADOWS","_CLUSTER_LIGHT_LOOP","_SCREEN_SPACE_OCCLUSION","_LIGHT_LAYERS","_LIGHT_COOKIES"},
                    new[]{"_MAIN_LIGHT_SHADOWS_SCREEN","_ADDITIONAL_LIGHTS","_ADDITIONAL_LIGHT_SHADOWS","_DBUFFER_MRT3"},
                    new[]{"LIGHTMAP_ON","DIRLIGHTMAP_COMBINED","LIGHTMAP_SHADOW_MIXING","SHADOWS_SHADOWMASK"},
                    new[]{"DYNAMICLIGHTMAP_ON"},new[]{"PROBE_VOLUMES_L1"},new[]{"PROBE_VOLUMES_L2"},
                    new[]{"_REFLECTION_PROBE_BLENDING","_REFLECTION_PROBE_BOX_PROJECTION"},
                    new[]{"_ADDITIONAL_LIGHTS_VERTEX","FOG_EXP2","LOD_FADE_CROSSFADE"},
                    new[]{"INSTANCING_ON"}})
                    variants.Add(new ShaderVariantCollection.ShaderVariant(shader, PassType.ScriptableRenderPipeline, keywords));
                variants.Add(new ShaderVariantCollection.ShaderVariant(shader, PassType.ShadowCaster));
                variants.Add(new ShaderVariantCollection.ShaderVariant(shader, PassType.Meta));
                variants.WarmUp();
                foreach (string pass in new[] { "ForwardSkin", "ShadowCaster", "DepthOnly", "DepthNormals", "Meta", "MotionVectors", "XRMotionVectors" })
                {
                    int index = material.FindPass(pass);
                    Assert.That(index, Is.GreaterThanOrEqualTo(0), pass);
                    Assert.That(material.SetPass(index), Is.True, pass);
                }
                Assert.That(ShaderUtil.GetShaderMessages(shader).Where(m => m.severity.ToString() == "Error").Select(m => m.message), Is.Empty);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = oldAsync; Object.DestroyImmediate(material); Object.DestroyImmediate(variants);
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(cameraObject);
                GraphicsSettings.defaultRenderPipeline = previousDefault; QualitySettings.renderPipeline = previousQuality;
            }
        }
    }
}
#endif
