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
#if UNITY_6000_0_OR_NEWER
        public static UnityEditor.Build.NamedBuildTarget CurrentNamedBuildTarget
        {
            get
            {
#if UNITY_SERVER
                    return NamedBuildTarget.Server;
#else
                BuildTarget buildTarget = EditorUserBuildSettings.activeBuildTarget;
                BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
                UnityEditor.Build.NamedBuildTarget namedBuildTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(targetGroup);
                return namedBuildTarget;
#endif
            }
        }
#endif

        static List<string> GetFlags()
        {
#if UNITY_6000_0_OR_NEWER
            string s = PlayerSettings.GetScriptingDefineSymbols(CurrentNamedBuildTarget);
#else
            string s = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
#endif
            string[] split = s.Split(';');
            return new List<string>(split);
        }

        static void SetFlags(List<string> flags)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < flags.Count; ++i)
            {
                sb.Append(flags[i]);
                sb.Append(";");
            }
#if UNITY_6000_0_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(CurrentNamedBuildTarget, sb.ToString());
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, sb.ToString());
#endif
        }

#if __BETTERSHADERS__
        [MenuItem("Window/Better Lit Shader/Development Mode/Enable")]
        static void EnableDevMode()
        {
            var flags = GetFlags();
            if (!flags.Contains("__BETTERLIT_DEVMODE__"))
            {
                flags.Add("__BETTERLIT_DEVMODE__");
                SetFlags(flags);
            }
        }

        [MenuItem("Window/Better Lit Shader/Development Mode/Disable")]
        static void DisableDevMode()
        {
            var flags = GetFlags();
            if (flags.Remove("__BETTERLIT_DEVMODE__"))
                SetFlags(flags);
        }

        // Validate the menu item defined by the function above.
        // The menu item will be disabled if this function returns false.
        [MenuItem("Window/Better Lit Shader/Development Mode/Enable", true)]
        static bool ValidateEnableDevMode()
        {
            var flags = GetFlags();
            return !flags.Contains("__BETTERLIT_DEVMODE__");
        }
        [MenuItem("Window/Better Lit Shader/Development Mode/Disable", true)]
        static bool ValidateDisableDevMode()
        {
            return GetFlags().Contains("__BETTERLIT_DEVMODE__");
        }
