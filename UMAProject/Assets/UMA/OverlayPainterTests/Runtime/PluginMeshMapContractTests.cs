#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.TexturePaint.Tests
{
    public sealed partial class PluginApiV2Tests
    {
        private sealed class DynamicMeshMapProbe : ITexturePaintGeneratorV2,
            ITexturePaintDynamicChannelUsageV2,ITexturePaintDynamicMeshMapUsageV2
        {
            public TexturePaintMeshMapMask requested;
            public bool executed;
            public TexturePaintPluginDescriptor Descriptor {get;}=new TexturePaintPluginDescriptor
            {
                id="com.uma.tests.dynamic-mesh-maps",displayName="Dynamic mesh-map probe",
                capabilities=TexturePaintPluginCapability.Generator|TexturePaintPluginCapability.ReadsMeshMaps,
                declaredChannels=TexturePaintChannelMask.Albedo,
                requiredMeshMaps=TexturePaintMeshMapMask.WorldPosition
            };
            public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters)=>TexturePaintChannelMask.None;
            public TexturePaintMeshMapMask ResolveMeshMaps(TexturePaintPluginParameterSet parameters)=>requested;
            public Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                executed=true;
                Assert.That(context.GetMeshMap("surface",TexturePaintMeshMap.WorldPosition),Is.Null);
                Assert.That(context.GetMeshMap("surface",TexturePaintMeshMap.WorldNormal),Is.Null);
                return Task.CompletedTask;
            }
        }

        [Test]
        public async Task DynamicMeshMapNoneAvoidsUnneededSnapshotMemory()
        {
            var plugin=new DynamicMeshMapProbe{requested=TexturePaintMeshMapMask.None};
            host.SnapshotMemoryBudgetBytes=1;
            await host.ExecuteCommandAsync(plugin,store,null,null,CancellationToken.None);
            Assert.That(plugin.executed,Is.True);
            Assert.That(set.layers,Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DynamicMeshMapRequestsCannotExceedTheDeclaredContract(bool pluginLayer)
        {
            var plugin=new DynamicMeshMapProbe{requested=TexturePaintMeshMapMask.WorldNormal};
            LogAssert.Expect(LogType.Error,new System.Text.RegularExpressions.Regex(
                "Overlay Painter plugin com\\.uma\\.tests\\.dynamic-mesh-maps: "+
                (pluginLayer?"Plugin layer regeneration failed":"Transaction failed")));
            if(pluginLayer)
            {
                var layer=set.AddPluginLayer("Dynamic contract test");
                var destinations=new Dictionary<TextureSet,TexturePaintLayer>{{set,layer}};
                var error=Assert.ThrowsAsync<InvalidOperationException>(async()=>
                    await host.ExecutePluginLayerAsync(plugin,store,null,destinations,null,CancellationToken.None));
                Assert.That(error.Message,Does.Contain("outside its declared mesh-map contract"));
                Assert.That(set.layers[0],Is.SameAs(layer),"Reject before replacing any cached layer");
            }
            else
            {
                var error=Assert.ThrowsAsync<InvalidOperationException>(async()=>
                    await host.ExecuteCommandAsync(plugin,store,null,null,CancellationToken.None));
                Assert.That(error.Message,Does.Contain("outside its declared mesh-map contract"));
                Assert.That(set.layers,Is.Empty);
            }
            Assert.That(plugin.executed,Is.False,"Reject unadvertised inputs before calling plugin code");
        }
    }
}
#endif
