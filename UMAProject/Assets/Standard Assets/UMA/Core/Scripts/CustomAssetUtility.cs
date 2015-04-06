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
	    public static void CreateAsset<T>() where T : ScriptableObject
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

	        string assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(path + "/New " + typeof(T).Name + ".asset");

	        AssetDatabase.CreateAsset(asset, assetPathAndName);

	        AssetDatabase.SaveAssets();
	        EditorUtility.FocusProjectWindow();
	        Selection.activeObject = asset;
	    }
	}
}
#endif