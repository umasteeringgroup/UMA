#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintPathSymmetryTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object Invoke(object target,string method,params object[] args)
            =>typeof(TexturePaintStageWindow).GetMethod(method,Private).Invoke(target,args);
        private static void Set(object target,string field,object value)
            =>typeof(TexturePaintStageWindow).GetField(field,Private).SetValue(target,value);
        private static T Get<T>(object target,string field)=>(T)typeof(TexturePaintStageWindow).GetField(field,Private).GetValue(target);

        private sealed class Path:IDisposable
        {
            public readonly TexturePaintStageWindow stage=ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            public readonly BrushPreset brush=ScriptableObject.CreateInstance<BrushPreset>();
            public readonly TextureSet set=new TextureSet();
            public readonly TexturePaintLayer layer;
            private readonly TextureStore store=new TextureStore();
            private GameObject legacyRoot;
            public Path(bool worldSpace,bool legacyX=false,bool brushMirror=false,bool autoUpdate=false)
            {
                layer=set.AddSplineLayer("Symmetry path");layer.spline.worldSpace=worldSpace;
                layer.splineSettings=new TexturePaintSplineSettings
                {
                    brushSize=.04f,brushHardness=1,brushFlow=1,brushMirrorStroke=brushMirror,
                    mirrorX=legacyX,symmetry=new TexturePaintSymmetry{enabled=false,mirrorX=true},
                    autoUpdate=autoUpdate,editorSettingsVersion=1,pathMode=TexturePaintPathMode.Ribbon
                };
                ((List<TextureSet>)typeof(TextureStore).GetField("sets",Private).GetValue(store)).Add(set);
                var controller=new TexturePaintStageController();
                typeof(TexturePaintStageController).GetProperty("Textures").SetValue(controller,store);
                Set(stage,"controller",controller);Set(stage,"transientBrush",brush);Set(stage,"spline",layer.spline);
                Invoke(stage,"RestoreSplineSettings",layer.splineSettings);
                Invoke(stage,"CaptureSplineSettings",layer);
                if(legacyX || brushMirror)layer.layerSymmetryVersion=0;
            }
            public List<Matrix4x4> Transforms()=>
                (List<Matrix4x4>)Invoke(stage,"GetPathSymmetryTransforms",!layer.spline.worldSpace,brush,
                    legacyRoot!=null ? legacyRoot.transform.position : Vector3.zero);
            public void SetLegacyRoot(Vector3 position)
            {
                legacyRoot=new GameObject("Translated symmetry root");legacyRoot.transform.position=position;
                var reconstruction=new MeshReconstructionResult{root=legacyRoot};
                typeof(TexturePaintStageController).GetProperty("Reconstruction").SetValue(Get<TexturePaintStageController>(stage,"controller"),reconstruction);
            }
            public void Edit(TexturePaintSymmetry frame)
                =>Invoke(stage,"ApplyLayerSymmetryEdit",set,frame);
            public void Dispose()
            {
                Invoke(stage,"CancelScheduledSplineReapply");Invoke(stage,"ClearLightweightHistory");
                Set(stage,"controller",null);Object.DestroyImmediate(stage);Object.DestroyImmediate(brush);store.Dispose();
                if(legacyRoot!=null)Object.DestroyImmediate(legacyRoot);
            }
        }

        [TestCase(false,false)] [TestCase(false,true)]
        [TestCase(true,false)] [TestCase(true,true)]
        public void ToolbarTurnsOffLegacyBrushMirrorWithUndoPersistenceAndAutoUpdate(bool worldSpace,bool autoUpdate)
        {
            using var p=new Path(worldSpace,false,true,autoUpdate);
            Assert.That(p.stage.SceneToolbarMirrorX,Is.True,"Toolbar must display the effective legacy brush mirror");
            Assert.That(p.Transforms().Count,Is.EqualTo(2));
            p.stage.SceneToolbarMirrorX=false;
            Assert.That(p.layer.layerSymmetry.enabled,Is.True);
            Assert.That(p.layer.layerSymmetry.mirrorX,Is.False);
            Assert.That(p.brush.mirrorStroke,Is.True,"Keep the brush recipe; the layer frame must override its legacy mirror");
            Assert.That(p.Transforms().Count,Is.EqualTo(1));
            Assert.That(Get<bool>(p.stage,"splineReapplyPending"),Is.EqualTo(autoUpdate));
            Assert.That(Get<bool>(p.stage,"splineReapplyDelayScheduled"),Is.EqualTo(autoUpdate));
            Assert.That(Invoke(p.stage,"UndoLightweight"),Is.True);
            Assert.That(p.Transforms().Count,Is.EqualTo(2));
            Assert.That(Invoke(p.stage,"RedoLightweight"),Is.True);
            Assert.That(p.Transforms().Count,Is.EqualTo(1));
            var saved=JsonUtility.FromJson<TexturePaintSplineSettings>(JsonUtility.ToJson(p.layer.splineSettings));
            var savedFrame=JsonUtility.FromJson<TexturePaintSymmetry>(JsonUtility.ToJson(p.layer.layerSymmetry));
            using var reopened=new Path(worldSpace);
            reopened.layer.layerSymmetry=savedFrame;
            reopened.layer.splineSettings=saved;Invoke(reopened.stage,"RestoreSplineSettings",saved);
            Assert.That(reopened.stage.SceneToolbarMirrorX,Is.False);
            Assert.That(reopened.Transforms().Count,Is.EqualTo(1));
            Assert.That(saved.brushMirrorStroke,Is.True);
        }

        [TestCase(false)] [TestCase(true)]
        public void LegacyEnabledFrameWithXOffMigratesWithoutFallbackToOtherMirrorSettings(bool worldSpace)
        {
            using var p=new Path(worldSpace,true,true);
            p.layer.splineSettings.symmetry=new TexturePaintSymmetry{enabled=true,mirrorX=false};
            p.layer.splineSettings.localSymmetry=new TexturePaintSymmetry{enabled=true,mirrorX=true};
            Assert.That(p.stage.SceneToolbarMirrorX,Is.False);
            Assert.That(p.Transforms().Count,Is.EqualTo(1),"The old effective frame must migrate even when it has no copies");
            p.stage.SceneToolbarMirrorX=true;Assert.That(p.Transforms().Count,Is.EqualTo(2));
            p.stage.SceneToolbarMirrorX=false;Assert.That(p.Transforms().Count,Is.EqualTo(1));
            Assert.That(p.layer.layerSymmetry.mirrorX,Is.False);
        }

        [TestCase(false)] [TestCase(true)]
        public void LayerFrameAxesAndOriginUseTheCorrectPathSpace(bool worldSpace)
        {
            using var p=new Path(worldSpace);
            var frame=new TexturePaintSymmetry{enabled=true,mirrorX=false,mirrorY=true,origin=new Vector3(.1f,.2f,0)};
            p.Edit(frame);
            Vector3 point=new Vector3(.25f,.35f,0);
            Vector3 localPoint=p.Transforms()[1].MultiplyPoint3x4(point);
            Assert.That(localPoint.x,Is.EqualTo(.25f).Within(.00001f));
            Assert.That(localPoint.y,Is.EqualTo(worldSpace ? .05f : 1.05f).Within(.00001f));
            frame.mirrorY=false;frame.mirrorX=true;p.Edit(frame);
            Vector3 sharedPoint=p.Transforms()[1].MultiplyPoint3x4(point);
            Assert.That(sharedPoint.x,Is.EqualTo(worldSpace?-.05f:.95f).Within(.00001f));
            Assert.That(sharedPoint.y,Is.EqualTo(.35f).Within(.00001f));
            frame.mirrorY=true;frame.mirrorX=false;p.Edit(frame);
            Assert.That(Vector3.Distance(p.Transforms()[1].MultiplyPoint3x4(point),localPoint),Is.LessThan(.00001f));
        }

        [TestCase(false)] [TestCase(true)]
        public void LegacyXMirrorsInTheCorrectPathSpace(bool worldSpace)
        {
            using var p=new Path(worldSpace,true);
            var transforms=p.Transforms();Assert.That(transforms.Count,Is.EqualTo(2));
            Vector3 reflected=transforms[1].MultiplyPoint3x4(new Vector3(.2f,.3f,0));
            Assert.That(reflected.x,Is.EqualTo(worldSpace?-.2f:.8f).Within(.00001f));
            Assert.That(reflected.y,Is.EqualTo(.3f).Within(.00001f));
            p.stage.SceneToolbarMirrorX=false;Assert.That(p.Transforms().Count,Is.EqualTo(1));
        }

        [TestCase(false)] [TestCase(true)]
        public void TurningOffLegacyXPreservesRadialPlacementWithATranslatedCharacterRoot(bool worldSpace)
        {
            using var p=new Path(worldSpace,true);
            Vector3 root=new Vector3(2,3,4);p.SetLegacyRoot(root);
            p.layer.splineSettings.radialSymmetry=4;
            Set(p.stage,"radialSymmetry",4);Invoke(p.stage,"CaptureSplineSettings",p.layer);
            Vector3 point=worldSpace ? root+Vector3.right : new Vector3(.8f,.5f,0);
            var before=p.Transforms();Assert.That(before.Count,Is.EqualTo(8));
            p.stage.SceneToolbarMirrorX=false;
            var after=p.Transforms();Assert.That(after.Count,Is.EqualTo(4));
            Assert.That(p.layer.layerSymmetry.origin,Is.EqualTo(worldSpace ? root : Vector3.zero));
            for(int i=0;i<4;i++)
                Assert.That(Vector3.Distance(after[i].MultiplyPoint3x4(point),before[i*2].MultiplyPoint3x4(point)),
                    Is.LessThan(.00001f),"Turning off X must not move the surviving radial copies");
            Assert.That(Vector3.Distance(after[1].MultiplyPoint3x4(point),worldSpace ? root+Vector3.back : new Vector3(.5f,.8f,0)),Is.LessThan(.00001f));
            Assert.That(Invoke(p.stage,"UndoLightweight"),Is.True);Assert.That(p.Transforms().Count,Is.EqualTo(8));
            Assert.That(Invoke(p.stage,"RedoLightweight"),Is.True);
            Assert.That(Vector3.Distance(p.Transforms()[1].MultiplyPoint3x4(point),after[1].MultiplyPoint3x4(point)),Is.LessThan(.00001f));
        }

        [Test]
        public void UVStampsUseTheLayerFrameAndCorrectReflectedTextureHandedness()
        {
            using var p=new Path(false,false,true);
            var local=new TexturePaintSymmetry{enabled=true,mirrorX=false,mirrorY=true};
            p.Edit(local);
            var sample=new StrokeSample(new Vector3(.2f,.3f,0),Vector3.forward,new Vector2(.2f,.3f),0,0)
                {previousUV=new Vector2(.1f,.2f),direction=Vector3.right,footprintScale=Vector2.one};
            Invoke(p.stage,"QueueDirectUVSplineFootprint",sample);
            var queued=Get<List<StrokeDispatchSample>>(p.stage,"splineDispatchSamples");
            Assert.That(queued.Count,Is.EqualTo(2));
            Assert.That(Vector2.Distance(queued[1].sample.uv,new Vector2(.2f,.7f)),Is.LessThan(.00001f));
            Assert.That(Vector2.Distance(queued[1].sample.previousUV,new Vector2(.1f,.8f)),Is.LessThan(.00001f));
            Assert.That(queued[1].sample.footprintScale.y,Is.EqualTo(-1));
        }

        [Test]
        public void UVRadialCopiesRotateInTheUVPlaneAroundTextureCenter()
        {
            using var p=new Path(false);
            p.Edit(new TexturePaintSymmetry{enabled=true,mirrorX=false,radialCopies=4});
            var transforms=p.Transforms();Assert.That(transforms.Count,Is.EqualTo(4));
            Vector3 rotated=transforms[1].MultiplyPoint3x4(new Vector3(.8f,.5f,0));
            Assert.That(Vector3.Distance(rotated,new Vector3(.5f,.8f,0)),Is.LessThan(.00001f));
        }

        [TestCase(false)] [TestCase(true)]
        public void LayerMirrorCheckboxControlsTheCurrentPath(bool worldSpace)
        {
            using var p=new Path(worldSpace,true);
            var window=ScriptableObject.CreateInstance<TexturePaintConstructionInputTestWindow>();
            bool focus=true;string focused=null;
            window.draw=()=>
            {
                Invoke(p.stage,"DrawSymmetryPropertiesForSet",p.set);
                if(focus)GUI.FocusControl("SymmetryMirrorX");
                focused=GUI.GetNameOfFocusedControl();
            };
            try
            {
                window.wantsMouseMove=true;window.ShowUtility();window.position=new Rect(100,100,500,700);window.Focus();
                window.SendEvent(new Event{type=EventType.Layout});window.SendEvent(new Event{type=EventType.Repaint});
                window.SendEvent(new Event{type=EventType.MouseMove,mousePosition=new Vector2(400,10)});
                Assert.That(focused,Is.EqualTo("SymmetryMirrorX"));focus=false;
                window.SendEvent(new Event{type=EventType.KeyDown,keyCode=KeyCode.Space,character=' '});
                window.SendEvent(new Event{type=EventType.KeyUp,keyCode=KeyCode.Space});
                Assert.That(p.layer.layerSymmetry.enabled,Is.True);
                Assert.That(p.layer.layerSymmetry.mirrorX,Is.False);
                Assert.That(p.Transforms().Count,Is.EqualTo(1));
            }
            finally{window.draw=null;window.Close();}
        }

        [TestCase(false,false)] [TestCase(false,true)]
        [TestCase(true,false)] [TestCase(true,true)]
        public void RibbonRenderingUsesTheSelectedLayerFrameInsteadOfHiddenBrushMirroring(bool directUV,bool mirror)
        {
            using var p=new Path(!directUV,false,true);
            p.Edit(new TexturePaintSymmetry
                {enabled=true,mirrorX=mirror,origin=directUV?Vector3.zero:new Vector3(.5f,.5f,0)});
            using var fixture=new TexturePaintGpuTestFixture(Color.clear);
            var brush=fixture.CreateBrush(1,1,shape:BrushPreset.Shape.Square);brush.size=.08f;brush.mirrorStroke=true;
            var layer=fixture.set.AddLayer("Local symmetry output");
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            using var engine=new PaintingEngine(null,null,null,shader);
            try
            {
                var samples=new List<StrokeSample>
                {
                    new StrokeSample(new Vector3(.18f,.5f,0),Vector3.forward,new Vector2(.18f,.5f),0,0),
                    new StrokeSample(new Vector3(.4f,.5f,0),Vector3.forward,new Vector2(.4f,.5f),0,1)
                };
                var source=TexturePaintStageWindow.BuildRibbonSegments(samples,brush.size,1);
                var copies=(List<TexturePaintRibbonSegment>)Invoke(p.stage,"ExpandPathRibbon",source,directUV,brush,Vector3.zero);
                Assert.That(copies.Count,Is.EqualTo(mirror?2:1));
                var context=fixture.CreateContext(brush,TexturePaintTool.Paint,Color.white,strength:1);
                context.projectionDepth=1;context.directUV=directUV;
                Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay),Is.True);
                Assert.That(engine.ApplyRibbon(copies,samples,false,false,false,directUV),Is.True);
                var pixels=TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front);
                Assert.That(pixels[32*64+18].a,Is.GreaterThan(.99f));
                Assert.That(pixels[32*64+45].a,Is.EqualTo(mirror?1:0).Within(.001f));
            }
            finally{engine.EndStroke(false);Object.DestroyImmediate(brush);}
        }
    }
}
#endif
