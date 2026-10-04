#if UNITY_INCLUDE_TESTS
using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UMA.TexturePaint.Examples;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed partial class PluginApiV2Tests
    {
        [TestCase(0, .25f, 1f)]
        [TestCase(0, 1.7f, .4f)]
        [TestCase(0, 8f, 3f)]
        [TestCase(1, .25f, 3f)]
        [TestCase(1, 1.7f, 1f)]
        [TestCase(1, 8f, .4f)]
        public void QuiltPuffsHaveSmoothCrownsAndNoDiagonalCrease(int pattern, float roundness, float aspect)
        {
            var plugin = ScriptableObject.CreateInstance<TextileSurfaceGeneratorPlugin>();
            try
            {
                var p = QuiltPuffParameters(plugin, pattern, roundness, aspect);
                var height = QuiltHeightSampler(p);
                var center = new Vector2(.5f, .5f);
                float crown = height(center);
                Assert.That(crown, Is.GreaterThan(.1f), "Padding must retain a raised crown.");

                // One-sided gradients must meet at the crown and across each diagonal.
                // Byte-encoded height maps cannot resolve this small derivative discontinuity.
                AssertQuiltSmooth(height, center, new Vector2(.001f, 0f), crown);
                AssertQuiltSmooth(height, center, new Vector2(0f, .001f), crown);
                AssertQuiltSmooth(height, new Vector2(.7f, .7f), new Vector2(.001f, -.001f), crown);
                AssertQuiltSmooth(height, new Vector2(.7f, .3f), new Vector2(.001f, .001f), crown);

                Assert.That(height(new Vector2(.75f, .5f)), Is.GreaterThan(crown * .4f),
                    "Padding should fill the cell rather than leave a narrow central spike.");
                foreach (Vector2 boundary in new[] { new Vector2(1f, .5f), new Vector2(.5f, 1f) })
                {
                    Vector2 step = boundary.x == 1f ? new Vector2(.001f, 0f) : new Vector2(0f, .001f);
                    Assert.That(Mathf.Abs(height(boundary)), Is.LessThan(crown * .0002f));
                    Assert.That(height(boundary - step), Is.LessThan(crown * .0002f),
                        "Padding should smoothly settle into the seam.");
                    Assert.That(height(boundary + step), Is.LessThan(crown * .0002f),
                        "Adjacent cells should meet smoothly at the seam.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(plugin); }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void QuiltRoundnessBroadensTheCrownWithoutReducingPuffHeight(int pattern)
        {
            var plugin = ScriptableObject.CreateInstance<TextileSurfaceGeneratorPlugin>();
            try
            {
                var p = QuiltPuffParameters(plugin, pattern, .25f);
                var soft = QuiltHeightSampler(p);
                p.Get("puffRoundness").number = 8f;
                var full = QuiltHeightSampler(p);
                var center = new Vector2(.5f, .5f);
                Assert.That(full(center), Is.EqualTo(soft(center)).Within(.000001f));
                foreach (Vector2 shoulder in new[] { new Vector2(.75f, .5f), new Vector2(.75f, .75f) })
                    Assert.That(full(shoulder), Is.GreaterThan(soft(shoulder) + soft(center) * .2f),
                        "More roundness should fill out the shoulders and flatten the crown.");

                float initial = full(new Vector2(.75f, .5f));
                p.Get("puffHeight").number *= 2f;
                Assert.That(QuiltHeightSampler(p)(new Vector2(.75f, .5f)),
                    Is.EqualTo(initial * 2f).Within(.000001f), "Puff Height should scale relief independently.");
            }
            finally { UnityEngine.Object.DestroyImmediate(plugin); }
        }

        [TestCase(.25f)]
        [TestCase(8f)]
        public void QuiltWavePaddingHasSmoothCrestsAndContinuousCellBoundaries(float roundness)
        {
            var plugin = ScriptableObject.CreateInstance<TextileSurfaceGeneratorPlugin>();
            try
            {
                var height = QuiltHeightSampler(QuiltPuffParameters(plugin, 2, roundness));
                var crest = new Vector2(.75f, .75f);
                float crown = height(crest);
                Assert.That(crown, Is.GreaterThan(.1f));
                AssertQuiltSmooth(height, crest, new Vector2(.001f, .001f), crown);
                Assert.That(Mathf.Abs(height(new Vector2(.5f, .5f))), Is.LessThan(crown * .0002f));
                Assert.That(height(new Vector2(.625f, .625f)), Is.GreaterThan(crown * .2f),
                    "Wave channels should have broad padding between seams.");
                var boundary = new Vector2(1f, .5f);
                AssertQuiltSmooth(height, boundary, new Vector2(.001f, 0f), crown);
                Assert.That(height(boundary - new Vector2(.001f, 0f)),
                    Is.EqualTo(height(boundary + new Vector2(.001f, 0f))).Within(crown * .001f),
                    "Wrapping a wave cell should preserve crest height.");
            }
            finally { UnityEngine.Object.DestroyImmediate(plugin); }
        }

        [Test]
        public async Task QuiltRoundnessProducesFullerPaddingInRectangularNormalControlOutput()
        {
            ResizeGarmentMaterialFixture();
            const int width = 192, height = 96;
            var normal = set.channels[TexturePaintChannel.NormalControl];
            normal.editable.Dispose();
            normal.editable = new EditableTextureTarget("Quilt relief", width, height,
                RenderTextureFormat.ARGB32, null, Color.clear);
            var plugin = ScriptableObject.CreateInstance<TextileSurfaceGeneratorPlugin>();
            try
            {
                var p = QuiltPuffParameters(plugin, 0, .25f, 1.5f);
                p.Get("scale").number = 2f;
                p.Get("puffHeight").number = .3f;
                Color[] soft = (await RenderGarmentMaterial(plugin, p))[1];
                p.Get("puffRoundness").number = 8f;
                Color[] full = (await RenderGarmentMaterial(plugin, p))[1];
                int crown = 23 * width + 31, shoulder = 23 * width + 47;
                Assert.That(full[crown].a, Is.GreaterThan(.99f));
                Assert.That(full[shoulder].a, Is.GreaterThan(.99f));
                Assert.That(full[crown].r, Is.EqualTo(soft[crown].r).Within(.005f));
                Assert.That(full[shoulder].r, Is.GreaterThan(soft[shoulder].r + .02f),
                    "The generated height channel must fill the shoulders as roundness increases.");
                p.Get("puffHeight").number = 0f;
                Color[] flat = (await RenderGarmentMaterial(plugin, p))[1];
                Assert.That(flat[shoulder].r, Is.EqualTo(.5f).Within(.005f));
            }
            finally { UnityEngine.Object.DestroyImmediate(plugin); }
        }

        private TexturePaintPluginParameterSet QuiltPuffParameters(TextileSurfaceGeneratorPlugin plugin,
            int pattern, float roundness, float aspect = 1f)
        {
            var p = host.CreateParameters(plugin);
            p.Get("mode").number = 0f;
            p.Get("quiltPattern").number = pattern;
            p.Get("scale").number = 1f;
            p.Get("aspect").number = aspect;
            p.Get("puffRoundness").number = roundness;
            p.Get("puffHeight").number = .16f;
            p.Get("stitchDepth").number = 0f;
            p.Get("breakupScale").number = .1f;
            return p;
        }

        private static Func<Vector2, float> QuiltHeightSampler(TexturePaintPluginParameterSet parameters)
        {
            Type engine = typeof(TextileSurfaceGeneratorPlugin).Assembly.GetType(
                "UMA.TexturePaint.Examples.TextileSurfaceEngine", true);
            Type settingsType = engine.GetNestedType("Settings", BindingFlags.NonPublic);
            object settings = settingsType.GetConstructor(new[] { typeof(TexturePaintPluginParameterSet) })
                .Invoke(new object[] { parameters });
            MethodInfo sampleMode = engine.GetMethod("SampleMode", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo height = sampleMode.ReturnType.GetField("height", BindingFlags.Instance | BindingFlags.Public);
            bool diamond = parameters.Integer("quiltPattern") == 1;
            float scale = parameters.Float("scale"), aspect = parameters.Float("aspect");
            return lattice =>
            {
                Vector2 q = diamond
                    ? new Vector2(lattice.x - lattice.y, lattice.x + lattice.y) * .70710678f
                    : lattice;
                Vector2 uv = new Vector2(q.x / (scale * aspect), q.y / scale);
                object sample = sampleMode.Invoke(null,
                    new[] { settings, (object)uv, null, null, new Vector2(1f / 4096f, 1f / 4096f) });
                return (float)height.GetValue(sample);
            };
        }

        private static void AssertQuiltSmooth(Func<Vector2, float> height, Vector2 point, Vector2 step, float crown)
        {
            float changeInSlope = Mathf.Abs(height(point - step) - 2f * height(point) + height(point + step));
            Assert.That(changeInSlope, Is.LessThan(crown * .0002f),
                "The puff has a cusp or hard crease at lattice position " + point + ".");
        }
    }
}
#endif
