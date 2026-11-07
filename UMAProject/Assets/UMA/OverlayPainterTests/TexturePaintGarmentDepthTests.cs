#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed partial class TexturePaintGarmentTests
    {
        private static void ConfigureZipperDepth(Fixture f)
        {
            var s=f.settings.garment;
            s.surfaceColor=new Color(.65f,.65f,.65f,1);s.accentColor=new Color(.7f,.7f,.7f,1);
            s.detailSize=.025f;s.relief=.008f;s.stitchRows=0;s.slider=.65f;s.opening=0;
            s.wear=s.dirt=s.irregularity=0;s.normal=true;
        }

        private static float MaximumRowAlpha(Color[] image,int row)
        {
            float maximum=0;
            for(int x=Fixture.Size/4;x<Fixture.Size*3/4;x++)maximum=Mathf.Max(maximum,image[row*Fixture.Size+x].a);
            return maximum;
        }

        [TestCase(false)] [TestCase(true)]
        public void ZipperToothDesignsAndStitchPatternsHaveDistinctCoordinatedOutputs(bool path)
        {
            using var f=new Fixture(TexturePaintGarmentPreset.MetalZipper);ConfigureZipperDepth(f);
            var s=f.settings.garment;
            var original=f.Render(path);s.zipperStyle.toothShape=TexturePaintZipperToothShape.Interlocking;
            Assert.That(Difference(original[TexturePaintChannel.Albedo],f.Render(path)[TexturePaintChannel.Albedo]),Is.LessThan(.00001f));
            var designs=new List<Color[]>();
            foreach(TexturePaintZipperToothShape shape in Enum.GetValues(typeof(TexturePaintZipperToothShape)))
            {
                if(shape==TexturePaintZipperToothShape.ConstructionDefault)continue;
                s.zipperStyle.toothShape=shape;var images=f.Render(path);
                foreach(var pair in images)
                {
                    Assert.That(pair.Value.All(c=>float.IsFinite(c.r)&&float.IsFinite(c.g)&&float.IsFinite(c.b)&&float.IsFinite(c.a)),Is.True);
                    // Garments intentionally use material/shadow alpha for Albedo and AO;
                    // physical channels share the full construction coverage.
                    bool shading=pair.Key==TexturePaintChannel.Albedo || pair.Key==TexturePaintChannel.AmbientOcclusion;
                    // Shading channels may be stored in 8-bit targets; allow one alpha quantization step.
                    Assert.That(pair.Value.Zip(images[TexturePaintChannel.NormalControl],(a,b)=>shading?a.a-b.a:Mathf.Abs(a.a-b.a)).Max(),Is.LessThan(shading?1f/255+.001f:.001f),shape+" "+pair.Key);
                }
                foreach(var previous in designs)Assert.That(Difference(previous,images[TexturePaintChannel.Metallic]),Is.GreaterThan(.002f),shape.ToString());
                designs.Add(images[TexturePaintChannel.Metallic]);
            }
            s.stitchRows=1;s.stitchSize=.0015f;s.stitchSpacing=.018f;s.threadColor=Color.white;
            var stitches=new List<Color[]>();
            foreach(TexturePaintZipperStitchStyle style in Enum.GetValues(typeof(TexturePaintZipperStitchStyle)))
            {
                s.zipperStyle.stitchStyle=style;var image=f.Render(path)[TexturePaintChannel.NormalControl];
                // Thread occupies very few texels: compare the local relief, not a whole-image average.
                foreach(var previous in stitches)Assert.That(previous.Zip(image,(a,b)=>Mathf.Abs(a.r-b.r)).Max(),Is.GreaterThan(.0001f),style.ToString());
                stitches.Add(image);
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void ZipperTapeWidthWeaveAndCordsAreIndependentOfHardware(bool path)
        {
            using var f=new Fixture(TexturePaintGarmentPreset.MetalZipper);ConfigureZipperDepth(f);
            var s=f.settings.garment;s.openStart=s.openEnd=true;
            var original=f.Render(path);s.zipperStyle.tapeWidth=.95f;var wide=f.Render(path);
            Assert.That(wide[TexturePaintChannel.Albedo].Count(c=>c.a>.9f),Is.GreaterThan(original[TexturePaintChannel.Albedo].Count(c=>c.a>.9f)));
            Assert.That(wide[TexturePaintChannel.Metallic].Count(c=>c.r>.95f),Is.EqualTo(original[TexturePaintChannel.Metallic].Count(c=>c.r>.95f)));
            s.zipperStyle.weave=TexturePaintZipperTapeWeave.Herringbone;s.zipperStyle.weaveScale=.008f;
            s.zipperStyle.weaveContrast=.8f;s.zipperStyle.weaveHeight=.5f;
            var woven=f.Render(path);
            Assert.That(Difference(wide[TexturePaintChannel.Albedo],woven[TexturePaintChannel.Albedo]),Is.GreaterThan(.002f));
            Assert.That(Difference(wide[TexturePaintChannel.NormalControl],woven[TexturePaintChannel.NormalControl]),Is.GreaterThan(.0001f));
            s.zipperStyle.cordHeight=2;s.zipperStyle.cordWidth=.04f;var cord=f.Render(path);
            Assert.That(Difference(woven[TexturePaintChannel.NormalControl],cord[TexturePaintChannel.NormalControl]),Is.GreaterThan(.0001f));
        }

        [Test]
        public void ZipperStylePresetsPreserveOutputsAndDeepCloneThroughSerialization()
        {
            var s=TexturePaintGarmentSettings.Create(TexturePaintGarmentPreset.MetalZipper);
            s.slider=.3f;s.angle=23;s.openEnd=true;s.normal=true;s.metallic=false;
            foreach(TexturePaintZipperStylePreset preset in Enum.GetValues(typeof(TexturePaintZipperStylePreset)))
            {
                s.ApplyZipperStyle(preset);
                Assert.That(s.slider,Is.EqualTo(.3f));Assert.That(s.angle,Is.EqualTo(23));Assert.That(s.openEnd,Is.True);
                Assert.That(s.normal,Is.True);Assert.That(s.metallic,Is.False);
                var copy=JsonUtility.FromJson<TexturePaintGarmentSettings>(JsonUtility.ToJson(s)).Clone();copy.Normalize();
                Assert.That(JsonUtility.ToJson(copy),Is.EqualTo(JsonUtility.ToJson(s)));
                copy.zipperStyle.tapeWidth=.1f;Assert.That(s.zipperStyle.tapeWidth,Is.GreaterThan(.1f));
            }
        }

        [TestCase("zipperTeeth",false)] [TestCase("zipperStops",false)]
        [TestCase("zipperSlider",false)] [TestCase("zipperPull",false)] [TestCase("zipperTang",false)]
        [TestCase("zipperTeeth",true)] [TestCase("zipperStops",true)]
        [TestCase("zipperSlider",true)] [TestCase("zipperPull",true)] [TestCase("zipperTang",true)]
        public void EachZipperPartWritesIndependentMetalShineAndHeight(string field,bool path)
        {
            using var f=new Fixture(TexturePaintGarmentPreset.MetalZipper);ConfigureZipperDepth(f);
            var s=f.settings.garment;s.falloff=.001f;
            var part=(TexturePaintZipperPartSettings)typeof(TexturePaintGarmentSettings).GetField(field).GetValue(s);
            part.height=0;part.metallic=0;part.smoothness=0;
            var flat=f.Render(path);
            part.height=3;part.metallic=1;part.smoothness=1;
            var raised=f.Render(path);
            int changed=0;float heightGain=0;
            for(int i=0;i<Fixture.Size*Fixture.Size;i++)
            {
                Assert.That(raised[TexturePaintChannel.Metallic][i].a,Is.EqualTo(raised[TexturePaintChannel.NormalControl][i].a).Within(.001f));
                if(raised[TexturePaintChannel.Metallic][i].r-flat[TexturePaintChannel.Metallic][i].r<.95f)continue;
                changed++;
                Assert.That(raised[TexturePaintChannel.Roughness][i].r,Is.LessThan(.055f),"Fully smooth hardware must not retain matte bevels");
                heightGain+=raised[TexturePaintChannel.NormalControl][i].r-flat[TexturePaintChannel.NormalControl][i].r;
            }
            Assert.That(changed,Is.GreaterThan(0),field+" must have visible independent hardware pixels");
            Assert.That(heightGain/Mathf.Max(1,changed),Is.GreaterThan(.003f),field+" must raise Normal Control");
        }

        [Test]
        public void ZipperPartControlsCloneSerializeAndRetainExplicitZeroes()
        {
            var s=TexturePaintGarmentSettings.Create(TexturePaintGarmentPreset.MetalZipper);
            s.zipperPull.height=0;s.zipperPull.smoothness=1;s.zipperPull.metallic=.75f;
            var copy=s.Clone();copy.zipperPull.height=4;
            Assert.That(s.zipperPull.height,Is.Zero);
            var restored=JsonUtility.FromJson<TexturePaintGarmentSettings>(JsonUtility.ToJson(s));restored.Normalize();
            Assert.That(restored.zipperPull.height,Is.Zero);Assert.That(restored.zipperPull.smoothness,Is.EqualTo(1));
            Assert.That(restored.zipperPull.metallic,Is.EqualTo(.75f));
            Assert.That(s.OutputChannels(),Does.Contain(TexturePaintChannel.Metallic));
            Assert.That(s.OutputChannels(),Does.Contain(TexturePaintChannel.Roughness));
            Assert.That(s.OutputChannels(),Does.Contain(TexturePaintChannel.NormalControl));
        }

        [TestCase(TexturePaintGarmentPreset.MetalZipper,false,.5f)]
        [TestCase(TexturePaintGarmentPreset.MetalZipper,false,1.5f)]
        [TestCase(TexturePaintGarmentPreset.MetalZipper,true,.5f)]
        [TestCase(TexturePaintGarmentPreset.MetalZipper,true,1.5f)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper,false,.5f)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper,false,1.5f)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper,true,.5f)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper,true,1.5f)]
        public void ZipperStopsReachBothPhysicalEndsDespiteOrdinaryAreaFade(TexturePaintGarmentPreset preset,bool path,float width)
        {
            using var f=new Fixture(preset);ConfigureZipperDepth(f);
            var s=f.settings.garment;s.slider=.5f;s.zipperWidth=width;s.falloff=.15f;
            var stopped=f.Render(path)[TexturePaintChannel.Albedo];
            s.openStart=s.openEnd=true;var open=f.Render(path)[TexturePaintChannel.Albedo];
            foreach(int row in new[]{0,Fixture.Size-1})
            {
                Assert.That(MaximumRowAlpha(stopped,row),Is.GreaterThan(.65f),
                    "The stop must remain visible at the actual endpoint, including its antialiased boundary");
                Assert.That(MaximumRowAlpha(open,row),Is.GreaterThan(.99f),
                    "Opening the end removes its stop and lets the backing continue for wrapping");
            }
        }

        private static (int first,int last) StopRows(Dictionary<TexturePaintChannel,Color[]> stopped,
            Dictionary<TexturePaintChannel,Color[]> open)
        {
            int first=Fixture.Size,last=-1;
            var albedo=stopped[TexturePaintChannel.Albedo];
            var a=stopped[TexturePaintChannel.NormalControl];var b=open[TexturePaintChannel.NormalControl];
            for(int y=0;y<Fixture.Size/4;y++)for(int x=Fixture.Size/4;x<Fixture.Size*3/4;x++)
            {
                int i=y*Fixture.Size+x;
                // Opening an end also removes the tape fade, so isolate the bar's physical
                // height rather than interpreting the open backing's alpha as a stop mask.
                if(albedo[i].a<.25f||Mathf.Abs(a[i].r-b[i].r)<.001f)continue;
                first=Mathf.Min(first,y);last=Mathf.Max(last,y);
            }
            return(first,last);
        }

        [TestCase(TexturePaintGarmentPreset.MetalZipper,.5f)]
        [TestCase(TexturePaintGarmentPreset.MetalZipper,1.5f)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper,.5f)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper,1.5f)]
        public void LengtheningAProjectionDoesNotMoveItsStopAwayFromThePhysicalEnd(TexturePaintGarmentPreset preset,float width)
        {
            using var f=new Fixture(preset);ConfigureZipperDepth(f);
            var s=f.settings.garment;s.zipperWidth=width;s.slider=1;s.falloff=.15f;
            var stopped=f.Render();s.openStart=true;var open=f.Render();var square=StopRows(stopped,open);
            // The lower projector edge stays at world y=0 while its other end moves away.
            f.settings.height=.8f;f.settings.position.y=.4f;s.openStart=false;
            stopped=f.Render();s.openStart=true;open=f.Render();var longStrip=StopRows(stopped,open);
            Assert.That(square.first,Is.LessThanOrEqualTo(1),"The square stop must touch its lower boundary");
            Assert.That(longStrip.first,Is.LessThanOrEqualTo(1),"Long paths must not introduce a proportional gap below the bar");
            Assert.That(longStrip.last,Is.EqualTo(square.last).Within(1),
                "A stop's physical height is determined by hardware width, not projection length");
            Assert.That(longStrip.last,Is.InRange(2,Mathf.CeilToInt(.064f*width*Fixture.Size)+2),
                "The bar must remain confined to its physical end cap");
        }

        [TestCase(TexturePaintGarmentPreset.MetalZipper,false)]
        [TestCase(TexturePaintGarmentPreset.MetalZipper,true)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper,false)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper,true)]
        public void TheBottomStopCannotPaintOverTheSolidZipperPull(TexturePaintGarmentPreset preset,bool path)
        {
            using var f=new Fixture(preset);ConfigureZipperDepth(f);
            var s=f.settings.garment;s.slider=.25f;s.falloff=.001f;s.zipperWidth=1;s.openStart=true;
            var without=f.Render(path);s.openStart=false;var with=f.Render(path);
            var height=without[TexturePaintChannel.NormalControl];
            float maximum=0;
            for(int y=0;y<8;y++)for(int x=Fixture.Size*35/100;x<Fixture.Size*65/100;x++)
                maximum=Mathf.Max(maximum,height[y*Fixture.Size+x].r-.5f);
            Assert.That(maximum,Is.GreaterThan(.025f),"The pull must overlap the stop region in this setup");
            int solid=0,exposedStop=0;
            for(int y=0;y<8;y++)for(int x=Fixture.Size*35/100;x<Fixture.Size*65/100;x++)
            {
                int i=y*Fixture.Size+x;
                // Tall hardware can approach its crown height inside an antialiased edge.
                // Require fully metallic pixels on both presets; backing alpha alone cannot
                // distinguish the partially covered rim from the solid pull.
                bool opaqueMetal=without[TexturePaintChannel.Metallic][i].r>.9995f;
                if(opaqueMetal&&height[i].a>.95f&&height[i].r-.5f>maximum*.92f)
                {
                    solid++;
                    foreach(var channel in new[]{TexturePaintChannel.Albedo,TexturePaintChannel.AmbientOcclusion,
                        TexturePaintChannel.Metallic,TexturePaintChannel.NormalControl})
                    {
                        Color a=without[channel][i],b=with[channel][i];
                        Assert.That(Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)+Mathf.Abs(a.a-b.a),
                            Is.LessThan(.006f),channel+": metal below the opaque pull must not show through it");
                    }
                }
                else if(Mathf.Abs(with[TexturePaintChannel.Albedo][i].grayscale-without[TexturePaintChannel.Albedo][i].grayscale)>.01f)
                    exposedStop++;
            }
            Assert.That(solid,Is.GreaterThan(3),"Measure resolved opaque pull pixels, not just its antialiased rim");
            Assert.That(exposedStop,Is.GreaterThan(3),"The lower bar must still appear beside the pull or through its cutout");
        }

        [TestCase(TexturePaintGarmentPreset.MetalZipper,false)]
        [TestCase(TexturePaintGarmentPreset.MetalZipper,true)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper,false)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper,true)]
        public void ZipperDepthControlsReliefAndContactWithoutChangingToothLayout(TexturePaintGarmentPreset preset,bool path)
        {
            using var f=new Fixture(preset);ConfigureZipperDepth(f);
            var s=f.settings.garment;s.stitchRows=1;s.stitchSize=.004f;s.stitchSpacing=.025f;s.falloff=.001f;
            s.zipperDepth=0;var flat=f.Render(path);
            s.zipperDepth=1;var normal=f.Render(path);
            s.zipperDepth=3;var deep=f.Render(path);
            foreach(Color pixel in flat[TexturePaintChannel.NormalControl].Where(c=>c.a>.1f))
                Assert.That(pixel.r,Is.EqualTo(.5f).Within(.0005f),"Zero depth must flatten tape, thread and hardware");
            foreach(Color pixel in flat[TexturePaintChannel.Normal].Where(c=>c.a>.1f))
            {
                Assert.That(pixel.r,Is.EqualTo(.5f).Within(.001f));
                Assert.That(pixel.g,Is.EqualTo(.5f).Within(.001f));
                Assert.That(pixel.b,Is.EqualTo(1).Within(.001f));
            }
            Assert.That(normal[TexturePaintChannel.NormalControl].Max(c=>c.r),Is.GreaterThan(.54f));
            Assert.That(deep[TexturePaintChannel.NormalControl].Max(c=>c.r),
                Is.GreaterThan(normal[TexturePaintChannel.NormalControl].Max(c=>c.r)+.01f));
            Assert.That(MaximumDarkening(normal[TexturePaintChannel.Albedo],deep[TexturePaintChannel.Albedo],
                new Rect(.4f,.25f,.3f,.45f)),Is.GreaterThan(.015f),"Stronger depth must make local contact more visible");
            Assert.That(MaximumRowAlpha(flat[TexturePaintChannel.Albedo],Fixture.Size/2),Is.GreaterThan(.99f),
                "Zero depth keeps the zipper's material and footprint visible");
            for(int y=10;y<Fixture.Size-10;y++)for(int x=Fixture.Size/4;x<Fixture.Size*3/4;x++)
            {
                int i=y*Fixture.Size+x;
                foreach(var image in new[]{normal,deep})
                {
                    Assert.That(image[TexturePaintChannel.Metallic][i].r,
                        Is.EqualTo(flat[TexturePaintChannel.Metallic][i].r).Within(.001f),
                        "Depth must not resize, shift or change the metallic identity of teeth and hardware");
                    Assert.That(image[TexturePaintChannel.NormalControl][i].a,
                        Is.EqualTo(flat[TexturePaintChannel.NormalControl][i].a).Within(.001f),
                        "Depth must preserve hardware and tape coverage");
                }
            }
        }

        [TestCase(TexturePaintGarmentPreset.MetalZipper)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper)]
        public void MaximumReliefOnANarrowZipperRetainsUnclippedBevelsAndDepthResponse(TexturePaintGarmentPreset preset)
        {
            using var f=new Fixture(preset);ConfigureZipperDepth(f);
            f.gpu.mesh.vertices=f.gpu.mesh.vertices.Select(v=>v*.225f).ToArray();f.gpu.mesh.RecalculateBounds();
            f.settings.width=f.settings.height=.045f;f.settings.position=new Vector3(.0225f,.0225f,0);
            var s=f.settings.garment;s.relief=.1f;s.detailSize=.004f;s.falloff=.001f;s.zipperDepth=1;
            var normal=f.Render();s.zipperDepth=4;var deep=f.Render();
            float peak=normal[TexturePaintChannel.NormalControl].Max(c=>c.r);
            float deepPeak=deep[TexturePaintChannel.NormalControl].Max(c=>c.r);
            Assert.That(peak,Is.InRange(.6f,.9f),"Large relief must be bounded before shaping, not clipped into a flat white height plateau");
            Assert.That(deepPeak,Is.InRange(peak+.005f,.9f),"Even the maximum saved relief must retain a response to 3D depth");
            var raised=new List<float>();
            for(int y=Fixture.Size/4;y<Fixture.Size*3/5;y++)for(int x=Fixture.Size*42/100;x<Fixture.Size*61/100;x++)
            {
                Color p=deep[TexturePaintChannel.NormalControl][y*Fixture.Size+x];
                if(p.a>.9f&&p.r>.58f)raised.Add(p.r);
            }
            Assert.That(raised.Count,Is.GreaterThan(20));
            Assert.That(raised.Max()-raised.Min(),Is.GreaterThan(.025f),"The pull must retain its rounded bevel and lower cutout rather than a clipped plateau");
            Assert.That(deep[TexturePaintChannel.Normal].Any(c=>c.a>.9f&&Mathf.Abs(c.r-.5f)+Mathf.Abs(c.g-.5f)>.1f),Is.True,
                "Preserved height variation must still produce visible normal-map relief");
        }

        [Test]
        public void ZipperDepthPersistsDefaultsAndBindsItsOwnShaderControl()
        {
            var old=JsonUtility.FromJson<TexturePaintGarmentSettings>("{\"enabled\":true,\"preset\":15}");
            old.Normalize();Assert.That(old.zipperDepth,Is.EqualTo(1));
            old.zipperDepth=2.7f;Assert.That(old.Clone().zipperDepth,Is.EqualTo(2.7f));
            var restored=JsonUtility.FromJson<TexturePaintGarmentSettings>(JsonUtility.ToJson(old));
            restored.Normalize();Assert.That(restored.zipperDepth,Is.EqualTo(2.7f));
            var properties=new MaterialPropertyBlock();restored.Bind(properties,TexturePaintChannel.Albedo,Vector2.one);
            Assert.That(properties.GetFloat("_GarmentZipperDepth"),Is.EqualTo(2.7f));
            restored.zipperDepth=float.NaN;restored.Normalize();Assert.That(restored.zipperDepth,Is.EqualTo(1));
            restored.zipperDepth=-1;restored.Normalize();Assert.That(restored.zipperDepth,Is.Zero);
            var zero=JsonUtility.FromJson<TexturePaintGarmentSettings>(JsonUtility.ToJson(restored));
            zero.Normalize();Assert.That(zero.zipperDepth,Is.Zero,"A deliberately saved flat zipper must not migrate back to depth one");
            var legacy=JsonUtility.FromJson<TexturePaintGarmentSettings>(
                "{\"enabled\":true,\"preset\":15,\"zipperDepth\":0,\"zipperDepthVersion\":0}");
            legacy.Normalize();Assert.That(legacy.zipperDepth,Is.EqualTo(1),"Native documents with an unversioned zero need the compatible default");
            legacy.zipperDepth=0;
            var migratedZero=JsonUtility.FromJson<TexturePaintGarmentSettings>(JsonUtility.ToJson(legacy));
            migratedZero.Normalize();Assert.That(migratedZero.zipperDepth,Is.Zero,"Migration must run only once");
            restored.zipperDepth=9;restored.Normalize();Assert.That(restored.zipperDepth,Is.EqualTo(4));
        }
    }
}
#endif
