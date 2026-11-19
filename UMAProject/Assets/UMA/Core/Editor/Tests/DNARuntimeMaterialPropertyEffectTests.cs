using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    /// <summary>
    /// A runtime material property effect also works as character DNA: it reports its own build type,
    /// writes the renderer's property block in the post-apply step, and an unset texture does not throw.
    /// </summary>
    public class DNARuntimeMaterialPropertyEffectTests
    {
        private const string FloatProperty = "_DNARuntimeMaterialTestFloat";
        private const string TextureProperty = "_DNARuntimeMaterialTestTexture";

        private GameObject avatarObject;
        private UMAData data;
        private SkinnedMeshRenderer renderer;
        private Material material;
        private Texture2D texture;
        private DNA dna;

        [SetUp]
        public void SetUp()
        {
            dna = ScriptableObject.CreateInstance<DNA>();
            avatarObject = new GameObject("DNARuntimeMaterialPropertyEffectTests");
            data = avatarObject.AddComponent<UMAData>();
            var rendererObject = new GameObject("Renderer");
            rendererObject.transform.SetParent(avatarObject.transform, false);
            renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
            material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            renderer.sharedMaterial = material;
            data.SetRenderers(new[] { renderer });
            texture = new Texture2D(1, 1);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(avatarObject);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(dna);
        }

        private static DNAEffect_RuntimeMaterialProperty FloatEffect()
        {
            return new DNAEffect_RuntimeMaterialProperty
            {
                propertyName = FloatProperty,
                parameterType = DNAEffect_RuntimeMaterialProperty.ParameterType.Float,
                zeroFloatValue = 2f,
                oneFloatValue = 6f,
                curve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                minMapping = 0f,
                maxMapping = 1f
            };
        }

        private float ReadFloat(string property)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, 0);
            return block.GetFloat(Shader.PropertyToID(property));
        }

        [Test]
        public void ReportsTheMaterialPropertyBuildType()
        {
            var effect = new DNAEffect_RuntimeMaterialProperty();

            Assert.AreEqual(DNAInstanceCollection.DNABuildType.MaterialProperty, effect.AreaEffect);
            Assert.AreNotEqual(0, (int)(DNAInstanceCollection.DNABuildType.RendererOnly & effect.AreaEffect),
                "It is set on the renderers, so it must be part of the pass that needs no build.");
            Assert.IsFalse(effect.RequiresExpressionBuild, "An expression player must still treat it as build-free.");
        }

        [Test]
        public void PostApplyWritesThePropertyBlock()
        {
            FloatEffect().PostApply(data, dna, 0.5f);

            Assert.AreEqual(4f, ReadFloat(FloatProperty), 0.0001f);
        }

        [Test]
        public void TheRendererOnlyPassOfADnaRunsIt()
        {
            dna.effects.Add(FloatEffect());

            // What a DNA change with no build, and the end of a build, both do.
            dna.PostApply(data, 1f, DNAInstanceCollection.DNABuildType.RendererOnly);

            Assert.AreEqual(6f, ReadFloat(FloatProperty), 0.0001f);
        }

        [Test]
        public void UnsetTextureDoesNotThrowAndLeavesOtherPropertiesAlone()
        {
            var block = new MaterialPropertyBlock();
            block.SetFloat(Shader.PropertyToID(FloatProperty), 3.5f);
            renderer.SetPropertyBlock(block, 0);
            var effect = new DNAEffect_RuntimeMaterialProperty
            {
                propertyName = TextureProperty,
                parameterType = DNAEffect_RuntimeMaterialProperty.ParameterType.Texture,
                curve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                minMapping = 0f,
                maxMapping = 1f
            };

            Assert.DoesNotThrow(() => effect.PostApply(data, dna, 0f));
            Assert.DoesNotThrow(() => effect.PostApply(data, dna, 1f));
            Assert.AreEqual(3.5f, ReadFloat(FloatProperty), 0.0001f);
        }

        [Test]
        public void SetTextureIsWritten()
        {
            var effect = new DNAEffect_RuntimeMaterialProperty
            {
                propertyName = TextureProperty,
                parameterType = DNAEffect_RuntimeMaterialProperty.ParameterType.Texture,
                oneTextureValue = texture,
                curve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                minMapping = 0f,
                maxMapping = 1f
            };

            effect.PostApply(data, dna, 1f);

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, 0);
            Assert.AreSame(texture, block.GetTexture(Shader.PropertyToID(TextureProperty)));
        }
    }
}
