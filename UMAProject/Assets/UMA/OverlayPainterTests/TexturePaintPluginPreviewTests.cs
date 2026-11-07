#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintPluginPreviewTests
    {
        private TextureStore store;
        private TextureSet set;
        private TexturePaintLayer layer;
        private Mesh mesh;

        [SetUp]
        public void SetUp()
        {
            mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up }, triangles = new[] { 0, 1, 2 } };
            set = new TextureSet { persistentId = "preview-test", surface = new ReconstructedSurface
                { mesh = mesh, triangleIslands = new[] { 0 } } };
            foreach (var channel in new[] { TexturePaintChannel.Albedo, TexturePaintChannel.Roughness,
                TexturePaintChannel.NormalControl })
                set.channels.Add(channel, new TextureChannelTarget { channel = channel,
                    editable = new EditableTextureTarget("Preview test", 256, 64,
                        RenderTextureFormat.ARGB32, null, Color.black) });
            layer = new TexturePaintLayer { kind = TexturePaintLayerKind.Plugin };
            set.layers.Add(layer);
            store = new TextureStore();
            ((List<TextureSet>)typeof(TextureStore).GetField("sets", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(store)).Add(set);
        }

        [TearDown]
        public void TearDown() { store?.Dispose(); UnityEngine.Object.DestroyImmediate(mesh); }

        [Test]
        public async Task FabricSurfaceDetailGeneratesRoughWeaveWithoutReplacingColorOrReliefSettings()
        {
            Type pluginType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                pluginType ??= assembly.GetType("UMA.TexturePaint.Examples.ClothTextureGeneratorPlugin");
            Assert.That(pluginType, Is.Not.Null);
            var asset = ScriptableObject.CreateInstance(pluginType);
            var plugin = (ITexturePaintCommandExtensionV2)asset;
            using var host = new PluginHost();
            try
            {
                var parameters = host.CreateParameters(plugin);
                TexturePaintStageWindow.ConfigureFabricSurfaceDetail(layer, parameters);
                await host.ExecutePluginLayerAsync(plugin, store, parameters,
                    new Dictionary<TextureSet, TexturePaintLayer> { { set, layer } }, null, CancellationToken.None);
                var generated = set.layers[0];
                Assert.That(generated.channels.Keys, Is.EquivalentTo(new[] {
                    TexturePaintChannel.Roughness, TexturePaintChannel.NormalControl }));
                Assert.That(generated.GetChannelSettings(TexturePaintChannel.NormalControl).blendMode,
                    Is.EqualTo(TexturePaintBlendMode.Overlay), "Weave must retain the preset's relief blending on first generation.");
                var pixels = ReadFabricPixels(generated.channels[TexturePaintChannel.Roughness].Front);
                float sum = 0; int covered = 0;
                foreach (var pixel in pixels) if (pixel.a > .5f) { sum += pixel.r; covered++; }
                Assert.That(covered, Is.GreaterThan(0));
                Assert.That(sum / covered, Is.GreaterThan(.65f), "The fabric finish should be predominantly rough.");
                Assert.That(host.Undo(), Is.True);
                Assert.That(set.layers[0], Is.SameAs(layer));
                Assert.That(layer.channels, Is.Empty);
                Assert.That(host.Redo(), Is.True);
                Assert.That(set.layers[0], Is.SameAs(generated));
                parameters.Get("outputAlbedo").boolean = true;
                parameters.Get("baseColor").color = Color.red;
                await host.ExecutePluginLayerAsync(plugin, store, parameters,
                    new Dictionary<TextureSet, TexturePaintLayer> { { set, generated } }, null, CancellationToken.None);
                Assert.That(set.layers[0].channels.ContainsKey(TexturePaintChannel.Albedo), Is.True);
                Assert.That(set.layers[0].GetChannelSettings(TexturePaintChannel.NormalControl).blendMode,
                    Is.EqualTo(TexturePaintBlendMode.Overlay), "Enabling color and regenerating must retain the relief blend.");
                Assert.That(host.Undo(), Is.True);
                Assert.That(set.layers[0], Is.SameAs(generated));
                Assert.That(host.Redo(), Is.True);
                Assert.That(set.layers[0].GetChannelSettings(TexturePaintChannel.NormalControl).blendMode,
                    Is.EqualTo(TexturePaintBlendMode.Overlay));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        private static Color[] ReadFabricPixels(RenderTexture source)
        {
            var previous = RenderTexture.active;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = source;
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                copy.Apply();
                return copy.GetPixels();
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(copy); }
        }

        [TestCase(0)]
        [TestCase(1)]
        public async Task VeinNetworkColorsAndThicknessFollowTheSameVessels(int projection)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UMA.TexturePaint.Examples.VeinsSubdermalGeneratorPlugin"))
                .First(t => t != null);
            var asset = ScriptableObject.CreateInstance(type);
            set.channels.Add(TexturePaintChannel.Thickness, new TextureChannelTarget
            {
                channel = TexturePaintChannel.Thickness,
                editable = new EditableTextureTarget("Vein thickness", 256, 64,
                    RenderTextureFormat.ARGB32, null, Color.black)
            });
            using var host = new PluginHost();
            try
            {
                var plugin = (ITexturePaintCommandExtensionV2)asset;
                var p = host.CreateParameters(plugin);
                Assert.That(p.Get("veinWidth").number, Is.EqualTo(1f));
                p.Get("projection").number = projection;
                p.Get("surfaceMode").number = 1;
                p.Get("scale").number = 4;
                p.Get("primaryColor").color = Color.white;
                p.Get("veinColor").color = Color.black;
                p.Get("veinIntensity").number = 1;
                p.Get("veinDepth").number = .4f;
                p.Get("mottlingEnabled").boolean = false;
                p.Get("bruisesEnabled").boolean = false;
                foreach (string id in new[] { "spotAmount", "freckleAmount", "poreAmount",
                    "redness", "oiliness", "wrinkleAmount", "thicknessVariation" })
                    p.Get(id).number = 0;
                p.Get("veinsEnabled").boolean = false;
                var baseline = await Generate(plugin, p);
                p.Get("veinsEnabled").boolean = true;
                var veins = await Generate(plugin, p);
                var repeat = await Generate(plugin, p);
                Assert.That(repeat[TexturePaintChannel.Albedo],
                    Is.EqualTo(veins[TexturePaintChannel.Albedo]));
                int changed = 0;
                for (int i = 0; i < veins[TexturePaintChannel.Albedo].Length; i++)
                {
                    float colorDelta = baseline[TexturePaintChannel.Albedo][i].r -
                        veins[TexturePaintChannel.Albedo][i].r;
                    float thicknessDelta = baseline[TexturePaintChannel.Thickness][i].r -
                        veins[TexturePaintChannel.Thickness][i].r;
                    Assert.That(thicknessDelta, Is.EqualTo(colorDelta * .4f).Within(.008f));
                    if (colorDelta > .03f) changed++;
                }
                Assert.That(changed, Is.GreaterThan(20), "Veins must survive the complete generation pipeline.");
                await host.ExecutePluginLayerAsync(plugin, store, p,
                    new Dictionary<TextureSet, TexturePaintLayer> { { set, layer } }, null, CancellationToken.None);
                Assert.That(set.layers[0].channels.ContainsKey(TexturePaintChannel.Thickness), Is.True);
                Assert.That(host.Undo(), Is.True);
                Assert.That(set.layers[0], Is.SameAs(layer));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [TestCase(0, 0)] [TestCase(1, 0)]
        [TestCase(0, 1)] [TestCase(1, 1)]
        [TestCase(0, 2)]
        public async Task RustFollowsUnderlyingNormalReliefAndAoWithoutReadingItselfOrHigherLayers(int projection, int inputKind)
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UMA.TexturePaint.Examples.RustCorrosionGeneratorPlugin"))
                .First(t => t != null);
            var asset = ScriptableObject.CreateInstance(type);
            var compositor = new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            set.channelPackShader = TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute");
            foreach (var target in set.channels.Values) target.Dispose();
            set.channels.Clear();
            foreach (var channel in new[] { TexturePaintChannel.Albedo, TexturePaintChannel.Normal,
                TexturePaintChannel.NormalControl, TexturePaintChannel.AmbientOcclusion })
            {
                var neutral = channel == TexturePaintChannel.Normal ? new Color(.5f,.5f,1,1) :
                    channel == TexturePaintChannel.NormalControl ? new Color(.5f,.5f,.5f,1) : Color.white;
                set.channels[channel] = new TextureChannelTarget { channel = channel, format = RenderTextureFormat.ARGBHalf,
                    editable = new EditableTextureTarget("Rust base",256,256,RenderTextureFormat.ARGBHalf,null,neutral),
                    composite = EditableTextureTarget.Create("Rust composite",256,256,RenderTextureFormat.ARGBHalf) };
            }
            var inputChannel = inputKind == 0 ? TexturePaintChannel.Normal :
                inputKind == 1 ? TexturePaintChannel.NormalControl : TexturePaintChannel.AmbientOcclusion;
            var detail = new TexturePaintLayer { kind = TexturePaintLayerKind.Paint, name = "Underlying bevel" };
            var image = new Texture2D(256,256,TextureFormat.RGBAFloat,false,true);
            var pixels = new Color[256*256];
            for (int y=0;y<256;y++) for (int x=0;x<256;x++)
            {
                float t=(x-64f)/10f, bump=Mathf.Exp(-t*t);
                if (inputKind == 0)
                {
                    var n = new Vector3(t*bump,0,1).normalized;
                    pixels[y*256+x] = new Color(n.x*.5f+.5f,.5f,n.z*.5f+.5f,1);
                }
                else { float h=inputKind == 1 ? .5f+.3f*bump : 1f-.8f*bump; pixels[y*256+x]=new Color(h,h,h,1); }
            }
            image.SetPixels(pixels);image.Apply();
            detail.channels[inputChannel]=new EditableTextureTarget("Underlying bevel",256,256,RenderTextureFormat.ARGBHalf,image,Color.clear);
            detail.GetChannelSettings(inputChannel);
            set.layers.Insert(0,detail);
            using var host = new PluginHost();
            try
            {
                var plugin=(ITexturePaintCommandExtensionV2)asset;
                var p=host.CreateParameters(plugin);
                p.Get("projection").number=projection;
                foreach(var id in new[]{"spread","pitting","streaking","flaking","edgeAmount"}) p.Get(id).number=0;
                p.Get("cavityAmount").number=1;
                p.Get("normalDetailRadius").number=16;
                p.Get("normalDetailStrength").number=16;
                detail.visible=false;set.BindPreviewTextures();
                var flat=await Generate(plugin,p);
                detail.visible=true;set.BindPreviewTextures();
                var draft=await Generate(plugin,p);
                Assert.That(draft[TexturePaintChannel.Albedo].Sum(c=>c.a),
                    Is.GreaterThan((flat.TryGetValue(TexturePaintChannel.Albedo,out var flatPixels) ? flatPixels.Sum(c=>c.a) : 0)+1),"Draft must detect underlying relief/AO.");
                await host.ExecutePluginLayerAsync(plugin,store,p,
                    new Dictionary<TextureSet,TexturePaintLayer>{{set,layer}},null,CancellationToken.None);
                var generated=set.layers[1];
                var first=ReadFabricPixels(generated.channels[TexturePaintChannel.Albedo].Front);
                float cavity=0, crown=0, away=0;
                for(int y=16;y<100;y++) for(int x=36;x<95;x++)
                {
                    float a=first[y*256+x].a;
                    if(Mathf.Abs(x-64)>9 && Mathf.Abs(x-64)<18)cavity+=a;
                    if(Mathf.Abs(x-64)<4)crown+=a;
                    if(x<40 || x>90)away+=a;
                }
                Assert.That(first.Sum(c=>c.a),Is.GreaterThan(10),"Full generation must detect detail.");
                if(inputKind != 2)
                {
                    Assert.That(cavity,Is.GreaterThan(crown+1),"Cavity rust belongs at the foot of a raised bevel, not its crown.");
                    Assert.That(cavity,Is.GreaterThan(away+1));
                }
                // Opposing detail above rust must not cancel the input below it.
                var upper=new TexturePaintLayer {kind=TexturePaintLayerKind.Paint,name="Higher detail"};
                upper.channels[inputChannel]=new EditableTextureTarget("Higher detail",256,256,RenderTextureFormat.ARGBHalf,null,
                    inputKind == 0 ? new Color(.5f,.5f,1,1) : inputKind == 1 ? new Color(.5f,.5f,.5f,1) : Color.white);
                upper.GetChannelSettings(inputChannel);set.layers.Add(upper);set.BindPreviewTextures();
                await host.ExecutePluginLayerAsync(plugin,store,p,
                    new Dictionary<TextureSet,TexturePaintLayer>{{set,generated}},null,CancellationToken.None);
                Assert.That(ReadFabricPixels(set.layers[1].channels[TexturePaintChannel.Albedo].Front),Is.EqualTo(first),
                    "Regeneration must exclude its previous output and higher layers.");
                if(inputKind != 2)
                {
                    p.Get("cavityAmount").number=0;p.Get("edgeAmount").number=1;
                    await host.ExecutePluginLayerAsync(plugin,store,p,
                        new Dictionary<TextureSet,TexturePaintLayer>{{set,set.layers[1]}},null,CancellationToken.None);
                    var edgePixels=ReadFabricPixels(set.layers[1].channels[TexturePaintChannel.Albedo].Front);
                    float edgeCrown=0,edgeFoot=0;
                    for(int y=16;y<100;y++) for(int x=36;x<95;x++)
                    {
                        if(Mathf.Abs(x-64)<4)edgeCrown+=edgePixels[y*256+x].a;
                        if(Mathf.Abs(x-64)>9 && Mathf.Abs(x-64)<18)edgeFoot+=edgePixels[y*256+x].a;
                    }
                    Assert.That(edgeCrown,Is.GreaterThan(edgeFoot+1),"Edge concentration should target convex crowns.");
                    p.Get("normalDetailStrength").number=0;
                    await host.ExecutePluginLayerAsync(plugin,store,p,
                        new Dictionary<TextureSet,TexturePaintLayer>{{set,set.layers[1]}},null,CancellationToken.None);
                    Assert.That(set.layers[1].channels.TryGetValue(TexturePaintChannel.Albedo,out var disabled)
                        ? ReadFabricPixels(disabled.Front).Sum(c=>c.a) : 0,Is.LessThan(.01f),
                        "Disabling normal influence should restore the flat mesh-only result.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(asset);compositor.Dispose();set.compositor=null; }
        }

        [Test]
        public async Task StubbleWorldDownDraftUsesMeshMapsAndLeavesLayerUntouched()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UMA.TexturePaint.Examples.StubbleMakerGeneratorPlugin"))
                .First(t => t != null);
            var asset = ScriptableObject.CreateInstance(type);
            try
            {
                var plugin = (ITexturePaintCommandExtensionV2)asset;
                var p = new TexturePaintPluginParameterSet();
                p.ResetToDefaults(plugin.Descriptor);
                mesh.uv = new[] { Vector2.up, Vector2.zero, Vector2.one };
                p.Get("density").number = 1;
                string before = JsonUtility.ToJson(p);
                var first = await Generate(plugin, p);
                var second = await Generate(plugin, p);
                foreach (var channel in new[] { TexturePaintChannel.Albedo,
                    TexturePaintChannel.Roughness, TexturePaintChannel.NormalControl })
                {
                    Assert.That(first[channel].Count(c => c.a > .05f), Is.GreaterThan(10),
                        "World-aligned draft must produce visible " + channel);
                    Assert.That(second[channel], Is.EqualTo(first[channel]), "Stable roots must regenerate identically.");
                }
                Assert.That(JsonUtility.ToJson(p), Is.EqualTo(before));
                Assert.That(layer.channels, Is.Empty);
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        private Task<Dictionary<TexturePaintChannel, Color[]>> Generate(ITexturePaintCommandExtensionV2 plugin,
            TexturePaintPluginParameterSet values = null, bool mask = false, CancellationToken token = default)
            => TexturePaintPluginPreview.GenerateAsync(store, set, layer, plugin,
                values ?? new TexturePaintPluginParameterSet(), mask, token);

        [TestCase(0, 0, RenderTextureFormat.ARGBHalf)]
        [TestCase(1, 0, RenderTextureFormat.ARGBHalf)]
        [TestCase(0, 1, RenderTextureFormat.ARGBHalf)]
        [TestCase(1, 1, RenderTextureFormat.ARGBHalf)]
        [TestCase(0, 0, RenderTextureFormat.ARGB32)]
        [TestCase(1, 1, RenderTextureFormat.ARGB32)]
        public async Task SkinPoresReachNormalControlAndMergeWithExistingNormals(int projection, int fullSkin, RenderTextureFormat normalFormat)
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UMA.TexturePaint.Examples.VeinsSubdermalGeneratorPlugin"))
                .First(t => t != null);
            var asset = ScriptableObject.CreateInstance(type);
            var compositor = new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            set.channelPackShader = TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute");
            var baseVector = new Vector3(.3f, -.2f, .93f).normalized;
            var baseNormal = new Color(baseVector.x * .5f + .5f, baseVector.y * .5f + .5f, baseVector.z * .5f + .5f, 1);
            set.channels[TexturePaintChannel.NormalControl].Dispose();
            foreach (var channel in new[] { TexturePaintChannel.NormalControl, TexturePaintChannel.Normal })
                set.channels[channel] = new TextureChannelTarget
                {
                    channel = channel, format = channel == TexturePaintChannel.Normal ? normalFormat : RenderTextureFormat.ARGBHalf,
                    composite = EditableTextureTarget.Create("Skin composite", 256, 256,
                        channel == TexturePaintChannel.Normal ? normalFormat : RenderTextureFormat.ARGBHalf),
                    editable = new EditableTextureTarget("Skin normal test", 256, 256,
                        channel == TexturePaintChannel.Normal ? normalFormat : RenderTextureFormat.ARGBHalf, null,
                        channel == TexturePaintChannel.Normal ? baseNormal : new Color(.5f, .5f, .5f, 1))
                };
            using var host = new PluginHost();
            try
            {
                var plugin = (ITexturePaintCommandExtensionV2)asset;
                var p = host.CreateParameters(plugin);
                p.Get("projection").number = projection;
                p.Get("surfaceMode").number = fullSkin;
                p.Get("veinsEnabled").boolean = false;
                p.Get("bruisesEnabled").boolean = false;
                p.Get("mottlingEnabled").boolean = false;
                foreach (string id in new[] { "spotAmount", "freckleAmount", "poreAmount", "redness", "oiliness", "wrinkleAmount" })
                    p.Get(id).number = 0;
                p.Get("poreScale").number = 12;
                await host.ExecutePluginLayerAsync(plugin, store, p,
                    new Dictionary<TextureSet, TexturePaintLayer> { { set, layer } }, null, CancellationToken.None);
                var baseline = ReadFabricPixels(set.GetVisibleTexture(TexturePaintChannel.Normal));
                p.Get("poreAmount").number = .45f;
                await host.ExecutePluginLayerAsync(plugin, store, p,
                    new Dictionary<TextureSet, TexturePaintLayer> { { set, set.layers[0] } }, null, CancellationToken.None);
                var heights = ReadFabricPixels(set.GetVisibleTexture(TexturePaintChannel.NormalControl));
                var normals = ReadFabricPixels(set.GetVisibleTexture(TexturePaintChannel.Normal));
                int recessed = 0, changed = 0, precise = 0;
                for (int y = 8; y < 240; y++) for (int x = 8; x < 240 - y; x++)
                {
                    int i = y * 256 + x;
                    Assert.That(baseline[i].r, Is.EqualTo(baseNormal.r).Within(.003f));
                    if (heights[i].r < .499f) recessed++;
                    if (Mathf.Abs(normals[i].r - baseline[i].r) + Mathf.Abs(normals[i].g - baseline[i].g) > .0003f) changed++;
                    if (heights[i].r < .5f && heights[i].r > .499f) precise++;
                    var n = new Vector3(normals[i].r * 2 - 1, normals[i].g * 2 - 1, normals[i].b * 2 - 1);
                    Assert.That(n.magnitude, Is.EqualTo(1f).Within(.007f));
                }
                Assert.That(recessed, Is.GreaterThan(100), "Pore defaults must produce recessed height.");
                Assert.That(precise, Is.GreaterThan(100), "Sub-byte height detail must survive generation and composition.");
                Assert.That(changed, Is.GreaterThan(100), "Height must perturb the existing normal map.");
                var rawNormal = ReadFabricPixels(set.GetChannel(TexturePaintChannel.Normal).editable.Front);
                Assert.That(rawNormal[256 * 20 + 20].r, Is.EqualTo(baseNormal.r).Within(.003f), "Source normals remain intact.");
                Assert.That(host.Undo(), Is.True);
                var undone = ReadFabricPixels(set.GetVisibleTexture(TexturePaintChannel.Normal));
                Assert.That(undone[256 * 20 + 20].r, Is.EqualTo(baseline[256 * 20 + 20].r).Within(.001f));
            }
            finally { set.compositor = null; compositor.Dispose(); UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public async Task DraftUses128DimensionsAndCurrentParametersWithoutCommitting()
        {
            var plugin = new DraftPlugin();
            var values = new TexturePaintPluginParameterSet(); values.Get("color", true).color = Color.red;
            var revision = set.channels[TexturePaintChannel.Albedo].editable.Revision;
            string before = JsonUtility.ToJson(values);
            var first = await Generate(plugin, values);
            values.Get("color").color = Color.blue;
            var second = await Generate(plugin, values);
            Assert.That(first[TexturePaintChannel.Albedo], Has.Length.EqualTo(128 * 128));
            Assert.That(first[TexturePaintChannel.Albedo][0], Is.EqualTo(Color.red));
            Assert.That(second[TexturePaintChannel.Albedo][0], Is.EqualTo(Color.blue));
            Assert.That(set.layers, Has.Count.EqualTo(1));
            Assert.That(layer.channels, Is.Empty);
            Assert.That(set.channels[TexturePaintChannel.Albedo].editable.Revision, Is.EqualTo(revision));
            values.Get("color").color = Color.red;
            Assert.That(JsonUtility.ToJson(values), Is.EqualTo(before), "Plugin edits must affect only its cloned parameters.");
        }

        [Test]
        public async Task ClothCloseupShowsDefaultWeaveWithoutChangingAuthoredScaleOrColors()
        {
            Type type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                type ??= assembly.GetType("UMA.TexturePaint.Examples.ClothTextureGeneratorPlugin");
            var asset = ScriptableObject.CreateInstance(type);
            try
            {
                var plugin = (ITexturePaintCommandExtensionV2)asset;
                var p = new TexturePaintPluginParameterSet(); p.ResetToDefaults(plugin.Descriptor);
                p.Get("baseColor").color = new Color(.15f, .3f, .5f, 1);
                PluginManagerWindow.UseClothBaseColor(p);
                Assert.That(p.Float("threadColorAmount"), Is.Zero);
                string before = JsonUtility.ToJson(p);
                var full = await TexturePaintPluginPreview.GenerateDraftAsync(store, set, layer, plugin, p, false, default);
                var swatch = await TexturePaintPluginPreview.GenerateDraftAsync(store, set, layer, plugin, p, false, default, 8f);
                float fullRange = Range(full.output[TexturePaintChannel.NormalControl]);
                Assert.That(fullRange, Is.LessThan(.005f), "Unresolved full-fabric threads must not alias.");
                Assert.That(Range(swatch.output[TexturePaintChannel.NormalControl]), Is.GreaterThan(.03f),
                    "A close-up must show the thread relief hidden by the low-resolution full view.");
                Assert.That(JsonUtility.ToJson(p), Is.EqualTo(before));
                Assert.That(layer.channels, Is.Empty);
                var image = new Texture2D(128,128,TextureFormat.RGBA32,false,false);
                try
                {
                    var pixels = swatch.output[TexturePaintChannel.Albedo];
                    var display = new Color[pixels.Length];
                    for(int i=0;i<pixels.Length;i++) display[i]=pixels[i].gamma;
                    image.SetPixels(display); image.Apply();
                    System.IO.File.WriteAllBytes("Library/cloth-review.png", image.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        private static float Range(Color[] pixels)
        {
            float min=1f,max=0f;
            foreach(var pixel in pixels) { min=Mathf.Min(min,pixel.r); max=Mathf.Max(max,pixel.r); }
            return max-min;
        }

        [Test]
        public async Task QuiltSpriteSetDraftUsesSelectedLeatherTileAndRoughnessWithoutCommitting()
        {
            Type type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                type ??= assembly.GetType("UMA.TexturePaint.Examples.TextileSurfaceGeneratorPlugin");
            var plugin = ScriptableObject.CreateInstance(type);
            try
            {
                var spriteSet = UnityEditor.AssetDatabase.LoadAssetAtPath<OverlayPainterSpriteSet>(
                    TexturePaintAssets.ResolveInstallAssetPath("OverlayPainter/Textures/LeatherSpriteSet.asset"));
                Assert.That(spriteSet, Is.Not.Null);
                var parameters = new TexturePaintPluginParameterSet();
                parameters.ResetToDefaults(((ITexturePaintCommandExtensionV2)plugin).Descriptor);
                parameters.Get("mode").number = 0;
                parameters.Get("scale").number = 2;
                var input = parameters.Get("quiltSpriteSet"); input.spriteSet = spriteSet;
                input.spriteSetSelectionExplicit = true; input.enabledSpriteIndices.Add(3);
                var before = JsonUtility.ToJson(parameters);
                var result = await Generate((ITexturePaintCommandExtensionV2)plugin, parameters);
                Assert.That(result[TexturePaintChannel.Albedo].Length, Is.EqualTo(128 * 128));
                Assert.That(Array.Exists(result[TexturePaintChannel.Roughness], pixel => pixel.a > .9f && pixel.r > .1f), Is.True);
                Assert.That(Array.Exists(result[TexturePaintChannel.NormalControl], pixel => pixel.r > .53f), Is.True);
                Assert.That(JsonUtility.ToJson(parameters), Is.EqualTo(before));
                Assert.That(layer.channels, Is.Empty);
            }
            finally { UnityEngine.Object.DestroyImmediate(plugin); }
        }

        [Test]
        public async Task RectangularMaskFillsSquareDraftAndRemainsUntouched()
        {
            layer.layerMask = new TexturePaintLayerMask { target = new EditableTextureTarget("Mask test",
                256, 64, RenderTextureFormat.ARGB32, null, Color.white) };
            var revision = layer.layerMask.target.Revision;
            var result = await Generate(new DraftPlugin { maskOpacity = .5f }, mask: true);
            Assert.That(result[TexturePaintChannel.Custom][128 * 128 - 1].r, Is.EqualTo(.7f).Within(.001f));
            Assert.That(result[TexturePaintChannel.Custom][0].a, Is.EqualTo(1));
            Assert.That(layer.layerMask.target.Revision, Is.EqualTo(revision));
            Assert.That(layer.layerMask.target.Width, Is.EqualTo(256));
        }

        [TestCase(0)] [TestCase(1)]
        public async Task RealClothSupportsFlatAndTriplanarWithoutRepairingTheLiveMesh(int projection)
        {
            Type pluginType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                pluginType ??= assembly.GetType("UMA.TexturePaint.Examples.ClothTextureGeneratorPlugin");
            Assert.That(pluginType, Is.Not.Null);
            var asset = ScriptableObject.CreateInstance(pluginType);
            try
            {
                var values = new TexturePaintPluginParameterSet(); values.Get("projection", true).number = projection;
                var result = await Generate((ITexturePaintCommandExtensionV2)asset, values);
                Assert.That(result.ContainsKey(TexturePaintChannel.Albedo), Is.True);
                Assert.That(Array.Exists(result[TexturePaintChannel.Albedo], p => p.a > .1f), Is.True);
                Assert.That(mesh.normals, Is.Empty, "Draft mesh maps must not repair the original mesh.");
                Assert.That(layer.channels, Is.Empty);
                Assert.That(values.values, Has.Count.EqualTo(1), "Hydration must use a clone.");
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public async Task CancellationAfterPluginCompletionDiscardsItsOutput()
        {
            using var cancellation = new CancellationTokenSource();
            var plugin = new DraftPlugin { completed = cancellation.Cancel };
            try { await Generate(plugin, token: cancellation.Token); Assert.Fail("Cancelled output was returned."); }
            catch (OperationCanceledException) { }
            Assert.That(layer.channels, Is.Empty);
        }

        [Test]
        public async Task FilterReadsSquareSnapshotOfRectangularInput()
        {
            var result = await Generate(new InputFilter());
            Assert.That(result[TexturePaintChannel.Albedo][128 * 128 - 1], Is.EqualTo(Color.black));
            Assert.That(layer.channels, Is.Empty);
        }

        private sealed class InputFilter : ITexturePaintFilterV2
        {
            public TexturePaintPluginDescriptor Descriptor { get; } = new TexturePaintPluginDescriptor {
                id = "com.uma.tests.preview-filter", displayName = "Draft filter",
                capabilities = TexturePaintPluginCapability.Filter, declaredChannels = TexturePaintChannelMask.Albedo };
            public Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                var source = context.source.Get("preview-test", TexturePaintChannel.Albedo);
                Assert.That(source.width, Is.EqualTo(128)); Assert.That(source.height, Is.EqualTo(128));
                var pixels = new Color[source.width * source.height];
                for (int y = 0; y < source.height; y++)
                for (int x = 0; x < source.width; x++) pixels[y * source.width + x] = source.GetPixel(x, y);
                context.WriteTile("preview-test", TexturePaintChannel.Albedo,
                    new RectInt(0, 0, source.width, source.height), pixels,
                    TexturePaintPluginColorSpace.Linear, TexturePaintPluginBlend.Replace);
                return Task.CompletedTask;
            }
        }

        [Test]
        public void NormalTilesNormalizeVectorsAndMaskTilesUseScalarCoverage()
        {
            var command = new TexturePaintPluginTileCommand { surfaceId = "s", channel = TexturePaintChannel.Normal,
                rect = new RectInt(0, 0, 1, 1), opacity = 1, pixels = new[] { new Color(1, 1, 1, .5f) } };
            Color normal = TexturePaintPluginPreview.Rasterize(new[] { command }, "s", false, default)
                [TexturePaintChannel.Normal][0];
            Assert.That(new Vector3(normal.r * 2 - 1, normal.g * 2 - 1, normal.b * 2 - 1).magnitude,
                Is.EqualTo(1).Within(.001f));
            command.target = TexturePaintPluginTarget.LayerMask; command.opacity = .5f;
            command.pixels[0] = new Color(.4f, .4f, .4f, 0);
            Color mask = TexturePaintPluginPreview.Rasterize(new[] { command }, "s", true, default)
                [TexturePaintChannel.Normal][0];
            Assert.That(mask.r, Is.EqualTo(.2f).Within(.001f)); Assert.That(mask.a, Is.EqualTo(1));
        }

        [Test]
        public async Task EveryLayerEffectAppearsInPluginPreview()
        {
            var plugin=new DraftPlugin { shaped=true };
            var values=new TexturePaintPluginParameterSet();values.Get("color",true).color=new Color(.3f,.4f,.5f,1);
            var baseline=(await TexturePaintPluginPreview.GenerateAsync(store,set,layer,plugin,values,false,default))[TexturePaintChannel.Albedo];
            foreach(TexturePaintLayerEffectKind kind in Enum.GetValues(typeof(TexturePaintLayerEffectKind)))
            {
                layer.effects=new TexturePaintLayerEffects();
                TexturePaintPathGeneratorTests.ConfigureVisibleEffect(layer.effects.Get(kind));
                var styled=(await TexturePaintPluginPreview.GenerateAsync(store,set,layer,plugin,values,false,default))[TexturePaintChannel.Albedo];
                Assert.That(styled.Zip(baseline,(a,b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)+Mathf.Abs(a.a-b.a)).Average(),
                    Is.GreaterThan(.0001f),kind.ToString());
            }
        }

        private sealed class DraftPlugin : ITexturePaintGeneratorV2, ITexturePaintDynamicChannelUsageV2
        {
            public bool shaped;
            public Action completed;
            public float maskOpacity = 1;
            public TexturePaintPluginDescriptor Descriptor { get; } = new TexturePaintPluginDescriptor {
                id = "com.uma.tests.preview", displayName = "Draft", capabilities = TexturePaintPluginCapability.Generator,
                declaredChannels = TexturePaintChannelMask.Albedo, supportedTargets = TexturePaintPluginTarget.All };
            public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) => TexturePaintChannelMask.None;
            public Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                var mask = context.source.GetMask("preview-test");
                var info = context.source.GetChannelInfo("preview-test", TexturePaintChannel.Albedo);
                int width = mask?.width ?? info.width, height = mask?.height ?? info.height;
                Assert.That(width, Is.EqualTo(128)); Assert.That(height, Is.EqualTo(128));
                var pixels = new Color[width * height];
                Color color = mask != null ? new Color(.4f, .4f, .4f, 0) : context.parameters.Get("color")?.color ?? Color.white;
                for (int i = 0; i < pixels.Length; i++)
                {
                    float radius=new Vector2(i%width-width*.5f,i/width-height*.5f).magnitude;
                    pixels[i] = !shaped || (radius>18 && radius<46) ? color : Color.clear;
                }
                if (mask != null) context.WriteMaskTile("preview-test", new RectInt(0, 0, width, height), pixels,
                    opacity: maskOpacity);
                else context.WriteTile("preview-test", TexturePaintChannel.Albedo, new RectInt(0, 0, width, height),
                    pixels, TexturePaintPluginColorSpace.Linear, TexturePaintPluginBlend.Replace);
                context.parameters.Get("private", true).number = 123;
                completed?.Invoke();
                return Task.CompletedTask;
            }
        }
    }
}
#endif
