using UnityEngine;
using UnityEditor;
using UMA;

public class UMAAvatarLoadSaveMenuItems : Editor
{
#if !StripLitJson
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
					asset.Save(avatar.umaData.umaRecipe, avatar.context);
					System.IO.File.WriteAllText(path, asset.recipeString);
					ScriptableObject.Destroy(asset);
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
					asset.Save(avatar.umaData.umaRecipe, avatar.context);
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
					avatar.Load(asset);
					Destroy(asset);
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
						avatar.Load(asset);
					}
					else
					{
						Debug.LogError("Failed To Load Asset \"" + path + "\"\nAssets must be inside the project and descend from the UMARecipeBase type");
					}
				}
			}
		}
	}
#endif
}
