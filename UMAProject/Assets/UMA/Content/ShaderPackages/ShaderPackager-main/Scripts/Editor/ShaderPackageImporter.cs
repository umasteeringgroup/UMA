//////////////////////////////////////////////////////
// Shader Packager
// Copyright (c)2021 Jason Booth
//////////////////////////////////////////////////////

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif
using System.IO;

namespace UMA.ShaderPackager
{
    [ScriptedImporter(0, ShaderPackageImporter.k_FileExtension)]
    public class ShaderPackageImporter : ScriptedImporter
    {
      public const string k_FileExtension = ".umaShaderPack";

        public override void OnImportAsset(AssetImportContext ctx)
        {

            string fileContent = File.ReadAllText(ctx.assetPath);
            var package = ObjectFactory.CreateInstance<ShaderPackage>();

            if (!string.IsNullOrEmpty(fileContent))
            {
                EditorJsonUtility.FromJsonOverwrite(fileContent, package);
            }

            if (package.entries == null)
            {
                package.entries = new List<ShaderPackage.Entry>();
            }

#if __BETTERSHADERS__
         if (package.betterShader != null)
         {
            package.betterShaderPath = AssetDatabase.GetAssetPath(package.betterShader);
         }
#endif

            package.Pack(false);

#if __BETTERSHADERS__
         if (package.betterShader != null)
         {
               ctx.DependsOnSourceAsset(package.betterShaderPath);
         }
         
#endif

         foreach (var e in package.entries)
            {
                if (e.shader != null)
                {
                    ctx.DependsOnSourceAsset(AssetDatabase.GetAssetPath(e.shader));
                }
            }

            string shaderSrc = package.GetShaderSrc();
            if (shaderSrc == null)
            {
            Debug.LogError("No Shader for this platform and SRP provided");
                // maybe make an error shader here?
                return;
            }

            Shader shader = ShaderUtil.CreateShaderAsset(ctx, shaderSrc, false);

            ctx.AddObjectToAsset("MainAsset", shader);
            ctx.SetMainObject(shader);
        }


    }
}
