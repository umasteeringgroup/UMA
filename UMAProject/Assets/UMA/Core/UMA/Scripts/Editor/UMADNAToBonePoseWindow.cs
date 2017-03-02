//	============================================================
//	Name:		UMADNAToBonePoseWindow
//	Author: 	Eli Curtz
//	Copyright:	(c) 2016 Eli Curtz
//	============================================================

#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Text;
using System.Collections.Generic;

using System.IO;
using System.Xml.Serialization;

namespace UMA.PoseTools
{
	public class UMADNAToBonePoseWindow : EditorWindow
	{
		public UMAData sourceUMA;
		public UnityEngine.Object outputFolder;

		private int dnaIndex = 0;

		private Vector2 scrollPosition;

		/*
		public void SavePoseSet()
		{
			string folderPath = "";
			if (poseFolder != null)
			{
				folderPath = AssetDatabase.GetAssetPath(poseFolder);
			}

			string filePath = EditorUtility.SaveFilePanel("Save pose set", folderPath, poseAnimation.name + "_Poses.xml", "xml");

			if (filePath.Length != 0)
			{
			}
		}
		*/

		public void EnforceFolder(ref UnityEngine.Object folderObject)
		{
			if (folderObject != null)
			{
				string destpath = AssetDatabase.GetAssetPath(folderObject);

				if (string.IsNullOrEmpty(destpath))
				{
					folderObject = null;
				}
				else if (!System.IO.Directory.Exists(destpath))
				{
					destpath = destpath.Substring(0, destpath.LastIndexOf('/'));
					folderObject = AssetDatabase.LoadMainAssetAtPath(destpath);
				}
			}
		}


		void OnGUI()
		{
			sourceUMA = EditorGUILayout.ObjectField("Source UMA", sourceUMA, typeof(UMAData), true) as UMAData;

			if (sourceUMA == null)
			{
				GUI.enabled = false;
			}
			else
			{
				DnaConverterBehaviour[] dnaConverters = sourceUMA.umaRecipe.raceData.dnaConverterList;
				string[] dnaNames = new string[dnaConverters.Length];
				for (int i = 0; i < dnaConverters.Length; i++)
				{
					dnaNames[i] = dnaConverters[i].name;
				}

				dnaIndex = EditorGUILayout.Popup("DNA Converter", dnaIndex, dnaNames);
			}

			EditorGUILayout.Space();

			outputFolder = EditorGUILayout.ObjectField("Output Folder", outputFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;
			EnforceFolder(ref outputFolder);

			if (GUILayout.Button("Save Pose Set"))
			{
//				SavePoseSet();
			}
		}

		public static UMABonePose CreatePoseAsset(string assetFolder, string assetName)
		{
			if (!System.IO.Directory.Exists(assetFolder))
			{
				System.IO.Directory.CreateDirectory(assetFolder);
			}

			UMABonePose asset = ScriptableObject.CreateInstance<UMABonePose>();

			AssetDatabase.CreateAsset(asset, assetFolder + "/" + assetName + ".asset");

			AssetDatabase.SaveAssets();

			return asset;
		}

		[MenuItem("UMA/Pose Tools/Bone Pose DNA Extractor")]
		public static void OpenUMADNAToBonePoseWindow()
		{
			EditorWindow win = EditorWindow.GetWindow(typeof(UMADNAToBonePoseWindow));

			win.titleContent.text = "Pose Extractor";
        }
	}
}
#endif