using UnityEngine;
using UnityEditor;
using UMA;
using UMA.CharacterSystem;
using System.Collections.Generic;

namespace UMA.Editors
{
   public class UMAAvatarLoadSaveMenuItems : Editor
   {
		[UnityEditor.MenuItem("GameObject/UMA/Save Mecanim Avatar to Asset (runtime only)")]
		[MenuItem("UMA/Runtime/Save Selected Avatars Mecanim Avatar to Asset", priority = 1)]
		public static void SaveMecanimAvatar()
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

			if (avatar.umaData == null)
			{
				EditorUtility.DisplayDialog("Notice", "The Avatar must be constructed before using this function", "OK");
				return;
			}

			if (avatar.umaData.animator == null)
			{
				EditorUtility.DisplayDialog("Notice", "Animator has not been assigned!", "OK");
				return;
			}
			if (avatar.umaData.animator.avatar == null)
			{
				EditorUtility.DisplayDialog("Notice", "Mecanim avatar is null!", "OK");
				return;
			}

			string path = EditorUtility.SaveFilePanelInProject("Save avatar", "CreatedAvatar.asset", "asset", "Save the avatar");

			AssetDatabase.CreateAsset(avatar.umaData.animator.avatar, path);
			AssetDatabase.SaveAssets();

			EditorUtility.DisplayDialog("Saved", "Avatar save to assets as CreatedAvatar", "OK");
		}

		[UnityEditor.MenuItem("GameObject/UMA/Save Atlas Textures (runtime only)")]
		[MenuItem("CONTEXT/DynamicCharacterAvatar/Save Selected Avatars generated textures to PNG",false,10)]
		[MenuItem("UMA/Runtime/Save Selected Avatar Atlas Textures")]
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
			   string NormalName = PathBase + "_Normal.PNG";
			   Texture diff = smr.materials[i].GetTexture("_MainTex");
			   if (diff != null) SaveTexture(diff, DiffuseName);
			   Texture bmp = smr.materials[i].GetTexture("_BumpMap");
			   if (bmp != null) SaveTexture(bmp, NormalName);
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
		 Texture2D tex = new Texture2D(rt.width, rt.height,TextureFormat.ARGB32,false,true);
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

		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as Character text file (runtime only)")]
		[UnityEditor.MenuItem("GameObject/UMA/Save as Character Text file (runtime only)")]
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


		[UnityEditor.MenuItem("GameObject/UMA/Show Mesh Info (runtime only)")]
		public static void ShowSelectedAvatarStats()
		{
			if (Selection.gameObjects.Length == 1)
			{
				var selectedTransform = Selection.gameObjects[0].transform;
				var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				}
				if (avatar != null)
				{
					SkinnedMeshRenderer sk = avatar.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
					if (sk != null)
					{
						List<string> info = new List<string>
					{
						sk.gameObject.name,
						"Mesh index type: " + sk.sharedMesh.indexFormat.ToString(),
						"VertexLength: " + sk.sharedMesh.vertices.Length,
						"Submesh Count: " + sk.sharedMesh.subMeshCount
					};
						for (int i = 0; i < sk.sharedMesh.subMeshCount; i++)
						{
							int[] tris = sk.sharedMesh.GetTriangles(i);
							info.Add("Submesh " + i + " Tri count: " + tris.Length);
						}
						Rect R = new Rect(200.0f, 200.0f, 300.0f, 600.0f);
						DisplayListWindow.ShowDialog("Mesh Info", R, info);
					}
				}
			}

		}

		[UnityEditor.MenuItem("GameObject/UMA/Save as Character Asset (runtime only)")]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as Asset (runtime only)")]
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

		[UnityEditor.MenuItem("GameObject/UMA/Load from Character Text file (runtime only)")]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Load Avatar from text file (runtime only)")]
		[MenuItem("UMA/Load and Save/Load Selected Avatar(s) txt", priority = 1)]
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

		[UnityEditor.MenuItem("GameObject/UMA/Load from Character Asset (runtime only)")]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Load Avatar from Asset (runtime only)")]
		[MenuItem("UMA/Load and Save/Load Selected Avatar(s) assets", priority = 1)]
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
		[UnityEditor.MenuItem("GameObject/UMA/Save as Optimized Character Text File (runtime only)")]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as Optimized Character Text File")]
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
		[UnityEditor.MenuItem("GameObject/UMA/Save as Optimized Character Asset (runtime only)")]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as Optimized Character Asset File")]
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
				   if(UAI.EvilAddAsset(type, o))
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
