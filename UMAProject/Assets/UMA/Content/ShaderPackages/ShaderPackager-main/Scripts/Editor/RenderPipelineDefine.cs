#define __BETTERSHADERS__
//////////////////////////////////////////////////////
// Shader Packager
// Copyright (c)2021 Jason Booth
//////////////////////////////////////////////////////

using System;
using System.Linq;
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
         var foundType = (from assembly in AppDomain.CurrentDomain.GetAssemblies()
                          from type in GetTypesSafe(assembly)
                          where type.Name == className
                          select type).FirstOrDefault();

         return foundType != null;
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

         return types.Where(x => x != null);
      }




   }
}

#endif