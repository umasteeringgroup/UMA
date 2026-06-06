///////////////////////////////////////////
///
/// Shader Packager
/// ©2021 Jason Booth
///
/// makes sure shader get in the shader menu

using System;
using UnityEditor;
using UnityEngine;

namespace UMA.ShaderPackager
{
    class ShaderPackagerPostProcessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            RegisterShaders(importedAssets);

        }

        static void RegisterShaders(string[] paths)
        {
         foreach (var assetPath in paths)
            {
                if (!assetPath.EndsWith(ShaderPackageImporter.k_FileExtension, StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }

                var mainObj = AssetDatabase.LoadMainAssetAtPath(assetPath) as Shader;

                if (mainObj != null)
                {
                    ShaderUtil.ClearShaderMessages(mainObj);
                    if (!ShaderUtil.ShaderHasError(mainObj))
                    {
                        ShaderUtil.RegisterShader(mainObj);
                    }
                }

            foreach (var obj in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
                {
                    if (obj is Shader)
                    {
                        Shader s = obj as Shader;
                        ShaderUtil.ClearShaderMessages(s);
                        if (!ShaderUtil.ShaderHasError(s))
                        {
                            ShaderUtil.RegisterShader((Shader)obj);
                        }
                    }
                }
            }
        }
    }
}