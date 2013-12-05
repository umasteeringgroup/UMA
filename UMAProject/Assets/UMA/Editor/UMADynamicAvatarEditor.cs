using UnityEngine;
using UnityEditor;
using UMA;

[CustomEditor(typeof(UMADynamicAvatar))]
public class UMADynamicAvatarEditor : Editor 
{
	public override void OnInspectorGUI()
	{
		UMADynamicAvatar umaDynamicAvatar = (UMADynamicAvatar)target;
		var oldRecipe = umaDynamicAvatar.umaRecipe;
		base.OnInspectorGUI();
		if (oldRecipe != umaDynamicAvatar.umaRecipe)
		{
			if (EditorApplication.isPlaying)
			{
				umaDynamicAvatar.Load(umaDynamicAvatar.umaRecipe);
			}
		}

		if (EditorApplication.isPlaying)
		{
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Save Avatar Txt"))
			{

				if (umaDynamicAvatar)
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
			}

			if (GUILayout.Button("Save Avatar Asset"))
			{
				if (umaDynamicAvatar)
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
			}

			if (GUILayout.Button("Load Avatar Txt"))
			{
				UMAData umaData = umaDynamicAvatar.umaData;
				RaceData umaRace = umaData.umaRecipe.raceData;
				if (umaData && umaDynamicAvatar)
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
			}
			if (GUILayout.Button("Load Avatar Asset"))
			{
				UMAData umaData = umaDynamicAvatar.umaData;
				RaceData umaRace = umaData.umaRecipe.raceData;
				if (umaData && umaDynamicAvatar)
				{
					var path = EditorUtility.OpenFilePanel("Load serialized Avatar", "Assets", "asset");
					if (path.Length != 0)
					{
						var index = path.IndexOf("/Assets/");
						if( index > 0 )
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
							Debug.LogError("Failed To Load Asset \""+path+"\"\nAssets must be inside the project and descend from the UMARecipeBase type");
						}
					}
				}
			}
			GUILayout.EndHorizontal();	
		}
		//else
		//{
		//    var da = target as UMADynamicAvatar;

		//    if (da.umaData == null || da.umaData.umaRoot == null)
		//    {
		//        if (GUILayout.Button("Show In Editor"))
		//        {
		//            da.Initialize();
		//            //da.umaChild.hideFlags = HideFlags.HideAndDontSave;
		//            //da.umaData.hideFlags = HideFlags.HideAndDontSave;
		//            var generator = GameObject.Find("UMAGenerator").GetComponent<UMA.UMAGenerator>();
		//            generator.Initialize();
		//            da.umaData.firstBake = true;
		//            da.umaData.isMeshDirty = true;
		//            da.umaData.isShapeDirty = true;
		//            da.umaData.isTextureDirty = true;

		//            while (!generator.HandleDirtyUpdate(da.umaData))
		//            {
		//                // keep looping till done
		//            }
		//        }
		//    }
		//    else
		//    {
		//        if (GUILayout.Button("Hide In Editor"))
		//        {
		//            DestroyImmediate(da.umaData.umaRoot.transform.parent.gameObject);
		//            da.umaChild = null;
		//            if (da.umaData.hideFlags == HideFlags.HideAndDontSave)
		//            {
		//                DestroyImmediate(da.umaData);
		//                da.umaData = null;
		//            }
		//            else
		//            {
		//                da.umaData.umaRoot = null;
		//                da.umaData.animator = null;
		//                da.umaData.myRenderer = null;
		//            }
		//        }
		//    }
		//}
	}
}
