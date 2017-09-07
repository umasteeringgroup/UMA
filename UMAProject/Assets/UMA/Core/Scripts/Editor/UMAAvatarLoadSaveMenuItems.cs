using UnityEngine;
using UnityEditor;
using UMA;
using UMA.CharacterSystem;

namespace UMA.Editors
{
   public class UMAAvatarLoadSaveMenuItems : Editor
   {
      [MenuItem("UMA/Runtime/Save Selected Avatars generated textures to PNG")]
      public static void SaveSelectedAvatarsPNG()
      {
         if (!Application.isPlaying)
         {
            EditorUtility.DisplayDialog("Notice", "This function is only available at runtime", "Got it");
            return;
         }

         if (Selection.gameObjects.Length != 1)
         {
            EditorUtility.DisplayDialog("Notice", "Only one Avatar can be selected.", "OK");
            return;
         }

         var selectedTransform = Selection.gameObjects[0].transform;
         var avatar = selectedTransform.GetComponent<UMAAvatarBase>();

         if (avatar == null)
         {
            EditorUtility.DisplayDialog("Notice", "An Avatar must be selected to use this function", "OK");
            return;
         }

         SkinnedMeshRenderer smr = avatar.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
         if (smr == null)
         {
            EditorUtility.DisplayDialog("Warning", "Could not find SkinnedMeshRenderer in Avatar hierarchy", "OK");
            return;
         }

         string path = EditorUtility.SaveFilePanelInProject("Save Texture(s)", "Texture.png", "png", "Base Filename to save PNG files to.");
         if (!string.IsNullOrEmpty(path))
         {
            string basename = System.IO.Path.GetFileNameWithoutExtension(path);
            string pathname = System.IO.Path.GetDirectoryName(path);
            // save the diffuse texture
            for (int i = 0; i < smr.materials.Length; i++)
            {
               string PathBase = System.IO.Path.Combine(pathname, basename + "_material_" + i.ToString());
               string DiffuseName = PathBase + "_Diffuse.PNG";
               SaveTexture(smr.materials[i].GetTexture("_MainTex"), DiffuseName);
            }
         }
      }

      private static void SaveTexture(Texture texture, string diffuseName)
      {
         if (texture is RenderTexture)
         {
            SaveRenderTexture(texture as RenderTexture, diffuseName);
            return;
         }
         else if (texture is Texture2D)
         {
            SaveTexture2D(texture as Texture2D, diffuseName);
            return;
         }
         EditorUtility.DisplayDialog("Error", "Texture is not RenderTexture or Texture2D", "OK");
      }

      static public Texture2D GetRTPixels(RenderTexture rt)
      {

         // Remember currently active render texture
         RenderTexture currentActiveRT = RenderTexture.active;

         // Set the supplied RenderTexture as the active one
         RenderTexture.active = rt;

         // Create a new Texture2D and read the RenderTexture image into it
         Texture2D tex = new Texture2D(rt.width, rt.height);
         tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);

         // Restorie previously active render texture
         RenderTexture.active = currentActiveRT;
         return tex;
      }

      private static void SaveRenderTexture(RenderTexture texture, string textureName)
      {
         Texture2D tex = GetRTPixels(texture);
         SaveTexture2D(tex, textureName);
      }

      private static void SaveTexture2D(Texture2D texture, string textureName)
      {
         byte[] data = texture.EncodeToPNG();
         System.IO.File.WriteAllBytes(textureName, data);
      }

