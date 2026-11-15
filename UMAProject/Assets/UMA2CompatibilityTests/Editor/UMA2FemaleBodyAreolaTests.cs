using System;
using System.IO;
using NUnit.Framework;
using UMA.Editors.PackageSupport;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The UMA 2 female body keeps its areola in four textures (albedo, normal map, skin mask, mask map).
    /// They must agree with each other and with the torso mesh: the dot in the masks lies on the breast apex,
    /// and the painted areola lies on the dot. When they drift apart the skin tint shows a second, offset disc.
    /// Positions are found in the files, so a repainted texture is covered without touching this fixture.
    /// </summary>
    public sealed class UMA2FemaleBodyAreolaTests
    {
        // One texture pixel is about 1.3 mm on the breast.
        const float ApexTolerancePixels = 12f;
        const float AgreementTolerancePixels = 3f;

        static string Folder => UMAContentCatalog.Root(UMAContentKind.Uma2) + "/Races/HumanFemale/";

        sealed class Image
        {
            public Color32[] pixels; public int width, height;
            public Color32 At(int x, int yFromTop) => pixels[(height - 1 - yFromTop) * width + x];
        }

        static Image Load(string relativePath)
        {
            string path = Folder + relativePath;
            if (!File.Exists(path)) Assert.Ignore("The UMA 2 female body content is not installed (" + path + ").");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(ImageConversion.LoadImage(texture, File.ReadAllBytes(path)), Is.True, path);
                return new Image { pixels = texture.GetPixels32(), width = texture.width, height = texture.height };
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        /// <summary>The most forward torso vertex on each side, in texture pixels (y from the top).</summary>
        static Vector2[] BreastApexes(int size)
        {
            var slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(Folder + "Slots/UMA_Human_Female_Torso_Slot.asset");
            if (slot == null || slot.meshData == null) Assert.Ignore("The UMA 2 female torso slot is not installed.");
            var vertices = slot.meshData.vertices; var uv = slot.meshData.uv;
            var apex = new Vector2[2]; var forward = new[] { float.MinValue, float.MinValue };
            for (int i = 0; i < vertices.Length; i++)
            {
                // The slot is Z-up and faces +Y.
                int side = vertices[i].x < 0 ? 0 : 1;
                if (vertices[i].y <= forward[side]) continue;
                forward[side] = vertices[i].y;
                apex[side] = new Vector2(uv[i].x * size, (1 - uv[i].y) * size);
            }
            return apex;
        }

        /// <summary>Centre of the dark dot around a point: pixels darker than halfway between the dot and its surroundings.</summary>
        static Vector2 DarkDot(Image image, Vector2 near, Func<Color32, int> channel, string what)
        {
            const int reach = 40;
            double rim = 0; int rimCount = 0; int floor = 255;
            for (int k = 0; k < 128; k++)
            {
                double angle = k * Math.PI / 64;
                rim += channel(image.At((int)Math.Round(near.x + (reach + 6) * Math.Cos(angle)), (int)Math.Round(near.y + (reach + 6) * Math.Sin(angle)))); rimCount++;
            }
            rim /= rimCount;
            for (int y = (int)near.y - reach; y <= near.y + reach; y++)
                for (int x = (int)near.x - reach; x <= near.x + reach; x++) floor = Math.Min(floor, channel(image.At(x, y)));
            Assert.That(rim - floor, Is.GreaterThan(15), what + ": no dot within " + reach + " pixels of " + near);
            double threshold = (rim + floor) * 0.5, sx = 0, sy = 0; int count = 0;
            for (int y = (int)near.y - reach; y <= near.y + reach; y++)
                for (int x = (int)near.x - reach; x <= near.x + reach; x++)
                    if (channel(image.At(x, y)) < threshold) { sx += x; sy += y; count++; }
            return new Vector2((float)(sx / count), (float)(sy / count));
        }

        /// <summary>Centre of what the body albedo adds to the areola-free albedo around a point.</summary>
        static Vector2 PaintedAreola(Image painted, Image clean, Vector2 near)
        {
            const int reach = 60;
            double sx = 0, sy = 0, weight = 0;
            for (int y = (int)near.y - reach; y <= near.y + reach; y++)
                for (int x = (int)near.x - reach; x <= near.x + reach; x++)
                {
                    Color32 a = painted.At(x, y), b = clean.At(x, y);
                    int difference = Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b)));
                    if (difference < 6) continue;
                    sx += x * (double)difference; sy += y * (double)difference; weight += difference;
                }
            Assert.That(weight, Is.GreaterThan(0), "No areola is painted within " + reach + " pixels of " + near);
            return new Vector2((float)(sx / weight), (float)(sy / weight));
        }

        [Test]
        public void TheSkinMaskDotLiesOnTheBreastApex()
        {
            var mask = Load("Textures/Body/UMA2_F_Body_SkinMask.png");
            foreach (Vector2 apex in BreastApexes(mask.width))
            {
                Vector2 dot = DarkDot(mask, apex, c => c.r, "Skin mask");
                Assert.That(Vector2.Distance(dot, apex), Is.LessThanOrEqualTo(ApexTolerancePixels),
                    "Skin mask dot at " + dot + ", breast apex at " + apex);
            }
        }

        [Test]
        public void ThePaintedAreolaLiesOnTheSkinMaskDot()
        {
            var mask = Load("Textures/Body/UMA2_F_Body_SkinMask.png");
            var painted = Load("Textures/Body/F_H_bod_Albedo2.png");
            var clean = Load("Textures/Body/F_H_bod_Albedo.png");
            Assert.That(painted.width, Is.EqualTo(mask.width), "The albedo and the skin mask differ in size.");
            foreach (Vector2 apex in BreastApexes(mask.width))
            {
                Vector2 dot = DarkDot(mask, apex, c => c.r, "Skin mask");
                Vector2 areola = PaintedAreola(painted, clean, apex);
                Assert.That(Vector2.Distance(areola, dot), Is.LessThanOrEqualTo(AgreementTolerancePixels),
                    "Painted areola at " + areola + ", skin mask dot at " + dot);
            }
        }

        [Test]
        public void TheDetailCoverageDotLiesOnTheSkinMaskDot()
        {
            var mask = Load("Textures/Body/UMA2_F_Body_SkinMask.png");
            var maskMap = Load("Textures/Body/UMA2_F_Body_SSS.AO.Detail.Gloss.png");
            Assert.That(maskMap.width, Is.EqualTo(mask.width), "The mask map and the skin mask differ in size.");
            foreach (Vector2 apex in BreastApexes(mask.width))
            {
                Vector2 dot = DarkDot(mask, apex, c => c.r, "Skin mask");
                Vector2 coverage = DarkDot(maskMap, apex, c => c.b, "Mask map detail coverage");
                Assert.That(Vector2.Distance(coverage, dot), Is.LessThanOrEqualTo(AgreementTolerancePixels),
                    "Detail coverage dot at " + coverage + ", skin mask dot at " + dot);
            }
        }
    }
}
