//////////////////////////////////////////////////////
// Shader Packager
// Copyright (c)2021 Jason Booth
//////////////////////////////////////////////////////

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA.ShaderPackager
{
    public class ShaderPackage : ScriptableObject
    {
        public enum SRPTarget
        {
            Standard,
            URP,
            HDRP
        }

        public enum UnityVersion
        {
            Min = 0,
            Unity2018_4 = 20184,
            Unity2019_1 = 20191,
            Unity2019_2 = 20192,
            Unity2019_3 = 20193,
            Unity2019_4 = 20194,
            Unity2020_1 = 20201,
            Unity2020_2 = 20202,
            Unity2020_3 = 20203,
            Unity2021_1 = 20211,
            Unity2021_2 = 20212,
            Unity2021_3 = 20213,
            Unity2022_1 = 20221,
            Unity2022_2 = 20222,
            Unity2022_3 = 20223,
            Max = 30000
        }

        [System.Serializable]
        public class Entry
        {
            public SRPTarget srpTarget = SRPTarget.Standard;
            public UnityVersion UnityVersionMin = UnityVersion.Min;
            public UnityVersion UnityVersionMax = UnityVersion.Max;
            public Shader shader;
            public string shaderSrc;
        }

        public List<Entry> entries = new List<Entry>();
#if __BETTERSHADERS__
      public Shader betterShader;
      public string betterShaderPath;
      public JBooth.BetterShaders.OptionOverrides optionOverrides;
#endif

        public void Pack(bool warnErrors)
        {
#if __BETTERSHADERS__
         if (betterShader != null)
         {
            betterShaderPath = AssetDatabase.GetAssetPath(betterShader);
         }
         if (betterShader == null)
         {
            if (!System.IO.File.Exists(betterShaderPath))
            {
               Debug.LogWarning("Shader Packager: Source shader GUID and path have changed, you will need to manually repack the shaders from source");
               return;
            }
         }
         if (!string.IsNullOrEmpty(betterShaderPath))
         {
            var assetPath = betterShaderPath;
            if (assetPath.EndsWith(".surfshader"))
            {
               entries.Clear();
               ShaderPackage.Entry e = new ShaderPackage.Entry();
               entries.Add(e);
               e.shaderSrc = JBooth.BetterShaders.BetterShaderImporterEditor.BuildExportShader(
                  JBooth.BetterShaders.ShaderBuilder.RenderPipeline.HDRP2019, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.HDRP;
               e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2019_4;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Unity2020_1;

               e = new ShaderPackage.Entry();
               entries.Add(e);
               e.shaderSrc = JBooth.BetterShaders.BetterShaderImporterEditor.BuildExportShader(
                  JBooth.BetterShaders.ShaderBuilder.RenderPipeline.Standard, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.Standard;
               e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2019_4;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Max;

               e = new ShaderPackage.Entry();
               entries.Add(e);
               e.shaderSrc = JBooth.BetterShaders.BetterShaderImporterEditor.BuildExportShader(
                  JBooth.BetterShaders.ShaderBuilder.RenderPipeline.URP2019, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.URP;
               e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2019_4;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Unity2020_1;

               e = new ShaderPackage.Entry();
               entries.Add(e);
               e.shaderSrc = JBooth.BetterShaders.BetterShaderImporterEditor.BuildExportShader(
                  JBooth.BetterShaders.ShaderBuilder.RenderPipeline.HDRP2020, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.HDRP;
               e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2020_2;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Unity2022_1;

               e = new ShaderPackage.Entry();
               entries.Add(e);
               e.shaderSrc = JBooth.BetterShaders.BetterShaderImporterEditor.BuildExportShader(
                  JBooth.BetterShaders.ShaderBuilder.RenderPipeline.HDRP2021, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.HDRP;
               e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2021_2;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Max;


               e = new ShaderPackage.Entry();
               entries.Add(e);
               e.shaderSrc = JBooth.BetterShaders.BetterShaderImporterEditor.BuildExportShader(
                  JBooth.BetterShaders.ShaderBuilder.RenderPipeline.URP2020, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.URP;
               e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2020_2;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Max;
            }
            else if (assetPath.EndsWith(".stackedshader"))
            {
               entries.Clear();
               ShaderPackage.Entry e = new ShaderPackage.Entry();
               entries.Add(e);
               e.shaderSrc = JBooth.BetterShaders.StackedShaderImporterEditor.BuildExportShader(
                  JBooth.BetterShaders.ShaderBuilder.RenderPipeline.HDRP2019, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.HDRP;
               e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2019_4;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Unity2020_1;

               e = new ShaderPackage.Entry();
               entries.Add(e);
               e.shaderSrc = JBooth.BetterShaders.StackedShaderImporterEditor.BuildExportShader(
                  JBooth.BetterShaders.ShaderBuilder.RenderPipeline.Standard, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.Standard;
               e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2019_4;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Max;

               e = new ShaderPackage.Entry();
               entries.Add(e);
               e.shaderSrc = JBooth.BetterShaders.StackedShaderImporterEditor.BuildExportShader(
                  JBooth.BetterShaders.ShaderBuilder.RenderPipeline.URP2019, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.URP;
               e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2019_4;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Unity2020_2;


               e = new ShaderPackage.Entry();
               entries.Add(e);
               e.shaderSrc = JBooth.BetterShaders.StackedShaderImporterEditor.BuildExportShader(
                  JBooth.BetterShaders.ShaderBuilder.RenderPipeline.URP2020, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.URP;
               e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2020_2;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Unity2020_3;

               e = new ShaderPackage.Entry();
               entries.Add(e);
               e.shaderSrc = JBooth.BetterShaders.StackedShaderImporterEditor.BuildExportShader(
                  JBooth.BetterShaders.ShaderBuilder.RenderPipeline.HDRP2020, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.HDRP;
               e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2020_2;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Max;

               e = new ShaderPackage.Entry();
               entries.Add(e);
               e.shaderSrc = JBooth.BetterShaders.StackedShaderImporterEditor.BuildExportShader(
                  JBooth.BetterShaders.ShaderBuilder.RenderPipeline.URP2021, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.URP;
               e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2020_1;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Max;

               e = new ShaderPackage.Entry();
               entries.Add(e);
               e.shaderSrc = JBooth.BetterShaders.StackedShaderImporterEditor.BuildExportShader(
                  JBooth.BetterShaders.ShaderBuilder.RenderPipeline.HDRP2021, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.HDRP;
               e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2020_2;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Max;
            }
         }
#endif

            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e.shader)
                {
#if __BETTERSHADERS__
                    if(betterShader == null)
#endif
                    {
                        if (warnErrors)
                        {
                            Debug.LogError("Shader is null, cannot pack");
                        }
                        break;
                    }
                }
                if (e.UnityVersionMax == ShaderPackage.UnityVersion.Min && e.UnityVersionMin == ShaderPackage.UnityVersion.Min)
                {
                    e.UnityVersionMax = ShaderPackage.UnityVersion.Max;
                }
                if (e.shader != null)
                {
                    var path = AssetDatabase.GetAssetPath(e.shader);
                    e.shaderSrc = System.IO.File.ReadAllText(path);
                }
            }
        }

        public string GetShaderSrc()
        {
            UnityVersion curVersion = UnityVersion.Min;
#if UNITY_2018_4_OR_NEWER
            curVersion = UnityVersion.Unity2018_4;
#endif
#if UNITY_2019_1_OR_NEWER
            curVersion = UnityVersion.Unity2019_1;
#endif
#if UNITY_2019_2_OR_NEWER
            curVersion = UnityVersion.Unity2019_2;
#endif
#if UNITY_2019_3_OR_NEWER
            curVersion = UnityVersion.Unity2019_3;
#endif
#if UNITY_2019_4_OR_NEWER
            curVersion = UnityVersion.Unity2019_4;
#endif
#if UNITY_2020_1_OR_NEWER
            curVersion = UnityVersion.Unity2020_1;
#endif
#if UNITY_2020_2_OR_NEWER
            curVersion = UnityVersion.Unity2020_2;
#endif
#if UNITY_2020_3_OR_NEWER
            curVersion = UnityVersion.Unity2020_3;
#endif
#if UNITY_2021_1_OR_NEWER
      curVersion = UnityVersion.Unity2021_1;
#endif
#if UNITY_2021_2_OR_NEWER
      curVersion = UnityVersion.Unity2021_2;
#endif
#if UNITY_2021_3_OR_NEWER
      curVersion = UnityVersion.Unity2021_3;
#endif
#if UNITY_2022_1_OR_NEWER
      curVersion = UnityVersion.Unity2022_1;
#endif
#if UNITY_2022_2_OR_NEWER
      curVersion = UnityVersion.Unity2022_2;
#endif
#if UNITY_2022_3_OR_NEWER
      curVersion = UnityVersion.Unity2022_3;
#endif

            SRPTarget target = SRPTarget.Standard;
#if USING_HDRP
      target = SRPTarget.HDRP;
#endif
#if USING_URP
      target = SRPTarget.URP;
#endif
            UnityVersion maxFound = UnityVersion.Min;

            string s = null;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (target != e.srpTarget)
                {
                    continue;
                }
                if (e.UnityVersionMax > maxFound)
                {
                    maxFound = e.UnityVersionMax;
                    s = e.shaderSrc;
                }
            
                // default init state..
                if (e.UnityVersionMax == UnityVersion.Min && e.UnityVersionMin == UnityVersion.Min)
                {
                    e.UnityVersionMax = UnityVersion.Max;
                }
                if (curVersion >= e.UnityVersionMin && curVersion <= e.UnityVersionMax)
                {
                    s = e.shaderSrc;
                }
            }
            return s;
        }
    }
}
