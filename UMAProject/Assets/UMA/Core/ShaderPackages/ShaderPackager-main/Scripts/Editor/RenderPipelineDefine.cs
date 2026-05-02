#define __BETTERSHADERS__
//////////////////////////////////////////////////////
// Shader Packager
// Copyright (c)2021 Jason Booth
//////////////////////////////////////////////////////

using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;

#if UNITY_2019_3_OR_NEWER

// installs defines for render pipelines, so we can #if USING_HDRP and do stuff. Can't believe Unity doesn't provide this crap, they
// really go out of their way to make it hard to work across pipelines.

namespace UMA.ShaderPackager
{
    public static class RenderPipelineDefine
   {
      private const string HDRP_PACKAGE = "HDRenderPipelineAsset";
      private const string URP_PACKAGE = "UniversalRenderPipelineAsset";

      public static bool IsHDRP { get; private set; }
      public static bool IsURP { get; private set; }
      public static bool IsStandardRP { get; private set; }

      [UnityEditor.Callbacks.DidReloadScripts]
      private static void OnScriptsReloaded()
      {
         IsHDRP = DoesTypeExist(HDRP_PACKAGE);
         IsURP = DoesTypeExist(URP_PACKAGE);

         if (!(IsHDRP || IsURP))
      {
            IsStandardRP = true;
         }

      }

      public static bool DoesTypeExist(string className)
            {
         System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
         for (int assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
         {
            foreach (Type type in GetTypesSafe(assemblies[assemblyIndex]))
            {
               if (type.Name == className)
               {
                  return true;
               }
            }
         }

         return false;
         }

      public static IEnumerable<Type> GetTypesSafe(System.Reflection.Assembly assembly)
      {
         Type[] types;

         try
         {
            types = assembly.GetTypes();
         }
         catch (ReflectionTypeLoadException e)
         {
            types = e.Types;
         }

         List<Type> validTypes = new List<Type>();
         for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
         {
            Type type = types[typeIndex];
            if (type != null)
            {
               validTypes.Add(type);
            }
         }

         return validTypes;
      }




   }
}

#endif