#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintLayerSymmetryTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object Invoke(TexturePaintStageWindow stage,string method,params object[] args)
            =>typeof(TexturePaintStageWindow).GetMethod(method,Private).Invoke(stage,args);
        private static void Set(TexturePaintStageWindow stage,string field,object value)
            =>typeof(TexturePaintStageWindow).GetField(field,Private).SetValue(stage,value);

        private sealed class Workspace:IDisposable
        {
            public readonly TexturePaintStageWindow stage=ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            public readonly BrushPreset brush=ScriptableObject.CreateInstance<BrushPreset>();
            public readonly TextureSet set=new TextureSet{persistentId="layer-symmetry-tests"};
            private readonly TextureStore store=new TextureStore();
            public Workspace()
            {
                ((List<TextureSet>)typeof(TextureStore).GetField("sets",Private).GetValue(store)).Add(set);
                var controller=new TexturePaintStageController();
                typeof(TexturePaintStageController).GetProperty("Textures").SetValue(controller,store);
                Set(stage,"controller",controller);Set(stage,"transientBrush",brush);
            }
            public TexturePaintLayer Add(TexturePaintLayerKind kind)
            {
                TexturePaintLayer layer=kind==TexturePaintLayerKind.Spline ? set.AddSplineLayer("Path") :
                    kind==TexturePaintLayerKind.Projection ? set.AddProjectionLayer("Projection") : set.AddLayer(kind.ToString());
                layer.kind=kind;
                if(kind==TexturePaintLayerKind.Spline)
                {
                    layer.spline.worldSpace=false;
                    layer.splineSettings=new TexturePaintSplineSettings{autoUpdate=false,editorSettingsVersion=1,
                        brushSize=.04f,pathMode=TexturePaintPathMode.Ribbon};
                }
                return layer;
            }
            public void Select(TexturePaintLayer layer)
            {set.activeLayerIndex=set.layers.FindIndex(x=>x.id==layer.id);Invoke(stage,"SyncActiveLayerSelection",set);}
            public TexturePaintLayer Current(string id)=>set.layers.Find(x=>x.id==id);
            public TexturePaintSymmetry Resolve(TexturePaintLayer layer)
                =>(TexturePaintSymmetry)Invoke(stage,"ResolveLayerSymmetry",layer);
            public void Edit(TexturePaintSymmetry frame)=>Invoke(stage,"ApplyLayerSymmetryEdit",set,frame);
            public void Dispose()
            {
                Invoke(stage,"CancelScheduledSplineReapply");Invoke(stage,"ClearLightweightHistory");
                Set(stage,"controller",null);Object.DestroyImmediate(stage);Object.DestroyImmediate(brush);store.Dispose();
            }
        }

        private static TexturePaintSymmetry Frame(int index,bool enabled=true)=>new TexturePaintSymmetry
        {
            enabled=enabled,mirrorX=index==0,mirrorY=index==1,mirrorZ=index==2,
            origin=new Vector3(.1f*index,.2f,.3f),euler=new Vector3(0,0,17*index),
            radialCopies=index+1,radialAxis=Vector3.up
        };
        private static void SameFrame(TexturePaintSymmetry actual,TexturePaintSymmetry expected)
            =>Assert.That(JsonUtility.ToJson(actual),Is.EqualTo(JsonUtility.ToJson(expected)));

        [Test]
        public void SwitchingPaintPathAndProjectionKeepsEachLayersOwnSymmetry()
        {
            using var w=new Workspace();
            var paint=w.Add(TexturePaintLayerKind.Paint);var path=w.Add(TexturePaintLayerKind.Spline);
            var projection=w.Add(TexturePaintLayerKind.Projection);
            var layers=new[]{paint,path,projection};
            for(int i=0;i<layers.Length;i++){w.Select(layers[i]);w.Edit(Frame(i));}
            for(int pass=0;pass<2;pass++)for(int i=layers.Length-1;i>=0;i--)
            {
                w.Select(layers[i]);SameFrame(w.Resolve(w.Current(layers[i].id)),Frame(i));
                Assert.That(w.stage.SceneToolbarMirrorX,Is.EqualTo(i==0));
            }
            w.Select(path);w.stage.SceneToolbarMirrorX=true;
            Assert.That(w.Current(path.id).layerSymmetry.mirrorX,Is.True);
            SameFrame(w.Current(paint.id).layerSymmetry,Frame(0));
            SameFrame(w.Current(projection.id).layerSymmetry,Frame(2));
        }

        [Test]
        public void NewPaintLayerStartsDisabledEvenAfterAnEnabledLayerWasSelected()
        {
            using var w=new Workspace();var first=w.Add(TexturePaintLayerKind.Paint);
            w.Select(first);w.Edit(Frame(0));w.brush.mirrorStroke=true;
            Set(w.stage,"authoringSymmetry",Frame(0));Set(w.stage,"mirrorX",true);
            var fresh=w.Add(TexturePaintLayerKind.Paint);w.Select(fresh);
            Assert.That(fresh.layerSymmetryVersion,Is.EqualTo(1));
            Assert.That(w.Resolve(fresh).enabled,Is.False);
            Assert.That(w.stage.SceneToolbarMirrorX,Is.False);
            Assert.That(w.Resolve(fresh).Transforms().Count,Is.EqualTo(1));
            SameFrame(first.layerSymmetry,Frame(0));
        }

        [TestCase(TexturePaintLayerKind.Paint)]
        [TestCase(TexturePaintLayerKind.Spline)]
        [TestCase(TexturePaintLayerKind.Projection)]
        public void MasterOffOverridesOldAxesAndBrushSettingsOnTheSelectedLayer(TexturePaintLayerKind kind)
        {
            using var w=new Workspace();var layer=w.Add(kind);
            layer.paintSettings=new TexturePaintLayerSettings{symmetry=Frame(0),mirrorX=true,brushMirrorStroke=true};
            if(layer.splineSettings!=null)
            {
                layer.splineSettings.symmetry=Frame(0);layer.splineSettings.localSymmetry=Frame(1);
                layer.splineSettings.mirrorX=layer.splineSettings.brushMirrorStroke=true;
                layer.splineSettings.radialSymmetry=8;
            }
            layer.layerSymmetry=Frame(0,false);layer.layerSymmetryVersion=1;
            Set(w.stage,"authoringSymmetry",Frame(0));Set(w.stage,"mirrorX",true);w.brush.mirrorStroke=true;
            w.Select(layer);
            Assert.That(w.stage.SceneToolbarMirrorX,Is.False);
            Assert.That(w.Resolve(layer).Transforms(kind==TexturePaintLayerKind.Spline).Count,Is.EqualTo(1));
            Assert.That(layer.layerSymmetry.mirrorX,Is.True,"Master-off preserves authored axes for re-enabling");
            w.stage.SceneToolbarMirrorX=true;
            Assert.That(layer.layerSymmetry.enabled,Is.True,"Turning on the X toolbar button enables this layer");
            Assert.That(w.stage.SceneToolbarMirrorX,Is.True);
        }

        [TestCase(TexturePaintLayerKind.Paint)]
        [TestCase(TexturePaintLayerKind.Spline)]
        [TestCase(TexturePaintLayerKind.Projection)]
        public void UndoRedoAfterSwitchingLayersRestoresOnlyTheEditedFrame(TexturePaintLayerKind kind)
        {
            using var w=new Workspace();var edited=w.Add(kind);var other=w.Add(TexturePaintLayerKind.Paint);
            other.layerSymmetry=Frame(2);
            w.Select(edited);var before=w.Resolve(edited).Clone();w.Edit(Frame(0));
            w.Select(other);
            Assert.That(Invoke(w.stage,"UndoLightweight"),Is.True);
            SameFrame(w.Current(edited.id).layerSymmetry,before);
            SameFrame(w.Current(other.id).layerSymmetry,Frame(2));
            w.Select(other);
            Assert.That(Invoke(w.stage,"RedoLightweight"),Is.True);
            SameFrame(w.Current(edited.id).layerSymmetry,Frame(0));
            SameFrame(w.Current(other.id).layerSymmetry,Frame(2));
        }

        private static IEnumerable<TexturePaintLayerKind> EveryLayerKind()
        {foreach(TexturePaintLayerKind kind in Enum.GetValues(typeof(TexturePaintLayerKind)))yield return kind;}

        [TestCaseSource(nameof(EveryLayerKind))]
        public void CloneAndDocumentRoundTripPreserveAnIndependentFrameForEveryLayerKind(TexturePaintLayerKind kind)
        {
            using var w=new Workspace();var source=w.Add(kind);source.layerSymmetry=Frame(2,false);
            source.layerSymmetryVersion=1;
            var clone=w.set.CloneLayer(source);var destination=new TextureSet();
            try
            {
                SameFrame(clone.layerSymmetry,source.layerSymmetry);
                Assert.That(clone.layerSymmetry,Is.Not.SameAs(source.layerSymmetry));
                clone.layerSymmetry.origin=Vector3.one;
                SameFrame(source.layerSymmetry,Frame(2,false));
                var metadata=(TexturePaintDocumentLayer)typeof(TexturePaintDocumentStorage)
                    .GetMethod("CaptureLayerMetadata",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{source});
                var saved=JsonUtility.FromJson<TexturePaintDocumentLayer>(JsonUtility.ToJson(metadata));
                typeof(TexturePaintDocumentStorage).GetMethod("RestoreLayer",BindingFlags.Static|BindingFlags.NonPublic)
                    .Invoke(null,new object[]{saved,destination});
                var restored=destination.layers[0];
                Assert.That(restored.kind,Is.EqualTo(kind));
                Assert.That(restored.layerSymmetryVersion,Is.EqualTo(1));
                SameFrame(restored.layerSymmetry,source.layerSymmetry);
                Assert.That(restored.layerSymmetry,Is.Not.SameAs(saved.layerSymmetry));
                restored.layerSymmetry.enabled=true;SameFrame(source.layerSymmetry,Frame(2,false));
            }
            finally{clone.Dispose();destination.Dispose();}
        }

        [TestCase(true)]
        [TestCase(false)]
        public void OldPathFramesMigrateWithSharedThenLocalPriorityExactlyOnce(bool sharedEnabled)
        {
            using var w=new Workspace();var layer=w.Add(TexturePaintLayerKind.Spline);layer.layerSymmetryVersion=0;
            layer.splineSettings.symmetry=Frame(0,sharedEnabled);layer.splineSettings.localSymmetry=Frame(1);
            layer.splineSettings.mirrorX=layer.splineSettings.brushMirrorStroke=true;
            layer.splineSettings.radialSymmetry=8;
            var expected=sharedEnabled?Frame(0):Frame(1);SameFrame(w.Resolve(layer),expected);
            Assert.That(layer.layerSymmetryVersion,Is.EqualTo(1));
            layer.splineSettings.symmetry=Frame(2);layer.splineSettings.localSymmetry=Frame(2);
            SameFrame(w.Resolve(layer),expected);
            layer.layerSymmetry.enabled=false;
            Assert.That(w.Resolve(layer).enabled,Is.False,"Legacy enabled fields cannot reactivate a migrated master switch");
        }

        [TestCase(TexturePaintLayerKind.Paint,false)]
        [TestCase(TexturePaintLayerKind.Paint,true)]
        [TestCase(TexturePaintLayerKind.Spline,false)]
        [TestCase(TexturePaintLayerKind.Spline,true)]
        public void OldMirrorAndBrushFlagsMigrateOnlyForUnversionedLayers(TexturePaintLayerKind kind,bool brushMirror)
        {
            using var w=new Workspace();var layer=w.Add(kind);layer.layerSymmetryVersion=0;
            if(kind==TexturePaintLayerKind.Spline)
            {
                layer.splineSettings.symmetry=new TexturePaintSymmetry{enabled=false};
                layer.splineSettings.localSymmetry=new TexturePaintSymmetry{enabled=false};
                layer.splineSettings.mirrorX=!brushMirror;layer.splineSettings.brushMirrorStroke=brushMirror;
            }
            else layer.paintSettings=new TexturePaintLayerSettings{symmetry=new TexturePaintSymmetry{enabled=false},
                mirrorX=!brushMirror,brushMirrorStroke=brushMirror};
            var migrated=w.Resolve(layer);Assert.That(migrated.enabled,Is.True);Assert.That(migrated.mirrorX,Is.True);
            layer.layerSymmetry.enabled=false;Assert.That(w.Resolve(layer).enabled,Is.False);
        }

        [Test]
        public void OldPaintFrameAndPathRadialCopiesMigrateWithoutBorrowingAnotherLayersState()
        {
            using var w=new Workspace();var paint=w.Add(TexturePaintLayerKind.Paint);paint.layerSymmetryVersion=0;
            paint.paintSettings=new TexturePaintLayerSettings{symmetry=Frame(2)};
            SameFrame(w.Resolve(paint),Frame(2));
            var path=w.Add(TexturePaintLayerKind.Spline);path.layerSymmetryVersion=0;
            path.splineSettings.symmetry=new TexturePaintSymmetry{enabled=false};
            path.splineSettings.mirrorX=path.splineSettings.brushMirrorStroke=false;
            path.splineSettings.radialSymmetry=4;path.splineSettings.symmetryAxis=Vector3.forward;
            var radial=w.Resolve(path);Assert.That(radial.enabled,Is.True);Assert.That(radial.mirrorX,Is.False);
            Assert.That(radial.radialCopies,Is.EqualTo(4));Assert.That(radial.radialAxis,Is.EqualTo(Vector3.forward));
            Assert.That(radial.Transforms(true),Has.Count.EqualTo(4));SameFrame(paint.layerSymmetry,Frame(2));
        }

        [Test]
        public void DocumentsWithoutTheNewFrameRemainMarkedForMigration()
        {
            using var w=new Workspace();
            var legacySettings=new TexturePaintSplineSettings{symmetry=Frame(1),autoUpdate=false,
                editorSettingsVersion=1};
            // Older documents omit both new fields. Unity may construct the missing nested
            // object while deserializing, so the version determines whether migration runs.
            var old=JsonUtility.FromJson<TexturePaintDocumentLayer>(
                "{\"name\":\"Old path\",\"kind\":2,\"spline\":{\"worldSpace\":false},\"splineSettings\":"+
                JsonUtility.ToJson(legacySettings)+",\"channels\":[],\"strokes\":[]}");
            Assert.That(old.layerSymmetryVersion,Is.Zero);
            typeof(TexturePaintDocumentStorage).GetMethod("RestoreLayer",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{old,w.set});
            var restored=w.set.layers[0];
            Assert.That(restored.layerSymmetryVersion,Is.Zero);
            SameFrame(w.Resolve(restored),Frame(1));
            Assert.That(restored.layerSymmetryVersion,Is.EqualTo(1));
            Assert.That(restored.layerSymmetry.mirrorX,Is.False);
            Assert.That(restored.layerSymmetry.mirrorY,Is.True);
            restored.layerSymmetry.enabled=false;
            Assert.That(w.Resolve(restored).enabled,Is.False,"The migrated frame remains authoritative");
        }
    }
}
#endif
