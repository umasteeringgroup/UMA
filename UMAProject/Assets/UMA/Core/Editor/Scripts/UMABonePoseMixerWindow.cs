//	============================================================
//	Name:		UMABonePoseMixerWindow
//	Author: 	Eli Curtz
//	Copyright:	(c) 2013 Eli Curtz
//	============================================================
#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace UMA.PoseTools
{
	public class UMABonePoseMixerWindow : EditorWindow
	{
		public Transform skeleton = null;
		public UnityEngine.Object poseFolder = null;
		public UMABonePose newComponent = null;
		public string poseName = "";
		private Dictionary<UMABonePose, float> poseComponents = new Dictionary<UMABonePose, float>();
		private Dictionary<string, float> boneComponents = new Dictionary<string, float>();

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
			Transform newSkeleton = EditorGUILayout.ObjectField("Rig Prefab", skeleton, typeof(Transform), true) as Transform;
			if (skeleton != newSkeleton)
			{
				skeleton = newSkeleton;
				boneComponents = new Dictionary<string, float>();

				Transform[] transforms = UMABonePose.GetTransformsInPrefab(skeleton);
				foreach (Transform bone in transforms)
				{
					boneComponents.Add(bone.name, 1f);
				}
			}

			EditorGUILayout.Space();

			EditorGUILayout.LabelField("Component Poses");
			EditorGUI.indentLevel++;
			UMABonePose changedPose = null;
			UMABonePose deletedPose = null;
			float sliderVal = 0;
			List<string> activeBones = new List<string>();
			foreach (KeyValuePair<UMABonePose, float> entry in poseComponents)
			{
				GUILayout.BeginHorizontal();

				sliderVal = EditorGUILayout.Slider(entry.Key.name, entry.Value, 0f, 2f);
				if (sliderVal != entry.Value)
				{
					changedPose = entry.Key;
				}

				if (GUILayout.Button("-", GUILayout.Width(20f)))
				{
					deletedPose = entry.Key;
				}
				else
				{
					foreach (UMABonePose.PoseBone pose in entry.Key.poses)
					{
						if (!activeBones.Contains(pose.bone))
						{
							activeBones.Add(pose.bone);
						}
					}
				}
				GUILayout.EndHorizontal();
			}
			if (changedPose != null)
			{
				poseComponents[changedPose] = sliderVal;
			}
			if (deletedPose != null)
			{
				poseComponents.Remove(deletedPose);
			}

			GUILayout.BeginHorizontal();
			newComponent = EditorGUILayout.ObjectField(newComponent, typeof(UMABonePose), false) as UMABonePose;
			GUI.enabled = (newComponent != null);
			if (GUILayout.Button("+", GUILayout.Width(30f)))
			{
				poseComponents.Add(newComponent, 1f);
				newComponent = null;
			}
			GUI.enabled = true;
			GUILayout.EndHorizontal();
			EditorGUI.indentLevel--;

			EditorGUILayout.Space();

			EditorGUILayout.LabelField("Component Bones");
			EditorGUI.indentLevel++;
			foreach (string bone in activeBones)
			{
				GUILayout.BeginHorizontal();

				if (boneComponents.ContainsKey(bone))
				{
					boneComponents[bone] = EditorGUILayout.Slider(bone, boneComponents[bone], 0f, 2f);
				}

				GUILayout.EndHorizontal();
			}
			EditorGUI.indentLevel--;

			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Left"))
			{
				foreach (string bone in activeBones)
				{
					if (bone.Contains("Left") || bone.Contains("left"))
					{
						boneComponents[bone] = 1f;
					}
					else if (bone.Contains("Right") || bone.Contains("right"))
					{
						boneComponents[bone] = 0f;
					}
					else
					{
						boneComponents[bone] = 0.5f;
					}
				}
			}

			if (GUILayout.Button("Right"))
			{
				foreach (string bone in activeBones)
				{
					if (bone.Contains("Left") || bone.Contains("left"))
					{
						boneComponents[bone] = 0f;
					}
					else if (bone.Contains("Right") || bone.Contains("right"))
					{
						boneComponents[bone] = 1f;
					}
					else
					{
						boneComponents[bone] = 0.5f;
					}
				}
			}

			if (GUILayout.Button("Mirror"))
			{
				foreach (string bone in activeBones)
				{
					boneComponents[bone] = Mathf.Max(1f - boneComponents[bone], 0f);
				}

				if (poseName.EndsWith("_L"))
				{
					poseName = poseName.Substring(0, poseName.Length - 1) + "R";
				}
				else if (poseName.EndsWith("_R"))
				{
					poseName = poseName.Substring(0, poseName.Length - 1) + "L";
				}
			}
			GUILayout.EndHorizontal();

			EditorGUILayout.Space();

			poseFolder = EditorGUILayout.ObjectField("Pose Folder", poseFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;
			EnforceFolder(ref poseFolder);

			GUILayout.BeginHorizontal();
			poseName = EditorGUILayout.TextField("New Pose", poseName);
			if ((skeleton == null) || (poseFolder == null) || (poseComponents.Count < 1) || (poseName.Length < 1))
			{
				GUI.enabled = false;
			}
			if (GUILayout.Button("Build", GUILayout.Width(60f)))
			{
				string folderPath = AssetDatabase.GetAssetPath(poseFolder);

				UMABonePose newPose = CreatePoseAsset(folderPath, poseName);

				Transform[] sourceBones = UMABonePose.GetTransformsInPrefab(skeleton);

				foreach (string bone in activeBones)
				{
					Transform source = System.Array.Find<Transform>(sourceBones, entry => entry.name == bone);

					if (source != null)
					{
						Vector3 position = source.localPosition;
						Quaternion rotation = source.localRotation;
						Vector3 scale = source.localScale;
						bool include = false;

						foreach (KeyValuePair<UMABonePose, float> entry in poseComponents)
						{
							float strength = entry.Value * boneComponents[bone];

							if (strength > 0f)
							{
								foreach (UMABonePose.PoseBone pose in entry.Key.poses)
								{
									if (pose.bone == bone)
									{
										position += pose.position * strength;
										Quaternion posedRotation = rotation * pose.rotation;
										rotation = Quaternion.Slerp(rotation, posedRotation, strength);
										scale = Vector3.Slerp(scale, pose.scale, strength);
									}
								}

								include = true;
							}
						}

						if (include)
						{
							newPose.AddBone(source, position, rotation, scale, "");
						}
					}
					else
					{
						Debug.LogWarning("Bone not found in skeleton: " + bone);
					}
				}

				EditorUtility.SetDirty(newPose);
				AssetDatabase.SaveAssets();
			}
			GUI.enabled = true;

			GUILayout.EndHorizontal();
		}

		public static UMABonePose CreatePoseAsset(string assetFolder, string assetName)
		{
			if (!System.IO.Directory.Exists(assetFolder))
			{
				System.IO.Directory.CreateDirectory(assetFolder);
			}

			UMABonePose asset = ScriptableObject.CreateInstance<UMABonePose>();
			AssetDatabase.CreateAsset(asset, assetFolder + "/" + assetName + ".asset");
			return asset;
		}

		[MenuItem("UMA/Pose Tools/Bone Pose Mixer", priority = 1)]
		public static void OpenUMABonePoseBuildWindow()
		{
			EditorWindow win = EditorWindow.GetWindow(typeof(UMABonePoseMixerWindow));
			win.titleContent.text = "Pose Mixer";
        }
	}
}
#endif