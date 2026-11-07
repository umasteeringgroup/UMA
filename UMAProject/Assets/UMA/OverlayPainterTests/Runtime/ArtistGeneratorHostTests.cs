#if UNITY_INCLUDE_TESTS
using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UMA.TexturePaint.Examples;
using UnityEngine;
namespace UMA.TexturePaint.Tests
{
    public sealed partial class PluginApiV2Tests
    {
        [Test,Category("ArtistGenerators")]
        public async Task EyePluginCommitIsUndoableAndCancellationKeepsExistingLayers()
        {
            var plugin=ScriptableObject.CreateInstance<EyeIrisGeneratorPlugin>();
            try
            {
                var p=host.CreateParameters(plugin);await host.ExecuteCommandAsync(plugin,store,p,null,CancellationToken.None);
                Assert.That(set.layers.Count,Is.EqualTo(1));Assert.That(set.layers[0].channels.ContainsKey(TexturePaintChannel.Normal),Is.False);
                Assert.That(host.Undo(),Is.True);Assert.That(set.layers.Count,Is.Zero);Assert.That(host.Redo(),Is.True);
                var original=set.layers[0];using var cancel=new CancellationTokenSource();cancel.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async()=>await host.ExecuteCommandAsync(plugin,store,p,null,cancel.Token));
                Assert.That(set.layers[0],Is.SameAs(original));
            }
            finally{UnityEngine.Object.DestroyImmediate(plugin);}
        }
        [Test,Category("ArtistGenerators")]
        public async Task AdvancedEmbroideryGeneratesCoordinatedChannelsThroughHost()
        {
            var plugin=ScriptableObject.CreateInstance<TextileSurfaceGeneratorPlugin>();
            try
            {
                var p=host.CreateParameters(plugin);p.Get("mode").number=1;p.Get("embroideryStyle").number=2;p.Get("patternTiling").number=1;
                await host.ExecuteCommandAsync(plugin,store,p,null,CancellationToken.None);
                var layer=set.layers[0];var a=Read(layer.channels[TexturePaintChannel.Albedo].Front,2,2);
                Assert.That(a.a,Is.GreaterThan(.9f));
                foreach(var channel in new[]{TexturePaintChannel.Roughness,TexturePaintChannel.NormalControl})
                    Assert.That(Read(layer.channels[channel].Front,2,2).a,Is.EqualTo(a.a).Within(.01f));
                Assert.That(Read(layer.channels[TexturePaintChannel.Albedo].Front,0,0).a,Is.LessThan(.01f));
            }
            finally{UnityEngine.Object.DestroyImmediate(plugin);}
        }
    }
}
#endif
