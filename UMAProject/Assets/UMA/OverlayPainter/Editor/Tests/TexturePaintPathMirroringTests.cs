#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintPathMirroringTests
    {
        private static Texture2D Source(bool normal)
        {
            var image = new Texture2D(16, 16, TextureFormat.RGBAFloat, false, true);
            image.filterMode = FilterMode.Point; image.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            {
                float nx = x < 8 ? .1f : .4f, ny = y < 8 ? .2f : .5f;
                image.SetPixel(x, y, normal
                    ? new Color(nx * .5f + .5f, ny * .5f + .5f, Mathf.Sqrt(1 - nx * nx - ny * ny) * .5f + .5f, 1)
                    : new Color(x < 8 ? .2f : .8f, y < 8 ? .3f : .7f, .4f, x < 8 ? .4f : .8f));
            }
            image.Apply(); return image;
        }

        private static void EqualPixel(Color actual, Color expected, string label)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(.025f), label + " R");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(.025f), label + " G");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(.025f), label + " B");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(.015f), label + " alpha");
        }

        private static TexturePaintRibbonSegment MirrorRibbon(TexturePaintRibbonSegment segment)
        {
            Vector4 Point(Vector4 p) => new Vector4(1f - p.x, p.y, p.z, p.w);
            Vector4 Direction(Vector4 p) => new Vector4(-p.x, p.y, p.z, p.w);
            segment.leftStartAlong = Point(segment.leftStartAlong);
            segment.rightStartFlow = Point(segment.rightStartFlow);
            segment.leftEndAlong = Point(segment.leftEndAlong);
            segment.rightEndFlow = Point(segment.rightEndFlow);
            segment.normalStartPressure = Direction(segment.normalStartPressure);
            segment.normalEndPressure = Direction(segment.normalEndPressure);
            return segment;
        }

        private static Color Bilinear(Color[] pixels, Vector2 uv)
        {
            float x = uv.x * 64 - .5f, y = uv.y * 64 - .5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            return Color.Lerp(Color.Lerp(pixels[y0 * 64 + x0], pixels[y0 * 64 + x0 + 1], x - x0),
                Color.Lerp(pixels[(y0 + 1) * 64 + x0], pixels[(y0 + 1) * 64 + x0 + 1], x - x0), y - y0);
        }

        [TestCase(TexturePaintChannel.Albedo, false)] [TestCase(TexturePaintChannel.Albedo, true)]
        [TestCase(TexturePaintChannel.Normal, false)] [TestCase(TexturePaintChannel.Normal, true)]
        [TestCase(TexturePaintChannel.Roughness, false)] [TestCase(TexturePaintChannel.Roughness, true)]
        public void RibbonCrossingSplitUVIslandsHasNoTransparentSeamAndPreservesSourceHoles(
            TexturePaintChannel channel, bool mirrored)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear, channel);
            // The mesh is continuous at x=.5, but its two halves occupy disjoint UV islands.
            // Their shared world edge sits halfway between texel centres, like a clothing seam.
            fixture.mesh.Clear();
            fixture.mesh.vertices = new[] { new Vector3(0,0),new Vector3(.5f,0),new Vector3(.5f,1),new Vector3(0,1),
                new Vector3(.5f,0),new Vector3(1,0),new Vector3(1,1),new Vector3(.5f,1) };
            fixture.mesh.normals = new[] { Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward,
                Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward };
            fixture.mesh.uv = new[] { new Vector2(.09375f,.09375f),new Vector2(.40625f,.09375f),
                new Vector2(.40625f,.90625f),new Vector2(.09375f,.90625f),new Vector2(.59375f,.09375f),
                new Vector2(.90625f,.09375f),new Vector2(.90625f,.90625f),new Vector2(.59375f,.90625f) };
            fixture.mesh.subMeshCount = 2;
            fixture.mesh.SetTriangles(new[] {0,1,2,0,2,3},0);
            fixture.mesh.SetTriangles(new[] {4,5,6,4,6,7},1);
            fixture.mesh.RecalculateBounds();
            var brush = fixture.CreateBrush(1,1,shape:BrushPreset.Shape.Square); brush.size=.3f;
            var source = new Texture2D(16,16,TextureFormat.RGBAFloat,false,true)
                { filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp };
            var paint = channel == TexturePaintChannel.Normal ? new Color(.65f,.7f,.9330127f,.65f)
                : channel == TexturePaintChannel.Roughness ? new Color(.35f,.35f,.35f,.65f)
                : new Color(.15f,.35f,.7f,.65f);
            for(int y=0;y<16;y++) for(int x=0;x<16;x++)
            { Color pixel=paint; if(y>=7&&y<=8) pixel.a=0; source.SetPixel(x,y,pixel); }
            source.Apply();
            var layer=fixture.set.AddLayer("Ribbon over split UVs");
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            using var engine=new PaintingEngine(null,null,null,shader);
            try
            {
                var context=fixture.CreateContext(brush,TexturePaintTool.Paint,Color.white,channel,1);
                context.paintSource=TexturePaintBrushSource.Texture; context.sourceTexture=source; context.projectionDepth=1;
                var samples=new[]{new StrokeSample(new Vector3(.1f,.5f,0),Vector3.forward,new Vector2(.1f,.5f),0,0),
                    new StrokeSample(new Vector3(mirrored?.56f:.9f,.5f,0),Vector3.forward,new Vector2(.9f,.5f),0,1)};
                var segments=TexturePaintStageWindow.BuildRibbonSegments(samples,brush.size,1);
                if(mirrored) segments.Add(MirrorRibbon(segments[0]));
                Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay),Is.True);
                Assert.That(engine.ApplyRibbon(segments,samples,false,false),Is.True);
                var pixels=TexturePaintGpuTestFixture.ReadPixels(layer.channels[channel].Front);
                foreach(float seamUV in new[]{.40625f,.59375f})
                {
                    EqualPixel(Bilinear(pixels,new Vector2(seamUV,.6f)),paint,
                        "Bilinear sampling at the world seam must retain source opacity on both islands");
                    Assert.That(Bilinear(pixels,new Vector2(seamUV,.5f)).a,Is.LessThan(.001f),
                        "UV padding must preserve transparent source holes at the island boundary.");
                }
                Assert.That(pixels[38*64+32].a,Is.LessThan(.001f),"Padding must remain local to the island edge.");
                Assert.That(pixels[32*64+15].a,Is.LessThan(.001f),"Padding must not fill transparent interior pixels.");
            }
            finally{engine.EndStroke(false);Object.DestroyImmediate(brush);Object.DestroyImmediate(source);}
        }

        [TestCase(false)] [TestCase(true)]
        public void EveryMirroredRibbonCopyKeepsBothButtCaps(bool directUV)
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.clear);
            var brush=fixture.CreateBrush(1,1,shape:BrushPreset.Shape.Square);brush.size=.12f;
            var layer=fixture.set.AddLayer("Mirrored caps");
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            using var engine=new PaintingEngine(null,null,null,shader);
            try
            {
                var context=fixture.CreateContext(brush,TexturePaintTool.Paint,Color.white,strength:1);
                context.projectionDepth=1;context.directUV=directUV;
                var samples=new[]{new StrokeSample(new Vector3(.18f,.5f,0),Vector3.forward,new Vector2(.18f,.5f),0,0),
                    new StrokeSample(new Vector3(.4f,.5f,0),Vector3.forward,new Vector2(.4f,.5f),0,1)};
                var segments=TexturePaintStageWindow.BuildRibbonSegments(samples,brush.size,1);
                segments.Add(MirrorRibbon(segments[0]));
                Assert.That(engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay),Is.True);
                Assert.That(engine.ApplyRibbon(segments,samples,false,false,false,directUV),Is.True);
                var pixels=TexturePaintGpuTestFixture.ReadPixels(layer.channels[TexturePaintChannel.Albedo].Front);
                foreach(int x in new[]{10,28,36,55})
                    Assert.That(pixels[32*64+x].a,Is.LessThan(.001f),"A copied endpoint must not smear beyond its butt cap, x="+x);
                foreach(int x in new[]{18,45}) Assert.That(pixels[32*64+x].a,Is.GreaterThan(.99f));
            }
            finally{engine.EndStroke(false);Object.DestroyImmediate(brush);}
        }

        [TestCase(false, false, false, TexturePaintPathFlipMode.Off, TexturePaintPathFlipMode.Off)]
        [TestCase(true, true, false, TexturePaintPathFlipMode.EveryTile, TexturePaintPathFlipMode.EveryTile)]
        [TestCase(false, false, false, TexturePaintPathFlipMode.Alternate, TexturePaintPathFlipMode.Off)]
        [TestCase(true, false, true, TexturePaintPathFlipMode.Off, TexturePaintPathFlipMode.Alternate)]
        [TestCase(false, true, false, TexturePaintPathFlipMode.Alternate, TexturePaintPathFlipMode.Alternate)]
        [TestCase(true, true, true, TexturePaintPathFlipMode.Alternate, TexturePaintPathFlipMode.Off)]
        [TestCase(false, false, false, TexturePaintPathFlipMode.Random, TexturePaintPathFlipMode.Random)]
        [TestCase(true, false, true, TexturePaintPathFlipMode.Random, TexturePaintPathFlipMode.Off)]
        [TestCase(false, true, true, TexturePaintPathFlipMode.Off, TexturePaintPathFlipMode.Random)]
        [TestCase(true, true, false, TexturePaintPathFlipMode.Alternate, TexturePaintPathFlipMode.Random)]
        public void RibbonMirrorsWholeTilesConsistentlyAcrossEveryChannel(bool directUV, bool alongY,
            bool reversed, TexturePaintPathFlipMode flipX, TexturePaintPathFlipMode flipY)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear);
            foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
            {
                if (channel == TexturePaintChannel.Albedo) continue;
                fixture.set.channels.Add(channel, new TextureChannelTarget { channel = channel,
                    format = RenderTextureFormat.ARGBHalf, editable = new EditableTextureTarget(channel.ToString(),
                        64, 64, RenderTextureFormat.ARGBHalf, null, Color.clear) });
            }
            var brush = fixture.CreateBrush(1, 1, shape: BrushPreset.Shape.Square); brush.size = .4f;
            var colorSource = Source(false); var normalSource = Source(true);
            var layer = fixture.set.AddLayer("Mirrored ribbon");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            Assert.That(shader, Is.Not.Null); Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
            using var engine = new PaintingEngine(null, null, null, shader);
            try
            {
                var context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.white, strength: 1);
                context.projectionDepth = 1; context.directUV = directUV;
                foreach (var channel in fixture.set.channels.Keys)
                    context.channelSources[channel] = new TexturePaintChannelSourceSettings { source = TexturePaintBrushSource.Texture,
                        sourceTexture = channel == TexturePaintChannel.Normal ? normalSource : colorSource };
                var samples = new List<StrokeSample> {
                    new StrokeSample(new Vector3(.5f,0,0), Vector3.forward, new Vector2(.5f,0), 0, 0),
                    new StrokeSample(new Vector3(.5f,1,0), Vector3.forward, new Vector2(.5f,1), 0, 1) };
                var segments = TexturePaintStageWindow.BuildRibbonSegments(samples, .4f, .25f);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplyRibbon(segments, samples, alongY, reversed, false, directUV), Is.True);
                var baseline = new Dictionary<TexturePaintChannel, Color[]>();
                foreach (var pair in layer.channels) baseline.Add(pair.Key, TexturePaintGpuTestFixture.ReadPixels(pair.Value.Front));
                Assert.That(baseline.Count, Is.EqualTo(Enum.GetValues(typeof(TexturePaintChannel)).Length));
                Assert.That(engine.RewindActiveStroke(), Is.True); engine.EndStroke(false);
                context.textureFlipX = flipX; context.textureFlipY = flipY;
                // Revisit the first seed after a different one: regeneration must not consume random state.
                foreach (int seed in new[] { -137, 941, -137 })
                {
                    context.textureFlipSeed = seed;
                    Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                    Assert.That(engine.ApplyRibbon(segments, samples, alongY, reversed, false, directUV), Is.True);
                    foreach (var pair in layer.channels)
                    {
                        var actual = TexturePaintGpuTestFixture.ReadPixels(pair.Value.Front);
                        for (int tile = 0; tile < 4; tile++) foreach (int offset in new[] { 4, 11 }) foreach (int x in new[] { 18, 45 })
                        {
                            int y = tile * 16 + offset;
                            bool fx = TexturePaintPathMirroring.ShouldFlip(flipX, tile, seed, false);
                            bool fy = TexturePaintPathMirroring.ShouldFlip(flipY, tile, seed, true);
                            int expectedX = (alongY ? fx : fy) ? 63 - x : x;
                            int expectedY = (alongY ? fy : fx) ? tile * 16 + 15 - offset : y;
                            Color expected = baseline[pair.Key][expectedY * 64 + expectedX];
                            if (pair.Key == TexturePaintChannel.Normal)
                            { if (fx) expected.r = 1 - expected.r; if (fy) expected.g = 1 - expected.g; }
                            Assert.That(expected.a, Is.GreaterThan(.3f), "Baseline must contain painted pixels");
                            EqualPixel(actual[y * 64 + x], expected, pair.Key + " tile " + tile + " seed " + seed);
                        }
                    }
                    Assert.That(engine.RewindActiveStroke(), Is.True); engine.EndStroke(false);
                }
            }
            finally { engine.EndStroke(false); Object.DestroyImmediate(brush); Object.DestroyImmediate(colorSource); Object.DestroyImmediate(normalSource); }
        }

        [TestCase(0, false)] [TestCase(0, true)]
        [TestCase(1, false)] [TestCase(1, true)]
        [TestCase(2, false)] [TestCase(2, true)]
        public void MirroredStampSamplesAndNormalsMatchForCpuSingleAndBatch(int backend, bool normal)
        {
            TexturePaintChannel channel = normal ? TexturePaintChannel.Normal : TexturePaintChannel.Albedo;
            using var fixture = new TexturePaintGpuTestFixture(Color.clear, channel);
            using var engine = backend == 0 ? new PaintingEngine(null, null, null) : TexturePaintGpuTestFixture.CreateEngine();
            var brush = fixture.CreateBrush(1, 1, shape: BrushPreset.Shape.Square);
            var source = Source(normal); var layer = fixture.set.AddLayer("Mirrored stamp");
            try
            {
                var context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.white, channel, 1);
                context.paintSource = TexturePaintBrushSource.Texture; context.sourceTexture = source; context.directUV = true;
                Color[] baseline = null;
                for (int pass = 0; pass < 4; pass++)
                {
                    var sample = TexturePaintGpuTestFixture.CenterSample();
                    if (pass > 0) sample = TexturePaintPathMirroring.Apply(sample, 1,
                        pass != 2 ? TexturePaintPathFlipMode.Alternate : TexturePaintPathFlipMode.Off,
                        pass != 1 ? TexturePaintPathFlipMode.Alternate : TexturePaintPathFlipMode.Off, 0);
                    Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                    int dispatches = engine.Performance.computeDispatches;
                    if (backend == 2)
                    {
                        Assert.That(engine.ApplySamples(new[] { new StrokeDispatchSample(sample, .4f, default),
                            new StrokeDispatchSample(sample, .4f, default) }), Is.True);
                        Assert.That(engine.Performance.computeDispatches - dispatches, Is.EqualTo(1),
                            "Two overlapping stamps must use one batch dispatch, not sequential fallback.");
                    }
                    else Assert.That(engine.ApplySample(sample, .4f), Is.True);
                    var actual = TexturePaintGpuTestFixture.ReadPixels(layer.channels[channel].Front);
                    if (pass == 0) baseline = actual;
                    else foreach (int x in new[] { 18, 45 }) foreach (int y in new[] { 18, 45 })
                    {
                        bool fx = pass != 2, fy = pass != 1;
                        Color expected = baseline[(fy ? 63 - y : y) * 64 + (fx ? 63 - x : x)];
                        if (normal) { if (fx) expected.r = 1 - expected.r; if (fy) expected.g = 1 - expected.g; }
                        EqualPixel(actual[y * 64 + x], expected, "backend " + backend + " pass " + pass);
                    }
                    Assert.That(engine.RewindActiveStroke(), Is.True); engine.EndStroke(false);
                }
                if (backend != 0) Assert.That(engine.Performance.computeDispatches, Is.GreaterThan(0));
            }
            finally { engine.EndStroke(false); Object.DestroyImmediate(brush); Object.DestroyImmediate(source); }
        }

        [Test] public void SeededFlipsAreIndependentRepeatableAndDoNotChangeGlobalRandom()
        {
            var before = UnityEngine.Random.state;
            int xCount = 0, yCount = 0, differentAxes = 0, differentSeeds = 0;
            for (int i = 0; i < 128; i++)
            {
                bool x = TexturePaintPathMirroring.ShouldFlip(TexturePaintPathFlipMode.Random, i, -137, false);
                bool y = TexturePaintPathMirroring.ShouldFlip(TexturePaintPathFlipMode.Random, i, -137, true);
                if (x) xCount++; if (y) yCount++; if (x != y) differentAxes++;
                if (x != TexturePaintPathMirroring.ShouldFlip(TexturePaintPathFlipMode.Random, i, 941, false)) differentSeeds++;
                Assert.That(TexturePaintPathMirroring.ShouldFlip(TexturePaintPathFlipMode.Alternate, i, 0, false), Is.EqualTo((i & 1) != 0));
                Assert.That(TexturePaintPathMirroring.ShouldFlip(TexturePaintPathFlipMode.Random, i, -137, false), Is.EqualTo(x));
            }
            Assert.That(xCount, Is.InRange(32,96)); Assert.That(yCount, Is.InRange(32,96));
            Assert.That(differentAxes, Is.InRange(32,96)); Assert.That(differentSeeds, Is.InRange(32,96));
            Assert.That(UnityEngine.Random.state, Is.EqualTo(before));
        }
    }
}
#endif
