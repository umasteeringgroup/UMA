#if UNITY_INCLUDE_TESTS
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UMA.TexturePaint.Examples;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed partial class PluginApiV2Tests
    {
        [TestCase(0f, false, 0f)]
        [TestCase(45f, false, 0f)]
        [TestCase(90f, true, 0f)]
        [TestCase(135f, true, 35f)]
        [TestCase(0f, true, -60f)]
        public void StubbleWorldDownResolvesRotatedMirroredAndSkewedSurfaceMetric(
            float uvAngle, bool mirror, float directionAngle)
        {
            Quaternion rotation = Quaternion.Euler(0, 0, uvAngle);
            Vector3 du = rotation * new Vector3(mirror ? -3f : 3f, .7f, 0);
            Vector3 dv = rotation * new Vector3(.2f, 1.2f, 0);
            CheckStubbleDirection(du, dv, Vector3.forward, directionAngle, false);
        }

        [Test]
        public void StubbleWorldDownHasStableHorizontalFallbackAndIslandEdgeDifferences()
        {
            CheckStubbleDirection(Vector3.right, Vector3.forward, Vector3.up, 0, false);
            // Adjacent texels on the other island contain wildly unrelated positions.
            // One-sided differences must retain this island's direction, including at UV=0.
            CheckStubbleDirection(Vector3.right, Vector3.up, Vector3.forward, 0, true);
        }

        private static void CheckStubbleDirection(Vector3 du, Vector3 dv, Vector3 n,
            float angle, bool islandBoundary)
        {
            const int size = 16, outWidth = 256, outHeight = 128;
            var positions = new Color[size * size];
            var normals = new Color[size * size];
            var islands = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool other = islandBoundary && x >= 8;
                    Vector3 p = du * ((x + .5f) / size) + dv * ((y + .5f) / size);
                    if (other) p += new Vector3(50, -90, 30);
                    positions[y * size + x] = new Color(p.x, p.y, p.z, 1);
                    normals[y * size + x] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
                    islands[y * size + x] = new Color(0, 0, other ? 1 : 0, 1);
                }
            TexturePaintReadOnlyMeshMap Map(TexturePaintMeshMap kind, Color[] pixels) =>
                (TexturePaintReadOnlyMeshMap)Activator.CreateInstance(typeof(TexturePaintReadOnlyMeshMap),
                    BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new object[] { "s", kind, size, size, pixels }, null);
            var method = typeof(StubbleMakerGeneratorPlugin).Assembly
                .GetType("UMA.TexturePaint.Examples.StubbleMakerGeneratorEngine")
                .GetMethod("TryWorldDirection", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (float u in new[] { .01f, 7.5f / size })
            {
                object[] args = { Map(TexturePaintMeshMap.WorldPosition, positions),
                    Map(TexturePaintMeshMap.WorldNormal, normals), Map(TexturePaintMeshMap.SurfaceId, islands),
                    new Vector2(u, .5f), outWidth, outHeight, angle, null };
                Assert.That(method.Invoke(null, args), Is.EqualTo(true));
                Vector2 pixelDirection = (Vector2)args[7];
                Vector3 actual = (du * pixelDirection.x / outWidth + dv * pixelDirection.y / outHeight).normalized;
                Vector3 expected = Vector3.ProjectOnPlane(Vector3.down, n);
                if (expected.sqrMagnitude < .000001f) expected = Vector3.forward;
                expected = Quaternion.AngleAxis(angle, n) * expected.normalized;
                Assert.That(Vector3.Dot(actual, expected), Is.GreaterThan(.999f));
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task StubbleWorldDownGeneratesAlignedStrandsAndRootRedness(bool rotated, bool mirrored)
        {
            const int size = 128;
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.one - Vector3.forward, Vector3.up };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            Vector2 UV(float x, float y)
            {
                if (mirrored) y = 1 - y;
                return rotated ? new Vector2(y, 1 - x) : new Vector2(x, y);
            }
            mesh.uv = new[] { UV(0, 0), UV(1, 0), UV(1, 1), UV(0, 1) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            set.surface.triangleIslands = new[] { 0, 0 };
            foreach (var channel in set.channels.Values)
            {
                channel.editable.Dispose();
                channel.editable = new EditableTextureTarget("Stubble world test", size, size,
                    RenderTextureFormat.ARGB32, null, Color.clear);
            }
            var plugin = ScriptableObject.CreateInstance<StubbleMakerGeneratorPlugin>();
            try
            {
                var p = host.CreateParameters(plugin);
                Assert.That(p.Integer("directionSpace", -1), Is.Zero);
                Assert.That(plugin.ResolveMeshMaps(p), Is.EqualTo(TexturePaintMeshMapMask.WorldPosition |
                    TexturePaintMeshMapMask.WorldNormal | TexturePaintMeshMapMask.SurfaceId));
                p.Get("density").number = 1;
                p.Get("hairLength").number = 12;
                p.Get("hairWidth").number = 3;
                p.Get("hairOpacity").number = 1;
                foreach (string id in new[] { "curvature", "directionVariation", "randomPositionX", "randomPositionY",
                    "lengthVariation", "widthVariation", "shadowAmount", "hairColorVariation", "rednessAmount" })
                    p.Get(id).number = 0;
                await host.ExecuteCommandAsync(plugin, store, p, null, CancellationToken.None);
                Color[] ReadAll(RenderTexture rt)
                {
                    var previous = RenderTexture.active;
                    var image = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
                    try
                    {
                        RenderTexture.active = rt;
                        image.ReadPixels(new Rect(0, 0, size, size), 0, 0); image.Apply();
                        return image.GetPixels();
                    }
                    finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
                }
                Color[] hair = ReadAll(set.layers[0].channels[TexturePaintChannel.Albedo].Front);
                float dx = 0, dy = 0;
                for (int y = 16; y < size - 16; y++)
                    for (int x = 16; x < size - 16; x++)
                    {
                        dx += Mathf.Abs(hair[y * size + x].a - hair[y * size + x + 1].a);
                        dy += Mathf.Abs(hair[y * size + x].a - hair[(y + 1) * size + x].a);
                    }
                Assert.That(rotated ? dy : dx, Is.GreaterThan((rotated ? dx : dy) * 1.4f),
                    "Generated strands must rotate with the world-to-UV mapping.");
                Assert.That(host.Undo(), Is.True);
                p.Get("rednessAmount").number = .6f;
                p.Get("rednessColor").color = Color.red;
                p.Get("rednessRadius").number = 2.4f;
                await host.ExecuteCommandAsync(plugin, store, p, null, CancellationToken.None);
                var layer = set.layers[0];
                // Roots are fixed independently of strand direction: x=(5+.5)*7.5, y=(4+.5)*13.8.
                Color root = Read(layer.channels[TexturePaintChannel.SkinColorMask].Front, 41, 62);
                Assert.That(root.a, Is.GreaterThan(.3f));
                Assert.That(root.r, Is.GreaterThan(.9f));
                Assert.That(root.g, Is.LessThan(.01f));
                Assert.That(Read(layer.channels[TexturePaintChannel.SkinColorMask].Front, 41, 68).a, Is.LessThan(.01f),
                    "Follicle redness must stay at the root, not run along the hair.");
                Assert.That(layer.channels.ContainsKey(TexturePaintChannel.NormalControl), Is.True);
                Assert.That(host.Undo(), Is.True);
                // Switching back to UV Direction restores manually aligned growth and skips maps.
                p.Get("directionSpace").number = 1;
                p.Get("rednessAmount").number = 0;
                Assert.That(plugin.ResolveMeshMaps(p), Is.EqualTo(TexturePaintMeshMapMask.None));
                await host.ExecuteCommandAsync(plugin, store, p, null, CancellationToken.None);
                Color[] manual = ReadAll(set.layers[0].channels[TexturePaintChannel.Albedo].Front);
                dx = 0; dy = 0;
                for (int y = 16; y < size - 16; y++)
                    for (int x = 16; x < size - 16; x++)
                    {
                        dx += Mathf.Abs(manual[y * size + x].a - manual[y * size + x + 1].a);
                        dy += Mathf.Abs(manual[y * size + x].a - manual[(y + 1) * size + x].a);
                    }
                Assert.That(dx, Is.GreaterThan(dy * 1.4f));
            }
            finally { UnityEngine.Object.DestroyImmediate(plugin); }
        }
    }
}
#endif
