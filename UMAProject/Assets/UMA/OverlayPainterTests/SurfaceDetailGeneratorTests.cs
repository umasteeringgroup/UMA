#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;
using UMA.TexturePaint.Examples;
using UnityEngine;
namespace UMA.TexturePaint.Tests
{
    [Category("SurfaceDetailGenerators")]
    public sealed class SurfaceDetailGeneratorTests
    {
        private static TexturePaintPluginParameterSet Settings(bool splash)
        {
            var plugin=splash?(SurfaceDepositionPlugin)ScriptableObject.CreateInstance<SplashDepositionGeneratorPlugin>():ScriptableObject.CreateInstance<SurfaceResidueGeneratorPlugin>();
            try{var p=new TexturePaintPluginParameterSet();p.ResetToDefaults(plugin.Descriptor);return p;}finally{UnityEngine.Object.DestroyImmediate(plugin);}
        }
        [TestCase(false)][TestCase(true)]
        public void DeterministicAndColorIndependentWithAlignedChannels(bool splash)
        {
            var p=Settings(splash);var a=new SurfaceDepositionMaterial(p,splash);
            var loaded=JsonUtility.FromJson<TexturePaintPluginParameterSet>(JsonUtility.ToJson(p));var b=new SurfaceDepositionMaterial(loaded,splash);
            loaded.Get("tint").color=Color.green;var color=new SurfaceDepositionMaterial(loaded,splash);
            int occupied=0;
            for(int y=0;y<50;y++)for(int x=0;x<50;x++)
            {
                var uv=new Vector2((x+.5f)/50,(y+.5f)/50);var s=a.Sample(uv,.001f);var t=b.Sample(uv,.001f);var c=color.Sample(uv,.001f);
                Assert.That(s.coverage,Is.EqualTo(t.coverage));Assert.That(s.height,Is.EqualTo(c.height));Assert.That(s.coverage,Is.EqualTo(c.coverage));
                Assert.That(s.Channel(TexturePaintChannel.NormalControl).a,Is.EqualTo(s.Channel(TexturePaintChannel.Roughness).a));
                Assert.That(float.IsFinite(s.height+s.roughness+s.coverage),Is.True);if(s.coverage>.1f)occupied++;
            }
            Assert.That(occupied,Is.GreaterThan(10));Assert.That(a.Sample(new Vector2(-1,0),.001f).coverage,Is.Zero);
        }
        [Test]
        public void DryingChangesMaterialWithoutMovingTheDeposit()
        {
            var p=Settings(true);p.Get("drying").number=0;var wet=new SurfaceDepositionMaterial(p,true);p.Get("drying").number=1;var dry=new SurfaceDepositionMaterial(p,true);
            var uv=new Vector2(.5f,.6f);var a=wet.Sample(uv,.001f);var b=dry.Sample(uv,.001f);
            Assert.That(a.coverage,Is.EqualTo(b.coverage));Assert.That(b.roughness,Is.GreaterThan(a.roughness));Assert.That(b.height,Is.LessThan(a.height));
        }
        [Test]
        public void ResidueDefaultsToRoughnessAndOptionalRelief()
        {var p=Settings(false);Assert.That(p.Boolean("outputRoughness"),Is.True);Assert.That(p.Boolean("outputAlbedo"),Is.False);Assert.That(p.Boolean("outputNormalControl"),Is.False);}
        [TestCase(false)][TestCase(true)]
        public void SpatialIndexDoesNotClipExtremeStretchedMarks(bool splash)
        {
            var p=Settings(splash);p.Get("count").number=1;p.Get("scatter").number=0;p.Get("size").number=.08f;
            p.Get("variation").number=0;p.Get("rotation").number=37;
            if(splash){p.Get("impact").number=1;p.Get("droplets").number=1;p.Get("gravity").number=3;}
            else{p.Get("style").number=1;p.Get("wipe").number=2;}
            var accelerated=new SurfaceDepositionMaterial(p,splash);var unculled=new SurfaceDepositionMaterial(p,splash);
            // Compare the spatial index with exhaustive evaluation of the same single mark.
            var bins=(Array)typeof(SurfaceDepositionMaterial).GetField("bins",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(unculled);
            object markList=null;foreach(var cell in bins)if(cell!=null){markList=cell;break;}
            Assert.That(markList,Is.Not.Null);for(int i=0;i<bins.Length;i++)bins.SetValue(markList,i);
            for(int y=0;y<192;y++)for(int x=0;x<192;x++)
            {
                var uv=new Vector2((x+.5f)/192,(y+.5f)/192);
                Assert.That(accelerated.Sample(uv,1f/192).coverage,Is.EqualTo(unculled.Sample(uv,1f/192).coverage),"Culling clipped a stretched mark at "+uv);
            }
        }
        [Test]
        public void SeedAndRunoffChangeGeometryAndZeroCoverageClears()
        {
            var p=Settings(true);var a=new SurfaceDepositionMaterial(p,true);p.Get("seed").number=35;p.Get("gravityAngle").number=90;var b=new SurfaceDepositionMaterial(p,true);float difference=0;
            for(int y=0;y<50;y++)for(int x=0;x<50;x++){var uv=new Vector2(x/50f,y/50f);difference+=Mathf.Abs(a.Sample(uv,.001f).coverage-b.Sample(uv,.001f).coverage);}
            Assert.That(difference,Is.GreaterThan(2));p.Get("amount").number=0;Assert.That(new SurfaceDepositionMaterial(p,true).Sample(new Vector2(.5f,.6f),.001f).coverage,Is.Zero);
        }
    }
}
namespace UMA.TexturePaint.Editor.Tests
{
    public sealed partial class TexturePaintGarmentTests
    {
        [TestCase(TexturePaintGarmentPreset.TigWeld)][TestCase(TexturePaintGarmentPreset.MigWeld)]
        [TestCase(TexturePaintGarmentPreset.SolderSeam)][TestCase(TexturePaintGarmentPreset.BrazedSeam)]
        [TestCase(TexturePaintGarmentPreset.FloralLace)][TestCase(TexturePaintGarmentPreset.GothicLace)][TestCase(TexturePaintGarmentPreset.ScallopedLace)]
        [Category("SurfaceDetailGenerators")]
        public void SurfaceDetailPathsAndProjectionsHaveMatchingCoverageAndRaisedRelief(TexturePaintGarmentPreset preset)
        {
            using var f=new Fixture(preset);
            // Judge openwork at a resolved footprint. At 128px the fine net threads share
            // texels with their holes, so antialiased partial coverage is the correct result.
            if(TexturePaintGarmentSettings.Kind(preset)==TexturePaintGarmentKind.Lace)
                foreach(var target in f.gpu.set.channels.Values)
                {target.editable.Dispose();target.editable=new EditableTextureTarget(target.channel.ToString(),512,512,RenderTextureFormat.ARGBHalf,null,Color.clear);}
            foreach(bool path in new[]{false,true})
            {
                var data=f.Render(path);var a=data[TexturePaintChannel.Albedo];var h=data[TexturePaintChannel.NormalControl];var r=data[TexturePaintChannel.Roughness];
                Assert.That(a.Max(c=>c.a),Is.GreaterThan(.4f));Assert.That(h.Max(c=>c.r),Is.GreaterThan(.5005f));
                Assert.That(data.ContainsKey(TexturePaintChannel.Normal),Is.False);
                for(int i=0;i<a.Length;i++)Assert.That(r[i].a,Is.EqualTo(a[i].a).Within(.01f));
                if(TexturePaintGarmentSettings.Kind(preset)==TexturePaintGarmentKind.Lace)
                    Assert.That(a.Count(c=>c.a<.01f),Is.GreaterThan(a.Length/4),"Lace must leave genuine open holes.");
            }
            var json=JsonUtility.ToJson(f.settings.garment);var restored=JsonUtility.FromJson<TexturePaintGarmentSettings>(json);restored.Normalize();
            Assert.That(restored.preset,Is.EqualTo(preset));Assert.That(restored.weldHeat,Is.EqualTo(f.settings.garment.weldHeat));
        }
    }
}
#endif
