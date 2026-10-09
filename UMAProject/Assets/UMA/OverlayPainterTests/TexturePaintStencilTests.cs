#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintStencilTests
    {
        private static Texture2D Image()
        {
            var image=new Texture2D(64,64,TextureFormat.RGBA32,false,true){wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Point};
            var pixels=new Color[4096];for(int y=0;y<64;y++)for(int x=0;x<64;x++)pixels[y*64+x]=x<32?Color.white:Color.black;
            image.SetPixels(pixels);image.Apply();return image;
        }
        private static TexturePaintStencil Stencil(Texture2D image) => new TexturePaintStencil
        {enabled=true,texture=image,size=Vector2.one,viewProjection=Matrix4x4.Ortho(0,1,0,1,-1,1)};
        [Test] public void CameraStencilRendersCoverageAndRotationInUVSpace()
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.clear);var image=Image();
            try
            {
                var settings=Stencil(image);
                using(var renderer=new TexturePaintStencilRenderer())
                {
                    var mask=(RenderTexture)renderer.GetMask(settings,fixture.set,64,64);
                    var pixels=TexturePaintGpuTestFixture.ReadPixels(mask);
                    Assert.That(pixels[32*64+16].r,Is.GreaterThan(.99));Assert.That(pixels[32*64+48].r,Is.LessThan(.01));
                    Assert.That(renderer.GetMask(settings,fixture.set,64,64),Is.SameAs(mask),"One shared stencil mask per surface/resolution/stroke.");
                }
                settings.rotation=90;
                using(var renderer=new TexturePaintStencilRenderer())
                {
                    var pixels=TexturePaintGpuTestFixture.ReadPixels((RenderTexture)renderer.GetMask(settings,fixture.set,64,64));
                    Assert.That(pixels[48*64+32].r,Is.GreaterThan(.99));Assert.That(pixels[16*64+32].r,Is.LessThan(.01));
                }
            }
            finally{Object.DestroyImmediate(image);}
        }
        [Test] public void BatchedStampsRespectTheSameCameraStencil()
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.clear);using var engine=TexturePaintGpuTestFixture.CreateEngine();var image=Image();var brush=fixture.CreateBrush(1,1);
            try
            {
                var context=fixture.CreateContext(brush,TexturePaintTool.Paint,Color.red,strength:1);context.stencil=Stencil(image);
                Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceTexture),Is.True);
                var sample=TexturePaintGpuTestFixture.CenterSample();
                Assert.That(engine.ApplySamples(new[]{new StrokeDispatchSample(sample,.4f,default),new StrokeDispatchSample(sample,.4f,default)}),Is.True);engine.EndStroke();
                var pixels=fixture.ReadPixels();Assert.That(pixels[32*64+24].a,Is.GreaterThan(.99));Assert.That(pixels[32*64+40].a,Is.LessThan(.01));
                Assert.That(engine.Performance.computeDispatches,Is.GreaterThan(0));
            }
            finally{Object.DestroyImmediate(image);Object.DestroyImmediate(brush);}
        }
        [TestCase(false)] [TestCase(true)] public void StencilPaintAndUndoMatchOnGPUAndCPU(bool cpu)
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.clear);
            using var engine=cpu?new PaintingEngine(null,null,null):TexturePaintGpuTestFixture.CreateEngine();
            var image=Image();var brush=fixture.CreateBrush(1,1);brush.size=1;
            try
            {
                var layer=fixture.set.AddLayer("Stencil paint");var context=fixture.CreateContext(brush,TexturePaintTool.Paint,Color.red,strength:1);
                context.stencil=Stencil(image);context.limitStrokeCoverage=true;
                Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay),Is.True);
                Assert.That(engine.ApplySample(TexturePaintGpuTestFixture.CenterSample(),.4f),Is.True);engine.EndStroke();
                var target=layer.channels[TexturePaintChannel.Albedo].Front;var pixels=TexturePaintGpuTestFixture.ReadPixels(target);
                Assert.That(pixels[32*64+24].a,Is.GreaterThan(.99));Assert.That(pixels[32*64+40].a,Is.LessThan(.01));
                Assert.That(layer.strokes[0].stencil,Is.Not.SameAs(context.stencil));Assert.That(layer.strokes[0].stencil.viewProjection,Is.EqualTo(context.stencil.viewProjection));
                Assert.That(engine.Undo(),Is.True);Assert.That(TexturePaintGpuTestFixture.ReadPixels(target)[32*64+24].a,Is.LessThan(.01));
                Assert.That(engine.Redo(),Is.True);Assert.That(TexturePaintGpuTestFixture.ReadPixels(target)[32*64+24].a,Is.GreaterThan(.99));
            }
            finally{Object.DestroyImmediate(image);Object.DestroyImmediate(brush);}
        }
    }
}
#endif
