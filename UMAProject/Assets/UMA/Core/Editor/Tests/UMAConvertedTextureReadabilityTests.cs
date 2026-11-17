#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Tests
{
    /// <summary>
    /// Converted atlases free their CPU copy unless the generator keeps them readable,
    /// and either way the GPU texture holds the composited atlas.
    /// </summary>
    public class UMAConvertedTextureReadabilityTests
    {
        private static Texture2D Build(UMAResourceReuseAtlasTests.Fixture f, bool keepReadable, bool reuse = false)
        {
            f.Generator.keepConvertedTexturesReadable = keepReadable;
            UMAData data = f.Avatar(reuse);
            f.Build(data);
            if (f.Generator.useAsyncConversion)
            {
                AsyncGPUReadback.WaitAllRequests();
                RenderTexToCPU.ApplyQueuedCopies(0);
            }
            Texture atlas = data.generatedMaterials.materials[0].resultingAtlasList[0];
            Assert.IsInstanceOf<Texture2D>(atlas, "Conversion did not produce a Texture2D.");
            return (Texture2D)atlas;
        }

        // What the GPU texture holds, without relying on its CPU copy.
        private static Color32[] ReadGpu(Texture texture)
        {
            RenderTexture temporary = RenderTexture.GetTemporary(texture.width, texture.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;
                copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
                copy.Apply(false, false);
                return copy.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                if (copy != null) Object.DestroyImmediate(copy);
            }
        }

        private static void AssertNotBlank(Color32[] pixels)
        {
            foreach (Color32 pixel in pixels)
            {
                if (pixel.r != 0 || pixel.g != 0 || pixel.b != 0) return;
            }
            Assert.Fail("The atlas is blank.");
        }

        [TestCase(false)] [TestCase(true)]
        public void ConvertedAtlasDropsItsCpuCopyByDefaultAndKeepsItsPixels(bool async)
        {
            if (async && !SystemInfo.supportsAsyncGPUReadback) Assert.Ignore("GPU readback unavailable on this test device.");
            using (var f = new UMAResourceReuseAtlasTests.Fixture(true, async))
            {
                Texture2D readable = Build(f, true);
                Texture2D gpuOnly = Build(f, false);

                Assert.IsTrue(readable.isReadable, "Keep Converted Textures Readable must keep the CPU copy.");
                Assert.IsFalse(gpuOnly.isReadable, "The CPU copy must be freed by default.");
                Color32[] expected = ReadGpu(readable);
                AssertNotBlank(expected);
                CollectionAssert.AreEqual(expected, ReadGpu(gpuOnly), "Dropping the CPU copy changed the atlas.");
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void ReadableAtlasCpuPixelsMatchTheGpuTexture(bool async)
        {
            if (async && !SystemInfo.supportsAsyncGPUReadback) Assert.Ignore("GPU readback unavailable on this test device.");
            using (var f = new UMAResourceReuseAtlasTests.Fixture(true, async))
            {
                Texture2D readable = Build(f, true);
                Color32[] cpu = readable.GetPixels32();
                AssertNotBlank(cpu);
                CollectionAssert.AreEqual(ReadGpu(readable), cpu,
                    "A readable atlas must hold the composited pixels on the CPU too, not an unfilled buffer.");
            }
        }

        [Test]
        public void CompressedAtlasDropsItsCpuCopyAfterCompressing()
        {
            using (var f = new UMAResourceReuseAtlasTests.Fixture(true, false))
            {
                Color32[] uncompressed = ReadGpu(Build(f, false));
                var quality = f.Generator.qualityTextures;
                quality.compression.overrideValue = true;
                quality.compression.value = GeneratorRuntimeCompression.Fast;
                f.Generator.qualityTextures = quality;
                if (quality.EffectiveCompression(true, RenderTextureFormat.ARGB32, 32, 32) == GeneratorRuntimeCompression.Disabled)
                    Assert.Ignore("Runtime compression unavailable on this test device.");

                Texture2D readable = Build(f, true);
                Texture2D gpuOnly = Build(f, false);
                Assert.AreNotEqual(TextureFormat.ARGB32, gpuOnly.format, "The atlas was not compressed.");
                Assert.AreEqual(readable.format, gpuOnly.format);
                Assert.IsTrue(readable.isReadable);
                Assert.IsFalse(gpuOnly.isReadable, "The CPU copy must be freed after compression.");
                Color32[] compressed = ReadGpu(gpuOnly);
                CollectionAssert.AreEqual(ReadGpu(readable), compressed);
                // Block compression is lossy, but every channel must stay where it was.
                for (int i = 0; i < compressed.Length; i++)
                {
                    Assert.That(Mathf.Abs(compressed[i].r - uncompressed[i].r), Is.LessThanOrEqualTo(16), "Red moved at pixel " + i);
                    Assert.That(Mathf.Abs(compressed[i].g - uncompressed[i].g), Is.LessThanOrEqualTo(16), "Green moved at pixel " + i);
                    Assert.That(Mathf.Abs(compressed[i].b - uncompressed[i].b), Is.LessThanOrEqualTo(16), "Blue moved at pixel " + i);
                    Assert.That(Mathf.Abs(compressed[i].a - uncompressed[i].a), Is.LessThanOrEqualTo(16), "Alpha moved at pixel " + i);
                }
            }
        }

        [Test]
        public void ReadableAndGpuOnlyAtlasesAreNotShared()
        {
            using (var f = new UMAResourceReuseAtlasTests.Fixture(true, false))
            {
                Texture2D readable = Build(f, true, reuse: true);
                Texture2D gpuOnly = Build(f, false, reuse: true);
                Assert.AreNotSame(readable, gpuOnly, "A shared atlas would hand a GPU-only texture to a reader that asked for a readable one.");
                Assert.IsTrue(readable.isReadable);
                Assert.IsFalse(gpuOnly.isReadable);
            }
        }

        [Test]
        public void RebuildSwitchesBetweenReadableAndGpuOnly()
        {
            using (var f = new UMAResourceReuseAtlasTests.Fixture(true, false))
            {
                f.Generator.keepConvertedTexturesReadable = false;
                UMAData data = f.Avatar(false);
                f.Build(data);
                var first = (Texture2D)data.generatedMaterials.materials[0].resultingAtlasList[0];
                Assert.IsFalse(first.isReadable);
                Color32[] expected = ReadGpu(first);

                // Rebuild in place: the previous GPU-only texture is reused when it fits.
                f.Build(data);
                var reused = (Texture2D)data.generatedMaterials.materials[0].resultingAtlasList[0];
                Assert.AreSame(first, reused);
                CollectionAssert.AreEqual(expected, ReadGpu(reused));

                // A readable rebuild cannot reuse a texture without CPU pixels.
                f.Generator.keepConvertedTexturesReadable = true;
                f.Build(data);
                var readable = (Texture2D)data.generatedMaterials.materials[0].resultingAtlasList[0];
                Assert.AreNotSame(first, readable);
                Assert.IsTrue(readable.isReadable);
                Assert.IsTrue(first == null, "The replaced GPU-only texture must be released.");
                CollectionAssert.AreEqual(expected, readable.GetPixels32());
            }
        }
    }
}
#endif
