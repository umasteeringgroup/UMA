#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintOriginalOverlayTests
    {
        [TestCase(TexturePaintChannel.Albedo)]
        [TestCase(TexturePaintChannel.Roughness)]
        [TestCase(TexturePaintChannel.Normal)]
        [TestCase(TexturePaintChannel.NormalControl)]
        public void HidingOriginalLeavesTransparentNeutralBaseAndPreservesNewLayers(TexturePaintChannel channel)
        {
            using var fixture=new TexturePaintGpuTestFixture(new Color(.2f,.3f,.4f,1),channel);
            using var compositor=new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            var set=fixture.set;set.compositor=compositor;
            var target=set.GetChannel(channel);
            target.composite=EditableTextureTarget.Create("Base Visibility Test",64,64,target.format);
            var original=TexturePaintGpuTestFixture.ReadPixels(target.editable.Front);
            set.hideOriginalOverlay=true;set.RecomposeAll();
            var empty=TexturePaintGpuTestFixture.ReadPixels(target.composite)[0];
            Assert.That(empty.a,Is.EqualTo(0).Within(.002f));
            Assert.That(empty.r,Is.EqualTo(TextureSet.DefaultColor(channel).r).Within(.002f));
            var layer=set.AddLayer("New Artwork");
            var paint=set.GetPaintTarget(channel,TexturePaintSourceMode.SourceOverlay);
            paint.Reset(null,new Color(.6f,.4f,.2f,.5f));
            set.RecomposeAll();
            var result=TexturePaintGpuTestFixture.ReadPixels(target.composite)[0];
            Assert.That(result.a,Is.EqualTo(.5f).Within(.003f));
            Assert.That(result.r,Is.EqualTo(.6f).Within(.004f),"Neutral transparent RGB must not tint new artwork.");
            set.CompositeChannel(channel,new RectInt(0,0,16,16));
            var partial=TexturePaintGpuTestFixture.ReadPixels(target.composite);
            Assert.That(partial[0].r,Is.EqualTo(result.r).Within(.004f));
            Assert.That(partial[63*64+63].r,Is.EqualTo(result.r).Within(.004f),"Partial repaint must not divide untouched pixels by alpha again.");
            Assert.That(compositor.ComposeBelowLayer(set,channel,layer,target.composite),Is.True);
            Assert.That(TexturePaintGpuTestFixture.ReadPixels(target.composite)[0].a,Is.EqualTo(0).Within(.002f),"Generators sampling below the first layer must not see hidden source artwork.");
            var untouched=TexturePaintGpuTestFixture.ReadPixels(target.editable.Front);
            Assert.That(untouched,Is.EqualTo(original));
            layer.visible=false;set.hideOriginalOverlay=false;set.RecomposeAll();
            Assert.That(TexturePaintGpuTestFixture.ReadPixels(target.composite),Is.EqualTo(original));
            set.compositor=null;
        }

        [Test]
        public void HiddenOriginalSurvivesDocumentRoundTripAndPixelsRemainRecoverable()
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.red);
            using var store=new TextureStore();var sets=(List<TextureSet>)store.Sets;sets.Add(fixture.set);
            var document=TexturePaintDocumentStorage.CreateTransient(null);
            try
            {
                fixture.set.hideOriginalOverlay=true;TexturePaintDocumentStorage.Save(document,store);
                Assert.That(document.surfaces[0].hideOriginalOverlay,Is.True);
                fixture.set.hideOriginalOverlay=false;TexturePaintDocumentStorage.Restore(document,store);
                Assert.That(fixture.set.hideOriginalOverlay,Is.True);
                Assert.That(TexturePaintGpuTestFixture.ReadPixels(fixture.target.Front)[0],Is.EqualTo(Color.red));
                fixture.set.ClearModifications();Assert.That(fixture.set.hideOriginalOverlay,Is.False);
                Assert.That(new TexturePaintDocumentSurface().hideOriginalOverlay,Is.False,"Older documents keep their original artwork.");
            }
            finally{sets.Clear();Object.DestroyImmediate(document);}
        }

        [Test]
        public void OriginalVisibilitySupportsUndoAndRedoWithoutDeletingPixels()
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.red);
            var stage=ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            try
            {
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(TexturePaintStageWindow);
                type.GetMethod("ChangeOriginalOverlayVisibility",flags).Invoke(stage,new object[]{fixture.set,false});
                Assert.That(fixture.set.hideOriginalOverlay,Is.True);
                type.GetMethod("UndoLightweight",flags).Invoke(stage,null);
                Assert.That(fixture.set.hideOriginalOverlay,Is.False);
                type.GetMethod("RedoLightweight",flags).Invoke(stage,null);
                Assert.That(fixture.set.hideOriginalOverlay,Is.True);
                Assert.That(TexturePaintGpuTestFixture.ReadPixels(fixture.target.Front)[0],Is.EqualTo(Color.red));
            }
            finally{Object.DestroyImmediate(stage);}
        }
    }
}
#endif
