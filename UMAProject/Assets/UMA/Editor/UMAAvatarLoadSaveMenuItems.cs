using UnityEngine;
using UnityEditor;
using UMA;

public class UMAAvatarLoadSaveMenuItems : Editor
{
	[MenuItem("UMA/Load and Save/Save Selected Avatar(s) Txt")]
	public static void SaveSelectedAvatarsTxt()
	{
		for (int i = 0; i < Selection.objects.Length; i++)
		{
			var avatarGO = Selection.objects[i] as GameObject;
			if (avatarGO == null) continue;

			var avatar = avatarGO.GetComponent<UMAAvatarBase>();
			if( avatar != null )
			{
				var path = EditorUtility.SaveFilePanel("Save serialized Avatar", "Assets", avatar.name + ".txt", "txt");
				if (path.Length != 0)
				{
					var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
					asset.Save(avatar.umaData, avatar.context);
					System.IO.File.WriteAllText(path, asset.recipeString);
					ScriptableObject.Destroy(asset);
				}
			}
		}
	}

	[MenuItem("UMA/Load and Save/Save Selected Avatar(s) asset")]
	public static void SaveSelectedAvatarsAsset()
	{
		for (int i = 0; i < Selection.objects.Length; i++)
		{
			var avatar = Selection.objects[i] as UMAAvatarBase;
			if (avatar != null)
			{
				var path = EditorUtility.SaveFilePanelInProject("Save serialized Avatar", avatar.name + ".asset", "asset", "Message 2");
				if (path.Length != 0)
				{
					var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
					asset.Save(avatar.umaData, avatar.context);
					AssetDatabase.CreateAsset(asset, path);
					AssetDatabase.SaveAssets();
				}
			}
		}
	}

	[MenuItem("UMA/Load and Save/Load Selected Avatar(s) txt")]
	public static void LoadSelectedAvatarsTxt()
	{
		for (int i = 0; i < Selection.objects.Length; i++)
		{
			var avatar = Selection.objects[i] as UMAAvatarBase;
			if( avatar != null )
			{
				var path = EditorUtility.OpenFilePanel("Load serialized Avatar", "Assets", "txt");
				if (path.Length != 0)
				{
					var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
					asset.recipeString = System.IO.File.ReadAllText(path);
					avatar.Load(asset);
					Destroy(asset);
				}
			}
		}
	}

	[MenuItem("UMA/Load and Save/Load Selected Avatar(s) assets")]
	public static void LoadSelectedAvatarsAsset()
	{
		for (int i = 0; i < Selection.objects.Length; i++)
		{
			var avatar = Selection.objects[i] as UMAAvatarBase;
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
}
