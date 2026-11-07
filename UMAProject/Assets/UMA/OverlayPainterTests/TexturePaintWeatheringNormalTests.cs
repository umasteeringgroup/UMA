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
    public sealed class TexturePaintWeatheringNormalTests
    {
        [Test]
        public void NormalDetailDoesNotInventCurvatureAcrossTextureOrIslandBoundaries()
        {
            const int size=32;
            var pixels=new Color[size*size];var ids=new Color[size*size];
            for(int y=0;y<size;y++) for(int x=0;x<size;x++)
            {
                pixels[y*size+x]=x<16 ? new Color(.5f,.5f,1,1) : new Color(.8f,.5f,.9f,1);
                ids[y*size+x]=x<16 ? new Color(0,0,0,1) : new Color(0,1,0,1);
            }
            var normal=(TexturePaintReadOnlyImage)Activator.CreateInstance(typeof(TexturePaintReadOnlyImage),
                BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{"s",TexturePaintChannel.Normal,size,size,false,pixels},null);
            var islands=(TexturePaintReadOnlyMeshMap)Activator.CreateInstance(typeof(TexturePaintReadOnlyMeshMap),
                BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{"s",TexturePaintMeshMap.SurfaceId,size,size,ids},null);
            var helper=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("UMA.TexturePaint.Examples.SurfaceNormalDetail")).First(t=>t!=null);
            var sample=helper.GetMethod("Curvature",BindingFlags.Public|BindingFlags.Static);
            float Read(float u,TexturePaintReadOnlyMeshMap map) => (float)sample.Invoke(null,new object[]{normal,map,u,.25f,4f});
            Assert.That(Read(0,null),Is.EqualTo(0).Within(.00001f),"Texture edges must not wrap to the opposite side.");
            Assert.That(Read(15f/31f,null),Is.GreaterThan(.1f),"The normal discontinuity is detectable without an island guard.");
            Assert.That(Read(15f/31f,islands),Is.EqualTo(0).Within(.00001f),"Unrelated UV islands must not create a false ridge.");
        }

        private static IEnumerable<TestCaseData> Cases()
        {
            foreach (string name in new[] { "Dirtify", "EdgeWear", "Agify", "DrippingCorrosion", "FabricFuzz", "ScratchDent", "RustCorrosion" })
            foreach (bool height in new[] { false, true })
            {
                yield return new TestCaseData(name, height, false);
                if (name == "Dirtify" || name == "EdgeWear" || name == "DrippingCorrosion")
                    yield return new TestCaseData(name, height, true);
            }
        }

        [TestCaseSource(nameof(Cases))]
        public async Task WeatheringUsesOnlyUnderlyingNormalAndHeightDetail(string name, bool height, bool gpu)
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            const int size = 128;
            using var fixture = new TexturePaintGpuTestFixture(Color.white, size: size);
            var set = fixture.set;
            using var compositor = new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            set.channelPackShader = TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute");
            set.channels[TexturePaintChannel.Albedo].composite = EditableTextureTarget.Create("Weathering color",size,size,RenderTextureFormat.ARGBHalf);
            foreach(var channel in new[]{TexturePaintChannel.Normal,TexturePaintChannel.NormalControl,TexturePaintChannel.AmbientOcclusion})
            {
                // Normal output deliberately uses 8-bit precision, while height remains half-float.
                var format = channel == TexturePaintChannel.Normal ? RenderTextureFormat.ARGB32 : RenderTextureFormat.ARGBHalf;
                set.channels[channel] = new TextureChannelTarget { channel=channel,format=format,
                    editable=new EditableTextureTarget("Weathering base",size,size,format,null,Neutral(channel)),
                    composite=EditableTextureTarget.Create("Weathering composite",size,size,format) };
            }
            var store = new TextureStore();
            ((List<TextureSet>)typeof(TextureStore).GetField("sets",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(store)).Add(set);
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("UMA.TexturePaint.Examples."+name+"GeneratorPlugin")).First(t=>t!=null);
            var asset=ScriptableObject.CreateInstance(type);
            var image=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true);
            var detailChannel=height ? TexturePaintChannel.NormalControl : TexturePaintChannel.Normal;
            var pixels=new Color[size*size];
            for(int y=0;y<size;y++) for(int x=0;x<size;x++)
            {
                float t=(x-64f)/5f,bump=Mathf.Exp(-t*t);
                var n=new Vector3(t*bump,0,1).normalized;
                float h=.5f+.4f*bump;
                pixels[y*size+x]=height ? new Color(h,h,h,1) : new Color(n.x*.5f+.5f,.5f,n.z*.5f+.5f,1);
            }
            image.SetPixels(pixels);image.Apply();
            var detail=new TexturePaintLayer {name="Underlying bevel",kind=TexturePaintLayerKind.Paint};
            detail.channels[detailChannel]=new EditableTextureTarget("Bevel",size,size,RenderTextureFormat.ARGBHalf,image,Color.clear);
            detail.GetChannelSettings(detailChannel);set.layers.Add(detail);
            set.layers.Add(new TexturePaintLayer {name="Weathering",kind=TexturePaintLayerKind.Plugin});
            using var host=new PluginHost {GpuGeneratorShader=gpu ? TexturePaintGpuTestFixture.LoadShader("PluginGenerators.compute") : null};
            try
            {
                var plugin=(ITexturePaintCommandExtensionV2)asset;
                var p=host.CreateParameters(plugin);
                foreach(string id in new[]{"breakup","fractalEdge","detectionLevel","cavityInfluence","aoInfluence","dripAmount","corrosionSpreadMeters",
                    "density","pilling","dentAmount","dentRimAmount","pingAmount","pingRimAmount","scratchAmount","scrapeAmount","scratchLip","chipAmount",
                    "spread","pitting","streaking","flaking"}) p.Get(id,true).number=0;
                p.Get("normalCurvature",true).number=16;p.Get("normalDetailStrength",true).number=16;
                p.Get("normalDetailRadius",true).number=16;p.Get("curvatureContrast",true).number=1;
                p.Get("edgeAmount",true).number=1;p.Get("edgeBias",true).number=1;
                p.Get("cavityAmount",true).number=1;
                async Task<Color[]> Generate()
                {
                    set.BindPreviewTextures();
                    await host.ExecutePluginLayerAsync(plugin,store,p,new Dictionary<TextureSet,TexturePaintLayer>{{set,set.layers[1]}},null,CancellationToken.None);
                    return set.layers[1].channels.TryGetValue(TexturePaintChannel.Albedo,out var output)
                        ? TexturePaintGpuTestFixture.ReadPixels(output.Front) : new Color[size*size];
                }
                detail.visible=false;
                var flat=await Generate();
                detail.visible=true;
                var shaped=await Generate();
                Assert.That(shaped.Zip(flat,(a,b)=>Mathf.Abs(a.a-b.a)).Sum(),Is.GreaterThan(1f),"Normal detail must affect coverage on a flat mesh.");
                if(name == "Dirtify" || name == "EdgeWear")
                {
                    p.Get("featureSize").number=8;p.Get("spread").number=1;
                    var spread=await Generate();
                    Assert.That(spread.Sum(c=>c.a),Is.GreaterThan(shaped.Sum(c=>c.a)+1f),
                        "Gap/edge spread must include normal-derived features in neighbor samples.");
                    p.Get("spread").number=0;
                }
                // A covering layer above the generator must not hide its inputs; its own relief/AO
                // outputs must not become inputs on subsequent runs either.
                var upper=new TexturePaintLayer {name="Higher covering layer",kind=TexturePaintLayerKind.Paint};
                foreach(var channel in new[]{TexturePaintChannel.Normal,TexturePaintChannel.NormalControl,TexturePaintChannel.AmbientOcclusion})
                {
                    upper.channels[channel]=new EditableTextureTarget("Higher input",size,size,RenderTextureFormat.ARGBHalf,null,Neutral(channel));
                    upper.GetChannelSettings(channel);
                }
                set.layers.Add(upper);
                Assert.That(await Generate(),Is.EqualTo(shaped),"Regeneration must exclude itself and higher layers.");
                detail.GetChannelSettings(detailChannel).opacity=0;
                Assert.That(await Generate(),Is.EqualTo(flat),"Zero-opacity lower detail must not influence weathering.");
                detail.GetChannelSettings(detailChannel).opacity=1;
                p.Get("normalCurvature",true).number=0;p.Get("normalDetailStrength",true).number=0;
                Assert.That(await Generate(),Is.EqualTo(flat),"Normal influence zero restores mesh-only behavior.");
            }
            finally
            {
                ((List<TextureSet>)typeof(TextureStore).GetField("sets",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(store)).Clear();
                store.Dispose();set.compositor=null;
                UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        private static Color Neutral(TexturePaintChannel channel) => channel == TexturePaintChannel.Normal ? new Color(.5f,.5f,1,1) :
            channel == TexturePaintChannel.NormalControl ? new Color(.5f,.5f,.5f,1) : Color.white;
    }
}
#endif
