#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UMA.TexturePaint.Examples;
using UnityEngine;
namespace UMA.TexturePaint.Tests
{
    [Category("MakeupCoatings")]
    public sealed class MakeupCoatingTests
    {
        public static TexturePaintAnatomy Rig()=>new(){bones=new(){
            new(){name="Head",position=new(0,0,-.4f)},new(){name="LeftEye",position=new(-.5f,0,0)},new(){name="RightEye",position=new(.5f,0,0)},
            new(){name="LipsSuperior",position=new(0,-.9f,.3f)},new(){name="LipsInferior",position=new(0,-1.1f,.3f)},
            new(){name="LeftLips",position=new(-.42f,-1,.25f)},new(){name="RightLips",position=new(.42f,-1,.25f)}}};
        public static TexturePaintPluginParameterSet Makeup(){var p=new TexturePaintPluginParameterSet();p.ResetToDefaults(MakeupFacePaintGeneratorPlugin.Describe());return p;}
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)][TestCase(6)]
        public void MakeupIsMirroredDeterministicAndChannelAligned(int style)
        {
            var p=Makeup();p.Get("style").number=style;var a=new MakeupMaterial(p);var frame=MakeupFaceFrame.Resolve(Rig());
            var b=new MakeupMaterial(JsonUtility.FromJson<TexturePaintPluginParameterSet>(JsonUtility.ToJson(p)));int count=0;
            for(int y=0;y<100;y++)for(int x=0;x<70;x++)
            {
                var q=new Vector3(x/70f,-1.5f+y*.02f,.3f);var s=a.Sample(frame,q,Vector3.forward,.004f);q.x=-q.x;var t=b.Sample(frame,q,Vector3.forward,.004f);
                Assert.That(s.coverage,Is.EqualTo(t.coverage).Within(.00001f));Assert.That(s.height,Is.EqualTo(t.height).Within(.00001f));
                Assert.That(s.Channel(TexturePaintChannel.Albedo).a,Is.EqualTo(s.Channel(TexturePaintChannel.Roughness).a));
                Assert.That(s.Channel(TexturePaintChannel.NormalControl).a,Is.EqualTo(s.coverage));if(s.coverage>.05f)count++;
            }
            Assert.That(count,Is.GreaterThan(10));
        }
        [Test]
        public void MakeupFollowsTransformedRigAndRejectsBackOfHead()
        {
            var rig=Rig();var frame=MakeupFaceFrame.Resolve(rig);var p=Makeup();p.Get("style").number=3;var material=new MakeupMaterial(p);
            var matrix=Matrix4x4.TRS(new Vector3(5,2,-9),Quaternion.Euler(23,64,-17),Vector3.one*1.7f);
            var transformed=MakeupFaceFrame.Resolve(rig.Copy(matrix));var q=new Vector3(.68f,-.52f,.2f);
            Assert.That(material.Sample(frame,q,Vector3.forward,.005f).coverage,Is.EqualTo(material.Sample(transformed,matrix.MultiplyPoint3x4(q),matrix.MultiplyVector(Vector3.forward),.005f).coverage).Within(.00002f));
            Assert.That(material.Sample(frame,q,Vector3.back,.005f).coverage,Is.Zero);
            Assert.Throws<InvalidOperationException>(()=>MakeupFaceFrame.Resolve(null));
        }
        [Test]
        public void PigmentFinishAndOpacityDoNotMoveFeatures()
        {
            var p=Makeup();p.Get("style").number=3;var a=new MakeupMaterial(p);var frame=MakeupFaceFrame.Resolve(Rig());var q=new Vector3(.68f,-.52f,.2f);
            p.Get("color").color=Color.green;p.Get("finish").number=2;var b=new MakeupMaterial(p);var x=a.Sample(frame,q,Vector3.forward,.002f);var y=b.Sample(frame,q,Vector3.forward,.002f);
            Assert.That(x.coverage,Is.EqualTo(y.coverage));Assert.That(y.roughness,Is.LessThan(x.roughness));
            p.Get("opacity").number=0;Assert.That(new MakeupMaterial(p).Sample(frame,q,Vector3.forward,.002f).coverage,Is.Zero);
            p.Get("opacity").number=1;p.Get("side").number=1;Assert.That(new MakeupMaterial(p).Sample(frame,q,Vector3.forward,.002f).coverage,Is.Zero);
        }
        [TestCase(0)][TestCase(4)][TestCase(6)]
        public void BoundaryControlsMoveEveryMakeupPattern(int style)
        {
            var p=Makeup();p.Get("style").number=style;var a=new MakeupMaterial(p);
            p.Get("offsetY").number=.19f;p.Get("width").number=.7f;p.Get("rotation").number=20;
            var b=new MakeupMaterial(p);var frame=MakeupFaceFrame.Resolve(Rig());float difference=0;
            for(int y=0;y<80;y++)for(int x=0;x<60;x++)
            {var q=new Vector3(x/60f,-1.5f+y*.025f,.3f);difference+=Mathf.Abs(a.Sample(frame,q,Vector3.forward,.003f).coverage-b.Sample(frame,q,Vector3.forward,.003f).coverage);}
            Assert.That(difference,Is.GreaterThan(10));
        }
        [Test]
        public void AnatomySnapshotsAreDetached()
        {
            var original=Rig();var context=new TexturePaintReadContextV2(null,null,null,null,new List<string>{"face"},anatomies:new(){{"face",original}});
            original.bones.Clear();var copy=context.GetAnatomy("face");Assert.That(copy.bones.Count,Is.GreaterThan(2));copy.bones.Clear();Assert.That(context.GetAnatomy("face").bones.Count,Is.GreaterThan(2));
        }
        static TexturePaintPluginParameterSet Coating(int style,float amount)
        {
            var plugin=ScriptableObject.CreateInstance<PrintAgingGeneratorPlugin>();try{var p=new TexturePaintPluginParameterSet();p.ResetToDefaults(plugin.Descriptor);p.Get("coatingStyle").number=style;p.Get("coatingAmount").number=amount;return p;}finally{UnityEngine.Object.DestroyImmediate(plugin);}
        }
        [Test]
        public void DisabledCoatingIsExactLegacyResponse()
        {
            var c=new CoatingDamageField(Coating(0,0)).Sample(Vector3.one,Color.magenta,.37f,-.014f,.81f,false);
            Assert.That(c.color,Is.EqualTo(Color.magenta));Assert.That(c.roughness,Is.EqualTo(.37f));Assert.That(c.height,Is.EqualTo(-.014f));Assert.That(c.metallic,Is.EqualTo(.81f));
        }
        [TestCase(0)][TestCase(1)][TestCase(2)]
        public void CoatingsHaveRaisedDamageAndExposedSubstrate(int style)
        {
            var p=Coating(style,1);var a=new CoatingDamageField(p);var b=new CoatingDamageField(p.Clone());float raised=0,recess=0,exposed=0;
            for(int y=0;y<80;y++)for(int x=0;x<80;x++)
            {
                var q=new Vector3(x/80f,y/80f,.1f);var s=a.Sample(q,Color.gray,.3f,0,1,false);var t=b.Sample(q,Color.gray,.3f,0,1,false);
                Assert.That(s.height,Is.EqualTo(t.height));Assert.That(float.IsFinite(s.height+s.roughness+s.metallic),Is.True);
                raised=Mathf.Max(raised,s.height);recess=Mathf.Min(recess,s.height);exposed+=s.exposed;
            }
            Assert.That(raised,Is.GreaterThan(.002f));Assert.That(recess,Is.LessThan(-.005f));Assert.That(exposed,Is.GreaterThan(10));
        }
    }
    public sealed partial class PluginApiV2Tests
    {
        [Test,Category("MakeupCoatings")]
        public void CheapMeshMapsUpgradeWithoutLosingCanceledCache()
        {
            var needed=TexturePaintMeshMapMask.WorldPosition|TexturePaintMeshMapMask.WorldNormal|TexturePaintMeshMapMask.SurfaceId;
            var cheap=set.GetProceduralMeshMaps(32,requestedMaps:needed);
            Assert.That(cheap.ambientOcclusion,Is.Null);Assert.That(cheap.curvature,Is.Null);Assert.That(cheap.exposedEdgeDistance,Is.Null);
            using(var cancel=new CancellationTokenSource())
            {cancel.Cancel();Assert.Throws<OperationCanceledException>(()=>set.GetProceduralMeshMaps(32,new TexturePaintOperationContext(cancel.Token)));}
            Assert.That(set.GetProceduralMeshMaps(32,requestedMaps:needed),Is.SameAs(cheap));
            var full=set.GetProceduralMeshMaps(32);Assert.That(full,Is.Not.SameAs(cheap));
            Assert.That(full.ambientOcclusion,Is.Not.Null);Assert.That(full.curvature,Is.Not.Null);Assert.That(full.thickness,Is.Not.Null);Assert.That(full.exposedEdgeDistance,Is.Not.Null);
            Assert.That(set.GetProceduralMeshMaps(16,requestedMaps:needed),Is.SameAs(full));
        }
        [TestCase(false),TestCase(true),Category("MakeupCoatings")]
        public async Task CoatingHostWritesRaisedAndRecessedMaterialWithSharedAlpha(bool corrosion)
        {
            foreach(var target in set.channels.Values){target.editable.Dispose();target.editable=new EditableTextureTarget(target.channel.ToString(),64,64,RenderTextureFormat.ARGBHalf,null,Color.clear);}
            ScriptableObject asset=corrosion?ScriptableObject.CreateInstance<RustCorrosionGeneratorPlugin>():ScriptableObject.CreateInstance<PrintAgingGeneratorPlugin>();
            try
            {
                var plugin=(ITexturePaintGeneratorV2)asset;var p=host.CreateParameters(plugin);
                p.Get("coatingAmount",true).number=1;p.Get("coatingStyle",true).number=1;p.Get("coatingDensity",true).number=.8f;p.Get("coatingScale",true).number=8;
                p.Get("wholeCoating",true).boolean=true;p.Get("outputMetallic",true).boolean=true;
                await host.ExecuteCommandAsync(plugin,store,p,null,CancellationToken.None);
                var layer=set.layers[0];Assert.That(layer.channels.ContainsKey(TexturePaintChannel.NormalControl),Is.True);
                float min=1,max=0;
                for(int y=2;y<50;y+=2)for(int x=2;x<60-y;x+=2)
                {
                    var a=Read(layer.channels[TexturePaintChannel.Albedo].Front,x,y);var h=Read(layer.channels[TexturePaintChannel.NormalControl].Front,x,y);
                    Assert.That(h.a,Is.EqualTo(a.a).Within(.005f));if(a.a<.5f)continue;min=Mathf.Min(min,h.r);max=Mathf.Max(max,h.r);
                    Assert.That(Read(layer.channels[TexturePaintChannel.Roughness].Front,x,y).a,Is.EqualTo(a.a).Within(.005f));
                }
                Assert.That(min,Is.LessThan(.49f));Assert.That(max,Is.GreaterThan(.501f));
                Assert.That(host.Undo(),Is.True);Assert.That(host.Redo(),Is.True);
            }
            finally{UnityEngine.Object.DestroyImmediate(asset);}
        }
        [Test,Category("MakeupCoatings")]
        public async Task MakeupHostPreservesHistoryAndSharesOutputCoverage()
        {
            mesh.vertices=new[]{new Vector3(-1,-1.6f,.3f),new Vector3(1,-1.6f,.3f),new Vector3(-1,.4f,.3f),new Vector3(1,.4f,.3f)};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.triangles=new[]{0,1,2,1,3,2};mesh.RecalculateNormals();
            set.surface.triangleIslands=new[]{0,0};set.surface.anatomy=MakeupCoatingTests.Rig();
            foreach(var target in set.channels.Values){target.editable.Dispose();target.editable=new EditableTextureTarget(target.channel.ToString(),64,64,RenderTextureFormat.ARGBHalf,null,Color.clear);}
            var plugin=ScriptableObject.CreateInstance<MakeupFacePaintGeneratorPlugin>();
            try
            {
                var p=MakeupCoatingTests.Makeup();p.Get("style").number=3;p.Get("outputNormalControl").boolean=true;
                await host.ExecuteCommandAsync(plugin,store,p,null,CancellationToken.None);
                Assert.That(set.layers.Count,Is.EqualTo(1));var layer=set.layers[0];
                float signal=0;for(int y=0;y<64;y++)for(int x=0;x<64;x++)
                {var a=Read(layer.channels[TexturePaintChannel.Albedo].Front,x,y);var r=Read(layer.channels[TexturePaintChannel.Roughness].Front,x,y);Assert.That(a.a,Is.EqualTo(r.a).Within(.002f));signal+=a.a;}
                Assert.That(signal,Is.GreaterThan(1));Assert.That(layer.channels.ContainsKey(TexturePaintChannel.Normal),Is.False);
                Assert.That(host.Undo(),Is.True);Assert.That(set.layers.Count,Is.Zero);Assert.That(host.Redo(),Is.True);
                using var cancel=new CancellationTokenSource();cancel.Cancel();Assert.CatchAsync<OperationCanceledException>(async()=>await host.ExecuteCommandAsync(plugin,store,p,null,cancel.Token));Assert.That(set.layers[0],Is.SameAs(layer));
            }
            finally{UnityEngine.Object.DestroyImmediate(plugin);}
        }
    }
}
#endif
