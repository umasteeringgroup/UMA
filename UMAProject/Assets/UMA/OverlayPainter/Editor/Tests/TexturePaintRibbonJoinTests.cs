#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintRibbonJoinTests
    {
        private sealed class Ribbon : IDisposable
        {
            public readonly TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            public readonly List<Object> owned = new List<Object>();
            public readonly PaintingEngine engine;
            public readonly StrokeContext context;
            public readonly TexturePaintLayer layer;
            public Ribbon(bool allChannels = false)
            {
                if (allChannels) foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
                    if (channel != TexturePaintChannel.Albedo)
                        fixture.set.channels.Add(channel, new TextureChannelTarget { channel = channel,
                            format = RenderTextureFormat.ARGBHalf, editable = new EditableTextureTarget(channel.ToString(),
                                64, 64, RenderTextureFormat.ARGBHalf, null, Color.clear) });
                var brush = fixture.CreateBrush(1, 1, shape: BrushPreset.Shape.Square); brush.size = .4f; owned.Add(brush);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
                Assert.That(shader, Is.Not.Null); Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
                engine = new PaintingEngine(null, null, null, shader);
                layer = fixture.set.AddLayer("Join crossfade");
                context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.white, strength: 1);
                context.projectionDepth = 1; context.ribbonCrossfadeJoins = true; context.ribbonJoinOverlap = .5f;
            }
            public Texture2D Image(Func<int,int,Color> pixel)
            {
                var image = new Texture2D(16, 16, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                owned.Add(image);
                for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) image.SetPixel(x,y,pixel(x,y));
                image.Apply(); return image;
            }
            public void Source(Texture2D image)
            { context.paintSource = TexturePaintBrushSource.Texture; context.sourceTexture = image; }
            public Dictionary<TexturePaintChannel,Color[]> Render(bool directUV = false, bool alongY = false, bool reversed = false, bool closed = false, int tiles = 4, float spanOffset = 0)
            {
                var samples = new List<StrokeSample> {
                    new StrokeSample(new Vector3(.5f,0,0), Vector3.forward, new Vector2(.5f,0),0,0),
                    new StrokeSample(new Vector3(.5f,1,0), Vector3.forward, new Vector2(.5f,1),0,1) };
                // Explicit intrinsic coordinates let the test measure both sides of an internal
                // or closing join independently of the separate spline tessellation tests.
                var segments = new[] { new TexturePaintRibbonSegment {
                    leftStartAlong = new Vector4(.1f,0,0,0), rightStartFlow = new Vector4(.9f,0,0,1),
                    leftEndAlong = new Vector4(.1f,1,0,tiles + spanOffset), rightEndFlow = new Vector4(.9f,1,0,1),
                    normalStartPressure = new Vector4(0,0,1,1), normalEndPressure = new Vector4(0,0,1,1),
                    colorStart = context.color, colorEnd = context.color } };
                context.directUV = directUV;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplyRibbon(segments,samples,alongY,reversed,closed,directUV), Is.True);
                var result = new Dictionary<TexturePaintChannel,Color[]>();
                foreach (var pair in layer.channels) result.Add(pair.Key, TexturePaintGpuTestFixture.ReadPixels(pair.Value.Front));
                Assert.That(engine.RewindActiveStroke(), Is.True); engine.EndStroke(false);
                return result;
            }
            public void Dispose()
            { engine.EndStroke(false); engine.Dispose(); fixture.Dispose(); foreach (var item in owned) Object.DestroyImmediate(item); }
        }
        private static Color Normal(float x, float y) => new Color(x*.5f+.5f,y*.5f+.5f,Mathf.Sqrt(1-x*x-y*y)*.5f+.5f,1);
        private static Color BlendNormal(Color a, Color b, float weight)
        {
            var v = new Vector3(Mathf.Lerp(a.r,b.r,weight)*2-1,Mathf.Lerp(a.g,b.g,weight)*2-1,Mathf.Lerp(a.b,b.b,weight)*2-1).normalized;
            return new Color(v.x*.5f+.5f,v.y*.5f+.5f,v.z*.5f+.5f,1);
        }
        private static void Equal(Color actual, Color expected, string message)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(.015f),message+" R");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(.015f),message+" G");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(.015f),message+" B");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(.008f),message+" A");
        }

        private static void AssertAlbedoCoverage(Dictionary<TexturePaintChannel, Color[]> pixels)
        {
            Color[] albedo = pixels[TexturePaintChannel.Albedo];
            foreach (var pair in pixels)
                for (int i = 0; i < albedo.Length; i++)
                    Assert.That(pair.Value[i].a, Is.EqualTo(albedo[i].a).Within(.003f),
                        pair.Key + " must share albedo coverage at pixel " + i);
        }

        [TestCase(false, false, false, 0f, false)]
        [TestCase(true, true, false, .5f, false)]
        [TestCase(false, false, true, .5f, true)]
        [TestCase(true, true, true, 0f, true)]
        public void SpriteSetChannelsShareCroppedAlbedoCoverageThroughJoinsAndFlips(
            bool directUV, bool alongY, bool reversed, float overlap, bool endpoints)
        {
            using var ribbon = new Ribbon(true);
            ribbon.context.ribbonJoinOverlap = overlap;
            ribbon.context.textureFlipX = TexturePaintPathFlipMode.Alternate;
            ribbon.context.textureFlipY = TexturePaintPathFlipMode.Random;
            ribbon.context.textureFlipSeed = -137;
            foreach (var channel in ribbon.fixture.set.channels.Keys)
            {
                Texture2D image = ribbon.Image((x, y) => channel == TexturePaintChannel.Albedo
                    ? new Color(.2f, .4f, .7f, x < 7 || x > 9 || y < 7 || y > 9 ? 0f
                        : x == 8 || y == 8 ? .35f : .8f)
                    : channel == TexturePaintChannel.Normal
                        ? new Color(.5f, .5f, 1f, 0f) : new Color(.65f, .65f, .65f, 0f));
                // Neighboring regions in the sheet must not determine this sprite's coverage.
                Sprite sprite = Sprite.Create(image, new Rect(4, 4, 8, 8), Vector2.one * .5f);
                ribbon.owned.Add(sprite);
                ribbon.context.channelSources[channel] = TexturePaintStageWindow.CreateSpriteSetPathSourceSettings(
                    sprite, false, TexturePaintNormalConvention.OpenGL);
            }
            if (endpoints)
            {
                ribbon.context.ribbonBeginningTexture = ribbon.Image((x, y) => new Color(.5f, .5f, 1f, .2f));
                ribbon.context.ribbonEndTexture = ribbon.Image((x, y) => new Color(.5f, .5f, 1f, .7f));
            }

            var pixels = ribbon.Render(directUV, alongY, reversed);
            Assert.That(pixels.Count, Is.EqualTo(Enum.GetValues(typeof(TexturePaintChannel)).Length));
            AssertAlbedoCoverage(pixels);
            int transparent = 0, visible = 0;
            for (int y = 20; y < 44; y++)
            {
                int pixel = y * 64 + 32;
                float alpha = pixels[TexturePaintChannel.Albedo][pixel].a;
                if (alpha < .001f) transparent++;
                else
                {
                    visible++;
                    Assert.That(pixels[TexturePaintChannel.Roughness][pixel].r,
                        Is.EqualTo(.65f).Within(.015f), "A map's zero alpha must not suppress its visible value.");
                    Equal(pixels[TexturePaintChannel.Normal][pixel], new Color(.5f, .5f, 1f, alpha),
                        "Partial coverage must preserve the source normal direction.");
                }
            }
            Assert.That(transparent, Is.GreaterThan(0), "The cropped albedo has transparent interior pixels.");
            Assert.That(visible, Is.GreaterThan(0), "Non-albedo channels must paint despite their own zero alpha.");
            if (endpoints)
            {
                Assert.That(pixels[TexturePaintChannel.Albedo][32].a, Is.EqualTo(.2f).Within(.003f));
                Assert.That(pixels[TexturePaintChannel.Albedo][63 * 64 + 32].a, Is.EqualTo(.7f).Within(.003f));
            }
        }

        [TestCase(false, 0f, false)] [TestCase(true, .5f, false)]
        [TestCase(false, .5f, true)] [TestCase(true, 0f, true)]
        public void OverlaySetChannelsShareAlbedoOrExplicitMaskCoverage(bool directUV, float overlap, bool explicitMask)
        {
            using var ribbon = new Ribbon(true);
            ribbon.context.ribbonJoinOverlap = overlap;
            var overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            ribbon.owned.Add(overlay);
            var maps = new List<Texture>();
            foreach (var channel in ribbon.fixture.set.channels.Keys)
            {
                ribbon.fixture.set.channels[channel].umaChannelIndex = maps.Count;
                maps.Add(ribbon.Image((x, y) => channel == TexturePaintChannel.Albedo
                    ? new Color(.2f, .4f, .7f, .45f)
                    : channel == TexturePaintChannel.Normal
                        ? new Color(.5f, .5f, 1f, 0f) : new Color(.65f, .65f, .65f, 0f)));
                ribbon.context.channelSources[channel] = new TexturePaintChannelSourceSettings
                { source = TexturePaintBrushSource.Overlay, sourceOverlay = overlay };
            }
            overlay.textureList = maps.ToArray();
            if (explicitMask) overlay.alphaMask = ribbon.Image((x, y) => new Color(1f, 1f, 1f, .2f));

            var pixels = ribbon.Render(directUV);
            Assert.That(pixels.Count, Is.EqualTo(Enum.GetValues(typeof(TexturePaintChannel)).Length));
            AssertAlbedoCoverage(pixels);
            float expectedAlpha = explicitMask ? .2f : .45f;
            foreach (var pair in pixels)
                for (int y = 0; y < 64; y++)
                    Assert.That(pair.Value[y * 64 + 32].a, Is.EqualTo(expectedAlpha).Within(.003f),
                        pair.Key + " common overlay opacity at row " + y);
            Assert.That(pixels[TexturePaintChannel.Roughness][32 * 64 + 32].r,
                Is.EqualTo(.65f).Within(.015f));
        }

        [TestCase(false)] [TestCase(true)]
        public void OverlayAlbedoSuppliesCoverageWithoutAnAlbedoDestination(bool explicitMask)
        {
            using var ribbon = new Ribbon(true);
            ribbon.fixture.set.channels[TexturePaintChannel.Albedo].Dispose();
            ribbon.fixture.set.channels.Remove(TexturePaintChannel.Albedo);
            ribbon.fixture.set.channels[TexturePaintChannel.Roughness].umaChannelIndex = 1;
            var overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            ribbon.owned.Add(overlay);
            overlay.textureList = new Texture[]
            {
                ribbon.Image((x, y) => new Color(.2f, .4f, .7f, .45f)),
                ribbon.Image((x, y) => new Color(.65f, .65f, .65f, 0f))
            };
            if (explicitMask) overlay.alphaMask = ribbon.Image((x, y) => new Color(1f, 1f, 1f, .2f));
            foreach (var channel in new[] { TexturePaintChannel.Albedo, TexturePaintChannel.Roughness })
                ribbon.context.channelSources[channel] = new TexturePaintChannelSourceSettings
                { source = TexturePaintBrushSource.Overlay, sourceOverlay = overlay };

            var pixels = ribbon.Render();
            Assert.That(pixels.Count, Is.EqualTo(1), "A coverage source must not require an editable albedo channel.");
            float expectedAlpha = explicitMask ? .2f : .45f;
            for (int y = 0; y < 64; y++)
                Equal(pixels[TexturePaintChannel.Roughness][y * 64 + 32],
                    new Color(.65f, .65f, .65f, expectedAlpha), "Overlay coverage without albedo target at row " + y);
        }

        [TestCase(false)] [TestCase(true)]
        public void LockedAlbedoStillSuppliesCoverageFromItsEffectiveSource(bool memberOverride)
        {
            using var ribbon = new Ribbon(true);
            ribbon.context.channelSources[TexturePaintChannel.Albedo] = new TexturePaintChannelSourceSettings
            { source = TexturePaintBrushSource.Color, color = new Color(.2f, .4f, .7f, .4f) };
            ribbon.context.channelSources[TexturePaintChannel.Roughness] = new TexturePaintChannelSourceSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceTexture = ribbon.Image((x, y) => new Color(.65f, .65f, .65f, 0f))
            };
            var albedo = ribbon.layer.GetChannelSettings(TexturePaintChannel.Albedo);
            albedo.locked = true;
            if (memberOverride)
                albedo.sourceSettings = new TexturePaintChannelSourceSettings
                {
                    source = TexturePaintBrushSource.Texture,
                    sourceTexture = ribbon.Image((x, y) => new Color(.2f, .4f, .7f, .2f))
                };

            var pixels = ribbon.Render();
            float expectedAlpha = memberOverride ? .2f : .4f;
            for (int y = 0; y < 64; y++)
                Assert.That(pixels[TexturePaintChannel.Roughness][y * 64 + 32].a,
                    Is.EqualTo(expectedAlpha).Within(.003f), "Locked albedo still defines the ribbon silhouette.");
            if (pixels.TryGetValue(TexturePaintChannel.Albedo, out Color[] locked))
                Assert.That(locked[32 * 64 + 32].a, Is.Zero, "The coverage source must remain locked.");
        }

        [TestCase(0f)] [TestCase(.5f)]
        public void AChannelWithoutAuthoredAlbedoRetainsItsOwnCoverage(float overlap)
        {
            using var ribbon = new Ribbon(true);
            ribbon.context.ribbonJoinOverlap = overlap;
            ribbon.context.channelSources[TexturePaintChannel.Roughness] = new TexturePaintChannelSourceSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceTexture = ribbon.Image((x, y) => new Color(.65f, .65f, .65f, .3f))
            };
            var pixels = ribbon.Render();
            Assert.That(pixels.Count, Is.EqualTo(1));
            for (int y = 0; y < 64; y++)
                Assert.That(pixels[TexturePaintChannel.Roughness][y * 64 + 32].a,
                    Is.EqualTo(.3f).Within(.003f), "Absent albedo must retain the map's coverage.");
        }

        [TestCase(false, 0f, false)] [TestCase(true, .5f, false)]
        [TestCase(false, .5f, true)] [TestCase(true, 0f, true)]
        public void SharedPartialCoveragePreservesFlippedNormalsAndStraightAlphaAccumulation(
            bool directUV, float overlap, bool existingPaint)
        {
            using var ribbon = new Ribbon(true);
            ribbon.context.ribbonJoinOverlap = overlap;
            ribbon.context.textureFlipX = TexturePaintPathFlipMode.Alternate;
            ribbon.context.textureFlipY = TexturePaintPathFlipMode.Random;
            ribbon.context.textureFlipSeed = -137;
            Color sourceNormal = Normal(.6f, .3f);
            sourceNormal.a = 0f;
            ribbon.context.channelSources[TexturePaintChannel.Albedo] = new TexturePaintChannelSourceSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceTexture = ribbon.Image((x, y) => new Color(.2f, .4f, .7f, .3f))
            };
            ribbon.context.channelSources[TexturePaintChannel.Normal] = new TexturePaintChannelSourceSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceTexture = ribbon.Image((x, y) => sourceNormal)
            };
            Color destinationNormal = Normal(-.25f, .2f);
            destinationNormal.a = existingPaint ? .5f : 0f;
            if (existingPaint)
                ribbon.layer.channels[TexturePaintChannel.Normal] = new EditableTextureTarget("Existing ribbon normal",
                    64, 64, RenderTextureFormat.ARGBHalf, ribbon.Image((x, y) => destinationNormal), Color.clear);

            Color TileNormal(int tile)
            {
                Color normal = sourceNormal;
                if (TexturePaintPathMirroring.ShouldFlip(ribbon.context.textureFlipX, tile, -137, false))
                    normal.r = 1f - normal.r;
                if (TexturePaintPathMirroring.ShouldFlip(ribbon.context.textureFlipY, tile, -137, true))
                    normal.g = 1f - normal.g;
                return normal;
            }

            var pixels = ribbon.Render(directUV)[TexturePaintChannel.Normal];
            for (int y = 0; y < 64; y++)
            {
                float along = (y + .5f) / 16f;
                int tile = Mathf.FloorToInt(along), left = tile, right = tile;
                float phase = along - tile, join = tile;
                if (overlap > 0f)
                {
                    if (phase < overlap * .5f && tile > 0) left--;
                    else if (phase > 1f - overlap * .5f && tile < 3) { right++; join++; }
                }
                Color paint = TileNormal(tile);
                if (left != right)
                    paint = BlendNormal(TileNormal(left), TileNormal(right),
                        Mathf.SmoothStep(0f, 1f, (along - join + overlap * .5f) / overlap));
                Vector3 paintVector = new Vector3(paint.r, paint.g, paint.b) * 2f - Vector3.one;
                Vector3 destinationVector = new Vector3(destinationNormal.r, destinationNormal.g, destinationNormal.b)
                    * 2f - Vector3.one;
                Vector3 expectedVector = (paintVector * .3f + destinationVector * destinationNormal.a * .7f).normalized;
                Color expected = new Color(expectedVector.x * .5f + .5f, expectedVector.y * .5f + .5f,
                    expectedVector.z * .5f + .5f, .3f + destinationNormal.a * .7f);
                Equal(pixels[y * 64 + 32], expected, "Partial normal coverage at row " + y);
            }
        }

        [TestCase(false,false,false,.2f)] [TestCase(true,true,false,.2f)]
        [TestCase(false,true,true,.5f)] [TestCase(true,false,true,.5f)]
        [TestCase(false,false,false,1f)] [TestCase(true,true,true,1f)]
        public void CrossfadeUsesComplementaryWeightsOnEveryChannelWithoutOpacitySeams(bool directUV,bool alongY,bool reversed,float overlap)
        {
            using var ribbon = new Ribbon(true); ribbon.context.ribbonJoinOverlap = overlap;
            Color head = new Color(.2f,.4f,.7f,1), tail = new Color(.8f,.6f,.3f,1);
            Color headNormal = Normal(.1f,.3f), tailNormal = Normal(.6f,.3f);
            foreach (var channel in ribbon.fixture.set.channels.Keys)
            {
                bool normal = channel == TexturePaintChannel.Normal;
                var image = ribbon.Image((x,y)=>(alongY ? y : x)<8 ? (normal ? headNormal : head) : (normal ? tailNormal : tail));
                ribbon.context.channelSources[channel] = new TexturePaintChannelSourceSettings { source=TexturePaintBrushSource.Texture,sourceTexture=image };
            }
            var pixels = ribbon.Render(directUV,alongY,reversed);
            Assert.That(pixels.Count,Is.EqualTo(Enum.GetValues(typeof(TexturePaintChannel)).Length));
            foreach (var pair in pixels)
            {
                for (int y = 0; y < 64; y++) Assert.That(pair.Value[y*64+32].a,Is.EqualTo(1).Within(.002f),pair.Key+" opacity at "+y);
                for (int join = 1; join < 4; join++) for (int y=0;y<64;y++)
                {
                    float along=(y+.5f)/16f, distance=along-join;
                    if (Mathf.Abs(distance)>=overlap*.5f) continue;
                    float incoming=Mathf.SmoothStep(0,1,(distance+overlap*.5f)/overlap);
                    bool normal=pair.Key==TexturePaintChannel.Normal;
                    // The first and final images extend inward only, preserving their open
                    // endpoints. Their midpoint therefore differs from an interior tile's.
                    float outgoingMidpoint = join == 1 ? (1 + overlap*.5f)*.5f : join - .5f;
                    float incomingMidpoint = join == 3 ? (7 - overlap*.5f)*.5f : join + .5f;
                    bool outgoingTail = along >= outgoingMidpoint, incomingTail = along >= incomingMidpoint;
                    if (reversed) { outgoingTail = !outgoingTail; incomingTail = !incomingTail; }
                    Color a = normal ? (outgoingTail ? tailNormal : headNormal) : (outgoingTail ? tail : head);
                    Color b = normal ? (incomingTail ? tailNormal : headNormal) : (incomingTail ? tail : head);
                    a = TexturePaintChannelUtility.ConstrainColor(pair.Key,a);
                    b = TexturePaintChannelUtility.ConstrainColor(pair.Key,b);
                    Equal(pair.Value[y*64+32],normal ? BlendNormal(a,b,incoming) : Color.Lerp(a,b,incoming),pair.Key+" join "+join+" row "+y);
                }
            }
        }

        [TestCase(TexturePaintPathFlipMode.Alternate,false)] [TestCase(TexturePaintPathFlipMode.Random,true)]
        public void EachOverlappingTileRetainsItsOwnSeededFlipsAndNormalDirections(TexturePaintPathFlipMode mode,bool directUV)
        {
            using var ribbon = new Ribbon(true);
            ribbon.context.textureFlipX=mode; ribbon.context.textureFlipY=TexturePaintPathFlipMode.Random; ribbon.context.textureFlipSeed=-137;
            Color normal=Normal(.6f,.3f);
            foreach(var channel in ribbon.fixture.set.channels.Keys)
                ribbon.context.channelSources[channel]=new TexturePaintChannelSourceSettings { source=TexturePaintBrushSource.Texture,
                    sourceTexture=ribbon.Image((x,y)=>channel==TexturePaintChannel.Normal ? normal : new Color(x<8 ? .2f : .8f,.4f,.6f,1)) };
            var pixels=ribbon.Render(directUV,true);
            foreach(var pair in pixels) for(int join=1;join<4;join++) foreach(int y in new[]{join*16-2,join*16+1})
            {
                Color Tile(int index)
                {
                    bool fx=TexturePaintPathMirroring.ShouldFlip(mode,index,-137,false);
                    bool fy=TexturePaintPathMirroring.ShouldFlip(TexturePaintPathFlipMode.Random,index,-137,true);
                    if(pair.Key!=TexturePaintChannel.Normal)return TexturePaintChannelUtility.ConstrainColor(pair.Key,new Color(fx ? .8f : .2f,.4f,.6f,1));
                    Color n=normal;if(fx)n.r=1-n.r;if(fy)n.g=1-n.g;return n;
                }
                float incoming=Mathf.SmoothStep(0,1,((y+.5f)/16f-join+.25f)/.5f);
                Color a=Tile(join-1),b=Tile(join);
                Equal(pair.Value[y*64+18],pair.Key==TexturePaintChannel.Normal ? BlendNormal(a,b,incoming) : Color.Lerp(a,b,incoming),pair.Key+" seeded join "+join);
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void TransparentEndpointImageDoesNotBleedAndOpenEndsRemainInPlace(bool directUV)
        {
            using var ribbon=new Ribbon();
            ribbon.context.color=Color.red;
            ribbon.context.ribbonBeginningTexture=ribbon.Image((x,y)=>new Color(0,1,0,0));
            ribbon.context.ribbonEndTexture=ribbon.Image((x,y)=>Color.blue);
            var pixels=ribbon.Render(directUV)[TexturePaintChannel.Albedo];
            Assert.That(pixels[32].a,Is.Zero);
            Equal(pixels[63*64+32],Color.blue,"Last source endpoint");
            foreach(int y in new[]{14,15,16,17})
            {
                float incoming=Mathf.SmoothStep(0,1,((y+.5f)/16f-1+.25f)/.5f);
                Equal(pixels[y*64+32],new Color(1,0,0,incoming),"Transparent source RGB must not bleed");
            }
            Equal(pixels[32*64+32],Color.red,"Interior");
            Assert.That(pixels[32*64].a,Is.Zero,"The overlap must not widen ribbon geometry");
        }

        [TestCase(false,1)] [TestCase(true,1)] [TestCase(false,3)] [TestCase(true,3)]
        public void ClosedRibbonCrossfadesLastImageIntoFirstWithoutAnOpacityGap(bool directUV,int tiles)
        {
            using var ribbon=new Ribbon();
            ribbon.Source(ribbon.Image((x,y)=>x<8 ? Color.red : Color.blue));
            var pixels=ribbon.Render(directUV,closed:true,tiles:tiles)[TexturePaintChannel.Albedo];
            foreach(int y in new[]{0,1,62,63})
            {
                float along=(y+.5f)/64f*tiles;
                float distance=y<32 ? along : along-tiles;
                float incoming=Mathf.SmoothStep(0,1,(distance+.25f)/.5f);
                Equal(pixels[y*64+32],Color.Lerp(Color.blue,Color.red,incoming),"Closing join row "+y);
            }
        }

        [Test] public void SingleOpenTileAndDisabledOrZeroOverlapKeepTheirOriginalPixels()
        {
            using var ribbon=new Ribbon(); ribbon.Source(ribbon.Image((x,y)=>new Color(x/15f,y/15f,.5f,.5f)));
            ribbon.context.ribbonCrossfadeJoins=false;
            var baseline=ribbon.Render(tiles:1)[TexturePaintChannel.Albedo];
            ribbon.context.ribbonCrossfadeJoins=true; ribbon.context.ribbonJoinOverlap=1;
            var single=ribbon.Render(tiles:1)[TexturePaintChannel.Albedo];
            TexturePaintGpuTestFixture.AssertImage("Single open tile crossfade",baseline,single);
            ribbon.context.ribbonCrossfadeJoins=false;
            baseline=ribbon.Render()[TexturePaintChannel.Albedo];
            ribbon.context.ribbonCrossfadeJoins=true; ribbon.context.ribbonJoinOverlap=0;
            TexturePaintGpuTestFixture.AssertImage("Zero join overlap",baseline,ribbon.Render()[TexturePaintChannel.Albedo]);
        }

        [TestCase(-.00001f)] [TestCase(.00001f)]
        public void FittedTileRoundoffDoesNotMoveTheEndImage(float offset)
        {
            using var ribbon=new Ribbon();
            ribbon.context.color=Color.red;
            ribbon.context.ribbonEndTexture=ribbon.Image((x,y)=>Color.blue);
            var exact=ribbon.Render(tiles:3)[TexturePaintChannel.Albedo];
            var rounded=ribbon.Render(tiles:3,spanOffset:offset)[TexturePaintChannel.Albedo];
            TexturePaintGpuTestFixture.AssertImage("Fitted tile roundoff",exact,rounded);
        }

        [Test] public void PartialAlphaAndWholePathFadesRemainIndependentOfJoinFades()
        {
            using var ribbon=new Ribbon();ribbon.context.color=new Color(.8f,.3f,.1f,.4f);
            ribbon.context.ribbonStartFade=2;ribbon.context.ribbonStartFadeCurve=AnimationCurve.Linear(0,.5f,1,.5f);
            var pixels=ribbon.Render()[TexturePaintChannel.Albedo];
            for(int y=0;y<64;y++)Equal(pixels[y*64+32],new Color(.8f,.3f,.1f,.2f).linear,"Partial alpha row "+y);
        }
    }
}
#endif
