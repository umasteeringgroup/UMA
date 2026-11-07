#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        [Test]
        public void FriendlyHairListNamesTrackRenamesWithoutExposingIdsAsLabels()
        {
            var helper = HairGroomCommands.AddHelper(groom, HairHelperType.BraidRail, Vector3.zero);
            helper.name = "Braid around bun"; groom.Groups[0].name = "Gathered sweep";
            using var so = new SerializedObject(groom);
            Assert.That(HairNamedItemDrawer.DisplayName(so.FindProperty("groups").GetArrayElementAtIndex(0)), Is.EqualTo("Gathered sweep"));
            Assert.That(HairNamedItemDrawer.DisplayName(so.FindProperty("sharedHelpers").GetArrayElementAtIndex(0)), Is.EqualTo("Braid around bun"));
            Assert.That(HairNamedItemDrawer.DisplayName(so.FindProperty("lods").GetArrayElementAtIndex(0)), Is.EqualTo(groom.Lods[0].name));
            string id = groom.Groups[0].Id;
            var group = so.FindProperty("groups").GetArrayElementAtIndex(0);
            group.FindPropertyRelative("name").stringValue = "Side hair"; so.ApplyModifiedProperties();
            Assert.That(HairNamedItemDrawer.DisplayName(group), Is.EqualTo("Side hair")); Assert.That(groom.Groups[0].Id, Is.EqualTo(id));
        }

        [Test]
        public void WidthCurveSerializedEditingChangesMeshAndSupportsUndo()
        {
            var profile = groom.Groups[0].profile;
            profile.Configure(HairCardShape.Ribbon, .01f, .0015f, 13);
            profile.WidthAlongCard.keys = new[] { new Keyframe(0, .2f), new Keyframe(.06f, 1), new Keyframe(1, 0) };
            Assert.That(profile.EvaluateWidth(0), Is.EqualTo(.0032f).Within(1e-6f));
            Undo.ClearUndo(profile); Undo.IncrementCurrentGroup();
            using (var so = new SerializedObject(profile))
            {
                var property = so.FindProperty("widthAlongCard"); var curve = property.animationCurveValue;
                curve.MoveKey(0, new Keyframe(0, 1)); property.animationCurveValue = curve; so.ApplyModifiedProperties();
            }
            Assert.That(profile.EvaluateWidth(0), Is.EqualTo(.01f).Within(1e-6f));
            using (var mesh = HairCardMeshGenerator.Build(HairGroomEvaluator.Evaluate(groom))) Assert.That(mesh.vertexCount, Is.GreaterThan(0));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.That(profile.EvaluateWidth(0), Is.EqualTo(.0032f).Within(1e-6f));
            Undo.ClearUndo(profile);
        }

        [UnityTest]
        public IEnumerator GroomAndCardProfileInspectorsRenderNamedItemsWithoutMutation()
        {
            HairGroomCommands.AddHelper(groom, HairHelperType.BraidRail, Vector3.zero);
            var profile = groom.Groups[0].profile;
            var groomEditor = UnityEditor.Editor.CreateEditor(groom);
            var profileEditor = UnityEditor.Editor.CreateEditor(profile);
            var window = ScriptableObject.CreateInstance<HairNaturalInspectorTestWindow>();
            Assert.That(groomEditor, Is.TypeOf<HairGroomAssetEditor>()); Assert.That(profileEditor, Is.TypeOf<HairCardProfileEditor>());
            string before = EditorJsonUtility.ToJson(groom), profileBefore = EditorJsonUtility.ToJson(profile);
            try
            {
                foreach (string list in new[] { "groups", "sharedHelpers", "lods" })
                {
                    var property = groomEditor.serializedObject.FindProperty(list); property.isExpanded = true;
                    if (property.arraySize > 0) property.GetArrayElementAtIndex(0).isExpanded = true;
                }
                window.Show();
                foreach (float width in new[] { 360f, 800f })
                    foreach (var editor in new[] { groomEditor, profileEditor })
                    {
                        window.draw = editor.OnInspectorGUI; window.position = new Rect(20, 20, width, 900); window.Repaint();
                        yield return null; yield return null; LogAssert.NoUnexpectedReceived();
                    }
                Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before));
                Assert.That(EditorJsonUtility.ToJson(profile), Is.EqualTo(profileBefore));
            }
            finally { window.Close(); Object.DestroyImmediate(groomEditor); Object.DestroyImmediate(profileEditor); }
        }

        [Test]
        public void HybridPreviewConfiguresLegacyCoreWithoutMutatingMaterialsAndSyncPersistsIt()
        {
            var atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>(); atlas.EnsureIntegrity(); groom.Groups[0].atlas = atlas;
            var core = new Material(Shader.Find(HairSweptShaderGUI.ShaderName));
            var fringe = new Material(Shader.Find(HairSweptShaderGUI.SoftShaderName));
            atlas.material = core; atlas.secondPassMaterial = fringe;
            using var preview = new HairPreviewMaterialSet();
            try
            {
                using var build = HairCardMeshGenerator.Build(HairGroomEvaluator.Evaluate(groom));
                var result = preview.Update(build);
                Assert.That(result[0].GetFloat("_HybridCore"), Is.EqualTo(1)); Assert.That(core.GetFloat("_HybridCore"), Is.Zero);
                Assert.That(result[^1].GetFloat("_HybridCore"), Is.Zero);
                var report = new HairValidationReport(); HairBakePipeline.ValidateUmaMaterialPasses(groom, report);
                Assert.That(report.WarningCount, Is.GreaterThan(0), "Legacy exported materials need the explicit sync action; preview must not silently dirty them.");
                Undo.IncrementCurrentGroup(); HairSweptShaderGUI.SynchronizeHybrid(atlas);
                Assert.That(core.GetFloat("_HybridCore"), Is.EqualTo(1)); Assert.That(fringe.GetFloat("_HybridCore"), Is.Zero);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.That(core.GetFloat("_HybridCore"), Is.Zero);
                Undo.ClearUndo(core); Undo.ClearUndo(fringe);
            }
            finally { groom.Groups[0].atlas = null; Object.DestroyImmediate(atlas); Object.DestroyImmediate(core); Object.DestroyImmediate(fringe); }
        }

        [TestCase(false, 1)] [TestCase(false, 4)] [TestCase(true, 1)] [TestCase(true, 4)]
        public void RootTipRgbaAndHybridFadeRenderContinuously(bool hybrid, int samples)
        {
            bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            var core = new Material(Shader.Find(HairSweptShaderGUI.ShaderName));
            var soft = new Material(Shader.Find(HairSweptShaderGUI.SoftShaderName));
            var quad = new Mesh { vertices = new[] { new Vector3(-1,-1,-1), new Vector3(-1,1,-1), new Vector3(1,1,-1), new Vector3(1,-1,-1) } };
            quad.normals = Enumerable.Repeat(Vector3.back, 4).ToArray(); quad.tangents = Enumerable.Repeat(new Vector4(0,1,0,1), 4).ToArray();
            quad.uv = new[] { Vector2.one, Vector2.right, Vector2.zero, Vector2.up }; // Deliberately reversed atlas UVs.
            quad.colors = Enumerable.Repeat(Color.white, 4).ToArray(); quad.triangles = new[] { 0,1,2,0,2,3 };
            var rt = new RenderTexture(32,32,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear) { antiAliasing = samples }; rt.Create();
            var pixels = new Texture2D(32,32,TextureFormat.RGBA32,false,true); var old = RenderTexture.active;
            try
            {
                foreach (var material in new[] { core, soft })
                {
                    material.SetFloat("_DebugView", 1); material.SetFloat("_AlphaToCoverage", 0); material.SetFloat("_Cutoff", .05f);
                    material.SetFloat("_RootFade", 1); material.SetFloat("_ColorPower", 1); material.SetFloat("_AlphaDensity", 4);
                    material.SetColor("_RootColor", new Color(1,1,1,.12f)); material.SetColor("_TipColor", new Color(1,1,1,.82f));
                }
                core.SetFloat("_HybridCore", 1);
                float Draw(float along, int corePass = 0, bool drawSoft = true)
                {
                    quad.uv2 = Enumerable.Repeat(new Vector2(along, .3f), 4).ToArray();
                    using var commands = new CommandBuffer(); commands.SetRenderTarget(rt); commands.ClearRenderTarget(true,true,Color.clear);
                    commands.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-1,1,-1,1,.01f,10),true));
                    if (hybrid) commands.DrawMesh(quad,Matrix4x4.identity,core,0,corePass);
                    if (drawSoft) commands.DrawMesh(quad,Matrix4x4.identity,soft,0,0);
                    Graphics.ExecuteCommandBuffer(commands); RenderTexture.active = rt; pixels.ReadPixels(new Rect(0,0,32,32),0,0); pixels.Apply();
                    return pixels.GetPixel(16,16).a;
                }
                foreach (float t in new[] { 0f, .1f, .25f, .5f, .75f, 1f })
                    Assert.That(Draw(t), Is.EqualTo(Mathf.Lerp(.12f,.82f,t)).Within(.02f), "Root/tip alpha must blend, even above the core's cutoff, without density boosting opacity.");
                foreach (var material in new[] { core, soft }) { material.SetColor("_RootColor",Color.white); material.SetColor("_TipColor",Color.white); material.SetFloat("_RootOpacityFade",.1f); }
                Assert.That(Draw(0), Is.Zero.Within(.01f)); Assert.That(Draw(.05f), Is.EqualTo(.5f).Within(.025f)); Assert.That(Draw(.2f), Is.EqualTo(1f).Within(.01f));
                if (hybrid) // Depth normals must not restore a solid root silhouette either.
                    Assert.That(Draw(.05f, core.FindPass("DepthNormals"), false), Is.Zero.Within(.01f));
                foreach (var material in new[] { core, soft }) { material.SetFloat("_RootOpacityFade",0); material.SetFloat("_Coverage",.3f); }
                Assert.That(Draw(.5f), Is.EqualTo(.3f).Within(.025f));
                quad.colors = Enumerable.Repeat(new Color(1,1,1,.5f),4).ToArray();
                foreach (var material in new[] { core, soft }) { material.SetFloat("_VertexAlphaInfluence",1); material.SetColor("_BaseColor",new Color(1,1,1,.5f)); }
                Assert.That(Draw(.5f), Is.EqualTo(.075f).Within(.02f), "Base, vertex and strand alpha each multiply once.");
                foreach (var shader in new[] { core.shader, soft.shader }) Assert.That(ShaderUtil.GetShaderMessages(shader).Where(m => m.severity.ToString() == "Error"), Is.Empty);
            }
            finally { RenderTexture.active = old; ShaderUtil.allowAsyncCompilation = async; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(pixels); Object.DestroyImmediate(quad); Object.DestroyImmediate(core); Object.DestroyImmediate(soft); }
        }
    }
}
#endif
