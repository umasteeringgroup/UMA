#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class EyebrowPathGeneratorTests
    {
        private static EyebrowSettings Small()=>new() {count=30,secondary=new(0),baby=new(0)};
        private static TexturePaintPathGeneratorContext Context(int size=256)=>new() {size=new Vector2(.01f,.05f),resolution=size,footprintPixels=128};
        private static TexturePaintPathGeneratorSettings Settings(EyebrowSettings s)=>new() {enabled=true,generatorId=EyebrowPathGenerator.GeneratorId,extensionJson=s.Save()};
        private static Vector2[] Geometry(EyebrowPathGenerator.Groom g)=>g.strands.SelectMany(h=>h.points).ToArray();

        [Test] public void OppositeWorldBrowsKeepUpperGrowthAboveThePath()
        {
            StrokeSample[] Samples(float direction)=>new[]{
                new StrokeSample(Vector3.zero,Vector3.forward,Vector2.zero,0,0),
                new StrokeSample(Vector3.right*direction*.05f,Vector3.forward,Vector2.right,0,0)};
            var left=TexturePaintPathGeneratorContext.FromSamples(Samples(1),.01f,256,false,false);
            var right=TexturePaintPathGeneratorContext.FromSamples(Samples(-1),.01f,256,false,false);
            Assert.That(left.acrossPointsDown,Is.False);Assert.That(right.acrossPointsDown,Is.True);
            var settings=Settings(Small());string saved=settings.extensionJson;
            using var a=(EyebrowPathGenerator.Prepared)new EyebrowPathGenerator().Prepare(settings,left);
            using var b=(EyebrowPathGenerator.Prepared)new EyebrowPathGenerator().Prepare(settings,right);
            var ga=Geometry(a.groom);var gb=Geometry(b.groom);
            Assert.That(gb.Length,Is.EqualTo(ga.Length));
            for(int i=0;i<ga.Length;i++)
            {
                Assert.That(gb[i].x,Is.EqualTo(ga[i].x).Within(1e-6f));
                Assert.That(gb[i].y,Is.EqualTo(-ga[i].y).Within(1e-6f),"Opposite across axes must give the same world-up growth.");
            }
            Assert.That(settings.extensionJson,Is.EqualTo(saved),"Context correction must not modify the artist's settings.");
            var uv=TexturePaintPathGeneratorContext.FromSamples(Samples(-1),.01f,256,false,true);
            Assert.That(uv.acrossPointsDown,Is.False,"UV projects retain their authored Flip Across.");
            var manual=Small();manual.flipAcross=true;
            using var overridden=(EyebrowPathGenerator.Prepared)new EyebrowPathGenerator().Prepare(Settings(manual),right);
            CollectionAssert.AreEqual(ga,Geometry(overridden.groom));
        }
        [Test] public void ParallelRasterRetainsDeterministicFragmentOrderingAndCancellation()
        {
            var s=Small();var groom=EyebrowPathGenerator.GenerateGroom(s,5);
            var a=EyebrowPathGenerator.Rasterize(groom,s,128,64);
            var b=EyebrowPathGenerator.Rasterize(groom,s,128,64);
            CollectionAssert.AreEqual(a.coverage,b.coverage);
            CollectionAssert.AreEqual(a.strand,b.strand);
            CollectionAssert.AreEqual(a.hits,b.hits);
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(()=>EyebrowPathGenerator.Rasterize(groom,s,128,64,
                new TexturePaintPathGeneratorContext{cancellation=cancellation.Token}));
        }
        [Test] public void ProceduralPathEditsDebounceForTwoSecondsAndExplicitUpdateIsImmediate()
        {
            var stage=ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var type=typeof(TexturePaintStageWindow);
            try
            {
                var layer=new TexturePaintLayer{kind=TexturePaintLayerKind.Spline,
                    splineSettings=new TexturePaintSplineSettings{pathGenerator=Settings(Small())}};
                type.GetField("pendingSplineLayer",flags).SetValue(stage,layer);
                type.GetField("splineReapplyPending",flags).SetValue(stage,true);
                var schedule=type.GetMethod("ScheduleSplineReapply",flags);
                double start=EditorApplication.timeSinceStartup;
                schedule.Invoke(stage,new object[]{true});
                double due=(double)type.GetField("splineReapplyNotBefore",flags).GetValue(stage);
                Assert.That(due-start,Is.InRange(1.99,2.1));
                schedule.Invoke(stage,new object[]{true});
                Assert.That((double)type.GetField("splineReapplyNotBefore",flags).GetValue(stage),Is.GreaterThanOrEqualTo(due));
                schedule.Invoke(stage,new object[]{false});
                Assert.That((double)type.GetField("splineReapplyNotBefore",flags).GetValue(stage),Is.LessThanOrEqualTo(EditorApplication.timeSinceStartup));
            }
            finally
            {
                type.GetMethod("CancelScheduledSplineReapply",flags).Invoke(stage,null);
                UnityEngine.Object.DestroyImmediate(stage);
            }
        }

        [Test] public void RegistrationAndDefaultChannelsAreAvailable()
        {EyebrowPathGenerator.Register();Assert.That(TexturePaintPathGenerators.Find(EyebrowPathGenerator.GeneratorId).MenuPath,Is.EqualTo("Hair/Eyebrows"));}
        [Test] public void UvLayoutFingerprintSurvivesReconstructionOrderingButRejectsMovedUvs()
        {
            var a=new Mesh {vertices=new[]{Vector3.zero,Vector3.right,Vector3.up},uv=new[]{Vector2.zero,Vector2.right,Vector2.up},triangles=new[]{0,1,2}};
            var b=new Mesh {vertices=new[]{Vector3.up,Vector3.zero,Vector3.right},uv=new[]{Vector2.up,Vector2.zero,Vector2.right},triangles=new[]{1,2,0}};
            try
            {
                var before=TexturePaintSurfaceFingerprintUtility.Compute(a,true);
                var reordered=TexturePaintSurfaceFingerprintUtility.Compute(b,true);
                Assert.That(before.uv,Is.Not.EqualTo(reordered.uv));
                Assert.That(before.uvLayout,Is.EqualTo(reordered.uvLayout));
                b.uv=new[]{Vector2.up*.9f,Vector2.zero,Vector2.right};
                Assert.That(TexturePaintSurfaceFingerprintUtility.Compute(b,true).uvLayout,Is.Not.EqualTo(before.uvLayout));
                Assert.That(TexturePaintSurfaceFingerprintUtility.Compute(a).uvLayout,Is.Empty,"Ordinary geometry checks must not sort the UV layout.");
            }
            finally{UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);}
        }
        [Test] public void MinifiedMaterialMapsRetainSharedCoverageAndStraightColor()
        {
            var s=Small();s.color=new Color(.4f,.2f,.1f,1);s.tipColor=s.color;s.grayFraction.value=0;
            s.colorVariation.value=0;s.layerDepthContrast.value=0;
            using var p=(EyebrowPathGenerator.Prepared)new EyebrowPathGenerator().Prepare(Settings(s),Context());
            var albedo=p.raster.Coverage;
            Assert.That(albedo.mipmapCount,Is.GreaterThan(1));
            for(int mip=1;mip<albedo.mipmapCount;mip++)
            {
                var pixels=albedo.GetPixels(mip);
                foreach(var texture in p.raster.Channels.Values)
                {
                    var data=texture.GetPixels(mip);
                    for(int i=0;i<pixels.Length;i++)Assert.That(data[i].a,Is.EqualTo(pixels[i].a).Within(1f/255));
                }
                foreach(var color in pixels.Where(c=>c.a>.01f))
                    Assert.That(color.r,Is.EqualTo(s.color.r).Within(.012f),"Transparent background must not darken minified hair.");
            }
        }
        [Test] public void ReorderedEquippedSlotRestoresPathPixelsWithoutOrphaningTheDocument()
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.clear);
            using var store=new TextureStore();
            var sets=(System.Collections.Generic.List<TextureSet>)store.Sets;sets.Add(fixture.set);
            var document=TexturePaintDocumentStorage.CreateTransient(null);
            try
            {
                var layer=fixture.set.AddSplineLayer("Shell eyebrow");layer.spline.worldSpace=false;
                layer.spline.AddPoint(Vector3.zero,Vector2.zero,0,-1,Vector3.forward);
                layer.spline.AddPoint(Vector3.right,Vector2.right,0,-1,Vector3.forward);
                layer.splineSettings.pathGenerator=Settings(Small());
                fixture.set.GetPaintTarget(TexturePaintChannel.Albedo,TexturePaintSourceMode.SourceOverlay).Reset(null,new Color(.2f,.1f,.05f,.75f));
                TexturePaintDocumentStorage.Save(document,store);
                var vertices=fixture.mesh.vertices;var uv=fixture.mesh.uv;
                fixture.mesh.vertices=new[]{vertices[2],vertices[3],vertices[0],vertices[1]};
                fixture.mesh.uv=new[]{uv[2],uv[3],uv[0],uv[1]};
                fixture.mesh.triangles=new[]{2,3,0,2,0,1};
                fixture.set.surface.sourceSubmeshIndex=7;
                fixture.set.persistentId="equipped-shell";
                fixture.set.RemoveLayerAt(0);
                var report=TexturePaintDocumentStorage.Restore(document,store);
                Assert.That(report.restoredLayers,Is.EqualTo(1));
                Assert.That(fixture.set.layers[0].splineSettings.pathGenerator.generatorId,Is.EqualTo(EyebrowPathGenerator.GeneratorId));
                Assert.That(TexturePaintGpuTestFixture.ReadPixels(fixture.set.layers[0].channels[TexturePaintChannel.Albedo].Front)[0].a,Is.EqualTo(.75f).Within(.002f));
                TexturePaintDocumentStorage.Save(document,store);
                Assert.That(document.surfaces.Count,Is.EqualTo(1));
                Assert.That(document.surfaces[0].orphaned,Is.False);
            }
            finally{sets.Clear();UnityEngine.Object.DestroyImmediate(document);}
        }
        [Test] public void UvPathSceneCurveExistsWithoutSelectingAPointAndTracksEdits()
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.clear);
            var stage=ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            try
            {
                fixture.owner.transform.position=new Vector3(3,4,5);
                var layer=fixture.set.AddSplineLayer("UV eyebrow");
                var spline=layer.spline;spline.worldSpace=false;
                spline.AddPoint(Vector3.zero,new Vector2(.2f,.3f),0,-1,Vector3.forward);
                spline.AddPoint(Vector3.zero,new Vector2(.5f,.6f),0,-1,Vector3.forward);
                spline.AddPoint(Vector3.zero,new Vector2(.8f,.3f),0,-1,Vector3.forward);
                spline.EnsureControlPoints();
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                typeof(TexturePaintStageWindow).GetField("selectedSplinePoint",flags).SetValue(stage,-1);
                var build=typeof(TexturePaintStageWindow).GetMethod("BuildSurfaceHuggingCurve",flags);
                var args=new object[]{new[]{fixture.set},fixture.set.surface,spline,24};
                var before=(Vector3[])build.Invoke(stage,args);
                Assert.That(before.Length,Is.GreaterThan(3));
                Assert.That(Vector3.Distance(before[0],new Vector3(3.2f,4.3f,5)),Is.LessThan(.002f));
                Assert.That(Vector3.Distance(before[before.Length-1],new Vector3(3.8f,4.3f,5)),Is.LessThan(.002f));
                Assert.That(TexturePaintStageWindow.MoveTwoDimensionalSplinePoint(fixture.set,spline,1,
                    new Vector2(.5f,.8f),Vector3.forward,0,Vector3.zero),Is.True);
                var after=(Vector3[])build.Invoke(stage,args);
                Assert.That(after[24].y-before[24].y,Is.EqualTo(.2f).Within(.002f));
                Assert.That(spline.worldSpace,Is.False);
                Assert.That(spline.uvPoints[1],Is.EqualTo(new Vector2(.5f,.8f)));
            }
            finally{UnityEngine.Object.DestroyImmediate(stage);}
        }

        [Test] public void RegenerationIsDeterministicAndDoesNotTouchUnityRandom()
        {
            var random=UnityEngine.Random.state;var s=Small();
            var a=EyebrowPathGenerator.GenerateGroom(s,5);var b=EyebrowPathGenerator.GenerateGroom(s,5);
            Assert.That(Geometry(a),Is.EqualTo(Geometry(b)));Assert.That(JsonUtility.ToJson(UnityEngine.Random.state),Is.EqualTo(JsonUtility.ToJson(random)));
        }
        [Test] public void AppearanceStreamsDoNotMoveRootsOrChangeGeometry()
        {
            var s=Small();var before=Geometry(EyebrowPathGenerator.GenerateGroom(s,5));
            s.color=Color.red;s.colorVariation.value=.8f;s.grayFraction.value=.8f;s.roughness.value=.1f;
            Assert.That(Geometry(EyebrowPathGenerator.GenerateGroom(s,5)),Is.EqualTo(before));
            Assert.That(EyebrowPathGenerator.Random(3,4,1),Is.Not.EqualTo(EyebrowPathGenerator.Random(3,4,2)));
        }
        [Test] public void LayerDepthIsOrderedAndContrastDoesNotRearrangeHair()
        {
            var s=Small();var gen=new EyebrowPathGenerator();
            using var a=(EyebrowPathGenerator.Prepared)gen.Prepare(Settings(s),Context());
            for(int i=1;i<a.groom.strands.Count;i++)Assert.That(a.groom.strands[i].depth,Is.GreaterThanOrEqualTo(a.groom.strands[i-1].depth));
            var before=a.raster.Coverage.GetPixels();s.layerDepthContrast.value=.7f;
            using var b=(EyebrowPathGenerator.Prepared)gen.Prepare(Settings(s),Context());
            Assert.That(Geometry(a.groom),Is.EqualTo(Geometry(b.groom)));Assert.That(b.raster.Coverage.GetPixels(),Is.Not.EqualTo(before));
            Assert.That(b.raster.Coverage.GetPixels().Select(x=>x.a),Is.EqualTo(before.Select(x=>x.a)));
        }
        [Test] public void MirroredVariantsUseBoundedAsymmetryAndSharedMaterialAtlas()
        {
            var s=Small();s.symmetry=BrowSymmetryMode.LinkedShapeIndependentHairs;s.asymmetry.value=0;
            var primary=s.Clone();primary.symmetry=BrowSymmetryMode.ExactMirror;
            Assert.That(Geometry(EyebrowPathGenerator.GenerateGroom(s,5)),Is.EqualTo(Geometry(EyebrowPathGenerator.GenerateGroom(primary,5))));
            s.asymmetry.value=.5f;var gen=new EyebrowPathGenerator();
            using var p=(EyebrowPathGenerator.Prepared)gen.Prepare(Settings(s),Context());Assert.That(p.variants,Is.EqualTo(2));
            var texture=p.raster.Coverage;Assert.That(texture.GetPixels(0,0,texture.width/2,texture.height),Is.Not.EqualTo(texture.GetPixels(texture.width/2,0,texture.width/2,texture.height)));
            var props=new MaterialPropertyBlock();gen.Bind(props,Settings(s),p,TexturePaintChannel.Albedo,Color.white,Vector2.one);
            Assert.That(props.GetFloat("_PathRasterVariants"),Is.EqualTo(2));
            foreach(var other in p.raster.Channels.Values)Assert.That(other.GetPixels().Select(x=>x.a),Is.EqualTo(texture.GetPixels().Select(x=>x.a)));
        }
        [Test] public void HigherCountRetainsExistingStrandIdsAndRoots()
        {
            var s=Small();var before=EyebrowPathGenerator.GenerateGroom(s,5);s.count=70;
            var after=EyebrowPathGenerator.GenerateGroom(s,5);
            foreach(var strand in before.strands)Assert.That(after.strands.Single(h=>h.id==strand.id).points,Is.EqualTo(strand.points));
        }
        [Test] public void CurvesRespectEndpointsMidpointAndBothInfluenceDirections()
        {
            var p=BrowParameter.Profile(2,1,3,4);p.midpoint=.25f;
            Assert.That(p.Evaluate(0),Is.EqualTo(2));Assert.That(p.Evaluate(.25f),Is.EqualTo(6));Assert.That(p.Evaluate(1),Is.EqualTo(8));
            p.mode=BrowCurveMode.FromStart;Assert.That(p.Evaluate(0),Is.EqualTo(2));Assert.That(p.Evaluate(1),Is.EqualTo(8));
            p.mode=BrowCurveMode.FromEnd;Assert.That(p.Evaluate(0),Is.EqualTo(2));Assert.That(p.Evaluate(1),Is.EqualTo(8));
            p.mode=BrowCurveMode.Region;p.start=0;p.middle=1;Assert.That(p.Evaluate(0),Is.Zero);Assert.That(p.Evaluate(.5f),Is.EqualTo(2));Assert.That(p.Evaluate(1),Is.Zero);
        }
        [Test] public void SaveCloneAndMigrationPreserveWeightedCurvesAndUnavailableGenerator()
        {
            var s=Small();var key=new Keyframe(.4f,.7f,2,-3,.2f,.6f){weightedMode=WeightedMode.Both};
            s.length.mode=BrowCurveMode.Curve;s.length.curve=new AnimationCurve(new Keyframe(0,1),key,new Keyframe(1,.2f)) {preWrapMode=WrapMode.PingPong,postWrapMode=WrapMode.Loop};
            var saved=Settings(s);var cloned=saved.Clone();TexturePaintPathGenerators.Unregister(EyebrowPathGenerator.GeneratorId);
            try
            {
                cloned.Normalize();Assert.That(cloned.extensionJson,Is.EqualTo(saved.extensionJson));
                var loaded=EyebrowSettings.Load(cloned.extensionJson);Assert.That(loaded.length.curve.keys[1].outWeight,Is.EqualTo(.6f));
                Assert.That(loaded.length.curve.postWrapMode,Is.EqualTo(WrapMode.Loop));Assert.That(loaded.length.curve.keys[1].weightedMode,Is.EqualTo(WeightedMode.Both));
                loaded.version=0;Assert.That(EyebrowSettings.Load(loaded.Save()).version,Is.EqualTo(EyebrowSettings.CurrentVersion));
                loaded.version=99;Assert.Throws<InvalidOperationException>(()=>EyebrowSettings.Load(loaded.Save()));
            }
            finally{EyebrowPathGenerator.Register();}
        }
        public sealed class HistoryData:ScriptableObject {public TexturePaintPathGeneratorSettings settings;}
        [Test] public void UndoRedoRestoresEditableSettings()
        {
            var data=ScriptableObject.CreateInstance<HistoryData>();data.settings=Settings(Small());string old=data.settings.extensionJson;
            try
            {
                Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(data,"Eyebrow test");
                var s=Small();s.count=74;data.settings.extensionJson=s.Save();Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();Assert.That(data.settings.extensionJson,Is.EqualTo(old));Undo.PerformRedo();Assert.That(EyebrowSettings.Load(data.settings.extensionJson).count,Is.EqualTo(74));
            }
            finally{Undo.ClearUndo(data);UnityEngine.Object.DestroyImmediate(data);}
        }
        [Test] public void InPlaceCurveEditInvalidatesCachedGeometry()
        {
            var gen=new EyebrowPathGenerator();var s=Small();s.length.mode=BrowCurveMode.Curve;
            using var a=(EyebrowPathGenerator.Prepared)gen.Prepare(Settings(s),Context());var before=Geometry(a.groom);
            s.length.curve.MoveKey(0,new Keyframe(0,2));
            using var b=(EyebrowPathGenerator.Prepared)gen.Prepare(Settings(s),Context());Assert.That(Geometry(b.groom),Is.Not.EqualTo(before));
        }
        [Test] public void ReverseAndAcrossFlipKeepAnatomicalPopulationAndIds()
        {
            var s=Small();var a=EyebrowPathGenerator.GenerateGroom(s,5);s.reverse=true;s.flipAcross=true;
            var b=EyebrowPathGenerator.GenerateGroom(s,5);Assert.That(b.strands.Count,Is.EqualTo(a.strands.Count));
            for(int i=0;i<a.strands.Count;i++)for(int j=0;j<a.strands[i].points.Length;j++)
            {Assert.That(b.strands[i].points[j].x,Is.EqualTo(5-a.strands[i].points[j].x).Within(1e-5f));Assert.That(b.strands[i].points[j].y,Is.EqualTo(-a.strands[i].points[j].y).Within(1e-5f));}
        }
        [Test] public void ResolutionAndPreviewQualityOnlyChangeSampling()
        {
            var gen=new EyebrowPathGenerator();var settings=Settings(Small());var c=Context(1024);
            using var a=(EyebrowPathGenerator.Prepared)gen.Prepare(settings,c);c.resolution=4096;c.footprintPixels=256;c.draft=true;
            using var b=(EyebrowPathGenerator.Prepared)gen.Prepare(settings,c);Assert.That(Geometry(a.groom),Is.EqualTo(Geometry(b.groom)));
        }
        [Test] public void EveryMaterialOutputUsesIdenticalCoverageAndNeutralBackground()
        {
            using var p=(EyebrowPathGenerator.Prepared)new EyebrowPathGenerator().Prepare(Settings(Small()),Context());
            var alpha=p.raster.Coverage.GetPixels().Select(x=>x.a).ToArray();Assert.That(alpha.Any(a=>a>0),Is.True);Assert.That(alpha.Count(a=>a==0),Is.GreaterThan(alpha.Length/2));
            foreach(var texture in p.raster.Channels.Values)Assert.That(texture.GetPixels().Select(x=>x.a).ToArray(),Is.EqualTo(alpha));
            var height=p.raster.Channels[TexturePaintChannel.NormalControl].GetPixels();
            for(int i=0;i<alpha.Length;i++)if(alpha[i]==0)Assert.That(height[i].r,Is.EqualTo(.5f).Within(.003f));
            Assert.That(p.raster.Channels.ContainsKey(TexturePaintChannel.Normal),Is.False);
        }
        [Test] public void PreparationFailureCancellationAndDisposalAreSafe()
        {
            var gen=new EyebrowPathGenerator();var c=Context();var p=(EyebrowPathGenerator.Prepared)gen.Prepare(Settings(Small()),c);var texture=p.raster.Coverage;
            c.closed=true;Assert.Throws<InvalidOperationException>(()=>gen.Prepare(Settings(Small()),c));Assert.That(p.raster.Coverage,Is.Not.Null);
            c.closed=false;c.cancellation=new CancellationToken(true);Assert.Throws<OperationCanceledException>(()=>gen.Prepare(Settings(Small()),c));
            p.Dispose();p.Dispose();Assert.That(texture==null,Is.True);Assert.Throws<ObjectDisposedException>(()=>gen.Bind(new MaterialPropertyBlock(),Settings(Small()),p,TexturePaintChannel.Albedo,Color.white,Vector2.one));
        }
        [Test] public void ZeroDensityIsTransparentAndZeroLengthIsRejected()
        {
            var s=Small();s.count=0;using var p=(EyebrowPathGenerator.Prepared)new EyebrowPathGenerator().Prepare(Settings(s),Context());
            Assert.That(p.raster.Coverage.GetPixels().All(x=>x.a==0),Is.True);
            var c=Context();c.size=Vector2.zero;Assert.Throws<InvalidOperationException>(()=>new EyebrowPathGenerator().Prepare(Settings(s),c));
        }
        [Test] public void ArcLengthContextIgnoresRedundantPathPoints()
        {
            var a=new StrokeSample(Vector3.zero,Vector3.forward,Vector2.zero,0,0);var b=new StrokeSample(Vector3.right,Vector3.forward,Vector2.right,0,0);
            var middle=new StrokeSample(Vector3.right*.3f,Vector3.forward,Vector2.right*.3f,0,0);
            var first=TexturePaintPathGeneratorContext.FromSamples(new[]{a,b},.2f,2048,false,false);
            var second=TexturePaintPathGeneratorContext.FromSamples(new[]{a,middle,middle,b},.2f,2048,false,false);
            Assert.That(first.size,Is.EqualTo(second.size));Assert.That(first.footprintPixels,Is.EqualTo(second.footprintPixels));
        }
        [Test] public void HostRasterMaintainsAlignmentAndPreservesResultOnClosedPathFailure()
        {
            using var f=new TexturePaintGpuTestFixture(Color.clear,size:256);
            foreach(var channel in new[]{TexturePaintChannel.Roughness,TexturePaintChannel.NormalControl})f.set.channels.Add(channel,new TextureChannelTarget {channel=channel,format=RenderTextureFormat.ARGBHalf,editable=new EditableTextureTarget(channel.ToString(),256,256,RenderTextureFormat.ARGBHalf,null,Color.clear)});
            var layer=f.set.AddSplineLayer("Eyebrows");var brush=f.CreateBrush(1,1);brush.size=.075f;
            try
            {
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(TexturePaintAssets.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
                using var engine=new PaintingEngine(null,null,null,shader);var context=f.CreateContext(brush,TexturePaintTool.Paint,Color.white,strength:1);
                context.pathGenerator=Settings(Small());context.pathGeneratorContext=Context();context.directUV=true;context.projectionDepth=1;
                foreach(var channel in f.set.channels.Keys)context.channelSources[channel]=new TexturePaintChannelSourceSettings {source=TexturePaintBrushSource.Color,color=Color.white};
                var samples=new[]{new StrokeSample(new Vector3(.15f,.4f,0),Vector3.forward,new Vector2(.15f,.4f),0,0),new StrokeSample(new Vector3(.85f,.6f,0),Vector3.forward,new Vector2(.85f,.6f),0,0)};
                var segments=TexturePaintStageWindow.BuildRibbonSegments(samples,.075f,.15f,fitCompleteTiles:false);
                Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay),Is.True,engine.LastStrokeError);
                Assert.That(engine.ApplyRibbon(segments,samples,false,false,false,true),Is.True);engine.EndStroke(true);
                var colors=layer.channels.ToDictionary(p=>p.Key,p=>TexturePaintGpuTestFixture.ReadPixels(p.Value.Front));
                var alpha=colors[TexturePaintChannel.Albedo].Select(p=>p.a).ToArray();Assert.That(alpha.Any(a=>a>.1f),Is.True);
                foreach(var pixels in colors.Values)for(int i=0;i<alpha.Length;i++)Assert.That(pixels[i].a,Is.EqualTo(alpha[i]).Within(.002f));
                context.pathGeneratorContext.closed=true;context.replaceLayer=layer;context.replaceHistoryGroup=true;context.historyGroupKey="eyebrow";
                Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay),Is.False);
                Assert.That(TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front),Is.EqualTo(colors[TexturePaintChannel.Albedo]));
            }
            finally{UnityEngine.Object.DestroyImmediate(brush);}
        }

        [Test] public void ReflectedRibbonAttachmentPreservesWidthHandednessAndSampleData()
        {
            var samples=new[]{new StrokeSample(new Vector3(-.2f,0,0),Vector3.forward,Vector2.zero,0,0),
                new StrokeSample(new Vector3(-.4f,0,0),Vector3.forward,Vector2.one,0,0)};
            var segments=TexturePaintStageWindow.BuildRibbonSegments(samples,.02f,.04f);
            var before=segments[0];var normal=new Vector3(.1f,0,1).normalized;
            TexturePaintStageWindow.ConformSymmetryRibbon(segments,(Vector3 p,Vector3 n,out Vector3 hit,out Vector3 hn)=>
                {hit=p+Vector3.forward*.03f;hn=normal;return true;});
            var after=segments[0];
            Assert.That(Vector3.Distance(after.leftStartAlong,after.rightStartFlow),Is.EqualTo(.04f).Within(1e-5));
            Assert.That(after.leftStartAlong.w,Is.EqualTo(before.leftStartAlong.w));
            Assert.That(after.rightStartFlow.w,Is.EqualTo(before.rightStartFlow.w));
            Assert.That(after.normalStartPressure.w,Is.EqualTo(before.normalStartPressure.w));
            Assert.That(Vector3.Dot((Vector3)after.normalStartPressure,normal),Is.GreaterThan(.999f));
            Assert.That(Vector3.Dot((Vector3)after.rightStartFlow-(Vector3)after.leftStartAlong,
                (Vector3)before.rightStartFlow-(Vector3)before.leftStartAlong),Is.GreaterThan(0));
        }

        [Test] public void ReflectedRibbonDoesNotSnapToDistantOrInvalidSurfaces()
        {
            var samples=new[]{new StrokeSample(Vector3.zero,Vector3.forward,Vector2.zero,0,0),
                new StrokeSample(Vector3.right*.2f,Vector3.forward,Vector2.one,0,0)};
            foreach(var hit in new[]{Vector3.one*100,new Vector3(float.NaN,0,0)})
            {
                var segments=TexturePaintStageWindow.BuildRibbonSegments(samples,.02f,.04f);
                var before=segments[0];
                TexturePaintStageWindow.ConformSymmetryRibbon(segments,(Vector3 p,Vector3 n,out Vector3 position,out Vector3 hn)=>
                    {position=hit;hn=n;return true;});
                Assert.That(segments[0].leftStartAlong,Is.EqualTo(before.leftStartAlong));
                Assert.That(segments[0].rightEndFlow,Is.EqualTo(before.rightEndFlow));
            }
        }

        [Test] public void MirroredEyebrowRendersOnOffsetShellWithMatchingChannelCoverage()
        {
            var mesh=new Mesh {vertices=new[]{new Vector3(.1f,-.2f,0),new Vector3(.7f,-.2f,0),new Vector3(.7f,.2f,0),new Vector3(.1f,.2f,0),
                new Vector3(-.7f,-.2f,.04f),new Vector3(-.1f,-.2f,.04f),new Vector3(-.1f,.2f,.04f),new Vector3(-.7f,.2f,.04f)},
                normals=Enumerable.Repeat(Vector3.forward,8).ToArray(),
                uv=new[]{Vector2.zero,new Vector2(.49f,0),new Vector2(.49f,1),Vector2.up,new Vector2(.51f,0),Vector2.right,Vector2.one,new Vector2(.51f,1)},
                triangles=new[]{0,1,2,0,2,3,4,5,6,4,6,7}};
            var samples=new[]{new StrokeSample(new Vector3(.2f,0,0),Vector3.forward,Vector2.zero,0,0),
                new StrokeSample(new Vector3(.6f,0,0),Vector3.forward,Vector2.one,0,0)};
            try{ValidateMirroredShell(mesh,samples,.025f,Settings(Small()),null);}
            finally{UnityEngine.Object.DestroyImmediate(mesh);}
        }

        // Also used by the main-project validation script with the actual equipped shell.
        public static string ValidateMirroredShell(Mesh mesh,StrokeSample[] samples,float halfWidth,
            TexturePaintPathGeneratorSettings generator,string outputDirectory)
        {
            using var f=new TexturePaintGpuTestFixture(Color.clear,size:512);
            f.set.surface.mesh=mesh;
            f.set.surface.collider=f.owner.AddComponent<MeshCollider>();
            f.set.surface.collider.sharedMesh=mesh;
            foreach(var channel in new[]{TexturePaintChannel.Roughness,TexturePaintChannel.NormalControl})
                f.set.channels.Add(channel,new TextureChannelTarget {channel=channel,format=RenderTextureFormat.ARGBHalf,
                    editable=new EditableTextureTarget(channel.ToString(),512,512,RenderTextureFormat.ARGBHalf,null,Color.clear)});
            var layer=f.set.AddSplineLayer("Mirror test");var brush=f.CreateBrush(1,1);brush.size=halfWidth;
            try
            {
                using var engine=new PaintingEngine(null,null,null,AssetDatabase.LoadAssetAtPath<Shader>(
                    TexturePaintAssets.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader")));
                var context=f.CreateContext(brush,TexturePaintTool.Paint,Color.white,strength:1);
                context.pathGenerator=generator;
                context.pathGeneratorContext=TexturePaintPathGeneratorContext.FromSamples(samples,halfWidth*2,512,false,false);
                context.projectionDepth=.5f;context.normalAngleLimit=90;
                foreach(var channel in f.set.channels.Keys)context.channelSources[channel]=new TexturePaintChannelSourceSettings {source=TexturePaintBrushSource.Color,color=Color.white};
                var original=TexturePaintStageWindow.BuildRibbonSegments(samples,halfWidth,halfWidth*2,fitCompleteTiles:false);
                using(var store=new TextureStore())
                {
                    var sets=(System.Collections.Generic.List<TextureSet>)store.Sets;sets.Add(f.set);
                    var stage=ScriptableObject.CreateInstance<TexturePaintStageWindow>();
                    var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                    var controller=new TexturePaintStageController();
                    typeof(TexturePaintStageController).GetProperty("Textures").SetValue(controller,store);
                    typeof(TexturePaintStageWindow).GetField("controller",flags).SetValue(stage,controller);
                    layer.layerSymmetry=new TexturePaintSymmetry {enabled=true,mirrorX=true};layer.layerSymmetryVersion=1;
                    ((System.Collections.Generic.List<TextureSet>)typeof(TexturePaintStageWindow).GetField("strokeTextureSets",flags).GetValue(stage)).Add(f.set);
                    ((System.Collections.Generic.List<string>)typeof(TexturePaintStageWindow).GetField("selectedSlots",flags).GetValue(stage)).Add("Body");
                    try
                    {
                        original=(System.Collections.Generic.List<TexturePaintRibbonSegment>)typeof(TexturePaintStageWindow)
                            .GetMethod("ExpandPathRibbon",flags).Invoke(stage,new object[]{original,false,brush,Vector3.zero});
                    }
                    finally
                    {
                        typeof(TexturePaintStageWindow).GetField("controller",flags).SetValue(stage,null);
                        UnityEngine.Object.DestroyImmediate(stage);sets.Clear();
                    }
                }
                Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay),Is.True,engine.LastStrokeError);
                Assert.That(engine.ApplyRibbon(original,samples,false,false),Is.True);engine.EndStroke(true);
                var pixels=TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front);
                double left=0,right=0;for(int i=0;i<pixels.Length;i++){if(i%512<256)left+=pixels[i].a;else right+=pixels[i].a;}
                Assert.That(left,Is.GreaterThan(10));Assert.That(right,Is.GreaterThan(10),"Reflected hairs must reach the offset shell.");
                foreach(var pair in layer.channels)
                {
                    var data=TexturePaintGpuTestFixture.ReadPixels(pair.Value.Front);
                    for(int i=0;i<data.Length;i++)Assert.That(data[i].a,Is.EqualTo(pixels[i].a).Within(.002f));
                    if(outputDirectory!=null)
                    {
                        System.IO.Directory.CreateDirectory(outputDirectory);
                        var image=new Texture2D(512,512,TextureFormat.RGBA32,false,true);image.SetPixels(data);image.Apply();
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(outputDirectory,pair.Key+".png"),image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image);
                    }
                }
                return "512x512 coverage sums: "+left+" / "+right+"; Albedo, Roughness and Normal Control alpha aligned.";
            }
            finally{f.set.surface.mesh=f.mesh;UnityEngine.Object.DestroyImmediate(brush);}
        }
    }
}
#endif
