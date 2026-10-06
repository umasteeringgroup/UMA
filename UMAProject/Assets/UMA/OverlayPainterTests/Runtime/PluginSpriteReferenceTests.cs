#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed partial class PluginApiV2Tests
    {
        private sealed class OptionalSpriteProbe : ITexturePaintGeneratorV2,
            ITexturePaintDynamicChannelUsageV2
        {
            public bool executed;
            public TexturePaintReadOnlyParameterTexture captured;
            public TexturePaintPluginDescriptor Descriptor { get; } = new TexturePaintPluginDescriptor
            {
                id="com.uma.tests.optional-sprite",displayName="Optional sprite",
                capabilities=TexturePaintPluginCapability.Generator,
                declaredChannels=TexturePaintChannelMask.Albedo,
                parameters=new List<TexturePaintPluginParameterDefinition>
                {
                    new TexturePaintPluginParameterDefinition{id="pattern",type=TexturePaintPluginParameterType.Sprite}
                }
            };
            public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters)
                =>TexturePaintChannelMask.None;
            public Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                captured=context.GetTextureParameter("pattern");executed=true;
                context.WriteTile("surface",TexturePaintChannel.Albedo,new RectInt(1,1,1,1),new[]{Color.red},
                    TexturePaintPluginColorSpace.Linear,TexturePaintPluginBlend.Replace);
                return Task.CompletedTask;
            }
        }

        [TestCase(false,false)]
        [TestCase(false,true)]
        [TestCase(true,false)]
        [TestCase(true,true)]
        public async Task MissingOptionalSpriteReferencesDoNotPreventGeneration(bool savedParameters,bool pluginLayer)
        {
            var plugin=new OptionalSpriteProbe();var atlas=new Texture2D(2,2);
            Sprite sprite=Sprite.Create(atlas,new Rect(0,0,2,2),new Vector2(.5f,.5f));
            try
            {
                var parameters=host.CreateParameters(plugin);parameters.Get("pattern").sprite=sprite;
                string saved=JsonUtility.ToJson(parameters);
                Object.DestroyImmediate(sprite);
                if(savedParameters)parameters=JsonUtility.FromJson<TexturePaintPluginParameterSet>(saved);
                Assert.That(parameters.Sprite("pattern")==null,Is.True,"The native sprite is missing");
                if(!savedParameters)
                    Assert.That(ReferenceEquals(parameters.Sprite("pattern"),null),Is.False,
                        "Exercise a destroyed Unity object whose managed wrapper is still present");
                host.SnapshotMemoryBudgetBytes=1;
                if(pluginLayer)
                {
                    var layer=set.AddPluginLayer("Saved optional sprite");layer.pluginId=plugin.Descriptor.id;
                    await host.ExecutePluginLayerAsync(plugin,store,parameters,
                        new Dictionary<TextureSet,TexturePaintLayer>{{set,layer}},null,CancellationToken.None);
                }
                else await host.ExecuteCommandAsync(plugin,store,parameters,null,CancellationToken.None);
                Assert.That(plugin.executed,Is.True);
                Assert.That(plugin.captured,Is.Null,"Missing sprites provide no parameter texture snapshot");
                Assert.That(set.layers,Has.Count.EqualTo(1));
                Assert.That(Read(set.layers[0].channels[TexturePaintChannel.Albedo].Front,1,1).r,Is.GreaterThan(.9f));
            }
            finally
            {
                if(sprite!=null)Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(atlas);
            }
        }
    }
}
#endif
