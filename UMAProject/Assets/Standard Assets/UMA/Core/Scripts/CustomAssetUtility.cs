// Snippet by Jacob Pennock (www.jacobpennock.com)
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

namespace UMAEditor
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

        public static T CreateAsset<T>(bool selectCreatedAsset = true, string newAssetName = "") where T : ScriptableObject
	    {
	        T asset = ScriptableObject.CreateInstance<T>();

	        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
	        if (path == "")
	        {
	            path = "Assets";
	        }
	        else if (File.Exists(path)) // modified this line, folders can have extensions.
	        {
	            path = path.Replace("/" + Path.GetFileName(AssetDatabase.GetAssetPath(Selection.activeObject)), "");
	        }

			var assetName = newAssetName == "" ? "New " + typeof(T).Name : newAssetName;

			string assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(path + "/" + assetName + ".asset");

			var uniqueAssetNameAndPath = AssetDatabase.GenerateUniqueAssetPath(assetPathAndName);

	        AssetDatabase.CreateAsset(asset, uniqueAssetNameAndPath);

	        AssetDatabase.SaveAssets();
			if(selectCreatedAsset)
				Selection.activeObject = asset;
			return asset;
	    }
	}
}
#endif
