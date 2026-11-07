#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace UMA.TexturePaint.Editor.Tests
{
    [Category("MakeupCoatings")]
    public sealed class TexturePaintFaceCoatingPresetTests
    {
        [Test]
        public async Task FinishedPresetsApplyAllChannelsAndMasks()
        {
            for(int i=0;i<TexturePaintFaceCoatingPresets.Names.Length;i++)await Render(i,64);
        }
        public static async Task<Dictionary<TexturePaintChannel,Color[]>> Render(int index,int size)
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using var host=new PluginHost();host.Discover();
            var preset=AssetDatabase.LoadAssetAtPath<TexturePaintMaterialPreset>(TexturePaintFaceCoatingPresets.Folder+"/"+TexturePaintFaceCoatingPresets.Names[index]+".asset");
            Assert.That(preset,Is.Not.Null);Assert.That(preset.layers.SelectMany(l=>l.channels).All(c=>c.settings.enabled&&c.settings.opacity==1),Is.True);
            using var fixture=new TexturePaintGpuTestFixture(Color.gray,size:size);var set=fixture.set;
            if(index<6)
            {
                fixture.mesh.vertices=fixture.mesh.vertices.Select(v=>new Vector3(v.x*2-1,v.y*2-1.6f,.3f)).ToArray();
                set.surface.anatomy=new TexturePaintAnatomy{bones=new(){
                    new(){name="Head",position=new(0,0,-.4f)},new(){name="LeftEye",position=new(-.5f,0,0)},new(){name="RightEye",position=new(.5f,0,0)},
                    new(){name="LipsSuperior",position=new(0,-.9f,.3f)},new(){name="LipsInferior",position=new(0,-1.1f,.3f)},
                    new(){name="LeftLips",position=new(-.42f,-1,.25f)},new(){name="RightLips",position=new(.42f,-1,.25f)}}};
            }
            using var compositor=new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor=compositor;set.channelPackShader=TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute");
            set.channels[TexturePaintChannel.Albedo].composite=EditableTextureTarget.Create("Preset color",size,size,RenderTextureFormat.ARGBHalf);
            foreach(var ch in new[]{TexturePaintChannel.Normal,TexturePaintChannel.NormalControl,TexturePaintChannel.Roughness,TexturePaintChannel.Metallic,TexturePaintChannel.AmbientOcclusion})
            {
                Color neutral=ch==TexturePaintChannel.Normal?new Color(.5f,.5f,1,1):ch==TexturePaintChannel.Metallic?Color.black:ch==TexturePaintChannel.AmbientOcclusion?Color.white:new Color(.5f,.5f,.5f,1);
                set.channels[ch]=new TextureChannelTarget{channel=ch,format=RenderTextureFormat.ARGBHalf,
                    editable=new EditableTextureTarget("Preset input",size,size,RenderTextureFormat.ARGBHalf,null,neutral),
                    composite=EditableTextureTarget.Create("Preset composite",size,size,RenderTextureFormat.ARGBHalf)};
            }
            using var store=new TextureStore();var sets=(List<TextureSet>)typeof(TextureStore).GetField("sets",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(store);sets.Add(set);
            try
            {
                var applied=await TexturePaintMaterialPresetStorage.ApplyAsync(preset,store,new[]{set},host,null,
                    new TexturePaintMaterialPresetApplyOptions{wrapInGroup=false,strictChannels=true,strictPlugins=true},null,CancellationToken.None);
                Assert.That(applied.warnings,Is.Empty);Assert.That(set.layers.Count,Is.EqualTo(preset.layers.Count));
                foreach(var layer in set.layers)
                {
                    Assert.That(layer.pluginStale,Is.False,preset.name+": "+layer.pluginLastError);
                    Assert.That(layer.channels.Count,Is.GreaterThan(0));
                    if(layer.channels.ContainsKey(TexturePaintChannel.NormalControl))Assert.That(layer.GetChannelSettings(TexturePaintChannel.NormalControl).blendMode,Is.EqualTo(TexturePaintBlendMode.Overlay));
                }
                if(index>=6)Assert.That(set.layers.Last().layerMask.effects.stack.Count,Is.EqualTo(1));
                set.RecomposeAll();var output=new Dictionary<TexturePaintChannel,Color[]>();
                foreach(var ch in set.channels.Keys)output[ch]=TexturePaintGpuTestFixture.ReadPixels(set.GetVisibleTexture(ch));
                Assert.That(output[TexturePaintChannel.Albedo].All(c=>float.IsFinite(c.r+c.g+c.b+c.a)),Is.True);
                return output;
            }
            finally{sets.Clear();set.compositor=null;}
        }
    }
}
#endif
