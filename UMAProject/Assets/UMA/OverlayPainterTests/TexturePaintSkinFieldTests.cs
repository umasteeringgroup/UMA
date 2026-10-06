#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintSkinFieldTests
    {
        [TestCase(1731)]
        [TestCase(7)]
        [TestCase(42)]
        public void BruisesRemainContinuousAcrossCellAndNearestSeedBoundaries(int seed)
        {
            var type = Type.GetType("UMA.TexturePaint.Examples.AAAOrganicGeneratorEngine+Noise, UMA.TexturePaint.Examples", true);
            var sample = (Func<Vector3, int, float, float, float>)type.GetMethod("SoftClusters",
                BindingFlags.Static | BindingFlags.Public).CreateDelegate(typeof(Func<Vector3, int, float, float, float>));
            var random = new System.Random(seed);
            int visible = 0;
            for (int i = 0; i < 12000; i++)
            {
                var p = new Vector3((float)random.NextDouble() * 8 - 4,
                    (float)random.NextDouble() * 8 - 4, (float)random.NextDouble() * 3 - 1.5f);
                float value = sample(p, seed, 1f, .65f);
                Assert.That(value, Is.InRange(0f, 1f));
                if (value > .1f) visible++;
                foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
                    Assert.That(Mathf.Abs(value - sample(p + axis * .0001f, seed, 1f, .65f)),
                        Is.LessThan(.0004f), "A nearby cell must not abruptly replace the bruise intensity.");
            }
            Assert.That(visible, Is.GreaterThan(500));
            Assert.That(sample(Vector3.zero, seed, 1f, 0f), Is.Zero);
            if (seed != 1731) return;
            const int size = 512;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float value = sample(new Vector3(x * 6f / size, y * 6f / size, .2f), seed, 1f, .65f);
                    pixels[y * size + x] = Color.Lerp(new Color(.67f, .39f, .29f), new Color(.28f, .09f, .24f), value);
                }
                texture.SetPixels(pixels); texture.Apply();
                Directory.CreateDirectory("Library/SkinGeneratorQA");
                File.WriteAllBytes("Library/SkinGeneratorQA/continuous-bruises.png", texture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
#endif
