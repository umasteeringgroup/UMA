#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UMA.TexturePaint.Examples;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    [Category("ArtistGenerators")]
    public sealed class ArtistGeneratorTests
    {
        private static TexturePaintPluginParameterSet Eye()
        {var p=new TexturePaintPluginParameterSet();p.ResetToDefaults(EyeIrisGeneratorPlugin.Describe());return p;}
        [Test] public void EyeDeterminismColorIndependenceAndNeutralScleraHeight()
        {
            var p=Eye();var a=new EyeIrisMaterial(p);var b=new EyeIrisMaterial(p.Clone());
            p.Get("look").number=2;var green=new EyeIrisMaterial(p);
            for(int i=0;i<64;i++)
            {
                var uv=new Vector2(.3f+i*.006f,.56f);var x=a.Sample(uv,.0005f);var y=b.Sample(uv,.0005f);var z=green.Sample(uv,.0005f);
                Assert.That(x.color,Is.EqualTo(y.color));Assert.That(x.height,Is.EqualTo(z.height));Assert.That(x.coverage,Is.EqualTo(z.coverage));
                Assert.That(x.Channel(TexturePaintChannel.Albedo).a,Is.EqualTo(x.Channel(TexturePaintChannel.NormalControl).a));
            }
            Assert.That(a.Sample(new Vector2(.9f,.9f),.001f).height,Is.EqualTo(.5f));
            Assert.That(a.Sample(new Vector2(.5f,.5f),.001f).height,Is.EqualTo(.5f));
            Assert.That(a.Sample(new Vector2(.5f,.5f),.001f).color.maxColorComponent,Is.LessThan(.01f));
        }
        [Test] public void EyeSharedAndIndependentUVPoliciesAreExplicit()
        {
            var p=Eye();p.Get("asymmetry").number=1;var shared=new EyeIrisMaterial(p);
            p.Get("asymmetry").number=0;var same=new EyeIrisMaterial(p);
            Assert.That(shared.Sample(new Vector2(.62f,.5f),.001f).color,Is.EqualTo(same.Sample(new Vector2(.62f,.5f),.001f).color));
            p.Get("layout").number=2;var pair=new EyeIrisMaterial(p);
            Assert.That(pair.Sample(new Vector2(.31f,.5f),.001f).color,Is.EqualTo(pair.Sample(new Vector2(.81f,.5f),.001f).color));
            p.Get("asymmetry").number=1;var varied=new EyeIrisMaterial(p);
            Assert.That(varied.Sample(new Vector2(.31f,.5f),.001f).color,Is.Not.EqualTo(varied.Sample(new Vector2(.81f,.5f),.001f).color));
        }
        [Test] public void EyeTransparentCoverageAndSeedSurviveSerialization()
        {
            var p=Eye();p.Get("irisOnly").boolean=true;p.Get("seed").number=221;
            var roundTrip=JsonUtility.FromJson<TexturePaintPluginParameterSet>(JsonUtility.ToJson(p));
            var a=new EyeIrisMaterial(p);var b=new EyeIrisMaterial(roundTrip);
            Assert.That(a.Sample(Vector2.zero,.001f).coverage,Is.Zero);
            Assert.That(a.Sample(new Vector2(.61f,.53f),.001f).color,Is.EqualTo(b.Sample(new Vector2(.61f,.53f),.001f).color));
            p.Get("seed").number=222;Assert.That(new EyeIrisMaterial(p).Sample(new Vector2(.61f,.53f),.001f).color,Is.Not.EqualTo(b.Sample(new Vector2(.61f,.53f),.001f).color));
        }
        [Test] public void EyeInvalidDimensionsAreClampedAndPupilShapesDiffer()
        {
            var p=Eye();p.Get("radius").number=float.NaN;p.Get("aspect").number=0;
            var a=new EyeIrisMaterial(p).Sample(Vector2.one*.5f,.001f);Assert.That(float.IsFinite(a.height+a.color.r),Is.True);
            p=Eye();var circle=new EyeIrisMaterial(p);p.Get("pupilShape").number=1;var slit=new EyeIrisMaterial(p);
            Assert.That(slit.Sample(new Vector2(.54f,.5f),.001f).color.r,Is.GreaterThan(circle.Sample(new Vector2(.54f,.5f),.001f).color.r));
        }
        [Test] public void EmbroideryHasTransparentBoundaryRaisedThreadsAndCancellation()
        {
            var plugin=ScriptableObject.CreateInstance<TextileSurfaceGeneratorPlugin>();
            try
            {
                var p=new TexturePaintPluginParameterSet();p.ResetToDefaults(plugin.Descriptor);p.Get("embroideryStyle").number=2;
                var type=typeof(TextileSurfaceGeneratorPlugin).Assembly.GetType("UMA.TexturePaint.Examples.AdvancedEmbroidery",true);
                object Create(CancellationToken token)=>Activator.CreateInstance(type,BindingFlags.Public|BindingFlags.Instance,null,new object[]{p,null,null,token},null);
                var e=Create(CancellationToken.None);var method=type.GetMethod("Sample");
                float Field(Vector2 uv,string field)=>(float)method.Invoke(e,new object[]{uv,.001f}).GetType().GetField(field).GetValue(method.Invoke(e,new object[]{uv,.001f}));
                Assert.That(Field(Vector2.zero,"coverage"),Is.Zero);Assert.That(Field(Vector2.one*.5f,"coverage"),Is.EqualTo(1));
                Assert.That(Field(new Vector2(.51f,.53f),"height"),Is.GreaterThan(0));
                var source=new CancellationTokenSource();source.Cancel();var error=Assert.Throws<TargetInvocationException>(()=>Create(source.Token));
                Assert.That(error.InnerException,Is.TypeOf<OperationCanceledException>());source.Dispose();
            }
            finally{UnityEngine.Object.DestroyImmediate(plugin);}
        }
    }

}
namespace UMA.TexturePaint.Editor.Tests
{
    public sealed partial class TexturePaintGarmentTests
    {
        [TestCase(TexturePaintGarmentPreset.LeatherWrap)]
        [TestCase(TexturePaintGarmentPreset.ClothBandage)]
        [TestCase(TexturePaintGarmentPreset.GripTape)]
        [TestCase(TexturePaintGarmentPreset.GauzeWrap)]
        [TestCase(TexturePaintGarmentPreset.AdhesiveDressing)]
        [Category("ArtistGenerators")]
        public void WrapsProjectAndFollowPathsWithMatchingCoverage(TexturePaintGarmentPreset preset)
        {
            using var f=new Fixture(preset);
            foreach(bool path in new[]{false,true})
            {
                var images=f.Render(path);var a=images[TexturePaintChannel.Albedo];
                Assert.That(a.Max(c=>c.a),Is.GreaterThan(.9f));
                Assert.That(images[TexturePaintChannel.NormalControl].Where(c=>c.a>.9f).Max(c=>c.r),Is.GreaterThan(.5005f));
                Assert.That(images.ContainsKey(TexturePaintChannel.Normal),Is.False,"Relief must not also write direct normal.");
                for(int i=0;i<a.Length;i++)Assert.That(images[TexturePaintChannel.Roughness][i].a,Is.EqualTo(a[i].a).Within(.01f));
            }
            var s=f.settings.garment;s.wrapOverlap=.7f;s.wrapAngle=-50;
            var copy=JsonUtility.FromJson<TexturePaintGarmentSettings>(JsonUtility.ToJson(s));copy.Normalize();
            Assert.That(copy.wrapOverlap,Is.EqualTo(.7f));Assert.That(copy.wrapAngle,Is.EqualTo(-50));Assert.That(copy.kind,Is.EqualTo(TexturePaintGarmentKind.Wraps));
        }
        [Test,Category("ArtistGenerators")]
        public void WrapOverlapAndLiftChangeHeightWithoutMovingTheFootprint()
        {
            using var f=new Fixture(TexturePaintGarmentPreset.ClothBandage);var a=f.Render();
            f.settings.garment.wrapOverlap=.7f;f.settings.garment.wrapLift=1.5f;var b=f.Render();
            Assert.That(Difference(a[TexturePaintChannel.NormalControl],b[TexturePaintChannel.NormalControl]),Is.GreaterThan(.0001));
            var ac=a[TexturePaintChannel.Albedo];var bc=b[TexturePaintChannel.Albedo];
            for(int i=0;i<ac.Length;i++)Assert.That(ac[i].a,Is.EqualTo(bc[i].a).Within(.001));
        }
    }
}
#endif
