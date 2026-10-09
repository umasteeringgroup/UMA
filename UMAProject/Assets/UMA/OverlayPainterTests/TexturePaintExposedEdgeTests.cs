#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintExposedEdgeTests
    {
        [Test]
        public void FlatSplitQuadFindsRimsButNotUvSeamsOrTriangulation()
        {
            var vertices = new[] { Vector3.zero, Vector3.right, Vector3.one - Vector3.forward,
                Vector3.zero, Vector3.one - Vector3.forward, Vector3.up };
            var edges = new TexturePaintExposedEdges(vertices, new[] { 0, 1, 2, 3, 4, 5 }, Matrix4x4.identity);
            Assert.That(edges.EdgeCount, Is.EqualTo(4));
            Assert.That(edges.Distance(new Vector3(.5f, .5f, 0), 0), Is.EqualTo(.5f).Within(.00001f));
            Assert.That(edges.Distance(new Vector3(.01f, .5f, 0), 1), Is.EqualTo(.01f).Within(.00001f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CreasesDistinguishConvexFromConcaveEvenWithSplitVertices(bool concave)
        {
            float z = concave ? 1 : -1;
            var vertices = new[] { Vector3.zero, Vector3.up, new Vector3(-1, 0, z),
                Vector3.zero, new Vector3(1, 0, z), Vector3.up };
            var edges = new TexturePaintExposedEdges(vertices, new[] { 0, 1, 2, 3, 4, 5 }, Matrix4x4.identity);
            Assert.That(edges.EdgeCount, Is.EqualTo(concave ? 4 : 5));
            float distance = edges.Distance(new Vector3(0, .5f, 0), 0);
            if (concave) Assert.That(distance, Is.GreaterThan(.1f));
            else Assert.That(distance, Is.EqualTo(0).Within(.00001f));
        }

        [Test]
        public void NearbyDisconnectedRimDoesNotBleedOntoAnotherPlate()
        {
            var vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1,1,0), Vector3.up,
                new Vector3(.5f,.4f,.001f), new Vector3(.6f,.4f,.001f), new Vector3(.5f,.6f,.001f) };
            var edges = new TexturePaintExposedEdges(vertices, new[] { 0,1,2, 0,2,3, 4,5,6 }, Matrix4x4.identity);
            Assert.That(edges.Distance(new Vector3(.5f,.5f,0), 0), Is.EqualTo(.5f).Within(.00001f));
        }

        [Test]
        public void DistancesUseWorldScaleAndIgnoreSmoothTessellation()
        {
            float z = -.05f;
            var vertices = new[] { Vector3.zero, Vector3.up, new Vector3(-1,0,z), new Vector3(1,0,z) };
            var edges = new TexturePaintExposedEdges(vertices, new[] { 0,1,2, 0,3,1 }, Matrix4x4.Scale(Vector3.one * 3));
            Assert.That(edges.EdgeCount, Is.EqualTo(4), "A shallow bend is not a crease.");
            Assert.That(edges.Distance(new Vector3(0,1.5f,0), 0), Is.GreaterThan(1));
        }

        [TestCase(60f, 20f, true)]
        [TestCase(0f, 20f, false)]
        [TestCase(60f, -20f, false)]
        public void InterruptedCreaseRequiresConvexContinuationAndDetectedEdgesAtBothEnds(float lastAngle, float middleAngle, bool bridge)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float[] angles = { 60, middleAngle, lastAngle };
            for (int i = 0; i < angles.Length; i++)
            {
                int start = vertices.Count;
                float z = -Mathf.Tan(angles[i] * Mathf.Deg2Rad * .5f);
                vertices.AddRange(new[] { new Vector3(0,i,0), new Vector3(0,i+1,0),
                    new Vector3(-1,i+.5f,z), new Vector3(1,i+.5f,z) });
                triangles.AddRange(new[] { start,start+1,start+2, start,start+3,start+1 });
            }
            var edges = new TexturePaintExposedEdges(vertices.ToArray(), triangles.ToArray(), Matrix4x4.identity);
            float distance = edges.Distance(new Vector3(0,1.5f,0), 2);
            if (bridge) Assert.That(distance, Is.EqualTo(0).Within(.00001f), "A shallow section must not break a continuous exposed crease.");
            else Assert.That(distance, Is.GreaterThan(.1f), "Do not extend into a flat end or bridge a concave section.");
        }

        [TestCase(.001f)]
        [TestCase(1f)]
        public void ThinBevelTrianglesRetainGeometricNormals(float scale)
        {
            var origin = new Vector3(.14f,1.71f,-.06f);
            var vertices = new[] { Vector3.zero, new Vector3(0,.05f,0),
                new Vector3(-.00003f,0,-.00003f), new Vector3(.00003f,0,-.00003f) };
            for (int i = 0; i < vertices.Length; i++) vertices[i] = (vertices[i] + origin) * scale;
            var edges = new TexturePaintExposedEdges(vertices, new[] {0,1,2, 0,3,1}, Matrix4x4.identity);
            Assert.That(edges.EdgeCount, Is.EqualTo(5), "Small face area must not erase a sharp bevel's normal.");
            Assert.That(edges.Distance((vertices[0]+vertices[1])*.5f, 0), Is.LessThan(1e-7f * scale));
        }

        [Test]
        public void RestoredLayersKeepOverallWearWhileNewAndResetLayersUseEdges()
        {
            var asset = CreatePlugin();
            try
            {
                var plugin = (ITexturePaintCommandExtensionV2)asset;
                var fresh = new TexturePaintPluginParameterSet(); fresh.ResetToDefaults(plugin.Descriptor);
                Assert.That(fresh.Integer("wearMode"), Is.Zero);
                var old = fresh.Clone(); old.values.RemoveAll(v => v.id == "wearMode");
                old.Get("amount").number = .23f;
                var restored = JsonUtility.FromJson<TexturePaintPluginParameterSet>(JsonUtility.ToJson(old));
                Assert.That(restored.EnsureDefaults(plugin.Descriptor), Is.True);
                Assert.That(restored.Integer("wearMode"), Is.EqualTo(1));
                Assert.That(restored.Float("amount"), Is.EqualTo(.23f));
                Assert.That(restored.EnsureDefaults(plugin.Descriptor), Is.False);
                restored.Get("wearMode").number = 0;
                restored.EnsureDefaults(plugin.Descriptor);
                Assert.That(restored.Integer("wearMode"), Is.Zero);
                restored.ResetToDefaults(plugin.Descriptor);
                Assert.That(restored.Integer("wearMode"), Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GeneratedWearFollowsRimsAndPreservesOverallOption(bool gpu)
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            const int size = 128;
            using var fixture = new TexturePaintGpuTestFixture(Color.white, size: size);
            var set = fixture.set;
            // A broad curvature signal represents the old armor-plate selection.
            var maps = set.GetProceduralMeshMaps(size);
            maps.curvature.SetPixels(Enumerable.Repeat(new Color(.9f,.9f,.9f,1), size * size).ToArray()); maps.curvature.Apply();
            var store = new TextureStore();
            var sets = (List<TextureSet>)typeof(TextureStore).GetField("sets", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(store);
            sets.Add(set);
            var layer = set.AddPluginLayer("Wear test");
            int layerIndex = set.layers.IndexOf(layer);
            var asset = CreatePlugin();
            using var host = new PluginHost { GpuGeneratorShader = gpu ? TexturePaintGpuTestFixture.LoadShader("PluginGenerators.compute") : null };
            try
            {
                var plugin = (ITexturePaintCommandExtensionV2)asset;
                var parameters = host.CreateParameters(plugin);
                foreach (string id in new[] { "breakup", "fractalEdge", "spread", "cavityInfluence", "detailWear" }) parameters.Get(id).number = 0;
                parameters.Get("amount").number = 1;
                parameters.Get("edgeWidth").number = 3;
                async Task<Color[]> Generate()
                {
                    await host.ExecutePluginLayerAsync(plugin, store, parameters,
                        new Dictionary<TextureSet, TexturePaintLayer> { { set, set.layers[layerIndex] } }, null, CancellationToken.None);
                    return TexturePaintGpuTestFixture.ReadPixels(set.layers[layerIndex].channels[TexturePaintChannel.Albedo].Front);
                }
                Color[] edges = await Generate();
                Assert.That(edges[64 * size + 1].a, Is.GreaterThan(.5f), "Physical open rim must receive wear.");
                Assert.That(edges[64 * size + 64].a, Is.LessThan(.001f), "Broad curvature / triangle diagonal must not fill the plate.");
                parameters.Get("wearMode").number = 1;
                Color[] overall = await Generate();
                Assert.That(overall[64 * size + 64].a, Is.GreaterThan(.75f), "Overall Wear keeps the broad legacy treatment.");
                parameters.values.RemoveAll(v => v.id == "wearMode");
                Assert.That(await Generate(), Is.EqualTo(overall), "Migrating saved parameters must preserve generated pixels.");
            }
            finally { sets.Clear(); store.Dispose(); UnityEngine.Object.DestroyImmediate(asset); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EdgeFadeIsLinearAndCurvesControlCoverageWithoutRimGaps(bool gpu)
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            const int size = 128;
            using var fixture = new TexturePaintGpuTestFixture(Color.white, size: size);
            var set = fixture.set;
            foreach (var channel in new[] { TexturePaintChannel.Metallic, TexturePaintChannel.Roughness, TexturePaintChannel.NormalControl })
                set.channels[channel] = new TextureChannelTarget { channel = channel, format = RenderTextureFormat.ARGBHalf,
                    editable = new EditableTextureTarget("Fade base", size, size, RenderTextureFormat.ARGBHalf, null, Color.gray) };
            var maps = set.GetProceduralMeshMaps(size);
            var distances = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float d = (x + .5f) / 64f * .03f;
                distances[y * size + x] = new Color(d, d, d, 1);
            }
            maps.exposedEdgeDistance.SetPixels(distances); maps.exposedEdgeDistance.Apply();
            var store = new TextureStore();
            var sets = (List<TextureSet>)typeof(TextureStore).GetField("sets", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(store);
            sets.Add(set); set.AddPluginLayer("Fade test");
            var asset = CreatePlugin();
            using var host = new PluginHost { GpuGeneratorShader = gpu ? TexturePaintGpuTestFixture.LoadShader("PluginGenerators.compute") : null };
            try
            {
                var plugin = (ITexturePaintCommandExtensionV2)asset;
                var p = host.CreateParameters(plugin);
                p.Get("edgeWidth").number = 3; p.Get("amount").number = 1;
                p.Get("breakup").number = 0; p.Get("detailWear").number = 0; p.Get("cavityInfluence").number = 0;
                async Task<Color[]> Generate()
                {
                    await host.ExecutePluginLayerAsync(plugin, store, p,
                        new Dictionary<TextureSet, TexturePaintLayer> { { set, set.layers[0] } }, null, CancellationToken.None);
                    return TexturePaintGpuTestFixture.ReadPixels(set.layers[0].channels[TexturePaintChannel.Albedo].Front);
                }
                void Check(Color[] pixels, Func<float, float> expected)
                {
                    for (int x = 0; x < size; x++)
                        Assert.That(pixels[64 * size + x].a, Is.EqualTo(expected((x + .5f) / 64f)).Within(.008f), "Fade at x=" + x);
                }
                // Keep default detection threshold, UV spread and edge distortion enabled:
                // none may reshape the physical distance envelope or create offset bands.
                Color[] linear = await Generate();
                Check(linear, t => Mathf.Clamp01(1 - t));
                foreach (var channel in new[] { TexturePaintChannel.Metallic, TexturePaintChannel.Roughness, TexturePaintChannel.NormalControl })
                    Check(TexturePaintGpuTestFixture.ReadPixels(set.layers[0].channels[channel].Front), t => Mathf.Clamp01(1 - t));
                p.Get("fadeLevel").number = 0;
                Check(await Generate(), t => t < 1 ? 1 : 0);
                p.Get("fadeLevel").number = .5f;
                Check(await Generate(), t => t < 1 ? 1 - t * .5f : 0);
                p.Get("fadeLevel").number = 1;
                p.Get("falloffCurve").curve = new AnimationCurve(new Keyframe(0,1), new Keyframe(.5f,.85f), new Keyframe(1,0));
                var lookup = TexturePaintPluginCurveLookup.Bake(p.Curve("falloffCurve"));
                Check(await Generate(), t => t < 1 ? Mathf.Clamp01(TexturePaintPluginCurveLookup.Evaluate(lookup,t)) : 0);
                p.Get("falloffCurve").curve = AnimationCurve.Linear(0,1,1,0);
                p.Get("breakup").number = 1; p.Get("fractalEdge").number = 1;
                Color[] broken = await Generate();
                for (int x = 0; x < 64; x++)
                {
                    float t = (x + .5f) / 64f, fade = 1 - t;
                    Assert.That(broken[64 * size + x].a, Is.InRange(fade * (1 - t) - .008f, fade + .008f),
                        "Breakup must respect the fade envelope and preserve the inner rim.");
                }
                p.Get("breakupCurve").curve = AnimationCurve.Constant(0,1,0);
                Check(await Generate(), t => Mathf.Clamp01(1 - t));
                p.Get("breakupCurve").curve = AnimationCurve.Constant(0,1,1);
                Color[] fullBreakup = await Generate();
                Assert.That(fullBreakup.Where((c,i) => i % size < 48).Sum(c => c.a),
                    Is.LessThan(linear.Where((c,i) => i % size < 48).Sum(c => c.a) * .95f));
            }
            finally { sets.Clear(); store.Dispose(); UnityEngine.Object.DestroyImmediate(asset); }
        }

        private static ScriptableObject CreatePlugin() => ScriptableObject.CreateInstance(
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UMA.TexturePaint.Examples.EdgeWearGeneratorPlugin")).First(t => t != null));
    }
}
#endif
