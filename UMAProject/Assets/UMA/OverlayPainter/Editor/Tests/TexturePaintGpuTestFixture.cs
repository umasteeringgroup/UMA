#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    internal sealed class TexturePaintGpuTestFixture : IDisposable
    {
        public const int Size = 64;
        public readonly TextureSet set;
        public readonly EditableTextureTarget target;
        public readonly Mesh mesh;
        public readonly GameObject owner;

        public TexturePaintGpuTestFixture(Color clear, TexturePaintChannel channel = TexturePaintChannel.Albedo,
            RenderTextureFormat format = RenderTextureFormat.ARGBHalf, int size = Size)
        {
            mesh = new Mesh
            {
                name = "Texture Paint Golden Quad",
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                    new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f)
                },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward },
                tangents = new[]
                {
                    new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f),
                    new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f)
                },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            mesh.RecalculateBounds();
            owner = new GameObject("Texture Paint Golden Surface");
            set = new TextureSet
            {
                persistentId = "golden-surface",
                surface = new ReconstructedSurface
                {
                    index = 0,
                    gameObject = owner,
                    mesh = mesh,
                    slotName = "Body",
                    slotNames = new List<string> { "Body" },
                    triangleSlotNames = new[] { "Body", "Body" },
                    triangleIslands = new[] { 0, 0 }
                }
            };
            target = new EditableTextureTarget("Texture Paint Golden", size, size, format, null, clear);
            set.channels.Add(channel, new TextureChannelTarget
            {
                channel = channel,
                materialProperty = "_MainTex",
                format = format,
                sRGB = false,
                editable = target
            });
        }

        public static PaintingEngine CreateEngine()
        {
            RequireComputeShaders();
            ComputeShader stroke = LoadShader("StrokeRasterize.compute");
            ComputeShader blur = LoadShader("Blur.compute");
            ComputeShader normal = LoadShader("NormalTouchup.compute");
            return new PaintingEngine(stroke, blur, normal);
        }

        public static ComputeShader LoadShader(string filename)
        {
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/" + filename));
            Assert.That(shader, Is.Not.Null, "Missing production compute shader " + filename + ".");
            return shader;
        }

        public static void RequireComputeShaders()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("GPU golden-image tests require compute-shader support.");
        }

        public BrushPreset CreateBrush(float hardness = 0.4f, float flow = 0.75f,
            TexturePaintBlendMode blend = TexturePaintBlendMode.Normal, BrushPreset.Shape shape = BrushPreset.Shape.Circle)
        {
            BrushPreset brush = ScriptableObject.CreateInstance<BrushPreset>();
            brush.name = "Golden Brush";
            brush.hardness = hardness;
            brush.flow = flow;
            brush.blendMode = blend;
            brush.shape = shape;
            return brush;
        }

        public StrokeContext CreateContext(BrushPreset brush, TexturePaintTool tool, Color color,
            TexturePaintChannel channel = TexturePaintChannel.Albedo, float strength = 0.8f)
        {
            return new StrokeContext
            {
                textures = set,
                brush = brush,
                tool = tool,
                channel = channel,
                color = color,
                paintSource = TexturePaintBrushSource.Color,
                strength = strength,
                pressureAffectsFlow = true
            };
        }

        public static StrokeSample CenterSample(Vector2 previousUV = default)
        {
            StrokeSample sample = new StrokeSample(new Vector3(0.5f, 0.5f), Vector3.forward,
                new Vector2(0.5f, 0.5f), 0, 0)
            {
                slotName = "Body",
                uvIsland = 0,
                previousUV = previousUV == default ? new Vector2(0.5f, 0.5f) : previousUV,
                pressure = 1f,
                flowMultiplier = 1f
            };
            return sample;
        }

        public void SetPixels(Color[] pixels)
        {
            Assert.That(pixels, Has.Length.EqualTo(target.Width * target.Height));
            Texture2D texture = new Texture2D(target.Width, target.Height, TextureFormat.RGBAHalf, false, true);
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            Graphics.CopyTexture(texture, target.Front);
            Graphics.CopyTexture(texture, target.Back);
            UnityEngine.Object.DestroyImmediate(texture);
        }

        public Color[] ReadPixels() => ReadPixels(target.Front);

        public static Color[] ReadPixels(RenderTexture texture)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            Texture2D readback = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false, true);
            readback.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
            readback.Apply(false, false);
            Color[] pixels = readback.GetPixels();
            UnityEngine.Object.DestroyImmediate(readback);
            RenderTexture.active = previous;
            return pixels;
        }

        public static void AssertImage(string name, Color[] expected, Color[] actual,
            float maximumTolerance = 0.004f, float meanTolerance = 0.0005f)
        {
            Assert.That(actual, Has.Length.EqualTo(expected.Length));
            float maximum = 0f;
            double total = 0d;
            for (int i = 0; i < expected.Length; i++)
            {
                Color delta = expected[i] - actual[i];
                float error = Mathf.Max(Mathf.Abs(delta.r), Mathf.Abs(delta.g));
                error = Mathf.Max(error, Mathf.Abs(delta.b));
                error = Mathf.Max(error, Mathf.Abs(delta.a));
                maximum = Mathf.Max(maximum, error);
                total += error;
            }
            float mean = (float)(total / Math.Max(1, expected.Length));
            if (maximum > maximumTolerance || mean > meanTolerance)
                WriteFailureArtifacts(name, expected, actual);
            Assert.That(maximum, Is.LessThanOrEqualTo(maximumTolerance), name + " maximum pixel error");
            Assert.That(mean, Is.LessThanOrEqualTo(meanTolerance), name + " mean pixel error");
        }

        private static void WriteFailureArtifacts(string name, Color[] expected, Color[] actual)
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/TexturePaintGoldenFailures"));
            Directory.CreateDirectory(folder);
            string safe = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
            WritePng(Path.Combine(folder, safe + "-expected.png"), expected);
            WritePng(Path.Combine(folder, safe + "-actual.png"), actual);
            Color[] difference = new Color[expected.Length];
            for (int i = 0; i < difference.Length; i++)
            {
                Color delta = expected[i] - actual[i];
                difference[i] = new Color(Mathf.Abs(delta.r) * 8f, Mathf.Abs(delta.g) * 8f,
                    Mathf.Abs(delta.b) * 8f, 1f);
            }
            WritePng(Path.Combine(folder, safe + "-diff.png"), difference);
        }

        private static void WritePng(string path, Color[] pixels)
        {
            Texture2D image = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            image.SetPixels(pixels);
            image.Apply(false, false);
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }

        public void Dispose()
        {
            set.Dispose();
            UnityEngine.Object.DestroyImmediate(owner);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
#endif
