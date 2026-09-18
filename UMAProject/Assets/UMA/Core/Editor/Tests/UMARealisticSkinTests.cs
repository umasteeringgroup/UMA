#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public class UMARealisticSkinTests
    {
        const string OriginalPath = "Assets/UMA/SRP/ShaderGraphs/Materials/UMA3_SkinShader_URP.mat";
        const string Examples = "Assets/UMA/SRP/Shaders/Skin/Examples/";

        [Test]
        public void OriginalGraphIsUnchangedAndEveryInputHasTheSameType()
        {
            var original = AssetDatabase.LoadAssetAtPath<Material>(OriginalPath);
            if (original == null) Assert.Ignore("Original URP content package is not installed.");
            Assert.That(original.shader.name, Is.EqualTo(UMARealisticSkinUtility.OriginalShaderName));
            var shader = Shader.Find(UMARealisticSkinUtility.ShaderName);
            Assert.That(shader, Is.Not.Null);
            for(int i=0;i<original.shader.GetPropertyCount();i++)
            {
                string name=original.shader.GetPropertyName(i);
                // Shader Graph generates hidden lightmapping/selection properties too.
                if(name.StartsWith("unity_") || name.StartsWith("_Selection") || name.StartsWith("_ObjectId")) continue;
                if((original.shader.GetPropertyFlags(i)&ShaderPropertyFlags.HideInInspector)!=0)continue;
                int index=shader.FindPropertyIndex(name);
                Assert.That(index,Is.GreaterThanOrEqualTo(0),name);
                var expected=original.shader.GetPropertyType(i);
                var actual=shader.GetPropertyType(index);
                if(expected==ShaderPropertyType.Float || expected==ShaderPropertyType.Range)
                    Assert.That(actual==ShaderPropertyType.Float || actual==ShaderPropertyType.Range,Is.True,name);
                else Assert.That(actual,Is.EqualTo(expected),name);
            }
        }

        [Test]
        public void MaterialCopyPreservesInputsWithoutChangingSource()
        {
            var original=AssetDatabase.LoadAssetAtPath<Material>(OriginalPath);
            if(original==null)Assert.Ignore("Original URP content package is not installed.");
            var source=new Material(original);
            source.SetColor("_Base_Color",new Color(.4f,.2f,.1f));
            source.SetVector("_Smoothness_Remap",new Vector4(.12f,.62f,0,0));
            source.SetTextureScale("_BaseMap",new Vector2(2,3));
            source.SetTextureOffset("_BaseMap",new Vector2(.1f,.2f));
            string before=EditorJsonUtility.ToJson(source);
            var copy=UMARealisticSkinUtility.CreateMaterialCopy(source);
            try
            {
                Assert.That(EditorJsonUtility.ToJson(source),Is.EqualTo(before));
                Assert.That(copy.shader.name,Is.EqualTo(UMARealisticSkinUtility.ShaderName));
                foreach(string property in new[]{"_BaseMap","_BumpMap","_MaskMap","_Skinmask","_DetailNormalMap"})
                    Assert.That(copy.GetTexture(property),Is.EqualTo(source.GetTexture(property)),property);
                Assert.That(copy.GetColor("_Base_Color"),Is.EqualTo(source.GetColor("_Base_Color")));
                Assert.That(copy.GetVector("_Smoothness_Remap"),Is.EqualTo(source.GetVector("_Smoothness_Remap")));
                Assert.That(copy.GetTextureScale("_BaseMap"),Is.EqualTo(source.GetTextureScale("_BaseMap")));
                Assert.That(copy.GetTextureOffset("_BaseMap"),Is.EqualTo(source.GetTextureOffset("_BaseMap")));
                Assert.That(copy.renderQueue,Is.EqualTo(2000));
                Assert.That(copy.shaderKeywords,Is.Empty);
            }
            finally {Object.DestroyImmediate(source);Object.DestroyImmediate(copy);}
        }

        [Test]
        public void CopyRejectsUnrelatedShaders()
        {
            var source=new Material(Shader.Find("Hidden/InternalErrorShader"));
            try{Assert.Throws<ArgumentException>(()=>UMARealisticSkinUtility.CreateMaterialCopy(source));}
            finally{Object.DestroyImmediate(source);}
            Assert.Throws<ArgumentNullException>(()=>UMARealisticSkinUtility.CreateMaterialCopy(null));
        }

        [TestCase("Natural")]
        [TestCase("Matte")]
        [TestCase("Dewy")]
        public void PresetKeepsUmaAtlasChannelsAndReferences(string preset)
        {
            var original=AssetDatabase.LoadAssetAtPath<UMAMaterial>("Assets/UMA/SRP/ShaderGraphs/Materials/UMA3_SkinShader_URP.asset");
            if(original==null)Assert.Ignore("Original URP content package is not installed.");
            var uma=AssetDatabase.LoadAssetAtPath<UMAMaterial>(Examples+"UMA3_SkinRealistic_"+preset+".asset");
            Assert.That(uma,Is.Not.Null);
            Assert.That(uma.material.shader.name,Is.EqualTo(UMARealisticSkinUtility.ShaderName));
            Assert.That(uma.channels.Length,Is.EqualTo(original.channels.Length));
            for(int i=0;i<uma.channels.Length;i++)
            {
                Assert.That(JsonUtility.ToJson(uma.channels[i]),Is.EqualTo(JsonUtility.ToJson(original.channels[i])));
            }
            foreach(string property in new[]{"_BaseMap","_BumpMap","_MaskMap","_Skinmask","_DetailNormalMap"})
                Assert.That(uma.material.GetTexture(property),Is.Not.Null,property);
            Assert.That(uma.materialType,Is.EqualTo(original.materialType));
        }

        [Test]
        public void ShaderPassesAndLightingVariantsCompile()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)Assert.Ignore("Requires a graphics device: do not pass -nographics.");
            var previousDefault=GraphicsSettings.defaultRenderPipeline;
            var previousQuality=QualitySettings.renderPipeline;
            var pipeline=AssetDatabase.FindAssets("t:RenderPipelineAsset")
                .Select(g=>AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(p=>p!=null && p.GetType().Name=="UniversalRenderPipelineAsset");
            if(pipeline==null)Assert.Ignore("Requires a URP pipeline asset.");
            var cameraObject=new GameObject("Skin shader test camera") {hideFlags=HideFlags.HideAndDontSave};
            var camera=cameraObject.AddComponent<Camera>();camera.cullingMask=0;
            var target=new RenderTexture(16,16,24);
            Material material=null;
            var variants=new ShaderVariantCollection();
            bool oldAsync=ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation=false;
            try
            {
                GraphicsSettings.defaultRenderPipeline=pipeline;
                QualitySettings.renderPipeline=pipeline;
                // Selecting the asset alone does not instantiate URP in a batch-mode test.
                // Without a render, FindPass resolves the built-in fallback instead.
                target.Create();
                RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});
                var shader=Shader.Find(UMARealisticSkinUtility.ShaderName);
                Assert.That(shader,Is.Not.Null);
                material=new Material(shader);
                foreach(var keywords in new[]{Array.Empty<string>(),
                    new[]{"_MAIN_LIGHT_SHADOWS","_ADDITIONAL_LIGHTS","_ADDITIONAL_LIGHT_SHADOWS","_SHADOWS_SOFT"},
                    new[]{"_MAIN_LIGHT_SHADOWS_CASCADE","_ADDITIONAL_LIGHTS","_CLUSTER_LIGHT_LOOP","_SCREEN_SPACE_OCCLUSION","_LIGHT_LAYERS","_LIGHT_COOKIES"},
                    new[]{"_MAIN_LIGHT_SHADOWS_SCREEN","_ADDITIONAL_LIGHTS","_DBUFFER_MRT3"},
                    new[]{"LIGHTMAP_ON","DIRLIGHTMAP_COMBINED","LIGHTMAP_SHADOW_MIXING","SHADOWS_SHADOWMASK"},
                    new[]{"DYNAMICLIGHTMAP_ON"},new[]{"PROBE_VOLUMES_L1"},new[]{"PROBE_VOLUMES_L2"},
                    new[]{"_REFLECTION_PROBE_BLENDING","_REFLECTION_PROBE_BOX_PROJECTION"},
                    new[]{"_ADDITIONAL_LIGHTS_VERTEX","FOG_EXP2","LOD_FADE_CROSSFADE"},
                    new[]{"INSTANCING_ON"}})
                    variants.Add(new ShaderVariantCollection.ShaderVariant(shader,PassType.ScriptableRenderPipeline,keywords));
                variants.Add(new ShaderVariantCollection.ShaderVariant(shader,PassType.ShadowCaster));
                variants.Add(new ShaderVariantCollection.ShaderVariant(shader,PassType.Meta));
                variants.WarmUp();
                foreach(string pass in new[]{"ForwardSkin","ShadowCaster","DepthOnly","DepthNormals","Meta","MotionVectors","XRMotionVectors"})
                {
                    int index=material.FindPass(pass);
                    Assert.That(index,Is.GreaterThanOrEqualTo(0),pass);
                    Assert.That(material.SetPass(index),Is.True,pass);
                }
                Assert.That(ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").Select(m=>m.message),Is.Empty);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation=oldAsync;Object.DestroyImmediate(material);Object.DestroyImmediate(variants);
                target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(cameraObject);
                GraphicsSettings.defaultRenderPipeline=previousDefault;QualitySettings.renderPipeline=previousQuality;
            }
        }
    }
}
#endif
