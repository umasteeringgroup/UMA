#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        [TestCase(HairSweptShaderGUI.RenderingMode.Cutout)]
        [TestCase(HairSweptShaderGUI.RenderingMode.AlphaBlended)]
        [TestCase(HairSweptShaderGUI.RenderingMode.Hybrid)]
        public void HairRenderingModesPreserveResourcesAndShareGeometry(HairSweptShaderGUI.RenderingMode mode)
        {
            var atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>(); atlas.EnsureIntegrity();
            var original = new Material(Shader.Find(HairSweptShaderGUI.ShaderName)); atlas.material=original;
            original.SetColor("_RootColor",Color.magenta); original.SetFloat("_AlphaDensity",1.7f); original.SetFloat("_RootOpacityFade",.06f); original.SetTexture("_BaseMap",Texture2D.grayTexture);
            groom.Groups[0].atlas=atlas; Material first=null,second=null;
            try
            {
                Undo.IncrementCurrentGroup(); HairSweptShaderGUI.ApplyRendering(groom,atlas,mode); first=atlas.material;second=atlas.secondPassMaterial;
                Assert.That(first,Is.Not.SameAs(original));Assert.That(first.GetColor("_RootColor"),Is.EqualTo(Color.magenta));
                Assert.That(first.GetTexture("_BaseMap"),Is.SameAs(Texture2D.grayTexture));Assert.That(first.GetFloat("_AlphaDensity"),Is.EqualTo(1.7f));
                Assert.That(first.GetFloat("_RootOpacityFade"),Is.EqualTo(.06f));
                Assert.That(first.renderQueue,Is.EqualTo(mode==HairSweptShaderGUI.RenderingMode.AlphaBlended?3000:2450));
                using var build=HairCardMeshGenerator.Build(HairGroomEvaluator.Evaluate(groom));
                if(mode==HairSweptShaderGUI.RenderingMode.Hybrid)
                {
                    Assert.That(second.shader.name,Is.EqualTo(HairSweptShaderGUI.SoftShaderName));Assert.That(second.renderQueue,Is.GreaterThan(first.renderQueue));
                    Assert.That(second.GetFloat("_RootOpacityFade"),Is.EqualTo(.06f));
                    Assert.That(second.GetShaderPassEnabled("ShadowCaster"),Is.False);
                    Assert.That(build.mesh.subMeshCount,Is.EqualTo(2));
                    Assert.That(build.mesh.GetSubMesh(1).indexStart,Is.GreaterThanOrEqualTo(build.mesh.GetSubMesh(0).indexStart+build.mesh.GetSubMesh(0).indexCount));
                    Assert.That(build.mesh.GetIndices(1),Is.EqualTo(build.mesh.GetIndices(0)),"Both passes draw the same vertices, with independent index ranges.");
                    first.SetColor("_TipColor",Color.cyan);HairSweptShaderGUI.SynchronizeHybrid(atlas);
                    Assert.That(second.GetColor("_TipColor"),Is.EqualTo(Color.cyan));Assert.That(second.GetShaderPassEnabled("ShadowCaster"),Is.False);
                }
                else Assert.That(second,Is.Null);
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();Assert.That(atlas.material,Is.SameAs(original));
                Assert.That(original.shader.name,Is.EqualTo(HairSweptShaderGUI.ShaderName));
            }
            finally{Undo.ClearUndo(atlas);if(first!=null)Object.DestroyImmediate(first);if(second!=null)Object.DestroyImmediate(second);Object.DestroyImmediate(original);Object.DestroyImmediate(atlas);}
        }

        [TestCase(false,1)]
        [TestCase(false,4)]
        [TestCase(true,1)]
        [TestCase(true,4)]
        public void HairAlphaCoverageDoesNotClipTwiceOrRequireMsaa(bool blended,int samples)
        {
            var shader=Shader.Find(blended?HairSweptShaderGUI.SoftShaderName:HairSweptShaderGUI.ShaderName);
            Assert.That(shader,Is.Not.Null);
            Assert.That(GraphicsSettings.currentRenderPipeline,Is.Not.Null,"Run URP rendering tests with a URP pipeline assigned.");
            bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            var material=new Material(shader);
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            texture.SetPixels(Enumerable.Repeat(new Color(1,1,1,.4f),4).ToArray());texture.Apply();
            var quad=new Mesh();quad.vertices=new[]{new Vector3(-1,-1,-1),new Vector3(-1,1,-1),new Vector3(1,1,-1),new Vector3(1,-1,-1)};
            quad.normals=Enumerable.Repeat(Vector3.back,4).ToArray();quad.tangents=Enumerable.Repeat(new Vector4(0,1,0,1),4).ToArray();
            quad.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};quad.colors=Enumerable.Repeat(Color.white,4).ToArray();quad.triangles=new[]{0,1,2,0,2,3};
            var rt=new RenderTexture(32,32,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){antiAliasing=samples};rt.Create();
            var pixels=new Texture2D(32,32,TextureFormat.RGBA32,false,true);var old=RenderTexture.active;
            float previous=Shader.GetGlobalFloat("_AlphaToMaskAvailable");
            try
            {
                material.SetTexture("_BaseMap",texture);material.SetFloat("_AlphaDensity",1);material.SetFloat("_Cutoff",.3f);
                material.SetFloat("_AlphaToCoverage",1);material.SetFloat("_DebugView",1);
                Color Draw()
                {
                    using var commands=new CommandBuffer();commands.SetRenderTarget(rt);commands.ClearRenderTarget(true,true,Color.clear);
                    commands.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-1,1,-1,1,.01f,10),true));
                    commands.SetGlobalFloat("_AlphaToMaskAvailable",0);commands.DrawMesh(quad,Matrix4x4.identity,material,0,0);Graphics.ExecuteCommandBuffer(commands);
                    RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,32,32),0,0);pixels.Apply();return pixels.GetPixel(16,16);
                }
                Assert.That(Draw().a,Is.EqualTo(blended?.4f:1f).Within(.035f));
                if(!blended)
                {
                    material.SetFloat("_DitheredOpacity",1);Draw();
                    Assert.That(pixels.GetPixels().Average(p=>(double)p.a),Is.GreaterThan(.95),"Dither must not thin an already accepted constant-alpha strand a second time.");
                    material.SetFloat("_DitheredOpacity",0);
                }
                material.SetFloat("_Cutoff",.8f);
                Assert.That(Draw().a,Is.EqualTo(blended?.4f:0f).Within(.035f),"Blended fibers must not obey the opaque core threshold.");
                material.SetFloat("_AlphaDensity",3);
                Assert.That(Draw().a,Is.EqualTo(1f).Within(.035f));
                material.SetFloat("_RootOpacityFade",.1f);quad.uv2=Enumerable.Repeat(new Vector2(.05f,0),4).ToArray();
                Assert.That(Draw().a,Is.EqualTo(blended?.5f:0f).Within(.035f),"Root fade precedes clipping and uses unflipped strand coordinates.");
                material.SetFloat("_RootOpacityFade",0);quad.uv2=Enumerable.Repeat(Vector2.zero,4).ToArray();
                Assert.That(Draw().a,Is.EqualTo(1f).Within(.035f),"Zero must preserve opaque roots on old materials.");
                material.SetColor("_BaseColor",new Color(1,1,1,0));Assert.That(Draw().a,Is.Zero.Within(.01f));
                material.SetColor("_BaseColor",Color.white);texture.SetPixels(Enumerable.Repeat(Color.clear,4).ToArray());texture.Apply();
                Assert.That(Draw().a,Is.Zero.Within(.01f),"Density must leave atlas background transparent.");
                material.SetFloat("_Cutoff",0);
                Assert.That(Draw().a,Is.Zero.Within(.01f),"A zero cutoff must not make fully transparent atlas background opaque.");
                Assert.That(ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error"),Is.Empty);
            }
            finally{ShaderUtil.allowAsyncCompilation=async;Shader.SetGlobalFloat("_AlphaToMaskAvailable",previous);RenderTexture.active=old;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(pixels);Object.DestroyImmediate(quad);Object.DestroyImmediate(texture);Object.DestroyImmediate(material);}
        }
    }
}
#endif
