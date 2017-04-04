// Snippet by Jacob Pennock (www.jacobpennock.com)
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

namespace UMA
{
	/// <summary>
	/// Utility class for creating scriptable object assets.
	/// </summary>
	public static class CustomAssetUtility
	{
        public static void CreatePrefab<T>()
        {
            CreatePrefab(typeof(T).Name, typeof(T));
        }

        public static void CreatePrefab(string name, params System.Type[] types)
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (path == "")
            {
                path = "Assets";
            }
            else if (File.Exists(path))    
            {
                path = path.Replace("/" + Path.GetFileName(AssetDatabase.GetAssetPath(Selection.activeObject)), "");
            }

            string assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(path + "/New "+name+".prefab");

            GameObject go = new GameObject(name);
            foreach (System.Type t in types)
            {
                go.AddComponent(t);
            }
            PrefabUtility.CreatePrefab(assetPathAndName, go);
            GameObject.DestroyImmediate(go,false);
        }

		/// <summary>
		/// Creates a new asset of the type T
		/// </summary>
		/// <param name="newAssetPath">The full path relative to 'Assets' (including extension) where the file should be saved. If empty the path and name are based on the currently selected object and desired type.</param>
		/// <param name="selectCreatedAsset">If true the created asset will be selected after it is created (and show in the inspector)</param>
		/// <returns>t</returns>
		public static T CreateAsset<T>(string newAssetPath = "", bool selectCreatedAsset = true) where T : ScriptableObject
	    {
	        T asset = ScriptableObject.CreateInstance<T>();

			string assetPathAndName = "";
			if (newAssetPath != "")
			{
				assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(newAssetPath);
				//make sure the user hasn't send a path that includes the desired filename
				var dir = Path.GetDirectoryName(assetPathAndName);
				//make sure the directory exists
				Directory.CreateDirectory(dir);
			}
			else
			{
				var path = AssetDatabase.GetAssetPath(Selection.activeObject);
				if (path == "")
				{
					path = "Assets";
				}
				else if (File.Exists(path)) // modified this line, folders can have extensions.
				{
					path = path.Replace("/" + Path.GetFileName(AssetDatabase.GetAssetPath(Selection.activeObject)), "");
				}

				var assetName = "New " + typeof(T).Name;

				assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(path + "/" + assetName + ".asset");
			}

	        AssetDatabase.CreateAsset(asset, assetPathAndName);

	        AssetDatabase.SaveAssets();
			if(selectCreatedAsset)
				Selection.activeObject = asset;
			return asset;
	    }
	}
}
#endif
