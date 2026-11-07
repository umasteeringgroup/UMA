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
        private static IEnumerable<TestCaseData> EveryGarmentConstruction()
        {
            foreach(TexturePaintGarmentPreset preset in Enum.GetValues(typeof(TexturePaintGarmentPreset)))
                yield return new TestCaseData(preset);
        }

        private static bool IsMaterialOnly(TexturePaintGarmentPreset preset)
            => TexturePaintGarmentSettings.Kind(preset)==TexturePaintGarmentKind.Wear ||
               preset==TexturePaintGarmentPreset.PrintedLogo;

        [TestCaseSource(nameof(EveryGarmentConstruction))]
        public void EveryConstructionHonorsZeroReliefAndKeepsExtremeReliefUnclipped(TexturePaintGarmentPreset preset)
        {
            using var f=new Fixture(preset);
            var s=f.settings.garment;s.normal=s.height=s.masks=true;
            // The default 1.2 mm darning yarn is finer than this fixture's 1.56 mm
            // texels. Resolve its weave when testing shape; its filtered volume is
            // tested separately at the actual preset pitch below.
            if(preset==TexturePaintGarmentPreset.DarnedRepair)s.detailSize=.008f;
            s.relief=.1f;s.depthStrength=0;s.falloff=.001f;
            var flat=f.Render();
            s.relief=0;s.depthStrength=4;var zeroRelief=f.Render();
            foreach(var image in new[]{flat,zeroRelief})
            {
                foreach(var c in image[TexturePaintChannel.NormalControl].Where(c=>c.a>.01f))
                    Assert.That(c.r,Is.EqualTo(.5f).Within(.0005f),"Depth zero and Relief Height zero must include thread and microdetail");
                foreach(var c in image[TexturePaintChannel.Normal].Where(c=>c.a>.01f))
                {
                    Assert.That(c.r,Is.EqualTo(.5f).Within(.001f));
                    Assert.That(c.g,Is.EqualTo(.5f).Within(.001f));
                    Assert.That(c.b,Is.EqualTo(1f).Within(.001f));
                }
            }
            s.relief=.1f;s.shadingStrength=3;var extreme=f.Render();
            var covered=extreme[TexturePaintChannel.NormalControl].Where(c=>c.a>.1f).Select(c=>c.r).ToArray();
            Assert.That(covered,Is.Not.Empty);
            Assert.That(covered.Min(),Is.GreaterThan(.001f),"Negative detail must not clip into a flat black recess");
            Assert.That(covered.Max(),Is.LessThan(.999f),"Positive detail must not clip into a flat white plateau");
            if(IsMaterialOnly(preset))
                Assert.That(covered.All(v=>Mathf.Abs(v-.5f)<.0005f),Is.True,"Wash and printing remain material effects at any relief setting");
            else
                Assert.That(covered.Max()-covered.Min(),Is.GreaterThan(.003f),"Raised construction must retain a shaped height profile");
            foreach(var c in extreme[TexturePaintChannel.Custom])
            {
                Assert.That(c.r,Is.InRange(0f,1f));Assert.That(c.g,Is.InRange(0f,1f));
                Assert.That(c.b,Is.InRange(0f,1f));Assert.That(c.a,Is.InRange(0f,1f));
            }
        }

        [Test]
        public void UnresolvedDarningRetainsThreadVolumeWithoutInventingLargeRelief()
        {
            using var f=new Fixture(TexturePaintGarmentPreset.DarnedRepair);
            var s=f.settings.garment;s.height=true;s.relief=.1f;s.depthStrength=4;s.falloff=.001f;
            var pixels=f.Render()[TexturePaintChannel.NormalControl];
            float crown=pixels.Where(c=>c.a>.1f).Max(c=>c.r);
            Assert.That(crown,Is.GreaterThan(.5002f),"Subpixel yarn must retain its average physical volume");
            Assert.That(crown,Is.LessThan(.504f),"A large Relief Height cannot turn fine darning yarn into a raised block");
        }

        [TestCaseSource(nameof(EveryGarmentConstruction))]
        public void ShadingStrengthChangesContactsWithoutChangingHeightOrMaterialPlacement(TexturePaintGarmentPreset preset)
        {
            using var f=new Fixture(preset);
            var s=f.settings.garment;s.normal=s.height=s.metallic=true;s.relief=.008f;s.falloff=.001f;
            s.shadingStrength=0;var unshaded=f.Render();
            s.shadingStrength=2;var shaded=f.Render();
            foreach(var channel in new[]{TexturePaintChannel.Normal,TexturePaintChannel.NormalControl,TexturePaintChannel.Metallic})
                Assert.That(Difference(unshaded[channel],shaded[channel]),Is.LessThan(.00001f),channel+" placement and relief must be independent of contact shading");
            Assert.That(unshaded[TexturePaintChannel.AmbientOcclusion].Max(c=>c.a),Is.LessThan(.00001f),
                "Shading zero removes the generated contact AO");
            if(!IsMaterialOnly(preset))
            {
                Assert.That(shaded[TexturePaintChannel.AmbientOcclusion].Max(c=>c.a),Is.GreaterThan(.01f),
                    "Raised detail should seat on the material below");
                Assert.That(Difference(unshaded[TexturePaintChannel.Albedo],shaded[TexturePaintChannel.Albedo]),Is.GreaterThan(.0005f),
                    "Contact depth must also be visible when the material has no AO map");
            }
        }

        [TestCase(TexturePaintGarmentPreset.TensionFolds)]
        [TestCase(TexturePaintGarmentPreset.Pleats)]
        [TestCase(TexturePaintGarmentPreset.DirtyCuffs)]
        [TestCase(TexturePaintGarmentPreset.PatchPocket)]
        [TestCase(TexturePaintGarmentPreset.Waistband)]
        [TestCase(TexturePaintGarmentPreset.Buttonhole)]
        [TestCase(TexturePaintGarmentPreset.SewnButton)]
        [TestCase(TexturePaintGarmentPreset.Rivet)]
        [TestCase(TexturePaintGarmentPreset.FrayedTear)]
        [TestCase(TexturePaintGarmentPreset.DarnedRepair)]
        [TestCase(TexturePaintGarmentPreset.LeatherPatch)]
        [TestCase(TexturePaintGarmentPreset.RubberBadge)]
        [TestCase(TexturePaintGarmentPreset.EmbroideredPatch)]
        public void NonZipperDepthChangesOnlyPhysicalRelief(TexturePaintGarmentPreset preset)
        {
            using var f=new Fixture(preset);
            var s=f.settings.garment;s.height=true;s.relief=.008f;s.depthStrength=0;s.falloff=.001f;
            var flat=f.Render();s.depthStrength=3;var raised=f.Render();
            foreach(var channel in new[]{TexturePaintChannel.Albedo,TexturePaintChannel.AmbientOcclusion})
                Assert.That(Difference(flat[channel],raised[channel]),Is.LessThan(.00001f),
                    channel+" remains controlled by Shading Strength, independently of depth");
            if(!IsMaterialOnly(preset))
                Assert.That(Difference(flat[TexturePaintChannel.NormalControl],raised[TexturePaintChannel.NormalControl]),Is.GreaterThan(.0005f));
        }

        [TestCase(TexturePaintGarmentPreset.PatchPocket)]
        [TestCase(TexturePaintGarmentPreset.ReinforcementPanel)]
        [TestCase(TexturePaintGarmentPreset.RepairPatch)]
        public void ClothPanelContactSitsOutsideTheOverlapInsteadOfDirtyingTheWholeFace(TexturePaintGarmentPreset preset)
        {
            using var f=new Fixture(preset);
            var s=f.settings.garment;s.stitchRows=0;s.wear=0;s.recess=1;s.falloff=.001f;s.shadingStrength=1;
            var ao=f.Render()[TexturePaintChannel.AmbientOcclusion];
            float edge=preset==TexturePaintGarmentPreset.RepairPatch?.87f:.89f;
            float outside=MeanWindow(ao,new Rect(edge,.46f,.025f,.08f),c=>c.a);
            float inside=MeanWindow(ao,new Rect(edge-.09f,.46f,.025f,.08f),c=>c.a);
            Assert.That(outside,Is.GreaterThan(inside+.025f),
                "The lapped edge must shadow exposed fabric beside the panel, while its face stays clean");
        }

        [Test]
        public void LeatherImpressionsRecessWhileMoldedRubberMarksRise()
        {
            float MotifRelief(TexturePaintGarmentPreset preset)
            {
                using var f=new Fixture(preset);
                var s=f.settings.garment;s.relief=.012f;s.damage=0;s.stitchRows=0;s.falloff=.001f;
                var h=f.Render()[TexturePaintChannel.NormalControl];
                float motif=MeanWindow(h,new Rect(.47f,.47f,.06f,.06f),c=>c.r);
                float baseMaterial=MeanWindow(h,new Rect(.47f,.74f,.06f,.04f),c=>c.r);
                return motif-baseMaterial;
            }
            Assert.That(MotifRelief(TexturePaintGarmentPreset.LeatherPatch),Is.LessThan(-.004f),"Stamped leather presses into the hide");
            Assert.That(MotifRelief(TexturePaintGarmentPreset.RubberBadge),Is.GreaterThan(.008f),"Molded rubber has a raised emblem");
        }

        [Test]
        public void PrintedInkNeverAcquiresPatchEdgesOrLiftedCorners()
        {
            using var f=new Fixture(TexturePaintGarmentPreset.PrintedLogo);
            var s=f.settings.garment;s.normal=true;s.relief=.1f;s.depthStrength=4;s.shadingStrength=3;s.damage=1;
            var images=f.Render();
            foreach(var c in images[TexturePaintChannel.NormalControl].Where(c=>c.a>.01f))Assert.That(c.r,Is.EqualTo(.5f).Within(.0005f));
            Assert.That(images[TexturePaintChannel.AmbientOcclusion].Max(c=>c.a),Is.Zero);
            Assert.That(MeanWindow(images[TexturePaintChannel.Albedo],new Rect(.83f,.78f,.08f,.08f),c=>c.a),Is.LessThan(.001f),
                "A print has no raised backing or peeled patch corner outside the ink");
        }

        [Test]
        public void SharedDepthAndShadingControlsPreserveLegacyZipperValuesAndSavedZeroes()
        {
            foreach(float value in new[]{0f,1f,2.7f})
            {
                string old="{\"enabled\":true,\"preset\":15,\"zipperDepth\":"+value.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"zipperDepthVersion\":1}";
                var settings=JsonUtility.FromJson<TexturePaintGarmentSettings>(old);settings.Normalize();
                Assert.That(settings.depthStrength,Is.EqualTo(value));Assert.That(settings.shadingStrength,Is.EqualTo(1));
                settings.shadingStrength=0;
                var saved=JsonUtility.FromJson<TexturePaintGarmentSettings>(JsonUtility.ToJson(settings));saved.Normalize();
                Assert.That(saved.depthStrength,Is.EqualTo(value));Assert.That(saved.shadingStrength,Is.Zero);
                Assert.That(saved.Clone().depthStrength,Is.EqualTo(value));Assert.That(saved.Clone().shadingStrength,Is.Zero);
                var properties=new MaterialPropertyBlock();saved.Bind(properties,TexturePaintChannel.Albedo,Vector2.one);
                Assert.That(properties.GetFloat("_GarmentDepth"),Is.EqualTo(value));Assert.That(properties.GetFloat("_GarmentShading"),Is.Zero);
            }
            foreach(TexturePaintGarmentPreset preset in Enum.GetValues(typeof(TexturePaintGarmentPreset)))
            {
                var legacy=JsonUtility.FromJson<TexturePaintGarmentSettings>("{\"preset\":"+(int)preset+"}");legacy.Normalize();
                Assert.That(legacy.depthStrength,Is.EqualTo(1));Assert.That(legacy.shadingStrength,Is.EqualTo(1));
            }
            var invalid=TexturePaintGarmentSettings.Create(TexturePaintGarmentPreset.PatchPocket);
            invalid.depthStrength=float.NaN;invalid.shadingStrength=float.PositiveInfinity;invalid.Normalize();
            Assert.That(invalid.depthStrength,Is.EqualTo(1));Assert.That(invalid.shadingStrength,Is.EqualTo(1));
            invalid.depthStrength=9;invalid.shadingStrength=9;invalid.Normalize();
            Assert.That(invalid.depthStrength,Is.EqualTo(4));Assert.That(invalid.shadingStrength,Is.EqualTo(3));
        }
    }
}
#endif
