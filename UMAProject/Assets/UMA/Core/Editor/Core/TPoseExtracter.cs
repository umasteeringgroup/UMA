﻿#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

namespace UMA.Editors
{
	public static class TPoseExtracter
	{
	    [MenuItem("UMA/Extract T-Pose")]
	    static void ExtractTPose()
	    {
			var selectedObjects = Selection.objects;
			if (selectedObjects.Length > 0)
			{
				bool extracted = false;
				foreach (var selectedObject in selectedObjects)
				{
					var assetPath = AssetDatabase.GetAssetPath(selectedObject);

					if (!string.IsNullOrEmpty(assetPath))
					{
						// Get asset path directory
						var assetDirectory = new FileInfo(assetPath).Directory.FullName + Path.DirectorySeparatorChar + "TPoses";

						// Trim off the path at "Assets" to get the relative path to the assets directory
						assetDirectory = assetDirectory.Substring(assetDirectory.IndexOf("Assets"));

						var modelImporter = AssetImporter.GetAtPath(assetPath) as ModelImporter;
						if( modelImporter != null )
						{
							var asset = UmaTPose.CreateInstance<UMA.UmaTPose>();
							asset.ReadFromHumanDescription(modelImporter.humanDescription);
							var name = selectedObject.name;
							if (name.EndsWith("(Clone)"))
							{
								name = name.Substring(0, name.Length - 7);
								asset.boneInfo[0].name = name;
								asset.Serialize();
							}
							if (!Directory.Exists(assetDirectory))
                                Directory.CreateDirectory(assetDirectory);
                            try
                            {
                                AssetDatabase.CreateAsset(asset, assetDirectory + Path.DirectorySeparatorChar + name + "_TPose.asset");
                            }
                            catch (UnityException e)
                            {
                                Debug.Log(e.ToString());
                            }
							extracted = true;
						}
					}
				}
				if (extracted)
				{
					AssetDatabase.SaveAssets();
					return;
				}
			}

	        foreach (var animator in Transform.FindObjectsOfType(typeof(Animator)) as Animator[])
	        {
	            var asset = UmaTPose.CreateInstance<UmaTPose>();
	            asset.ReadFromTransform(animator);
	            var name = animator.name;
	            if (name.EndsWith("(Clone)"))
	            {
	                name = name.Substring(0, name.Length - 7);
                    asset.boneInfo[0].name = name;
                    asset.Serialize();
	            }
                if (!Directory.Exists("Assets/UMA/Content/Generated/TPoses"))
					Directory.CreateDirectory("Assets/UMA/Content/Generated/TPoses");
				AssetDatabase.CreateAsset(asset, "Assets/UMA/Content/Generated/TPoses/" + name + "_TPose.asset");
	            EditorUtility.SetDirty(asset);
	            AssetDatabase.SaveAssets();
	        }
	    }
	}
}
#endif