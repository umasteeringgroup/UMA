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
    public sealed class TexturePaintVDMTests
    {
        private readonly List<Object> objects = new List<Object>();
        private const string Folder = "Assets/UMAProjectData/Tests/OverlayPainter/GeneratedVDMTests";
        private T Own<T>(T item) where T : Object { objects.Add(item); return item; }
        private Texture2D Image(Color value, TextureFormat format = TextureFormat.RGBAFloat)
        {
            var image = Own(new Texture2D(8,8,format,false,true));
            var pixels = new Color[64]; Array.Fill(pixels,value);
            image.SetPixels(pixels); image.Apply(); return image;
        }
        private TexturePaintVDMApplicationSettings Application(Color raw)
        {
            var brush = Own(ScriptableObject.CreateInstance<TexturePaintVDMBrush>());
            brush.vectorTexture = Image(raw);
            return brush.CreateApplication();
        }
        private TexturePaintChannelSourceSettings Source(TexturePaintVDMApplicationSettings application) =>
            new TexturePaintChannelSourceSettings { vdm = application };
        [TearDown] public void TearDown()
        {
            TexturePaintVDMSource.ClearCache();
            foreach (var item in objects) if (item != null && !EditorUtility.IsPersistent(item)) Object.DestroyImmediate(item);
            objects.Clear();
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }

        [TestCase(0f)] [TestCase(-2f)] [TestCase(3f)]
        public void SignedHeightSurvivesGPUConversionWithoutLuminanceOrClamping(float height)
        {
            var settings = Application(new Color(12,-17,height,1));
            Texture2D result = TexturePaintVDMSource.Resolve(settings);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.format, Is.EqualTo(TextureFormat.RGBAFloat));
            Color pixel = result.GetPixel(3,3);
            Assert.That(pixel.r, Is.EqualTo(.5f+height).Within(.00001));
            Assert.That(pixel.g, Is.EqualTo(pixel.r));
            Assert.That(pixel.b, Is.EqualTo(pixel.r));
            Assert.That(settings.Decode(new Color(12,-17,height)), Is.EqualTo(new Vector3(12,-17,height)));
            Assert.That(TexturePaintVDMSource.Resolve(settings), Is.SameAs(result));
        }

        [Test]
        public void DecodeSnapshotSupportsAxesOffsetScaleAndFutureVectorOutput()
        {
            var settings = Application(Color.clear);
            settings.neutral = Vector3.one * .5f; settings.componentScale = Vector3.one * 2;
            settings.uAxis = Vector3.up; settings.vAxis = -Vector3.right; settings.nAxis = Vector3.forward;
            settings.amplitude = 2; settings.reliefEncodingScale = 3;
            Color raw = new Color(1,2,-1);
            Assert.That(settings.Decode(raw),Is.EqualTo(new Vector3(3,-1,-3)));
            Assert.That(settings.Displacement(raw,Vector3.right,Vector3.up,Vector3.forward), Is.EqualTo(new Vector3(6,-2,-6)));
            Assert.That(settings.EncodeHeight(raw), Is.EqualTo(-17.5f));
        }

        [TestCase(TexturePaintVDMCoverage.Footprint,1f)]
        [TestCase(TexturePaintVDMCoverage.SourceAlpha,.25f)]
        [TestCase(TexturePaintVDMCoverage.MaskRed,.125f)]
        public void CoverageIsIndependentOfSignedHeight(TexturePaintVDMCoverage mode,float expected)
        {
            var settings = Application(new Color(0,0,-1,.25f));
            settings.coverage = mode; settings.coverageMask = Image(new Color(.125f,1,1,1));
            Color pixel = TexturePaintVDMSource.Resolve(settings).GetPixel(3,3);
            Assert.That(pixel.r, Is.EqualTo(-.5f).Within(.00001));
            Assert.That(pixel.a, Is.EqualTo(expected).Within(.00001));
        }

        [Test]
        public void ByteTexturesAndStaleOrMissingSourcesAreRejected()
        {
            var settings = Application(Color.black);
            settings.vectorTexture = Image(Color.black,TextureFormat.RGBA32);
            Assert.That(TexturePaintVDMSource.Validate(settings,out _),Is.False);
            settings.vectorTexture = Image(Color.black); settings.sourceRevision = "old-revision";
            Assert.That(TexturePaintVDMSource.Validate(settings,out string error),Is.False);
            StringAssert.Contains("changed",error);
            settings.vectorTexture = null;
            Assert.That(TexturePaintVDMSource.Validate(settings,out _),Is.False);
        }

        [TestCase(TexturePaintProjectionMode.Planar)]
        [TestCase(TexturePaintProjectionMode.Wrapped)]
        public void ProjectionWritesHDRHeightWithIndependentAlpha(TexturePaintProjectionMode mode)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear,TexturePaintChannel.NormalControl);
            var layer = fixture.set.AddLayer("VDM projection");
            var settings = new TexturePaintProjectionSettings
            {
                placed=true,position=new Vector3(.5f,.5f,0),width=1,height=1,depth=1,
                mode=mode,fade=TexturePaintProjectionFade.None,depthFade=0,firstSurfaceOnly=false,connectedSurfaceOnly=false,
                channelSourcesVersion=1,
                channelSources=new List<TexturePaintProjectionChannelSource> {
                    new TexturePaintProjectionChannelSource { channel=TexturePaintChannel.NormalControl,
                        vdm=Application(new Color(10,-10,1.5f,.25f)) } }
            };
            settings.channelSources[0].vdm.coverage=TexturePaintVDMCoverage.SourceAlpha;
            using var renderer = new TexturePaintProjectionRenderer();
            Assert.That(renderer.Generate(new[]{fixture.set},new[]{fixture.set},new[]{layer},settings,out var error),Is.True,error);
            var pixel = TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.NormalControl].Front)[32*64+32];
            Assert.That(pixel.r,Is.EqualTo(2).Within(.005));
            Assert.That(pixel.a,Is.EqualTo(.25f).Within(.005));
            var clone=layer.projectionSettings.Clone(); clone.channelSources[0].vdm.amplitude=99;
            Assert.That(layer.projectionSettings.channelSources[0].vdm.amplitude,Is.EqualTo(1));
        }

        [TestCase(TexturePaintPathMode.Stamps)] [TestCase(TexturePaintPathMode.Ribbon)]
        public void PathUsesSignedVDMSourceAndIgnoresTransparentAlbedoCoverage(TexturePaintPathMode mode)
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.clear,TexturePaintChannel.NormalControl);
            var layer=fixture.set.AddSplineLayer("VDM path");
            layer.splineSettings=new TexturePaintSplineSettings { pathMode=mode };
            var brush=Own(fixture.CreateBrush(1,1,shape:BrushPreset.Shape.Square)); brush.size=.4f;
            using var engine=TexturePaintGpuTestFixture.CreateEngine();
            var context=fixture.CreateContext(brush,TexturePaintTool.Paint,Color.white,TexturePaintChannel.NormalControl,1);
            context.derivedLayerRaster=true; context.replaceLayer=layer; context.directUV=true; context.projectionDepth=1;
            var settings=Application(new Color(10,-10,-1.25f,.25f)); settings.coverage=TexturePaintVDMCoverage.SourceAlpha;
            context.channelSources[TexturePaintChannel.NormalControl]=Source(settings);
            context.channelSources[TexturePaintChannel.Albedo]=new TexturePaintChannelSourceSettings { sourceTexture=Image(Color.clear) };
            Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay),Is.True,engine.LastStrokeError);
            if (mode==TexturePaintPathMode.Stamps)
                Assert.That(engine.ApplySample(TexturePaintGpuTestFixture.CenterSample(),.3f),Is.True);
            else
            {
                var samples=new[]{new StrokeSample(new Vector3(.2f,.5f,0),Vector3.forward,new Vector2(.2f,.5f),0,0),
                    new StrokeSample(new Vector3(.8f,.5f,0),Vector3.forward,new Vector2(.8f,.5f),0,1)};
                var segments=TexturePaintStageWindow.BuildRibbonSegments(samples,.4f,1);
                Assert.That(engine.ApplyRibbon(segments,samples,false,false,false,true),Is.True);
            }
            engine.EndStroke(false);
            var pixel=TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.NormalControl].Front)[32*64+32];
            Assert.That(pixel.r,Is.EqualTo(-.75f).Within(.01));
            Assert.That(pixel.a,Is.EqualTo(.25f).Within(.01));
        }

        [TestCase(TexturePaintPathMode.Continuous)] [TestCase(TexturePaintPathMode.Filled)]
        public void UnsupportedPathModePreservesPixels(TexturePaintPathMode mode)
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.clear,TexturePaintChannel.NormalControl);
            var layer=fixture.set.AddSplineLayer("VDM path"); layer.splineSettings=new TexturePaintSplineSettings{pathMode=mode};
            var brush=Own(fixture.CreateBrush()); using var engine=TexturePaintGpuTestFixture.CreateEngine();
            var context=fixture.CreateContext(brush,TexturePaintTool.Paint,Color.white,TexturePaintChannel.NormalControl);
            context.derivedLayerRaster=true; context.replaceLayer=layer;
            context.channelSources[TexturePaintChannel.NormalControl]=Source(Application(Color.blue));
            Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay),Is.False);
            StringAssert.Contains("Stamps or Ribbon",engine.LastStrokeError);
        }

        [Test]
        public void DocumentAndProjectionRoundTripKeepFullVectorSnapshot()
        {
            UMAPathUtility.EnsureAssetFolder(Folder);
            var application=Application(new Color(-1,2,3));
            AssetDatabase.CreateAsset(application.vectorTexture,Folder+"/Vector.asset");
            AssetDatabase.CreateAsset(application.brush,Folder+"/Brush.asset");
            application.amplitude=2; application.reliefEncodingScale=7; application.nAxis=-Vector3.up;
            var channel=new TexturePaintDocumentLayerChannel { channel=TexturePaintChannel.NormalControl };
            channel.SetSourceSettings(Source(application));
            var layer=new TexturePaintDocumentLayer { kind=TexturePaintLayerKind.Projection };
            layer.channels.Add(channel);
            layer.projectionSettings=new TexturePaintProjectionSettings {channelSourcesVersion=1};
            var projection=layer.projectionSettings.GetChannelSource(TexturePaintChannel.NormalControl,true);
            projection.ApplyChannelSettings(Source(application));
            var surface=new TexturePaintDocumentSurface(); surface.layers.Add(layer);
            var document=ScriptableObject.CreateInstance<TexturePaintDocument>(); document.surfaces.Add(surface);
            string path=Folder+"/Document.asset"; AssetDatabase.CreateAsset(document,path); AssetDatabase.SaveAssets();
            Resources.UnloadAsset(document);
            var restored=AssetDatabase.LoadAssetAtPath<TexturePaintDocument>(path).surfaces[0].layers[0];
            var saved=restored.channels[0].GetSourceSettings();
            Assert.That(saved.vdm.vectorTexture,Is.EqualTo(application.vectorTexture));
            Assert.That(saved.vdm.nAxis,Is.EqualTo(-Vector3.up));
            Assert.That(saved.vdm.Decode(new Color(-1,2,3)),Is.EqualTo(new Vector3(-1,2,-2)));
            Assert.That(saved.vdm.reliefEncodingScale,Is.EqualTo(7));
            Assert.That(restored.projectionSettings.channelSources[0].vdm.amplitude,Is.EqualTo(2));
            var clone=saved.Clone(); clone.vdm.amplitude=99;
            Assert.That(saved.vdm.amplitude,Is.EqualTo(2));
        }

        [Test]
        public void ImportCopyPreservesSignedEXRAndLeavesInputUntouched()
        {
            UMAPathUtility.EnsureAssetFolder(Folder);
            var input=Image(new Color(-2,3,-.75f,1));
            byte[] bytes=input.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
            string source=Folder+"/input.exr"; System.IO.File.WriteAllBytes(source,bytes);
            var brush=TexturePaintVDMImport.ImportCopy(source,Folder+"/Imported.asset");
            Assert.That(System.IO.File.ReadAllBytes(source),Is.EqualTo(bytes));
            Assert.That(brush.vectorTexture.format,Is.EqualTo(TextureFormat.RGBAFloat));
            Assert.That(brush.vectorTexture.isDataSRGB,Is.False);
            Assert.That(TexturePaintVDMSource.Resolve(brush.CreateApplication()).GetPixel(3,3).r,Is.EqualTo(-.25f).Within(.00001));
            Assert.That(brush.sourceGuid,Is.Not.Empty);
        }

        [Test]
        public void SignedFloatRasterSurvivesDocumentPixelCompression()
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.clear,TexturePaintChannel.NormalControl);
            var height=TexturePaintVDMSource.Resolve(Application(new Color(8,-9,-1.25f,1)));
            fixture.target.Reset(height,Color.clear);
            var capture=typeof(TexturePaintDocumentStorage).GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic);
            var pixels=(TexturePaintPixelData)capture.Invoke(null,new object[]{fixture.target.Front,false});
            Assert.That(pixels.textureFormat,Is.EqualTo(TextureFormat.RGBAHalf));
            using var restored=new EditableTextureTarget("VDM restored",64,64,RenderTextureFormat.ARGBHalf,null,Color.clear);
            var restore=typeof(TexturePaintDocumentStorage).GetMethod("Restore",BindingFlags.Static|BindingFlags.NonPublic,
                null,new[]{typeof(TexturePaintPixelData),typeof(EditableTextureTarget)},null);
            restore.Invoke(null,new object[]{pixels,restored});
            Assert.That(TexturePaintGpuTestFixture.ReadPixels(restored.Front)[32*64+32].r,Is.EqualTo(-.75f).Within(.001));
        }

        [Test]
        public void ImageFlipChangesSamplePositionWithoutChangingDisplacementSign()
        {
            var settings=Application(Color.black);
            var pixels=new Color[64];
            for(int y=0;y<8;y++) for(int x=0;x<8;x++) pixels[y*8+x]=new Color(x,y,y<4?-.25f:.75f,1);
            settings.vectorTexture.SetPixels(pixels); settings.vectorTexture.Apply();
            settings.sourceRevision=TexturePaintVDMSource.Revision(settings.vectorTexture);
            Assert.That(TexturePaintVDMSource.Resolve(settings).GetPixel(2,1).r,Is.EqualTo(.25f).Within(.001));
            settings.flipImageY=true;
            Assert.That(TexturePaintVDMSource.Resolve(settings).GetPixel(2,1).r,Is.EqualTo(1.25f).Within(.001));
        }

        [Test]
        public void ProjectionCanSelectVDMTypeBeforeAssigningBrush()
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.clear,TexturePaintChannel.NormalControl);
            var layer=fixture.set.AddLayer("Unassigned VDM");
            var settings=new TexturePaintProjectionSettings {channelSourcesVersion=1};
            settings.channelSources.Add(new TexturePaintProjectionChannelSource {
                channel=TexturePaintChannel.NormalControl,vdm=new TexturePaintVDMApplicationSettings {enabled=true}});
            using var renderer=new TexturePaintProjectionRenderer();
            Assert.That(renderer.Generate(new[]{fixture.set},new[]{fixture.set},new[]{layer},settings,out var error),Is.True,error);
            Assert.That(layer.projectionSettings.channelSources[0].vdm.enabled,Is.True);
            Assert.That(layer.channels.ContainsKey(TexturePaintChannel.NormalControl),Is.True);
            settings.channelSources[0].vdm=Application(Color.blue);
            Assert.That(renderer.Generate(new[]{fixture.set},new[]{fixture.set},new[]{layer},settings,out error),Is.True,error);
        }

        [Test]
        public void PathSourcePopulationDoesNotDropVDMWithNoOrdinaryTexture()
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.clear,TexturePaintChannel.NormalControl);
            var layer=fixture.set.AddSplineLayer("VDM path");
            fixture.set.GetPaintTarget(TexturePaintChannel.NormalControl,TexturePaintSourceMode.SourceOverlay);
            layer.GetChannelSettings(TexturePaintChannel.NormalControl).sourceSettings=Source(Application(Color.blue));
            var context=new StrokeContext(); TexturePaintStageWindow.PopulateLayerChannelSources(context,layer);
            Assert.That(context.channelSources.ContainsKey(TexturePaintChannel.NormalControl),Is.True);
            Assert.That(context.channelSources[TexturePaintChannel.NormalControl].vdm.vectorTexture,Is.Not.Null);
        }
    }
}
#endif