      [MenuItem("UMA/Load and Save/Save Selected Avatar(s) Txt", priority=1)]
      public static void SaveSelectedAvatarsTxt()
      {
         for (int i = 0; i < Selection.gameObjects.Length; i++)
         {
            var selectedTransform = Selection.gameObjects[i].transform;
            var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
            while (avatar == null && selectedTransform.parent != null)
            {
               selectedTransform = selectedTransform.parent;
               avatar = selectedTransform.GetComponent<UMAAvatarBase>();
            }

            if (avatar != null)
            {
               var path = EditorUtility.SaveFilePanel("Save serialized Avatar", "Assets", avatar.name + ".txt", "txt");
               if (path.Length != 0)
               {
                  var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
                  //check if Avatar is DCS
                  if (avatar is UMA.CharacterSystem.DynamicCharacterAvatar)
                  {
                     asset.Save(avatar.umaData.umaRecipe, avatar.context, (avatar as DynamicCharacterAvatar).WardrobeRecipes, true);
                  }
                  else
                  {
                     asset.Save(avatar.umaData.umaRecipe, avatar.context);
                  }				
                  System.IO.File.WriteAllText(path, asset.recipeString);
                  UMAUtils.DestroySceneObject(asset);
               }
            }
         }
      }

 
       [MenuItem("UMA/Load and Save/Save Selected Avatar(s) asset", priority = 1)]
      public static void SaveSelectedAvatarsAsset()
      {
         for (int i = 0; i < Selection.gameObjects.Length; i++)
         {
            var selectedTransform = Selection.gameObjects[i].transform;
            var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
            while (avatar == null && selectedTransform.parent != null)
            {
               selectedTransform = selectedTransform.parent;
               avatar = selectedTransform.GetComponent<UMAAvatarBase>();
            }
            if (avatar != null)
            {
               var path = EditorUtility.SaveFilePanelInProject("Save serialized Avatar", avatar.name + ".asset", "asset", "Message 2");
               if (path.Length != 0)
               {
                  var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
                  //check if Avatar is DCS
                  if (avatar is DynamicCharacterAvatar)
                  {
                     asset.Save(avatar.umaData.umaRecipe, avatar.context, (avatar as DynamicCharacterAvatar).WardrobeRecipes,true);
                  }
                  else
                  {
                     asset.Save(avatar.umaData.umaRecipe, avatar.context);
                  }
                  AssetDatabase.CreateAsset(asset, path);
                  AssetDatabase.SaveAssets();
                  Debug.Log("Recipe size: " + asset.recipeString.Length + " chars");

               }
            }
         }
      }

      [MenuItem("UMA/Load and Save/Load Selected Avatar(s) txt")]
      public static void LoadSelectedAvatarsTxt()
      {
         for (int i = 0; i < Selection.gameObjects.Length; i++)
         {
            var selectedTransform = Selection.gameObjects[i].transform;
            var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
            while (avatar == null && selectedTransform.parent != null)
            {
               selectedTransform = selectedTransform.parent;
               avatar = selectedTransform.GetComponent<UMAAvatarBase>();
            }

            if (avatar != null)
            {
               var path = EditorUtility.OpenFilePanel("Load serialized Avatar", "Assets", "txt");
               if (path.Length != 0)
               {
                  var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
                  asset.recipeString = FileUtils.ReadAllText(path);
                  //check if Avatar is DCS
                  if (avatar is DynamicCharacterAvatar)
                  {
                     (avatar as DynamicCharacterAvatar).LoadFromRecipeString(asset.recipeString);
                       }
                  else
                  {
                     avatar.Load(asset);
                  }
                  
                  UMAUtils.DestroySceneObject(asset);
               }
            }
         }
      }


       [MenuItem("UMA/Load and Save/Load Selected Avatar(s) assets")]
      public static void LoadSelectedAvatarsAsset()
      {
         for (int i = 0; i < Selection.gameObjects.Length; i++)
         {
            var selectedTransform = Selection.gameObjects[i].transform;
            var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
            while (avatar == null && selectedTransform.parent != null)
            {
               selectedTransform = selectedTransform.parent;
               avatar = selectedTransform.GetComponent<UMAAvatarBase>();
            }

            if (avatar != null)
            {
               var path = EditorUtility.OpenFilePanel("Load serialized Avatar", "Assets", "asset");
               if (path.Length != 0)
               {
                  var index = path.IndexOf("/Assets/");
                  if (index > 0)
                  {
                     path = path.Substring(index + 1);
                  }
                  var asset = AssetDatabase.LoadMainAssetAtPath(path) as UMARecipeBase;
                  if (asset != null)
                  {
                     //check if Avatar is DCS
                     if (avatar is DynamicCharacterAvatar)
                     {
                        (avatar as DynamicCharacterAvatar).LoadFromRecipe(asset);
                     }
                     else
                     {
                        avatar.Load(asset);
                     }
                  }
                  else
                  {
                     Debug.LogError("Failed To Load Asset \"" + path + "\"\nAssets must be inside the project and descend from the UMARecipeBase type");
                  }
               }
            }
         }
      }

