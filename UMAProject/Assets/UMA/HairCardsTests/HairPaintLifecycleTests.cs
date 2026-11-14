#if UNITY_INCLUDE_TESTS
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        [TestCase(false)] [TestCase(true)]
        public void PaintReleaseRebuildAndCursorExitKeepMapAndOverlay(bool texture)
        {
            var map = texture ? TextureTestMap(32) : groom.Groups[0].FindMap(HairMapKind.GrowthArea);
            map.values = new[] { .2f, .2f, .2f };
            var stage = CreateEditingStage();
            var surface = new GameObject("Paint lifecycle surface");
            var overlay = new GameObject("Paint lifecycle overlay");
            var overlayMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
            var filter = surface.AddComponent<MeshFilter>(); filter.sharedMesh = sourceMesh;
            SetStageField(stage, "scalpFilter", filter);
            SetStageField(stage, "growthOverlayFilter", overlay.AddComponent<MeshFilter>());
            SetStageField(stage, "growthOverlayRenderer", overlay.AddComponent<MeshRenderer>());
            SetStageField(stage, "growthOverlayMaterial", overlayMaterial);
            SetStageField(stage, "raycastSurfaceMesh", sourceMesh);
            SetStageField(stage, "vertexSpatialIndex", new HairVertexSpatialIndex(sourceMesh));
            try
            {
                stage.SceneTool = HairSceneTool.PaintGrowth; stage.BrushRadius = .25f;
                stage.BrushHardness = 1; stage.BrushStrength = 1; stage.PaintValue = 1; stage.MirrorPaintX = false;
                stage.RebuildNow();
                var center = texture ? Vector3.zero : sourceMesh.vertices[0];
                InvokeStageMethod(stage, "BeginStroke", "Paint lifecycle regression");
                InvokeStageMethod(stage, "PaintMapAt", center);
                string painted = JsonUtility.ToJson(map);
                InvokeStageMethod(stage, "ReleaseSceneInputCapture", true);
                for (int frame = 0; frame < 3; frame++)
                {
                    stage.RebuildNow();
                    InvokeStageMethod(stage, "ReleaseSceneInputCapture", true); // Leaving after mouse release.
                    Assert.That(JsonUtility.ToJson(stage.ActiveMap), Is.EqualTo(painted));
                    Assert.That(overlay.GetComponent<MeshRenderer>().enabled, Is.True);
                    if (texture)
                    {
                        var preview = (HairTextureMapPreview)typeof(HairCardStage).GetField("textureMapPreview", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stage);
                        Assert.That(preview.Texture.GetPixel(8,16).r, Is.EqualTo(1));
                        AssertPaintOverlayIsOrange(preview);
                        // DontSave textures are intentionally omitted by editor
                        // serialization. Restore the existing material as Unity does,
                        // then recover on idle without another stroke or any Undo.
                        int revision = map.TextureRevision;
                        EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(preview.Material), preview.Material);
                        Assert.That(preview.NeedsRefresh(map), Is.True);
                        InvokeStageMethod(stage, "EditorUpdate");
                        Assert.That(preview.NeedsRefresh(map), Is.False);
                        Assert.That(map.TextureRevision, Is.EqualTo(revision));
                        Assert.That(JsonUtility.ToJson(stage.ActiveMap), Is.EqualTo(painted));
                        AssertPaintOverlayIsOrange(preview);
                        Assert.That(preview.Material.GetTexture("_Map"), Is.SameAs(preview.Texture));
                    }
                    else Assert.That(overlay.GetComponent<MeshFilter>().sharedMesh.colors32[0].r, Is.GreaterThan(200));
                }
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert.That(stage.ActiveMap.SampleVertex(0), Is.EqualTo(.2f).Within(1e-6));
                Undo.PerformRedo(); Assert.That(JsonUtility.ToJson(stage.ActiveMap), Is.EqualTo(painted));
            }
            finally
            {
                var preview = (HairTextureMapPreview)typeof(HairCardStage).GetField("textureMapPreview", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stage);
                preview?.Dispose();
                var mesh = (Mesh)typeof(HairCardStage).GetField("growthOverlayMesh", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stage);
                if (mesh != null) Object.DestroyImmediate(mesh);
                DestroyEditingStage(stage); Object.DestroyImmediate(surface); Object.DestroyImmediate(overlay); Object.DestroyImmediate(overlayMaterial);
            }
        }

        [TestCase("Material")] [TestCase("Texture")] [TestCase("Mesh")] [TestCase("Parameters")]
        public void TexturePreviewRepairsLostResourcesWithoutChangingPaint(string lost)
        {
            var map = TextureTestMap(32);
            Array.Fill(map.texture.EnsureTile(map,0,0,0,1,2).pixels, 1f); map.texture.Touch();
            string painted = JsonUtility.ToJson(map); int revision = map.TextureRevision;
            using var preview = new HairTextureMapPreview();
            preview.Update(sourceMesh, sourceMesh, map, _ => true, false);
            Assert.That(preview.NeedsRefresh(map), Is.False);
            switch (lost)
            {
                case "Material": Object.DestroyImmediate(preview.Material); break;
                case "Texture": Object.DestroyImmediate(preview.Texture); break;
                case "Mesh": Object.DestroyImmediate(preview.Mesh); break;
                default:
                    preview.Material.SetFloat("_InvAtlasSize", 0); preview.Material.SetFloat("_Resolution", 0);
                    preview.Material.SetVector("_Range", Vector4.zero); break;
            }
            Assert.That(preview.NeedsRefresh(map), Is.True);
            preview.Update(sourceMesh, sourceMesh, map, _ => true, false);
            Assert.That(preview.NeedsRefresh(map), Is.False);
            Assert.That(map.TextureRevision, Is.EqualTo(revision)); Assert.That(JsonUtility.ToJson(map), Is.EqualTo(painted));
            AssertPaintOverlayIsOrange(preview);
        }

        private static void AssertPaintOverlayIsOrange(HairTextureMapPreview preview)
        {
            bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            var target = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(32, 32, TextureFormat.RGBA32, false, true);
            var previous = RenderTexture.active;
            try
            {
                target.Create();
                using var commands = new CommandBuffer();
                commands.SetRenderTarget(target); commands.ClearRenderTarget(true, true, Color.clear);
                commands.SetViewProjectionMatrices(Matrix4x4.identity, GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-1,1,-1,1,.01f,10), true));
                commands.DrawMesh(preview.Mesh, Matrix4x4.TRS(Vector3.back, Quaternion.Euler(90,0,0), Vector3.one), preview.Material);
                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active = target; readback.ReadPixels(new Rect(0,0,32,32),0,0); readback.Apply();
                var color = readback.GetPixel(16,16);
                Assert.That(color.r, Is.GreaterThan(color.b * 2), "The painted texture interior must render orange, not revert to the blue fallback.");
            }
            finally { RenderTexture.active = previous; ShaderUtil.allowAsyncCompilation = async; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(readback); }
        }
    }
}
#endif
