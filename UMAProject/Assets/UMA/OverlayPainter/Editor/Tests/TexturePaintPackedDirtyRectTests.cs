#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintPackedDirtyRectTests
    {
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
