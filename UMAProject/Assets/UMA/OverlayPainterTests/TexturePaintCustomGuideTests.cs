#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintCustomGuideTests
    {
        private Material sourceMaterial;
        private UMAMaterial umaMaterial;
        private Texture2D sourceTexture;
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        [SetUp]
        public void SetUp()
        {
            sourceTexture = Own(new Texture2D(32, 32));
            sourceMaterial = Own(new Material(Shader.Find("Standard")));
            sourceMaterial.SetTexture("_MainTex", sourceTexture);
            umaMaterial = Own(ScriptableObject.CreateInstance<UMAMaterial>());
            umaMaterial.material = sourceMaterial;
            umaMaterial.channels = new[] { new UMAMaterial.MaterialChannel
            {
                channelType = UMAMaterial.ChannelType.DiffuseTexture,
                textureFormat = RenderTextureFormat.ARGB32,
                materialPropertyName = "_MainTex", sourceTextureName = "MainTex", DownSample = 1
            }};
        }
        [TearDown]
        public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }
        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(true, false)]
        public async Task MaterialWithoutCustomChannelSupportsPathGuidedScars(bool drawGuide, bool visible)
        {
            var mesh = Own(new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1, 0), Vector3.up },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            });
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var surface = new ReconstructedSurface
            {
                mesh = mesh, umaMaterial = umaMaterial, previewMaterial = sourceMaterial,
                triangleIslands = new[] { 0, 0 }, sourceTextures = new Texture[] { sourceTexture }
            };
            var reconstruction = new MeshReconstructionResult();
            reconstruction.surfaces.Add(surface);
            using var actualStore = new TextureStore();
            actualStore.Initialize(reconstruction, 128,
                AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/UMA/OverlayPainter/Shaders/LayerComposite.compute"));
            var actual = actualStore.Sets[0];
            actual.persistentId = "scar-guide-surface";
            var custom = actual.GetChannel(TexturePaintChannel.Custom);
            Assert.That(custom, Is.Not.Null, "Path guides must be available independently of shader channels.");
            Assert.That(custom.materialProperty, Is.Null);
            Assert.That(custom.umaChannelIndex, Is.EqualTo(-1));
            Assert.That(custom.sRGB, Is.False);
            Assert.That(actual.materialCapability.Channels.Count, Is.EqualTo(1),
                "The guide must not add a physical shader/export channel to the material descriptor.");
            Assert.That(umaMaterial.channels.Length, Is.EqualTo(1));
            Assert.That(custom.Texture.width, Is.EqualTo(actual.GetChannel(TexturePaintChannel.Albedo).Texture.width));
            Assert.That(custom.Texture.height, Is.EqualTo(actual.GetChannel(TexturePaintChannel.Albedo).Texture.height));
            int width = custom.Texture.width, height = custom.Texture.height;
            var guide = Own(new Texture2D(width, height, TextureFormat.RGBA32, false, true));
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = drawGuide && Mathf.Abs(x - width / 2) <= 1 ? Color.white : Color.clear;
            guide.SetPixels(pixels); guide.Apply();
            var path = actual.AddLayer("Scar path guide");
            path.kind = TexturePaintLayerKind.Spline;
            path.visible = visible;
            path.channels[TexturePaintChannel.Custom] = new EditableTextureTarget("Path guide", width, height,
                RenderTextureFormat.ARGB32, guide, Color.clear);
            actual.CompositeChannel(TexturePaintChannel.Custom);
            var plugin = (ITexturePaintGeneratorV2)Own(ScriptableObject.CreateInstance(Type.GetType(
                "UMA.TexturePaint.Examples.ScarWoundGeneratorPlugin, UMA.TexturePaint.Examples", true)));
            using var host = new PluginHost();
            var parameters = host.CreateParameters(plugin);
            parameters.Get("guideSource").number = 1;
            parameters.Get("scarWidth").number = 2;
            await host.ExecuteCommandAsync(plugin, actualStore, parameters, null, CancellationToken.None);
            var scar = actual.layers.Find(layer => layer.pluginId == plugin.Descriptor.id);
            if (!drawGuide || !visible)
            {
                Assert.That(scar, Is.Null, "An empty or hidden path must not generate a scar.");
                return;
            }
            Assert.That(scar, Is.Not.Null);
            Assert.That(scar.pluginId, Is.EqualTo(plugin.Descriptor.id));
            var output = scar.channels[TexturePaintChannel.Albedo].Front;
            var read = Own(new Texture2D(width, height, TextureFormat.RGBA32, false, true));
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = output;
                read.ReadPixels(new Rect(0, 0, width, height), 0, 0); read.Apply();
            }
            finally { RenderTexture.active = previous; }
            Assert.That(read.GetPixel(width / 2, height / 2).a,
                drawGuide && visible ? Is.GreaterThan(.01f) : Is.EqualTo(0f).Within(.001f));
            Assert.That(read.GetPixel(1, height / 2).a, Is.EqualTo(0f).Within(.001f),
                "The guide must localize the scar rather than generating across the material.");
        }


    }
}
#endif
