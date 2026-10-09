#if UNITY_INCLUDE_TESTS
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
    public sealed class TexturePaintClothingPresetTests
    {
        [Test]
        public async Task BundledFinishesApplyAndRegenerateTheirCompleteMaterialStacks()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            string[] assets=AssetDatabase.FindAssets("t:TexturePaintMaterialPreset",new[]{TexturePaintClothingPresets.Folder});
            Assert.That(assets.Length,Is.EqualTo(7));
            using var host=new PluginHost();host.Discover();
            foreach(string guid in assets)
            {
                var preset=AssetDatabase.LoadAssetAtPath<TexturePaintMaterialPreset>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.That(preset.thumbnail,Is.Not.Null,preset.name);
                Assert.That(preset.layers.SelectMany(l=>l.channels).All(c=>c.settings.enabled&&c.settings.opacity==1),Is.True,preset.name);
                const int size=32;using var fixture=new TexturePaintGpuTestFixture(Color.gray,size:size);var set=fixture.set;
                using var compositor=new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
                set.compositor=compositor;set.channelPackShader=TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute");
                set.channels[TexturePaintChannel.Albedo].composite=EditableTextureTarget.Create("Preset color",size,size,RenderTextureFormat.ARGBHalf);
                foreach(var channel in new[]{TexturePaintChannel.Normal,TexturePaintChannel.NormalControl,TexturePaintChannel.Roughness})
                {
                    Color neutral=channel==TexturePaintChannel.Normal?new Color(.5f,.5f,1,1):new Color(.5f,.5f,.5f,1);
                    set.channels[channel]=new TextureChannelTarget{channel=channel,format=RenderTextureFormat.ARGBHalf,
                        editable=new EditableTextureTarget("Preset input",size,size,RenderTextureFormat.ARGBHalf,null,neutral),
                        composite=EditableTextureTarget.Create("Preset composite",size,size,RenderTextureFormat.ARGBHalf)};
                }
                var store=new TextureStore();var sets=(List<TextureSet>)typeof(TextureStore).GetField("sets",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(store);sets.Add(set);
                try
                {
                    await TexturePaintMaterialPresetStorage.ApplyAsync(preset,store,new[]{set},host,null,
                        new TexturePaintMaterialPresetApplyOptions{wrapInGroup=false,strictChannels=true,strictPlugins=true},null,CancellationToken.None);
                    Assert.That(set.layers.Count,Is.EqualTo(preset.layers.Count),preset.name);
                    foreach(var layer in set.layers)
                    {
                        Assert.That(layer.pluginStale,Is.False,preset.name+": "+layer.pluginLastError);
                        Assert.That(layer.channels.ContainsKey(TexturePaintChannel.Albedo),Is.True);
                        Assert.That(layer.channels.ContainsKey(TexturePaintChannel.Roughness),Is.True);
                        Assert.That(layer.channels.ContainsKey(TexturePaintChannel.NormalControl),Is.True);
                        Assert.That(layer.GetChannelSettings(TexturePaintChannel.NormalControl).blendMode,Is.EqualTo(TexturePaintBlendMode.Overlay));
                    }
                }
                finally{sets.Clear();store.Dispose();set.compositor=null;}
            }
        }
    }
}
#endif
