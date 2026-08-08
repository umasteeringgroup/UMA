#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintNormalControlTests
    {
        private const int Size = 16;
        private TextureSet set;

        [SetUp]
        public void SetUp()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            set = new TextureSet
            {
                persistentId = "normal-control-test",
                surface = new ReconstructedSurface { index = 0 },
                channelPackShader = TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute")
            };
            AddChannel(TexturePaintChannel.Normal, new Color(0.5f, 0.5f, 1f, 1f),
                RenderTextureFormat.ARGBHalf);
            AddChannel(TexturePaintChannel.NormalControl, new Color(0.5f, 0.5f, 0.5f, 1f),
                RenderTextureFormat.ARGBHalf);
        }

        [TearDown]
        public void TearDown()
        {
            set?.Dispose();
            set = null;
            TexturePaintSpriteSource.ClearCache();
        }

        [Test]
        public void NeutralControlPreservesComposedNormal()
        {
            Color encoded = Encode(new Vector3(0.24f, -0.31f, 0.92f).normalized);
            Fill(set.GetChannel(TexturePaintChannel.Normal).composite, encoded);
            Fill(set.GetChannel(TexturePaintChannel.NormalControl).composite,
                new Color(0.5f, 0.5f, 0.5f, 1f));

            set.BindPreviewTextures(false);

            Color actual = Read(set.GetVisibleTexture(TexturePaintChannel.Normal), Size / 2, Size / 2);
            Assert.That(actual.r, Is.EqualTo(encoded.r).Within(0.003f));
            Assert.That(actual.g, Is.EqualTo(encoded.g).Within(0.003f));
            Assert.That(actual.b, Is.EqualTo(encoded.b).Within(0.003f));
        }

        [TestCase(0f, 1)]
        [TestCase(1f, -1)]
        public void LineHeightProducesExpectedRecessedOrRaisedSlopes(float lineValue,
            int expectedLeftDirection)
        {
            Fill(set.GetChannel(TexturePaintChannel.Normal).composite,
                new Color(0.5f, 0.5f, 1f, 1f));
            Color[] control = Solid(new Color(0.5f, 0.5f, 0.5f, 1f));
            for (int y = 0; y < Size; y++)
                control[y * Size + Size / 2] = new Color(lineValue, lineValue, lineValue, 1f);
            Write(set.GetChannel(TexturePaintChannel.NormalControl).composite, control);

            set.normalControlStrength = 4f;
            set.normalControlRadius = 1;
            set.BindPreviewTextures(false);

            RenderTexture normal = set.GetVisibleTexture(TexturePaintChannel.Normal);
            Color left = Read(normal, Size / 2 - 1, Size / 2);
            Color right = Read(normal, Size / 2 + 1, Size / 2);
            Color center = Read(normal, Size / 2, Size / 2);
            Assert.That(Mathf.Sign(left.r - 0.5f), Is.EqualTo(expectedLeftDirection));
            Assert.That(Mathf.Sign(right.r - 0.5f), Is.EqualTo(-expectedLeftDirection));
            Assert.That(center.r, Is.EqualTo(0.5f).Within(0.003f));
        }

        [Test]
        public void DerivedNormalsRemainUnitLengthOverExistingDetail()
        {
            Fill(set.GetChannel(TexturePaintChannel.Normal).composite,
                Encode(new Vector3(0.35f, 0.1f, 0.93f).normalized));
            Color[] control = Solid(new Color(0.5f, 0.5f, 0.5f, 1f));
            for (int y = 0; y < Size; y++)
            for (int x = Size / 2; x < Size; x++)
                control[y * Size + x] = Color.white;
            Write(set.GetChannel(TexturePaintChannel.NormalControl).composite, control);

            set.BindPreviewTextures(false);

            Color encoded = Read(set.GetVisibleTexture(TexturePaintChannel.Normal),
                Size / 2 - 1, Size / 2);
            Vector3 normal = new Vector3(encoded.r * 2f - 1f, encoded.g * 2f - 1f,
                encoded.b * 2f - 1f);
            Assert.That(normal.magnitude, Is.EqualTo(1f).Within(0.006f));
            Assert.That(Mathf.Abs(normal.x - 0.35f), Is.GreaterThan(0.01f));
        }

        [Test]
        public void DirtyNormalControlUpdateRepacksGeneratedSlopeHalo()
        {
            TextureChannelTarget normalTarget = set.GetChannel(TexturePaintChannel.Normal);
            TexturePhysicalChannelGroup physical = new TexturePhysicalChannelGroup
            {
                materialProperty = "_PackedNormal",
                packed = EditableTextureTarget.Create("Packed Normal Control Test", Size, Size,
                    RenderTextureFormat.ARGBHalf)
            };
            physical.componentTargets[0] = normalTarget;
            physical.componentTargets[1] = normalTarget;
            physical.componentTargets[2] = normalTarget;
            physical.sourceComponents[0] = 0;
            physical.sourceComponents[1] = 1;
            physical.sourceComponents[2] = 2;
            set.physicalChannelGroups.Add(physical.materialProperty, physical);

            Fill(normalTarget.composite, new Color(0.5f, 0.5f, 1f, 1f));
            Fill(set.GetChannel(TexturePaintChannel.NormalControl).composite,
                new Color(0.5f, 0.5f, 0.5f, 1f));
            set.normalControlStrength = 4f;
            set.BindPreviewTextures(false);
            Assert.That(Read(physical.packed, Size / 2 - 1, Size / 2).r,
                Is.EqualTo(0.5f).Within(0.003f));

            Color[] control = Solid(new Color(0.5f, 0.5f, 0.5f, 1f));
            for (int y = 0; y < Size; y++)
                control[y * Size + Size / 2] = Color.white;
            Write(set.GetChannel(TexturePaintChannel.NormalControl).composite, control);
            set.CompositeChannel(TexturePaintChannel.NormalControl,
                new RectInt(Size / 2, 0, 1, Size));
            set.BindPreviewTextures(false, new RectInt(Size / 2, 0, 1, Size));

            Assert.That(Read(physical.packed, Size / 2 - 1, Size / 2).r, Is.LessThan(0.48f),
                "The physical map must include the generated slope just outside the painted rect.");
        }

        [Test]
        public void NormalControlTextureSourcesAreConvertedToGrayscale()
        {
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                source.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
                source.Apply(false, false);
                Texture2D converted = TexturePaintSpriteSource.Resolve(source, null,
                    TexturePaintChannel.NormalControl, TexturePaintNormalConvention.OpenGL);
                Assert.That(converted, Is.Not.Null);
                for (int i = 0; i < 4; i++)
                {
                    Color value = converted.GetPixel(i % 2, i / 2);
                    Assert.That(value.r, Is.EqualTo(value.g).Within(0.004f));
                    Assert.That(value.g, Is.EqualTo(value.b).Within(0.004f));
                }
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void NormalControlIsClassifiedAsGrayscaleAuxiliaryData()
        {
            Assert.That(TexturePaintChannelUtility.DisplayName(TexturePaintChannel.NormalControl),
                Is.EqualTo("Normal Control"));
            Assert.That(TexturePaintChannelUtility.IsGrayscale(TexturePaintChannel.NormalControl), Is.True);
            Assert.That(TexturePaintChannelUtility.IsAuxiliary(TexturePaintChannel.NormalControl), Is.True);
            Assert.That(TexturePaintChannelUtility.IsColor(TexturePaintChannel.NormalControl), Is.False);
            Assert.That(TexturePaintChannelUtility.IsVector(TexturePaintChannel.NormalControl), Is.False);
            Assert.That(TextureSet.DefaultColor(TexturePaintChannel.NormalControl),
                Is.EqualTo(new Color(0.5f, 0.5f, 0.5f, 1f)));
        }

        [Test]
        public void AutomaticChannelMatchesNormalAndHasNoMaterialBinding()
        {
            TextureChannelTarget oldControl = set.GetChannel(TexturePaintChannel.NormalControl);
            oldControl.Dispose();
            set.channels.Remove(TexturePaintChannel.NormalControl);
            TextureStore store = new TextureStore();
            MethodInfo ensure = typeof(TextureStore).GetMethod("EnsureNormalControlChannel",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(ensure, Is.Not.Null);
            ensure.Invoke(store, new object[] { set });

            TextureChannelTarget normal = set.GetChannel(TexturePaintChannel.Normal);
            TextureChannelTarget control = set.GetChannel(TexturePaintChannel.NormalControl);
            Assert.That(control, Is.Not.Null);
            Assert.That(control.Texture.width, Is.EqualTo(normal.Texture.width));
            Assert.That(control.Texture.height, Is.EqualTo(normal.Texture.height));
            Assert.That(control.materialProperty, Is.Null);
            Assert.That(control.sourceKeyword, Is.Null);
            Assert.That(control.umaChannelIndex, Is.EqualTo(-1));
            Assert.That(control.sRGB, Is.False);
            RenderTextureFormat expected = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                ? RenderTextureFormat.ARGBHalf
                : RenderTextureFormat.ARGB32;
            Assert.That(control.format, Is.EqualTo(expected));
        }

        [Test]
        public void PreNormalControlDocumentsMigrateToNeutralConversionSettings()
        {
            TexturePaintDocument document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            try
            {
                document.schemaVersion = 19;
                document.surfaces.Add(new TexturePaintDocumentSurface
                {
                    normalControlStrength = 0f,
                    normalControlRadius = 0,
                    normalControlInvert = true
                });

                document.Migrate();

                Assert.That(document.schemaVersion, Is.EqualTo(TexturePaintDocument.CurrentSchemaVersion));
                Assert.That(document.surfaces[0].normalControlStrength, Is.EqualTo(2f));
                Assert.That(document.surfaces[0].normalControlRadius, Is.EqualTo(1));
                Assert.That(document.surfaces[0].normalControlInvert, Is.False);
            }
            finally { Object.DestroyImmediate(document); }
        }

        private void AddChannel(TexturePaintChannel channel, Color clear, RenderTextureFormat format)
        {
            EditableTextureTarget editable = new EditableTextureTarget("Normal Control " + channel,
                Size, Size, format, null, clear);
            TextureChannelTarget target = new TextureChannelTarget
            {
                channel = channel,
                format = format,
                editable = editable,
                composite = EditableTextureTarget.Create("Normal Control " + channel + " Composite",
                    Size, Size, format)
            };
            Fill(target.composite, clear);
            set.channels.Add(channel, target);
        }

        private static Color Encode(Vector3 normal)
            => new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f,
                normal.z * 0.5f + 0.5f, 1f);

        private static Color[] Solid(Color color)
        {
            Color[] values = new Color[Size * Size];
            for (int i = 0; i < values.Length; i++) values[i] = color;
            return values;
        }

        private static void Fill(RenderTexture target, Color color) => Write(target, Solid(color));

        private static void Write(RenderTexture target, Color[] pixels)
        {
            Texture2D source = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false, true);
            try
            {
                source.SetPixels(pixels);
                source.Apply(false, false);
                Graphics.Blit(source, target);
            }
            finally { Object.DestroyImmediate(source); }
        }

        private static Color Read(RenderTexture source, int x, int y)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D readable = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false, true);
            try
            {
                RenderTexture.active = source;
                readable.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
                readable.Apply(false, false);
                return readable.GetPixel(x, y);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(readable);
            }
        }
    }
}
#endif