#endif

        public enum SRPTarget
        {
            Standard,
            URP,
            HDRP
        }

        public enum UnityVersion
        {
            Min = 0,
            Unity2021_2 = 20212,
            Unity2021_3 = 20213,
            Unity2022_1 = 20221,
            Unity2022_2 = 20222,
            Unity2022_3 = 20223,
            Unity2023_2 = 20232,
            Unity2023_3 = 20233,
            Max = 30000
        }

        [System.Serializable]
        public class Entry
        {
            public SRPTarget srpTarget = SRPTarget.Standard;
            public UnityVersion UnityVersionMin = UnityVersion.Min;
            public UnityVersion UnityVersionMax = UnityVersion.Max;
            public Shader shader;
            [HideInInspector] public string shaderSrc;
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
                    e.shaderSrc = JBooth.BetterShaders.BetterShaderImporterEditor.BuildExportShader(JBooth.BetterShaders.ShaderBuilder.RenderPipeline.Standard, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.Standard;
                    e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2021_2;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Max;

               e = new ShaderPackage.Entry();
               entries.Add(e);
                    e.shaderSrc = JBooth.BetterShaders.BetterShaderImporterEditor.BuildExportShader(JBooth.BetterShaders.ShaderBuilder.RenderPipeline.HDRP2021, optionOverrides, assetPath);
                    e.srpTarget = ShaderPackage.SRPTarget.HDRP;
                    e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2021_2;
                    e.UnityVersionMax = ShaderPackage.UnityVersion.Unity2022_1;

               e = new ShaderPackage.Entry();
               entries.Add(e);
                    e.shaderSrc = JBooth.BetterShaders.BetterShaderImporterEditor.BuildExportShader(JBooth.BetterShaders.ShaderBuilder.RenderPipeline.URP2021, optionOverrides, assetPath);
                    e.srpTarget = ShaderPackage.SRPTarget.URP;
                    e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2021_2;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Unity2022_1;

               e = new ShaderPackage.Entry();
               entries.Add(e);
                    e.shaderSrc = JBooth.BetterShaders.BetterShaderImporterEditor.BuildExportShader(JBooth.BetterShaders.ShaderBuilder.RenderPipeline.HDRP2022, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.HDRP;
                    e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2022_2;
                    e.UnityVersionMax = ShaderPackage.UnityVersion.Unity2023_3;

               e = new ShaderPackage.Entry();
               entries.Add(e);
                    e.shaderSrc = JBooth.BetterShaders.BetterShaderImporterEditor.BuildExportShader(JBooth.BetterShaders.ShaderBuilder.RenderPipeline.URP2022, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.URP;
                    e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2022_2;
                    e.UnityVersionMax = ShaderPackage.UnityVersion.Unity2023_3;

                    e = new ShaderPackage.Entry();
                    entries.Add(e);
                    e.shaderSrc = JBooth.BetterShaders.BetterShaderImporterEditor.BuildExportShader(JBooth.BetterShaders.ShaderBuilder.RenderPipeline.HDRP2023, optionOverrides, assetPath);
                    e.srpTarget = ShaderPackage.SRPTarget.HDRP;
                    e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2023_3;
                    e.UnityVersionMax = ShaderPackage.UnityVersion.Max;

                    e = new ShaderPackage.Entry();
                    entries.Add(e);
                    e.shaderSrc = JBooth.BetterShaders.BetterShaderImporterEditor.BuildExportShader(JBooth.BetterShaders.ShaderBuilder.RenderPipeline.URP2023, optionOverrides, assetPath);
                    e.srpTarget = ShaderPackage.SRPTarget.URP;
                    e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2023_3;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Max;
            }
            else if (assetPath.EndsWith(".stackedshader"))
            {
               entries.Clear();
               ShaderPackage.Entry e = new ShaderPackage.Entry();
               entries.Add(e);
                    e.shaderSrc = JBooth.BetterShaders.StackedShaderImporterEditor.BuildExportShader(JBooth.BetterShaders.ShaderBuilder.RenderPipeline.Standard, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.Standard;
                    e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2021_2;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Max;

               e = new ShaderPackage.Entry();
               entries.Add(e);
                    e.shaderSrc = JBooth.BetterShaders.StackedShaderImporterEditor.BuildExportShader(JBooth.BetterShaders.ShaderBuilder.RenderPipeline.URP2021, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.URP;
                    e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2021_2;
                    e.UnityVersionMax = ShaderPackage.UnityVersion.Unity2022_1;

               e = new ShaderPackage.Entry();
               entries.Add(e);
                    e.shaderSrc = JBooth.BetterShaders.StackedShaderImporterEditor.BuildExportShader(JBooth.BetterShaders.ShaderBuilder.RenderPipeline.HDRP2021, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.HDRP;
                    e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2022_1;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Max;

               e = new ShaderPackage.Entry();
               entries.Add(e);
                    e.shaderSrc = JBooth.BetterShaders.StackedShaderImporterEditor.BuildExportShader(JBooth.BetterShaders.ShaderBuilder.RenderPipeline.URP2022, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.URP;
                    e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2022_2;
                    e.UnityVersionMax = ShaderPackage.UnityVersion.Unity2023_3;

               e = new ShaderPackage.Entry();
               entries.Add(e);
                    e.shaderSrc = JBooth.BetterShaders.StackedShaderImporterEditor.BuildExportShader(JBooth.BetterShaders.ShaderBuilder.RenderPipeline.HDRP2022, optionOverrides, assetPath);
               e.srpTarget = ShaderPackage.SRPTarget.HDRP;
                    e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2022_2;
                    e.UnityVersionMax = ShaderPackage.UnityVersion.Unity2023_3;

                    e = new ShaderPackage.Entry();
                    entries.Add(e);
                    e.shaderSrc = JBooth.BetterShaders.StackedShaderImporterEditor.BuildExportShader(JBooth.BetterShaders.ShaderBuilder.RenderPipeline.URP2023, optionOverrides, assetPath);
                    e.srpTarget = ShaderPackage.SRPTarget.URP;
                    e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2023_3;
                    e.UnityVersionMax = ShaderPackage.UnityVersion.Max;

                    e = new ShaderPackage.Entry();
                    entries.Add(e);
                    e.shaderSrc = JBooth.BetterShaders.StackedShaderImporterEditor.BuildExportShader(JBooth.BetterShaders.ShaderBuilder.RenderPipeline.HDRP2023, optionOverrides, assetPath);
                    e.srpTarget = ShaderPackage.SRPTarget.HDRP;
                    e.UnityVersionMin = ShaderPackage.UnityVersion.Unity2023_3;
               e.UnityVersionMax = ShaderPackage.UnityVersion.Max;
            }
         }
#endif

            foreach (var e in entries)
                {
                if (e.shader
#if __BETTERSHADERS__
               && betterShader == null
#endif
               )
                    {
                        if (warnErrors)
                        {
                            Debug.LogError("Shader is null, cannot pack");
                        }
                        break;
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

#if UNITY_2023_3_OR_NEWER
      curVersion = UnityVersion.Unity2023_3;
#endif

            SRPTarget target = SRPTarget.Standard;
            if (RenderPipelineDefine.IsHDRP)
            {
      target = SRPTarget.HDRP;
            }
            else if (RenderPipelineDefine.IsURP)
            {
      target = SRPTarget.URP;
            }

            string s = null;
            foreach (var e in entries)
            {
                if (target != e.srpTarget)
                    continue;
                // default init state..
                if (e.UnityVersionMax == UnityVersion.Min && e.UnityVersionMin == UnityVersion.Min)
                {
                    e.UnityVersionMax = UnityVersion.Max;
                }
                if (curVersion >= e.UnityVersionMin && curVersion <= e.UnityVersionMax)
                {
                    if (s != null)
                    {
                        Debug.LogWarning("Found multiple possible entries for unity version of shader");
                    }
                    s = e.shaderSrc;
                }
            }
            return s;
        }
    }
}