      //@jaimi this is the equivalent of your previous JSON save but the resulting file does not need a special load method
       [MenuItem("UMA/Load and Save/Save DynamicCharacterAvatar(s) txt (optimized)", priority = 1)]
       public static void SaveSelectedAvatarsDCSTxt()
       {
           if (!Application.isPlaying)
           {
               EditorUtility.DisplayDialog("Notice", "This function is only available at runtime", "Got it");
               return;
         }
         else
         {
            EditorUtility.DisplayDialog("Notice", "The optimized save type is only compatible with DynamicCharacterAvatar avatars (or child classes of)", "Continue");
         }

         for (int i = 0; i < Selection.gameObjects.Length; i++)
           {
               var selectedTransform = Selection.gameObjects[i].transform;
               var avatar = selectedTransform.GetComponent<DynamicCharacterAvatar>();

               if (avatar != null)
               {
                   var path = EditorUtility.SaveFilePanel("Save DynamicCharacterAvatar Optimized Text", "Assets", avatar.name + ".txt", "txt");
                   if (path.Length != 0)
                   {
                  avatar.DoSave(false, path);
                   }
               }
           }
       }
      //@jaimi this is the equivalent of your previous JSON save but the resulting file does not need a special load method and the resulting asset can also be inspected and edited
      [MenuItem("UMA/Load and Save/Save DynamicCharacterAvatar(s) asset (optimized)", priority = 1)]
      public static void SaveSelectedAvatarsDCSAsset()
      {
         if (!Application.isPlaying)
         {
            EditorUtility.DisplayDialog("Notice", "This function is only available at runtime", "Got it");
            return;
         }
         else
         {
            EditorUtility.DisplayDialog("Notice", "The optimized save type is only compatible with DynamicCharacterAvatar avatars (or child classes of)", "Continue");
         }

         for (int i = 0; i < Selection.gameObjects.Length; i++)
         {
            var selectedTransform = Selection.gameObjects[i].transform;
            var avatar = selectedTransform.GetComponent<DynamicCharacterAvatar>();

            if (avatar != null)
            {
               var path = EditorUtility.SaveFilePanelInProject("Save DynamicCharacterAvatar Optimized Asset", avatar.name + ".asset", "asset", "Message 2");
               if (path.Length != 0)
               {
                  avatar.DoSave(true, path);
               }
            }
         }
      }

       [UnityEditor.MenuItem("Assets/Add Selected Assets to UMA Global Library")]
       public static void AddSelectedToGlobalLibrary()
       {
           int added = 0;
           UMAAssetIndexer UAI = UMAAssetIndexer.Instance;

           foreach(Object o in Selection.objects)
           {
               System.Type type = o.GetType();
               if (UAI.IsIndexedType(type))
               {
                   UAI.EvilAddAsset(type, o);
                   added++;
               }
           }
           UAI.ForceSave();
           EditorUtility.DisplayDialog("Success", added + " item(s) added to Global Library","OK");
       }


       //We dont need this now pUMATextRecipe works out which model was used itself
       /*[MenuItem("UMA/Load and Save/Load Dynamic Character Avatar From JSON", priority = 1)]
       public static void LoadSelectedAvatarsJSON()
       {
           if (!Application.isPlaying)
           {
               EditorUtility.DisplayDialog("Notice", "This function is only available at runtime", "Got it");
               return;
           }

           for (int i = 0; i < Selection.gameObjects.Length; i++)
           {
               var selectedTransform = Selection.gameObjects[i].transform;
               var avatar = selectedTransform.GetComponent<UMACharacterSystem.DynamicCharacterAvatar>();

               if (avatar != null)
               {
                   var path = EditorUtility.OpenFilePanel("Load DynamicCharacterAvatar from JSON Text", "Assets", "json");
                   if (path.Length != 0)
                   {
                       avatar.FromJson(System.IO.File.ReadAllText(path));
                   }
               }
           }
       }*/
   }
}
