#if UNITY_EDITOR
using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UMA.Tests
{
    /// <summary>
    /// Force GPU Texture Compression: the encoders reproduce their source, and generated atlases
    /// come out compressed through each transfer route, or untouched where it does not apply.
    /// </summary>
    public class UMAGpuTextureCompressionTests
    {
        private const int Size = 64;

        [TearDown]
        public void RestoreMode()
        {
            UMAGpuTextureCompressor.ModeOverride = null;
        }

        private static void RequireCompression()
        {
            if (!UMAGpuTextureCompressor.IsAvailable) Assert.Ignore("GPU texture compression unavailable on this test device.");
        }

        private static void RequireDirectCopy()
        {
            RequireCompression();
            if (UMAGpuTextureCompressor.Mode != UMAGpuTextureCompressor.TransferMode.DirectCopy)
                Assert.Ignore("This test device cannot copy blocks into a compressed texture.");
        }

        // A smooth image with an alpha ramp, as a render texture with its mip levels, like an atlas.
        private static RenderTexture CreateSource(int width = Size, int height = Size)
        {
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float u = x / (float)(width - 1), v = y / (float)(height - 1);
                    pixels[y * width + x] = new Color(.2f + .6f * u, .7f - .5f * v, .3f + .3f * u * v, x < width / 2 ? 1f : 1f - .8f * v);
                }
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            image.SetPixels(pixels);
            image.Apply(false, false);
            var source = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                useMipMap = true, autoGenerateMips = false, name = "GPU compression test source"
            };
            source.Create();
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(image, source);
            RenderTexture.active = previous;
            source.GenerateMips();
            UnityEngine.Object.DestroyImmediate(image);
            return source;
        }

        // What the GPU texture holds at one mip level, without relying on a CPU copy.
        private static Color32[] ReadGpu(Texture texture, int mip = 0)
        {
            int width = Mathf.Max(1, texture.width >> mip), height = Mathf.Max(1, texture.height >> mip);
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            FilterMode filter = texture.filterMode;
            Texture2D copy = null;
            try
            {
                texture.filterMode = FilterMode.Point;
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;
                copy = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                copy.Apply(false, false);
                return copy.GetPixels32();
            }
            finally
            {
                texture.filterMode = filter;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        private static void AssertClose(Color32[] expected, Color32[] actual, bool alpha, double maxRmse, string what)
        {
            Assert.AreEqual(expected.Length, actual.Length, what);
            double color = 0, transparency = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                int r = expected[i].r - actual[i].r, g = expected[i].g - actual[i].g, b = expected[i].b - actual[i].b, a = expected[i].a - actual[i].a;
                color += r * r + g * g + b * b;
                transparency += a * a;
            }
            Assert.That(Math.Sqrt(color / (expected.Length * 3)), Is.LessThanOrEqualTo(maxRmse), what + ": colour error");
            if (alpha) Assert.That(Math.Sqrt(transparency / expected.Length), Is.LessThanOrEqualTo(maxRmse), what + ": alpha error");
        }

        private static void Release(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
        }

        [TestCase(GraphicsFormat.RGBA_DXT1_UNorm, false)]
        [TestCase(GraphicsFormat.RGBA_DXT5_UNorm, true)]
        public void DirectCopyReproducesTheSource(GraphicsFormat format, bool alpha)
        {
            RequireDirectCopy();
            if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample)) Assert.Ignore(format + " unavailable on this test device.");
            RenderTexture source = CreateSource();
            Texture2D compressed = null;
            try
            {
                compressed = UMAGpuTextureCompressor.CompressImmediate(source, format, false);
                Assert.NotNull(compressed);
                Assert.AreEqual(format, compressed.graphicsFormat);
                Assert.AreEqual(1, compressed.mipmapCount);
                Assert.IsFalse(compressed.isReadable, "A GPU copy must not leave a CPU copy behind.");
                AssertClose(ReadGpu(source), ReadGpu(compressed), alpha, 6, format.ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(compressed);
                Release(source);
            }
        }

        // Every mip level of the source is a different flat colour, so a level encoded from the
        // wrong source level, or a padded 2x2 or 1x1 level gone wrong, shows as the wrong colour.
        [TestCase(GraphicsFormat.RGBA_DXT1_UNorm, false)]
        [TestCase(GraphicsFormat.RGBA_DXT5_UNorm, true)]
        public void EveryMipLevelIsEncodedFromItsOwnSourceLevel(GraphicsFormat format, bool alpha)
        {
            RequireDirectCopy();
            if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample)) Assert.Ignore(format + " unavailable on this test device.");
            var source = new RenderTexture(Size, Size / 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                useMipMap = true, autoGenerateMips = false, name = "GPU compression mip test source"
            };
            source.Create();
            Texture2D compressed = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                var colours = new Color32[source.mipmapCount];
                for (int mip = 0; mip < colours.Length; mip++)
                {
                    colours[mip] = new Color32((byte)(30 + mip * 30), (byte)(220 - mip * 25), (byte)(mip % 2 == 0 ? 60 : 180), (byte)(255 - mip * 20));
                    Graphics.SetRenderTarget(source, mip);
                    GL.Clear(false, true, colours[mip]);
                }
                RenderTexture.active = previous;

                compressed = UMAGpuTextureCompressor.CompressImmediate(source, format, true);
                Assert.AreEqual(source.mipmapCount, compressed.mipmapCount);
                Assert.AreEqual(7, compressed.mipmapCount, "64x32 has seven levels, the last two smaller than a block.");
                for (int mip = 0; mip < colours.Length; mip++)
                    foreach (Color32 pixel in ReadGpu(compressed, mip))
                    {
                        Assert.That(Mathf.Abs(pixel.r - colours[mip].r), Is.LessThanOrEqualTo(8), "Red at mip " + mip);
                        Assert.That(Mathf.Abs(pixel.g - colours[mip].g), Is.LessThanOrEqualTo(8), "Green at mip " + mip);
                        Assert.That(Mathf.Abs(pixel.b - colours[mip].b), Is.LessThanOrEqualTo(8), "Blue at mip " + mip);
                        if (alpha) Assert.That(Mathf.Abs(pixel.a - colours[mip].a), Is.LessThanOrEqualTo(2), "Alpha at mip " + mip);
                    }
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(compressed);
                Release(source);
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void ReadbackMatchesTheDirectCopyAndHonoursReadability(bool keepReadable)
        {
            RequireDirectCopy();
            RenderTexture source = CreateSource();
            Texture2D direct = null, readback = null;
            try
            {
                GraphicsFormat format = UMAGpuTextureCompressor.Format;
                direct = UMAGpuTextureCompressor.CompressImmediate(source, format, true);
                UMAGpuTextureCompressor.ModeOverride = UMAGpuTextureCompressor.TransferMode.Readback;
                readback = UMAGpuTextureCompressor.CompressBlocking(source, format, true, keepReadable);
                Assert.NotNull(readback);
                Assert.AreEqual(keepReadable, readback.isReadable);
                Assert.AreEqual(direct.mipmapCount, readback.mipmapCount);
                for (int mip = 0; mip < direct.mipmapCount; mip++)
                    CollectionAssert.AreEqual(ReadGpu(direct, mip), ReadGpu(readback, mip), "The two routes disagree at mip " + mip);
                // Unity decodes the CPU copy itself, which may round differently from the GPU.
                if (keepReadable)
                    AssertClose(ReadGpu(readback), readback.GetPixels32(), true, 2, "CPU pixels of a readable compressed texture");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(direct);
                UnityEngine.Object.DestroyImmediate(readback);
                Release(source);
            }
        }

        // Desktop devices cannot sample ETC2, so the blocks are decoded by Unity's own software
        // decoder, which also checks their bit layout against an independent implementation.
        [TestCase(GraphicsFormat.RGB_ETC2_UNorm, TextureFormat.ETC2_RGB, false)]
        [TestCase(GraphicsFormat.RGBA_ETC2_UNorm, TextureFormat.ETC2_RGBA8, true)]
        public void Etc2BlocksDecodeToTheSource(GraphicsFormat format, TextureFormat textureFormat, bool alpha)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback) Assert.Ignore("Compute shaders unavailable on this test device.");
            RenderTexture source = CreateSource();
            Texture2D decoded = null;
            try
            {
                uint[] blocks = UMAGpuTextureCompressor.EncodeBlocks(source, format);
                Assert.NotNull(blocks);
                Assert.AreEqual(Size / 4 * (Size / 4) * (alpha ? 4 : 2), blocks.Length);
                decoded = new Texture2D(Size, Size, textureFormat, false, true);
                decoded.SetPixelData(blocks, 0);
                decoded.Apply(false, false);
                AssertClose(ReadGpu(source), ReadGpu(decoded), alpha, 6, format.ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(decoded);
                Release(source);
            }
        }

        [Test]
        public void SizesThatAreNotWholeBlocksAreRejected()
        {
            RequireCompression();
            RenderTexture source = CreateSource(30, 32);
            try
            {
                Assert.Throws<ArgumentException>(() => UMAGpuTextureCompressor.CompressImmediate(source, UMAGpuTextureCompressor.Format, false));
                Assert.AreEqual(32, UMAGpuTextureCompressor.AlignToBlocks(30));
                Assert.AreEqual(32, UMAGpuTextureCompressor.AlignToBlocks(32));
                Assert.AreEqual(4, UMAGpuTextureCompressor.AlignToBlocks(0));
            }
            finally
            {
                Release(source);
            }
        }

        private static Texture BuildAtlas(UMAResourceReuseAtlasTests.Fixture f, UMAData data = null, bool reuse = false)
        {
            data = data ?? f.Avatar(reuse);
            f.Build(data);
            AsyncGPUReadback.WaitAllRequests();
            RenderTexToCPU.ApplyQueuedCopies(0);
            return data.generatedMaterials.materials[0].resultingAtlasList[0];
        }

        // Direct copy, synchronous readback and asynchronous readback must all end with the same atlas.
        [TestCase(UMAGpuTextureCompressor.TransferMode.DirectCopy, false)]
        [TestCase(UMAGpuTextureCompressor.TransferMode.DirectCopy, true)]
        [TestCase(UMAGpuTextureCompressor.TransferMode.Readback, false)]
        [TestCase(UMAGpuTextureCompressor.TransferMode.Readback, true)]
        public void GeneratedAtlasIsCompressedThroughEveryRoute(UMAGpuTextureCompressor.TransferMode mode, bool async)
        {
            RequireDirectCopy();
            using (var f = new UMAResourceReuseAtlasTests.Fixture(false, async))
            {
                Texture plain = BuildAtlas(f);
                Assert.IsInstanceOf<RenderTexture>(plain, "Without the switch the atlas stays a RenderTexture.");
                Color32[] expected = ReadGpu(plain);

                UMAGpuTextureCompressor.ModeOverride = mode;
                f.Generator.forceGPUTextureCompression = true;
                UMAData data = f.Avatar(false);
                Texture atlas = BuildAtlas(f, data);
                Assert.IsInstanceOf<Texture2D>(atlas, "The switch must produce a compressed Texture2D even with conversion off.");
                var compressed = (Texture2D)atlas;
                Assert.AreEqual(UMAGpuTextureCompressor.Format, compressed.graphicsFormat);
                Assert.IsFalse(compressed.isReadable, "No CPU copy unless Keep Converted Textures Readable asks for one.");
                Assert.AreSame(compressed, data.generatedMaterials.materials[0].material.GetTexture("_BaseMap"));
                AssertClose(expected, ReadGpu(compressed), true, 4, "Compressed atlas");

                data.CleanTextures();
                Assert.IsTrue(compressed == null, "The compressed atlas must be released with its avatar.");
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void ReadableSettingKeepsTheCompressedDataOnTheCpu(bool async)
        {
            RequireDirectCopy();
            using (var f = new UMAResourceReuseAtlasTests.Fixture(false, async))
            {
                f.Generator.forceGPUTextureCompression = true;
                f.Generator.keepConvertedTexturesReadable = true;
                var readable = (Texture2D)BuildAtlas(f);
                Assert.AreEqual(UMAGpuTextureCompressor.Format, readable.graphicsFormat);
                Assert.IsTrue(readable.isReadable);
                AssertClose(ReadGpu(readable), readable.GetPixels32(), true, 2, "Readable compressed atlas");
            }
        }

        [TestCase(UMAGpuTextureCompressor.TransferMode.DirectCopy, false)]
        [TestCase(UMAGpuTextureCompressor.TransferMode.Readback, false)]
        [TestCase(UMAGpuTextureCompressor.TransferMode.Readback, true)]
        public void MipLevelsAreCompressedWhenTheGeneratorMakesThem(UMAGpuTextureCompressor.TransferMode mode, bool async)
        {
            RequireDirectCopy();
            using (var f = new UMAResourceReuseAtlasTests.Fixture(false, async))
            {
                f.Generator.convertMipMaps = true;
                f.Generator.convertRenderTexture = true;
                var plain = (Texture2D)BuildAtlas(f);
                UMAGpuTextureCompressor.ModeOverride = mode;
                f.Generator.forceGPUTextureCompression = true;
                var compressed = (Texture2D)BuildAtlas(f);
                Assert.AreEqual(plain.mipmapCount, compressed.mipmapCount);
                Assert.Greater(compressed.mipmapCount, 1);
                for (int mip = 0; mip < compressed.mipmapCount; mip++)
                    AssertClose(ReadGpu(plain, mip), ReadGpu(compressed, mip), true, 6, "Atlas mip " + mip);
            }
        }

        [Test]
        public void AtlasSizeIsRoundedUpToWholeBlocks()
        {
            RequireCompression();
            using (var f = new UMAResourceReuseAtlasTests.Fixture(false, false))
            {
                f.Generator.forceGPUTextureCompression = true;
                UMAData data = f.Avatar(false);
                data.atlasResolutionScale = 0.85f;
                Texture atlas = BuildAtlas(f, data);
                Assert.IsInstanceOf<Texture2D>(atlas);
                Assert.AreEqual(0, atlas.width % 4, "Width " + atlas.width);
                Assert.AreEqual(0, atlas.height % 4, "Height " + atlas.height);
                Assert.Less(atlas.width, 32, "The scale must still apply.");
            }
        }

        [Test]
        public void UnavailableDevicesKeepTheirCurrentOutput()
        {
            using (var f = new UMAResourceReuseAtlasTests.Fixture(false, false))
            {
                UMAGpuTextureCompressor.ModeOverride = UMAGpuTextureCompressor.TransferMode.Unavailable;
                f.Generator.forceGPUTextureCompression = true;
                Assert.AreEqual(GraphicsFormat.None, UMAGpuTextureCompressor.ResolveFormat(f.Generator, RenderTextureFormat.ARGB32));
                Assert.IsInstanceOf<RenderTexture>(BuildAtlas(f), "With conversion off the atlas stays a RenderTexture.");

                f.Generator.convertRenderTexture = true;
                var converted = (Texture2D)BuildAtlas(f);
                Assert.IsFalse(GraphicsFormatUtility.IsCompressedFormat(converted.graphicsFormat), "Conversion must behave as it does without the switch.");
            }
        }

        [Test]
        public void OnlyEightBitColourChannelsAreCompressed()
        {
            RequireCompression();
            var generator = new GameObject("GPU compression format test").AddComponent<UMAGenerator>();
            try
            {
                generator.enabled = false;
                Assert.AreEqual(GraphicsFormat.None, UMAGpuTextureCompressor.ResolveFormat(generator, RenderTextureFormat.ARGB32), "Off by default.");
                generator.forceGPUTextureCompression = true;
                Assert.AreEqual(UMAGpuTextureCompressor.Format, UMAGpuTextureCompressor.ResolveFormat(generator, RenderTextureFormat.ARGB32));
                Assert.AreEqual(GraphicsFormat.None, UMAGpuTextureCompressor.ResolveFormat(generator, RenderTextureFormat.ARGBHalf));
                Assert.AreEqual(GraphicsFormat.None, UMAGpuTextureCompressor.ResolveFormat(generator, RenderTextureFormat.R8));
                Assert.AreEqual(GraphicsFormat.None, UMAGpuTextureCompressor.ResolveFormat(null, RenderTextureFormat.ARGB32));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(generator.gameObject);
            }
        }

        [Test]
        public void CompressedAndUncompressedAtlasesAreNotShared()
        {
            RequireCompression();
            using (var f = new UMAResourceReuseAtlasTests.Fixture(true, false))
            {
                Texture plain = BuildAtlas(f, reuse: true);
                f.Generator.forceGPUTextureCompression = true;
                Texture compressed = BuildAtlas(f, reuse: true);
                Texture again = BuildAtlas(f, reuse: true);
                Assert.AreNotSame(plain, compressed, "A shared atlas would ignore the switch.");
                Assert.IsTrue(GraphicsFormatUtility.IsCompressedFormat(compressed.graphicsFormat));
                Assert.AreSame(compressed, again, "Identical compressed atlases must still be shared.");
                f.Generator.forceGPUTextureCompression = false;
                Assert.AreSame(plain, BuildAtlas(f, reuse: true), "Turning the switch off must find the uncompressed atlas again.");
            }
        }

        [Test]
        public void RebuildReplacesTheCompressedAtlasWithoutLeakingIt()
        {
            RequireCompression();
            using (var f = new UMAResourceReuseAtlasTests.Fixture(false, false))
            {
                f.Generator.forceGPUTextureCompression = true;
                UMAData data = f.Avatar(false);
                Texture first = BuildAtlas(f, data);
                Color32[] expected = ReadGpu(first);
                Texture second = BuildAtlas(f, data);
                Assert.IsTrue(first == null || ReferenceEquals(first, second), "The replaced atlas must be released.");
                CollectionAssert.AreEqual(expected, ReadGpu(second));

                // Back to render textures in place.
                f.Generator.forceGPUTextureCompression = false;
                Texture plain = BuildAtlas(f, data);
                Assert.IsInstanceOf<RenderTexture>(plain);
                Assert.IsTrue(second == null, "The compressed atlas must be released when the switch goes off.");
            }
        }

        // Unity cannot Instantiate a texture without a CPU copy, so a renderer that needs its
        // own copy of a shared atlas must get one made on the GPU.
        [TestCase(false)] [TestCase(true)]
        public void SharedAtlasWithoutCpuCopyCanBeMadeUnique(bool compress)
        {
            if (compress) RequireCompression();
            using (var f = new UMAResourceReuseAtlasTests.Fixture(true, false))
            {
                f.Generator.forceGPUTextureCompression = compress;
                var a = f.Avatar(); var b = f.Avatar();
                f.Build(a); f.Meshes.Build(a); f.Build(b); f.Meshes.Build(b);
                var renderer = a.GetRenderer(0); var other = b.GetRenderer(0);
                Texture shared = other.sharedMaterial.GetTexture("_BaseMap");
                Assert.IsFalse(shared.isReadable);
                Assert.AreSame(shared, renderer.sharedMaterial.GetTexture("_BaseMap"));

                Texture unique = UMAResourceLeaseOwner.MakeTextureUnique(renderer, 0, "_BaseMap");
                Assert.AreNotSame(shared, unique);
                Assert.AreSame(unique, renderer.sharedMaterial.GetTexture("_BaseMap"));
                Assert.AreEqual(shared.graphicsFormat, unique.graphicsFormat);
                Assert.AreEqual(compress, GraphicsFormatUtility.IsCompressedFormat(unique.graphicsFormat));
                CollectionAssert.AreEqual(ReadGpu(shared), ReadGpu(unique));
            }
        }

        // Experimental: nothing may turn it on unless the user does.
        [Test]
        public void SwitchIsOffByDefaultEverywhere()
        {
            var holder = new GameObject("GPU compression default test");
            var profile = ScriptableObject.CreateInstance<GeneratorQualityProfile>();
            try
            {
                holder.SetActive(false);
                var generator = holder.AddComponent<UMAGenerator>();
                Assert.IsFalse(generator.forceGPUTextureCompression, "New generator");
                Assert.IsFalse(holder.AddComponent<UMAGeneratorOverride>().forceGPUTextureCompression, "New generator override");
                Assert.IsFalse(new GeneratorQualityConfiguration().forceGPUTextureCompression, "New quality configuration");
                Assert.IsFalse(profile.forceGPUTextureCompression.overrideValue, "A new quality profile must not override it");
                Assert.IsFalse(profile.forceGPUTextureCompression.value, "New quality profile value");

                // A profile that does not override the switch leaves it off.
                GeneratorQualityConfiguration resolved = GeneratorQualityConfiguration.Capture(generator);
                profile.Overlay(resolved);
                Assert.IsFalse(resolved.forceGPUTextureCompression, "Resolved configuration");
                Assert.AreEqual(GraphicsFormat.None, UMAGpuTextureCompressor.ResolveFormat(generator, RenderTextureFormat.ARGB32));

                // The generator prefab the project uses.
                UMAGeneratorBase project = UMAAssetIndexer.Instance != null ? UMAAssetIndexer.Instance.generator : null;
                if (project != null) Assert.IsFalse(project.forceGPUTextureCompression, "Project generator " + project.name);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void SwitchIsPartOfTheQualityConfiguration()
        {
            using (var f = new UMAResourceReuseAtlasTests.Fixture(false, false))
            {
                f.Generator.forceGPUTextureCompression = true;
                GeneratorQualityConfiguration captured = GeneratorQualityConfiguration.Capture(f.Generator);
                Assert.IsTrue(captured.forceGPUTextureCompression);
                GeneratorQualityConfiguration off = captured.Clone();
                off.forceGPUTextureCompression = false;
                Assert.IsFalse(captured.SameOutput(off), "The switch changes generated output.");
                off.ApplyTo(f.Generator);
                Assert.IsFalse(f.Generator.forceGPUTextureCompression);

                var profile = ScriptableObject.CreateInstance<GeneratorQualityProfile>();
                try
                {
                    profile.forceGPUTextureCompression = new GeneratorQualityValue<bool>(true) { overrideValue = true };
                    profile.Overlay(off);
                    Assert.IsTrue(off.forceGPUTextureCompression);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(profile);
                }
            }
        }
    }
}
#endif
