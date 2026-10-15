#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UMA.TexturePaint.Examples;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintNaturalStubbleTests
    {
        const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        static Type Engine => typeof(StubbleMakerGeneratorPlugin).Assembly.GetType("UMA.TexturePaint.Examples.StubbleMakerGeneratorEngine");
        static T Construct<T>(params object[] args) => (T)Activator.CreateInstance(typeof(T), Hidden, null, args, null);
        StubbleMakerGeneratorPlugin plugin;
        PluginHost host;
        TexturePaintPluginParameterSet p;

        [SetUp] public void Setup()
        {
            plugin = ScriptableObject.CreateInstance<StubbleMakerGeneratorPlugin>();
            host = new PluginHost(); p = host.CreateParameters(plugin);
            Set("directionSpace", 1); Set("referenceResolution", 256); Set("rootSpacing", 6);
        }
        [TearDown] public void Cleanup() { host.Dispose(); UnityEngine.Object.DestroyImmediate(plugin); }
        void Set(string id, float value) => p.Get(id, true).number = value;
        TexturePaintCommandContextV2 Context(int size, CancellationToken token = default)
        {
            var infos = new Dictionary<string, TexturePaintReadOnlyChannelInfo>();
            foreach (var ch in new[] { TexturePaintChannel.Albedo, TexturePaintChannel.Roughness, TexturePaintChannel.NormalControl })
                infos.Add("stubble|" + (int)ch, Construct<TexturePaintReadOnlyChannelInfo>("stubble", ch, size, size, false));
            var source = Construct<TexturePaintReadContextV2>(null, infos, null, null, new List<string> { "stubble" }, null, null, null);
            return Construct<TexturePaintCommandContextV2>(plugin.Descriptor, source, p, token, null, 128L * 1024 * 1024, TexturePaintPluginTarget.LayerContent);
        }
        object Field(int size)
        {
            object settings = Activator.CreateInstance(Engine.GetNestedType("Settings", BindingFlags.NonPublic), p);
            return Activator.CreateInstance(Engine.GetNestedType("NaturalHairField", BindingFlags.NonPublic), settings, Context(size), "stubble", size, size);
        }
        static List<object> Strands(object field) => ((IEnumerable)field.GetType().GetField("strands", Hidden).GetValue(field)).Cast<object>().ToList();
        static T Read<T>(object o, string name) => (T)o.GetType().GetField(name).GetValue(o);
        static Vector2 Root(object strand) => Read<Vector2[]>(strand, "points")[0];
        static string Geometry(object field) => string.Join(";", Strands(field).Select(s => string.Join(",", Read<Vector2[]>(s, "points").Select(v => v.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + v.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))));

        [Test] public void StubbleGroomIsDeterministicAndResolutionIndependent()
        {
            string baseline = Geometry(Field(256));
            Assert.That(baseline, Is.Not.Empty);
            Assert.That(Geometry(Field(256)), Is.EqualTo(baseline));
            Assert.That(Geometry(Field(1024)), Is.EqualTo(baseline));
            Assert.That(Geometry(Field(4096)), Is.EqualTo(baseline));
        }
        [Test] public void StubbleAppearanceStreamsDoNotRearrangeHairs()
        {
            string baseline = Geometry(Field(256));
            Set("grayFraction", .8f); Set("hairColorVariation", .3f); Set("opacityVariation", .7f); Set("hairRoughness", .2f);
            p.Get("hairColor").color = Color.red;
            Assert.That(Geometry(Field(256)), Is.EqualTo(baseline));
            Set("seed", 91);
            Assert.That(Geometry(Field(256)), Is.Not.EqualTo(baseline));
        }
        [Test] public void StubbleDensityRetainsSurvivingRootsAndZeroMeansZero()
        {
            Set("density", 1);
            var all = Strands(Field(256)).Select(Root).ToHashSet();
            Set("density", .35f);
            var sparse = Strands(Field(256)).Select(Root).ToList();
            Assert.That(sparse.Count, Is.InRange(1, all.Count - 1));
            Assert.That(sparse.All(all.Contains), Is.True);
            Set("density", 0); Set("profile", 1);
            Assert.That(Strands(Field(256)), Is.Empty, "Shaved Head must respect zero density.");
        }
        [Test] public void StubbleColorsRemainBrownAndRootsAreSpatiallyBalanced()
        {
            Set("density", 1); Set("hairColorVariation", .35f);
            var strands = Strands(Field(256));
            foreach (var strand in strands)
            {
                Color c = Read<Color>(strand, "color");
                Assert.That(c.r, Is.GreaterThan(c.g)); Assert.That(c.b, Is.GreaterThan(0));
            }
            float minimum = float.MaxValue;
            for (int i = 0; i < strands.Count; i++) for (int j = i + 1; j < strands.Count; j++)
                minimum = Mathf.Min(minimum, Vector2.Distance(Root(strands[i]), Root(strands[j])));
            Assert.That(minimum, Is.GreaterThanOrEqualTo(6 * .4f - .001f));
        }
        [Test] public void StubbleSettingsSurviveCloneAndJsonRoundTrip()
        {
            Set("grayFraction", .28f); Set("clippedFraction", .47f); Set("seed", 320);
            string baseline = Geometry(Field(256));
            p = p.Clone(); Assert.That(Geometry(Field(256)), Is.EqualTo(baseline));
            p = JsonUtility.FromJson<TexturePaintPluginParameterSet>(JsonUtility.ToJson(p));
            Assert.That(Geometry(Field(256)), Is.EqualTo(baseline));
            Assert.That(p.Float("grayFraction"), Is.EqualTo(.28f).Within(.0001f));
        }
        [Test] public void StubbleOldProfilesKeepLegacyWhileNewProfilesUseNatural()
        {
            Assert.That(p.Integer("renderModel"), Is.EqualTo(1));
            var old = new TexturePaintPluginParameterSet();
            old.Get("hairLength", true).number = 9;
            old.EnsureDefaults(plugin.Descriptor);
            Assert.That(old.Integer("renderModel"), Is.Zero);
            Assert.That(old.Float("hairLength"), Is.EqualTo(9));
            old.Get("renderModel").number = 1;
            old.EnsureDefaults(plugin.Descriptor);
            Assert.That(old.Integer("renderModel"), Is.EqualTo(1));
        }
        [Test] public async Task StubbleChannelsShareCoverageWithoutRoughnessDoubleAttenuation()
        {
            Set("shadowAmount", 0); Set("rednessAmount", 0); Set("follicleDepth", 0);
            Set("roughnessVariation", 0); Set("hairRoughness", .23f);
            var context = Context(256); await plugin.ExecuteAsync(context);
            var data = new Dictionary<TexturePaintChannel, Color[]>();
            foreach (var ch in new[] { TexturePaintChannel.Albedo, TexturePaintChannel.Roughness, TexturePaintChannel.NormalControl }) data[ch] = new Color[256 * 256];
            var commands = (IEnumerable)context.GetType().GetMethod("SealAndSnapshot", Hidden).Invoke(context, null);
            foreach (var command in commands)
            {
                var t = command.GetType(); t.GetMethod("MaterializeCompactPixels").Invoke(command, null);
                var ch = Read<TexturePaintChannel>(command, "channel"); var rect = Read<RectInt>(command, "rect");
                var compact = Read<Color32[]>(command, "compactPixels"); var floating = Read<Color[]>(command, "pixels");
                for (int y = 0; y < rect.height; y++) for (int x = 0; x < rect.width; x++)
                    data[ch][(y + rect.y) * 256 + x] = compact == null ? floating[y * rect.width + x] : compact[y * rect.width + x];
            }
            int visible = 0;
            for (int i = 0; i < 256 * 256; i++)
            {
                Color a = data[TexturePaintChannel.Albedo][i], r = data[TexturePaintChannel.Roughness][i], n = data[TexturePaintChannel.NormalControl][i];
                Assert.That(r.a, Is.EqualTo(a.a).Within(1f / 255));
                Assert.That(n.a, Is.EqualTo(a.a).Within(1f / 255));
                if (a.a <= .01f) continue;
                visible++; Assert.That(r.r, Is.EqualTo(.23f).Within(1f / 255));
                Assert.That(n.r, Is.InRange(.5f, .5f + p.Float("hairHeight") + .001f));
            }
            Assert.That(visible, Is.GreaterThan(100));
        }
        [Test] public async Task StubbleCancellationDoesNotPublishPartialCommands()
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var context = Context(256, cancellation.Token);
            bool canceled = false;
            try { await plugin.ExecuteAsync(context); }
            catch (OperationCanceledException) { canceled = true; }
            Assert.That(canceled, Is.True);
            var commands = (IEnumerable)context.GetType().GetMethod("SealAndSnapshot", Hidden).Invoke(context, null);
            Assert.That(commands.Cast<object>(), Is.Empty);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void StubblePresetsPreservePlacementAndSeed(int index)
        {
            Set("placementX", .123f); Set("placementY", .789f); Set("seed", 312);
            StubbleMakerPresets.Apply(p, index);
            Assert.That(p.Float("placementX"), Is.EqualTo(.123f).Within(.0001f));
            Assert.That(p.Integer("seed"), Is.EqualTo(312));
            Assert.That(p.Float("hairHeight"), Is.LessThan(.05f));
        }
    }
}
#endif
