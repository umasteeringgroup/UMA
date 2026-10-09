#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed partial class TexturePaintHemSeamTests
    {
        private sealed class Ribbon : IDisposable
        {
            public const int Size=128;
            public readonly TexturePaintGpuTestFixture fixture=new TexturePaintGpuTestFixture(Color.clear,size:Size);
            public readonly PaintingEngine engine;
            public readonly BrushPreset brush;
            public readonly StrokeContext context;
            public readonly TexturePaintLayer layer;
            public Ribbon()
            {
                foreach(TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
                    if(channel!=TexturePaintChannel.Albedo)fixture.set.channels.Add(channel,new TextureChannelTarget
                    {channel=channel,format=RenderTextureFormat.ARGBHalf,editable=new EditableTextureTarget(channel.ToString(),Size,Size,RenderTextureFormat.ARGBHalf,null,Color.clear)});
                brush=fixture.CreateBrush(1,1,shape:BrushPreset.Shape.Square);brush.size=.4f;
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(TexturePaintAssets.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
                Assert.That(shader,Is.Not.Null);Assert.That(ShaderUtil.ShaderHasError(shader),Is.False,string.Join("; ",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message)));
                engine=new PaintingEngine(null,null,null,shader);
                layer=fixture.set.AddLayer("Seam");
                context=fixture.CreateContext(brush,TexturePaintTool.Paint,Color.white,strength:1);
                context.projectionDepth=1;
                context.hemSeam=TexturePaintHemSeamSettings.Create(TexturePaintSeamPreset.DenimChainstitchHem);
            }
            public Dictionary<TexturePaintChannel,Color[]> secondaryPixels;
            public Dictionary<TexturePaintChannel,Color[]> Render(bool directUV=false,bool closed=false,float tiles=1.25f,TextureSet secondary=null)
            {
                context.hemSeam.PopulateSources(context);context.directUV=directUV;
                var samples=new[]{new StrokeSample(new Vector3(.5f,0,0),Vector3.forward,new Vector2(.5f,0),0,0),
                    new StrokeSample(new Vector3(.5f,1,0),Vector3.forward,new Vector2(.5f,1),0,1)};
                var segments=new[]{new TexturePaintRibbonSegment {
                    leftStartAlong=new Vector4(.1f,0,0,0),rightStartFlow=new Vector4(.9f,0,0,1),
                    leftEndAlong=new Vector4(.1f,1,0,tiles),rightEndFlow=new Vector4(.9f,1,0,1),
                    normalStartPressure=new Vector4(0,0,1,1),normalEndPressure=new Vector4(0,0,1,1),colorStart=Color.white,colorEnd=Color.white }};
                Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay,secondary==null ? null : new[]{fixture.set,secondary}),Is.True,engine.LastStrokeError);
                Assert.That(engine.ApplyRibbon(segments,samples,true,false,closed,directUV),Is.True);
                var result=layer.channels.ToDictionary(x=>x.Key,x=>TexturePaintGpuTestFixture.ReadPixels(x.Value.Front));
                if(secondary!=null)secondaryPixels=secondary.layers[secondary.activeLayerIndex].channels.ToDictionary(x=>x.Key,x=>TexturePaintGpuTestFixture.ReadPixels(x.Value.Front));
                Assert.That(engine.RewindActiveStroke(),Is.True);engine.EndStroke(false);
                return result;
            }
            public void Dispose(){engine.EndStroke(false);engine.Dispose();fixture.Dispose();Object.DestroyImmediate(brush);}
        }
        private static float Difference(Color[] a,Color[] b)
        {float total=0;for(int i=0;i<a.Length;i++)total+=Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b)+Mathf.Abs(a[i].a-b[i].a);return total/a.Length;}

        [TestCaseSource(nameof(Presets))]
        public void EveryConstructionGeneratesFiniteDistinctSupportedChannels(TexturePaintSeamPreset preset)
        {
            using var r=new Ribbon();r.context.hemSeam=TexturePaintHemSeamSettings.Create(preset);
            r.context.hemSeam.height=true;r.context.hemSeam.masks=true;r.context.hemSeam.normal=true;
            var pixels=r.Render();Assert.That(pixels.Count,Is.EqualTo(6));
            foreach(var pair in pixels)
            {
                foreach(Color c in pair.Value)
                {Assert.That(float.IsFinite(c.r)&&float.IsFinite(c.g)&&float.IsFinite(c.b)&&float.IsFinite(c.a),Is.True,preset+" "+pair.Key);Assert.That(c.a,Is.InRange(0f,1.001f));}
                Assert.That(pair.Value.Max(c=>c.a),Is.GreaterThan(.005f),preset+" "+pair.Key+" must generate output");
                Assert.That(pair.Value[Ribbon.Size*64].a,Is.Zero,"Outside the ribbon stays transparent");
                if(TexturePaintChannelUtility.IsGrayscale(pair.Key))
                    foreach(var c in pair.Value){Assert.That(c.r,Is.EqualTo(c.g).Within(.001f));Assert.That(c.g,Is.EqualTo(c.b).Within(.001f));}
                if(pair.Key==TexturePaintChannel.Normal)
                    foreach(var c in pair.Value.Where(c=>c.a>.1f))Assert.That(new Vector3(c.r*2-1,c.g*2-1,c.b*2-1).magnitude,Is.EqualTo(1).Within(.006f));
            }
        }
        private static IEnumerable<TexturePaintSeamPreset> Presets()=>Enum.GetValues(typeof(TexturePaintSeamPreset)).Cast<TexturePaintSeamPreset>();

        [TestCaseSource(nameof(Patterns))]
        public void EveryStitchPatternUsesItsOwnColorAndPlacement(TexturePaintStitchPattern pattern)
        {
            using var r=new Ribbon();var s=TexturePaintHemSeamSettings.Create(TexturePaintSeamPreset.Topstitch);r.context.hemSeam=s;
            s.puckering=0;s.seamProtection=0;s.rows[0].pattern=pattern;s.rows[0].offset=.2f;s.rows[0].thickness=.03f;s.rows[0].color=Color.red;
            var red=r.Render()[TexturePaintChannel.Albedo];
            Assert.That(red.Any(c=>c.r>.5f && c.a>.1f),Is.True);
            Assert.That(red.Max(c=>c.g),Is.LessThan(.001f));Assert.That(red.Max(c=>c.b),Is.LessThan(.001f));
            s.rows[0].offset=-.2f;s.rows[0].color=Color.blue;
            var blue=r.Render()[TexturePaintChannel.Albedo];Assert.That(blue.Max(c=>c.b),Is.GreaterThan(.5f));
            Assert.That(Difference(red,blue),Is.GreaterThan(.01f));
        }
        private static IEnumerable<TexturePaintStitchPattern> Patterns()=>Enum.GetValues(typeof(TexturePaintStitchPattern)).Cast<TexturePaintStitchPattern>();

        // Render() maps across [0,1] into UV X [.1,.9], and with tiles=1 maps
        // along directly to UV Y. Sample texel centers nearest these locations.
        private static Color AtSeam(Color[] pixels,float acrossOffset,float along)
        {
            int x=Mathf.Clamp(Mathf.FloorToInt((.5f+acrossOffset*.8f)*Ribbon.Size),0,Ribbon.Size-1);
            int y=Mathf.Clamp(Mathf.FloorToInt(along*Ribbon.Size),0,Ribbon.Size-1);
            return pixels[y*Ribbon.Size+x];
        }

        [Test]
        public void LockstitchContactDarkensBesideEachThreadButLeavesTheGapClear()
        {
            using var r=new Ribbon();var s=TexturePaintHemSeamSettings.Create(TexturePaintSeamPreset.Topstitch);r.context.hemSeam=s;
            s.puckering=0;s.wear=0;s.recessDarkening=.8f;s.ambientOcclusion=1;s.seamProtection=.8f;
            s.rows[0].spacing=.4f;s.rows[0].length=.5f;s.rows[0].thickness=.035f;s.rows[0].color=Color.red;
            var output=r.Render(tiles:1);
            Color beside=AtSeam(output[TexturePaintChannel.Albedo],.027f,.2f);
            Color gap=AtSeam(output[TexturePaintChannel.Albedo],.027f,.4f);
            Assert.That(beside.a,Is.GreaterThan(gap.a+.025f),"The cloth immediately beside the finite stitch receives contact shadow");
            Assert.That(beside.r,Is.LessThan(.2f),"Contact darkening is an affine black overlay on the cloth, not more thread paint");
            Assert.That(AtSeam(output[TexturePaintChannel.AmbientOcclusion],.027f,.2f).a,
                Is.GreaterThan(AtSeam(output[TexturePaintChannel.AmbientOcclusion],.027f,.4f).a+.025f));
            Assert.That(gap.a,Is.LessThan(.01f),"No continuous protected-seam stripe between the stitches");
        }

        [Test]
        public void BarTackNeedleHolesFollowTheCrosswiseThreadEnds()
        {
            using var r=new Ribbon();var s=TexturePaintHemSeamSettings.Create(TexturePaintSeamPreset.BarTack);r.context.hemSeam=s;
            s.rows[0].spacing=.4f;s.rows[0].span=.32f;s.rows[0].thickness=.025f;
            s.puckering=0;s.ambientOcclusion=1;
            var pixels=r.Render(tiles:1)[TexturePaintChannel.AmbientOcclusion];
            float needle=AtSeam(pixels,.165f,.2f).a;
            float unused=AtSeam(pixels,0,.2f-.4f*s.rows[0].length*.5f).a;
            Assert.That(needle,Is.GreaterThan(.15f),"A crosswise stitch sinks into its endpoint needle holes");
            Assert.That(unused,Is.LessThan(.01f),"The lockstitch's old centerline endpoints must not create phantom holes");
        }

        [TestCase(TexturePaintSeamPreset.DoubleTurnHem,.33f)]
        [TestCase(TexturePaintSeamPreset.Lapped,-.025f)]
        public void OverlapShadowStaysOnTheLowerClothAndMirrorsWithTheConstruction(TexturePaintSeamPreset preset,float shadowOffset)
        {
            using var r=new Ribbon();var s=TexturePaintHemSeamSettings.Create(preset);r.context.hemSeam=s;
            s.rows.Clear();s.roping=0;s.puckering=0;s.wear=0;s.seamProtection=0;s.recessDarkening=1;s.ambientOcclusion=1;
            var output=r.Render(tiles:1);
            float otherSide=preset==TexturePaintSeamPreset.Lapped ? -shadowOffset : -.33f;
            foreach(var channel in new[]{TexturePaintChannel.Albedo,TexturePaintChannel.AmbientOcclusion})
                Assert.That(AtSeam(output[channel],shadowOffset,.5f).a,
                    Is.GreaterThan(AtSeam(output[channel],otherSide,.5f).a+.06f),channel+" must show the overlap direction");
            s.mirror=true;var mirrored=r.Render(tiles:1);
            foreach(var channel in new[]{TexturePaintChannel.Albedo,TexturePaintChannel.AmbientOcclusion})
                Assert.That(AtSeam(mirrored[channel],-shadowOffset,.5f).a,
                    Is.EqualTo(AtSeam(output[channel],shadowOffset,.5f).a).Within(.005f),channel+" overlap shadow follows Mirror Across Path");
        }

        [TestCaseSource(nameof(Patterns))]
        public void InvisibleThreadDoesNotLeaveNeedleHolesOrClothAdjustments(TexturePaintStitchPattern pattern)
        {
            using var r=new Ribbon();var s=TexturePaintHemSeamSettings.Create(TexturePaintSeamPreset.Topstitch);r.context.hemSeam=s;
            s.rows[0].pattern=pattern;s.rows[0].color=Color.clear;s.masks=true;s.normal=true;
            var invisible=r.Render();s.rows.Clear();var absent=r.Render();
            foreach(var channel in invisible.Keys)
                Assert.That(Difference(invisible[channel],absent[channel]),Is.LessThan(.00001f),channel.ToString());
        }

        [Test]
        public void DenimRopingDoesNotBleachTheWholeHemBand()
        {
            using var r=new Ribbon();r.context.hemSeam.rows.Clear();
            var pixels=r.Render(tiles:1)[TexturePaintChannel.Albedo];
            float brightestAddition=0;
            for(int y=1;y<Ribbon.Size-1;y++)
                for(float x=-.2f;x<=.1f;x+=.03f)
                {
                    Color c=AtSeam(pixels,x,(y+.5f)/Ribbon.Size);
                    brightestAddition=Mathf.Max(brightestAddition,c.r*c.a);
                }
            Assert.That(brightestAddition,Is.LessThan(.04f),
                "Default denim wear should retain dark cloth between folds instead of painting broad white diagonal bands");
        }

        [Test]
        public void ZeroClothAdjustmentsAndNoThreadLeaveAlbedoTransparent()
        {
            using var r=new Ribbon();var s=r.context.hemSeam;s.rows.Clear();s.wear=0;s.recessDarkening=0;s.seamProtection=0;
            var p=r.Render()[TexturePaintChannel.Albedo];Assert.That(p.Max(c=>c.a),Is.Zero);
        }
        [Test]
        public void ClothUsesNeutralGrayscaleWhilePreservingUnderlyingFabricContrast()
        {
            using var r=new Ribbon();var s=r.context.hemSeam;s.rows.Clear();s.wear=.45f;s.recessDarkening=.65f;
            var p=r.Render()[TexturePaintChannel.Albedo];
            Assert.That(p.Any(c=>c.a>.05f && c.r<.05f),Is.True,"Recess darkening");
            Assert.That(p.Any(c=>c.a>.05f && c.r>.9f),Is.True,"Raised wear");
            foreach(var c in p){Assert.That(c.r,Is.EqualTo(c.g).Within(.001));Assert.That(c.g,Is.EqualTo(c.b).Within(.001));}
            // Actual straight-alpha payload preserves the input weave's high/low contrast.
            foreach(var c in p.Where(c=>c.a>.05f))
                Assert.That((.6f*(1-c.a)+c.r*c.a)-(.2f*(1-c.a)+c.r*c.a),Is.GreaterThan(.01f));
        }
        [Test]
        public void SeedIsDeterministicAndSharedBetweenWorldAndUvProjection()
        {
            using var r=new Ribbon();r.context.hemSeam.masks=true;
            var a=r.Render();var b=r.Render(true);
            foreach(var channel in a.Keys)Assert.That(Difference(a[channel],b[channel]),Is.LessThan(.003f),channel.ToString());
            b=r.Render();foreach(var channel in a.Keys)Assert.That(Difference(a[channel],b[channel]),Is.LessThan(.00001f));
            r.context.hemSeam.seed=714;b=r.Render();Assert.That(Difference(a[TexturePaintChannel.Albedo],b[TexturePaintChannel.Albedo]),Is.GreaterThan(.01));
        }
        [Test]
        public void ExistingImageSourceCannotOverrideGeneratedOutput()
        {
            using var r=new Ribbon();r.context.hemSeam.rows.Clear();
            r.layer.GetChannelSettings(TexturePaintChannel.Albedo).sourceSettings=new TexturePaintChannelSourceSettings{source=TexturePaintBrushSource.Color,color=Color.magenta};
            var a=r.Render()[TexturePaintChannel.Albedo];foreach(var c in a)Assert.That(c.r,Is.EqualTo(c.g).Within(.001));
        }
        [Test]
        public void MirrorFlipsEveryOutputIncludingTheNormalSlope()
        {
            using var r=new Ribbon();r.context.hemSeam.masks=true;r.context.hemSeam.height=true;r.context.hemSeam.normal=true;
            var a=r.Render();r.context.hemSeam.mirror=true;var b=r.Render();
            foreach(var channel in a.Keys)
            {
                float error=0;int count=0;
                for(int y=1;y<Ribbon.Size-1;y++)for(int x=16;x<Ribbon.Size-16;x++)
                {
                    Color c=a[channel][y*Ribbon.Size+x],d=b[channel][y*Ribbon.Size+Ribbon.Size-1-x];
                    if(channel==TexturePaintChannel.Normal){if(c.a<.4f||d.a<.4f)continue;d.r=1-d.r;}
                    error+=Mathf.Abs(c.r-d.r)+Mathf.Abs(c.g-d.g)+Mathf.Abs(c.b-d.b)+Mathf.Abs(c.a-d.a);count++;
                }
                Assert.That(error/Mathf.Max(1,count),Is.LessThan(channel==TexturePaintChannel.Normal ?.12f:.015f),channel.ToString());
            }
        }
        [Test]
        public void DisablingRowsAndOutputsDoesNotLeakMaterialState()
        {
            using var r=new Ribbon();var s=r.context.hemSeam;s.rows.Add(s.rows[0].Clone());s.rows[1].color=Color.red;r.Render();
            foreach(var row in s.rows)row.enabled=false;
            var noRows=r.Render()[TexturePaintChannel.Albedo];s.rows.Clear();
            Assert.That(Difference(noRows,r.Render()[TexturePaintChannel.Albedo]),Is.LessThan(.00001f));
        }
        [Test]
        public void ClosedLoopFitsPeriodicRopingAndStitchSpacing()
        {
            using var r=new Ribbon();var s=r.context.hemSeam;s.rows.Clear();s.ropingFrequency=3.71f;
            var p=r.Render(closed:true,tiles:2.37f)[TexturePaintChannel.Albedo];
            float closing=0,interior=0;
            for(int x=20;x<108;x++)
            {
                closing+=Mathf.Abs(p[x].a-p[(Ribbon.Size-1)*Ribbon.Size+x].a);
                for(int y=1;y<Ribbon.Size;y++)interior+=Mathf.Abs(p[y*Ribbon.Size+x].a-p[(y-1)*Ribbon.Size+x].a)/(Ribbon.Size-1);
            }
            Assert.That(closing,Is.LessThan(interior*2.5f+.01f),"The closing join is no larger than a normal neighboring-sample change");
        }
        [Test]
        public void ModelDeepCloneRoundTripAndLegacyDefaultsAreIndependent()
        {
            Assert.That(JsonUtility.FromJson<TexturePaintSplineSettings>("{}").hemSeam?.enabled ?? false,Is.False,"Missing legacy data must never enable the generator");
            var original=new TexturePaintSplineSettings{hemSeam=TexturePaintHemSeamSettings.Create(TexturePaintSeamPreset.FlatFelled)};
            original.hemSeam.rows[1].color=Color.green;original.hemSeam.seed=-391;
            var copy=original.Clone();copy.hemSeam.rows[1].color=Color.red;copy.hemSeam.rows.RemoveAt(0);
            Assert.That(original.hemSeam.rows.Count,Is.EqualTo(2));Assert.That(original.hemSeam.rows[1].color,Is.EqualTo(Color.green));
            var roundTrip=JsonUtility.FromJson<TexturePaintSplineSettings>(JsonUtility.ToJson(original));
            Assert.That(JsonUtility.ToJson(roundTrip.hemSeam),Is.EqualTo(JsonUtility.ToJson(original.hemSeam)));
            original.hemSeam.roping=float.NaN;original.hemSeam.rows[0].spacing=0;original.hemSeam.rows.Add(null);original.hemSeam.Normalize();
            Assert.That(float.IsFinite(original.hemSeam.roping),Is.True);Assert.That(original.hemSeam.rows[0].spacing,Is.GreaterThan(0));Assert.That(original.hemSeam.rows.Count,Is.EqualTo(2));
        }
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object Invoke(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private).Invoke(target,args);
        private static void Field(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
        [TestCase(false)] [TestCase(true)]
        public void GeneratorRoundTripsThroughDocumentAndEditorUndoWithoutOverwritingAssignedSources(bool worldSpace)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear);
            using var engine = new PaintingEngine(TexturePaintGpuTestFixture.LoadShader("StrokeRasterize.compute"),
                TexturePaintGpuTestFixture.LoadShader("Blur.compute"), TexturePaintGpuTestFixture.LoadShader("NormalTouchup.compute"), AssetDatabase.LoadAssetAtPath<Shader>(TexturePaintAssets.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader")));
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

                var sourceSettings=layer.GetChannelSettings(TexturePaintChannel.Albedo).sourceSettings;
                string beforeSource=JsonUtility.ToJson(sourceSettings);
                Invoke(stage,"BeginLightweightPathUndo",fixture.set,"Enable seam");
                var seam=TexturePaintHemSeamSettings.Create(TexturePaintSeamPreset.FlatFelled);
                seam.rows[1].color=Color.cyan;seam.seed=-714;
                Field(stage,"pathHemSeam",seam);
                Invoke(stage,"CompleteLightweightPathEdit",fixture.set,false);Invoke(stage,"ApplySpline");
                var authored=TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front);
                Assert.That(authored.Max(c=>c.a),Is.GreaterThan(.1f));
                Assert.That(JsonUtility.ToJson(layer.GetChannelSettings(TexturePaintChannel.Albedo).sourceSettings),Is.EqualTo(beforeSource));
                TexturePaintDocumentStorage.Save(document,store);
                var saved=JsonUtility.FromJson<TexturePaintSplineSettings>(JsonUtility.ToJson(document.surfaces[0].layers[0].splineSettings));
                Assert.That(saved.hemSeam.rows[1].color,Is.EqualTo(Color.cyan));Assert.That(saved.hemSeam.seed,Is.EqualTo(-714));
                Invoke(stage,"RestoreSplineSettings",saved);Invoke(stage,"ApplySpline");
                Assert.That(Difference(authored,TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front)),Is.LessThan(.0001f));
                int signature=(int)Invoke(stage,"GetPathRenderSignature",fixture.set,layer,layer.splineSettings);
                var modified=saved.Clone();modified.hemSeam.rows[1].offset+=.1f;
                Assert.That((int)Invoke(stage,"GetPathRenderSignature",fixture.set,layer,modified),Is.Not.EqualTo(signature));
                Invoke(stage,"BeginLightweightPathUndo",fixture.set,"Move thread");
                Field(stage,"pathHemSeam",modified.hemSeam);Invoke(stage,"CompleteLightweightPathEdit",fixture.set,false);Invoke(stage,"ApplySpline");
                Assert.That(layer.splineSettings.hemSeam.rows[1].offset,Is.EqualTo(modified.hemSeam.rows[1].offset),"The edited row must reach the saved rendering settings");
                Assert.That(Difference(authored,TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front)),Is.GreaterThan(.001f),
                    (string)typeof(TexturePaintStageWindow).GetField("workspaceStatus",Private).GetValue(stage));
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Invoke(stage,"ApplySpline");
                Assert.That(Difference(authored,TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front)),Is.LessThan(.0001f));
                Assert.That(Invoke(stage,"RedoLightweight"),Is.True);Invoke(stage,"ApplySpline");
                Assert.That(layer.splineSettings.hemSeam.rows[1].offset,Is.EqualTo(modified.hemSeam.rows[1].offset));
                Invoke(stage,"BeginLightweightPathUndo",fixture.set,"Disable seam outputs");
                var disabled=modified.hemSeam.Clone();disabled.albedo=disabled.normal=disabled.ao=disabled.roughness=disabled.height=disabled.masks=false;
                Field(stage,"pathHemSeam",disabled);Invoke(stage,"CompleteLightweightPathEdit",fixture.set,false);Invoke(stage,"ApplySpline");
                Assert.That(TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front).Max(c=>c.a),Is.Zero);
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Invoke(stage,"ApplySpline");
                Assert.That(TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front).Max(c=>c.a),Is.GreaterThan(.1f));
                disabled.normal=true; // This fixture only maps Albedo: stale seam output must clear.
                Field(stage,"pathHemSeam",disabled);Invoke(stage,"ApplySpline");
                Assert.That(TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front).Max(c=>c.a),Is.Zero);
                int layerCount=fixture.set.layers.Count;
                Invoke(stage,"CreateHemSeamLayer",fixture.set,TexturePaintSeamPreset.BarTack);
                var created=fixture.set.layers[fixture.set.activeLayerIndex];
                Assert.That(created.splineSettings.hemSeam.preset,Is.EqualTo(TexturePaintSeamPreset.BarTack));
                Assert.That(created.splineSettings.brushSize,Is.EqualTo(.0125f));
                Assert.That(created.splineSettings.startFade,Is.Zero);Assert.That(created.splineSettings.endFade,Is.Zero);
                Assert.That(created.splineSettings.brushHardness,Is.EqualTo(1));
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(fixture.set.layers.Count,Is.EqualTo(layerCount));
                Assert.That(Invoke(stage,"RedoLightweight"),Is.True);Assert.That(fixture.set.layers.Count,Is.EqualTo(layerCount+1));
                Assert.That(fixture.set.layers.Last().splineSettings.hemSeam.rows[0].pattern,Is.EqualTo(TexturePaintStitchPattern.BarTack));
            }
            finally
            {
                Invoke(stage,"CancelScheduledSplineReapply");Invoke(stage,"ClearLightweightHistory");Field(stage,"controller",null);
                Object.DestroyImmediate(stage);Object.DestroyImmediate(brush);Object.DestroyImmediate(document);
                sets.Clear();store.Dispose();
            }
        }

        [Test]
        public void MultipleTextureDestinationsShareConstructionAndCorrectMirroredUvNormals()
        {
            using var first=new Ribbon();using var second=new Ribbon();
            second.fixture.set.persistentId="seam-tile-1002";second.fixture.set.surface.index=1;
            var uv=second.fixture.mesh.uv;for(int i=0;i<uv.Length;i++)uv[i].y=1-uv[i].y;
            second.fixture.mesh.uv=uv;second.fixture.mesh.RecalculateTangents();
            first.context.hemSeam.height=true;first.context.hemSeam.masks=true;first.context.hemSeam.normal=true;
            var a=first.Render(secondary:second.fixture.set);var b=first.secondaryPixels;
            Assert.That(b.Count,Is.EqualTo(6));
            foreach(var channel in a.Keys)
            {
                float error=0;int count=0;
                for(int y=1;y<Ribbon.Size-1;y++)for(int x=16;x<Ribbon.Size-16;x++)
                {
                    Color c=a[channel][y*Ribbon.Size+x],d=b[channel][(Ribbon.Size-1-y)*Ribbon.Size+x];
                    if(channel==TexturePaintChannel.Normal){if(c.a<.4f||d.a<.4f)continue;d.g=1-d.g;}
                    error+=Mathf.Abs(c.r-d.r)+Mathf.Abs(c.g-d.g)+Mathf.Abs(c.b-d.b)+Mathf.Abs(c.a-d.a);count++;
                }
                Assert.That(error/Mathf.Max(1,count),Is.LessThan(.012f),channel.ToString());
            }
        }
        [Test]
        public void MissingOutputOnOneMemberClearsItsPreviousRasterWithoutBlockingOtherMembers()
        {
            using var first=new Ribbon();using var second=new Ribbon();
            second.fixture.set.persistentId="seam-no-normal";second.fixture.set.surface.index=1;
            second.fixture.set.GetChannel(TexturePaintChannel.Normal).editable.Dispose();
            second.fixture.set.channels.Remove(TexturePaintChannel.Normal);
            second.fixture.set.GetPaintTarget(TexturePaintChannel.Albedo,TexturePaintSourceMode.SourceOverlay).Reset(null,Color.red);
            first.context.hemSeam.albedo=first.context.hemSeam.ao=first.context.hemSeam.roughness=first.context.hemSeam.height=false;first.context.hemSeam.normal=true;
            first.context.replaceLayer=first.layer;first.context.replaceHistoryGroup=true;first.context.historyGroupKey="seam-replace";
            first.layer.proceduralGroupKey=second.layer.proceduralGroupKey="seam-replace";
            var pixels=first.Render(secondary:second.fixture.set);
            Assert.That(pixels[TexturePaintChannel.Normal].Max(c=>c.a),Is.GreaterThan(.1f));
            Assert.That(first.secondaryPixels[TexturePaintChannel.Albedo].Max(c=>c.a),Is.Zero,"Removed outputs must not leave an old seam on an unsupported member");
        }
        [Test]
        public void MaximumThreadReliefKeepsHeightInRange()
        {
            using var r=new Ribbon();var s=r.context.hemSeam;s.height=true;s.relief=.15f;s.rows.Clear();
            for(int i=0;i<8;i++)s.rows.Add(new TexturePaintSeamStitchRow { offset=0,relief=2,thickness=.1f });
            foreach(var c in r.Render()[TexturePaintChannel.NormalControl])Assert.That(c.r,Is.InRange(0f,1f));
        }
        [Test]
        public void CapturePresetContactSheetFromProductionRenderer()
        {
            using var r=new Ribbon();const int cell=128;const int columns=4;
            var sheet=new Texture2D(columns*cell,4*cell,TextureFormat.RGBA32,false,true);
            try
            {
                int index=0;
                foreach(var preset in Presets())
                {
                    r.context.hemSeam=TexturePaintHemSeamSettings.Create(preset);r.context.hemSeam.normal=true;
                    var output=r.Render();var colors=output[TexturePaintChannel.Albedo];var normals=output[TexturePaintChannel.Normal];
                    for(int y=0;y<cell;y++)for(int x=0;x<cell;x++)
                    {
                        // Neutral synthetic denim weave under the actual generated layer.
                        float weave=.8f+.2f*Mathf.Sin((x+y)*2.1f)*Mathf.Sin((x-y)*2.7f);
                        var cloth=new Color(.11f,.2f,.3f)*weave;cloth.a=1;
                        Color c=colors[y*cell+x];Color n=normals[y*cell+x];
                        var normal=new Vector3((n.r*2-1)*n.a,(n.g*2-1)*n.a,Mathf.Lerp(1,n.b*2-1,n.a)).normalized;
                        float light=.65f+.4f*Mathf.Max(0,Vector3.Dot(normal,new Vector3(-.4f,.5f,1).normalized));
                        var result=(cloth*(1-c.a)+new Color(c.r,c.g,c.b)*c.a)*light;result.a=1;
                        sheet.SetPixel(index%columns*cell+x,(3-index/columns)*cell+y,result);
                    }
                    index++;
                }
                sheet.Apply();Directory.CreateDirectory("Logs/HemSeam");File.WriteAllBytes("Logs/HemSeam/Presets.png",sheet.EncodeToPNG());
            }
            finally{Object.DestroyImmediate(sheet);}
        }

    }
}
#endif
