using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace UMA.Editors
{
	public class UMASaveCharacters : Editor
	{
		public static bool FileExists(string path)
		{
			return System.IO.Directory.Exists(System.IO.Directory.GetCurrentDirectory() + "/" + path);
		}

		[MenuItem("UMA/Tools/Save Character Prefabs")]
		static void SaveCharacterPrefabsMenuItem()
		{
			var newCharacters = new List<GameObject>();
			HashSet<UMAAvatarBase> saved = new HashSet<UMAAvatarBase>();
			foreach (var go in Selection.gameObjects)
			{
				var selectedTransform = go.transform;
				UMAAvatarBase avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				while( avatar == null && selectedTransform.parent != null )
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				}
				if (avatar != null && PrefabUtility.GetPrefabObject(avatar.umaData.umaRoot) == null)
				{
					if (saved.Add(avatar))
					{
						var path = EditorUtility.SaveFilePanelInProject("Save Avatar Prefab", avatar.name + ".prefab", "prefab", "Save Avatar Prefab", "Assets/UMA/UMA_Generated/Complete");
						if (FileExists(path))
						{
							Debug.LogWarning("Overwrite of prefabs not supported!");
						}
						else if (path.Length != 0)
						{
							newCharacters.Add(SaveCharacterPrefab(System.IO.Path.GetDirectoryName(path), System.IO.Path.GetFileNameWithoutExtension(path), avatar.umaData));
						}
					}
				}
			}
			if (newCharacters.Count > 0)
			{
				Selection.objects = newCharacters.ToArray();
			}
			else
			{
				Debug.LogWarning("No UMA characters selected or all selected characters are already saved as prefabs.");
			}
		}
		public static GameObject SaveCharacterPrefab(string assetFolder, string name, UMAData umaData)
		{
			return PowerToolsRuntime.SaveCharacterPrefab(assetFolder, name, umaData, EditorPrefs.GetBool("UnLogickFactory_PowerTools_Prefab_TPose"));
		}

		public static void SaveCharacterPrefab(UMAData umaData, string prefabName)
		{
			PowerToolsRuntime.EnsureProjectFolder("Assets/UMA/UMA_Generated/Complete");
			var assetFolder = AssetDatabase.GenerateUniqueAssetPath("Assets/UMA/UMA_Generated/Complete/" + prefabName);
			PowerToolsRuntime.SaveCharacterPrefab(assetFolder, prefabName, umaData, EditorPrefs.GetBool("UnLogickFactory_PowerTools_Prefab_TPose"));
		}
	}
}
