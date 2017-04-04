using UnityEngine;
using System;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

namespace UMA.Integrations.PowerTools
{
	[Serializable]
	public class UMALODConversionSet : ScriptableObject
	{
		public UMALODConversionEntry[] Conversions = new UMALODConversionEntry[0];
	
#if UNITY_EDITOR
		[MenuItem("Assets/Create/UMA/Misc/LOD Conversion Set")]
		public static void CreateUMALODConversion()
		{
			var asset = ScriptableObject.CreateInstance<UMALODConversionSet>();
	
			string path = AssetDatabase.GetAssetPath(Selection.activeObject);
			if (path == "")
			{
				path = "Assets";
			}
			else if (File.Exists(path)) // modified this line, folders can have extensions.
			{
				path = path.Replace(Path.GetFileName(AssetDatabase.GetAssetPath(Selection.activeObject)), "");
			}
	
			string assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(path + "/New UMA LOD Conversion Set.asset");
	
			AssetDatabase.CreateAsset(asset, assetPathAndName);
	
			AssetDatabase.SaveAssets();
			EditorUtility.FocusProjectWindow();
			Selection.activeObject = asset;
	
		}
#endif
	}
}