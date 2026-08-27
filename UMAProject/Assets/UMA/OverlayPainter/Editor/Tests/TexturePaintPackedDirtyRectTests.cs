#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintPackedDirtyRectTests
    {
        [Test]
        public void SceneWorldPositionPreviewNormalizesToSurfaceBounds()
        {
            Color result = TexturePaintStageWindow.VisualizeInternalPixel(
                TexturePaintScenePreviewMode.WorldPosition,
                new Color(12f, 24f, 36f, 1f), new Vector3(10f, 20f, 30f),
                new Vector3(4f, 8f, 12f), 1f);

            Assert.That(result.r, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(result.g, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(result.b, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void SceneCurvaturePreviewDistinguishesConcaveFlatAndConvexPixels()
        {
            Color concave = TexturePaintStageWindow.VisualizeInternalPixel(
                TexturePaintScenePreviewMode.SignedCurvature, new Color(0f, 0f, 0f, 1f),
                Vector3.zero, Vector3.one, 1f);
            Color flat = TexturePaintStageWindow.VisualizeInternalPixel(
                TexturePaintScenePreviewMode.SignedCurvature, new Color(0.5f, 0f, 0f, 1f),
                Vector3.zero, Vector3.one, 1f);
            Color convex = TexturePaintStageWindow.VisualizeInternalPixel(
                TexturePaintScenePreviewMode.SignedCurvature, new Color(1f, 0f, 0f, 1f),
                Vector3.zero, Vector3.one, 1f);

            Assert.That(concave.r, Is.GreaterThan(concave.b));
            Assert.That(convex.b, Is.GreaterThan(convex.r));
            Assert.That(flat.r, Is.EqualTo(flat.b).Within(0.0001f));
        }

        [Test]
        public void SceneIdPreviewIsDeterministicAndRejectsUncoveredPixels()
        {
            Color source = new Color(17f, 3f, 5f, 1f);
            Color first = TexturePaintStageWindow.VisualizeInternalPixel(
                TexturePaintScenePreviewMode.SurfaceId, source, Vector3.zero, Vector3.one, 1f);
            Color second = TexturePaintStageWindow.VisualizeInternalPixel(
                TexturePaintScenePreviewMode.SurfaceId, source, Vector3.zero, Vector3.one, 1f);
            Color uncovered = TexturePaintStageWindow.VisualizeInternalPixel(
                TexturePaintScenePreviewMode.SurfaceId, new Color(17f, 3f, 5f, 0f),
                Vector3.zero, Vector3.one, 1f);

            Assert.That(first, Is.EqualTo(second));
            Assert.That(uncovered, Is.EqualTo(Color.black));
        }

        [Test]
        public void PackedChannelSkipsDirtyRectOutsideSmallPhysicalTexture()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            Material material = new Material(shader);
            TextureSet set = new TextureSet
            {
                previewMaterial = material,
                channelPackShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/ChannelPack.compute"))
            };
            TexturePhysicalChannelGroup group = new TexturePhysicalChannelGroup
            {
                materialProperty = "_MaskMap",
                source = Texture2D.whiteTexture,
                packed = EditableTextureTarget.Create("Small Packed Texture", 16, 16,
                    RenderTextureFormat.ARGB32)
            };
            set.physicalChannelGroups.Add(group.materialProperty, group);

            try
            {
                // Models a large brush dirty rect from another, larger channel. Its intersection
                // with this 16x16 packed texture is empty and must not dispatch (0, 0, 1).
                set.BindPreviewTextures(false, new RectInt(64, 64, 256, 256));
            }
            finally
            {
                set.Dispose();
                Object.DestroyImmediate(material);
            }
        }
    }
}
#endif
