#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintPathGeneratorTests
    {
        private sealed class Ribbon : IDisposable
        {
            public const int Size=128;
            public readonly TexturePaintGpuTestFixture fixture=new(Color.clear,size:Size);
            public readonly PaintingEngine engine;
            public readonly BrushPreset brush;
            public readonly StrokeContext context;
            public readonly TexturePaintLayer layer;
            public Ribbon(string id)
            {
                foreach(TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
                    if(channel!=TexturePaintChannel.Albedo)fixture.set.channels.Add(channel,new TextureChannelTarget
                    {channel=channel,format=RenderTextureFormat.ARGBHalf,editable=new EditableTextureTarget(channel.ToString(),Size,Size,RenderTextureFormat.ARGBHalf,null,Color.clear)});
                brush=fixture.CreateBrush(1,1,shape:BrushPreset.Shape.Square);brush.size=.4f;
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(TexturePaintAssets.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
                Assert.That(shader,Is.Not.Null);
                Assert.That(ShaderUtil.ShaderHasError(shader),Is.False,string.Join("; ",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message)));
                engine=new PaintingEngine(null,null,null,shader);
                layer=fixture.set.AddSplineLayer("Generated path");
                context=fixture.CreateContext(brush,TexturePaintTool.Paint,Color.white,strength:1);
                context.projectionDepth=1;context.pathGenerator=TexturePaintPathGenerators.Find(id).CreateSettings();
                foreach(TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
                    context.channelSources[channel]=new TexturePaintChannelSourceSettings
                    {source=TexturePaintBrushSource.Color,color=new Color(.73f,.73f,.73f,1)};
            }
            public Dictionary<TexturePaintChannel,Color[]> Render(bool directUV=false,bool closed=false)
            {
                context.directUV=directUV;
                var segments=new[]{new TexturePaintRibbonSegment
                {
                    leftStartAlong=new Vector4(.1f,0,0,0),rightStartFlow=new Vector4(.9f,0,0,1),
                    leftEndAlong=new Vector4(.1f,1,0,1.25f),rightEndFlow=new Vector4(.9f,1,0,1),
                    normalStartPressure=new Vector4(0,0,1,1),normalEndPressure=new Vector4(0,0,1,1),
                    colorStart=Color.white,colorEnd=Color.white
                }};
                Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay),Is.True,engine.LastStrokeError);
                Assert.That(engine.ApplyRibbon(segments,null,true,false,closed,directUV),Is.True);
                var result=layer.channels.ToDictionary(x=>x.Key,x=>TexturePaintGpuTestFixture.ReadPixels(x.Value.Front));
                Assert.That(engine.RewindActiveStroke(),Is.True);engine.EndStroke(false);return result;
            }
            public void Dispose(){engine.Dispose();fixture.Dispose();Object.DestroyImmediate(brush);}
        }
        private static float Difference(Color[] a,Color[] b)=>a.Zip(b,(x,y)=>Mathf.Abs(x.r-y.r)+Mathf.Abs(x.g-y.g)+Mathf.Abs(x.b-y.b)+Mathf.Abs(x.a-y.a)).Average();

        [TestCase(false)] [TestCase(true)]
        public void TattooAllElevenChannelsShareGlyphAlphaAndScalarValues(bool directUV)
        {
            using var r=new Ribbon(TexturePaintPathGenerators.TextId);
            r.context.pathGenerator.text="O A";r.context.pathGenerator.preserveTextAspect=false;
            var output=r.Render(directUV);var albedo=output[TexturePaintChannel.Albedo];
            Assert.That(output.Count,Is.EqualTo(11));
            Assert.That(albedo.Count(c=>c.a>.5f),Is.GreaterThan(100));
            Assert.That(albedo.Count(c=>c.a<.001f),Is.GreaterThan(5000),"Glyph counters, spaces and margins stay transparent.");
            foreach(var pair in output)
            {
                for(int i=0;i<albedo.Length;i++)
                {
                    Assert.That(float.IsFinite(pair.Value[i].r+pair.Value[i].g+pair.Value[i].b+pair.Value[i].a),Is.True,pair.Key.ToString());
                    Assert.That(pair.Value[i].a,Is.EqualTo(albedo[i].a).Within(.002f),pair.Key+" coverage at "+i);
                }
            }
            foreach(var channel in new[]{TexturePaintChannel.Metallic,TexturePaintChannel.Roughness,TexturePaintChannel.Thickness})
                foreach(var pixel in output[channel].Where(c=>c.a>.95f))Assert.That(pixel.r,Is.EqualTo(.73f).Within(.005f),channel.ToString());
        }

        [Test]
        public void TattooRetainsCoverageWithoutAnAlbedoOutputAndUsesInkAlpha()
        {
            using var r=new Ribbon(TexturePaintPathGenerators.TextId);
            var original=r.Render()[TexturePaintChannel.Albedo];
            r.context.channelSources.Remove(TexturePaintChannel.Albedo);
            r.context.pathGenerator.color.a=.4f;
            var result=r.Render()[TexturePaintChannel.Metallic];
            for(int i=0;i<result.Length;i++)Assert.That(result[i].a,Is.EqualTo(original[i].a*.4f).Within(.002f));
        }

        [Test]
        public void TextContentStyleAndRepetitionChangeActualRaster()
        {
            using var r=new Ribbon(TexturePaintPathGenerators.TextId);
            r.context.pathGenerator.text="UMA";var before=r.Render()[TexturePaintChannel.Albedo];
            r.context.pathGenerator.text="Tattoo";r.context.pathGenerator.fontStyle=FontStyle.BoldAndItalic;
            r.context.pathGenerator.textRepeats=2;
            Assert.That(Difference(before,r.Render()[TexturePaintChannel.Albedo]),Is.GreaterThan(.03f));
        }

        [TestCase(true,false,false)] [TestCase(false,true,false)] [TestCase(true,true,false)]
        [TestCase(true,false,true)] [TestCase(false,true,true)] [TestCase(true,true,true)]
        public void TextFlipsMirrorGlyphsAndAllChannelMasks(bool flipX,bool flipY,bool directUV)
        {
            using var r=new Ribbon(TexturePaintPathGenerators.TextId);
            var s=r.context.pathGenerator;s.text="Fj7";s.textRepeats=2;
            var before=r.Render(directUV);
            // BeginStroke snapshots settings into the context; edit that current copy.
            s=r.context.pathGenerator;s.textFlipX=flipX;s.textFlipY=flipY;
            var after=r.Render(directUV);
            Assert.That(Difference(before[TexturePaintChannel.Albedo],after[TexturePaintChannel.Albedo]),Is.GreaterThan(.02f));
            for(int y=0;y<Ribbon.Size;y++)for(int x=0;x<Ribbon.Size;x++)
            {
                int index=y*Ribbon.Size+x;
                // The straight fixture runs along image Y; text X follows that path axis.
                int mirrored=(flipX?Ribbon.Size-1-y:y)*Ribbon.Size+(flipY?Ribbon.Size-1-x:x);
                float expected=before[TexturePaintChannel.Albedo][mirrored].a;
                foreach(var channel in after.Keys)
                    Assert.That(after[channel][index].a,Is.EqualTo(expected).Within(.004f),channel.ToString());
                Assert.That(after[TexturePaintChannel.NormalControl][index].r,
                    Is.EqualTo(before[TexturePaintChannel.NormalControl][mirrored].r).Within(.004f));
            }
        }

        [TestCase(true,false)] [TestCase(false,true)] [TestCase(true,true)]
        public void TextFlipsAlsoMirrorTheDraftPreview(bool flipX,bool flipY)
        {
            var s=TexturePaintPathGenerators.Find(TexturePaintPathGenerators.TextId).CreateSettings();s.text="Fj7";
            var before=TexturePaintGarmentPreview.Generate(null,null,new Vector2(.1f,.3f),pathGenerator:s)[TexturePaintChannel.Albedo];
            s.textFlipX=flipX;s.textFlipY=flipY;
            var after=TexturePaintGarmentPreview.Generate(null,null,new Vector2(.1f,.3f),pathGenerator:s)[TexturePaintChannel.Albedo];
            int size=TexturePaintPreviewDisplay.Resolution;
            Assert.That(Difference(before,after),Is.GreaterThan(.01f));
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                Assert.That(after[y*size+x].a,Is.EqualTo(before[(flipX?size-1-y:y)*size+(flipY?size-1-x:x)].a).Within(.004f));
        }

        [TestCase(TexturePaintPathGenerators.TattooId)]
        [TestCase(TexturePaintPathGenerators.TattooId+".scroll")]
        [TestCase(TexturePaintPathGenerators.TattooId+".flame")]
        public void ShapeTattoosShareTheirSilhouetteAcrossAllChannels(string id)
        {
            using var r=new Ribbon(id);var output=r.Render();var albedo=output[TexturePaintChannel.Albedo];
            Assert.That(albedo.Count(c=>c.a>.8f),Is.GreaterThan(100));
            foreach(var pair in output)for(int i=0;i<albedo.Length;i++)
                Assert.That(pair.Value[i].a,Is.EqualTo(albedo[i].a).Within(.002f),pair.Key.ToString());
        }

        [Test]
        public void TattooTapersChangeContourAndEachEndCanBeControlledIndependently()
        {
            var s=TexturePaintPathGenerators.Find(TexturePaintPathGenerators.TattooId).CreateSettings();
            s.lineBend=0;s.taperStart=s.taperEnd=false;
            using var full=new TexturePaintTattooShapeMask(s);
            float Column(Texture2D t,float u){int x=Mathf.RoundToInt(u*(t.width-1));return Enumerable.Range(0,t.height).Sum(y=>t.GetPixel(x,y).a);}
            s.taperStart=true;using var start=new TexturePaintTattooShapeMask(s);
            Assert.That(Column(start.Texture,.08f),Is.LessThan(Column(full.Texture,.08f)*.4f));
            Assert.That(Column(start.Texture,.92f),Is.EqualTo(Column(full.Texture,.92f)).Within(.01f));
            Assert.That(Column(start.Texture,.5f),Is.EqualTo(Column(full.Texture,.5f)).Within(.01f));
            s.taperStart=false;s.taperEnd=true;using var end=new TexturePaintTattooShapeMask(s);
            Assert.That(Column(end.Texture,.92f),Is.LessThan(Column(full.Texture,.92f)*.4f));
            Assert.That(Column(end.Texture,.08f),Is.EqualTo(Column(full.Texture,.08f)).Within(.01f));
            s.taperEnd=false;s.curlStart=true;using var curl=new TexturePaintTattooShapeMask(s);
            Assert.That(Difference(full.Texture.GetPixels(),curl.Texture.GetPixels()),Is.GreaterThan(.03f));
            Assert.That(Column(curl.Texture,.92f),Is.EqualTo(Column(full.Texture,.92f)).Within(.01f));
        }

        [TestCase(".hooks")] [TestCase(".spiral")] [TestCase(".flame")] [TestCase(".scroll")]
        public void TattooDesignsHaveSolidInkAndTransparentNegativeSpace(string suffix)
        {
            var s=TexturePaintPathGenerators.Find(TexturePaintPathGenerators.TattooId+suffix).CreateSettings();
            using var mask=new TexturePaintTattooShapeMask(s);var pixels=mask.Texture.GetPixels();
            Assert.That(pixels.Count(c=>c.a>.99f),Is.GreaterThan(pixels.Length*.03f));
            Assert.That(pixels.Count(c=>c.a<.01f),Is.GreaterThan(pixels.Length*.3f));
            for(int x=0;x<mask.Texture.width;x++)
            {
                Assert.That(mask.Texture.GetPixel(x,0).a,Is.Zero,"Clipped bottom edge");
                Assert.That(mask.Texture.GetPixel(x,mask.Texture.height-1).a,Is.Zero,"Clipped top edge");
            }
            for(int y=0;y<mask.Texture.height;y++)
            {
                Assert.That(mask.Texture.GetPixel(0,y).a,Is.Zero,"Clipped start");
                Assert.That(mask.Texture.GetPixel(mask.Texture.width-1,y).a,Is.Zero,"Clipped end");
            }
            var path=System.IO.Path.Combine("Library","PathGeneratorQA");System.IO.Directory.CreateDirectory(path);
            var preview=new Texture2D(mask.Texture.width,mask.Texture.height,TextureFormat.RGBA32,false);
            try
            {
                preview.SetPixels(pixels.Select(c=>Color.Lerp(Color.white,new Color(.01f,.01f,.06f),c.a)).ToArray());preview.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(path,"tattoo"+suffix+".png"),preview.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(preview); }
        }

        [Test]
        public void CurlTaperLengthAndEachTipRemainIndependentAfterSerialization()
        {
            var s=TexturePaintPathGenerators.Find(TexturePaintPathGenerators.TattooId+".hooks").CreateSettings();
            s.lineBend=0;s.taperStartLength=.15f;s.taperEndLength=.7f;
            using var shortStart=new TexturePaintTattooShapeMask(s);
            s.taperStartLength=1;
            var saved=JsonUtility.FromJson<TexturePaintPathGeneratorSettings>(JsonUtility.ToJson(s));
            Assert.That(saved.curlStart,Is.True);Assert.That(saved.curlEnd,Is.True);
            Assert.That(saved.taperEndLength,Is.EqualTo(.7f));
            using var longStart=new TexturePaintTattooShapeMask(saved);
            float left=0,right=0;
            for(int y=0;y<shortStart.Texture.height;y++)for(int x=0;x<shortStart.Texture.width;x++)
            {
                float delta=Mathf.Abs(shortStart.Texture.GetPixel(x,y).a-longStart.Texture.GetPixel(x,y).a);
                if(x<shortStart.Texture.width/2)left+=delta;else right+=delta;
            }
            Assert.That(left,Is.GreaterThan(20));Assert.That(right,Is.LessThan(.001f));
        }

        [TestCase("")] [TestCase("  \n ")]
        public void EmptyTextProducesNoMarks(string text)
        {
            using var r=new Ribbon(TexturePaintPathGenerators.TextId);r.context.pathGenerator.text=text;
            foreach(var pair in r.Render())Assert.That(pair.Value.Max(c=>c.a),Is.Zero,pair.Key.ToString());
        }

        [TestCase("count",false)] [TestCase("placement",false)] [TestCase("length",false)]
        [TestCase("angle",false)] [TestCase("sweep",false)] [TestCase("direction",false)]
        [TestCase("count",true)] [TestCase("placement",true)] [TestCase("length",true)]
        [TestCase("angle",true)] [TestCase("sweep",true)] [TestCase("direction",true)]
        public void TribalArmControlsChangeTheActualSilhouette(string control,bool flame)
        {
            var s=TexturePaintPathGenerators.Find(TexturePaintPathGenerators.TattooId+(flame?".flame":".scroll")).CreateSettings();
            s.tribalSymmetry=TexturePaintTattooSymmetry.None;s.tribalVariation=0;
            using var before=new TexturePaintTattooShapeMask(s);
            switch(control)
            {
                case "count":s.tribalArmCount=9;break;
                case "placement":s.tribalBranchStart=.4f;s.tribalBranchEnd=.6f;break;
                case "length":s.tribalArmLength=1.4f;break;
                case "angle":s.tribalArmAngle=120;break;
                case "sweep":s.tribalArmSweep=-.8f;break;
                case "direction":s.tribalCurlDirection=TexturePaintTattooCurlDirection.Outward;break;
            }
            using var after=new TexturePaintTattooShapeMask(s);
            Assert.That(Difference(before.Texture.GetPixels(),after.Texture.GetPixels()),Is.GreaterThan(.003f),control);
        }

        [Test]
        public void TribalVariationIsReproducibleOptionalAndDoesNotConsumeGlobalRandom()
        {
            var s=TexturePaintPathGenerators.Find(TexturePaintPathGenerators.TattooId+".scroll").CreateSettings();
            s.tribalVariation=.8f;var randomState=UnityEngine.Random.state;
            using var first=new TexturePaintTattooShapeMask(s);
            using var repeat=new TexturePaintTattooShapeMask(s);
            Assert.That(repeat.Texture.GetPixels32(),Is.EqualTo(first.Texture.GetPixels32()));
            Assert.That(UnityEngine.Random.state,Is.EqualTo(randomState));
            s.seed++;using var next=new TexturePaintTattooShapeMask(s);
            Assert.That(Difference(first.Texture.GetPixels(),next.Texture.GetPixels()),Is.GreaterThan(.003f));
            s.tribalVariation=0;using var zero=new TexturePaintTattooShapeMask(s);
            s.seed++;using var noVariation=new TexturePaintTattooShapeMask(s);
            Assert.That(noVariation.Texture.GetPixels32(),Is.EqualTo(zero.Texture.GetPixels32()));
        }

        [TestCase(TexturePaintTattooSymmetry.MirrorAcrossPath)] [TestCase(TexturePaintTattooSymmetry.HalfTurn)]
        public void TribalSymmetryIncludesSeededArmsSpineCurlsAndCutouts(TexturePaintTattooSymmetry symmetry)
        {
            var s=TexturePaintPathGenerators.Find(TexturePaintPathGenerators.TattooId+".scroll").CreateSettings();
            s.tribalSymmetry=symmetry;s.tribalArmCount=6;s.tribalVariation=.8f;s.curlStart=true;
            using var mask=new TexturePaintTattooShapeMask(s);var t=mask.Texture;
            for(int y=0;y<t.height;y++)for(int x=0;x<t.width;x++)
                Assert.That(t.GetPixel(x,y).a,Is.EqualTo(t.GetPixel(symmetry==TexturePaintTattooSymmetry.HalfTurn?t.width-1-x:x,t.height-1-y).a).Within(.008f));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(16)]
        public void TribalSilhouettesRetainTransparentGuttersAtExtremeSettings(int arms)
        {
            var s=TexturePaintPathGenerators.Find(TexturePaintPathGenerators.TattooId+".scroll").CreateSettings();
            s.tribalSymmetry=TexturePaintTattooSymmetry.None;s.tribalArmCount=arms;s.tattooAspect=1;
            s.tribalArmLength=1.5f;s.tribalArmAngle=160;s.tribalArmSweep=-1;s.lineWidth=.65f;
            s.tribalVariation=1;s.curlRadius=.22f;s.curlTurns=1.8f;s.curlStart=s.curlEnd=true;
            using var mask=new TexturePaintTattooShapeMask(s);var t=mask.Texture;
            Assert.That(t.GetPixels().Max(c=>c.a),Is.GreaterThan(.9f));
            for(int x=0;x<t.width;x++){Assert.That(t.GetPixel(x,0).a,Is.Zero);Assert.That(t.GetPixel(x,t.height-1).a,Is.Zero);}
            for(int y=0;y<t.height;y++){Assert.That(t.GetPixel(0,y).a,Is.Zero);Assert.That(t.GetPixel(t.width-1,y).a,Is.Zero);}
        }

        [Test]
        public void TribalSettingsRoundTripAndOlderMotifsRetainTheirConstruction()
        {
            var s=TexturePaintPathGenerators.Find(TexturePaintPathGenerators.TattooId+".flame").CreateSettings();
            Assert.That(s.proceduralTribal,Is.True);
            s.tribalArmCount=7;s.tribalBranchStart=.12f;s.tribalBranchEnd=.67f;s.tribalArmAngle=115;
            s.tribalArmSweep=-.6f;s.tribalArmLength=1.2f;s.tribalVariation=.7f;s.seed=-145;
            s.tribalCurlDirection=TexturePaintTattooCurlDirection.CounterClockwise;
            var restored=JsonUtility.FromJson<TexturePaintPathGeneratorSettings>(JsonUtility.ToJson(s));
            using var original=new TexturePaintTattooShapeMask(s);using var saved=new TexturePaintTattooShapeMask(restored);
            Assert.That(saved.Texture.GetPixels32(),Is.EqualTo(original.Texture.GetPixels32()));
            Assert.That(restored.Clone().tribalArmCount,Is.EqualTo(7));
            var legacy=JsonUtility.FromJson<TexturePaintPathGeneratorSettings>("{\"generatorId\":\"com.uma.path.tattoo-shape.scroll\",\"enabled\":true,\"tattooDesign\":4}");
            Assert.That(legacy.proceduralTribal,Is.False);
            using var old=new TexturePaintTattooShapeMask(legacy);
            legacy.tribalArmCount=16;legacy.tribalVariation=1;
            using var unchanged=new TexturePaintTattooShapeMask(legacy);
            Assert.That(unchanged.Texture.GetPixels32(),Is.EqualTo(old.Texture.GetPixels32()));
            legacy.proceduralTribal=true;using var upgraded=new TexturePaintTattooShapeMask(legacy);
            Assert.That(Difference(unchanged.Texture.GetPixels(),upgraded.Texture.GetPixels()),Is.GreaterThan(.01f));
        }

        [Test]
        public void ScarHealingClosesOpeningAndChangesHeight()
        {
            using var r=new Ribbon(TexturePaintPathGenerators.ScarId);
            r.context.pathGenerator.healing=0;var fresh=r.Render();
            r.context.pathGenerator.healing=1;var healed=r.Render();
            Assert.That(fresh[TexturePaintChannel.NormalControl].Where(c=>c.a>.5f).Min(c=>c.r),Is.LessThan(.45f));
            Assert.That(healed[TexturePaintChannel.NormalControl].Where(c=>c.a>.1f).Min(c=>c.r),Is.GreaterThan(.495f));
            Assert.That(healed[TexturePaintChannel.Albedo].Sum(c=>c.a),Is.LessThan(fresh[TexturePaintChannel.Albedo].Sum(c=>c.a)*.7f));
            Assert.That(Difference(fresh[TexturePaintChannel.NormalControl],healed[TexturePaintChannel.NormalControl]),Is.GreaterThan(.02f));
        }

        [Test]
        public void SuturesAndRemovedPuncturesProduceDistinctRelief()
        {
            using var r=new Ribbon(TexturePaintPathGenerators.ScarId);
            r.context.pathGenerator.stitchCount=7;r.context.pathGenerator.stitchThickness=.026f;
            var plain=r.Render();r.context.pathGenerator.stitches=true;var sewn=r.Render();
            r.context.pathGenerator.removedStitches=true;var removed=r.Render();
            Assert.That(Difference(plain[TexturePaintChannel.NormalControl],sewn[TexturePaintChannel.NormalControl]),Is.GreaterThan(.015f));
            Assert.That(Difference(sewn[TexturePaintChannel.Albedo],removed[TexturePaintChannel.Albedo]),Is.GreaterThan(.015f));
            Assert.That(Difference(plain[TexturePaintChannel.NormalControl],removed[TexturePaintChannel.NormalControl]),Is.GreaterThan(.005f));
        }

        [Test]
        public void BurnAndStretchMarkProfilesRemainAvailableOnPaths()
        {
            using var r=new Ribbon(TexturePaintPathGenerators.ScarId);
            var cut=r.Render()[TexturePaintChannel.NormalControl];
            r.context.pathGenerator.woundType=TexturePaintPathWoundType.Burn;
            var burn=r.Render()[TexturePaintChannel.NormalControl];
            r.context.pathGenerator.woundType=TexturePaintPathWoundType.StretchMark;
            var stretch=r.Render()[TexturePaintChannel.NormalControl];
            Assert.That(Difference(cut,burn),Is.GreaterThan(.015f));
            Assert.That(Difference(cut,stretch),Is.GreaterThan(.015f));
        }

        private static IEnumerable<string> Suite()=>TexturePaintPathGenerators.All.Select(x=>x.Id);
        [TestCaseSource(nameof(Suite))]
        public void EveryRegisteredGeneratorRendersFiniteUsefulOutput(string id)
        {
            using var r=new Ribbon(id);var output=r.Render();
            Assert.That(output[TexturePaintChannel.Albedo].Max(c=>c.a),Is.GreaterThan(.005f),id);
            foreach(var pair in output)foreach(var c in pair.Value)
            {
                Assert.That(float.IsFinite(c.r)&&float.IsFinite(c.g)&&float.IsFinite(c.b)&&float.IsFinite(c.a),Is.True,id+" "+pair.Key);
                Assert.That(c.a,Is.InRange(0f,1.002f));
            }
        }

        [Test]
        public void TextMaskHasTightUnclippedBoundsAndDisposes()
        {
            var mask=new TexturePaintPathTextMask("Agj\nUMA",Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),FontStyle.Italic);
            var texture=mask.Texture;Assert.That(texture.width,Is.GreaterThan(30));
            Assert.That(texture.GetPixels().Max(c=>c.a),Is.GreaterThan(.9f));
            for(int x=0;x<texture.width;x++)
            { Assert.That(texture.GetPixel(x,0).a,Is.Zero);Assert.That(texture.GetPixel(x,texture.height-1).a,Is.Zero); }
            mask.Dispose();Assert.That(texture==null,Is.True);
        }

        [Test]
        public void SettingsDeepCloneAndJsonRoundTripIncludeGenerators()
        {
            Assert.That(JsonUtility.FromJson<TexturePaintSplineSettings>("{}").pathGenerator?.enabled??false,Is.False);
            Assert.That(JsonUtility.FromJson<TexturePaintSplineSettings>(JsonUtility.ToJson(new TexturePaintSplineSettings())).pathGenerator?.enabled??false,Is.False);
            var settings=new TexturePaintSplineSettings { pathGenerator=TexturePaintPathGenerators.Find("com.uma.path.stitch.Chainstitch").CreateSettings() };
            var copy=settings.Clone();copy.pathGenerator.seam.rows[0].color=Color.magenta;
            Assert.That(settings.pathGenerator.seam.rows[0].color,Is.Not.EqualTo(Color.magenta));
            var restored=JsonUtility.FromJson<TexturePaintSplineSettings>(JsonUtility.ToJson(copy));
            Assert.That(restored.pathGenerator.generatorId,Is.EqualTo(copy.pathGenerator.generatorId));
            Assert.That(restored.pathGenerator.seam.rows[0].color,Is.EqualTo(Color.magenta));
            var tattoo=TexturePaintPathGenerators.Find(TexturePaintPathGenerators.TextId).CreateSettings();
            var restoredTattoo=JsonUtility.FromJson<TexturePaintPathGeneratorSettings>(JsonUtility.ToJson(tattoo)).Clone();
            Assert.That(restoredTattoo.seam,Is.Null);Assert.That(restoredTattoo.garment,Is.Null);
        }

        [Test]
        public void TattooFontAndTextSurviveNativeDocumentReload()
        {
            const string folder="Assets/UMAProjectData/Tests/PathGeneratorPersistence";
            UMAPathUtility.EnsureAssetFolder(folder);
            try
            {
                var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var doc=ScriptableObject.CreateInstance<TexturePaintDocument>();
                var surface=new TexturePaintDocumentSurface();
                var settings=TexturePaintPathGenerators.Find(TexturePaintPathGenerators.TextId).CreateSettings();
                settings.font=font;settings.text="Saved ink";settings.fontStyle=FontStyle.Italic;settings.textFlipX=settings.textFlipY=true;
                surface.layers.Add(new TexturePaintDocumentLayer { kind=TexturePaintLayerKind.Spline,splineSettings=new TexturePaintSplineSettings { pathGenerator=settings } });
                doc.surfaces.Add(surface);string path=folder+"/Tattoo.asset";
                AssetDatabase.CreateAsset(doc,path);AssetDatabase.SaveAssets();Resources.UnloadAsset(doc);
                var restored=AssetDatabase.LoadAssetAtPath<TexturePaintDocument>(path).surfaces[0].layers[0].splineSettings.pathGenerator;
                Assert.That(restored.font,Is.EqualTo(font));Assert.That(restored.text,Is.EqualTo("Saved ink"));Assert.That(restored.fontStyle,Is.EqualTo(FontStyle.Italic));
                Assert.That(restored.textFlipX,Is.True);Assert.That(restored.textFlipY,Is.True);
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }

        [Test]
        public void LegacyScarMaterialSettingsCopyWithoutMutatingOriginal()
        {
            var parameters=new TexturePaintPluginParameterSet();parameters.Get("scarAge",true).number=.83f;
            parameters.Get("freshColor",true).color=Color.red;parameters.Get("height",true).number=.18f;
            var result=TexturePaintStageWindow.MigrateLegacyScarSettings(parameters);
            Assert.That(result.generatorId,Is.EqualTo(TexturePaintPathGenerators.ScarId));
            Assert.That(result.healing,Is.EqualTo(.83f));Assert.That(result.color,Is.EqualTo(Color.red));Assert.That(result.depth,Is.EqualTo(.18f));
            result.healing=0;Assert.That(parameters.Float("scarAge"),Is.EqualTo(.83f));
        }

        [TestCase(TexturePaintPathGenerators.TextId)]
        [TestCase(TexturePaintPathGenerators.ScarId)]
        [TestCase("com.uma.path.stitch.Chainstitch")]
        public void DraftPreviewRendersBeforePathPointsExist(string id)
        {
            var output=TexturePaintGarmentPreview.Generate(null,null,new Vector2(.1f,.3f),
                pathGenerator:TexturePaintPathGenerators.Find(id).CreateSettings());
            Assert.That(output[TexturePaintChannel.Albedo].Max(c=>c.a),Is.GreaterThan(.1f));
            Assert.That(output.ContainsKey(TexturePaintChannel.NormalControl),Is.True);
        }

        [TestCase(TexturePaintLayerEffectKind.Stroke,false)]
        [TestCase(TexturePaintLayerEffectKind.InnerGlow,false)]
        [TestCase(TexturePaintLayerEffectKind.ProceduralStitch,false)]
        [TestCase(TexturePaintLayerEffectKind.BevelEdge,false)]
        [TestCase(TexturePaintLayerEffectKind.EdgeFade,false)]
        [TestCase(TexturePaintLayerEffectKind.Stroke,true)]
        [TestCase(TexturePaintLayerEffectKind.InnerGlow,true)]
        [TestCase(TexturePaintLayerEffectKind.ProceduralStitch,true)]
        [TestCase(TexturePaintLayerEffectKind.BevelEdge,true)]
        [TestCase(TexturePaintLayerEffectKind.EdgeFade,true)]
        public void GeneratedPathEffectsFollowVisiblePixelsAndLeaveRawRasterUnchanged(TexturePaintLayerEffectKind kind,bool tattoo)
        {
            using var r=new Ribbon(tattoo?TexturePaintPathGenerators.TattooId+".scroll":TexturePaintPathGenerators.TextId);
            r.context.pathGenerator.text="O";r.context.pathGenerator.preserveTextAspect=false;
            var raw=r.Render()[TexturePaintChannel.Albedo];
            var effect=r.layer.effects.Get(kind);effect.enabled=true;effect.color=Color.red;effect.width=3;
            effect.contourThreadWidth=3;effect.contourStitchLength=6;effect.contourStitchInset=3;
            r.context.ribbonEffects=r.layer.effects.Clone();
            var rerendered=r.Render()[TexturePaintChannel.Albedo];
            Assert.That(Difference(raw,rerendered),Is.LessThan(.0001f),"Contour effects must not be baked against the rectangular ribbon.");
            r.layer.splineSettings.pathMode=TexturePaintPathMode.Ribbon;
            r.layer.splineSettings.pathGenerator=r.context.pathGenerator.Clone();
            var image=new Texture2D(Ribbon.Size,Ribbon.Size,TextureFormat.RGBAFloat,false,true);
            try
            {
                image.SetPixels(raw);image.Apply();
                using var source=new EditableTextureTarget("Effect source",Ribbon.Size,Ribbon.Size,RenderTextureFormat.ARGBFloat,image,Color.clear);
                using var output=new EditableTextureTarget("Effect output",Ribbon.Size,Ribbon.Size,RenderTextureFormat.ARGBFloat,null,Color.clear);
                using var compositor=new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
                Assert.That(compositor.CompositeLayerInto(output.Front,r.fixture.set,r.layer,source,TexturePaintChannel.Albedo,1,TexturePaintBlendMode.Normal),Is.True);
                var styled=TexturePaintGpuTestFixture.ReadPixels(output.Front);
                if(kind==TexturePaintLayerEffectKind.EdgeFade)
                    Assert.That(styled.Sum(c=>c.a),Is.LessThan(raw.Sum(c=>c.a)-5),"Fade must reduce edge coverage.");
                else Assert.That(styled.Count(c=>c.r>.15f&&c.r>c.b*2),Is.GreaterThan(5),kind.ToString());
                for(int i=0;i<raw.Length;i++)
                    if(kind!=TexturePaintLayerEffectKind.Stroke && kind!=TexturePaintLayerEffectKind.EdgeFade)Assert.That(styled[i].a,Is.EqualTo(raw[i].a).Within(.003f),"Interior effects retain transparency.");
                Assert.That(Difference(raw,TexturePaintGpuTestFixture.ReadPixels(source.Front)),Is.LessThan(.0001f));
                if(!tattoo)
                {
                    var path=System.IO.Path.Combine("Library","PathGeneratorQA");System.IO.Directory.CreateDirectory(path);
                    image.SetPixels(styled.Select(c=>new Color(c.r+1-c.a,c.g+1-c.a,c.b+1-c.a,1)).ToArray());image.Apply();
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(path,"text-effect-"+kind+".png"),image.EncodeToPNG());
                }
                effect.enabled=false;output.Reset(null,Color.clear);
                compositor.CompositeLayerInto(output.Front,r.fixture.set,r.layer,source,TexturePaintChannel.Albedo,1,TexturePaintBlendMode.Normal);
                var disabled=TexturePaintGpuTestFixture.ReadPixels(output.Front);
                Assert.That(disabled.Max(c=>c.r),Is.LessThan(.01f),"Disabling the effect removes it without regeneration.");
            }
            finally { Object.DestroyImmediate(image); }
        }

        [TestCase(TexturePaintLayerEffectKind.Stroke)] [TestCase(TexturePaintLayerEffectKind.InnerGlow)] [TestCase(TexturePaintLayerEffectKind.ProceduralStitch)]
        [TestCase(TexturePaintLayerEffectKind.BevelEdge)]
        public void GeneratedPathPreviewIncludesContourEffects(TexturePaintLayerEffectKind kind)
        {
            using var owner=new TexturePaintLayer();
            var s=TexturePaintPathGenerators.Find(TexturePaintPathGenerators.TextId).CreateSettings();s.text="O";s.preserveTextAspect=false;
            var effect=owner.effects.Get(kind);effect.enabled=true;effect.color=Color.red;effect.width=3;effect.contourStitchInset=.5f;
            owner.channels[TexturePaintChannel.Albedo]=new EditableTextureTarget("Preview channel",128,128,RenderTextureFormat.ARGBFloat,null,Color.clear);
            var pixels=TexturePaintGarmentPreview.Generate(null,null,new Vector2(.1f,.3f),owner:owner,pathGenerator:s)[TexturePaintChannel.Albedo];
            Assert.That(pixels.Count(c=>c.a>.1f&&c.r>.1f&&c.r>c.b*2),Is.GreaterThan(3));
        }

        internal static void ConfigureVisibleEffect(TexturePaintLayerEffectSettings effect)
        {
            effect.enabled=true;effect.color=Color.red;effect.secondaryColor=Color.blue;
            effect.width=4;effect.level=.8f;effect.brightness=-.3f;
            effect.texture1=Texture2D.whiteTexture;
            effect.contourThreadWidth=2;effect.contourStitchLength=4;effect.contourStitchInset=1;
        }

        [TestCaseSource(nameof(Suite))]
        public void EveryLayerEffectChangesEveryRegisteredPathGenerator(string id)
        {
            using var r=new Ribbon(id);
            r.context.pathGenerator.color=new Color(.3f,.4f,.5f,1);
            var raw=r.Render()[TexturePaintChannel.Albedo];
            r.layer.splineSettings.pathMode=TexturePaintPathMode.Ribbon;
            r.layer.splineSettings.pathGenerator=r.context.pathGenerator.Clone();
            var image=new Texture2D(Ribbon.Size,Ribbon.Size,TextureFormat.RGBAFloat,false,true);
            try
            {
                image.SetPixels(raw);image.Apply();
                using var source=new EditableTextureTarget("Effect matrix source",Ribbon.Size,Ribbon.Size,RenderTextureFormat.ARGBFloat,image,Color.clear);
                using var output=new EditableTextureTarget("Effect matrix result",Ribbon.Size,Ribbon.Size,RenderTextureFormat.ARGBFloat,null,Color.clear);
                using var compositor=new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
                compositor.CompositeLayerInto(output.Front,r.fixture.set,r.layer,source,TexturePaintChannel.Albedo,1,TexturePaintBlendMode.Normal);
                var baseline=TexturePaintGpuTestFixture.ReadPixels(output.Front);
                foreach(TexturePaintLayerEffectKind kind in Enum.GetValues(typeof(TexturePaintLayerEffectKind)))
                {
                    r.layer.effects=new TexturePaintLayerEffects();
                    ConfigureVisibleEffect(r.layer.effects.Get(kind));
                    output.Reset(null,Color.clear);
                    compositor.CompositeLayerInto(output.Front,r.fixture.set,r.layer,source,TexturePaintChannel.Albedo,1,TexturePaintBlendMode.Normal);
                    var styled=TexturePaintGpuTestFixture.ReadPixels(output.Front);
                    Assert.That(Difference(styled,baseline),Is.GreaterThan(.000001f),id+" / "+kind);
                    Assert.That(styled.All(c=>float.IsFinite(c.r+c.g+c.b+c.a)),Is.True,id+" / "+kind);
                    // Paint and plugin layers must take the same shared effect route.
                    foreach(var layerKind in new[]{TexturePaintLayerKind.Paint,TexturePaintLayerKind.Plugin})
                    {
                        r.layer.kind=layerKind;
                        output.Reset(null,Color.clear);
                        compositor.CompositeLayerInto(output.Front,r.fixture.set,r.layer,source,TexturePaintChannel.Albedo,1,TexturePaintBlendMode.Normal);
                        Assert.That(Difference(styled,TexturePaintGpuTestFixture.ReadPixels(output.Front)),Is.LessThan(.0001f),layerKind+"/path parity: "+kind);
                    }
                    r.layer.kind=TexturePaintLayerKind.Spline;
                }
                Assert.That(Difference(raw,TexturePaintGpuTestFixture.ReadPixels(source.Front)),Is.LessThan(.0001f));
            }
            finally { Object.DestroyImmediate(image); }
        }

        [Test]
        public void BevelLightAngleChangesHighlightAndShadowWithoutChangingGlyphCoverage()
        {
            using var owner=new TexturePaintLayer();
            owner.channels[TexturePaintChannel.Albedo]=new EditableTextureTarget("Bevel preview",128,128,RenderTextureFormat.ARGBFloat,null,Color.clear);
            var settings=TexturePaintPathGenerators.Find(TexturePaintPathGenerators.TextId).CreateSettings();
            settings.text="O";settings.preserveTextAspect=false;settings.color=new Color(.3f,.3f,.3f,1);
            var effect=owner.effects.bevelEdge;effect.enabled=true;effect.width=6;effect.level=1;effect.bevelLightAngle=135;
            var first=TexturePaintGarmentPreview.Generate(null,null,Vector2.one,owner:owner,pathGenerator:settings)[TexturePaintChannel.Albedo];
            Assert.That(first.Count(c=>c.a>.9f&&c.r>.5f),Is.GreaterThan(10),"Lit edges must brighten.");
            Assert.That(first.Count(c=>c.a>.9f&&c.r<.1f),Is.GreaterThan(10),"Opposing edges must darken.");
            effect.bevelLightAngle=315;
            var opposite=TexturePaintGarmentPreview.Generate(null,null,Vector2.one,owner:owner,pathGenerator:settings)[TexturePaintChannel.Albedo];
            Assert.That(Difference(first,opposite),Is.GreaterThan(.01f));
            Assert.That(first.Select(c=>c.a),Is.EqualTo(opposite.Select(c=>c.a)));
            var image=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true);
            try
            {
                image.SetPixels(first.Select(c=>Color.Lerp(Color.white,new Color(c.r,c.g,c.b,1),c.a)).ToArray());image.Apply();
                System.IO.Directory.CreateDirectory("Library/PathGeneratorQA");
                System.IO.File.WriteAllBytes("Library/PathGeneratorQA/beveled-text.png",image.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(image); }
        }

        [Test]
        public void ContourFadeRevealsBackdropAndAppliesLayerOpacityOnce()
        {
            using var r=new Ribbon(TexturePaintPathGenerators.TextId);
            r.context.pathGenerator.text="O";r.context.pathGenerator.color=new Color(.3f,.4f,.5f,1);
            var raw=r.Render()[TexturePaintChannel.Albedo];
            r.layer.kind=TexturePaintLayerKind.Plugin;
            r.layer.effects.edgeFade.enabled=true;r.layer.effects.edgeFade.width=5;
            var image=new Texture2D(Ribbon.Size,Ribbon.Size,TextureFormat.RGBAFloat,false,true);
            try
            {
                image.SetPixels(raw);image.Apply();
                using var source=new EditableTextureTarget("Fade source",Ribbon.Size,Ribbon.Size,RenderTextureFormat.ARGBFloat,image,Color.clear);
                using var output=new EditableTextureTarget("Fade composite",Ribbon.Size,Ribbon.Size,RenderTextureFormat.ARGBFloat,null,Color.clear);
                using var compositor=new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
                compositor.CompositeLayerInto(output.Front,r.fixture.set,r.layer,source,TexturePaintChannel.Albedo,1,TexturePaintBlendMode.Normal);
                var isolated=TexturePaintGpuTestFixture.ReadPixels(output.Front);
                Assert.That(isolated.Sum(c=>c.a),Is.LessThan(raw.Sum(c=>c.a)-5));
                output.Reset(null,Color.blue);
                compositor.CompositeLayerInto(output.Front,r.fixture.set,r.layer,source,TexturePaintChannel.Albedo,.4f,TexturePaintBlendMode.Normal);
                var overBlue=TexturePaintGpuTestFixture.ReadPixels(output.Front);
                for(int i=0;i<isolated.Length;i++)
                {
                    Assert.That(overBlue[i].r,Is.EqualTo(isolated[i].r*.4f).Within(.002f));
                    Assert.That(overBlue[i].g,Is.EqualTo(isolated[i].g*.4f).Within(.002f));
                    Assert.That(overBlue[i].b,Is.EqualTo(isolated[i].b*.4f+1-isolated[i].a*.4f).Within(.002f));
                    Assert.That(overBlue[i].a,Is.EqualTo(1).Within(.002f));
                }
            }
            finally { Object.DestroyImmediate(image); }
        }

        private sealed class RasterGenerator : ITexturePaintPathGenerator
        {
            public string Id=>"test.path.raster";
            public string MenuPath=>"Tests/Raster";
            public TexturePaintPathGeneratorRaster last;
            public bool fail;
            public TexturePaintPathGeneratorSettings CreateSettings()=>new(){generatorId=Id,enabled=true};
            public IDisposable Prepare(TexturePaintPathGeneratorSettings settings)
            {
                if(fail)throw new InvalidOperationException("Expected preparation failure");
                last=new TexturePaintPathGeneratorRaster();
                var mask=new Texture2D(4,4,TextureFormat.RGBA32,false,true){wrapMode=TextureWrapMode.Clamp};
                mask.SetPixels(Enumerable.Range(0,16).Select(i=>new Color(1,1,1,(i%4==1||i%4==2)?1:0)).ToArray());mask.Apply();
                last.Coverage=mask;return last;
            }
            public void Bind(MaterialPropertyBlock p,TexturePaintPathGeneratorSettings s,IDisposable prepared,
                TexturePaintChannel channel,Color color,Vector2 size)=>((TexturePaintPathGeneratorRaster)prepared).Bind(p,channel,color);
        }

        [Test]
        public void RegisteredRasterGeneratorUsesSharedCoverageAndDisposesPreparedTextures()
        {
            var generator=new RasterGenerator();TexturePaintPathGenerators.Register(generator);
            try
            {
                using var r=new Ribbon(generator.Id);var output=r.Render();
                Assert.That(output[TexturePaintChannel.Albedo].Max(c=>c.a),Is.GreaterThan(.8f));
                Assert.That(output[TexturePaintChannel.Albedo].Select(c=>c.a),Is.EqualTo(output[TexturePaintChannel.Metallic].Select(c=>c.a)));
                Assert.That(generator.last.Coverage==null,Is.True);
                generator.fail=true;
                Assert.That(r.engine.BeginStroke(r.context,TexturePaintSourceMode.SourceOverlay),Is.False);
                Assert.That(r.engine.LastStrokeError,Does.Contain("Expected preparation failure"));
                r.context.pathGenerator.generatorId="test.missing";
                Assert.That(r.engine.BeginStroke(r.context,TexturePaintSourceMode.SourceOverlay),Is.False);
                Assert.That(r.engine.LastStrokeError,Does.Contain("not installed"));
            }
            finally { TexturePaintPathGenerators.Unregister(generator.Id); }
        }
    }
}
#endif
