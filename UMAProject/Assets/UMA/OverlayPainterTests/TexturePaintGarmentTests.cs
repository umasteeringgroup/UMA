#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace UMA.TexturePaint.Editor.Tests
{
    public sealed partial class TexturePaintGarmentTests
    {
        private sealed class Fixture:IDisposable
        {
            public const int Size=128;
            public readonly TexturePaintGpuTestFixture gpu=new TexturePaintGpuTestFixture(Color.clear,size:Size);
            public readonly TexturePaintProjectionRenderer renderer=new TexturePaintProjectionRenderer();
            public readonly TexturePaintLayer layer;
            public readonly TexturePaintProjectionSettings settings;
            public Fixture(TexturePaintGarmentPreset preset)
            {
                gpu.mesh.vertices=gpu.mesh.vertices.Select(v=>v*.2f).ToArray();gpu.mesh.RecalculateBounds();
                foreach(TexturePaintChannel c in Enum.GetValues(typeof(TexturePaintChannel)))if(c!=TexturePaintChannel.Albedo)
                    gpu.set.channels[c]=new TextureChannelTarget{channel=c,format=RenderTextureFormat.ARGBHalf,
                        editable=new EditableTextureTarget(c.ToString(),Size,Size,RenderTextureFormat.ARGBHalf,null,Color.clear)};
                layer=gpu.set.AddProjectionLayer();
                settings=new TexturePaintProjectionSettings{placed=true,position=new Vector3(.1f,.1f,0),width=.2f,height=.2f,depth=.1f,
                    fade=TexturePaintProjectionFade.None,depthFade=0,firstSurfaceOnly=false,connectedSurfaceOnly=false,
                    garment=TexturePaintGarmentSettings.Create(preset)};
            }
            public Dictionary<TexturePaintChannel,Color[]> Render(bool path=false,bool closed=false)
            {
                if(path)
                {
                    var shader=Shader.Find("Hidden/UMA/TexturePaint/RibbonProjection");Assert.That(ShaderUtil.ShaderHasError(shader),Is.False,string.Join(";",ShaderUtil.GetShaderMessages(shader).Select(x=>x.message)));
                    using var engine=new PaintingEngine(null,null,null,shader);var brush=gpu.CreateBrush(1,1,shape:BrushPreset.Shape.Square);brush.size=.1f;
                    try
                    {
                        var context=gpu.CreateContext(brush,TexturePaintTool.Paint,Color.white,strength:1);context.garment=settings.garment;
                        context.projectionDepth=1;context.garment.PopulateSources(context);context.replaceLayer=layer;context.replaceHistoryGroup=true;context.historyGroupKey="garment";
                        Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay),Is.True,engine.LastStrokeError);
                        var segments=new[]{new TexturePaintRibbonSegment {leftStartAlong=new Vector4(0,0,0,0),rightStartFlow=new Vector4(.2f,0,0,1),
                            leftEndAlong=new Vector4(0,.2f,0,1),rightEndFlow=new Vector4(.2f,.2f,0,1),normalStartPressure=new Vector4(0,0,1,1),
                            normalEndPressure=new Vector4(0,0,1,1),colorStart=Color.white,colorEnd=Color.white}};
                        Assert.That(engine.ApplyRibbon(segments,Array.Empty<StrokeSample>(),true,false,closed),Is.True);engine.EndStroke(true);
                    }
                    finally{Object.DestroyImmediate(brush);}
                }
                else
                {
                    var shader=Shader.Find("Hidden/UMA/TexturePaint/Projection");Assert.That(ShaderUtil.ShaderHasError(shader),Is.False,string.Join(";",ShaderUtil.GetShaderMessages(shader).Select(x=>x.message)));
                    Assert.That(renderer.Generate(new[]{gpu.set},new[]{gpu.set},new[]{layer},settings,out string error),Is.True,error);
                }
                return layer.channels.ToDictionary(x=>x.Key,x=>TexturePaintGpuTestFixture.ReadPixels(x.Value.Front));
            }
            public void Dispose(){renderer.Dispose();gpu.Dispose();}
        }
        private static IEnumerable<TestCaseData> Presets()
        {foreach(TexturePaintGarmentPreset p in Enum.GetValues(typeof(TexturePaintGarmentPreset)))foreach(bool path in new[]{false,true})yield return new TestCaseData(p,path);}
        private static float Difference(Color[] a,Color[] b)
        {float d=0;for(int i=0;i<a.Length;i++)d+=Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b)+Mathf.Abs(a[i].a-b[i].a);return d/a.Length;}
        [TestCaseSource(nameof(Presets))]
        public void AllPresetsRenderFiniteCoordinatedOutputsInProjectionAndPath(TexturePaintGarmentPreset preset,bool path)
        {
            using var f=new Fixture(preset);var s=f.settings.garment;
            s.albedo=s.normal=s.ao=s.roughnessOutput=s.metallic=s.height=s.masks=true;
            var images=f.Render(path);Assert.That(images.Count,Is.EqualTo(7));
            foreach(var pair in images)
            {
                foreach(var c in pair.Value)
                {
                    Assert.That(float.IsFinite(c.r)&&float.IsFinite(c.g)&&float.IsFinite(c.b)&&float.IsFinite(c.a),Is.True,pair.Key.ToString());
                    Assert.That(c.a,Is.InRange(0f,1.001f));
                    if(TexturePaintChannelUtility.IsGrayscale(pair.Key)){Assert.That(c.r,Is.EqualTo(c.g).Within(.001));Assert.That(c.g,Is.EqualTo(c.b).Within(.001));}
                    if(pair.Key==TexturePaintChannel.Normal&&c.a>.1f)Assert.That(new Vector3(c.r*2-1,c.g*2-1,c.b*2-1).magnitude,Is.EqualTo(1).Within(.008f));
                }
            }
            Assert.That(images[TexturePaintChannel.Albedo].Max(c=>c.a),Is.GreaterThan(.02f),preset+" must be visible");
            Assert.That(images[TexturePaintChannel.Normal].Max(c=>c.a),Is.GreaterThan(.02f));
        }
        [TestCase(TexturePaintGarmentPreset.TensionFolds)] [TestCase(TexturePaintGarmentPreset.PatchPocket)]
        [TestCase(TexturePaintGarmentPreset.MetalZipper)] [TestCase(TexturePaintGarmentPreset.FrayedTear)]
        public void SeedAndSizeAreDeterministicAndZeroAmountClears(TexturePaintGarmentPreset preset)
        {
            using var f=new Fixture(preset);var a=f.Render()[TexturePaintChannel.Albedo];
            Assert.That(Difference(a,f.Render()[TexturePaintChannel.Albedo]),Is.LessThan(.00001));
            f.settings.garment.amount=0;var zero=f.Render();foreach(var image in zero.Values)Assert.That(image.Max(c=>c.a),Is.Zero);
        }
        [TestCase(false)] [TestCase(true)]
        public void ZipperStitchesCastSeparateContactShadowsInAlbedoAndOcclusion(bool path)
        {
            using var f=new Fixture(TexturePaintGarmentPreset.MetalZipper);
            var settings=f.settings.garment;
            settings.surfaceColor=new Color(.5f,.5f,.5f,1);
            settings.threadColor=Color.white;
            // Resolve several pixels across a thread and several independent stitches along the tape.
            settings.stitchSize=.004f;settings.stitchSpacing=.025f;settings.stitchRows=0;
            settings.wear=settings.dirt=0;settings.irregularity=0;
            var bare=f.Render(path);
            settings.stitchRows=1;
            var sewn=f.Render(path);
            var rowShadows=new List<float>();
            int contacts=0,occludedContacts=0;
            for(int y=Fixture.Size*15/100;y<Fixture.Size*60/100;y++)
            {
                float strongestShadow=0;
                for(int x=Fixture.Size*70/100;x<Fixture.Size*84/100;x++)
                {
                    int i=y*Fixture.Size+x;
                    Color a=bare[TexturePaintChannel.Albedo][i],b=sewn[TexturePaintChannel.Albedo][i];
                    if(a.a<.8f||b.a<.8f)continue;
                    float darkness=((a.r+a.g+a.b)-(b.r+b.g+b.b))/3;
                    strongestShadow=Mathf.Max(strongestShadow,darkness);
                    if(darkness<=.003f)continue;
                    contacts++;
                    if(sewn[TexturePaintChannel.AmbientOcclusion][i].a>
                        bare[TexturePaintChannel.AmbientOcclusion][i].a+.003f)occludedContacts++;
                }
                rowShadows.Add(strongestShadow);
            }
            Assert.That(contacts,Is.GreaterThan(8),"Bright thread must cast a visible dark contact shadow onto the surrounding tape");
            Assert.That(occludedContacts,Is.GreaterThan(contacts/2),"Albedo contact shading and AO must describe the same stitches");
            Assert.That(rowShadows.Min(),Is.LessThan(rowShadows.Max()*.4f),
                "There must be gaps between stitch shadows, rather than a continuous dark stripe");
        }

        private static void ConfigureZipperWithoutAO(Fixture f)
        {
            // Clothing shaders can omit an AO map entirely. Contact depth must remain visible
            // in albedo, rather than depending on an output the material never samples.
            f.gpu.set.channels[TexturePaintChannel.AmbientOcclusion].Dispose();
            f.gpu.set.channels.Remove(TexturePaintChannel.AmbientOcclusion);
            var s=f.settings.garment;s.ao=false;s.surfaceColor=new Color(.65f,.65f,.65f,1);
            s.accentColor=new Color(.7f,.7f,.7f,1);s.threadColor=Color.white;
            s.detailSize=.016f;s.relief=.008f;s.opening=0;s.stitchRows=0;
            s.wear=s.dirt=s.irregularity=0;s.falloff=.001f;
        }

        private static float MeanWindow(Color[] image,Rect window,Func<Color,float> value)
        {
            float sum=0;int count=0;
            for(int y=Mathf.FloorToInt(window.yMin*Fixture.Size);y<Mathf.CeilToInt(window.yMax*Fixture.Size);y++)
                for(int x=Mathf.FloorToInt(window.xMin*Fixture.Size);x<Mathf.CeilToInt(window.xMax*Fixture.Size);x++)
                {sum+=value(image[y*Fixture.Size+x]);count++;}
            return sum/Mathf.Max(1,count);
        }

        private static float MaximumDarkening(Color[] plain,Color[] shaded,Rect window)
        {
            float maximum=0;
            for(int y=Mathf.FloorToInt(window.yMin*Fixture.Size);y<Mathf.CeilToInt(window.yMax*Fixture.Size);y++)
                for(int x=Mathf.FloorToInt(window.xMin*Fixture.Size);x<Mathf.CeilToInt(window.xMax*Fixture.Size);x++)
                {
                    int i=y*Fixture.Size+x;
                    if(plain[i].a>.95f&&shaded[i].a>.95f)
                        maximum=Mathf.Max(maximum,plain[i].grayscale-shaded[i].grayscale);
                }
            return maximum;
        }

        [TestCase(TexturePaintGarmentPreset.MetalZipper,false)]
        [TestCase(TexturePaintGarmentPreset.MetalZipper,true)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper,false)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper,true)]
        public void IndividualZipperTeethCastLocalAlbedoShadowsWithoutAnAOTexture(TexturePaintGarmentPreset preset,bool path)
        {
            using var f=new Fixture(preset);ConfigureZipperWithoutAO(f);
            var s=f.settings.garment;s.slider=1;s.recess=0;
            var plain=f.Render(path);
            s.recess=1;var shaded=f.Render(path);
            Assert.That(shaded.ContainsKey(TexturePaintChannel.AmbientOcclusion),Is.False);
            var a=plain[TexturePaintChannel.Albedo];var b=shaded[TexturePaintChannel.Albedo];
            float x0=preset==TexturePaintGarmentPreset.MetalZipper?.65f:.615f;
            var rowDarkening=new List<float>();
            // Keep the slider and end stops outside the measured tooth bank.
            for(int y=Fixture.Size/10;y<Fixture.Size*43/100;y++)
                rowDarkening.Add(MaximumDarkening(a,b,new Rect(x0,y/(float)Fixture.Size,.025f,1f/Fixture.Size)));
            float strongest=rowDarkening.Max();
            Assert.That(strongest,Is.GreaterThan(.02f),"Each tooth needs an albedo contact shadow on the adjacent tape");
            int peaks=0;
            for(int i=1;i<rowDarkening.Count-1;i++)
                if(rowDarkening[i]>.5f*strongest&&rowDarkening[i]>rowDarkening[i-1]&&rowDarkening[i]>=rowDarkening[i+1])peaks++;
            Assert.That(peaks,Is.GreaterThanOrEqualTo(3),"Separate teeth must cast separate contact shadows");
            if(preset==TexturePaintGarmentPreset.MetalZipper)
            {
                int valleys=0;
                for(int i=1;i<rowDarkening.Count-1;i++)
                    if(rowDarkening[i]<strongest*.7f&&rowDarkening[i]<rowDarkening[i-1]&&rowDarkening[i]<=rowDarkening[i+1])valleys++;
                Assert.That(valleys,Is.GreaterThanOrEqualTo(2),"Each sequence of teeth needs distinct contact-shadow valleys");
                // Pixel phase changes how many samples land at the very bottom of each gap.
                // Require their depth and repeated structure instead of an arbitrary pixel count.
                Assert.That(rowDarkening.Min(),Is.LessThan(strongest*.35f),
                    "The gaps between metal teeth must not become a continuous dark band");
            }
            else
                Assert.That(rowDarkening.Min(),Is.LessThan(strongest*.95f),
                    "Coil turns must remain distinguishable along their continuous return");
            Assert.That(MaximumDarkening(a,b,new Rect(.72f,.12f,.055f,.3f)),Is.LessThan(.002f),
                "Increasing contact shading must leave the distant tape unchanged");
            Assert.That(Difference(plain[TexturePaintChannel.NormalControl],shaded[TexturePaintChannel.NormalControl]),Is.LessThan(.00001f),
                "The shading control must not change the physical tooth height");
        }

        [TestCase(TexturePaintGarmentPreset.MetalZipper,false)]
        [TestCase(TexturePaintGarmentPreset.MetalZipper,true)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper,false)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper,true)]
        public void ZipperPullShadesItsPerimeterAndCutoutWhileKeepingTheHoleOpen(TexturePaintGarmentPreset preset,bool path)
        {
            using var f=new Fixture(preset);ConfigureZipperWithoutAO(f);
            var s=f.settings.garment;s.slider=.65f;s.recess=0;
            var plain=f.Render(path);
            s.recess=1;var shaded=f.Render(path);
            var a=plain[TexturePaintChannel.Albedo];var b=shaded[TexturePaintChannel.Albedo];
            Assert.That(MaximumDarkening(a,b,new Rect(.41f,.28f,.055f,.16f)),Is.GreaterThan(.02f),
                "The outside of the raised pull needs a localized contact shadow");
            Assert.That(MaximumDarkening(a,b,new Rect(.48f,.345f,.025f,.09f)),Is.GreaterThan(.02f),
                "The open cutout needs a contact shadow along its inner wall");
            var heights=shaded[TexturePaintChannel.NormalControl];
            float rim=MeanWindow(heights,new Rect(.445f,.37f,.02f,.04f),c=>c.r);
            float hole=MeanWindow(heights,new Rect(.512f,.38f,.02f,.025f),c=>c.r);
            Assert.That(rim,Is.GreaterThan(hole+.015f),
                "The pull's cutout must expose the lower tape or teeth rather than becoming solid raised metal");
            Assert.That(MaximumDarkening(a,b,new Rect(.72f,.25f,.055f,.35f)),Is.LessThan(.002f),
                "Pull shading must remain close to the hardware");
        }

        [TestCase(false)] [TestCase(true)]
        public void ZipperThreadContactsRemainVisibleWithoutAOAndFollowThreadOpacity(bool path)
        {
            using var f=new Fixture(TexturePaintGarmentPreset.MetalZipper);ConfigureZipperWithoutAO(f);
            var s=f.settings.garment;s.stitchSize=.004f;s.stitchSpacing=.025f;s.slider=1;s.recess=1;
            var bare=f.Render(path);
            s.stitchRows=1;s.threadColor=new Color(1,1,1,0);
            var hidden=f.Render(path);
            foreach(var channel in bare.Keys)
                Assert.That(Difference(bare[channel],hidden[channel]),Is.LessThan(.00001f),
                    "Transparent thread must not leave a contact or needle mark in "+channel);
            s.threadColor=Color.white;var sewn=f.Render(path);
            var rowDarkening=new List<float>();
            for(int y=Fixture.Size*15/100;y<Fixture.Size*55/100;y++)
                rowDarkening.Add(MaximumDarkening(bare[TexturePaintChannel.Albedo],sewn[TexturePaintChannel.Albedo],
                    new Rect(.72f,y/(float)Fixture.Size,.12f,1f/Fixture.Size)));
            Assert.That(rowDarkening.Max(),Is.GreaterThan(.015f),"Visible white thread needs dark contact shading in albedo");
            Assert.That(rowDarkening.Count(v=>v<rowDarkening.Max()*.25f),Is.GreaterThan(2),
                "Needle and thread contacts must stay local to each stitch");
        }

        [TestCase(TexturePaintGarmentPreset.MetalZipper)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper)]
        public void ZipperSliderAndPullKeepTheirPhysicalShapeWhenTheProjectionGetsLonger(TexturePaintGarmentPreset preset)
        {
            using var f=new Fixture(preset);
            f.settings.garment.slider=.5f;f.settings.garment.stitchRows=0;
            f.settings.garment.irregularity=0;f.settings.garment.wear=f.settings.garment.dirt=0;
            var square=f.Render();
            f.settings.height=.4f;
            var tall=f.Render();
            foreach(var channel in new[]{TexturePaintChannel.NormalControl,TexturePaintChannel.Metallic})
            {
                float difference=0;int samples=0;
                // The mesh and projector center stay fixed. Only the amount of zipper beyond this
                // world-space window changes, so the slider, pull, and nearby teeth must agree.
                for(int y=Fixture.Size*22/100;y<Fixture.Size*78/100;y++)
                    for(int x=Fixture.Size*30/100;x<Fixture.Size*70/100;x++)
                    {
                        int i=y*Fixture.Size+x;
                        difference+=Mathf.Abs(square[channel][i].r-tall[channel][i].r);
                        samples++;
                    }
                Assert.That(difference/samples,Is.LessThan(.0008f),channel+" must not stretch hardware with the zipper length");
            }
        }
        [Test]
        public void SnapAndRivetHaveDifferentConstructionAtTheSameSizeAndRepeatCount()
        {
            using var f=new Fixture(TexturePaintGarmentPreset.Snap);
            f.settings.garment.repeat=1;
            var snap=f.Render();
            f.settings.garment.preset=TexturePaintGarmentPreset.Rivet;
            var rivet=f.Render();
            Assert.That(Difference(snap[TexturePaintChannel.NormalControl],rivet[TexturePaintChannel.NormalControl]),
                Is.GreaterThan(.0001f),"A snap socket/cap and a solid rivet need distinct relief, not the same circular stamp");
        }
        [TestCase(TexturePaintGarmentPreset.CompressionFolds)]
        [TestCase(TexturePaintGarmentPreset.MetalZipper)]
        [TestCase(TexturePaintGarmentPreset.Waistband)]
        public void ClosedRibbonKeepsClothAndStitchDetailContinuousAtTheJoin(TexturePaintGarmentPreset preset)
        {
            using var f=new Fixture(preset);
            var settings=f.settings.garment;
            settings.detailSize=.0273f;settings.stitchSpacing=.029f;settings.stitchSize=.004f;
            settings.slider=.5f;settings.irregularity=.8f;
            var images=f.Render(path:true,closed:true);
            bool zipper=preset==TexturePaintGarmentPreset.MetalZipper;
            int first=zipper?Fixture.Size*71/100:Fixture.Size/5;
            int last=zipper?Fixture.Size*82/100:Fixture.Size*4/5;
            float Distance(Color a,Color b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)+Mathf.Abs(a.a-b.a);
            foreach(var channel in new[]{TexturePaintChannel.Albedo,TexturePaintChannel.NormalControl,TexturePaintChannel.AmbientOcclusion})
            {
                var pixels=images[channel];float closing=0,interior=0;
                for(int x=first;x<last;x++)
                {
                    closing+=Distance(pixels[x],pixels[(Fixture.Size-1)*Fixture.Size+x]);
                    for(int y=1;y<Fixture.Size;y++)
                        interior+=Distance(pixels[y*Fixture.Size+x],pixels[(y-1)*Fixture.Size+x])/(Fixture.Size-1);
                }
                Assert.That(closing,Is.LessThan(interior*2.5f+.01f),
                    channel+" must not introduce a seam larger than ordinary neighboring texture samples");
            }
        }
        [TestCase(0,false)] [TestCase(90,false)] [TestCase(180,false)] [TestCase(-90,false)]
        [TestCase(0,true)] [TestCase(90,true)] [TestCase(180,true)] [TestCase(-90,true)]
        public void WaistbandFinishesOpenIndependentlyAtTheActualPathEnds(float angle,bool path)
        {
            using var f=new Fixture(TexturePaintGarmentPreset.Waistband);
            var s=f.settings.garment;s.angle=angle;s.colorAmount=1;s.surfaceColor=Color.gray;
            s.stitchRows=0;s.wear=s.recess=0;s.masks=true;
            var finished=f.Render(path);
            s.openStart=true;var openStart=f.Render(path);
            s.openStart=false;s.openEnd=true;var openEnd=f.Render(path);
            s.openStart=true;var openBoth=f.Render(path);
            foreach(var channel in new[]{TexturePaintChannel.Albedo,TexturePaintChannel.Roughness,TexturePaintChannel.NormalControl,TexturePaintChannel.Custom})
            {
                for(int x=Fixture.Size*45/100;x<Fixture.Size*55/100;x++)
                {
                    int start=x,end=(Fixture.Size-1)*Fixture.Size+x;
                    Assert.That(finished[channel][start].a,Is.LessThan(.8f),channel+" starts with a finite cap");
                    Assert.That(finished[channel][end].a,Is.LessThan(.8f),channel+" ends with a finite cap");
                    Assert.That(openStart[channel][start].a,Is.GreaterThan(.98f),channel+" must continue through the beginning");
                    Assert.That(openStart[channel][end].a,Is.EqualTo(finished[channel][end].a).Within(.001),channel+" must retain the far finish");
                    Assert.That(openEnd[channel][end].a,Is.GreaterThan(.98f),channel+" must continue through the end");
                    Assert.That(openEnd[channel][start].a,Is.EqualTo(finished[channel][start].a).Within(.001),channel+" must retain the beginning finish");
                    Assert.That(openBoth[channel][start].a,Is.GreaterThan(.98f));
                    Assert.That(openBoth[channel][end].a,Is.GreaterThan(.98f));
                }
            }
        }
        [TestCase(0)] [TestCase(90)]
        public void WaistbandEndStitchesAndContactShadowsKeepTheirPhysicalWidthOnLongStrips(float angle)
        {
            using var f=new Fixture(TexturePaintGarmentPreset.Waistband);
            var s=f.settings.garment;s.angle=angle;s.stitchSize=.004f;s.stitchSpacing=.02f;
            s.stitchRows=1;s.threadColor=Color.white;s.irregularity=0;s.wear=0;s.colorAmount=0;s.falloff=.001f;
            var square=f.Render();
            // Keep the beginning edge at the same world position as the projector grows.
            float halfLength=angle==0 ? .32f : .48f;
            f.settings.height=.8f;f.settings.position.y+=halfLength*(.8f-.2f);
            var longStrip=f.Render();
            foreach(var channel in new[]{TexturePaintChannel.Albedo,TexturePaintChannel.AmbientOcclusion})
            {
                float difference=0;int samples=0;
                for(int y=0;y<Fixture.Size*40/100;y++)
                    for(int x=Fixture.Size*35/100;x<Fixture.Size*65/100;x++)
                    {
                        int index=y*Fixture.Size+x;
                        Color a=square[channel][index],b=longStrip[channel][index];
                        difference+=Mathf.Abs(a.r*a.a-b.r*b.a)+Mathf.Abs(a.g*a.a-b.g*b.a)+Mathf.Abs(a.b*a.a-b.b*b.a)+Mathf.Abs(a.a-b.a);
                        samples++;
                    }
                Assert.That(difference/samples,Is.LessThan(.005f),channel+" end stitches and contact shadow must not stretch with path length");
            }
        }
        [Test]
        public void ClosedWaistbandHasNoEndsRegardlessOfSavedFinishToggles()
        {
            using var f=new Fixture(TexturePaintGarmentPreset.Waistband);
            f.settings.garment.angle=90;f.settings.garment.colorAmount=1;
            var finished=f.Render(path:true,closed:true);
            f.settings.garment.openStart=f.settings.garment.openEnd=true;
            var opened=f.Render(path:true,closed:true);
            foreach(var channel in finished.Keys)Assert.That(Difference(finished[channel],opened[channel]),Is.LessThan(.00001f));
            Assert.That(finished[TexturePaintChannel.Albedo][Fixture.Size/2].a,Is.GreaterThan(.98f));
            Assert.That(finished[TexturePaintChannel.Albedo][(Fixture.Size-1)*Fixture.Size+Fixture.Size/2].a,Is.GreaterThan(.98f));
        }
        [TestCase(TexturePaintGarmentPreset.MetalZipper)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper)]
        public void ZipperStopsCanBeRemovedIndependentlyWithoutRemovingTheSlider(TexturePaintGarmentPreset preset)
        {
            using var f=new Fixture(preset);
            var s=f.settings.garment;s.slider=.5f;s.falloff=.001f;s.relief=.008f;
            var finished=f.Render()[TexturePaintChannel.NormalControl];
            s.openStart=true;var openStart=f.Render()[TexturePaintChannel.NormalControl];
            s.openStart=false;s.openEnd=true;var openEnd=f.Render()[TexturePaintChannel.NormalControl];
            float WindowDifference(Color[] a,Color[] b,int from,int to)
            {
                float difference=0;
                for(int y=from;y<to;y++)for(int x=Fixture.Size/4;x<Fixture.Size*3/4;x++)
                {
                    int i=y*Fixture.Size+x;
                    difference=Mathf.Max(difference,Mathf.Abs(a[i].r*a[i].a-b[i].r*b[i].a));
                }
                return difference;
            }
            Assert.That(WindowDifference(finished,openStart,0,Fixture.Size/10),Is.GreaterThan(.0005f),"The beginning stop must disappear");
            Assert.That(WindowDifference(finished,openStart,Fixture.Size/2,Fixture.Size),Is.LessThan(.00001f),"The far stop stays in place");
            Assert.That(WindowDifference(finished,openEnd,Fixture.Size*9/10,Fixture.Size),Is.GreaterThan(.0005f),"The far stop must disappear");
            Assert.That(WindowDifference(finished,openEnd,0,Fixture.Size/2),Is.LessThan(.00001f),"The beginning stop stays in place");
            Assert.That(WindowDifference(finished,openStart,Fixture.Size/3,Fixture.Size*2/3),Is.LessThan(.00001f),"Opening an end must retain the slider and pull");
            Assert.That(WindowDifference(finished,openEnd,Fixture.Size/3,Fixture.Size*2/3),Is.LessThan(.00001f));
        }
        [TestCase(TexturePaintGarmentPreset.MetalZipper,false)]
        [TestCase(TexturePaintGarmentPreset.MetalZipper,true)]
        [TestCase(TexturePaintGarmentPreset.PatchPocket,false)]
        [TestCase(TexturePaintGarmentPreset.WovenLabel,true)]
        public void ZeroReliefAlsoFlattensBorderThreadsAndNeedleHoles(TexturePaintGarmentPreset preset,bool path)
        {
            using var f=new Fixture(preset);
            f.settings.garment.relief=0;f.settings.garment.normal=true;
            f.settings.garment.stitchSize=.004f;f.settings.garment.stitchSpacing=.025f;
            var images=f.Render(path);
            foreach(var pixel in images[TexturePaintChannel.NormalControl].Where(c=>c.a>.05f))
                Assert.That(pixel.r,Is.EqualTo(.5f).Within(.0005f),"Zero relief must include the thread crowns and needle recesses");
            foreach(var pixel in images[TexturePaintChannel.Normal].Where(c=>c.a>.05f))
            {
                Assert.That(pixel.r,Is.EqualTo(.5f).Within(.0005f));
                Assert.That(pixel.g,Is.EqualTo(.5f).Within(.0005f));
                Assert.That(pixel.b,Is.EqualTo(1f).Within(.0005f));
            }
        }
        [Test]
        public void TransparentButtonThreadLeavesNoContactShadowBetweenTheHoles()
        {
            using var f=new Fixture(TexturePaintGarmentPreset.SewnButton);
            f.settings.garment.repeat=1;f.settings.garment.stitchSize=.005f;
            f.settings.garment.threadColor=new Color(1,1,1,0);
            var transparent=f.Render();
            f.settings.garment.threadColor=Color.white;
            var opaque=f.Render();
            float hiddenAo=0,visibleAo=0;
            for(int y=Fixture.Size/2-1;y<=Fixture.Size/2;y++)
                for(int x=Fixture.Size/2-1;x<=Fixture.Size/2;x++)
                {
                    int i=y*Fixture.Size+x;
                    hiddenAo+=transparent[TexturePaintChannel.AmbientOcclusion][i].a*.25f;
                    visibleAo+=opaque[TexturePaintChannel.AmbientOcclusion][i].a*.25f;
                }
            Assert.That(hiddenAo,Is.LessThan(.01f),"Hidden crossing threads must not leave an AO mark on the center of the button");
            Assert.That(visibleAo,Is.GreaterThan(hiddenAo+.05f),"Visible crossing threads need a contact shadow");
        }
        [Test]
        public void TransparentButtonholeThreadContributesNeitherColorNorRaisedEmbroidery()
        {
            using var f=new Fixture(TexturePaintGarmentPreset.Buttonhole);
            f.settings.garment.repeat=1;
            // Resolve embroidery relief above half-float precision on this 20 cm test patch.
            f.settings.garment.relief=.003f;
            f.settings.garment.threadColor=new Color(1,0,0,0);
            var red=f.Render();
            f.settings.garment.threadColor=new Color(0,0,1,0);
            var blue=f.Render();
            foreach(var channel in red.Keys)
                Assert.That(Difference(red[channel],blue[channel]),Is.LessThan(.00001f),
                    "Transparent thread RGB must not leak into "+channel);
            f.settings.garment.threadColor=Color.white;
            var sewn=f.Render();
            float maximumRise=0;
            for(int i=0;i<Fixture.Size*Fixture.Size;i++)
            {
                var a=blue[TexturePaintChannel.NormalControl][i];var b=sewn[TexturePaintChannel.NormalControl][i];
                if(a.a>.25f&&b.a>.25f)maximumRise=Mathf.Max(maximumRise,b.r-a.r);
            }
            Assert.That(maximumRise,Is.GreaterThan(.0005f),"Visible buttonhole embroidery must add relief that disappears with its thread opacity");
        }
        [Test]
        public void LiveWearRespondsToHeightAndSeamProtectionAndDetectsCycles()
        {
            using var f=new Fixture(TexturePaintGarmentPreset.ThighFade);
            var folds=f.gpu.set.AddLayer("Folds");var height=f.gpu.set.GetPaintTarget(TexturePaintChannel.NormalControl,TexturePaintSourceMode.SourceOverlay);
            height.Reset(null,new Color(.55f,.55f,.55f,1));
            var seam=f.gpu.set.AddLayer("Protected seam");var mask=f.gpu.set.GetPaintTarget(TexturePaintChannel.Custom,TexturePaintSourceMode.SourceOverlay);
            mask.Reset(null,new Color(0,0,0,1));
            f.settings.garment.foldInput=TexturePaintLayerReference.To(f.gpu.set,folds);f.settings.garment.foldInput.channel=TexturePaintChannel.NormalControl;f.settings.garment.foldInput.component=TexturePaintReferenceComponent.Red;
            f.settings.garment.protectionInput=TexturePaintLayerReference.To(f.gpu.set,seam);f.settings.garment.protectionInput.channel=TexturePaintChannel.Custom;f.settings.garment.protectionInput.component=TexturePaintReferenceComponent.Green;
            f.Render();using var links=new TexturePaintLinkEvaluator();links.Refresh(new[]{f.gpu.set});
            Assert.That(f.layer.linkError,Is.Null.Or.Empty);
            var a=TexturePaintGpuTestFixture.ReadPixels(f.layer.channels[TexturePaintChannel.Albedo].Front);
            height.Reset(null,new Color(.45f,.45f,.45f,1));links.Refresh(new[]{f.gpu.set});
            var b=TexturePaintGpuTestFixture.ReadPixels(f.layer.channels[TexturePaintChannel.Albedo].Front);Assert.That(Difference(a,b),Is.GreaterThan(.01));
            mask.Reset(null,new Color(0,1,0,1));links.Refresh(new[]{f.gpu.set});
            Assert.That(Difference(b,TexturePaintGpuTestFixture.ReadPixels(f.layer.channels[TexturePaintChannel.Albedo].Front)),Is.GreaterThan(.001));
            long revision=f.layer.channels[TexturePaintChannel.Albedo].Revision;links.Refresh(new[]{f.gpu.set});
            Assert.That(f.layer.channels[TexturePaintChannel.Albedo].Revision,Is.EqualTo(revision),"Unchanged dependencies must not regenerate every repaint");
            f.layer.projectionSettings.garment.foldInput=TexturePaintLayerReference.To(f.gpu.set,f.layer);
            links.Refresh(new[]{f.gpu.set});Assert.That(f.layer.linkError,Does.Contain("circular").IgnoreCase);
        }
        [Test]
        public void MissingSourceKeepsCachedWearAndReportsError()
        {
            using var f=new Fixture(TexturePaintGarmentPreset.HipWhiskers);var before=f.Render()[TexturePaintChannel.Albedo];
            f.layer.projectionSettings.garment.foldInput=new TexturePaintLayerReference{layerId="missing"};
            using var links=new TexturePaintLinkEvaluator();links.Refresh(new[]{f.gpu.set});
            Assert.That(f.layer.linkError,Does.Contain("missing").IgnoreCase);
            Assert.That(Difference(before,TexturePaintGpuTestFixture.ReadPixels(f.layer.channels[TexturePaintChannel.Albedo].Front)),Is.Zero);
        }
        [Test]
        public void LogoAlphaAndColorsRemainLocalAndSourceAssetIsUnchanged()
        {
            using var f=new Fixture(TexturePaintGarmentPreset.PrintedLogo);var image=new Texture2D(8,8,TextureFormat.RGBA32,false,true);
            try
            {
                var pixels=Enumerable.Repeat(Color.clear,64).ToArray();for(int y=2;y<6;y++)for(int x=2;x<6;x++)pixels[y*8+x]=Color.red;
                image.SetPixels(pixels);image.Apply();f.settings.garment.motif=image;f.settings.garment.damage=0;
                var output=f.Render()[TexturePaintChannel.Albedo];Assert.That(output.Any(c=>c.r>.8f&&c.g<.01f&&c.a>.9f),Is.True);
                Assert.That(output[0].a,Is.Zero);Assert.That(image.GetPixels(),Is.EqualTo(pixels));
            }
            finally{Object.DestroyImmediate(image);}
        }
        [Test]
        public void ReliefPresetsDefaultToHeightWhileSavedOutputChoicesRemainExplicit()
        {
            foreach(TexturePaintGarmentPreset preset in Enum.GetValues(typeof(TexturePaintGarmentPreset)))
            {
                var settings=TexturePaintGarmentSettings.Create(preset);
                Assert.That(settings.normal,Is.False,preset.ToString());
                Assert.That(settings.height,Is.EqualTo(settings.kind!=TexturePaintGarmentKind.Wear),preset.ToString());
            }
            Assert.That(new TexturePaintGarmentSettings().height,Is.True,"Enabling a new generator also uses height");
            foreach(TexturePaintSeamPreset preset in Enum.GetValues(typeof(TexturePaintSeamPreset)))
            {
                var settings=TexturePaintHemSeamSettings.Create(preset);
                Assert.That(settings.normal,Is.False,preset.ToString());Assert.That(settings.height,Is.True,preset.ToString());
            }
            const string saved="{\"enabled\":true,\"normal\":true,\"height\":false}";
            var garment=JsonUtility.FromJson<TexturePaintGarmentSettings>(saved);garment.Normalize();
            var seam=JsonUtility.FromJson<TexturePaintHemSeamSettings>(saved);seam.Normalize();
            Assert.That(garment.normal&&seam.normal,Is.True);Assert.That(garment.height||seam.height,Is.False,
                "Loading existing documents and custom presets must keep their authored output choices");
        }
        [TestCase(TexturePaintGarmentPreset.CompressionFolds,false)]
        [TestCase(TexturePaintGarmentPreset.CompressionFolds,true)]
        [TestCase(TexturePaintGarmentPreset.PatchPocket,false)]
        [TestCase(TexturePaintGarmentPreset.MetalZipper,false)]
        [TestCase(TexturePaintGarmentPreset.FrayedTear,true)]
        [TestCase(TexturePaintGarmentPreset.EmbroideredPatch,false)]
        public void DefaultHeightAddsReliefThroughRnmAndPreservesUnderlyingFabricNormal(TexturePaintGarmentPreset preset,bool path)
        {
            using var f=new Fixture(preset);var set=f.gpu.set;
            var compositor=new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor=compositor;set.channelPackShader=TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute");
            Vector3 basis=new Vector3(.28f,-.19f,.94f).normalized;
            Color encoded=new Color(basis.x*.5f+.5f,basis.y*.5f+.5f,basis.z*.5f+.5f,1);
            set.GetChannel(TexturePaintChannel.Normal).editable.Reset(null,encoded);
            set.GetChannel(TexturePaintChannel.NormalControl).editable.Reset(null,new Color(.5f,.5f,.5f,1));
            foreach(var channel in new[]{TexturePaintChannel.Normal,TexturePaintChannel.NormalControl})
                set.GetChannel(channel).composite=EditableTextureTarget.Create("Generator height composite",Fixture.Size,Fixture.Size,RenderTextureFormat.ARGBHalf);
            try
            {
                f.Render(path);Assert.That(f.layer.channels.ContainsKey(TexturePaintChannel.Normal),Is.False);
                Assert.That(f.layer.channels.ContainsKey(TexturePaintChannel.NormalControl),Is.True);
                set.BindPreviewTextures();
                var heights=TexturePaintGpuTestFixture.ReadPixels(set.GetChannel(TexturePaintChannel.NormalControl).PreviewTexture);
                var normals=TexturePaintGpuTestFixture.ReadPixels(set.GetVisibleTexture(TexturePaintChannel.Normal));
                var basePixels=TexturePaintGpuTestFixture.ReadPixels(set.GetChannel(TexturePaintChannel.Normal).editable.Front);
                Vector3 Decode(Color c)=>new Vector3(c.r*2-1,c.g*2-1,c.b*2-1).normalized;
                float maxRelief=0;
                for(int y=2;y<Fixture.Size-2;y++)for(int x=2;x<Fixture.Size-2;x++)
                {
                    int i=y*Fixture.Size+x;
                    var detail=new Vector3(-(heights[i+1].r-heights[i-1].r),-(heights[i+Fixture.Size].r-heights[i-Fixture.Size].r),1).normalized;
                    Vector3 t=Decode(basePixels[i])+Vector3.forward,u=Vector3.Scale(detail,new Vector3(-1,-1,1));
                    Vector3 expected=(t*Vector3.Dot(t,u)/t.z-u).normalized,actual=Decode(normals[i]);
                    Assert.That(Vector3.Distance(expected,actual),Is.LessThan(.003f),"The final output must use RNM with the existing fabric normal");
                    maxRelief=Mathf.Max(maxRelief,Vector3.Distance(Decode(basePixels[i]),actual));
                }
                Assert.That(maxRelief,Is.GreaterThan(.001f),"Default height must produce a measurable normal adjustment");
                var options=f.layer.GetChannelSettings(TexturePaintChannel.NormalControl);options.hasNormalControlStrength=true;options.normalControlStrength=0;
                set.BindPreviewTextures();var neutral=TexturePaintGpuTestFixture.ReadPixels(set.GetVisibleTexture(TexturePaintChannel.Normal));
                foreach(var color in neutral)Assert.That(Vector3.Distance(Decode(color),Decode(basePixels[0])),Is.LessThan(.003f),"Zero Height Strength preserves the fabric normal");
            }
            finally{set.compositor=null;compositor.Dispose();}
        }
        [Test]
        public void CloneLegacyDefaultsAndRoundTripPreserveIndependentReferences()
        {
            var old=JsonUtility.FromJson<TexturePaintSplineSettings>("{}");Assert.That(old.garment?.enabled??false,Is.False);
            var legacy=JsonUtility.FromJson<TexturePaintGarmentSettings>("{\"preset\":13,\"enabled\":true}");
            Assert.That(legacy.openStart||legacy.openEnd,Is.False,"Old documents retain both finite finishes");
            var s=TexturePaintGarmentSettings.Create(TexturePaintGarmentPreset.LeatherPatch);s.foldInput=new TexturePaintLayerReference{layerId="fold"};
            s.openStart=true;
            var p=new TexturePaintProjectionSettings{garment=s};var clone=p.Clone();clone.garment.foldInput.layerId="other";clone.garment.damage=.7f;
            Assert.That(p.garment.foldInput.layerId,Is.EqualTo("fold"));Assert.That(p.garment.damage,Is.EqualTo(.45f));
            var loaded=JsonUtility.FromJson<TexturePaintProjectionSettings>(JsonUtility.ToJson(p));Assert.That(JsonUtility.ToJson(loaded.garment),Is.EqualTo(JsonUtility.ToJson(p.garment)));
            s.detailSize=float.NaN;s.relief=float.PositiveInfinity;s.Normalize();Assert.That(float.IsFinite(s.detailSize)&&float.IsFinite(s.relief),Is.True);
        }
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object Invoke(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private).Invoke(target,args);
        private static void Field(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
        [TestCase(false)] [TestCase(true)]
        public void GarmentPathRoundTripsThroughDocumentAndEditorUndoWithoutOverwritingAssignedSources(bool worldSpace)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear);
            using var engine = new PaintingEngine(TexturePaintGpuTestFixture.LoadShader("StrokeRasterize.compute"),
                TexturePaintGpuTestFixture.LoadShader("Blur.compute"), TexturePaintGpuTestFixture.LoadShader("NormalTouchup.compute"), AssetDatabase.LoadAssetAtPath<Shader>(TexturePaintAssets.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader")));
            var store = new TextureStore();
            var sets = (List<TextureSet>)typeof(TextureStore).GetField("sets", Private).GetValue(store); sets.Add(fixture.set);
            var reconstruction = new MeshReconstructionResult { root = fixture.owner };
            reconstruction.surfaces.Add(fixture.set.surface); reconstruction.logicalTargets.Rebuild(reconstruction.surfaces);
            var logical = new TexturePaintLogicalLayerController(reconstruction.logicalTargets);
            var target = reconstruction.logicalTargets.Targets[0]; target.members[0].textureSets.Add(fixture.set);
            fixture.set.surface.collider = fixture.owner.AddComponent<MeshCollider>(); fixture.set.surface.collider.sharedMesh = fixture.mesh;
            var layer = fixture.set.AddSplineLayer("Loaded zipper"); layer.spline.worldSpace = worldSpace; layer.spline.useBezier = false;
            layer.spline.AddPoint(new Vector3(.5f,0,0),new Vector2(.5f,0),0,0,Vector3.forward);
            layer.spline.AddPoint(new Vector3(.5f,1,0),new Vector2(.5f,1),0,1,Vector3.forward);
            var updateAnchor = typeof(TexturePaintStageWindow).GetMethod("UpdateSplineAnchorFromUV", BindingFlags.Static | BindingFlags.NonPublic);
            updateAnchor.Invoke(null, new object[] { fixture.set, layer.spline, 0 });
            updateAnchor.Invoke(null, new object[] { fixture.set, layer.spline, 1 });
            layer.splineSettings = new TexturePaintSplineSettings { source=TexturePaintBrushSource.Color, brushSize=.4f,
                brushHardness=.75f, brushFlow=1, pathMode=TexturePaintPathMode.Ribbon, startFade=.25f, endFade=.5f };
            Assert.That(logical.LinkAndRepair(target, fixture.set, layer, null, out _), Is.True);
            var controller = new TexturePaintStageController();
            typeof(TexturePaintStageController).GetProperty("Textures").SetValue(controller,store);
            typeof(TexturePaintStageController).GetProperty("Painting").SetValue(controller,engine);
            typeof(TexturePaintStageController).GetProperty("Reconstruction").SetValue(controller,reconstruction);
            typeof(TexturePaintStageController).GetProperty("LogicalLayers").SetValue(controller,logical);
            var stage = ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            var brush = fixture.CreateBrush(); var document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            try
            {
                Field(stage,"controller",controller); Field(stage,"transientBrush",brush); Field(stage,"spline",layer.spline);
                Invoke(stage,"RestoreSplineSettings",layer.splineSettings);
                Invoke(stage,"ApplySpline");

                var sourceSettings=layer.GetChannelSettings(TexturePaintChannel.Albedo).sourceSettings;
                string beforeSource=JsonUtility.ToJson(sourceSettings);
                Invoke(stage,"BeginLightweightPathUndo",fixture.set,"Enable seam");
                var seam=TexturePaintGarmentSettings.Create(TexturePaintGarmentPreset.PatchPocket);
                seam.threadColor=Color.cyan;seam.seed=-714;seam.openStart=true;
                Field(stage,"pathGarment",seam);
                Invoke(stage,"CompleteLightweightPathEdit",fixture.set,false);Invoke(stage,"ApplySpline");
                var authored=TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front);
                Assert.That(authored.Max(c=>c.a),Is.GreaterThan(.1f));
                Assert.That(JsonUtility.ToJson(layer.GetChannelSettings(TexturePaintChannel.Albedo).sourceSettings),Is.EqualTo(beforeSource));
                TexturePaintDocumentStorage.Save(document,store);
                var saved=JsonUtility.FromJson<TexturePaintSplineSettings>(JsonUtility.ToJson(document.surfaces[0].layers[0].splineSettings));
                Assert.That(saved.garment.threadColor,Is.EqualTo(Color.cyan));Assert.That(saved.garment.seed,Is.EqualTo(-714));
                Assert.That(saved.garment.openStart,Is.True);Assert.That(saved.garment.openEnd,Is.False);
                Invoke(stage,"RestoreSplineSettings",saved);Invoke(stage,"ApplySpline");
                Assert.That(Difference(authored,TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front)),Is.LessThan(.0001f));
                int signature=(int)Invoke(stage,"GetPathRenderSignature",fixture.set,layer,layer.splineSettings);
                var modified=saved.Clone();modified.garment.edgeWidth+=.1f;modified.garment.openStart=false;modified.garment.openEnd=true;
                Assert.That((int)Invoke(stage,"GetPathRenderSignature",fixture.set,layer,modified),Is.Not.EqualTo(signature));
                Invoke(stage,"BeginLightweightPathUndo",fixture.set,"Move thread");
                Field(stage,"pathGarment",modified.garment);Invoke(stage,"CompleteLightweightPathEdit",fixture.set,false);Invoke(stage,"ApplySpline");
                Assert.That(Difference(authored,TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front)),Is.GreaterThan(.001f));
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Invoke(stage,"ApplySpline");
                Assert.That(layer.splineSettings.garment.openStart,Is.True);Assert.That(layer.splineSettings.garment.openEnd,Is.False);
                Assert.That(Difference(authored,TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front)),Is.LessThan(.0001f));
                Assert.That(Invoke(stage,"RedoLightweight"),Is.True);Invoke(stage,"ApplySpline");
                Assert.That(layer.splineSettings.garment.edgeWidth,Is.EqualTo(modified.garment.edgeWidth));
                Assert.That(layer.splineSettings.garment.openStart,Is.False);Assert.That(layer.splineSettings.garment.openEnd,Is.True);
                Invoke(stage,"BeginLightweightPathUndo",fixture.set,"Disable seam outputs");
                var disabled=modified.garment.Clone();disabled.albedo=disabled.normal=disabled.ao=disabled.roughnessOutput=disabled.metallic=disabled.height=disabled.masks=false;
                Field(stage,"pathGarment",disabled);Invoke(stage,"CompleteLightweightPathEdit",fixture.set,false);Invoke(stage,"ApplySpline");
                Assert.That(TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front).Max(c=>c.a),Is.Zero);
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Invoke(stage,"ApplySpline");
                Assert.That(TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front).Max(c=>c.a),Is.GreaterThan(.1f));
                disabled.normal=true; // This fixture only maps Albedo: stale seam output must clear.
                Field(stage,"pathGarment",disabled);Invoke(stage,"ApplySpline");
                Assert.That(TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front).Max(c=>c.a),Is.Zero);
                int layerCount=fixture.set.layers.Count;
                Invoke(stage,"CreateGarmentPath",fixture.set,TexturePaintGarmentPreset.MetalZipper);
                var created=fixture.set.layers[fixture.set.activeLayerIndex];
                Assert.That(created.splineSettings.garment.preset,Is.EqualTo(TexturePaintGarmentPreset.MetalZipper));
                Assert.That(created.splineSettings.brushSize,Is.EqualTo(.014f));
                Assert.That(created.splineSettings.startFade,Is.Zero);Assert.That(created.splineSettings.endFade,Is.Zero);
                Assert.That(created.splineSettings.brushHardness,Is.EqualTo(1));
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(fixture.set.layers.Count,Is.EqualTo(layerCount));
                Assert.That(Invoke(stage,"RedoLightweight"),Is.True);Assert.That(fixture.set.layers.Count,Is.EqualTo(layerCount+1));
                Assert.That(fixture.set.layers.Last().splineSettings.garment.preset,Is.EqualTo(TexturePaintGarmentPreset.MetalZipper));
            }
            finally
            {
                Invoke(stage,"CancelScheduledSplineReapply");Invoke(stage,"ClearLightweightHistory");Field(stage,"controller",null);
                Object.DestroyImmediate(stage);Object.DestroyImmediate(brush);Object.DestroyImmediate(document);
                sets.Clear();store.Dispose();
            }
        }

        [Test]
        public void LiveInputsRespectPaintedMasksChannelOpacityAndGroups()
        {
            using var f=new Fixture(TexturePaintGarmentPreset.ThighFade);
            var compositor=new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            f.gpu.set.compositor=compositor;
            try
            {
                var folds=f.gpu.set.AddLayer("Masked folds");var pixels=f.gpu.set.GetPaintTarget(TexturePaintChannel.NormalControl,TexturePaintSourceMode.SourceOverlay);
                pixels.Reset(null,new Color(.58f,.58f,.58f,1));var mask=f.gpu.set.AddLayerMask(folds,1);
                f.settings.garment.foldInput=TexturePaintLayerReference.To(f.gpu.set,folds);
                f.settings.garment.foldInput.channel=TexturePaintChannel.NormalControl;f.settings.garment.foldInput.component=TexturePaintReferenceComponent.Red;
                var a=f.Render()[TexturePaintChannel.Albedo];
                mask.target.Reset(null,Color.black);var b=f.Render()[TexturePaintChannel.Albedo];
                Assert.That(Difference(a,b),Is.GreaterThan(.01));
                mask.target.Reset(null,Color.white);folds.GetChannelSettings(TexturePaintChannel.NormalControl).opacity=0;
                Assert.That(Difference(b,f.Render()[TexturePaintChannel.Albedo]),Is.LessThan(.001));
                folds.GetChannelSettings(TexturePaintChannel.NormalControl).opacity=1;
                var group=f.gpu.set.AddGroup("Fold group");folds.parentId=group.id;
                f.settings.garment.foldInput=TexturePaintLayerReference.To(f.gpu.set,group);
                f.settings.garment.foldInput.channel=TexturePaintChannel.NormalControl;f.settings.garment.foldInput.component=TexturePaintReferenceComponent.Red;
                Assert.That(Difference(a,f.Render()[TexturePaintChannel.Albedo]),Is.LessThan(.001));
                using var links=new TexturePaintLinkEvaluator();links.Refresh(new[]{f.gpu.set});Assert.That(f.layer.linkError,Is.Null.Or.Empty);
            }
            finally{f.gpu.set.compositor=null;compositor.Dispose();}
        }
        [TestCase(false)] [TestCase(true)]
        public void FlipsMirrorAllGeneratedChannelsIncludingNormalDirections(bool vertical)
        {
            using var f=new Fixture(TexturePaintGarmentPreset.CompressionFolds);f.settings.garment.height=f.settings.garment.masks=true;f.settings.garment.normal=true;
            f.settings.garment.angle=20;var original=f.Render();f.settings.flipX=!vertical;f.settings.flipY=vertical;var mirrored=f.Render();
            foreach(var pair in original)
            {
                double error=0;int count=0;
                for(int y=2;y<Fixture.Size-2;y++)for(int x=2;x<Fixture.Size-2;x++)
                {
                    var a=pair.Value[y*Fixture.Size+x];var b=mirrored[pair.Key][(vertical?Fixture.Size-1-y:y)*Fixture.Size+(vertical?x:Fixture.Size-1-x)];
                    if(pair.Key==TexturePaintChannel.Normal){if(a.a<.5f||b.a<.5f)continue;if(vertical)b.g=1-b.g;else b.r=1-b.r;}
                    error+=Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)+Mathf.Abs(a.a-b.a);count++;
                }
                Assert.That(error/Math.Max(count,1),Is.LessThan(.025),pair.Key.ToString());
            }
        }
        [Test]
        public async System.Threading.Tasks.Task GroupDuplicationAndMaterialPresetsRemapGarmentInputs()
        {
            using var f=new Fixture(TexturePaintGarmentPreset.ThighFade);
            var folds=f.gpu.set.AddLayer("Fold source");f.gpu.set.GetPaintTarget(TexturePaintChannel.NormalControl,TexturePaintSourceMode.SourceOverlay).Reset(null,new Color(.56f,.56f,.56f,1));
            f.settings.garment.foldInput=TexturePaintLayerReference.To(f.gpu.set,folds);f.settings.garment.foldInput.channel=TexturePaintChannel.NormalControl;f.settings.garment.foldInput.component=TexturePaintReferenceComponent.Red;
            f.Render();var group=f.gpu.set.AddGroup("Garment");folds.parentId=f.layer.parentId=group.id;
            var copy=f.gpu.set.DuplicateLayerAt(f.gpu.set.layers.IndexOf(group));
            var copiedFold=f.gpu.set.layers.Find(x=>x.parentId==copy.id&&x.kind==TexturePaintLayerKind.Paint);
            var copiedWear=f.gpu.set.layers.Find(x=>x.parentId==copy.id&&x.kind==TexturePaintLayerKind.Projection);
            Assert.That(copiedWear.projectionSettings.garment.foldInput.layerId,Is.EqualTo(copiedFold.id));
            var preset=ScriptableObject.CreateInstance<TexturePaintMaterialPreset>();
            using var target=new Fixture(TexturePaintGarmentPreset.ThighFade);var store=new TextureStore();
            var sets=(List<TextureSet>)typeof(TextureStore).GetField("sets",Private).GetValue(store);sets.Add(target.gpu.set);
            try
            {
                target.gpu.set.persistentId="garment-destination";
                TexturePaintMaterialPresetStorage.Capture(preset,f.gpu.set,new[]{folds,f.layer},false,null);
                var result=await TexturePaintMaterialPresetStorage.ApplyAsync(preset,store,new[]{target.gpu.set},null,null,new TexturePaintMaterialPresetApplyOptions{wrapInGroup=false},null,System.Threading.CancellationToken.None);
                Assert.That(result.warnings,Is.Empty);
                var received=target.gpu.set.layers.Last();var source=target.gpu.set.layers[target.gpu.set.layers.Count-2];
                Assert.That(received.projectionSettings.garment.foldInput.layerId,Is.EqualTo(source.id));
                Assert.That(received.linkError,Is.Null.Or.Empty);
                var before=TexturePaintGpuTestFixture.ReadPixels(received.channels[TexturePaintChannel.Albedo].Front);
                source.channels[TexturePaintChannel.NormalControl].Reset(null,new Color(.44f,.44f,.44f,1));store.RefreshLayerLinks();
                Assert.That(Difference(before,TexturePaintGpuTestFixture.ReadPixels(received.channels[TexturePaintChannel.Albedo].Front)),Is.GreaterThan(.01));
            }
            finally{Object.DestroyImmediate(preset);sets.Clear();store.Dispose();}
        }
        [Test]
        public void ProjectionEditsUndoRedoAndRasterizeKeepIndependentGarmentDefinitions()
        {
            using var f=new Fixture(TexturePaintGarmentPreset.PatchPocket);f.Render();
            var stage=ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            try
            {
                var changed=f.settings.Clone();changed.garment=TexturePaintGarmentSettings.Create(TexturePaintGarmentPreset.WeltPocket);
                Assert.That(Invoke(stage,"ChangeProjectionWithHistory",f.gpu.set,f.layer,changed,false),Is.True);
                Assert.That(f.gpu.set.layers[0].projectionSettings.garment.preset,Is.EqualTo(TexturePaintGarmentPreset.WeltPocket));
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(f.gpu.set.layers[0].projectionSettings.garment.preset,Is.EqualTo(TexturePaintGarmentPreset.PatchPocket));
                Assert.That(Invoke(stage,"RedoLightweight"),Is.True);
                var current=f.gpu.set.layers[0];var before=TexturePaintGpuTestFixture.ReadPixels(current.channels[TexturePaintChannel.Albedo].Front);
                Assert.That(Invoke(stage,"RasterizeFillLayerWithHistory",f.gpu.set,current),Is.True);
                Assert.That(f.gpu.set.layers[0].projectionSettings,Is.Null);
                Assert.That(Difference(before,TexturePaintGpuTestFixture.ReadPixels(f.gpu.set.layers[0].channels[TexturePaintChannel.Albedo].Front)),Is.LessThan(.001));
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(f.gpu.set.layers[0].projectionSettings.garment.preset,Is.EqualTo(TexturePaintGarmentPreset.WeltPocket));
            }
            finally{Invoke(stage,"ClearLightweightHistory");Object.DestroyImmediate(stage);}
        }
        [Test]
        public void GarmentAssetAndDocumentPersistMotifSpriteAndConstruction()
        {
            using var f=new Fixture(TexturePaintGarmentPreset.EmbroideredPatch);
            string name="GeneratedGarment-"+Guid.NewGuid().ToString("N"),folder="Assets/UMAProjectData/Tests/OverlayPainter/"+name;
            AssetDatabase.CreateFolder("Assets/UMAProjectData/Tests/OverlayPainter",name);
            var store=new TextureStore();var sets=(List<TextureSet>)typeof(TextureStore).GetField("sets",Private).GetValue(store);sets.Add(f.gpu.set);
            try
            {
                var image=new Texture2D(8,8,TextureFormat.RGBA32,false,true);image.SetPixels(Enumerable.Repeat(Color.cyan,64).ToArray());image.Apply();
                AssetDatabase.CreateAsset(image,folder+"/Logo.asset");
                var sprite=Sprite.Create(image,new Rect(0,0,8,8),Vector2.one*.5f);sprite.name="Logo";AssetDatabase.AddObjectToAsset(sprite,image);
                f.settings.garment.motifSprite=sprite;f.settings.garment.seed=-137;f.settings.garment.openEnd=true;f.Render();
                var document=ScriptableObject.CreateInstance<TexturePaintDocument>();AssetDatabase.CreateAsset(document,folder+"/Document.asset");
                TexturePaintDocumentStorage.Save(document,store);AssetDatabase.SaveAssetIfDirty(document);
                var preset=ScriptableObject.CreateInstance<TexturePaintGarmentPresetAsset>();preset.settings=f.settings.garment.Clone();AssetDatabase.CreateAsset(preset,folder+"/Preset.asset");AssetDatabase.SaveAssetIfDirty(preset);
                AssetDatabase.ImportAsset(folder+"/Document.asset",ImportAssetOptions.ForceUpdate);AssetDatabase.ImportAsset(folder+"/Preset.asset",ImportAssetOptions.ForceUpdate);
                document=AssetDatabase.LoadAssetAtPath<TexturePaintDocument>(folder+"/Document.asset");preset=AssetDatabase.LoadAssetAtPath<TexturePaintGarmentPresetAsset>(folder+"/Preset.asset");
                Assert.That(preset.settings.seed,Is.EqualTo(-137));Assert.That(preset.settings.motifSprite,Is.SameAs(sprite));
                Assert.That(preset.settings.openStart,Is.False);Assert.That(preset.settings.openEnd,Is.True);
                var saved=document.surfaces[0].layers[0].projectionSettings.garment;Assert.That(saved.seed,Is.EqualTo(-137));Assert.That(saved.motifSprite,Is.SameAs(sprite));
                Assert.That(saved.openStart,Is.False);Assert.That(saved.openEnd,Is.True);
                using var destination=new Fixture(TexturePaintGarmentPreset.ThighFade);var restored=new TextureStore();var members=(List<TextureSet>)typeof(TextureStore).GetField("sets",Private).GetValue(restored);members.Add(destination.gpu.set);
                try{TexturePaintDocumentStorage.Restore(document,restored);Assert.That(destination.gpu.set.layers[0].projectionSettings.garment.preset,Is.EqualTo(TexturePaintGarmentPreset.EmbroideredPatch));}
                finally{members.Clear();restored.Dispose();}
            }
            finally{sets.Clear();store.Dispose();TexturePaintSpriteSource.ClearCache();AssetDatabase.DeleteAsset(folder);}
        }
        [Test]
        public void LogicalUdimInputsResolveToEachLocalFoldSource()
        {
            using var a=new Fixture(TexturePaintGarmentPreset.ThighFade);using var b=new Fixture(TexturePaintGarmentPreset.ThighFade);
            b.gpu.set.persistentId="garment-1002";b.gpu.set.surface.index=1;
            TexturePaintLayer Source(Fixture f,float value)
            {
                var layer=f.gpu.set.AddLayer("Local folds");layer.logicalLayerId="folds";layer.paintTargetId="garment";
                f.gpu.set.GetPaintTarget(TexturePaintChannel.NormalControl,TexturePaintSourceMode.SourceOverlay).Reset(null,new Color(value,value,value,1));return layer;
            }
            var first=Source(a,.57f);var second=Source(b,.43f);
            a.settings.garment.foldInput=TexturePaintLayerReference.To(a.gpu.set,first);a.settings.garment.foldInput.channel=TexturePaintChannel.NormalControl;a.settings.garment.foldInput.component=TexturePaintReferenceComponent.Red;
            var sets=new[]{a.gpu.set,b.gpu.set};
            Assert.That(a.renderer.Generate(sets,sets,new[]{a.layer,b.layer},a.settings,out var error),Is.True,error);
            var bright=TexturePaintGpuTestFixture.ReadPixels(a.layer.channels[TexturePaintChannel.Albedo].Front);
            var dark=TexturePaintGpuTestFixture.ReadPixels(b.layer.channels[TexturePaintChannel.Albedo].Front);
            Assert.That(Difference(bright,dark),Is.GreaterThan(.02));
            using var links=new TexturePaintLinkEvaluator();links.Refresh(sets);
            var before=TexturePaintGpuTestFixture.ReadPixels(a.layer.channels[TexturePaintChannel.Albedo].Front);
            second.channels[TexturePaintChannel.NormalControl].Reset(null,new Color(.57f,.57f,.57f,1));links.Refresh(sets);
            Assert.That(Difference(before,TexturePaintGpuTestFixture.ReadPixels(a.layer.channels[TexturePaintChannel.Albedo].Front)),Is.Zero);
            Assert.That(Difference(before,TexturePaintGpuTestFixture.ReadPixels(b.layer.channels[TexturePaintChannel.Albedo].Front)),Is.LessThan(.001));
        }
        [Test]
        public void GeneratedMaterialChannelsRemainAlignedAcrossMirroredUvDestinations()
        {
            using var a=new Fixture(TexturePaintGarmentPreset.PatchPocket);using var b=new Fixture(TexturePaintGarmentPreset.PatchPocket);
            b.gpu.set.persistentId="garment-mirrored-uv";b.gpu.set.surface.index=1;
            b.gpu.mesh.uv=b.gpu.mesh.uv.Select(uv=>new Vector2(uv.x,1-uv.y)).ToArray();b.gpu.mesh.RecalculateTangents();
            a.settings.garment.height=a.settings.garment.masks=true;a.settings.garment.normal=true;var sets=new[]{a.gpu.set,b.gpu.set};
            Assert.That(a.renderer.Generate(sets,sets,new[]{a.layer,b.layer},a.settings,out var error),Is.True,error);
            foreach(var pair in a.layer.channels)
            {
                var first=TexturePaintGpuTestFixture.ReadPixels(pair.Value.Front);var second=TexturePaintGpuTestFixture.ReadPixels(b.layer.channels[pair.Key].Front);
                double difference=0;int count=0;
                for(int y=3;y<Fixture.Size-3;y++)for(int x=3;x<Fixture.Size-3;x++)
                {
                    var c=first[y*Fixture.Size+x];var d=second[(Fixture.Size-1-y)*Fixture.Size+x];
                    if(pair.Key==TexturePaintChannel.Normal){if(c.a<.5f||d.a<.5f)continue;d.g=1-d.g;}
                    difference+=Mathf.Abs(c.r-d.r)+Mathf.Abs(c.g-d.g)+Mathf.Abs(c.b-d.b)+Mathf.Abs(c.a-d.a);count++;
                }
                Assert.That(difference/Math.Max(1,count),Is.LessThan(.012),pair.Key.ToString());
            }
        }
        [Test]
        public void CaptureClothingPresetContactSheet()
        {
            const int columns=8,cell=128;var sheet=new Texture2D(columns*cell,4*cell,TextureFormat.RGBA32,false,true);
            try
            {
                int index=0;foreach(TexturePaintGarmentPreset preset in Enum.GetValues(typeof(TexturePaintGarmentPreset)))
                {
                    using var f=new Fixture(preset);f.settings.garment.normal=true;
                    var images=f.Render();var pixels=images[TexturePaintChannel.Albedo];var normals=images[TexturePaintChannel.Normal];
                    for(int y=0;y<cell;y++)for(int x=0;x<cell;x++)
                    {
                        Color c=pixels[y*cell+x],n=normals[y*cell+x];float weave=.85f+.15f*Mathf.Sin((x+y)*2.1f)*Mathf.Sin((x-y)*2.7f);
                        var background=new Color(.11f,.2f,.3f)*weave;
                        var normal=new Vector3((n.r*2-1)*n.a,(n.g*2-1)*n.a,Mathf.Lerp(1,n.b*2-1,n.a)).normalized;
                        float light=.6f+.45f*Mathf.Max(0,Vector3.Dot(normal,new Vector3(-.4f,.5f,1).normalized));
                        Color output=(background*(1-c.a)+new Color(c.r,c.g,c.b)*c.a)*light;output.a=1;
                        sheet.SetPixel(index%columns*cell+x,(3-index/columns)*cell+y,output);
                    }
                    index++;
                }
                sheet.Apply();Directory.CreateDirectory("Logs/Garment");File.WriteAllBytes("Logs/Garment/Presets.png",sheet.EncodeToPNG());
            }
            finally{Object.DestroyImmediate(sheet);}
        }
    }
}
#endif
