#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public class UMAWrinkleSourceMapTests
    {
        const float PlaneMeters = 0.0256f; // 64 texels of 0.4 mm

        // A flat square facing +Z with U along +X and V along +Y, covering u = 0..uMax of the texture.
        static UMAWrinkleSurfaceMap Plane(int size, float tangentW, float uMax = 1f)
        {
            float w = PlaneMeters * uMax;
            var positions = new[] { new System.Numerics.Vector3(0, 0, 0), new System.Numerics.Vector3(w, 0, 0), new System.Numerics.Vector3(w, PlaneMeters, 0), new System.Numerics.Vector3(0, PlaneMeters, 0) };
            var normals = new System.Numerics.Vector3[4];
            var tangents = new System.Numerics.Vector4[4];
            for (int i = 0; i < 4; i++) { normals[i] = new System.Numerics.Vector3(0, 0, 1); tangents[i] = new System.Numerics.Vector4(1, 0, 0, tangentW); }
            var uvs = new[] { new System.Numerics.Vector2(0, 0), new System.Numerics.Vector2(uMax, 0), new System.Numerics.Vector2(uMax, 1), new System.Numerics.Vector2(0, 1) };
            return UMAWrinkleSurfaceMap.Build(positions, normals, tangents, uvs, new[] { 0, 1, 2, 0, 2, 3 }, size);
        }

        // Height in mm of a plane tilted kx along X and ky along Y, at each texel centre (row 0 is the top, Y = PlaneMeters).
        static float[] Ramp(int size, float kx, float ky)
        {
            float texelMM = PlaneMeters * 1000f / size;
            var h = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    h[y * size + x] = kx * (x + 0.5f) * texelMM + ky * (size - y - 0.5f) * texelMM;
            return h;
        }

        static float Encoded(float slopeX, float slopeY, bool red)
        {
            var n = new Vector3(-slopeX, -slopeY, 1).normalized;
            return (red ? n.x : n.y) * 0.5f + 0.5f;
        }

        [TestCase(1f, 0.3f, -0.2f)]
        [TestCase(-1f, 0.3f, -0.2f)] // mirrored island: the bitangent points down, so the same tilt reads the other way
        public void PackTurnsATiltedHeightIntoTheMatchingSlope(float tangentW, float kx, float ky)
        {
            const int size = 64;
            var map = Plane(size, tangentW);
            var zero = new float[size * size];
            var packed = UMAWrinkleSourceMaps.Pack(map, Ramp(size, kx, ky), zero, zero, out int clamped);
            Assert.That(clamped, Is.EqualTo(0));
            float sy = ky * tangentW;
            for (int i = 0; i < size * size; i++)
            {
                Assert.That(map.Covered[i], Is.True);
                Assert.That(packed[i * 4], Is.EqualTo(Encoded(kx, sy, true)).Within(1e-4f), "red at " + i);
                Assert.That(packed[i * 4 + 1], Is.EqualTo(Encoded(kx, sy, false)).Within(1e-4f), "green at " + i);
            }
        }

        [Test]
        public void SlopesSteeperThanTheMapCanStoreAreClampedAndCounted()
        {
            const int size = 32;
            var map = Plane(size, 1f);
            var zero = new float[size * size];
            var packed = UMAWrinkleSourceMaps.Pack(map, Ramp(size, 10f, 0f), zero, zero, out int clamped);
            Assert.That(clamped, Is.EqualTo(size * size));
            Assert.That(packed[0], Is.EqualTo(Encoded(UMAWrinkleProceduralGenerator.MaxSlope, 0, true)).Within(1e-4f));
        }

        [Test]
        public void CreaseAndFlushPassThroughAndPaddingCopiesTheNearestCoveredTexel()
        {
            const int size = 64;
            var map = Plane(size, 1f, 0.5f);
            var crease = new float[size * size];
            var flush = new float[size * size];
            for (int i = 0; i < crease.Length; i++) { crease[i] = (i % 97) / 96f; flush[i] = (i % 31) / 30f; }
            var packed = UMAWrinkleSourceMaps.Pack(map, Ramp(size, 0.2f, 0.1f), crease, flush, out _);
            int padding = 0;
            for (int i = 0; i < size * size; i++)
            {
                if (map.Covered[i])
                {
                    Assert.That(packed[i * 4 + 2], Is.EqualTo(crease[i]));
                    Assert.That(packed[i * 4 + 3], Is.EqualTo(flush[i]));
                    continue;
                }
                padding++;
                int s = map.Source[i];
                for (int k = 0; k < 4; k++) Assert.That(packed[i * 4 + k], Is.EqualTo(packed[s * 4 + k]), "padding texel " + i);
            }
            Assert.That(padding, Is.GreaterThan(size * size / 3));
        }

        [Test]
        public void HeightGrayMappingCentresFlatOnMidGray()
        {
            Assert.That(UMAWrinkleSourceMaps.HeightToGray(0), Is.EqualTo(0.5f));
            Assert.That(UMAWrinkleSourceMaps.HeightToGray(UMAWrinkleSourceMaps.HeightRangeMM), Is.EqualTo(1f));
            Assert.That(UMAWrinkleSourceMaps.HeightToGray(-2 * UMAWrinkleSourceMaps.HeightRangeMM), Is.EqualTo(0f));
            Assert.That(UMAWrinkleSourceMaps.GrayToHeight(UMAWrinkleSourceMaps.HeightToGray(-0.37f)), Is.EqualTo(-0.37f).Within(1e-6f));
        }

        [Test]
        public void PngReaderKeepsSixteenBitGrayAndRowOrder()
        {
            const int size = 4;
            var values = new float[size * size];
            for (int i = 0; i < values.Length; i++) values[i] = i * 4099 / 65535f;
            values[1] = 1 / 65535f; values[2] = 1f;
            var image = UMAPngReader.Read(UMAWrinkleMapBaker.EncodeGray16(values, size));
            Assert.That(image.Width, Is.EqualTo(size)); Assert.That(image.Height, Is.EqualTo(size));
            Assert.That(image.Channels, Is.EqualTo(1)); Assert.That(image.BitDepth, Is.EqualTo(16));
            var read = image.FirstChannel();
            for (int i = 0; i < values.Length; i++) Assert.That(read[i] * 65535f, Is.EqualTo(Mathf.Round(values[i] * 65535f)).Within(1e-3f), "texel " + i);
        }

        [Test]
        public void PngReaderReadsEightBitColorTopRowFirst()
        {
            var tex = new Texture2D(3, 2, TextureFormat.RGBA32, false, true);
            try
            {
                var pixels = new Color32[6];
                for (int i = 0; i < 6; i++) pixels[i] = new Color32((byte)(i * 40), (byte)(200 - i * 30), (byte)(i * 7 + 3), (byte)(255 - i));
                tex.SetPixels32(pixels); tex.Apply(false, false);
                var image = UMAPngReader.Read(tex.EncodeToPNG());
                Assert.That(image.Channels, Is.EqualTo(4)); Assert.That(image.BitDepth, Is.EqualTo(8));
                // Unity's row 0 is the bottom; the reader's row 0 is the top.
                for (int y = 0; y < 2; y++)
                    for (int x = 0; x < 3; x++)
                    {
                        var p = pixels[(1 - y) * 3 + x];
                        int o = (y * 3 + x) * 4;
                        Assert.That(image.Values[o] * 255f, Is.EqualTo(p.r).Within(1e-3f));
                        Assert.That(image.Values[o + 1] * 255f, Is.EqualTo(p.g).Within(1e-3f));
                        Assert.That(image.Values[o + 2] * 255f, Is.EqualTo(p.b).Within(1e-3f));
                        Assert.That(image.Values[o + 3] * 255f, Is.EqualTo(p.a).Within(1e-3f));
                    }
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
            Assert.Throws<InvalidDataException>(() => UMAPngReader.Read(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
        }

        // The procedural bake's own height, packed again, gives back the bake's normals on the real head: same signs and
        // axes on every island. At 512 fine lines are thinner than a texel, so the rebuilt slope is a little weaker
        // (measured 0.84 of the bake at 512, 0.95 at 1024, 0.985 at 2048).
        [Test]
        public void ProceduralHeightRebuildsTheBakedNormalsOnTheHead()
        {
            var slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(UMAWrinkleMapBaker.UMA3HeadSlotPath);
            Assert.That(slot, Is.Not.Null, UMAWrinkleMapBaker.UMA3HeadSlotPath);
            const int size = 512;
            var surface = UMAWrinkleMapBaker.BuildSurface(slot, size);
            var bake = UMAWrinkleProceduralGenerator.Generate(surface, UMAWrinkleFaceLandmarks.UMA3Head(), 1, 1);
            var zero = new float[size * size];
            var packed = UMAWrinkleSourceMaps.Pack(surface, bake.HeightMM, zero, zero, out _);
            double dot = 0, reference = 0, error = 0; int count = 0;
            for (int i = 0; i < size * size; i++)
            {
                if (!surface.Covered[i]) continue;
                float rx = bake.WrinkleMap[i * 4] - 0.5f, ry = bake.WrinkleMap[i * 4 + 1] - 0.5f;
                float px = packed[i * 4] - 0.5f, py = packed[i * 4 + 1] - 0.5f;
                if (rx == 0 && ry == 0 && px == 0 && py == 0) continue;
                dot += px * rx + py * ry; reference += rx * rx + ry * ry;
                error += Math.Abs(px - rx) + Math.Abs(py - ry); count++;
            }
            Assert.That(count, Is.GreaterThan(10000));
            Assert.That(dot / reference, Is.InRange(0.75, 1.1));
            Assert.That(error / count / 2 * 255, Is.LessThan(4.0));
        }

        // Overlay Painter adds only the slope of the artist's height change; with no change the map, padding
        // included, must come back exactly as it was (so a block-compressed or hand-made map never drifts).
        [Test]
        public void ZeroHeightChangeLeavesTheWrinkleMapExactlyAsItWas()
        {
            const int size = 64;
            var map = Plane(size, 1f, 0.5f);
            var rgba = new float[size * size * 4];
            var random = new System.Random(5);
            for (int i = 0; i < rgba.Length; i++) rgba[i] = (float)random.NextDouble() * 0.6f + 0.2f;
            var original = (float[])rgba.Clone();
            int clamped = UMAWrinkleSourceMaps.AddSlopes(map, UMAWrinkleSourceMaps.BuildSlopeFrame(map), new float[size * size], rgba);
            Assert.That(clamped, Is.EqualTo(0));
            Assert.That(rgba, Is.EqualTo(original));

            // A tilted change on a flat map gives the tilt's slope, as Pack does.
            var flat = new float[size * size * 4];
            for (int i = 0; i < size * size; i++) { flat[i * 4] = 0.5f; flat[i * 4 + 1] = 0.5f; }
            UMAWrinkleSourceMaps.AddSlopes(map, UMAWrinkleSourceMaps.BuildSlopeFrame(map), Ramp(size, 0.3f, 0f), flat);
            Assert.That(flat[(10 * size + 10) * 4], Is.EqualTo(Encoded(0.3f, 0f, true)).Within(1e-4f));
            // Padding follows its covered texel where that texel changed.
            int padding = 10 * size + 50;
            Assert.That(map.Covered[padding], Is.False);
            Assert.That(flat[padding * 4], Is.EqualTo(flat[map.Source[padding] * 4]));
        }

        [Test]
        public void SlopeDecodesAndEncodesBackExactly()
        {
            foreach (var (sx, sy) in new[] { (0f, 0f), (0.4f, -0.2f), (-2f, 1.5f), (3.4f, 0.1f) })
            {
                Assert.That(UMAWrinkleSourceMaps.EncodeSlope(sx, sy, out float r, out float g), Is.False);
                UMAWrinkleSourceMaps.DecodeSlope(r, g, out float bx, out float by);
                Assert.That(bx, Is.EqualTo(sx).Within(1e-3f));
                Assert.That(by, Is.EqualTo(sy).Within(1e-3f));
            }
            Assert.That(UMAWrinkleSourceMaps.EncodeSlope(9f, 0f, out _, out _), Is.True, "steeper than the map can store");
        }

        [Test]
        public void WrinkleTexturesGetWrinkleLayoutsAndSafeImportSettings()
        {
            var map = new UMAMaterial.MaterialChannel { channelType = UMAMaterial.ChannelType.Texture, materialPropertyName = "_WrinkleMap", sourceTextureName = "_WrinkleMap" };
            var zones = new UMAMaterial.MaterialChannel { channelType = UMAMaterial.ChannelType.Texture, materialPropertyName = "_WrinkleControlMap", sourceTextureName = "_WrinkleControlMap" };
            var layout = UMAMaterial.InferTextureChannelLayout(map, null);
            Assert.That(layout.red, Is.EqualTo(UMAMaterial.TextureChannelUsage.WrinkleSlope));
            Assert.That(layout.green, Is.EqualTo(UMAMaterial.TextureChannelUsage.WrinkleSlope));
            Assert.That(layout.blue, Is.EqualTo(UMAMaterial.TextureChannelUsage.WrinkleCrease));
            Assert.That(layout.alpha, Is.EqualTo(UMAMaterial.TextureChannelUsage.WrinkleFlush));
            var zoneLayout = UMAMaterial.InferTextureChannelLayout(zones, null);
            Assert.That(zoneLayout.red, Is.EqualTo(UMAMaterial.TextureChannelUsage.WrinkleZone));
            Assert.That(zoneLayout.alpha, Is.EqualTo(UMAMaterial.TextureChannelUsage.WrinkleZone));

            var owner = ScriptableObject.CreateInstance<UMAMaterial>();
            try
            {
                owner.Compression = UMAMaterial.CompressionSettings.Fast;
                var mapOutput = UMAMaterial.InferTextureChannelOutputSettings(map, null, owner);
                var zoneOutput = UMAMaterial.InferTextureChannelOutputSettings(zones, null, owner);
                Assert.That(mapOutput.colorSpace, Is.EqualTo(UMAMaterial.TextureChannelColorSpace.Linear));
                Assert.That(mapOutput.compression, Is.EqualTo(UMAMaterial.TextureChannelImportCompression.HighQuality));
                Assert.That(zoneOutput.colorSpace, Is.EqualTo(UMAMaterial.TextureChannelColorSpace.Linear));
                Assert.That(zoneOutput.compression, Is.EqualTo(UMAMaterial.TextureChannelImportCompression.Uncompressed),
                    "zone ids must survive exactly");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void HeightIsKeptNextToItsWrinkleMapAsSixteenBitR16()
        {
            const string folder = "Assets/__WrinkleHeightSourceTest";
            Assert.That(AssetDatabase.IsValidFolder(folder), Is.False, folder + " already exists");
            AssetDatabase.CreateFolder("Assets", "__WrinkleHeightSourceTest");
            try
            {
                string mapPath = folder + "/Test_Wrinkles.png";
                Assert.That(UMAWrinkleMapBaker.HeightSourcePath(mapPath), Is.EqualTo(folder + "/Test_Wrinkles_height.png"));
                var map = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
                File.WriteAllBytes(mapPath, map.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(map);
                AssetDatabase.ImportAsset(mapPath, ImportAssetOptions.ForceSynchronousImport);

                const int size = 16;
                var gray = new float[size * size];
                for (int i = 0; i < gray.Length; i++) gray[i] = i / (float)(gray.Length - 1);
                var height = UMAWrinkleMapBaker.WriteHeightSource(UMAWrinkleMapBaker.HeightSourcePath(mapPath), gray, size);
                Assert.That(height, Is.Not.Null);
                Assert.That(height.format, Is.EqualTo(TextureFormat.R16));
                Assert.That(height.isDataSRGB, Is.False);
                Assert.That(UMAWrinkleMapBaker.FindHeightSource(AssetDatabase.LoadAssetAtPath<Texture2D>(mapPath)), Is.EqualTo(height));
                var read = UMAPngReader.Read(File.ReadAllBytes(UMAWrinkleMapBaker.HeightSourcePath(mapPath)));
                Assert.That(read.BitDepth, Is.EqualTo(16));
                Assert.That(read.FirstChannel()[5] * 65535f, Is.EqualTo(Mathf.Round(gray[5] * 65535f)).Within(1e-3f));
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }
    }
}
#endif
