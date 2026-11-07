#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintPathGeneratorEditorTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object Invoke(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Private).Invoke(target,args);
        private static void Set(object target,string field,object value)=>target.GetType().GetField(field,Private).SetValue(target,value);
        private static T Get<T>(object target,string field)=>(T)target.GetType().GetField(field,Private).GetValue(target);

        [Test]
        public void PathMenuPlacesEveryGeneratorAfterGarmentsWithoutLegacyDuplicates()
        {
            var stage=ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            try
            {
                var menu=new GenericMenu();Invoke(stage,"AddGarmentMenu",menu,null,true);Invoke(stage,"AddPathGeneratorMenu",menu,null);
                var items=(IList)typeof(GenericMenu).GetFields(Private).First(x=>typeof(IList).IsAssignableFrom(x.FieldType)).GetValue(menu);
                var names=items.Cast<object>().Select(x=>((GUIContent)x.GetType().GetField("content",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(x)).text).ToArray();
                int firstGenerator=Array.FindIndex(names,x=>x.StartsWith("Generators/",StringComparison.Ordinal));
                Assert.That(firstGenerator,Is.GreaterThan(0));
                Assert.That(names.Take(firstGenerator).All(x=>x.StartsWith("Garments/",StringComparison.Ordinal)),Is.True);
                Assert.That(names.Skip(firstGenerator).All(x=>x.StartsWith("Generators/",StringComparison.Ordinal)),Is.True);
                foreach(var generator in TexturePaintPathGenerators.All)Assert.That(names.Count(x=>x=="Generators/"+generator.MenuPath),Is.EqualTo(1));
                Assert.That(names.Any(x=>x.StartsWith("Garments/")&&(x.EndsWith("/Metal Zipper")||x.EndsWith("/Coil Zipper")||x.Contains("Hems & Seams"))),Is.False);
            }
            finally { Object.DestroyImmediate(stage); }
        }

        [TestCase(false,false)] [TestCase(true,false)] [TestCase(false,true)] [TestCase(true,true)]
        public void PathGeneratorEditsRebuildAndUndoRestoresPixelsAndSettings(bool worldSpace,bool tribal)
        {
            using var f=new TexturePaintGpuTestFixture(Color.clear);
            using var engine=new PaintingEngine(TexturePaintGpuTestFixture.LoadShader("StrokeRasterize.compute"),
                TexturePaintGpuTestFixture.LoadShader("Blur.compute"),TexturePaintGpuTestFixture.LoadShader("NormalTouchup.compute"),
                AssetDatabase.LoadAssetAtPath<Shader>(TexturePaintAssets.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader")));
            var store=new TextureStore();var sets=(List<TextureSet>)typeof(TextureStore).GetField("sets",Private).GetValue(store);sets.Add(f.set);
            var reconstruction=new MeshReconstructionResult { root=f.owner };reconstruction.surfaces.Add(f.set.surface);
            reconstruction.logicalTargets.Rebuild(reconstruction.surfaces);var logical=new TexturePaintLogicalLayerController(reconstruction.logicalTargets);
            var target=reconstruction.logicalTargets.Targets[0];target.members[0].textureSets.Add(f.set);
            f.set.surface.collider=f.owner.AddComponent<MeshCollider>();f.set.surface.collider.sharedMesh=f.mesh;
            var controller=new TexturePaintStageController();
            typeof(TexturePaintStageController).GetProperty("Textures").SetValue(controller,store);
            typeof(TexturePaintStageController).GetProperty("Painting").SetValue(controller,engine);
            typeof(TexturePaintStageController).GetProperty("Reconstruction").SetValue(controller,reconstruction);
            typeof(TexturePaintStageController).GetProperty("LogicalLayers").SetValue(controller,logical);
            var stage=ScriptableObject.CreateInstance<TexturePaintStageWindow>();var brush=f.CreateBrush();
            try
            {
                Set(stage,"controller",controller);Set(stage,"transientBrush",brush);
                Invoke(stage,"CreatePathGeneratorLayer",f.set,tribal?TexturePaintPathGenerators.TattooId+".scroll":TexturePaintPathGenerators.TextId,null);
                var layer=f.set.layers[f.set.activeLayerIndex];layer.spline.worldSpace=worldSpace;layer.spline.useBezier=false;
                layer.spline.AddPoint(new Vector3(.5f,.1f,0),new Vector2(.5f,.1f),0,0,Vector3.forward);
                layer.spline.AddPoint(new Vector3(.5f,.9f,0),new Vector2(.5f,.9f),0,1,Vector3.forward);
                var update=typeof(TexturePaintStageWindow).GetMethod("UpdateSplineAnchorFromUV",BindingFlags.Static|BindingFlags.NonPublic);
                update.Invoke(null,new object[]{f.set,layer.spline,0});update.Invoke(null,new object[]{f.set,layer.spline,1});
                brush.size=.35f;Set(stage,"pathAutoUpdate",false);
                var s=Get<TexturePaintPathGeneratorSettings>(stage,"pathGenerator");s.text="UMA";s.preserveTextAspect=false;s.tribalArmCount=2;
                Invoke(stage,"CaptureSplineSettings",layer);Invoke(stage,"ApplySpline");
                var original=TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front);
                Assert.That(original.Max(c=>c.a),Is.GreaterThan(.1f),Get<string>(stage,"workspaceStatus"));
                Invoke(stage,"BeginLightweightPathUndo",f.set,"Edit tattoo generator");
                if(tribal)s.tribalArmCount=8;else s.text="INK";
                Set(stage,"pathGenerator",s);Invoke(stage,"CompleteLightweightPathEdit",f.set,false);Invoke(stage,"ApplySpline");
                Assert.That(tribal?layer.splineSettings.pathGenerator.tribalArmCount.ToString():layer.splineSettings.pathGenerator.text,Is.EqualTo(tribal?"8":"INK"));
                var changed=TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front);
                Assert.That(original.Zip(changed,(a,b)=>Mathf.Abs(a.a-b.a)).Average(),Is.GreaterThan(.02f),Get<string>(stage,"workspaceStatus"));
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Invoke(stage,"ApplySpline");
                Assert.That(tribal?layer.splineSettings.pathGenerator.tribalArmCount.ToString():layer.splineSettings.pathGenerator.text,Is.EqualTo(tribal?"2":"UMA"));
                var undone=TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front);
                Assert.That(original.Zip(undone,(a,b)=>Mathf.Abs(a.a-b.a)).Average(),Is.LessThan(.0001f));
                Assert.That(Invoke(stage,"RedoLightweight"),Is.True);Invoke(stage,"ApplySpline");
                Assert.That(tribal?layer.splineSettings.pathGenerator.tribalArmCount.ToString():layer.splineSettings.pathGenerator.text,Is.EqualTo(tribal?"8":"INK"));
            }
            finally
            {
                Invoke(stage,"CancelScheduledSplineReapply");Invoke(stage,"ClearLightweightHistory");Set(stage,"controller",null);
                Object.DestroyImmediate(stage);Object.DestroyImmediate(brush);sets.Clear();store.Dispose();
            }
        }
    }
}
#endif
