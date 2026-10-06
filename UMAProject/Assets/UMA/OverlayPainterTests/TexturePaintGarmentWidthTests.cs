#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed partial class TexturePaintGarmentTests
    {
        [TestCase(TexturePaintGarmentPreset.MetalZipper,false)]
        [TestCase(TexturePaintGarmentPreset.MetalZipper,true)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper,false)]
        [TestCase(TexturePaintGarmentPreset.CoilZipper,true)]
        public void ZipperWidthResizesHardwareWithoutMovingTheBackingOrStitches(TexturePaintGarmentPreset preset,bool path)
        {
            using var f=new Fixture(preset);
            var s=f.settings.garment;
            s.surfaceColor=Color.black;s.accentColor=Color.white;s.threadColor=Color.red;
            s.detailSize=.012f;s.stitchSize=.002f;s.stitchSpacing=.015f;
            s.slider=1;s.opening=0;s.openStart=s.openEnd=true;s.irregularity=0;
            var wide=f.Render(path)[TexturePaintChannel.Albedo];
            s.zipperWidth=.5f;
            var narrow=f.Render(path)[TexturePaintChannel.Albedo];
            int wideHardware=0,narrowHardware=0;
            for(int y=16;y<44;y++)for(int x=0;x<Fixture.Size;x++)
            {
                int i=y*Fixture.Size+x;
                if(wide[i].g>.15f)wideHardware++;
                if(narrow[i].g>.15f)narrowHardware++;
                if(x<30 || x>97)
                {
                    Assert.That(narrow[i].r,Is.EqualTo(wide[i].r).Within(.002f),"Tape and stitch position/color must stay fixed.");
                    Assert.That(narrow[i].a,Is.EqualTo(wide[i].a).Within(.002f),"The backing width must stay fixed.");
                }
            }
            Assert.That(wideHardware,Is.GreaterThan(100));
            Assert.That(narrowHardware,Is.InRange(wideHardware*.3f,wideHardware*.7f),"Half-width hardware should occupy about half the tooth-bank area.");
        }

        [Test]
        public void ZipperWidthSurvivesCloningAndSerializationAndDefaultsForOlderDocuments()
        {
            var old=JsonUtility.FromJson<TexturePaintGarmentSettings>("{\"enabled\":true,\"preset\":15}");
            old.Normalize();Assert.That(old.zipperWidth,Is.EqualTo(1));
            old.zipperWidth=.43f;
            Assert.That(old.Clone().zipperWidth,Is.EqualTo(.43f));
            var restored=JsonUtility.FromJson<TexturePaintGarmentSettings>(JsonUtility.ToJson(old));
            restored.Normalize();Assert.That(restored.zipperWidth,Is.EqualTo(.43f));
            var properties=new MaterialPropertyBlock();
            restored.Bind(properties,TexturePaintChannel.Albedo,Vector2.one);
            Assert.That(properties.GetFloat("_GarmentZipperWidth"),Is.EqualTo(.43f));
            restored.zipperWidth=float.NaN;restored.Normalize();
            Assert.That(restored.zipperWidth,Is.EqualTo(1));
        }
    }
}
#endif
