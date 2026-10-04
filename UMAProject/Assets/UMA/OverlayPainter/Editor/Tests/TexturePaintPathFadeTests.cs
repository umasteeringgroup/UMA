#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintPathFadeTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static Shader RibbonShader => AssetDatabase.LoadAssetAtPath<Shader>(
            UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
        private static object Invoke(TexturePaintStageWindow stage, string name, params object[] args)
            => typeof(TexturePaintStageWindow).GetMethod(name, Private).Invoke(stage, args);
        private static void Field(TexturePaintStageWindow stage, string name, object value)
            => typeof(TexturePaintStageWindow).GetField(name, Private).SetValue(stage, value);
        private static float Alpha(TexturePaintLayer layer, int x, int y)
            => TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front)[y * 64 + x].a;
        private static List<StrokeSample> Line(float alpha = 1f) => new List<StrokeSample>
        {
            new StrokeSample(new Vector3(.5f,0,0),Vector3.forward,new Vector2(.5f,0),0,0) { color=new Color(1,1,1,alpha), hasColor=true },
            new StrokeSample(new Vector3(.5f,1,0),Vector3.forward,new Vector2(.5f,1),0,1) { color=new Color(1,1,1,alpha), hasColor=true }
        };

        [TestCase(false, 1f)] [TestCase(true, 1f)] [TestCase(false, .5f)] [TestCase(true, .5f)]
        public void SavedBrushSoftnessFadesSidesWithoutSeamsOrLosingSourceAlpha(bool directUV, float alpha)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear);
            var layer = fixture.set.AddLayer("Soft ribbon");
            var brush = fixture.CreateBrush(.75f, 1f, shape: BrushPreset.Shape.Square); brush.size = .4f;
            using var engine = new PaintingEngine(null, null, null, RibbonShader);
            try
            {
                var context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.white, strength:1);
                context.projectionDepth = 1; context.directUV = directUV;
                var samples = Line(alpha);
                var segments = TexturePaintStageWindow.BuildRibbonSegments(samples, brush.size, .2f);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplyRibbon(segments, samples, false, false, false, directUV), Is.True);
                Assert.That(Alpha(layer, 32, 32), Is.EqualTo(alpha).Within(.015f));
                Assert.That(Alpha(layer, 8, 32), Is.InRange(.08f * alpha, .4f * alpha));
                for (int y = 1; y < 63; y++)
                    Assert.That(Alpha(layer, 32, y), Is.EqualTo(alpha).Within(.015f), "Repeating tiles must not acquire internal fades at y=" + y);
                engine.EndStroke(true);
            }
            finally { engine.EndStroke(false); Object.DestroyImmediate(brush); }
        }

        [Test] public void EdgeFadeUsesCoverageAcrossChannelsAndOverridesBrushSoftness()
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear);
            var layer = fixture.set.AddLayer("Effect fade");
            var brush = fixture.CreateBrush(1f, 1f, shape:BrushPreset.Shape.Square); brush.size = .4f;
            using var engine = new PaintingEngine(null, null, null, RibbonShader);
            try
            {
                var context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.white, strength:1);
                context.projectionDepth = 1; context.ribbonEffects = new TexturePaintLayerEffects();
                var fade = context.ribbonEffects.edgeFade; fade.enabled = true;
                fade.channel = TexturePaintChannel.Normal; fade.edgeFadeStart = .5f;
                var samples = Line(); var segments = TexturePaintStageWindow.BuildRibbonSegments(samples, .4f, .2f);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplyRibbon(segments, samples, false, false), Is.True);
                Assert.That(Alpha(layer, 8, 32), Is.LessThan(.12f));
                Assert.That(Alpha(layer, 16, 32), Is.InRange(.65f, .95f));
                Assert.That(engine.RewindActiveStroke(), Is.True); engine.EndStroke(false);
                brush.hardness = 0f; fade.edgeFadeStart = 1f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplyRibbon(segments, samples, false, false), Is.True);
                Assert.That(Alpha(layer, 16, 32), Is.GreaterThan(.99f), "The explicit effect overrides brush softness.");
            }
            finally { engine.EndStroke(false); Object.DestroyImmediate(brush); }
        }

        [TestCase(false, false)] [TestCase(false, true)] [TestCase(true, false)]
        public void EndpointFadesFollowWholePathInsteadOfEachImageTile(bool closed, bool rotatedSource)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear);
            var layer = fixture.set.AddLayer("Endpoint fade");
            var brush = fixture.CreateBrush(1f, 1f, shape:BrushPreset.Shape.Square); brush.size = .4f;
            using var engine = new PaintingEngine(null, null, null, RibbonShader);
            try
            {
                var context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.white, strength:1);
                context.projectionDepth = 1; context.ribbonStartFade = .25f; context.ribbonEndFade = .5f;
                var samples = Line(); var segments = TexturePaintStageWindow.BuildRibbonSegments(samples, .4f, .2f);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplyRibbon(segments, samples, rotatedSource, rotatedSource, closed), Is.True);
                Assert.That(Alpha(layer,32,32), Is.GreaterThan(.99f));
                if (closed)
                { Assert.That(Alpha(layer,32,1), Is.GreaterThan(.99f)); Assert.That(Alpha(layer,32,62), Is.GreaterThan(.99f)); }
                else
                {
                    Assert.That(Alpha(layer,32,1), Is.LessThan(.04f)); Assert.That(Alpha(layer,32,62), Is.LessThan(.015f));
                    Assert.That(Alpha(layer,32,8), Is.InRange(.5f,.6f)); Assert.That(Alpha(layer,32,47), Is.InRange(.5f,.6f));
                    Assert.That(Alpha(layer,32,26), Is.GreaterThan(.99f), "Interior tile joins remain opaque.");
                }
            }
            finally { engine.EndStroke(false); Object.DestroyImmediate(brush); }
        }

        [TestCase(false, false)] [TestCase(true, false)]
        [TestCase(false, true)] [TestCase(true, true)]
        public void SideFadeBeyondOneHundredAlsoFadesCenter(bool directUV, bool effectOverride)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear);
            var layer = fixture.set.AddLayer("Extended side fade");
            var brush = fixture.CreateBrush(0f, 1f, shape: BrushPreset.Shape.Square);
            brush.size = .4f;
            using var engine = new PaintingEngine(null, null, null, RibbonShader);
            try
            {
                var context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.white, strength: 1);
                context.projectionDepth = 1;
                var samples = Line();
                var segments = TexturePaintStageWindow.BuildRibbonSegments(samples, brush.size, .2f);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplyRibbon(segments, samples, false, false, false, directUV), Is.True);
                float originalCenter = Alpha(layer, 32, 32), originalSide = Alpha(layer, 16, 32);
                Assert.That(originalCenter, Is.GreaterThan(.99f));
                Assert.That(engine.RewindActiveStroke(), Is.True); engine.EndStroke(false);
                if (effectOverride)
                {
                    context.ribbonEffects = new TexturePaintLayerEffects();
                    var fade = context.ribbonEffects.edgeFade;
                    fade.enabled = true; fade.edgeFadeStart = -1f;
                    fade.channel = TexturePaintChannel.Normal;
                    context.ribbonEffects.Normalize();
                    Assert.That(fade.edgeFadeStart, Is.EqualTo(-1f));
                }
                else context.ribbonSideFadeExtra = 1f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplyRibbon(segments, samples, false, false, false, directUV), Is.True);
                Assert.That(Alpha(layer, 32, 32), Is.InRange(.47f, .51f));
                Assert.That(Alpha(layer, 16, 32), Is.LessThan(originalSide * .5f));
            }
            finally { engine.EndStroke(false); Object.DestroyImmediate(brush); }
        }

        [TestCase(false)] [TestCase(true)]
        public void SideCurveControlsFullOpacityAndUpdatesAfterInPlaceEdits(bool effectOverride)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear);
            var layer = fixture.set.AddLayer("Curved source transparency");
            var brush = fixture.CreateBrush(0f, 1f, shape: BrushPreset.Shape.Square);
            brush.size = .4f;
            var source = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            source.SetPixel(0, 0, new Color(1f, .5f, .2f, .5f)); source.Apply();
            using var engine = new PaintingEngine(null, null, null, RibbonShader);
            try
            {
                var curve = AnimationCurve.Linear(0f, .4f, 1f, .4f);
                var context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.white, strength: 1);
                context.projectionDepth = 1;
                context.paintSource = TexturePaintBrushSource.Texture; context.sourceTexture = source;
                if (effectOverride)
                {
                    context.ribbonEffects = new TexturePaintLayerEffects();
                    context.ribbonEffects.edgeFade.enabled = true;
                    context.ribbonEffects.edgeFade.edgeFadeStart = 0;
                    context.ribbonEffects.edgeFade.curve = curve;
                }
                else context.ribbonSideFadeCurve = curve;
                var samples = Line();
                var segments = TexturePaintStageWindow.BuildRibbonSegments(samples, brush.size, .2f);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplyRibbon(segments, samples, false, false), Is.True);
                Assert.That(Alpha(layer, 32, 32), Is.EqualTo(.2f).Within(.01f));
                Assert.That(Alpha(layer, 8, 32), Is.EqualTo(.2f).Within(.01f));
                Assert.That(Alpha(layer, 32, 13), Is.EqualTo(.2f).Within(.01f), "Tile joins keep the same opacity.");
                Assert.That(engine.RewindActiveStroke(), Is.True); engine.EndStroke(false);
                curve.keys = new[] { new Keyframe(0, 0), new Keyframe(1, 0) };
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplyRibbon(segments, samples, false, false), Is.True);
                Assert.That(Alpha(layer, 32, 32), Is.LessThan(.001f), "Lowering the curve can fade the ribbon away completely.");
            }
            finally { engine.EndStroke(false); Object.DestroyImmediate(brush); Object.DestroyImmediate(source); }
        }

        [TestCase(false)] [TestCase(true)]
        public void EndpointCurvesControlOpacityBeyondFullLengthAndSkipClosedPaths(bool closed)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear);
            var layer = fixture.set.AddLayer("Endpoint curve");
            var brush = fixture.CreateBrush(1f, 1f, shape: BrushPreset.Shape.Square); brush.size = .4f;
            using var engine = new PaintingEngine(null, null, null, RibbonShader);
            try
            {
                var context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.white, strength: 1);
                context.projectionDepth = 1;
                context.ribbonStartFade = 1.5f; context.ribbonEndFade = 2f;
                context.ribbonStartFadeCurve = AnimationCurve.Linear(0, .6f, 1, 0);
                context.ribbonEndFadeCurve = AnimationCurve.Linear(0, .5f, 1, .5f);
                var samples = Line(); var segments = TexturePaintStageWindow.BuildRibbonSegments(samples, .4f, .2f);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplyRibbon(segments, samples, true, true, closed), Is.True);
                foreach (int y in new[] { 1, 16, 32, 48, 62 })
                {
                    float fraction = (y + .5f) / 64f;
                    float expected = closed ? 1f : .2f * fraction;
                    Assert.That(Alpha(layer, 32, y), Is.EqualTo(expected).Within(.005f));
                }
            }
            finally { engine.EndStroke(false); Object.DestroyImmediate(brush); }
        }

        [Test]
        public void SampleEndpointCurvesUseExtendedDistancesAndClampOpacity()
        {
            var samples = Line();
            TexturePaintStageWindow.ApplyPathEndpointFade(samples, 2f, 1.5f, false,
                AnimationCurve.Linear(0, 1, 1, 0), AnimationCurve.Linear(0, 2, 1, 2));
            Assert.That(samples[0].flowMultiplier, Is.Zero);
            Assert.That(samples[1].flowMultiplier, Is.EqualTo(.5f).Within(.001f));
        }

        [Test]
        public void FadeSettingsCloneAndSerializeWeightedCurvesWithoutSharingKeys()
        {
            var curve = new AnimationCurve(new Keyframe(0, .8f, 0, -1, .2f, .3f) { weightedMode = WeightedMode.Both },
                new Keyframe(1, .1f, -1, 0, .4f, .5f) { weightedMode = WeightedMode.Both });
            curve.preWrapMode = WrapMode.ClampForever; curve.postWrapMode = WrapMode.Loop;
            var settings = new TexturePaintSplineSettings { sideFadeExtra = 1f, startFade = 1.5f, endFade = 2f,
                sideFadeCurve = curve, startFadeCurve = curve, endFadeCurve = curve };
            var clone = settings.Clone();
            var restored = JsonUtility.FromJson<TexturePaintSplineSettings>(JsonUtility.ToJson(clone));
            foreach (var copy in new[] { clone.sideFadeCurve, clone.startFadeCurve, clone.endFadeCurve,
                restored.sideFadeCurve, restored.startFadeCurve, restored.endFadeCurve })
            {
                Assert.That(copy, Is.Not.SameAs(curve));
                Assert.That(TexturePaintFadeUtility.CurveSignature(copy), Is.EqualTo(TexturePaintFadeUtility.CurveSignature(curve)));
            }
            Assert.That(restored.sideFadeExtra, Is.EqualTo(1f));
            Assert.That(restored.startFade, Is.EqualTo(1.5f)); Assert.That(restored.endFade, Is.EqualTo(2f));
            clone.sideFadeCurve.MoveKey(0, new Keyframe(0, 0));
            Assert.That(settings.sideFadeCurve[0].value, Is.EqualTo(.8f));
            Assert.That(clone.startFadeCurve[0].value, Is.EqualTo(.8f));
        }

        [TestCase(false)] [TestCase(true)]
        public void PathSampleEndpointFadeUsesArcLengthAndLeavesClosedPathsAlone(bool closed)
        {
            var samples = new List<StrokeSample>();
            foreach(float y in new[] { 0f, .1f, .5f, .8f, 1f })
                samples.Add(new StrokeSample(new Vector3(0,y,0),Vector3.forward,new Vector2(0,y),0,0));
            TexturePaintStageWindow.ApplyPathEndpointFade(samples,.2f,.4f,closed);
            Assert.That(samples[0].flowMultiplier, Is.EqualTo(closed ? 1 : 0).Within(.001f));
            Assert.That(samples[1].flowMultiplier, Is.EqualTo(closed ? 1 : .5f).Within(.001f));
            Assert.That(samples[2].flowMultiplier, Is.EqualTo(1f));
            Assert.That(samples[3].flowMultiplier, Is.EqualTo(closed ? 1 : .5f).Within(.001f));
            Assert.That(samples[4].flowMultiplier, Is.EqualTo(closed ? 1 : 0).Within(.001f));
        }

        [TestCase(false)] [TestCase(true)]
        public void LoadedPathKeepsSoftEdgesAfterControlEditAndUndoAndPersistsEndpointFades(bool worldSpace)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear);
            using var engine = new PaintingEngine(TexturePaintGpuTestFixture.LoadShader("StrokeRasterize.compute"),
                TexturePaintGpuTestFixture.LoadShader("Blur.compute"), TexturePaintGpuTestFixture.LoadShader("NormalTouchup.compute"), RibbonShader);
            var store = new TextureStore();
            var sets = (List<TextureSet>)typeof(TextureStore).GetField("sets", Private).GetValue(store); sets.Add(fixture.set);
            var reconstruction = new MeshReconstructionResult { root = fixture.owner };
            reconstruction.surfaces.Add(fixture.set.surface); reconstruction.logicalTargets.Rebuild(reconstruction.surfaces);
            var logical = new TexturePaintLogicalLayerController(reconstruction.logicalTargets);
            var target = reconstruction.logicalTargets.Targets[0]; target.members[0].textureSets.Add(fixture.set);
            fixture.set.surface.collider = fixture.owner.AddComponent<MeshCollider>(); fixture.set.surface.collider.sharedMesh = fixture.mesh;
            var layer = fixture.set.AddSplineLayer("Loaded zipper"); layer.spline.worldSpace = worldSpace; layer.spline.useBezier = false;
            layer.spline.AddPoint(new Vector3(.5f,0,0),new Vector2(.5f,0),0,0,Vector3.forward);
            layer.spline.AddPoint(new Vector3(.5f,1,0),new Vector2(.5f,1),0,1,Vector3.forward);
            var updateAnchor = typeof(TexturePaintStageWindow).GetMethod("UpdateSplineAnchorFromUV", BindingFlags.Static | BindingFlags.NonPublic);
            updateAnchor.Invoke(null, new object[] { fixture.set, layer.spline, 0 });
            updateAnchor.Invoke(null, new object[] { fixture.set, layer.spline, 1 });
            layer.splineSettings = new TexturePaintSplineSettings { source=TexturePaintBrushSource.Color, brushSize=.4f,
                brushHardness=.75f, brushFlow=1, pathMode=TexturePaintPathMode.Ribbon, startFade=.25f, endFade=.5f };
            Assert.That(logical.LinkAndRepair(target, fixture.set, layer, null, out _), Is.True);
            var controller = new TexturePaintStageController();
            typeof(TexturePaintStageController).GetProperty("Textures").SetValue(controller,store);
            typeof(TexturePaintStageController).GetProperty("Painting").SetValue(controller,engine);
            typeof(TexturePaintStageController).GetProperty("Reconstruction").SetValue(controller,reconstruction);
            typeof(TexturePaintStageController).GetProperty("LogicalLayers").SetValue(controller,logical);
            var stage = ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            var brush = fixture.CreateBrush(); var document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            try
            {
                Field(stage,"controller",controller); Field(stage,"transientBrush",brush); Field(stage,"spline",layer.spline);
                Invoke(stage,"RestoreSplineSettings",layer.splineSettings);
                Invoke(stage,"ApplySpline");
                Assert.That(Alpha(layer,8,32), Is.InRange(.08f,.4f), "Loading and reapplying must retain the saved 75% hardness.");
                Assert.That(Alpha(layer,32,1), Is.LessThan(.04f));
                TexturePaintDocumentStorage.Save(document,store);
                Assert.That(document.surfaces[0].layers[0].splineSettings.startFade, Is.EqualTo(.25f));
                Assert.That(document.surfaces[0].layers[0].splineSettings.endFade, Is.EqualTo(.5f));
                var restored = JsonUtility.FromJson<TexturePaintSplineSettings>(JsonUtility.ToJson(document.surfaces[0].layers[0].splineSettings));
                Invoke(stage,"RestoreSplineSettings",restored);
                Invoke(stage,"BeginLightweightPathUndo",fixture.set,"Move control point");
                layer.spline.worldPoints[1] += Vector3.right * .05f; layer.spline.uvPoints[1] += Vector2.right * .05f;
                updateAnchor.Invoke(null, new object[] { fixture.set, layer.spline, 1 });
                Invoke(stage,"CompleteLightweightPathEdit",fixture.set,false); Invoke(stage,"ApplySpline");
                Assert.That(Alpha(layer,10,32), Is.InRange(.1f,.6f), "Moving a control point must not make the edges opaque.");
                Assert.That(layer.splineSettings.brushHardness, Is.EqualTo(.75f));
                Assert.That(Invoke(stage,"UndoLightweight"), Is.True); Invoke(stage,"ApplySpline");
                Assert.That(Alpha(layer,8,32), Is.InRange(.08f,.4f));
                Invoke(stage,"BeginLightweightPathUndo",fixture.set,"Edit Path Fading");
                Field(stage,"pathStartFade",0f); Field(stage,"pathEndFade",0f); brush.hardness=1f;
                Invoke(stage,"CompleteLightweightPathEdit",fixture.set,false); Invoke(stage,"ApplySpline");
                Assert.That(Alpha(layer,8,32), Is.GreaterThan(.98f)); Assert.That(Alpha(layer,32,1), Is.GreaterThan(.98f));
                Assert.That(Invoke(stage,"UndoLightweight"), Is.True); Invoke(stage,"ApplySpline");
                Assert.That(Alpha(layer,8,32), Is.InRange(.08f,.4f)); Assert.That(Alpha(layer,32,1), Is.LessThan(.04f));

                Invoke(stage, "BeginLightweightPathUndo", fixture.set, "Edit Fade Curves");
                brush.hardness = 0f;
                Field(stage, "pathSideFadeExtra", .5f);
                Field(stage, "pathStartFade", 1.5f); Field(stage, "pathEndFade", 2f);
                Field(stage, "pathSideFadeCurve", AnimationCurve.Linear(0, .25f, 1, .25f));
                Field(stage, "pathStartFadeCurve", AnimationCurve.Linear(0, .4f, 1, .4f));
                Field(stage, "pathEndFadeCurve", AnimationCurve.Linear(0, .5f, 1, .5f));
                Invoke(stage, "CompleteLightweightPathEdit", fixture.set, false); Invoke(stage, "ApplySpline");
                Assert.That(Alpha(layer, 32, 32), Is.EqualTo(.05f).Within(.005f));
                int signature = (int)Invoke(stage, "GetPathRenderSignature", fixture.set, layer, layer.splineSettings);
                var curveChange = layer.splineSettings.Clone(); curveChange.sideFadeCurve.MoveKey(0, new Keyframe(0, .3f));
                Assert.That((int)Invoke(stage, "GetPathRenderSignature", fixture.set, layer, curveChange), Is.Not.EqualTo(signature));
                TexturePaintDocumentStorage.Save(document, store);
                restored = JsonUtility.FromJson<TexturePaintSplineSettings>(JsonUtility.ToJson(document.surfaces[0].layers[0].splineSettings));
                Invoke(stage, "RestoreSplineSettings", restored); Invoke(stage, "ApplySpline");
                Assert.That(Alpha(layer, 32, 32), Is.EqualTo(.05f).Within(.005f));
                Assert.That(layer.splineSettings.sideFadeExtra, Is.EqualTo(.5f));
                Assert.That(layer.splineSettings.startFade, Is.EqualTo(1.5f));
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True); Invoke(stage, "ApplySpline");
                Assert.That(Alpha(layer, 8, 32), Is.InRange(.08f, .4f));
                Assert.That(Invoke(stage, "RedoLightweight"), Is.True); Invoke(stage, "ApplySpline");
                Assert.That(Alpha(layer, 32, 32), Is.EqualTo(.05f).Within(.005f));

                Invoke(stage, "BeginLightweightPathUndo", fixture.set, "Edit Path Texture Mirroring");
                Field(stage, "pathTextureFlipX", TexturePaintPathFlipMode.Alternate);
                Field(stage, "pathTextureFlipY", TexturePaintPathFlipMode.Random);
                Field(stage, "pathTextureFlipSeed", -137);
                Invoke(stage, "CompleteLightweightPathEdit", fixture.set, false); Invoke(stage, "ApplySpline");
                Assert.That(layer.splineSettings.textureFlipX, Is.EqualTo(TexturePaintPathFlipMode.Alternate));
                Assert.That(layer.splineSettings.textureFlipY, Is.EqualTo(TexturePaintPathFlipMode.Random));
                Assert.That(layer.splineSettings.textureFlipSeed, Is.EqualTo(-137));
                signature = (int)Invoke(stage, "GetPathRenderSignature", fixture.set, layer, layer.splineSettings);
                foreach (int field in new[] { 0, 1, 2 })
                {
                    var changed = layer.splineSettings.Clone();
                    if (field == 0) changed.textureFlipX = TexturePaintPathFlipMode.Off;
                    if (field == 1) changed.textureFlipY = TexturePaintPathFlipMode.Off;
                    if (field == 2) changed.textureFlipSeed++;
                    Assert.That((int)Invoke(stage, "GetPathRenderSignature", fixture.set, layer, changed), Is.Not.EqualTo(signature));
                }
                TexturePaintDocumentStorage.Save(document, store);
                restored = JsonUtility.FromJson<TexturePaintSplineSettings>(JsonUtility.ToJson(document.surfaces[0].layers[0].splineSettings));
                Invoke(stage, "RestoreSplineSettings", restored); Invoke(stage, "ApplySpline");
                Assert.That(layer.splineSettings.textureFlipSeed, Is.EqualTo(-137));
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True); Invoke(stage, "ApplySpline");
                Assert.That(layer.splineSettings.textureFlipX, Is.EqualTo(TexturePaintPathFlipMode.Off));
                Assert.That(layer.splineSettings.textureFlipY, Is.EqualTo(TexturePaintPathFlipMode.Off));
                Assert.That(Invoke(stage, "RedoLightweight"), Is.True); Invoke(stage, "ApplySpline");
                Assert.That(layer.splineSettings.textureFlipX, Is.EqualTo(TexturePaintPathFlipMode.Alternate));
                Assert.That(layer.splineSettings.textureFlipY, Is.EqualTo(TexturePaintPathFlipMode.Random));
                Assert.That(layer.splineSettings.textureFlipSeed, Is.EqualTo(-137));

                Invoke(stage, "BeginLightweightPathUndo", fixture.set, "Edit Ribbon Join Crossfade");
                Field(stage, "pathCrossfadeJoins", true); Field(stage, "pathJoinOverlap", .75f);
                Invoke(stage, "CompleteLightweightPathEdit", fixture.set, false); Invoke(stage, "ApplySpline");
                Assert.That(layer.splineSettings.ribbonCrossfadeJoins, Is.True);
                Assert.That(layer.splineSettings.ribbonJoinOverlap, Is.EqualTo(.75f));
                signature = (int)Invoke(stage, "GetPathRenderSignature", fixture.set, layer, layer.splineSettings);
                var joinChange = layer.splineSettings.Clone(); joinChange.ribbonJoinOverlap = .5f;
                Assert.That((int)Invoke(stage, "GetPathRenderSignature", fixture.set, layer, joinChange), Is.Not.EqualTo(signature));
                joinChange = layer.splineSettings.Clone(); joinChange.ribbonCrossfadeJoins = false;
                Assert.That((int)Invoke(stage, "GetPathRenderSignature", fixture.set, layer, joinChange), Is.Not.EqualTo(signature));
                TexturePaintDocumentStorage.Save(document, store);
                restored = JsonUtility.FromJson<TexturePaintSplineSettings>(JsonUtility.ToJson(document.surfaces[0].layers[0].splineSettings));
                Invoke(stage, "RestoreSplineSettings", restored); Invoke(stage, "ApplySpline");
                Assert.That(layer.splineSettings.ribbonCrossfadeJoins, Is.True);
                Assert.That(layer.splineSettings.ribbonJoinOverlap, Is.EqualTo(.75f));
                Assert.That(Alpha(layer, 32, 32), Is.EqualTo(.05f).Within(.005f), "Join blending must not double-apply source alpha or the existing path fades.");
                Assert.That(Invoke(stage, "UndoLightweight"), Is.True); Invoke(stage, "ApplySpline");
                Assert.That(layer.splineSettings.ribbonCrossfadeJoins, Is.False);
                Assert.That(Invoke(stage, "RedoLightweight"), Is.True); Invoke(stage, "ApplySpline");
                Assert.That(layer.splineSettings.ribbonCrossfadeJoins, Is.True);
                Assert.That(layer.splineSettings.ribbonJoinOverlap, Is.EqualTo(.75f));
            }
            finally
            {
                Invoke(stage,"CancelScheduledSplineReapply"); Invoke(stage,"ClearLightweightHistory"); Field(stage,"controller",null);
                Object.DestroyImmediate(stage); Object.DestroyImmediate(brush); Object.DestroyImmediate(document);
                sets.Clear(); store.Dispose();
            }
        }
    }
}
#endif
