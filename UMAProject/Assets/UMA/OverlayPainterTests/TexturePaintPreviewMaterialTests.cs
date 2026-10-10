#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintPreviewMaterialTests
    {
        [Test]
        public void PreviewParametersRoundTripWithoutCapturingOrReplacingLayerTextures()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var preview = new Material(shader);
            var restored = new Material(shader);
            var painted = new Texture2D(2, 2);
            try
            {
                source.SetFloat("_Smoothness", .324f);
                preview.CopyPropertiesFromMaterial(source);
                preview.SetFloat("_Smoothness", 1);
                preview.SetFloat("_Metallic", .8f);
                preview.SetColor("_BaseColor", new Color(.2f, .4f, .6f, .7f));
                preview.EnableKeyword("_EMISSION");
                preview.SetTexture("_MetallicGlossMap", Texture2D.whiteTexture);
                restored.SetTexture("_MetallicGlossMap", painted);
                var saved = new TexturePaintPreviewMaterialStates();
                saved.materials.Add(TexturePaintPreviewMaterialState.Capture(preview, "armor"));
                var workspace = new TexturePaintStageState { previewMaterialSettingsJson = JsonUtility.ToJson(saved) };
                workspace = JsonUtility.FromJson<TexturePaintStageState>(JsonUtility.ToJson(workspace));
                var settings = JsonUtility.FromJson<TexturePaintPreviewMaterialStates>(workspace.previewMaterialSettingsJson);
                Assert.That(settings.materials[0].surfaceId, Is.EqualTo("armor"));
                Assert.That(settings.materials[0].Apply(restored), Is.True);
                Assert.That(restored.GetFloat("_Smoothness"), Is.EqualTo(1));
                Assert.That(restored.GetFloat("_Metallic"), Is.EqualTo(.8f));
                Vector4 colorDifference = (Vector4)restored.GetColor("_BaseColor") - (Vector4)preview.GetColor("_BaseColor");
                Assert.That(colorDifference.sqrMagnitude, Is.LessThan(1e-10f));
                Assert.That(restored.IsKeywordEnabled("_EMISSION"), Is.True);
                Assert.That(restored.GetTexture("_MetallicGlossMap"), Is.SameAs(painted));
                Assert.That(source.GetFloat("_Smoothness"), Is.EqualTo(.324f));
                Assert.That(source.IsKeywordEnabled("_EMISSION"), Is.False);

                Assert.That(TexturePaintPreviewMaterialState.Capture(source, "armor").Apply(restored), Is.True);
                Assert.That(restored.GetFloat("_Smoothness"), Is.EqualTo(.324f));
                Assert.That(restored.GetTexture("_MetallicGlossMap"), Is.SameAs(painted));
            }
            finally
            {
                Object.DestroyImmediate(painted); Object.DestroyImmediate(restored);
                Object.DestroyImmediate(preview); Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void OverridesDoNotApplyToAnotherShader()
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            try
            {
                var state = TexturePaintPreviewMaterialState.Capture(material, "armor");
                state.shaderName = "Different shader";
                material.SetFloat("_Smoothness", .27f);
                Assert.That(state.Apply(material), Is.False);
                Assert.That(material.GetFloat("_Smoothness"), Is.EqualTo(.27f));
            }
            finally { Object.DestroyImmediate(material); }
        }

        [Test]
        public void SavedLayoutAcceptsDockedMaterialWindow()
        {
            var layout = new TexturePaintWorkspaceLayout.SavedLayout();
            var tabs = new TexturePaintWorkspaceLayout.LayoutNode { tabs = true, size = 1 };
            tabs.children.Add(new TexturePaintWorkspaceLayout.LayoutNode { className = nameof(TexturePaintDockWindow), size = 1 });
            tabs.children.Add(new TexturePaintWorkspaceLayout.LayoutNode { className = nameof(TexturePaintMaterialWindow), size = 1 });
            layout.windows.Add(new TexturePaintWorkspaceLayout.SavedWindow { position = new Rect(0, 0, 600, 800), view = tabs });
            Assert.DoesNotThrow(() => TexturePaintWorkspaceLayout.ValidateLayout(layout));
        }
    }
}
#endif
