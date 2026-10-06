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
                // On the coil preset the backing and teeth are nonmetal, so the pre-stop
                // metallic value independently identifies fully covered pull metal. Overall
                // alpha is already one from the backing and cannot distinguish an AA rim.
                bool opaqueMetal=preset!=TexturePaintGarmentPreset.CoilZipper ||
                    without[TexturePaintChannel.Metallic][i].r>.9995f;
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
