#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed partial class TexturePaintHemSeamTests
    {
        [TestCaseSource(nameof(Presets))]
        public void HemDepthAndShadingAreIndependentForEveryConstruction(TexturePaintSeamPreset preset)
        {
            using var r=new Ribbon();
            var s=TexturePaintHemSeamSettings.Create(preset);r.context.hemSeam=s;s.normal=true;
            var baseline=r.Render();
            s.depthStrength=0;var flat=r.Render();
            foreach(var c in flat[TexturePaintChannel.NormalControl].Where(c=>c.a>.1f))
                Assert.That(c.r,Is.EqualTo(.5f).Within(.001f),preset+": zero depth must flatten all cloth and thread");
            foreach(var c in flat[TexturePaintChannel.Normal].Where(c=>c.a>.1f))
                Assert.That(Vector3.Distance(new Vector3(c.r,c.g,c.b),new Vector3(.5f,.5f,1)),Is.LessThan(.003f));
            Assert.That(Difference(baseline[TexturePaintChannel.Albedo],flat[TexturePaintChannel.Albedo]),Is.LessThan(.00001f));
            s.depthStrength=3;var deep=r.Render();
            float Relief(Color[] image)=>image.Where(c=>c.a>.1f).Max(c=>Mathf.Abs(c.r-.5f));
            Assert.That(Relief(deep[TexturePaintChannel.NormalControl]),Is.GreaterThan(Relief(baseline[TexturePaintChannel.NormalControl])+.0005f));
            Assert.That(Difference(baseline[TexturePaintChannel.Albedo],deep[TexturePaintChannel.Albedo]),Is.LessThan(.00001f));
            s.shadingStrength=0;var unshaded=r.Render();
            Assert.That(Difference(deep[TexturePaintChannel.NormalControl],unshaded[TexturePaintChannel.NormalControl]),Is.LessThan(.00001f));
            Assert.That(unshaded[TexturePaintChannel.AmbientOcclusion].Max(c=>c.a),Is.Zero);
            s.shadingStrength=3;var shaded=r.Render();
            Assert.That(Difference(shaded[TexturePaintChannel.Albedo],unshaded[TexturePaintChannel.Albedo]),Is.GreaterThan(.0001f));
            Assert.That(Difference(deep[TexturePaintChannel.NormalControl],shaded[TexturePaintChannel.NormalControl]),Is.LessThan(.00001f));
        }

        [Test]
        public void MaximumHemDepthKeepsEightCrossingRowsAndCordProfilesUnclipped()
        {
            using var r=new Ribbon();var s=TexturePaintHemSeamSettings.Create(TexturePaintSeamPreset.Piping);
            r.context.hemSeam=s;s.relief=.15f;s.depthStrength=4;s.shadingStrength=3;s.masks=true;
            s.rows.Clear();
            for(int i=0;i<8;i++)s.rows.Add(new TexturePaintSeamStitchRow{offset=0,relief=2,thickness=.1f,
                pattern=i%2==0?TexturePaintStitchPattern.Zigzag:TexturePaintStitchPattern.CoverLooper,phase=i/8f});
            var result=r.Render();var height=result[TexturePaintChannel.NormalControl];
            Assert.That(height.Where(c=>c.a>.1f).Max(c=>c.r),Is.InRange(.51f,.98f));
            Assert.That(height.Where(c=>c.a>.1f).Min(c=>c.r),Is.GreaterThan(.02f));
            Assert.That(height.Where(c=>c.a>.9f).Select(c=>c.r).Distinct().Count(),Is.GreaterThan(20));
            foreach(var c in result[TexturePaintChannel.Custom])
            {Assert.That(c.r,Is.InRange(0f,1f));Assert.That(c.g,Is.InRange(0f,1f));Assert.That(c.b,Is.InRange(0f,1f));}
        }

        [Test]
        public void HemResponseDefaultsAndIntentionalZeroSurviveSerialization()
        {
            var s=JsonUtility.FromJson<TexturePaintHemSeamSettings>("{\"enabled\":true,\"materialResponseVersion\":0}");
            s.Normalize();Assert.That(s.depthStrength,Is.EqualTo(1));Assert.That(s.shadingStrength,Is.EqualTo(1));
            s.depthStrength=s.shadingStrength=0;
            var loaded=JsonUtility.FromJson<TexturePaintHemSeamSettings>(JsonUtility.ToJson(s));loaded.Normalize();
            Assert.That(loaded.depthStrength,Is.Zero);Assert.That(loaded.shadingStrength,Is.Zero);
            loaded.depthStrength=3;loaded.shadingStrength=2;
            var copy=loaded.Clone();var properties=new MaterialPropertyBlock();copy.Bind(properties,TexturePaintChannel.Albedo);
            Assert.That(properties.GetVector("_HemResponse").x,Is.EqualTo(3));
            Assert.That(properties.GetVector("_HemResponse").y,Is.EqualTo(2));
        }
    }
}
#endif
