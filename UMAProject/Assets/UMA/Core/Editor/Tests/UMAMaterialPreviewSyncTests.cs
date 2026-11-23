using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    // The UMAMaterial inspector's Material Preview edits the first-pass material. Edits must reach the second-pass material
    // where it has the property, without the settings that make the passes different, and a preview avatar's generated
    // materials of both passes must follow. Uses in-memory copies of the UMA 3 hair materials (two passes, one Shader Graph:
    // a cutout first pass and a transparent second pass); no asset is changed.
    public sealed class UMAMaterialPreviewSyncTests
    {
        private const string HairUMAMaterialPath = "Assets/UMA/SRP/ShaderGraphs/Materials/UMA3Hair.asset";

        private readonly List<Object> created = new List<Object>();
        private UMAMaterial hair;
        private Material pass1;
        private Material pass2;

        private T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        [SetUp]
        public void SetUp()
        {
            hair = AssetDatabase.LoadAssetAtPath<UMAMaterial>(HairUMAMaterialPath);
            if (hair == null || hair.material == null || hair.secondPass == null)
            {
                Assert.Ignore("The UMA 3 hair UMAMaterial with two passes is not installed (" + HairUMAMaterialPath + ").");
            }
            pass1 = Track(new Material(hair.material));
            pass2 = Track(new Material(hair.secondPass));
            foreach (string property in new[] { "_Smoothness", "_SpecularTint", "_AlphaModulation", "_Surface", "_ZWrite", "_SrcBlend", "_AlphaClip", "_CastShadows" })
            {
                if (!pass1.HasProperty(property) || !pass2.HasProperty(property))
                {
                    Assert.Ignore("The hair materials of the active render pipeline do not have " + property + ".");
                }
            }
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null)
                {
                    Object.DestroyImmediate(created[i]);
                }
            }
            created.Clear();
        }

        [Test]
        public void AnEditedPropertyIsCopiedToTheSecondPass()
        {
            var sync = new UMAMaterialPreviewSync();
            Assert.That(sync.MirrorChanges(pass1, pass2), Is.Zero, "The first call only records.");
            float ownAlphaModulation = pass2.GetFloat("_AlphaModulation");
            var tint = new Color(0.1f, 0.2f, 0.3f, 1f);

            pass1.SetFloat("_Smoothness", 0.83f);
            pass1.SetColor("_SpecularTint", tint);

            Assert.That(sync.MirrorChanges(pass1, pass2), Is.EqualTo(2));
            Assert.That(pass2.GetFloat("_Smoothness"), Is.EqualTo(0.83f).Within(1e-6f));
            Color copied = pass2.GetColor("_SpecularTint");
            Assert.That(new Vector4(copied.r, copied.g, copied.b, copied.a), Is.EqualTo(new Vector4(tint.r, tint.g, tint.b, tint.a)).Using<Vector4>((x, y) => Vector4.Distance(x, y) < 1e-5f ? 0 : 1));
            Assert.That(pass2.GetFloat("_AlphaModulation"), Is.EqualTo(ownAlphaModulation), "A property that was not edited keeps the second pass's own value.");
            Assert.That(sync.MirrorChanges(pass1, pass2), Is.Zero, "Nothing has changed since the last call.");
        }

        [Test]
        public void PassSetupPropertiesAreNotCopied()
        {
            var sync = new UMAMaterialPreviewSync();
            sync.MirrorChanges(pass1, pass2);
            float surface = pass2.GetFloat("_Surface");
            float zWrite = pass2.GetFloat("_ZWrite");
            float srcBlend = pass2.GetFloat("_SrcBlend");
            float alphaClip = pass2.GetFloat("_AlphaClip");
            float castShadows = pass2.GetFloat("_CastShadows");
            int queue = pass2.renderQueue;
            string[] keywords = pass2.shaderKeywords;

            pass1.SetFloat("_Surface", 1f - pass1.GetFloat("_Surface"));
            pass1.SetFloat("_ZWrite", 1f - pass1.GetFloat("_ZWrite"));
            pass1.SetFloat("_SrcBlend", pass1.GetFloat("_SrcBlend") + 1f);
            pass1.SetFloat("_AlphaClip", 1f - pass1.GetFloat("_AlphaClip"));
            pass1.SetFloat("_CastShadows", 1f - pass1.GetFloat("_CastShadows"));

            Assert.That(sync.MirrorChanges(pass1, pass2), Is.Zero);
            Assert.That(pass2.GetFloat("_Surface"), Is.EqualTo(surface));
            Assert.That(pass2.GetFloat("_ZWrite"), Is.EqualTo(zWrite));
            Assert.That(pass2.GetFloat("_SrcBlend"), Is.EqualTo(srcBlend));
            Assert.That(pass2.GetFloat("_AlphaClip"), Is.EqualTo(alphaClip));
            Assert.That(pass2.GetFloat("_CastShadows"), Is.EqualTo(castShadows));
            Assert.That(pass2.renderQueue, Is.EqualTo(queue));
            Assert.That(pass2.shaderKeywords, Is.EquivalentTo(keywords));

            Assert.That(UMAMaterialPreviewSync.IsPassSetupProperty("_StencilRefDepth"), Is.True);
            Assert.That(UMAMaterialPreviewSync.IsPassSetupProperty("_BUILTIN_Surface"), Is.True);
            Assert.That(UMAMaterialPreviewSync.IsPassSetupProperty("_CullModeForward"), Is.True);
            Assert.That(UMAMaterialPreviewSync.IsPassSetupProperty("_Smoothness"), Is.False);
        }

        [Test]
        public void ATextureIsCopiedWithItsScaleAndOffset()
        {
            string property = FirstSharedTextureProperty(pass1, pass2);
            if (property == null)
            {
                Assert.Ignore("The hair materials share no 2D texture property.");
            }
            var sync = new UMAMaterialPreviewSync();
            sync.MirrorChanges(pass1, pass2);
            var texture = Track(new Texture2D(2, 2));

            pass1.SetTexture(property, texture);
            pass1.SetTextureScale(property, new Vector2(2f, 3f));
            pass1.SetTextureOffset(property, new Vector2(0.5f, 0.25f));

            Assert.That(sync.MirrorChanges(pass1, pass2), Is.EqualTo(1));
            Assert.That(pass2.GetTexture(property), Is.SameAs(texture));
            Assert.That(pass2.GetTextureScale(property), Is.EqualTo(new Vector2(2f, 3f)));
            Assert.That(pass2.GetTextureOffset(property), Is.EqualTo(new Vector2(0.5f, 0.25f)));
        }

        [Test]
        public void APropertyTheSecondPassDoesNotHaveIsSkipped()
        {
            string only = null;
            for (int i = 0; i < pass1.shader.GetPropertyCount(); i++)
            {
                if (pass1.shader.GetPropertyType(i) == ShaderPropertyType.Float || pass1.shader.GetPropertyType(i) == ShaderPropertyType.Range)
                {
                    string name = pass1.shader.GetPropertyName(i);
                    if (!UMAMaterialPreviewSync.IsPassSetupProperty(name))
                    {
                        only = name;
                        break;
                    }
                }
            }
            Assert.That(only, Is.Not.Null);
            Shader other = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(other, Is.Not.Null);
            var otherPass = Track(new Material(other));
            Assert.That(otherPass.HasProperty(only), Is.False);
            var sync = new UMAMaterialPreviewSync();
            sync.MirrorChanges(pass1, otherPass);

            pass1.SetFloat(only, pass1.GetFloat(only) + 0.25f);

            Assert.That(sync.MirrorChanges(pass1, otherPass), Is.Zero);
        }

        [Test]
        public void ChangingTheShaderOnlyRecords()
        {
            var sync = new UMAMaterialPreviewSync();
            sync.MirrorChanges(pass1, pass2);
            float smoothness = pass2.GetFloat("_Smoothness");
            pass1.shader = Shader.Find("Hidden/InternalErrorShader");

            Assert.That(sync.MirrorChanges(pass1, pass2), Is.Zero);
            Assert.That(pass2.GetFloat("_Smoothness"), Is.EqualTo(smoothness));
        }

        [Test]
        public void AToggleCarriesItsKeyword()
        {
            Shader lit = Shader.Find("Shader Graphs/UMA3_Lit(Specular)");
            if (lit == null || lit.FindPropertyIndex("_EMISSION") < 0 || !lit.keywordSpace.FindKeyword("_EMISSION").isValid)
            {
                Assert.Ignore("The UMA3_Lit(Specular) Shader Graph with an _EMISSION toggle and keyword is not available.");
            }
            var first = Track(new Material(lit));
            var second = Track(new Material(lit));
            first.SetFloat("_EMISSION", 0f);
            first.DisableKeyword("_EMISSION");
            second.SetFloat("_EMISSION", 0f);
            second.DisableKeyword("_EMISSION");
            var sync = new UMAMaterialPreviewSync();
            sync.MirrorChanges(first, second);

            first.SetFloat("_EMISSION", 1f);
            first.EnableKeyword("_EMISSION");

            Assert.That(sync.MirrorChanges(first, second), Is.EqualTo(1));
            Assert.That(second.GetFloat("_EMISSION"), Is.EqualTo(1f));
            Assert.That(second.IsKeywordEnabled("_EMISSION"), Is.True);
        }

        [Test]
        public void GeneratedMaterialsOfBothPassesFollowTheirTemplatesAndKeepTheirChannelTextures()
        {
            var umaMaterial = Track(ScriptableObject.CreateInstance<UMAMaterial>());
            var serialized = new SerializedObject(umaMaterial);
            serialized.FindProperty("_material").objectReferenceValue = pass1;
            serialized.FindProperty("_secondPass").objectReferenceValue = pass2;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            umaMaterial.channels = hair.channels;
            string channelProperty = null;
            foreach (UMAMaterial.MaterialChannel channel in hair.channels)
            {
                if (pass1.HasProperty(channel.materialPropertyName) && pass2.HasProperty(channel.materialPropertyName))
                {
                    channelProperty = channel.materialPropertyName;
                    break;
                }
            }
            Assert.That(channelProperty, Is.Not.Null, "The hair UMAMaterial has a channel both passes use.");

            var generatedFirst = Track(new Material(pass1));
            var generatedSecond = Track(new Material(pass2));
            var built = Track(new Texture2D(2, 2));
            generatedFirst.SetTexture(channelProperty, built);
            generatedSecond.SetTexture(channelProperty, built);
            var generated = new List<UMAData.GeneratedMaterial>
            {
                new UMAData.GeneratedMaterial { umaMaterial = umaMaterial, material = generatedFirst, secondPassMaterial = generatedSecond },
            };

            pass1.SetFloat("_Smoothness", 0.71f);
            pass2.SetFloat("_Smoothness", 0.37f);

            Assert.That(UMAMaterialPreviewSync.SyncGeneratedMaterials(umaMaterial, generated), Is.EqualTo(2));
            Assert.That(generatedFirst.GetFloat("_Smoothness"), Is.EqualTo(0.71f).Within(1e-6f));
            Assert.That(generatedSecond.GetFloat("_Smoothness"), Is.EqualTo(0.37f).Within(1e-6f));
            Assert.That(generatedSecond.GetFloat("_Surface"), Is.EqualTo(pass2.GetFloat("_Surface")), "The second pass keeps its own surface type.");
            Assert.That(generatedFirst.GetTexture(channelProperty), Is.SameAs(built));
            Assert.That(generatedSecond.GetTexture(channelProperty), Is.SameAs(built));
        }

        private static string FirstSharedTextureProperty(Material first, Material second)
        {
            Shader shader = first.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture || shader.GetPropertyTextureDimension(i) != TextureDimension.Tex2D)
                {
                    continue;
                }
                string name = shader.GetPropertyName(i);
                if (!second.HasProperty(name))
                {
                    continue;
                }
                int index = second.shader.FindPropertyIndex(name);
                if (index >= 0 && second.shader.GetPropertyTextureDimension(index) == TextureDimension.Tex2D)
                {
                    return name;
                }
            }
            return null;
        }
    }
}
