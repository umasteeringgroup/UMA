using UnityEngine;
using UnityEditor;
using UMA;

public class UMAAvatarLoadSaveMenuItems : Editor
{
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
					if (avatar is UMACharacterSystem.DynamicCharacterAvatar)
					{
						asset.SaveCharacterSystem(avatar.umaData.umaRecipe, avatar.context, (avatar as UMACharacterSystem.DynamicCharacterAvatar).WardrobeRecipes);
					}
					else
					{
						asset.Save(avatar.umaData.umaRecipe, avatar.context);
					}				
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
					//check if Avatar is DCS
					if (avatar is UMACharacterSystem.DynamicCharacterAvatar)
					{
						asset.SaveCharacterSystem(avatar.umaData.umaRecipe, avatar.context, (avatar as UMACharacterSystem.DynamicCharacterAvatar).WardrobeRecipes);
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
					if (avatar is UMACharacterSystem.DynamicCharacterAvatar)
					{
						(avatar as UMACharacterSystem.DynamicCharacterAvatar).LoadFromRecipeString(asset.recipeString);
                    }
					else
					{
						avatar.Load(asset);
					}
						
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
						//check if Avatar is DCS
						if (avatar is UMACharacterSystem.DynamicCharacterAvatar)
						{
							(avatar as UMACharacterSystem.DynamicCharacterAvatar).LoadFromRecipe(asset);
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


    [MenuItem("UMA/Load and Save/Save Dynamic Character Avatar to JSON", priority = 1)]
    public static void SaveSelectedAvatarsJSON()
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
                var path = EditorUtility.SaveFilePanel("Save DynamicCharacterAvatar to JSON Text", "Assets", avatar.name + ".json", "json");
                if (path.Length != 0)
                {
                    string json = avatar.ToJson();
                    System.IO.File.WriteAllText(path, json);
                }
            }
        }
    }

    [MenuItem("UMA/Load and Save/Load Dynamic Character Avatar From JSON", priority = 1)]
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
    }
}
