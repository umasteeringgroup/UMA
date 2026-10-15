#if UNITY_INCLUDE_TESTS
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UMA.TexturePaint.Examples;
using UnityEngine;
namespace UMA.TexturePaint.Tests
{
    public sealed partial class PluginApiV2Tests
    {
        [Test,Category("SurfaceDetailGenerators")]
        public async Task WorldMappedDepositFollowsWorldPositionWhenUVsAreMirrored()
        {
            mesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.one-Vector3.forward};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.triangles=new[]{0,1,2,1,3,2};mesh.RecalculateNormals();
            set.surface.triangleIslands=new[]{0,0};
            foreach(var target in set.channels.Values){target.editable.Dispose();target.editable=new EditableTextureTarget(target.channel.ToString(),64,64,RenderTextureFormat.ARGBHalf,null,Color.clear);}
            var plugin=ScriptableObject.CreateInstance<SplashDepositionGeneratorPlugin>();
            try
            {
                var p=host.CreateParameters(plugin);p.Get("mapping").number=1;p.Get("worldSpan").number=1;p.Get("centerX").number=.3f;
                await host.ExecuteCommandAsync(plugin,store,p,null,CancellationToken.None);var a=Pixels(set.layers[0].channels[TexturePaintChannel.Roughness].Front);
                Assert.That(host.Undo(),Is.True);mesh.uv=new[]{Vector2.right,Vector2.zero,Vector2.one,Vector2.up};
                // This test changes the reconstructed mesh in place. A real new target has
                // fresh maps; discard the first target's baked UV mapping before comparison.
                set.proceduralMeshMaps?.Dispose();set.proceduralMeshMaps=null;
                await host.ExecuteCommandAsync(plugin,store,p,null,CancellationToken.None);var b=Pixels(set.layers[0].channels[TexturePaintChannel.Roughness].Front);
                float difference=0,signal=0;for(int y=2;y<62;y++)for(int x=2;x<62;x++){difference+=Mathf.Abs(a[y*64+x].a-b[y*64+63-x].a);signal+=a[y*64+x].a;}
                Assert.That(signal,Is.GreaterThan(10));Assert.That(difference/3600,Is.LessThan(.025f),"World-space runoff must not rotate with UV islands.");
            }
            finally{Object.DestroyImmediate(plugin);}
            Color[] Pixels(RenderTexture source)
            {
                var previous=RenderTexture.active;var texture=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
                try{RenderTexture.active=source;texture.ReadPixels(new Rect(0,0,64,64),0,0);texture.Apply();return texture.GetPixels();}
                finally{RenderTexture.active=previous;Object.DestroyImmediate(texture);}
            }
        }
        [TestCase(false)][TestCase(true)][Category("SurfaceDetailGenerators")]
        public async Task SurfaceDetailPluginHistoryAndCancellation(bool splash)
        {
            var plugin=splash?(SurfaceDepositionPlugin)ScriptableObject.CreateInstance<SplashDepositionGeneratorPlugin>():ScriptableObject.CreateInstance<SurfaceResidueGeneratorPlugin>();
            try
            {
                var p=host.CreateParameters(plugin);p.Get("size").number=.35f;p.Get("centerX").number=.25f;p.Get("centerY").number=.25f;
                await host.ExecuteCommandAsync(plugin,store,p,null,CancellationToken.None);
                Assert.That(set.layers.Count,Is.EqualTo(1));Assert.That(set.layers[0].channels.ContainsKey(TexturePaintChannel.Roughness),Is.True);
                Assert.That(set.layers[0].channels.ContainsKey(TexturePaintChannel.Normal),Is.False);
                Assert.That(host.Undo(),Is.True);Assert.That(set.layers.Count,Is.Zero);Assert.That(host.Redo(),Is.True);
                var original=set.layers[0];using var cancel=new CancellationTokenSource();cancel.Cancel();
                Assert.CatchAsync<System.OperationCanceledException>(async()=>await host.ExecuteCommandAsync(plugin,store,p,null,cancel.Token));
                Assert.That(set.layers[0],Is.SameAs(original));
            }
            finally{Object.DestroyImmediate(plugin);}
        }
    }
}
#endif
