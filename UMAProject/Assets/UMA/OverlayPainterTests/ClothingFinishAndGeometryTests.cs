#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UMA.TexturePaint.Examples;
using UMA.TexturePaint.Editor.Tests;
using UnityEngine;
using Object=UnityEngine.Object;

namespace UMA.TexturePaint.Tests
{
    public sealed class ClothingFinishAndGeometryTests
    {
        [Test]
        public void DraftMeshMapsUpgradeForFullGenerationAndCancellationRetainsTheCache()
        {
            using var fixture=new TexturePaintGpuTestFixture(Color.white,size:32);
            var draft=fixture.set.GetProceduralMeshMaps(16);Assert.That(draft.position.width,Is.EqualTo(16));
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(()=>fixture.set.GetProceduralMeshMaps(32,new TexturePaintOperationContext(cancellation.Token)));
            Assert.That(fixture.set.proceduralMeshMaps,Is.SameAs(draft));Assert.That(draft.position,Is.Not.Null);
            var full=fixture.set.GetProceduralMeshMaps(32);Assert.That(full.position.width,Is.EqualTo(32));
            Assert.That(fixture.set.GetProceduralMeshMaps(16),Is.SameAs(full));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public async Task NewGeneratorReliefPreservesUnderlyingHeightAndNormalAndHonorsAuthoredBlend(int mode)
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();const int size=32;
            using var fixture=new TexturePaintGpuTestFixture(Color.gray,size:size);var set=fixture.set;
            using var compositor=new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor=compositor;set.channelPackShader=TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute");
            set.channels[TexturePaintChannel.Albedo].composite=EditableTextureTarget.Create("Finish base",size,size,RenderTextureFormat.ARGBHalf);
            foreach(var channel in new[]{TexturePaintChannel.Normal,TexturePaintChannel.NormalControl})
                set.channels[channel]=new TextureChannelTarget{channel=channel,format=RenderTextureFormat.ARGBHalf,
                    editable=new EditableTextureTarget("Finish input",size,size,RenderTextureFormat.ARGBHalf,null,channel==TexturePaintChannel.Normal?new Color(.6f,.5f,.99f,1):new Color(.5f,.5f,.5f,1)),
                    composite=EditableTextureTarget.Create("Finish composite",size,size,RenderTextureFormat.ARGBHalf)};
            var image=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true);
            var mask=new Texture2D(2,2,TextureFormat.RGBA32,false,true);mask.SetPixels(Enumerable.Repeat(Color.white,4).ToArray());mask.Apply();
            image.SetPixels(Enumerable.Range(0,size*size).Select(i=>{float h=.5f+.12f*Mathf.Sin(i%size*.3f);return new Color(h,h,h,1);}).ToArray());image.Apply();
            var detail=new TexturePaintLayer{name="Existing relief"};detail.channels[TexturePaintChannel.NormalControl]=new EditableTextureTarget("Existing relief",size,size,RenderTextureFormat.ARGBHalf,image,Color.clear);
            set.layers.Add(detail);set.layers.Add(new TexturePaintLayer{name="Finish",kind=TexturePaintLayerKind.Plugin});
            var store=new TextureStore();var sets=(List<TextureSet>)typeof(TextureStore).GetField("sets",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(store);sets.Add(set);
            ScriptableObject asset=mode==0?ScriptableObject.CreateInstance<WetnessSweatGeneratorPlugin>():mode==1?ScriptableObject.CreateInstance<LeatherCoatedFabricGeneratorPlugin>():ScriptableObject.CreateInstance<PrintAgingGeneratorPlugin>();
            using var host=new PluginHost();
            try
            {
                var plugin=(ITexturePaintGeneratorV2)asset;var p=host.CreateParameters(plugin);
                p.Get("controlMask").texture=mask;p.Get("relief").number=0;p.Get("outputAlbedo").boolean=false;p.Get("outputRoughness").boolean=false;
                set.RecomposeAll();set.BindPreviewTextures();var before=TexturePaintGpuTestFixture.ReadPixels(set.GetVisibleTexture(TexturePaintChannel.Normal));
                await Generate();
                var settings=set.layers[1].GetChannelSettings(TexturePaintChannel.NormalControl);
                Assert.That(settings.blendMode,Is.EqualTo(TexturePaintBlendMode.Overlay));Assert.That(settings.opacity,Is.EqualTo(1));
                var neutral=TexturePaintGpuTestFixture.ReadPixels(set.GetVisibleTexture(TexturePaintChannel.Normal));
                Assert.That(before.Zip(neutral,(a,b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)).Max(),Is.LessThan(.005f),"Neutral generator relief must preserve lower height and normals.");
                p.Get("relief").number=.2f;await Generate();
                var raised=TexturePaintGpuTestFixture.ReadPixels(set.GetVisibleTexture(TexturePaintChannel.Normal));
                Assert.That(before.Zip(raised,(a,b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)).Sum(),Is.GreaterThan(.01f));
                set.layers[1].GetChannelSettings(TexturePaintChannel.NormalControl).blendMode=TexturePaintBlendMode.Normal;
                await Generate();Assert.That(set.layers[1].GetChannelSettings(TexturePaintChannel.NormalControl).blendMode,Is.EqualTo(TexturePaintBlendMode.Normal));
                async Task Generate(){await host.ExecutePluginLayerAsync(plugin,store,p,new Dictionary<TextureSet,TexturePaintLayer>{{set,set.layers[1]}},null,CancellationToken.None);set.RecomposeAll();set.BindPreviewTextures();}
            }
            finally{sets.Clear();store.Dispose();set.compositor=null;Object.DestroyImmediate(asset);Object.DestroyImmediate(image);Object.DestroyImmediate(mask);}
        }
        private static TexturePaintGeometryQueries Plates(bool roof,Matrix4x4 transform)
        {
            var vertices=new List<Vector3>{new(-2,-2,0),new(2,-2,0),new(2,2,0),new(-2,2,0)};
            var indices=new List<int>{0,1,2,0,2,3};
            if(roof){vertices.AddRange(new[]{new Vector3(-2,-2,.2f),new Vector3(2,-2,.2f),new Vector3(2,2,.2f),new Vector3(-2,2,.2f)});indices.AddRange(new[]{4,6,5,4,7,6});}
            return new TexturePaintGeometryQueries(vertices.ToArray(),indices.ToArray(),transform);
        }
        [Test] public void AmbientOcclusionDetectsAnOverhangAboveAFlatSurface()
        {
            var open=Plates(false,Matrix4x4.identity);var covered=Plates(true,Matrix4x4.identity);
            Assert.That(open.Occlusion(Vector3.zero,Vector3.forward,64,2,.0001f),Is.EqualTo(1).Within(.0001));
            Assert.That(covered.Occlusion(Vector3.zero,Vector3.forward,64,2,.0001f),Is.LessThan(.4f));
        }
        [Test] public void ThicknessUsesOppositeTrianglesAndNonUniformWorldScale()
        {
            var geometry=Plates(true,Matrix4x4.Scale(new Vector3(2,3,4)));
            Assert.That(geometry.Thickness(new Vector3(0,0,.8f),Vector3.forward,10,.00001f),Is.EqualTo(.8f).Within(.0001f));
            Assert.That(Plates(false,Matrix4x4.identity).Thickness(Vector3.zero,Vector3.forward,10,.00001f),Is.Zero);
        }
        [Test] public void UvBoundariesExcludeInternalDiagonalAndPreserveMirroredWinding()
        {
            Vector2[] uv={Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
            foreach(var indices in new[]{new[]{0,1,2,0,2,3},new[]{2,1,0,3,2,0},new[]{0,1,2,0,2,3,2,1,0,3,2,0}})
            {
                var contours=TexturePaintBoundaryPaths.FromMesh(uv,indices);
                Assert.That(contours.Count,Is.EqualTo(1));Assert.That(contours[0].closed,Is.True);Assert.That(contours[0].points.Count,Is.EqualTo(4));
                var inset=TexturePaintBoundaryPaths.Prepare(contours[0],.1f,0);
                Assert.That(inset.All(p=>p.x>=.099f&&p.x<=.901f&&p.y>=.099f&&p.y<=.901f),Is.True);
            }
        }
        [Test] public void MaskContoursRetainHolesAndDiscardTransparentPixels()
        {
            var pixels=Enumerable.Repeat(Color.white,25).ToArray();pixels[12]=new Color(1,1,1,0);
            var contours=TexturePaintBoundaryPaths.FromMask(pixels,5,5,.5f);
            Assert.That(contours.Count,Is.EqualTo(2));Assert.That(contours.All(c=>c.closed),Is.True);
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public async Task FinishOutputsShareCoverageHaveReliefAndRespectOutputSwitches(int mode)
        {
            ScriptableObject instance=mode==0?ScriptableObject.CreateInstance<WetnessSweatGeneratorPlugin>():mode==1?
                ScriptableObject.CreateInstance<LeatherCoatedFabricGeneratorPlugin>():ScriptableObject.CreateInstance<PrintAgingGeneratorPlugin>();
            try
            {
                var plugin=(ITexturePaintGeneratorV2)instance;using var host=new PluginHost();var parameters=host.CreateParameters(plugin);
                parameters.Get("outputMetallic").boolean=true;
                var commands=await Generate(plugin,parameters);
                Assert.That(commands.Count,Is.EqualTo(4));
                foreach(var command in commands)command.MaterializeCompactPixels();
                var reference=commands.First(c=>c.channel==TexturePaintChannel.Albedo);
                foreach(var command in commands)
                {
                    for(int i=0;i<1024;i++)
                    {
                        Color color=command.GetPixel(i);
                        Assert.That(float.IsNaN(color.r)||float.IsInfinity(color.r),Is.False);
                        Assert.That(color.a,Is.EqualTo(reference.GetPixel(i).a).Within(1f/255));
                        if(i%32<8)Assert.That(color.a,Is.Zero,"The black half of the control mask must protect every channel.");
                    }
                }
                var heights=commands.First(c=>c.channel==TexturePaintChannel.NormalControl);
                Assert.That(Enumerable.Range(0,1024).Any(i=>heights.GetPixel(i).a>.05f&&Mathf.Abs(heights.GetPixel(i).r-.5f)>.0001f),Is.True);
                foreach(var command in commands)command.ReleaseMaterializedCompactPixels();
                parameters.Get("outputNormalControl").boolean=false;
                var withoutHeight=await Generate(plugin,parameters);
                Assert.That(withoutHeight.Any(c=>c.channel==TexturePaintChannel.NormalControl),Is.False);
            }
            finally{Object.DestroyImmediate(instance);}
        }
        private static async Task<IReadOnlyList<TexturePaintPluginTileCommand>> Generate(ITexturePaintGeneratorV2 plugin,TexturePaintPluginParameterSet parameters)
        {
            const int size=32;var info=new Dictionary<string,TexturePaintReadOnlyChannelInfo>();var images=new Dictionary<string,TexturePaintReadOnlyImage>();
            foreach(var channel in new[]{TexturePaintChannel.Albedo,TexturePaintChannel.Roughness,TexturePaintChannel.Metallic,TexturePaintChannel.NormalControl,TexturePaintChannel.Normal})
            {
                string key=TexturePaintReadContextV2.Key("test",channel);info[key]=new TexturePaintReadOnlyChannelInfo("test",channel,size,size,false);
                images[key]=new TexturePaintReadOnlyImage("test",channel,size,size,false,Enumerable.Repeat(channel==TexturePaintChannel.Normal?new Color(.5f,.5f,1,1):new Color(.2f,.3f,.4f,1),size*size).ToArray());
            }
            var mask=Enumerable.Range(0,size*size).Select(i=>i%size<16?Color.clear:Color.white).ToArray();
            var textures=new Dictionary<string,TexturePaintReadOnlyParameterTexture>{{"controlMask",new TexturePaintReadOnlyParameterTexture("controlMask",size,size,false,mask)}};
            var source=new TexturePaintReadContextV2(images,info,null,textures,new List<string>{"test"});
            var context=new TexturePaintCommandContextV2(plugin.Descriptor,source,parameters,CancellationToken.None,null,32L*1024*1024);
            await plugin.ExecuteAsync(context);return context.SealAndSnapshot();
        }
    }
}
#endif
