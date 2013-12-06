using UnityEngine;
using UnityEditor;
using UMA;

[CustomEditor(typeof(UMADynamicAvatar))]
[CanEditMultipleObjects]
public class UMADynamicAvatarEditor : Editor
{
	bool showInEditor = false;
	public override void OnInspectorGUI()
	{
		var oldRecipes = new UMARecipeBase[targets.Length];
		var dynamicAvatars = new UMADynamicAvatar[targets.Length];
		for (int i = 0; i < targets.Length; i++)
		{
			dynamicAvatars[i] = targets[i] as UMADynamicAvatar;
			oldRecipes[i] = dynamicAvatars[i] != null ? dynamicAvatars[i].umaRecipe : null;
		}
		base.OnInspectorGUI();
		for (int i = 0; i < dynamicAvatars.Length; i++)
		{
			if (dynamicAvatars[i] != null)
			{
				if (dynamicAvatars[i].umaRecipe != oldRecipes[i])
				{
					if (EditorApplication.isPlaying)
					{
						// if in play mode just reload the new recipe
						dynamicAvatars[i].Load(dynamicAvatars[i].umaRecipe);
					}
					else
					{
						if (showInEditor)
						{
							HideEditorAvatar(dynamicAvatars[i]);
							ShowEditorAvatar(dynamicAvatars[i]);
						}
					}
				}
			}
		}

		if (EditorApplication.isPlaying)
		{
			if (dynamicAvatars.Length == 1 && dynamicAvatars[0] != null && dynamicAvatars[0].umaData != null)
			{
				var umaDynamicAvatar = dynamicAvatars[0];
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Save Avatar Txt"))
				{
					var path = EditorUtility.SaveFilePanel("Save serialized Avatar", "Assets", target.name + ".txt", "txt");
					if (path.Length != 0)
					{
						var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
						asset.Save(umaDynamicAvatar.umaData, umaDynamicAvatar.context);
						System.IO.File.WriteAllText(path, asset.recipeString);
						ScriptableObject.Destroy(asset);
					}
				}

				if (GUILayout.Button("Save Avatar Asset"))
				{
					var path = EditorUtility.SaveFilePanelInProject("Save serialized Avatar", target.name + ".asset", "asset", "Message 2");
					if (path.Length != 0)
					{
						var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
						asset.Save(umaDynamicAvatar.umaData, umaDynamicAvatar.context);
						AssetDatabase.CreateAsset(asset, path);
						AssetDatabase.SaveAssets();
					}
				}

				if (GUILayout.Button("Load Avatar Txt"))
				{
					var path = EditorUtility.OpenFilePanel("Load serialized Avatar", "Assets", "txt");
					if (path.Length != 0)
					{
						var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
						asset.recipeString = System.IO.File.ReadAllText(path);
						umaDynamicAvatar.Load(asset);
						Destroy(asset);
					}
				}
				if (GUILayout.Button("Load Avatar Asset"))
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
							umaDynamicAvatar.Load(asset);
						}
						else
						{
							Debug.LogError("Failed To Load Asset \"" + path + "\"\nAssets must be inside the project and descend from the UMARecipeBase type");
						}
					}
				}
				GUILayout.EndHorizontal();
			}
		}
		else
		{
			if (!showInEditor)
			{
				if (GUILayout.Button("Show In Editor"))
				{
					showInEditor = true;
					foreach (var da in dynamicAvatars)
					{
						if (da != null)
						{
							ShowEditorAvatar(da);
						}
					}
				}
			}
			else
			{
				if (GUILayout.Button("Hide In Editor"))
				{
					showInEditor = false;
					foreach (var da in dynamicAvatars)
					{
						if (da != null)
						{
							HideEditorAvatar(da);
						}
					}
				}
			}
		}
	}

	private void ShowEditorAvatar(UMADynamicAvatar da)
	{
		da.Initialize();
		da.context.UpdateDictionaries();
		da.umaData.firstBake = true;
		da.Load(da.umaRecipe);
		da.umaData.firstBake = true;
		var generator = GameObject.Find("UMAGenerator").GetComponent<UMA.UMAGenerator>();
		var meshCombiner = da.gameObject.AddComponent<UMADefaultMeshCombiner>();
		var ourGenerator = new UMAEditorGenerator(generator.textureNameList, meshCombiner);
		ourGenerator.UpdateUMAMesh(da.umaData);
		ourGenerator.UpdateUMABody(da.umaData);
		da.umaData.firstBake = true;
		da.umaData.myRenderer.enabled = true;
		DestroyImmediate(meshCombiner);
	}

	private void HideEditorAvatar(UMADynamicAvatar da)
	{
		da.umaChild = null;
		da.umaData._hasUpdatedBefore = false;
		DestroyImmediate(da.umaData.umaRoot.transform.parent.gameObject);
		DestroyImmediate(da.umaData);
		da.umaData = null;
	}

	void OnEnable()
	{
		var da = target as UMADynamicAvatar;
		showInEditor = da != null && da.umaData != null;
	}
}
