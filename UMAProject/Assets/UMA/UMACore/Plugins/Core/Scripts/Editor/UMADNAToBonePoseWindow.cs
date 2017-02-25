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

				Dictionary<Transform, MirrorData> mirrors = new Dictionary<Transform, MirrorData>();
				FindMirroredTransforms(sourceUMA.umaRoot.transform, mirrors);
			}
		}

		public enum MirrorPlane
		{
			Mirror_None,
			Mirror_Any,
			Mirror_X,
			Mirror_Y,
			Mirror_Z,		
		}

		public struct MirrorData
		{
			public Transform transformA;
			public Transform transformB;
			public MirrorPlane plane;
		}

		private static void FindMirroredTransforms(Transform transform, Dictionary<Transform, MirrorData> mirrors)
		{
			MirrorData mirror;
			// Current transform has a mirror, check children
			if (mirrors.TryGetValue(transform, out mirror) &&
				// But only add each pair of mirrors once
				(transform == mirror.transformA))
			{
				for (int i = 0; i < transform.childCount; i++)
				{
					Transform childA = transform.GetChild(i);
					Vector3 childApos = childA.localPosition;

					// If the bone is at the local origin check for a name match
					if (childApos == Vector3.zero)
					{
						int longestMatch = 0;
						int bestIndex = -1;

						for (int j = 0; j < mirror.transformB.childCount; j++)
						{
							Transform childB = mirror.transformB.GetChild(j);
							Vector3 childBpos = childB.localPosition;
							if (childBpos != Vector3.zero) continue;
							if (childB.name == childA.name)
							{
								longestMatch = childA.name.Length;
								bestIndex = j;
								break;
							}

							while ((childA.name.Length > longestMatch) &&
								(childB.name.StartsWith(childA.name.Substring(0, longestMatch + 1))))
							{
								longestMatch++;
								bestIndex = j;
							}
							while ((childA.name.Length > longestMatch) &&
								(childB.name.EndsWith(childA.name.Substring(childA.name.Length - (longestMatch + 1)))))
							{
								longestMatch++;
								bestIndex = j;
							}
						}

						// If we found a good enough match then consider it a mirror
						if (longestMatch > 5)
						{
							Transform childB = mirror.transformB.GetChild(bestIndex);
							MirrorData newMirror = new MirrorData();
							newMirror.plane = mirror.plane;
							newMirror.transformA = childA;
							newMirror.transformB = childB;
							mirrors.Add(childA, newMirror);
							mirrors.Add(childB, newMirror);

							Debug.Log("Found new named mirror: " + childA.name + " : " + childB.name);
						}

					}
					// Else check for a position match
					else
					{
						for (int j = 0; j < mirror.transformB.childCount; j++)
						{
							Transform childB = mirror.transformB.GetChild(j);
							Vector3 childBpos = childB.localPosition;

							switch (mirror.plane)
							{
								case MirrorPlane.Mirror_X:
									childBpos.x = -childBpos.x;
									break;
								case MirrorPlane.Mirror_Y:
									childBpos.y = -childBpos.y;
									break;
								case MirrorPlane.Mirror_Z:
									childBpos.z = -childBpos.z;
									break;
							}

							if (childApos == childBpos)
							{
								MirrorData newMirror = new MirrorData();
								newMirror.plane = mirror.plane;
								newMirror.transformA = childA;
								newMirror.transformB = childB;
								mirrors.Add(childA, newMirror);
								mirrors.Add(childB, newMirror);

								Debug.Log("Found new child mirror: " + childA.name + " : " + childB.name);
							}
						}
					}
				}
			}
			// Current transform has no mirror, see if children are mirrored
			else
			{
				for (int i = 0; i < transform.childCount; i++)
				{
					Transform childA = transform.GetChild(i);
					Vector3 childApos = childA.localPosition;
					if (childApos == Vector3.zero) continue;

					for (int j = i + 1; j < transform.childCount; j++)
					{
						Transform childB = transform.GetChild(j);
						Vector3 childBpos = childB.localPosition;
						MirrorPlane plane = MirrorPlane.Mirror_None;

						if (childApos == new Vector3(-childBpos.x, childBpos.y, childBpos.z))
						{
							plane = MirrorPlane.Mirror_X;
						}
						if (childApos == new Vector3(childBpos.x, -childBpos.y, childBpos.z))
						{
							plane = MirrorPlane.Mirror_Y;
						}
						if (childApos == new Vector3(childBpos.x, childBpos.y, -childBpos.z))
						{
							plane = MirrorPlane.Mirror_Z;
						}

						if (plane != MirrorPlane.Mirror_None)
						{
							MirrorData newMirror = new MirrorData();
							newMirror.plane = plane;
							newMirror.transformA = childA;
							newMirror.transformB = childB;
							mirrors.Add(childA, newMirror);
							mirrors.Add(childB, newMirror);

							Debug.Log("Found new branching mirror: " + childA.name + " : " + childB.name);
						}
					}
				}
			}

			// Recursively look at the children
			for (int i = 0; i < transform.childCount; i++)
			{
				FindMirroredTransforms(transform.GetChild(i), mirrors);
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