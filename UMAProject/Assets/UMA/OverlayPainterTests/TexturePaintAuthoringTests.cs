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
    public sealed class TexturePaintAuthoringTests
    {
        private const string Folder = "Assets/UMAProjectData/Tests/OverlayPainter/GeneratedAuthoringTests";
        private readonly List<Object> objects = new List<Object>();
        private readonly List<TextureStore> stores = new List<TextureStore>();
        private readonly List<TextureSet> sets = new List<TextureSet>();
        private readonly List<TextureLayerCompositor> compositors=new List<TextureLayerCompositor>();
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private T Own<T>(T value) where T : Object { objects.Add(value); return value; }
        [TearDown] public void TearDown()
        {
            foreach(var compositor in compositors)compositor.Dispose();compositors.Clear();
            foreach (var store in stores) store.Dispose();
            foreach (var set in sets) set.Dispose();
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            TexturePaintSpriteSource.ClearCache();
            objects.Clear(); stores.Clear(); sets.Clear();
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }
        private Texture2D Image(Color color)
        {
            var texture = Own(new Texture2D(16, 16, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp });
            var pixels = new Color[256]; Array.Fill(pixels, color); texture.SetPixels(pixels); texture.Apply(); return texture;
        }
        private Mesh Quad(float minX = -0.5f, float maxX = 0.5f, float z = 0, bool reverse = false)
        {
            var mesh = Own(new Mesh());
            mesh.vertices = new[] { new Vector3(minX,-0.5f,z),new Vector3(maxX,-0.5f,z),new Vector3(maxX,0.5f,z),new Vector3(minX,0.5f,z) };
            mesh.uv = new[] { Vector2.zero,Vector2.right,Vector2.one,Vector2.up };
            mesh.triangles = reverse ? new[] { 0,2,1,0,3,2 } : new[] { 0,1,2,0,2,3 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private TextureSet Set(Mesh mesh = null)
        {
            var set = new TextureSet { persistentId = Guid.NewGuid().ToString("N"), surface = new ReconstructedSurface
            { mesh = mesh != null ? mesh : Quad(), index = sets.Count, slotName = "Body", slotNames = new List<string> { "Body" },
                triangleSlotNames = new[] { "Body", "Body" }, triangleIslands = new[] { 0,0 } } };
            AddChannel(set, TexturePaintChannel.Albedo); sets.Add(set); return set;
        }
        private static void AddChannel(TextureSet set, TexturePaintChannel channel, int index = 0)
        {
            set.channels[channel] = new TextureChannelTarget { channel = channel, umaChannelIndex = index,
                materialProperty = "_" + channel, sourceKeyword = channel.ToString(), format = RenderTextureFormat.ARGBHalf,
                editable = new EditableTextureTarget("Projection test",64,64,RenderTextureFormat.ARGBHalf,null,Color.clear) };
        }
        private TextureStore Store(params TextureSet[] members)
        {
            var store = new TextureStore();
            var list = (List<TextureSet>)typeof(TextureStore).GetField("sets",Private).GetValue(store);
            list.AddRange(members); foreach(var set in members) sets.Remove(set); stores.Add(store); return store;
        }
        private TexturePaintProjectionSettings Definition(Texture2D image = null) => new TexturePaintProjectionSettings
        { texture = image != null ? image : Image(Color.red), placed = true, width = 1, height = 1, depth = 0.4f,
            fade = TexturePaintProjectionFade.None, depthFade = 0, visibilityResolution = 64 };
        private static Color Pixel(TexturePaintLayer layer, int x = 32, int y = 32, TexturePaintChannel channel = TexturePaintChannel.Albedo)
        {
            RenderTexture previous = RenderTexture.active; var read = new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            try { RenderTexture.active = layer.channels[channel].Front; read.ReadPixels(new Rect(x,y,1,1),0,0); read.Apply(); return read.GetPixel(0,0); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(read); }
        }
        private static void Generate(TexturePaintProjectionSettings settings, TextureSet[] all, TextureSet[] targets, params TexturePaintLayer[] layers)
        {
            using var renderer = new TexturePaintProjectionRenderer();
            Assert.That(renderer.Generate(all, targets, layers, settings, out string error), Is.True, error);
        }
        private static object Invoke(TexturePaintStageWindow stage, string method, params object[] args)
            => typeof(TexturePaintStageWindow).GetMethod(method, Private).Invoke(stage,args);


        private TexturePaintLayer Paint(TextureSet set,Color color)
        {
            var layer=set.AddLayer("Source");layer.channels[TexturePaintChannel.Albedo]=new EditableTextureTarget("Source",64,64,RenderTextureFormat.ARGBHalf,null,color);return layer;
        }
        private TexturePaintLayer Reference(TextureSet set,TextureSet owner,TexturePaintLayer source,bool instance=false)
        {
            var layer=set.AddLayer("Reference");layer.kind=TexturePaintLayerKind.Reference;layer.parentId=ReferenceEquals(set,owner)?source.parentId:null;layer.links=new TexturePaintLayerLinks();
            var reference=TexturePaintLayerReference.To(owner,source);reference.component=TexturePaintReferenceComponent.Color;
            if(instance)layer.links.instance=reference;else layer.links.content=reference;return layer;
        }
        [Test] public void ReferenceUpdatesThroughChainAndDoesNotRegenerateWhenUnchanged()
        {
            var set=Set();var source=Paint(set,Color.red);var a=Reference(set,set,source);var b=Reference(set,set,a);var store=Store(set);
            store.RefreshLayerLinks();Assert.That(a.linkError,Is.Null);Assert.That(Pixel(b).r,Is.GreaterThan(.99));var buffer=b.channels[TexturePaintChannel.Albedo];
            store.RefreshLayerLinks();Assert.That(b.channels[TexturePaintChannel.Albedo],Is.SameAs(buffer));
            source.channels[TexturePaintChannel.Albedo].Reset(null,Color.green);store.RefreshLayerLinks();Assert.That(Pixel(b).g,Is.GreaterThan(.99));
        }
        [Test] public void CyclesAndMissingSourcesRetainCachedPixelsAndRecover()
        {
            var set=Set();var source=Paint(set,Color.red);var a=Reference(set,set,source);var b=Reference(set,set,a);var store=Store(set);store.RefreshLayerLinks();
            a.links.content=TexturePaintLayerReference.To(set,b);store.RefreshLayerLinks();Assert.That(a.linkError,Is.Not.Empty);Assert.That(Pixel(b).r,Is.GreaterThan(.99));
            a.links.content=TexturePaintLayerReference.To(set,source);set.layers.Remove(source);store.RefreshLayerLinks();Assert.That(a.linkError,Does.Contain("missing"));
            set.layers.Insert(0,source);source.channels[TexturePaintChannel.Albedo].Reset(null,Color.blue);store.RefreshLayerLinks();Assert.That(a.linkError,Is.Null);Assert.That(Pixel(b).b,Is.GreaterThan(.99));
        }
        [Test] public void MissingChannelDoesNotBecomeOpaqueBlack()
        {
            var set=Set();var source=Paint(set,Color.red);var layer=Reference(set,set,source);var store=Store(set);store.RefreshLayerLinks();
            layer.links.content.channel=TexturePaintChannel.Normal;store.RefreshLayerLinks();Assert.That(layer.linkError,Does.Contain("channel"));Assert.That(Pixel(layer).r,Is.GreaterThan(.99));
        }
        [Test] public void LogicalReferenceUsesCorrespondingUDIMMember()
        {
            var a=Set();var b=Set();var sa=Paint(a,Color.red);var sb=Paint(b,Color.green);sa.logicalLayerId=sb.logicalLayerId="source";sa.paintTargetId=sb.paintTargetId="body";
            var ra=Reference(a,a,sa);var rb=Reference(b,a,sa);var store=Store(a,b);store.RefreshLayerLinks();Assert.That(Pixel(ra).r,Is.GreaterThan(.99));Assert.That(Pixel(rb).g,Is.GreaterThan(.99));
        }
        [TestCase(TexturePaintReferenceComponent.Red,.2f)]
        [TestCase(TexturePaintReferenceComponent.Green,.4f)]
        [TestCase(TexturePaintReferenceComponent.Blue,.8f)]
        [TestCase(TexturePaintReferenceComponent.Alpha,.6f)]
        public void ComponentReferencesExtractScalarCoverage(TexturePaintReferenceComponent component,float expected)
        {
            var set=Set();var source=Paint(set,new Color(.2f,.4f,.8f,.6f));var layer=Reference(set,set,source);layer.links.content.component=component;var store=Store(set);
            store.RefreshLayerLinks();Assert.That(Pixel(layer).a,Is.EqualTo(expected).Within(.003));layer.links.content.invert=true;store.RefreshLayerLinks();Assert.That(Pixel(layer).a,Is.EqualTo(1-expected).Within(.003));
        }
        [Test] public void LiveMaskMultipliesExistingPaintedMaskAndRetainsCacheWhenSourceDisappears()
        {
            var set=Set();var source=Paint(set,new Color(1,1,1,.4f));var layer=Paint(set,Color.red);set.AddLayerMask(layer,.5f);
            layer.links=new TexturePaintLayerLinks{mask=TexturePaintLayerReference.To(set,source)};var store=Store(set);store.RefreshLayerLinks();
            Assert.That(TexturePaintRegionRenderer.Read(layer.linkedMask).Decode()[100],Is.EqualTo(51).Within(2));
            set.layers.Remove(source);store.RefreshLayerLinks();Assert.That(layer.linkError,Is.Not.Empty);Assert.That(layer.linkedMask,Is.Not.Null);set.layers.Insert(0,source);
        }
        [Test] public void PaintInstanceHasIndependentTransformAndFollowsSource()
        {
            var set=Set();var source=Paint(set,Color.red);var layer=Reference(set,set,source,true);layer.links.offset=new Vector2(-.5f,0);var store=Store(set);store.RefreshLayerLinks();
            Assert.That(Pixel(layer,10,32).a,Is.LessThan(.01));Assert.That(Pixel(layer,50,32).r,Is.GreaterThan(.99));
            source.channels[TexturePaintChannel.Albedo].Reset(null,Color.green);store.RefreshLayerLinks();Assert.That(Pixel(layer,50,32).g,Is.GreaterThan(.99));Assert.That(layer.links.offset.x,Is.EqualTo(-.5f));
        }
        [Test] public void FillInstanceCopiesMaterialButKeepsPlacement()
        {
            var set=Set();var source=set.AddFillLayer("Fill",TexturePaintChannel.Albedo,new TexturePaintFillSettings{color=Color.red});
            var layer=set.CloneLayer(source,"Instance");set.layers.Add(layer);layer.links=new TexturePaintLayerLinks{instance=TexturePaintLayerReference.To(set,source)};layer.fillSettings.offset=new Vector2(.2f,.1f);
            var store=Store(set);store.RefreshLayerLinks();Assert.That(layer.linkError,Is.Null);Assert.That(Pixel(layer).r,Is.GreaterThan(.99));
            var settings=source.fillSettings.Clone();settings.color=Color.blue;set.UpdateFillLayer(source,TexturePaintChannel.Albedo,settings);store.RefreshLayerLinks();
            Assert.That(layer.linkError,Is.Null);Assert.That(Pixel(layer).b,Is.GreaterThan(.99));Assert.That(layer.fillSettings.offset,Is.EqualTo(new Vector2(.2f,.1f)));
        }
        [Test] public void ProjectionInstanceKeepsOneIndependentWorldPlacement()
        {
            var set=Set();var source=set.AddProjectionLayer();var definition=Definition(Image(Color.red));definition.width=definition.height=.2f;Generate(definition,new[]{set},new[]{set},source);
            var layer=set.CloneLayer(source,"Instance");set.layers.Add(layer);layer.projectionSettings.position=new Vector3(.3f,0,0);layer.projectionSettings.depthFromSurface=true;layer.links=new TexturePaintLayerLinks{instance=TexturePaintLayerReference.To(set,source)};
            var store=Store(set);store.RefreshLayerLinks();Assert.That(layer.linkError,Is.Null);Assert.That(Pixel(layer,51,32).r,Is.GreaterThan(.99));Assert.That(Pixel(layer).a,Is.LessThan(.01));
            source.projectionSettings.texture=Image(Color.green);store.RefreshLayerLinks();Assert.That(Pixel(layer,51,32).g,Is.GreaterThan(.99));Assert.That(layer.projectionSettings.position.x,Is.EqualTo(.3f));Assert.That(layer.projectionSettings.depthFromSurface,Is.True);
        }
        [Test] public void RegionRoundtripAndCloneAreIndependent()
        {
            var pixels=new byte[]{0,50,100,255};var region=TexturePaintRegion.Encode(2,2,pixels);var clone=region.Clone();clone.compressed[0]^=1;Assert.That(region.Decode(),Is.EqualTo(pixels));
            var json=JsonUtility.ToJson(region);Assert.That(JsonUtility.FromJson<TexturePaintRegion>(json).Decode(),Is.EqualTo(pixels));
        }

        [TestCase(0)]
        [TestCase(128)]
        [TestCase(255)]
        public void SceneSelectionPreviewShowsProtectedSelectedAndFeatheredCoverage(byte coverage)
        {
            var shader=Shader.Find("Hidden/UMA/TexturePaint/RegionSelection");
            Assert.That(shader!=null && shader.isSupported,Is.True);
            var material=Own(new Material(shader));
            var set=Set();
            set.activeRegion=TexturePaintRegion.Encode(1,1,new[]{coverage});
            material.SetTexture("_SelectionShape",set.RegionTexture);
            var target=RenderTexture.GetTemporary(16,16,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;
            try
            {
                using(var command=new UnityEngine.Rendering.CommandBuffer())
                {
                    command.SetRenderTarget(target);
                    command.ClearRenderTarget(false,true,new Color(.5f,.5f,.5f,1));
                    command.SetViewProjectionMatrices(Matrix4x4.identity,
                        GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-.5f,.5f,-.5f,.5f,-1,1),true));
                    command.DrawMesh(set.surface.mesh,Matrix4x4.identity,material,0,2);
                    Graphics.ExecuteCommandBuffer(command);
                }
                var read=Own(new Texture2D(1,1,TextureFormat.RGBAFloat,false,true));
                RenderTexture.active=target;read.ReadPixels(new Rect(8,8,1,1),0,0);read.Apply();
                Color tint=new Color(0,.9f,1,.24f*(1-coverage/255f));
                Color expected=Color.Lerp(new Color(.5f,.5f,.5f,1),tint,tint.a);
                Color actual=read.GetPixel(0,0);
                Assert.That(actual.r,Is.EqualTo(expected.r).Within(.005f));
                Assert.That(actual.g,Is.EqualTo(expected.g).Within(.005f));
                Assert.That(actual.b,Is.EqualTo(expected.b).Within(.005f));
                Assert.That(actual.a,Is.EqualTo(1).Within(.005f),"Scene indication must preserve the scene alpha.");
                Assert.That(set.activeRegion.Decode(),Is.EqualTo(new[]{coverage}),"Preview must not alter selection coverage.");
            }
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);}
        }
        [Test] public void UVSelectionPreviewTintsOnlyProtectionAndRefreshesWithoutChangingSelection()
        {
            var set=Set();var coverage=new byte[]{0,128,255};
            set.activeRegion=TexturePaintRegion.Encode(3,1,coverage);
            WithStage(Store(set),stage=>
            {
                try
                {
                    var preview=(Texture2D)Invoke(stage,"GetProtectedRegionPreview",set);
                    var pixels=preview.GetPixels32();
                    Assert.That(pixels[0],Is.EqualTo(new Color32(255,255,255,255)));
                    Assert.That(pixels[1].a,Is.EqualTo(127));
                    Assert.That(pixels[2].a,Is.Zero,"Fully selected pixels must remain unobscured.");
                    Assert.That(Invoke(stage,"GetProtectedRegionPreview",set),Is.SameAs(preview));
                    Assert.That(set.activeRegion.Decode(),Is.EqualTo(coverage));
                    Assert.That(set.RegionTexture.GetPixels32()[1].r,Is.EqualTo(128));
                    set.activeRegion=set.activeRegion.Adjust(0,0,true);
                    var inverted=(Texture2D)Invoke(stage,"GetProtectedRegionPreview",set);
                    Assert.That(preview==null,Is.True,"Old display textures must be released.");
                    Assert.That(inverted.GetPixels32()[0].a,Is.Zero);
                    Assert.That(inverted.GetPixels32()[2].a,Is.EqualTo(255));
                    set.activeRegion=null;
                    Assert.That(Invoke(stage,"GetProtectedRegionPreview",set),Is.Null);
                    Assert.That(inverted==null,Is.True);
                }
                finally{Invoke(stage,"DisposeRegionPreviews");}
            });
        }
        [Test] public void PolygonSelectsExpectedPixelsAndGrowShrinkFeatherWork()
        {
            var region=TexturePaintRegionRenderer.Polygon(9,9,new[]{new Vector2(3f/9,3f/9),new Vector2(6f/9,3f/9),new Vector2(6f/9,6f/9),new Vector2(3f/9,6f/9)});
            Assert.That(Array.FindAll(region.Decode(),p=>p>0).Length,Is.EqualTo(9));
            Assert.That(Array.FindAll(region.Adjust(1,0,false).Decode(),p=>p>0).Length,Is.EqualTo(25));
            Assert.That(Array.FindAll(region.Adjust(-1,0,false).Decode(),p=>p>0).Length,Is.EqualTo(1));
            Assert.That(region.Adjust(0,1,false).Decode()[3*9+3],Is.InRange(100,120));
            Assert.That(region.Adjust(0,0,true).Decode()[0],Is.EqualTo(255));
        }
        [TestCase(TexturePaintRegionCombine.Replace,128)]
        [TestCase(TexturePaintRegionCombine.Add,200)]
        [TestCase(TexturePaintRegionCombine.Subtract,99)]
        [TestCase(TexturePaintRegionCombine.Intersect,100)]
        public void RegionOperationsPreserveSoftCoverage(TexturePaintRegionCombine mode,int expected)
        {Assert.That(TexturePaintRegion.Combine(200,128,mode),Is.EqualTo(expected));}
        [Test] public void SharedSymmetryUsesMovableRotatedPlaneAndDeduplicatesCopies()
        {
            var symmetry=new TexturePaintSymmetry{enabled=true,origin=new Vector3(2,0,0)};var transforms=symmetry.Transforms();Assert.That(transforms.Count,Is.EqualTo(2));
            Assert.That(transforms[1].MultiplyPoint3x4(new Vector3(3,2,1)),Is.EqualTo(new Vector3(1,2,1)));
            symmetry.radialCopies=4;symmetry.mirrorY=true;symmetry.radialAxis=Vector3.forward;Assert.That(symmetry.Transforms().Count,Is.EqualTo(8));
            symmetry.euler=new Vector3(0,0,90);symmetry.radialCopies=1;symmetry.mirrorY=false;Assert.That(symmetry.Transforms()[1].MultiplyPoint3x4(new Vector3(2,3,0)).y,Is.EqualTo(-3).Within(.0001));
        }
        [Test] public void SymmetricWrappedProjectionPreservesReflectedControlPoints()
        {
            var source=Definition();source.position=new Vector3(.2f,.3f,.1f);source.rotation=Quaternion.Euler(20,40,15);source.points[4].z=.13f;
            var matrix=Matrix4x4.Scale(new Vector3(-1,1,1));var result=TexturePaintSymmetry.TransformProjection(source,matrix);
            for(int i=0;i<9;i++){int from=(i/3)*3+2-i%3;Assert.That(Vector3.Distance(result.PointToWorld(result.points[i]),matrix.MultiplyPoint3x4(source.PointToWorld(source.points[from]))),Is.LessThan(.00001));}
            Assert.That(result.flipX,Is.True);Assert.That(source.flipX,Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void SelectionClipsBrushOnCPUAndGPU(bool gpu)
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.clear);var layer=fixture.set.AddLayer("Paint");
            fixture.set.activeRegion=TexturePaintRegionRenderer.Polygon(64,64,new[]{Vector2.zero,new Vector2(.5f,0),new Vector2(.5f,1),Vector2.up});
            var brush=Own(fixture.CreateBrush(1,1,TexturePaintBlendMode.Normal,BrushPreset.Shape.Square));brush.size=1;
            using var engine=gpu?TexturePaintGpuTestFixture.CreateEngine():new PaintingEngine(null,null,null);
            var context=fixture.CreateContext(brush,TexturePaintTool.Paint,Color.green,strength:1);context.directUV=true;
            Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay),Is.True);
            engine.ApplySample(new StrokeSample(new Vector3(.5f,.5f,0),Vector3.forward,new Vector2(.5f,.5f),0,-1),1);engine.EndStroke(true);
            Assert.That(Pixel(layer,16,32).g,Is.GreaterThan(.9));Assert.That(Pixel(layer,48,32).a,Is.LessThan(.01));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void GeneratedPathIgnoresTemporarySelectionButRetainsPersistentMask(int backend)
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.clear);
            using var compositor=new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            fixture.set.compositor=compositor;
            fixture.set.channels[TexturePaintChannel.Albedo].composite=EditableTextureTarget.Create("Path selection composite",64,64,RenderTextureFormat.ARGBHalf);
            var layer=fixture.set.AddSplineLayer("Path");
            var selection=TexturePaintRegionRenderer.Polygon(64,64,new[]{Vector2.zero,new Vector2(.5f,0),new Vector2(.5f,1),Vector2.up});
            fixture.set.activeRegion=selection;
            var persistentMask=fixture.set.InitializeLayerMask(layer,1);
            persistentMask.target.Reset(fixture.set.RegionTexture,Color.white);
            var brush=Own(fixture.CreateBrush(1,1,TexturePaintBlendMode.Normal,BrushPreset.Shape.Square));brush.size=1;
            using var engine=backend==0?new PaintingEngine(null,null,null):TexturePaintGpuTestFixture.CreateEngine();
            var context=fixture.CreateContext(brush,TexturePaintTool.Paint,Color.green,strength:1);
            context.directUV=true;context.derivedLayerRaster=true;context.replaceLayer=layer;
            context.historyGroupKey="path-selection";context.replaceHistoryGroup=true;
            var sample=TexturePaintGpuTestFixture.CenterSample();sample.triangleIndex=-1;
            foreach(bool selected in new[]{true,false,true})
            {
                fixture.set.activeRegion=selected?selection:null;
                Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay),Is.True);
                if(backend==2)Assert.That(engine.ApplySamples(new[]{new StrokeDispatchSample(sample,1,default)}),Is.True);
                else Assert.That(engine.ApplySample(sample,1),Is.True);
                engine.EndStroke(true);
                Assert.That(Pixel(layer,48,32).a,Is.GreaterThan(.9),"Generated pixels must not depend on temporary selection.");
                Assert.That(layer.layerMask,Is.SameAs(persistentMask));
                fixture.set.CompositeChannel(TexturePaintChannel.Albedo);
                var visible=TexturePaintGpuTestFixture.ReadPixels(fixture.set.GetVisibleTexture(TexturePaintChannel.Albedo));
                Assert.That(visible[32*64+16].g,Is.GreaterThan(.9));
                Assert.That(visible[32*64+48].a,Is.LessThan(.01),"The persistent mask must still constrain regenerated output.");
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void PaintingAPathLayerMaskStillHonorsTemporarySelection(bool gpu)
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.clear);
            var layer=fixture.set.AddSplineLayer("Masked path");var mask=fixture.set.InitializeLayerMask(layer,0);
            fixture.set.activeRegion=TexturePaintRegionRenderer.Polygon(64,64,new[]{Vector2.zero,new Vector2(.5f,0),new Vector2(.5f,1),Vector2.up});
            var brush=Own(fixture.CreateBrush(1,1,TexturePaintBlendMode.Normal,BrushPreset.Shape.Square));brush.size=1;
            using var engine=gpu?TexturePaintGpuTestFixture.CreateEngine():new PaintingEngine(null,null,null);
            var context=fixture.CreateContext(brush,TexturePaintTool.Paint,Color.white,strength:1);
            context.editLayerMask=true;context.directUV=true;
            Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay),Is.True);
            Assert.That(engine.ApplySample(TexturePaintGpuTestFixture.CenterSample(),1),Is.True);engine.EndStroke(true);
            var values=TexturePaintRegionRenderer.Read(mask.target.Front).Decode();
            Assert.That(values[32*64+16],Is.GreaterThan(240));Assert.That(values[32*64+48],Is.LessThan(3));
        }
        [TestCase(false)] [TestCase(true)]
        public void SceneSelectionRespectsOcclusionUnlessThroughEnabled(bool through)
        {
            var front=Set(Quad(z:-.1f));var back=Set(Quad(z:.1f));var camera=Own(new GameObject("Camera")).AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-2);camera.transform.rotation=Quaternion.identity;
            camera.orthographic=true;camera.orthographicSize=.6f;camera.aspect=1;
            var regions=TexturePaintRegionRenderer.Project(new[]{front,back},camera,new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},through);
            Assert.That(regions[front].Decode()[32*64+32],Is.EqualTo(255));Assert.That(regions[back].Decode()[32*64+32],Is.EqualTo(through?255:0));
        }
        [Test] public void DocumentRoundtripPreservesReferencesRegionsAndCachedMask()
        {
            var set=Set();var source=Paint(set,new Color(1,1,1,.4f));var layer=Reference(set,set,source);layer.links.mask=TexturePaintLayerReference.To(set,source);
            set.activeRegion=TexturePaintRegion.Encode(2,2,new byte[]{0,30,200,255});set.savedRegions.Add(set.activeRegion.Clone());var store=Store(set);store.RefreshLayerLinks();
            var document=Own(ScriptableObject.CreateInstance<TexturePaintDocument>());TexturePaintDocumentStorage.Save(document,store);
            Assert.That(document.surfaces[0].activeRegion.Decode(),Is.EqualTo(set.activeRegion.Decode()));Assert.That(document.surfaces[0].layers[1].cachedLinkedMask,Is.Not.Null);
            Assert.That(document.surfaces[0].layers[1].links.content.layerId,Is.EqualTo(source.id));
            TexturePaintDocumentStorage.Restore(document,store);Assert.That(set.savedRegions.Count,Is.EqualTo(1));store.RefreshLayerLinks();Assert.That(set.layers[1].linkError,Is.Null);
        }

        [Test] public void GroupReferenceIsIsolatedAndTracksChildChanges()
        {
            var set=Set();var store=Store(set);var compositor=new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));set.compositor=compositor;compositors.Add(compositor);
            set.GetChannel(TexturePaintChannel.Albedo).editable.Reset(null,Color.blue);
            var child=Paint(set,new Color(1,0,0,.5f));var group=set.AddGroup("Group");child.parentId=group.id;group.opacity=.5f;
            var reference=Reference(set,set,group);store.RefreshLayerLinks();
            Assert.That(reference.linkError,Is.Null);Assert.That(Pixel(reference).a,Is.EqualTo(.25f).Within(.01));Assert.That(Pixel(reference).b,Is.LessThan(.01));
            child.channels[TexturePaintChannel.Albedo].Reset(null,new Color(0,1,0,.8f));store.RefreshLayerLinks();Assert.That(Pixel(reference).g,Is.GreaterThan(.99));Assert.That(Pixel(reference).a,Is.EqualTo(.4f).Within(.01));set.compositor=null;
        }
        [Test] public void GroupLiveMaskWorksWithoutPaintedMask()
        {
            var set=Set();var store=Store(set);var compositor=new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));set.compositor=compositor;compositors.Add(compositor);
            var maskSource=Paint(set,new Color(1,1,1,.2f));var child=Paint(set,Color.red);var group=set.AddGroup("Group");child.parentId=group.id;
            group.links=new TexturePaintLayerLinks{mask=TexturePaintLayerReference.To(set,maskSource)};var reference=Reference(set,set,group);store.RefreshLayerLinks();
            Assert.That(reference.linkError,Is.Null);Assert.That(Pixel(reference).a,Is.EqualTo(.2f).Within(.01));set.compositor=null;
        }
        [Test] public void GroupDuplicationRemapsInternalReferences()
        {
            var set=Set();var source=Paint(set,Color.red);var reference=Reference(set,set,source);var group=set.AddGroup("Group");source.parentId=reference.parentId=group.id;
            var copy=set.DuplicateLayerAt(set.layers.IndexOf(group));var duplicate=set.layers.Find(layer=>layer.parentId==copy.id && layer.kind==TexturePaintLayerKind.Reference);
            var duplicatedSource=set.layers.Find(layer=>layer.parentId==copy.id && layer.kind==TexturePaintLayerKind.Paint);
            Assert.That(duplicate.links.content.layerId,Is.EqualTo(duplicatedSource.id));Assert.That(duplicate.links.content.layerId,Is.Not.EqualTo(source.id));
        }
        [Test] public async System.Threading.Tasks.Task PresetRemapsInternalAnchorLinksAndKeepsThemLive()
        {
            var sourceSet=Set();var source=Paint(sourceSet,Color.red);var reference=Reference(sourceSet,sourceSet,source);Store(sourceSet).RefreshLayerLinks();
            var preset=Own(ScriptableObject.CreateInstance<TexturePaintMaterialPreset>());TexturePaintMaterialPresetStorage.Capture(preset,sourceSet,new[]{source,reference},false,null);
            var destination=Set();var store=Store(destination);
            await TexturePaintMaterialPresetStorage.ApplyAsync(preset,store,new[]{destination},null,null,new TexturePaintMaterialPresetApplyOptions{wrapInGroup=false},null,System.Threading.CancellationToken.None);
            Assert.That(destination.layers[1].links.content.layerId,Is.EqualTo(destination.layers[0].id));Assert.That(destination.layers[1].linkError,Is.Null);
            destination.layers[0].channels[TexturePaintChannel.Albedo].Reset(null,Color.green);store.RefreshLayerLinks();Assert.That(Pixel(destination.layers[1]).g,Is.GreaterThan(.99));
        }
        [TestCase(false)] [TestCase(true)]
        public void NegativeFootprintMirrorsImageOrientationOnCPUAndGPU(bool gpu)
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.clear);var layer=fixture.set.AddLayer("Paint");var image=Image(Color.red);
            for(int y=8;y<16;y++)for(int x=0;x<16;x++)image.SetPixel(x,y,Color.green);image.Apply();
            var brush=Own(fixture.CreateBrush(1,1,TexturePaintBlendMode.Normal,BrushPreset.Shape.Square));brush.size=1;
            using var engine=gpu?TexturePaintGpuTestFixture.CreateEngine():new PaintingEngine(null,null,null);
            var context=fixture.CreateContext(brush,TexturePaintTool.Paint,Color.white,strength:1);context.directUV=true;context.paintSource=TexturePaintBrushSource.Texture;context.sourceTexture=image;
            Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay),Is.True);var sample=TexturePaintGpuTestFixture.CenterSample();sample.footprintScale=new Vector2(1,-1);
            engine.ApplySample(sample,1);engine.EndStroke(true);
            Assert.That(Pixel(layer,32,48).r,Is.GreaterThan(.9));Assert.That(Pixel(layer,32,16).g,Is.GreaterThan(.9));
        }
        [Test] public void SceneSelectionPreservesVerticalOrientation()
        {
            var set=Set();var camera=Own(new GameObject("Camera")).AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-2);camera.orthographic=true;camera.orthographicSize=.6f;camera.aspect=1;
            var result=TexturePaintRegionRenderer.Project(new[]{set},camera,new[]{new Vector2(0,.5f),new Vector2(1,.5f),Vector2.one,Vector2.up},false)[set].Decode();
            Assert.That(result[48*64+32],Is.EqualTo(255));Assert.That(result[16*64+32],Is.EqualTo(0));
        }
        [TestCase(false)] [TestCase(true)]
        public void ProjectedSelectionHasContinuousCoverageAtSplitUVSeams(bool through)
        {
            var mesh=Own(new Mesh
            {
                vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(0,-.5f),new Vector3(0,.5f),new Vector3(-.5f,.5f),
                    new Vector3(0,-.5f),new Vector3(.5f,-.5f),new Vector3(.5f,.5f),new Vector3(0,.5f)},
                uv=new[]{new Vector2(.125f,.125f),new Vector2(.375f,.125f),new Vector2(.375f,.875f),new Vector2(.125f,.875f),
                    new Vector2(.625f,.125f),new Vector2(.875f,.125f),new Vector2(.875f,.875f),new Vector2(.625f,.875f)},
                triangles=new[]{0,1,2,0,2,3,4,5,6,4,6,7}
            });
            mesh.RecalculateBounds();var set=Set(mesh);
            var camera=Own(new GameObject("Seam selection camera")).AddComponent<Camera>();
            camera.transform.position=new Vector3(0,0,-2);camera.orthographic=true;camera.orthographicSize=.6f;camera.aspect=1;
            var region=TexturePaintRegionRenderer.Project(new[]{set},camera,
                new[]{new Vector2(0,.5f),new Vector2(1,.5f),Vector2.one,Vector2.up},through)[set];
            var mask=Own(region.CreateTexture());
            foreach(float u in new[]{.125f,.375f,.625f,.875f})
            {
                // Match GPU bilinear sampling at the exact island edge, not its texel center.
                Assert.That(mask.GetPixelBilinear(u-.5f/64,.65f-.5f/64).r,Is.GreaterThan(.99f),"Selected island edge at "+u);
                Assert.That(mask.GetPixelBilinear(u-.5f/64,.35f-.5f/64).r,Is.LessThan(.01f),"Unselected island edge at "+u);
            }
            Assert.That(region.Decode()[40*64+32],Is.Zero,"The middle of the unused gutter must stay unselected.");
        }
        [Test] public void SelectionGutterPaddingPreservesUnselectedAndSoftSurfacePixels()
        {
            var shader=Shader.Find("Hidden/UMA/TexturePaint/RegionSelection");
            var material=Own(new Material(shader));
            var source=Own(new Texture2D(8,4,TextureFormat.RGBA32,false,true)
                {filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp});
            var pixels=new Color32[32];
            pixels[1*8+2]=new Color32(128,128,128,255);
            pixels[1*8+4]=new Color32(0,0,0,255); // An owned but unselected/occluded island.
            source.SetPixels32(pixels);source.Apply();
            var target=RenderTexture.GetTemporary(8,4,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;
            try
            {
                Graphics.Blit(source,target,material,3);
                var result=TexturePaintRegionRenderer.Read(target).Decode();
                Assert.That(result[1*8+1],Is.EqualTo(128),"Soft coverage extends into the gutter without becoming opaque.");
                Assert.That(result[1*8+2],Is.EqualTo(128),"Owned coverage must remain unchanged.");
                Assert.That(result[1*8+4],Is.Zero,"Padding must not grow across unselected surface pixels.");
                Assert.That(result[1*8+5],Is.Zero,"The closest island owns the gutter even when its coverage is zero.");
                Assert.That(result[1*8+7],Is.Zero,"Padding is bounded to the edge neighborhood.");
            }
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);}
        }
        [Test] public void SerializedEmptySelectionIsNotAnActiveSelection()
        {
            var set=Set();set.activeRegion=new TexturePaintRegion();Assert.That(set.RegionTexture,Is.Null);Assert.That(set.activeRegion.Clone(),Is.Null);
            TexturePaintDocumentStorage.RestoreCachedLinkedMask(set.AddLayer("Empty"),new TexturePaintRegion());Assert.That(set.layers[0].linkedMask,Is.Null);
        }
        [Test] public void SymmetrySettingsCloneWithoutSharingAndSurviveJson()
        {
            var paint=new TexturePaintLayerSettings{symmetry=new TexturePaintSymmetry{enabled=true,mirrorY=true,origin=Vector3.one}};
            var clone=paint.Clone();clone.symmetry.origin=Vector3.zero;Assert.That(paint.symmetry.origin,Is.EqualTo(Vector3.one));
            var path=new TexturePaintSplineSettings{symmetry=paint.symmetry.Clone()};var restored=JsonUtility.FromJson<TexturePaintSplineSettings>(JsonUtility.ToJson(path));
            Assert.That(restored.symmetry.Transforms().Count,Is.EqualTo(4));Assert.That(restored.symmetry.origin,Is.EqualTo(Vector3.one));
        }

        [Test] public void MultiTileSelectionAndMaskConversionUndoCleanly()
        {
            var first=Set();var second=Set();var store=Store(first,second);var layer=Paint(first,Color.red);
            var controller=new TexturePaintStageController();typeof(TexturePaintStageController).GetProperty("Textures").SetValue(controller,store);
            var stage=Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());typeof(TexturePaintStageWindow).GetField("controller",Private).SetValue(stage,controller);
            typeof(TexturePaintStageWindow).GetField("transientBrush",Private).SetValue(stage,Own(ScriptableObject.CreateInstance<BrushPreset>()));
            try
            {
                var region=TexturePaintRegion.Encode(2,2,new byte[]{0,0,255,255});
                Invoke(stage,"SetRegions",new Dictionary<TextureSet,TexturePaintRegion>{{first,region},{second,region}},false);
                Assert.That(first.activeRegion,Is.Not.Null);Assert.That(second.activeRegion,Is.Not.Null);
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(first.activeRegion,Is.Null);Assert.That(second.activeRegion,Is.Null);
                Assert.That(Invoke(stage,"RedoLightweight"),Is.True);Assert.That(first.activeRegion.Decode(),Is.EqualTo(region.Decode()));
                first.activeLayerIndex=first.layers.IndexOf(layer);Invoke(stage,"RegionToMask",first);Assert.That(first.layers[0].layerMask,Is.Not.Null);
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(first.layers[0],Is.SameAs(layer));Assert.That(layer.layerMask,Is.Null);
            }
            finally{Invoke(stage,"ClearLightweightHistory");typeof(TexturePaintStageWindow).GetField("controller",Private).SetValue(stage,null);}
        }

        private sealed class LinkedGeneratorProbe : ITexturePaintGeneratorV2
        {
            public int calls;
            public TexturePaintPluginDescriptor Descriptor {get;}=new TexturePaintPluginDescriptor
            {id="org.uma.tests.linked-generator",displayName="Linked Generator",capabilities=TexturePaintPluginCapability.Generator,
                declaredChannels=TexturePaintChannelMask.Albedo,supportedTargets=TexturePaintPluginTarget.LayerContent};
            public System.Threading.Tasks.Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                calls++;
                foreach(string id in context.source.surfaceIds)
                {
                    var info=context.source.GetChannelInfo(id,TexturePaintChannel.Albedo);if(info==null)continue;
                    var pixels=new Color32[info.width*info.height];Array.Fill(pixels,new Color32(0,255,0,255));
                    context.WriteTileCompact(id,TexturePaintChannel.Albedo,new RectInt(0,0,info.width,info.height),pixels,TexturePaintPluginColorSpace.Linear,TexturePaintPluginBlend.Replace);
                }
                return System.Threading.Tasks.Task.CompletedTask;
            }
        }
        [TestCase(false)] [TestCase(true)] public void AutomaticGeneratorRefreshFollowsReferenceWithoutLoopingOrAddingHistory(bool garment)
        {
            var set=Set();var source=Paint(set,Color.red);
            if(garment)
            {
                var wear=set.AddProjectionLayer();wear.projectionSettings=new TexturePaintProjectionSettings{placed=true,width=1,height=1,depth=.4f,
                    garment=TexturePaintGarmentSettings.Create(TexturePaintGarmentPreset.ThighFade)};
                wear.projectionSettings.garment.foldInput=TexturePaintLayerReference.To(set,source);
                wear.projectionSettings.garment.foldInput.component=TexturePaintReferenceComponent.Red;
            }
            else Reference(set,set,source);
            var ordinary=Paint(set,Color.clear);var output=set.AddPluginLayer("Generated");var store=Store(set);
            using var host=new PluginHost();var probe=new LinkedGeneratorProbe();((List<ITexturePaintCommandExtensionV2>)typeof(PluginHost).GetField("commands",Private).GetValue(host)).Add(probe);
            output.pluginId=probe.Descriptor.id;var controller=new TexturePaintStageController();typeof(TexturePaintStageController).GetProperty("Textures").SetValue(controller,store);typeof(TexturePaintStageController).GetProperty("Plugins").SetValue(controller,host);
            var stage=Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());typeof(TexturePaintStageWindow).GetField("controller",Private).SetValue(stage,controller);
            typeof(TexturePaintStageWindow).GetField("transientBrush",Private).SetValue(stage,Own(ScriptableObject.CreateInstance<BrushPreset>()));
            try
            {
                Assert.That(stage.WaitForLinkedOutput(()=>{}),Is.False);Assert.That(probe.calls,Is.EqualTo(1));Assert.That(host.CanUndo,Is.False);
                Assert.That(set.layers[3],Is.SameAs(output),"Automatic refresh must preserve objects referenced by undo history.");
                Assert.That(stage.WaitForLinkedOutput(()=>{}),Is.False);Assert.That(probe.calls,Is.EqualTo(1));
                source.channels[TexturePaintChannel.Albedo].Reset(null,Color.blue);
                Assert.That(stage.WaitForLinkedOutput(()=>{}),Is.False);Assert.That(probe.calls,Is.EqualTo(2));Assert.That(host.CanUndo,Is.False);
                ordinary.channels[TexturePaintChannel.Albedo].Reset(null,Color.yellow);
                Assert.That(stage.WaitForLinkedOutput(()=>{}),Is.False);Assert.That(probe.calls,Is.EqualTo(3));
                ordinary.visible=false;
                Assert.That(stage.WaitForLinkedOutput(()=>{}),Is.False);Assert.That(probe.calls,Is.EqualTo(4));
                set.GetChannel(TexturePaintChannel.Albedo).editable.Reset(null,Color.white);
                Assert.That(stage.WaitForLinkedOutput(()=>{}),Is.False);Assert.That(probe.calls,Is.EqualTo(5));
                Assert.That(stage.WaitForLinkedOutput(()=>{}),Is.False);Assert.That(probe.calls,Is.EqualTo(5));
            }
            finally{Invoke(stage,"ClearLightweightHistory");typeof(TexturePaintStageWindow).GetField("controller",Private).SetValue(stage,null);}
        }
        [Test] public void MakingLinkedMaskIndependentPreservesPixelsAndCanUndo()
        {
            var set=Set();var source=Paint(set,new Color(1,0,0,.5f));var layer=Reference(set,set,source);layer.links.mask=TexturePaintLayerReference.To(set,source);var store=Store(set);store.RefreshLayerLinks();
            var controller=new TexturePaintStageController();typeof(TexturePaintStageController).GetProperty("Textures").SetValue(controller,store);
            var stage=Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());typeof(TexturePaintStageWindow).GetField("controller",Private).SetValue(stage,controller);
            typeof(TexturePaintStageWindow).GetField("transientBrush",Private).SetValue(stage,Own(ScriptableObject.CreateInstance<BrushPreset>()));
            try
            {
                Invoke(stage,"MakeLinkedLayerIndependent",set,layer);var independent=set.layers[1];Assert.That(independent.kind,Is.EqualTo(TexturePaintLayerKind.Paint));Assert.That(independent.links.HasLinks,Is.False);
                Assert.That(TexturePaintRegionRenderer.Read(independent.layerMask.target.Front).Decode()[100],Is.EqualTo(128).Within(2));
                source.channels[TexturePaintChannel.Albedo].Reset(null,Color.blue);store.RefreshLayerLinks();Assert.That(Pixel(independent).r,Is.GreaterThan(.99));
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(set.layers[1].links.content.IsSet,Is.True);Assert.That(Pixel(set.layers[1]).b,Is.GreaterThan(.99));
            }
            finally{Invoke(stage,"ClearLightweightHistory");typeof(TexturePaintStageWindow).GetField("controller",Private).SetValue(stage,null);}
        }

        [Test] public void CrossSurfaceFillInstanceUsesDestinationResolution()
        {
            var sourceSet=Set();var source=sourceSet.AddFillLayer("Source",TexturePaintChannel.Albedo,Color.red);var destination=Set();
            destination.GetChannel(TexturePaintChannel.Albedo).editable.Dispose();destination.GetChannel(TexturePaintChannel.Albedo).editable=new EditableTextureTarget("Destination",128,128,RenderTextureFormat.ARGBHalf,null,Color.clear);
            var instance=destination.AddFillLayer("Instance",TexturePaintChannel.Albedo,Color.blue);instance.links=new TexturePaintLayerLinks{instance=TexturePaintLayerReference.To(sourceSet,source)};
            var store=Store(sourceSet,destination);store.RefreshLayerLinks();Assert.That(instance.linkError,Is.Null);
            Assert.That(instance.channels[TexturePaintChannel.Albedo].Width,Is.EqualTo(128));Assert.That(Pixel(instance,100,100).r,Is.GreaterThan(.99));
        }

        [Test] public void UVSymmetryReflectsBrushAndSampleRotationTogether()
        {
            var sample=new StrokeSample(Vector3.zero,Vector3.forward,new Vector2(.2f,.3f),0,-1){rotation=17};
            var mirror=new TexturePaintSymmetry{enabled=true}.Transforms(true)[1];
            var method=typeof(TexturePaintStageWindow).GetMethod("SymmetricUV",BindingFlags.Static|BindingFlags.NonPublic);
            var result=(StrokeSample)method.Invoke(null,new object[]{sample,mirror,30f});
            Assert.That(Mathf.DeltaAngle(result.rotation+30,133),Is.EqualTo(0).Within(.0001));
            Assert.That(result.footprintScale.y,Is.EqualTo(-1));Assert.That(result.uv.x,Is.EqualTo(.8f).Within(.0001));
        }

        private void WithStage(TextureStore store,Action<TexturePaintStageWindow> action)
        {
            var controller=new TexturePaintStageController();typeof(TexturePaintStageController).GetProperty("Textures").SetValue(controller,store);
            var stage=Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());
            typeof(TexturePaintStageWindow).GetField("controller",Private).SetValue(stage,controller);
            typeof(TexturePaintStageWindow).GetField("transientBrush",Private).SetValue(stage,Own(ScriptableObject.CreateInstance<BrushPreset>()));
            int hot=GUIUtility.hotControl;
            bool editingText = EditorGUIUtility.editingTextField;
            // Keyboard tests own a synthetic view with no text field. Do not inherit
            // the user's focused Inspector input from the live Editor.
            EditorGUIUtility.editingTextField = false;
            try { action(stage); }
            finally
            {
                Invoke(stage,"CancelRegionGesture");GUIUtility.hotControl=hot;
                EditorGUIUtility.editingTextField = editingText;
                Invoke(stage,"ClearLightweightHistory");typeof(TexturePaintStageWindow).GetField("controller",Private).SetValue(stage,null);
            }
        }
        [Test] public void DenseProceduralDependenciesHaveBoundedKeysAndStableCaches()
        {
            var set=Set();var source=Paint(set,Color.red);
            for(int i=0;i<16;i++){source=Paint(set,Color.green);source.kind=TexturePaintLayerKind.Plugin;}
            var reference=Reference(set,set,source);var store=Store(set);store.RefreshLayerLinks();
            Assert.That(reference.linkError,Is.Null);Assert.That(reference.linkSignature.Length,Is.LessThan(64));
            long revision=reference.linkRevision;store.RefreshLayerLinks();Assert.That(reference.linkRevision,Is.EqualTo(revision));
            source.channels[TexturePaintChannel.Albedo].Reset(null,Color.blue);store.RefreshLayerLinks();
            Assert.That(Pixel(reference).b,Is.GreaterThan(.99));
        }
        [Test] public void InvalidGroupDependencyBlocksEveryConsumerAndRecoversAfterRepair()
        {
            var set=Set();var child=Paint(set,Color.red);var group=set.AddGroup("Group");child.parentId=group.id;
            var first=Reference(set,set,group);var second=Reference(set,set,group);
            child.links=new TexturePaintLayerLinks{mask=new TexturePaintLayerReference{layerId="missing",surfaceId=set.persistentId}};
            var store=Store(set);store.RefreshLayerLinks();
            Assert.That(group.linkError,Is.Not.Empty);Assert.That(first.linkError,Is.Not.Empty);Assert.That(second.linkError,Is.Not.Empty);
            child.links.mask=null;store.RefreshLayerLinks();
            Assert.That(group.linkError,Is.Null);Assert.That(first.linkError,Is.Null);Assert.That(second.linkError,Is.Null);
        }
        [TestCase(TexturePaintRegionCombine.Add,128)]
        [TestCase(TexturePaintRegionCombine.Subtract,127)]
        [TestCase(TexturePaintRegionCombine.Intersect,128)]
        public void FirstSelectionCombinesWithUnrestrictedCoverage(TexturePaintRegionCombine mode,int expected)
        {
            var set=Set();WithStage(Store(set),stage=>
            {
                typeof(TexturePaintStageWindow).GetField("regionCombine",Private).SetValue(stage,mode);
                Invoke(stage,"CombineRegion",set,TexturePaintRegion.Encode(1,1,new byte[]{128}));
                Assert.That(set.activeRegion.Decode()[0],Is.EqualTo(expected));
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(set.activeRegion,Is.Null);
            });
        }
        [Test] public void InvalidSelectionDataCannotPartiallyChangeOtherTiles()
        {
            var a=Set();var b=Set();var initial=TexturePaintRegion.Encode(1,1,new byte[]{100});a.activeRegion=initial;b.activeRegion=initial.Clone();
            WithStage(Store(a,b),stage=>
            {
                typeof(TexturePaintStageWindow).GetField("regionCombine",Private).SetValue(stage,TexturePaintRegionCombine.Add);
                var invalid=new TexturePaintRegion{width=1,height=1,compressed=new byte[]{0}};
                Assert.Throws<TargetInvocationException>(()=>Invoke(stage,"SetRegions",new Dictionary<TextureSet,TexturePaintRegion>
                    {{a,TexturePaintRegion.Encode(1,1,new byte[]{255})},{b,invalid}},true));
                Assert.That(a.activeRegion.Decode()[0],Is.EqualTo(100));Assert.That(b.activeRegion.Decode()[0],Is.EqualTo(100));
                Assert.That(Invoke(stage,"UndoLightweight"),Is.False);
            });
        }
        [TestCase(TexturePaintRegionCombine.Add,new byte[]{255,255,128,128,200,200})]
        [TestCase(TexturePaintRegionCombine.Subtract,new byte[]{0,255,31,63,121,10})]
        [TestCase(TexturePaintRegionCombine.Intersect,new byte[]{0,0,32,64,78,39})]
        public void CombiningSelectionsPreservesExactPixelsAndUndo(TexturePaintRegionCombine mode,byte[] expected)
        {
            var original=new byte[]{0,255,64,128,200,50};
            var incoming=new byte[]{255,0,128,128,100,200};
            var set=Set();set.activeRegion=TexturePaintRegion.Encode(3,2,original);
            var next=TexturePaintRegion.Encode(3,2,incoming);
            WithStage(Store(set),stage=>
            {
                typeof(TexturePaintStageWindow).GetField("regionCombine",Private).SetValue(stage,mode);
                Invoke(stage,"CombineRegion",set,next);
                Assert.That(set.activeRegion.Decode(),Is.EqualTo(expected));
                Assert.That(next.Decode(),Is.EqualTo(incoming),"Combining must not mutate its input.");
                Assert.That(typeof(TextureSet).GetField("regionTexture",Private).GetValue(set),Is.Null,
                    "Combining equal-resolution regions must not create or sample a Unity texture.");
                Invoke(stage,"UndoLightweight");Assert.That(set.activeRegion.Decode(),Is.EqualTo(original));
                Invoke(stage,"RedoLightweight");Assert.That(set.activeRegion.Decode(),Is.EqualTo(expected));
            });
        }

        [TestCase(TexturePaintRegionCombine.Add,4,4)]
        [TestCase(TexturePaintRegionCombine.Subtract,4,4)]
        [TestCase(TexturePaintRegionCombine.Intersect,4,4)]
        [TestCase(TexturePaintRegionCombine.Add,1,1)]
        [TestCase(TexturePaintRegionCombine.Subtract,3,5)]
        [TestCase(TexturePaintRegionCombine.Intersect,3,5)]
        public void CombiningDifferentResolutionsKeepsPixelCenteredSoftCoverage(TexturePaintRegionCombine mode,int width,int height)
        {
            var previous=TexturePaintRegion.Encode(2,2,new byte[]{0,64,128,255});
            var texture=Own(previous.CreateTexture());
            var values=new byte[width*height];Array.Fill(values,(byte)128);
            var next=TexturePaintRegion.Encode(width,height,values);
            var actual=TexturePaintRegion.CombineRegions(previous,next,mode).Decode();
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                // GetPixelBilinear places pixel zero at UV zero, unlike GPU pixel-center sampling.
                float u=(x+.5f)/width-.5f/previous.width,v=(y+.5f)/height-.5f/previous.height;
                byte sampled=(byte)Mathf.RoundToInt(texture.GetPixelBilinear(u,v).r*255);
                Assert.That(actual[y*width+x],Is.EqualTo(TexturePaintRegion.Combine(sampled,128,mode)).Within(1));
            }
            Assert.That(previous.Decode(),Is.EqualTo(new byte[]{0,64,128,255}));
        }

        [Test] public void CorruptPreviousSelectionCannotPartiallyApplyACombine()
        {
            var a=Set();var b=Set();a.activeRegion=TexturePaintRegion.Encode(1,1,new byte[]{100});
            b.activeRegion=new TexturePaintRegion{width=1,height=1,compressed=new byte[]{0}};
            WithStage(Store(a,b),stage=>
            {
                typeof(TexturePaintStageWindow).GetField("regionCombine",Private).SetValue(stage,TexturePaintRegionCombine.Add);
                var incoming=TexturePaintRegion.Encode(1,1,new byte[]{255});
                Assert.Throws<TargetInvocationException>(()=>Invoke(stage,"SetRegions",new Dictionary<TextureSet,TexturePaintRegion>{{a,incoming},{b,incoming}},true));
                Assert.That(a.activeRegion.Decode()[0],Is.EqualTo(100));
                Assert.That(b.activeRegion.compressed,Is.EqualTo(new byte[]{0}));
                Assert.That(Invoke(stage,"UndoLightweight"),Is.False);
            });
        }
        [Test] public void ClearAllSelectionsWorksFromAnUnselectedTileAndUndoesTogether()
        {
            var a=Set();var b=Set();b.activeRegion=TexturePaintRegion.Encode(1,1,new byte[]{128});
            WithStage(Store(a,b),stage=>
            {
                Invoke(stage,"ClearAllRegions");Assert.That(b.activeRegion,Is.Null);
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(b.activeRegion.Decode()[0],Is.EqualTo(128));Assert.That(a.activeRegion,Is.Null);
            });
        }
        [Test] public void SelectionCancellationReleasesOnlyItsOwnMouseCapture()
        {
            WithStage(Store(Set()),stage=>
            {
                Invoke(stage,"BeginRegionGesture",731,false,null,new Rect(0,0,100,100),null,new Vector2(10,10));
                Assert.That(GUIUtility.hotControl,Is.EqualTo(731));
                Invoke(stage,"CancelRegionInput",new Event{type=EventType.MouseDrag,alt=true});
                Assert.That(GUIUtility.hotControl,Is.EqualTo(0));Assert.That(typeof(TexturePaintStageWindow).GetField("regionDragging",Private).GetValue(stage),Is.False);
                Invoke(stage,"BeginRegionGesture",731,false,null,new Rect(0,0,100,100),null,new Vector2(10,10));
                GUIUtility.hotControl=999;Invoke(stage,"CancelRegionGesture");Assert.That(GUIUtility.hotControl,Is.EqualTo(999));
            });
        }
        [Test] public void SelectionClickWithoutAreaCannotEraseSelection()
        {
            WithStage(Store(Set()),stage=>
            {
                var toolField=typeof(TexturePaintStageWindow).GetField("regionTool",Private);toolField.SetValue(stage,Enum.Parse(toolField.FieldType,"Rectangle"));
                Invoke(stage,"BeginRegionGesture",731,false,null,new Rect(0,0,100,100),null,new Vector2(10,10));
                Assert.That(Invoke(stage,"HasRegionArea"),Is.False);
                Invoke(stage,"UpdateRegionGesture",new Vector2(30,30));Assert.That(Invoke(stage,"HasRegionArea"),Is.True);
            });
        }
        [Test] public void DeferredActionsSurviveAnActiveGestureAndRunExactlyOnce()
        {
            WithStage(Store(Set()),stage=>
            {
                GUIUtility.hotControl=712;var completed=new List<int>();
                Assert.That(stage.WaitForLinkedOutput(()=>{if(!stage.WaitForLinkedOutput(()=>completed.Add(1)))completed.Add(1);}),Is.True);
                Assert.That(stage.WaitForLinkedOutput(()=>completed.Add(2)),Is.True);
                Invoke(stage,"ResumeAfterLinkedUpdates");Assert.That(completed,Is.Empty);
                GUIUtility.hotControl=0;Invoke(stage,"ResumeAfterLinkedUpdates");Invoke(stage,"ResumeAfterLinkedUpdates");Invoke(stage,"ResumeAfterLinkedUpdates");
                Assert.That(completed,Is.EqualTo(new[]{1,2}));
            });
        }
        [Test] public void ReferenceUndoTargetsCurrentLayerAfterGeneratorReplacement()
        {
            var set=Set();var layer=Paint(set,Color.red);
            WithStage(Store(set),stage=>
            {
                Invoke(stage,"ChangeLayerLinks",set,layer,new TexturePaintLayerLinks{anchorName="Changed"});
                var replacement=set.CloneLayer(layer,layer.name,true);set.layers[0]=replacement;
                try
                {
                    Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(replacement.links?.anchorName,Is.Null.Or.Empty);
                    Assert.That(Invoke(stage,"RedoLightweight"),Is.True);Assert.That(replacement.links.anchorName,Is.EqualTo("Changed"));
                }
                finally {layer.Dispose();}
            });
        }
        [Test] public void ProjectionSymmetryCreationIsOneUndoableOperation()
        {
            var set=Set();var source=set.AddProjectionLayer();var definition=Definition();definition.position=new Vector3(.1f,0,0);
            Generate(definition,new[]{set},new[]{set},source);
            WithStage(Store(set),stage=>
            {
                source.layerSymmetry=new TexturePaintSymmetry{enabled=true,radialCopies=2,radialAxis=Vector3.forward};
                Invoke(stage,"CreateProjectionSymmetry",set,source);Assert.That(set.layers.Count,Is.EqualTo(4));
                var ids=set.layers.ConvertAll(layer=>layer.id);
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(set.layers.Count,Is.EqualTo(1));
                Assert.That(Invoke(stage,"UndoLightweight"),Is.False);
                Assert.That(Invoke(stage,"RedoLightweight"),Is.True);Assert.That(set.layers.ConvertAll(layer=>layer.id),Is.EqualTo(ids));
                foreach(var layer in set.layers)Assert.That(layer.linkError,Is.Null);
            });
        }
        [Test] public void FailedProjectionSymmetryLeavesStackAndHistoryIntact()
        {
            var set=Set();var source=set.AddProjectionLayer();source.projectionSettings=Definition();source.projectionSettings.channel=TexturePaintChannel.Normal;
            WithStage(Store(set),stage=>
            {
                source.layerSymmetry=new TexturePaintSymmetry{enabled=true};
                Invoke(stage,"CreateProjectionSymmetry",set,source);Assert.That(set.layers.Count,Is.EqualTo(1));
                Assert.That(Invoke(stage,"UndoLightweight"),Is.False);
            });
        }

        [Test] public void AutomaticGeneratorRefreshPreservesCreationUndoAndExistingPixels()
        {
            var set=Set();var source=Paint(set,Color.red);Reference(set,set,source);var output=set.AddPluginLayer("Generated");var store=Store(set);
            using var host=new PluginHost();var probe=new LinkedGeneratorProbe();
            ((List<ITexturePaintCommandExtensionV2>)typeof(PluginHost).GetField("commands",Private).GetValue(host)).Add(probe);
            output.pluginId=probe.Descriptor.id;
            WithStage(store,stage=>
            {
                var controller=(TexturePaintStageController)typeof(TexturePaintStageWindow).GetField("controller",Private).GetValue(stage);
                typeof(TexturePaintStageController).GetProperty("Plugins").SetValue(controller,host);
                Invoke(stage,"RegisterCreatedLayer",output,"Create Plugin");
                set.activeLayerIndex=0;
                Assert.That(stage.WaitForLinkedOutput(()=>{}),Is.False);
                Assert.That(set.activeLayerIndex,Is.EqualTo(0),"Background generation must preserve the artist's active layer.");
                var target=output.channels[TexturePaintChannel.Albedo];
                source.channels[TexturePaintChannel.Albedo].Reset(null,Color.blue);
                Assert.That(stage.WaitForLinkedOutput(()=>{}),Is.False);
                Assert.That(set.activeLayerIndex,Is.EqualTo(0));
                Assert.That(output.channels[TexturePaintChannel.Albedo],Is.SameAs(target));Assert.That(target.Front,Is.Not.Null);
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(set.layers.Contains(output),Is.False);
                Assert.That(Invoke(stage,"RedoLightweight"),Is.True);Assert.That(set.layers.Contains(output),Is.True);
                Assert.That(Pixel(output).g,Is.GreaterThan(.99));
            });
        }

        [Test] public void ReferenceCreationSelectsAnAuthoredNonAlbedoChannel()
        {
            var set=Set();AddChannel(set,TexturePaintChannel.Normal);var source=set.AddLayer("Normals");
            source.channels[TexturePaintChannel.Normal]=new EditableTextureTarget("Normal",64,64,RenderTextureFormat.ARGBHalf,null,new Color(.5f,.5f,1,1));
            WithStage(Store(set),stage=>
            {
                Invoke(stage,"CreateLinkedLayer",set,source,false);var reference=set.layers[1];
                Assert.That(reference.links.content.channel,Is.EqualTo(TexturePaintChannel.Normal));
                Assert.That(reference.links.outputChannel,Is.EqualTo(TexturePaintChannel.Normal));Assert.That(reference.linkError,Is.Null);
            });
        }
        [Test] public void ProjectionNormalizesNonFiniteInputsBeforeBuildingGeometry()
        {
            var settings=Definition();settings.width=float.NaN;settings.height=float.PositiveInfinity;settings.depth=-2;
            settings.position=new Vector3(float.NaN,float.NegativeInfinity,0);settings.rotation=new Quaternion(float.NaN,0,0,0);
            settings.points[0]=new Vector3(float.NaN,0,float.PositiveInfinity);settings.edgeWidth=float.NaN;settings.depthFade=float.NaN;
            settings.angleStart=float.NaN;settings.angleEnd=float.NaN;settings.visibilityTolerance=float.NaN;settings.Normalize();
            Assert.That(settings.width,Is.GreaterThan(0));Assert.That(settings.height,Is.LessThan(float.PositiveInfinity));Assert.That(settings.depth,Is.GreaterThan(0));
            Assert.That(settings.rotation,Is.EqualTo(Quaternion.identity));Assert.That(settings.position,Is.EqualTo(Vector3.zero));
            settings.mode=TexturePaintProjectionMode.Wrapped;Vector3 point=settings.Evaluate(0,0);
            Assert.That(float.IsFinite(point.x)&&float.IsFinite(point.y)&&float.IsFinite(point.z),Is.True);
            Assert.That(float.IsFinite(settings.edgeWidth)&&float.IsFinite(settings.depthFade)&&float.IsFinite(settings.angleEnd),Is.True);
        }

        [Test] public void CorruptSelectionBlocksPaintWithoutBreakingTheEditorAndCanBeCleared()
        {
            var set=Set();var corrupted=new TexturePaintRegion{width=2,height=2,compressed=new byte[]{0}};set.activeRegion=corrupted;
            Assert.DoesNotThrow(()=>{_ = set.RegionTexture;});Assert.That(set.RegionError,Does.Contain("could not be read"));
            Assert.That(set.RegionTexture.GetPixel(0,0).r,Is.Zero);Assert.That(set.activeRegion,Is.SameAs(corrupted));
            var cached=set.RegionTexture;Assert.That(set.RegionTexture,Is.SameAs(cached));
            WithStage(Store(set),stage=>
            {
                Invoke(stage,"ClearAllRegions");Assert.That(set.RegionTexture,Is.Null);Assert.That(set.RegionError,Is.Null);
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);Assert.That(set.RegionTexture.GetPixel(0,0).r,Is.Zero);
            });
        }
        [Test] public void SelectionAndProjectionModeTransitionsDoNotLeaveCompetingToolsArmed()
        {
            var set=Set();var layer=set.AddProjectionLayer();
            WithStage(Store(set),stage=>
            {
                var field=typeof(TexturePaintStageWindow).GetField("regionTool",Private);var rectangle=Enum.Parse(field.FieldType,"Rectangle");
                var geometry=typeof(TexturePaintStageWindow).GetField("geometryFillMode",Private);
                geometry.SetValue(stage,1);
                Invoke(stage,"SetRegionTool",rectangle);
                Assert.That(geometry.GetValue(stage),Is.EqualTo(0));
                var escape=new Event{type=EventType.KeyDown,keyCode=KeyCode.Escape};
                Assert.That(Invoke(stage,"HandleWorkspaceShortcuts",escape,true,false),Is.True);
                Assert.That(field.GetValue(stage).ToString(),Is.EqualTo("Paint"));
                Invoke(stage,"SetRegionTool",rectangle);Invoke(stage,"AddProjectionLayer",set);
                Assert.That(field.GetValue(stage).ToString(),Is.EqualTo("Paint"));
                Assert.That(set.layers[set.activeLayerIndex].projectionSettings.placed,Is.True);
            });
        }
        private sealed class LinkedMaskProbe : ITexturePaintGeneratorV2
        {
            public TexturePaintPluginDescriptor Descriptor {get;}=new TexturePaintPluginDescriptor
            {id="org.uma.tests.linked-mask",displayName="Linked Mask",capabilities=TexturePaintPluginCapability.Generator,
                declaredChannels=TexturePaintChannelMask.Albedo,supportedTargets=TexturePaintPluginTarget.LayerMask};
            public System.Threading.Tasks.Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                foreach(string id in context.source.surfaceIds)
                {
                    var mask=context.source.GetMask(id);var pixels=new Color32[mask.width*mask.height];
                    Array.Fill(pixels,new Color32(128,128,128,255));context.WriteMaskTileCompact(id,new RectInt(0,0,mask.width,mask.height),pixels);
                }
                return System.Threading.Tasks.Task.CompletedTask;
            }
        }
        [Test] public async System.Threading.Tasks.Task AutomaticMaskGenerationPreservesLayerMaskAndTargetIdentity()
        {
            var set=Set();var layer=Paint(set,Color.red);var mask=set.AddLayerMask(layer,1);var target=mask.target;
            var store=Store(set);using var host=new PluginHost();var plugin=new LinkedMaskProbe();set.activeLayerIndex=-1;
            await host.ExecuteLayerMaskAsync(plugin,store,host.CreateParameters(plugin),new Dictionary<TextureSet,TexturePaintLayer>{{set,layer}},
                null,System.Threading.CancellationToken.None,false);
            Assert.That(set.activeLayerIndex,Is.EqualTo(-1));Assert.That(set.layers[0],Is.SameAs(layer));Assert.That(layer.layerMask,Is.SameAs(mask));Assert.That(mask.target,Is.SameAs(target));
            Assert.That(TexturePaintRegionRenderer.Read(target.Front).Decode()[0],Is.EqualTo(128));Assert.That(host.CanUndo,Is.False);
            Assert.That(Pixel(layer).r,Is.GreaterThan(.99));
        }
        [TestCase(TexturePaintLayerKind.Paint)]
        [TestCase(TexturePaintLayerKind.Spline)]
        [TestCase(TexturePaintLayerKind.Fill)]
        [TestCase(TexturePaintLayerKind.Projection)]
        [TestCase(TexturePaintLayerKind.Plugin)]
        public void UVSelectionEventsCancelSafelyAndUseTheMouseUpPosition(TexturePaintLayerKind kind)
        {
            var a=Set();var b=Set();Paint(a,Color.red);
            a.layers[0].kind=kind;
            WithStage(Store(a,b),stage=>
            {
                var field=typeof(TexturePaintStageWindow).GetField("regionTool",Private);field.SetValue(stage,Enum.Parse(field.FieldType,"Rectangle"));
                var window=Own(ScriptableObject.CreateInstance<TexturePaintRegionInputTestWindow>());
                TextureSet current=a;var canvas=new Rect(0,0,100,100);
                window.draw=()=>Invoke(stage,"HandleRegionUV",canvas,canvas,current);
                try
                {
                    window.position=new Rect(100,100,160,160);window.ShowUtility();window.Focus();
                    void Send(EventType type,float x,float y,bool alt=false)
                        =>window.SendEvent(new Event{type=type,button=0,mousePosition=new Vector2(x,y),alt=alt});
                    Send(EventType.MouseDown,10,10);Send(EventType.MouseUp,10,10);
                    Assert.That(a.activeRegion,Is.Null,"A click must not replace the region with empty coverage.");
                    Send(EventType.MouseDown,10,10);Send(EventType.MouseDrag,70,70,true);
                    Assert.That(typeof(TexturePaintStageWindow).GetField("regionDragging",Private).GetValue(stage),Is.False);
                    Send(EventType.MouseDown,10,10);current=b;Send(EventType.MouseUp,70,70);
                    Assert.That(a.activeRegion,Is.Null);Assert.That(b.activeRegion,Is.Null,"Changing tiles cancels an in-flight gesture.");
                    current=a;Send(EventType.MouseDown,10,10);Send(EventType.MouseUp,70,70);
                    Assert.That(a.activeRegion,Is.Not.Null,"Mouse-up must complete the rectangle even without an intermediate drag event.");
                    Assert.That(a.activeRegion.Decode()[32*64+32],Is.EqualTo(255));
                    window.SendEvent(new Event{type=EventType.KeyDown,keyCode=KeyCode.Escape});
                    Assert.That(field.GetValue(stage).ToString(),Is.EqualTo("Paint"));Assert.That(a.activeRegion,Is.Not.Null);
                }
                finally{window.draw=null;window.Close();}
            });
        }

        [TestCase(TexturePaintLayerKind.Paint,true)]
        [TestCase(TexturePaintLayerKind.Spline,true)]
        [TestCase(TexturePaintLayerKind.Fill,true)]
        [TestCase(TexturePaintLayerKind.Projection,true)]
        [TestCase(TexturePaintLayerKind.Plugin,true)]
        [TestCase(TexturePaintLayerKind.Group,false)]
        [TestCase(TexturePaintLayerKind.Reference,false)]
        public void SelectionToolbarTracksSupportedModesAndRetainsClearForStoredSelections(TexturePaintLayerKind kind,bool supported)
        {
            var set=Set();var layer=Paint(set,Color.red);layer.kind=kind;
            WithStage(Store(set),stage=>
            {
                Assert.That(TexturePaintStageWindow.SupportsRegionSelection(layer,false),Is.EqualTo(supported));
                Assert.That(TexturePaintStageWindow.SupportsRegionSelection(layer,true),Is.True,"Painting any layer mask accepts selections.");
                Assert.That(stage.SelectionToolbarVisible,Is.EqualTo(supported));
                set.activeRegion=TexturePaintRegion.Encode(1,1,new byte[]{128});
                Assert.That(stage.SelectionToolbarVisible,Is.True);
                var field=typeof(TexturePaintStageWindow).GetField("regionTool",Private);
                field.SetValue(stage,Enum.Parse(field.FieldType,"Lasso"));
                Invoke(stage,"SuspendRegionToolForNonPaintLayer",set);
                Assert.That(field.GetValue(stage).ToString(),Is.EqualTo(supported?"Lasso":"Paint"));
                Invoke(stage,"ClearAllRegions");Assert.That(stage.SelectionToolbarVisible,Is.EqualTo(supported));
                Invoke(stage,"UndoLightweight");Assert.That(stage.SelectionToolbarVisible,Is.True);
            });
        }

        [TestCase(TexturePaintLayerKind.Fill)]
        [TestCase(TexturePaintLayerKind.Projection)]
        [TestCase(TexturePaintLayerKind.Plugin)]
        public void SelectionConversionReplacesLogicalLayerMasksAndUndoRestoresBothTiles(TexturePaintLayerKind kind)
        {
            var a=Set();var b=Set();var first=Paint(a,Color.red);var second=Paint(b,Color.green);
            first.kind=second.kind=kind;
            a.InitializeLayerMask(first,.8f);b.InitializeLayerMask(second,.6f);
            a.activeRegion=TexturePaintRegion.Encode(1,1,new byte[]{64});
            b.activeRegion=TexturePaintRegion.Encode(1,1,new byte[]{192});
            WithStage(Store(a,b),stage=>
            {
                var controller=(TexturePaintStageController)typeof(TexturePaintStageWindow).GetField("controller",Private).GetValue(stage);
                var reconstruction=new MeshReconstructionResult();
                reconstruction.logicalTargets.Rebuild(new[]{a.surface});
                var target=reconstruction.logicalTargets.Targets[0];target.isUdim=true;
                target.members[0].udimTileNumber=1001;target.members[0].textureSets.Add(a);
                var member=new TexturePaintLogicalTargetMember{slotName="Body",udimTileNumber=1002};
                member.textureSets.Add(b);target.members.Add(member);
                first.paintTargetId=second.paintTargetId=target.id;first.logicalLayerId=second.logicalLayerId="selection-layer";
                typeof(TexturePaintStageController).GetProperty("Reconstruction").SetValue(controller,reconstruction);
                typeof(TexturePaintStageController).GetProperty("LogicalLayers").SetValue(controller,new TexturePaintLogicalLayerController(reconstruction.logicalTargets));
                Assert.That(Invoke(stage,"CanConvertRegionToMask",a),Is.True);
                Invoke(stage,"RegionToMask",a);
                Assert.That(TexturePaintRegionRenderer.Read(a.layers[0].layerMask.target.Front).Decode()[0],Is.EqualTo(64).Within(1));
                Assert.That(TexturePaintRegionRenderer.Read(b.layers[0].layerMask.target.Front).Decode()[0],Is.EqualTo(192).Within(1));
                Assert.That(a.activeRegion.Decode()[0],Is.EqualTo(64));
                Assert.That(Invoke(stage,"UndoLightweight"),Is.True);
                Assert.That(a.layers[0],Is.SameAs(first));Assert.That(b.layers[0],Is.SameAs(second));
                Assert.That(TexturePaintRegionRenderer.Read(first.layerMask.target.Front).Decode()[0],Is.EqualTo(204).Within(1));
                Assert.That(TexturePaintRegionRenderer.Read(second.layerMask.target.Front).Decode()[0],Is.EqualTo(153).Within(1));
                Assert.That(Invoke(stage,"RedoLightweight"),Is.True);
                Assert.That(TexturePaintRegionRenderer.Read(b.layers[0].layerMask.target.Front).Decode()[0],Is.EqualTo(192).Within(1));
            });
        }
    }
    internal sealed class TexturePaintRegionInputTestWindow : EditorWindow
    {
        internal Action draw;
        private void OnGUI() {draw?.Invoke();}
    }
}
#endif
