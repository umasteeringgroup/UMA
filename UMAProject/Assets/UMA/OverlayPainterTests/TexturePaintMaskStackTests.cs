#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintMaskStackTests
    {
        private TexturePaintGpuTestFixture fixture;
        private TextureLayerCompositor compositor;
        private readonly List<Object> objects = new();
        [SetUp] public void Setup()
        {
            fixture = new TexturePaintGpuTestFixture(Color.clear);
            compositor = new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            fixture.set.compositor = compositor;
        }
        [TearDown] public void Cleanup()
        {
            fixture.set.compositor = null; compositor.Dispose(); fixture.Dispose();
            foreach(var obj in objects) if(obj!=null)Object.DestroyImmediate(obj); objects.Clear();
        }
        private TexturePaintLayer Mask(float baseValue=1)
        {var layer=fixture.set.AddLayer("Mask stack");fixture.set.AddLayerMask(layer,baseValue);return layer;}
        private Color[] Pixels(TexturePaintLayer layer) => TexturePaintGpuTestFixture.ReadPixels((RenderTexture)fixture.set.GetLayerMaskPreview(layer));
        private Texture2D Image(Func<int,int,Color> pixel)
        {
            var texture=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);objects.Add(texture);
            var pixels=new Color[4096];for(int y=0;y<64;y++)for(int x=0;x<64;x++)pixels[y*64+x]=pixel(x,y);
            texture.SetPixels(pixels);texture.Apply();return texture;
        }
        [Test] public void OrderOpacityDisableAndDuplicateAreEvaluatedIndependently()
        {
            var layer=Mask(.4f);var effects=layer.layerMask.effects;
            var fill=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Fill);fill.value=.2f;fill.blend=TexturePaintMaskBlend.Add;
            var invert=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Invert);effects.stack.Add(fill);effects.stack.Add(invert);
            Assert.That(Pixels(layer)[100].r,Is.EqualTo(.4f).Within(.006));
            effects.stack.Reverse();Assert.That(Pixels(layer)[100].r,Is.EqualTo(.8f).Within(.006));
            fill.opacity=.5f;Assert.That(Pixels(layer)[100].r,Is.EqualTo(.7f).Within(.006));
            invert.enabled=false;Assert.That(Pixels(layer)[100].r,Is.EqualTo(.5f).Within(.006));
            effects.stack.Add(fill.Clone());effects.Normalize();Assert.That(effects.stack[1].id,Is.Not.EqualTo(effects.stack[2].id));
            Assert.That(Pixels(layer)[100].r,Is.EqualTo(.6f).Within(.006));
        }
        [Test] public void VoronoiExpansionAndContrastChangeCoverageWithoutMovingCells()
        {
            var layer=Mask();var effect=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Voronoi);
            effect.seed=73;layer.layerMask.effects.stack.Add(effect);
            Color[] original=Pixels(layer);
            Assert.That(effect.inputMin,Is.Zero);Assert.That(effect.amount,Is.EqualTo(1));
            effect.inputMin=.3f;
            Color[] expanded=Pixels(layer);
            Assert.That(expanded.Count(p=>p.r<.01f),Is.GreaterThan(original.Count(p=>p.r<.01f)+100));
            Assert.That(expanded.Average(p=>p.r),Is.LessThan(original.Average(p=>p.r)));
            for(int i=0;i<original.Length;i++)
                Assert.That(expanded[i].r,Is.EqualTo(Mathf.Clamp01((original[i].r-.3f)/.7f)).Within(.002f),"Coverage must remap the same cell at pixel "+i);
            effect.amount=4;
            Color[] contrasted=Pixels(layer);
            for(int i=0;i<original.Length;i++)
                Assert.That(contrasted[i].r,Is.EqualTo(Mathf.Clamp01((expanded[i].r-.5f)*4+.5f)).Within(.004f));
            Assert.That(contrasted.Count(p=>p.r>.1f&&p.r<.9f),Is.LessThan(expanded.Count(p=>p.r>.1f&&p.r<.9f)));
            // Presets, duplication and document serialization retain the authored controls.
            layer.layerMask.effects.stack[0]=JsonUtility.FromJson<TexturePaintMaskEffect>(JsonUtility.ToJson(effect.Clone()));
            Assert.That(Pixels(layer),Is.EqualTo(contrasted));
            layer.layerMask.effects.stack[0].inputMin=1;
            Assert.That(Pixels(layer).All(p=>p.r<.001f),Is.True);
            layer.layerMask.effects.stack[0].inputMin=0;layer.layerMask.effects.stack[0].amount=1;
            Assert.That(Pixels(layer),Is.EqualTo(original));
        }

        [TestCase(true)] [TestCase(false)]
        public void WhiteAndBlackPaintedMasksReplaceDormantEntriesAndUndoEverything(bool white)
        {
            var layer=Mask(.3f);layer.kind=TexturePaintLayerKind.Plugin;layer.name="Edge Wear";
            var original=layer.layerMask;
            original.target.Reset(Image((x,y)=>x<32?Color.black:Color.white),Color.white);
            original.effects.startFromPaint=false;
            var voronoi=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Voronoi);
            voronoi.opacity=.83f;original.effects.stack.Add(voronoi);
            var dormant=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.PaintedMask);
            dormant.opacity=0;dormant.enabled=false;original.effects.stack.Add(dormant);
            var duplicate=dormant.Clone();duplicate.id=Guid.NewGuid().ToString("N");original.effects.stack.Add(duplicate);
            var procedural=Pixels(layer);
            var oldPixels=TexturePaintGpuTestFixture.ReadPixels(original.target.Front);
            var stage=ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            object Invoke(string method,params object[] args)=>typeof(TexturePaintStageWindow).GetMethod(method,flags).Invoke(stage,args);
            try
            {
                Assert.That(Invoke("AddPaintedMaskEffectWithHistory",fixture.set,layer,original.effects,white),Is.True);
                var created=layer.layerMask;
                Assert.That(created.baseValue,Is.EqualTo(white?1:0));
                Assert.That(created.PaintValue,Is.EqualTo(white?0:1));
                Assert.That(created.effects.startFromPaint,Is.False);
                Assert.That(created.effects.stack.Count,Is.EqualTo(2));
                var painted=created.effects.stack.Last();
                Assert.That(painted.name,Is.EqualTo(white?"White Painted Mask":"Black Painted Mask"));
                Assert.That(painted.kind,Is.EqualTo(TexturePaintMaskEffectKind.PaintedMask));
                Assert.That(painted.opacity,Is.EqualTo(1));Assert.That(painted.enabled,Is.True);
                Assert.That(painted.blend,Is.EqualTo(TexturePaintMaskBlend.Multiply));
                Assert.That(painted.id,Is.Not.EqualTo(dormant.id));
                var initialized=Pixels(layer);
                for(int i=0;i<initialized.Length;i++)Assert.That(initialized[i].r,Is.EqualTo(white?procedural[i].r:0).Within(.003f));
                // A half-strength painted correction must be applied once, not squared.
                created.target.Reset(null,new Color(.5f,.5f,.5f,1));
                var corrected=Pixels(layer);
                for(int i=0;i<corrected.Length;i++)Assert.That(corrected[i].r,Is.EqualTo(procedural[i].r*.5f).Within(.004f));
                Assert.That(Invoke("UndoLightweight"),Is.True);
                Assert.That(layer.layerMask,Is.SameAs(original));
                TexturePaintGpuTestFixture.AssertImage("Undo paint initialization",oldPixels,TexturePaintGpuTestFixture.ReadPixels(original.target.Front));
                Assert.That(original.effects.stack[1].opacity,Is.Zero);
                Assert.That(Invoke("RedoLightweight"),Is.True);Assert.That(layer.layerMask,Is.SameAs(created));
                TexturePaintGpuTestFixture.AssertImage("Redo painted correction",corrected,Pixels(layer));
            }
            finally {Invoke("ClearLightweightHistory");Object.DestroyImmediate(stage);}
        }

        [Test] public void PaintedMaskCreationChoicesAndFactoryStartEnabledAtFullOpacity()
        {
            var labels=(string[])typeof(TexturePaintStageWindow).GetField("maskEffectChoiceLabels",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            Assert.That(labels,Does.Contain("White Painted Mask"));Assert.That(labels,Does.Contain("Black Painted Mask"));
            Assert.That(labels,Does.Not.Contain("Painted Mask"));
            var effect=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.PaintedMask);
            Assert.That(effect.opacity,Is.EqualTo(1));Assert.That(effect.enabled,Is.True);
        }

        [Test] public void ProceduralChangesKeepPaintedCorrectionsIndependent()
        {
            var layer=Mask();var painted=Image((x,y)=>x<32?Color.black:Color.white);layer.layerMask.target.Reset(painted,Color.white);
            var effects=layer.layerMask.effects;effects.startFromPaint=false;
            var fill=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Fill);fill.value=.8f;effects.stack.Add(fill);
            effects.stack.Add(TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.PaintedMask));
            var pixels=Pixels(layer);Assert.That(pixels[20*64+16].r,Is.Zero.Within(.002));Assert.That(pixels[20*64+48].r,Is.EqualTo(.8f).Within(.002));
            fill.value=.3f;pixels=Pixels(layer);Assert.That(pixels[20*64+16].r,Is.Zero.Within(.002));Assert.That(pixels[20*64+48].r,Is.EqualTo(.3f).Within(.002));
            Assert.That(TexturePaintGpuTestFixture.ReadPixels(layer.layerMask.target.Front)[20*64+48].r,Is.EqualTo(1));
        }
        [Test] public void BlurMorphologyAndDistanceOperateOnThePreviousStackResult()
        {
            var layer=Mask();layer.layerMask.target.Reset(Image((x,y)=>x>=24 && x<40 && y>=24 && y<40?Color.white:Color.black),Color.black);
            var effect=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Dilate);effect.radius=4;layer.layerMask.effects.stack.Add(effect);
            Assert.That(Pixels(layer)[32*64+21].r,Is.GreaterThan(.99));
            effect.kind=TexturePaintMaskEffectKind.Erode;Assert.That(Pixels(layer)[32*64+26].r,Is.LessThan(.01));Assert.That(Pixels(layer)[32*64+32].r,Is.GreaterThan(.99));
            effect.kind=TexturePaintMaskEffectKind.Blur;var pixels=Pixels(layer);Assert.That(pixels[32*64+23].r,Is.InRange(.01f,.49f));Assert.That(pixels[32*64+24].r,Is.InRange(.51f,.99f));
            effect.kind=TexturePaintMaskEffectKind.Feather;effect.radius=8;pixels=Pixels(layer);
            Assert.That(pixels[32*64+20].r,Is.InRange(.01f,.49f));Assert.That(pixels[32*64+28].r,Is.InRange(.51f,.99f));
            Assert.That(pixels[32*64+4].r,Is.LessThan(.01));
        }
        [Test] public void SpatialMasksUpdateCompositeOutsideThePaintedDirtyRectangle()
        {
            var layer=Mask(0);layer.channels[TexturePaintChannel.Albedo]=new EditableTextureTarget("Dirty-region paint",64,64,RenderTextureFormat.ARGBHalf,null,Color.red);
            var target=fixture.set.GetChannel(TexturePaintChannel.Albedo);target.composite=EditableTextureTarget.Create("Mask dirty-region test",64,64,RenderTextureFormat.ARGBHalf);
            var effect=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Dilate);effect.radius=8;layer.layerMask.effects.stack.Add(effect);fixture.set.RecomposeAll();
            var paint=Image((x,y)=>x==32&&y==32?Color.white:Color.black);layer.layerMask.target.Reset(paint,Color.black);
            fixture.set.CompositeChannel(TexturePaintChannel.Albedo,new RectInt(32,32,1,1));
            Assert.That(TexturePaintGpuTestFixture.ReadPixels(target.composite)[32*64+38].r,Is.GreaterThan(.99),"Dilation changes pixels outside the painted tile.");
            layer.layerMask.target.Reset(null,Color.black);fixture.set.CompositeChannel(TexturePaintChannel.Albedo,new RectInt(32,32,1,1));
            Assert.That(TexturePaintGpuTestFixture.ReadPixels(target.composite)[32*64+38].r,Is.LessThan(.01),"Removing paint must clear the expanded result too.");
        }
        [Test] public void TextureChangesAndCurveChangesInvalidateCachedMask()
        {
            var layer=Mask();var effect=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Texture);effect.texture=Image((x,y)=>new Color(.2f,.2f,.2f,1));layer.layerMask.effects.stack.Add(effect);
            Assert.That(Pixels(layer)[100].r,Is.EqualTo(.2f).Within(.002));
            effect.texture.SetPixels(Enumerable.Repeat(new Color(.8f,.8f,.8f,1),4096).ToArray());effect.texture.Apply();
            Assert.That(Pixels(layer)[100].r,Is.EqualTo(.8f).Within(.002));
            var curves=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Curves);curves.curve=AnimationCurve.Linear(0,1,1,0);layer.layerMask.effects.stack.Add(curves);
            Assert.That(Pixels(layer)[100].r,Is.EqualTo(.2f).Within(.01));
            curves.curve=AnimationCurve.Linear(0,.3f,1,.3f);Assert.That(Pixels(layer)[100].r,Is.EqualTo(.3f).Within(.002));
        }
        [TestCaseSource(nameof(Kinds))] public void EveryEffectProducesFiniteGrayscaleOutput(TexturePaintMaskEffectKind kind)
        {
            var layer=Mask(.35f);var effect=TexturePaintMaskEffect.Create(kind);effect.texture=Image((x,y)=>new Color(x/63f,y/63f,.4f,1));
            if(kind>=TexturePaintMaskEffectKind.Curvature)effect.texture=null;
            layer.layerMask.effects.stack.Add(effect);var pixels=Pixels(layer);
            foreach(var pixel in pixels)
            {Assert.That(float.IsFinite(pixel.r),Is.True);Assert.That(pixel.r,Is.InRange(0f,1f));Assert.That(pixel.g,Is.EqualTo(pixel.r).Within(.001));Assert.That(pixel.b,Is.EqualTo(pixel.r).Within(.001));}
        }
        private static IEnumerable<TexturePaintMaskEffectKind> Kinds => Enum.GetValues(typeof(TexturePaintMaskEffectKind)).Cast<TexturePaintMaskEffectKind>();
        [Test] public void ReferencesUpdateInDependencyOrderAndRejectCycles()
        {
            using var store=new TextureStore();var sets=(List<TextureSet>)typeof(TextureStore).GetField("sets",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(store);sets.Add(fixture.set);
            try
            {
                var source=Mask(.4f);var target=Mask();var effect=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.LayerReference);
                effect.reference=TexturePaintLayerReference.To(fixture.set,source);effect.reference.component=TexturePaintReferenceComponent.Mask;target.layerMask.effects.stack.Add(effect);
                store.RefreshLayerLinks();Assert.That(target.linkError,Is.Null);Assert.That(Pixels(target)[100].r,Is.EqualTo(.4f).Within(.006));
                source.layerMask.target.Reset(null,Color.white);store.RefreshLayerLinks();Assert.That(Pixels(target)[100].r,Is.GreaterThan(.99));
                var cycle=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.LayerReference);cycle.reference=TexturePaintLayerReference.To(fixture.set,target);cycle.reference.component=TexturePaintReferenceComponent.Mask;source.layerMask.effects.stack.Add(cycle);
                store.RefreshLayerLinks();Assert.That(source.linkError,Is.Not.Empty);Assert.That(target.linkError,Is.Not.Empty);Assert.That(Pixels(target)[100].r,Is.GreaterThan(.99));
            }
            finally {sets.Clear();fixture.set.ownerStore=null;}
        }
        [Test] public void MissingReferenceKeepsItsSavedMaskAfterDocumentReopen()
        {
            using var store=new TextureStore();var sets=(List<TextureSet>)typeof(TextureStore).GetField("sets",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(store);sets.Add(fixture.set);
            try
            {
                var source=Mask(.4f);var destination=Mask();var effect=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.LayerReference);
                effect.reference=TexturePaintLayerReference.To(fixture.set,source);effect.reference.component=TexturePaintReferenceComponent.Mask;destination.layerMask.effects.stack.Add(effect);
                store.RefreshLayerLinks();var expected=Pixels(destination);
                var doc=ScriptableObject.CreateInstance<TexturePaintDocument>();objects.Add(doc);TexturePaintDocumentStorage.Save(doc,store);
                Assert.That(doc.surfaces[0].layers[1].maskReferenceCaches.Count,Is.EqualTo(1));doc.surfaces[0].layers.RemoveAt(0);
                TexturePaintDocumentStorage.Restore(doc,store);store.RefreshLayerLinks();destination=fixture.set.layers[0];
                Assert.That(destination.linkError,Is.Not.Empty);
                TexturePaintGpuTestFixture.AssertImage("Missing saved mask input",expected,Pixels(destination),.005f,.005f);
            }
            finally {sets.Clear();fixture.set.ownerStore=null;}
        }
        [Test] public void DuplicatedGroupsRemapMaskInputsToTheirCopiedChildren()
        {
            var source=Mask(.4f);var target=Mask();var group=fixture.set.AddGroup("Group");source.parentId=target.parentId=group.id;
            var effect=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.LayerReference);effect.reference=TexturePaintLayerReference.To(fixture.set,source);target.layerMask.effects.stack.Add(effect);
            var copiedGroup=fixture.set.DuplicateLayerAt(fixture.set.layers.IndexOf(group));
            var children=fixture.set.layers.Where(layer=>layer.parentId==copiedGroup.id).ToList();
            var copiedTarget=children.Single(layer=>layer.layerMask.effects.stack.Count==1);
            Assert.That(copiedTarget.layerMask.effects.stack[0].reference.layerId,Is.EqualTo(children.Single(layer=>layer!=copiedTarget).id));
        }
        [Test] public void LogicalUdimMaskReferencesReadTheCorrespondingLocalMember()
        {
            using var second=new TexturePaintGpuTestFixture(Color.clear);second.set.persistentId="second-tile";second.set.compositor=compositor;
            using var store=new TextureStore();var sets=(List<TextureSet>)typeof(TextureStore).GetField("sets",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(store);sets.Add(fixture.set);sets.Add(second.set);
            try
            {
                var firstSource=Mask(.3f);var secondSource=second.set.AddLayer("Source");second.set.AddLayerMask(secondSource,.7f);
                firstSource.logicalLayerId=secondSource.logicalLayerId="source";firstSource.paintTargetId=secondSource.paintTargetId="logical-target";
                var firstTarget=Mask();var secondTarget=second.set.AddLayer("Target");second.set.AddLayerMask(secondTarget,1);
                var effect=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.LayerReference);effect.reference=TexturePaintLayerReference.To(fixture.set,firstSource);effect.reference.component=TexturePaintReferenceComponent.Mask;
                firstTarget.layerMask.effects.stack.Add(effect);secondTarget.layerMask.effects.stack.Add(effect.Clone());store.RefreshLayerLinks();
                Assert.That(Pixels(firstTarget)[100].r,Is.EqualTo(.3f).Within(.006));
                Assert.That(TexturePaintGpuTestFixture.ReadPixels((RenderTexture)second.set.GetLayerMaskPreview(secondTarget))[100].r,Is.EqualTo(.7f).Within(.006));
            }
            finally {sets.Clear();fixture.set.ownerStore=null;second.set.ownerStore=null;second.set.compositor=null;}
        }

        [Test] public void DocumentAndMaterialRecipeRoundtripPreserveOrderAndPaint()
        {
            using var store=new TextureStore();var sets=(List<TextureSet>)typeof(TextureStore).GetField("sets",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(store);sets.Add(fixture.set);
            try
            {
                var layer=Mask(.6f);layer.layerMask.effects=TexturePaintMaskPreset.CreateRecipe(TexturePaintSmartMask.Grunge);
                var doc=ScriptableObject.CreateInstance<TexturePaintDocument>();objects.Add(doc);TexturePaintDocumentStorage.Save(doc,store);
                string recipe=JsonUtility.ToJson(layer.layerMask.effects);var expected=Pixels(layer);
                TexturePaintDocumentStorage.Restore(doc,store);var restored=fixture.set.layers[0];Assert.That(JsonUtility.ToJson(restored.layerMask.effects),Is.EqualTo(recipe));
                TexturePaintGpuTestFixture.AssertImage("Mask stack document",expected,Pixels(restored),.002f,.001f);
                var preset=ScriptableObject.CreateInstance<TexturePaintMaterialPreset>();objects.Add(preset);TexturePaintMaterialPresetStorage.Capture(preset,fixture.set,new[]{restored},false,null);
                Assert.That(JsonUtility.ToJson(preset.layers[0].maskEffects),Is.EqualTo(recipe));
                var clone=restored.layerMask.effects.Clone();clone.stack[1].curve=AnimationCurve.Linear(0,1,1,0);
                Assert.That(JsonUtility.ToJson(restored.layerMask.effects),Is.EqualTo(recipe));
            }
            finally {sets.Clear();fixture.set.ownerStore=null;}
        }
    }
}
#endif
