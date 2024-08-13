//	============================================================
//	Name:		UMABonePoseBuildWindow
//	Author: 	Eli Curtz
//	Copyright:	(c) 2013 Eli Curtz
//	============================================================

#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace UMA.PoseTools
{
	public class UMABonePoseBuildWindow : EditorWindow
	{
		public Transform sourceSkeleton;
		public UnityEngine.Object poseFolder;
		private Transform poseSkeleton;
		private string skelPoseID;
		private bool skelOpen;
		private AnimationClip poseAnimation;

		public class AnimationPose
		{
			[XmlAttribute("ID")]
			public string ID = "";
			public int frame = 0;
		}

		private List<AnimationPose> poses;
		private bool animOpen;
		private Vector2 scrollPosition;

		public void SavePoseSet()
		{
			string folderPath = "";
			if (poseFolder != null)
			{
				folderPath = AssetDatabase.GetAssetPath(poseFolder);
			}
			else if (poseAnimation != null)
			{
				folderPath = AssetDatabase.GetAssetPath(poseAnimation);
				folderPath = folderPath.Substring(0, folderPath.LastIndexOf('/'));
			}

			string filePath = EditorUtility.SaveFilePanel("Save pose set", folderPath, poseAnimation.name + "_Poses.xml", "xml");

			if (filePath.Length != 0)
			{
				XmlSerializer serializer = new XmlSerializer(typeof(List<AnimationPose>));
				using (var stream = new FileStream(filePath, FileMode.Create))
				{
					serializer.Serialize(stream, poses);
				}
			}
		}

		public void LoadPoseSet()
		{
			string folderPath = "";
			if (poseFolder != null)
			{
				folderPath = AssetDatabase.GetAssetPath(poseFolder);
			}
			else if (poseAnimation != null)
			{
				folderPath = AssetDatabase.GetAssetPath(poseAnimation);
				folderPath = folderPath.Substring(0, folderPath.LastIndexOf('/'));
			}

			string filePath = EditorUtility.OpenFilePanel("Load pose set", folderPath, "xml");

			if (filePath.Length != 0)
			{
				XmlSerializer serializer = new XmlSerializer(typeof(List<AnimationPose>));
				using (var stream = new FileStream(filePath, FileMode.Open))
				{
					poses = serializer.Deserialize(stream) as List<AnimationPose>;
				}
			}
		}

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

			sourceSkeleton = EditorGUILayout.ObjectField("Base Prefab", sourceSkeleton, typeof(Transform), true) as Transform;

			poseFolder = EditorGUILayout.ObjectField("Pose Folder", poseFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;
			EnforceFolder(ref poseFolder);

			EditorGUILayout.Space();

			// Single pose from skeleton
			if (skelOpen = EditorGUILayout.Foldout(skelOpen, "Pose Source"))
			{
				EditorGUI.indentLevel++;
				poseSkeleton = EditorGUILayout.ObjectField("Pose Rig", poseSkeleton, typeof(Transform), false) as Transform;
				skelPoseID = EditorGUILayout.TextField("ID", skelPoseID);

				if ((sourceSkeleton == null) || (poseSkeleton == null) || (skelPoseID == null) || (skelPoseID.Length < 1))
				{
					GUI.enabled = false;
				}
				if (GUILayout.Button("Build Pose"))
				{
					string folderPath;

					if (poseFolder != null)
					{
						folderPath = AssetDatabase.GetAssetPath(poseFolder);
					}
					else
					{
						folderPath = AssetDatabase.GetAssetPath(poseAnimation);
						folderPath = folderPath.Substring(0, folderPath.LastIndexOf('/'));
					}

					UMABonePose bonePose = CreatePoseAsset(folderPath, skelPoseID);

					Transform[] sourceBones = UMABonePose.GetTransformsInPrefab(sourceSkeleton);
					Transform[] poseBones = UMABonePose.GetTransformsInPrefab(poseSkeleton);

					List<UMABonePose.PoseBone> poseList = new List<UMABonePose.PoseBone>();

					foreach (Transform bone in poseBones)
					{
						Transform source = System.Array.Find<Transform>(sourceBones, entry => entry.name == bone.name);
						if (source)
						{
							if ((bone.localPosition != source.localPosition) ||
								(bone.localRotation != source.localRotation) ||
								(bone.localScale != source.localScale))
							{
								UMABonePose.PoseBone poseB = new UMABonePose.PoseBone();
								poseB.bone = bone.name;
								poseB.position = bone.localPosition - source.localPosition;
								poseB.rotation = bone.localRotation * Quaternion.Inverse(source.localRotation);
								poseB.scale = new Vector3(bone.localScale.x / source.localScale.x,
														bone.localScale.y / source.localScale.y,
														bone.localScale.z / source.localScale.z);

								poseList.Add(poseB);
							}
						}
						else
						{
							Debug.Log("Unmatched bone: " + bone.name);
						}
					}

					bonePose.poses = poseList.ToArray();

					EditorUtility.SetDirty(bonePose);
					AssetDatabase.SaveAssets();
				}
				GUI.enabled = true;
				EditorGUI.indentLevel--;
			}

			EditorGUILayout.Space();

			// Multiple poses from animation frames
			if (animOpen = EditorGUILayout.Foldout(animOpen, "Animation Source"))
			{
				EditorGUI.indentLevel++;
				poseAnimation = EditorGUILayout.ObjectField("Pose Animation", poseAnimation, typeof(AnimationClip), false) as AnimationClip;
				if (poses == null)
				{
					poses = new List<AnimationPose>();
					poses.Add(new AnimationPose());
				}

				bool validPose = false;
				AnimationPose deletedPose = null;
				scrollPosition = GUILayout.BeginScrollView(scrollPosition);

				foreach (AnimationPose pose in poses)
				{
					GUILayout.BeginHorizontal();

					EditorGUILayout.LabelField("ID", GUILayout.Width(50f));
					pose.ID = EditorGUILayout.TextField(pose.ID);
					EditorGUILayout.LabelField("Frame", GUILayout.Width(60f));
					pose.frame = EditorGUILayout.IntField(pose.frame, GUILayout.Width(50f));
					if ((pose.ID != null) && (pose.ID.Length > 0))
					{
						validPose = true;
					}

					if (GUILayout.Button("-", GUILayout.Width(20f)))
					{
						deletedPose = pose;
						validPose = false;
						break;
					}
					GUILayout.EndHorizontal();
				}
				if (deletedPose != null)
				{
					poses.Remove(deletedPose);
				}

				GUILayout.EndScrollView();

				GUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("+", GUILayout.Width(30f)))
				{
					poses.Add(new AnimationPose());
				}
				GUILayout.EndHorizontal();

				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Load Pose Set"))
				{
					LoadPoseSet();
				}
				if (!validPose)
				{
					GUI.enabled = false;
				}
				if (GUILayout.Button("Save Pose Set"))
				{
					SavePoseSet();
				}
				GUI.enabled = true;
				GUILayout.EndHorizontal();

				if ((sourceSkeleton == null) || (poseAnimation == null) || (!validPose))
				{
					GUI.enabled = false;
				}

				if (GUILayout.Button("Build Poses"))
				{
					string folderPath;

					if (poseFolder != null)
					{
						folderPath = AssetDatabase.GetAssetPath(poseFolder);
					}
					else
					{
						folderPath = AssetDatabase.GetAssetPath(poseAnimation);
						folderPath = folderPath.Substring(0, folderPath.LastIndexOf('/'));
					}

					Transform[] sourceBones = UMABonePose.GetTransformsInPrefab(sourceSkeleton);
					EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(poseAnimation);
					Dictionary<string, Vector3> positions = new Dictionary<string, Vector3>();
					Dictionary<string, Quaternion> rotations = new Dictionary<string, Quaternion>();
					Dictionary<string, Vector3> scales = new Dictionary<string, Vector3>();

					foreach (AnimationPose pose in poses)
					{
						if ((pose.ID == null) || (pose.ID.Length < 1))
						{
							Debug.LogWarning("Bad pose identifier, not building for frame: " + pose.frame);
							continue;
						}

						float time = (float)pose.frame / poseAnimation.frameRate;
						if ((time < 0f) || (time > poseAnimation.length))
						{
							Debug.LogWarning("Bad frame number, not building for pose: " + pose.ID);
							continue;
						}

						positions.Clear();
						rotations.Clear();
						scales.Clear();

						foreach (EditorCurveBinding binding in bindings)
						{
							if (binding.type == typeof(Transform))
							{
								AnimationCurve curve = AnimationUtility.GetEditorCurve(poseAnimation, binding);
								float val = curve.Evaluate(time);
								Vector3 position;
								Quaternion rotation;
								Vector3 scale;

								switch (binding.propertyName)
								{
									case "m_LocalPosition.x":
										if (positions.TryGetValue(binding.path, out position))
										{
											position.x = val;
											positions[binding.path] = position;
										}
										else
										{
											position = new Vector3();
											position.x = val;
											positions.Add(binding.path, position);
										}
										break;
									case "m_LocalPosition.y":
										if (positions.TryGetValue(binding.path, out position))
										{
											position.y = val;
											positions[binding.path] = position;
										}
										else
										{
											position = new Vector3();
											position.y = val;
											positions.Add(binding.path, position);
										}
										break;
									case "m_LocalPosition.z":
										if (positions.TryGetValue(binding.path, out position))
										{
											position.z = val;
											positions[binding.path] = position;
										}
										else
										{
											position = new Vector3();
											position.z = val;
											positions.Add(binding.path, position);
										}
										break;

									case "m_LocalRotation.w":
										if (rotations.TryGetValue(binding.path, out rotation))
										{
											rotation.w = val;
											rotations[binding.path] = rotation;
										}
										else
										{
											rotation = new Quaternion();
											rotation.w = val;
											rotations.Add(binding.path, rotation);
										}
										break;
									case "m_LocalRotation.x":
										if (rotations.TryGetValue(binding.path, out rotation))
										{
											rotation.x = val;
											rotations[binding.path] = rotation;
										}
										else
										{
											rotation = new Quaternion();
											rotation.x = val;
											rotations.Add(binding.path, rotation);
										}
										break;
									case "m_LocalRotation.y":
										if (rotations.TryGetValue(binding.path, out rotation))
										{
											rotation.y = val;
											rotations[binding.path] = rotation;
										}
										else
										{
											rotation = new Quaternion();
											rotation.y = val;
											rotations.Add(binding.path, rotation);
										}
										break;
									case "m_LocalRotation.z":
										if (rotations.TryGetValue(binding.path, out rotation))
										{
											rotation.z = val;
											rotations[binding.path] = rotation;
										}
										else
										{
											rotation = new Quaternion();
											rotation.z = val;
											rotations.Add(binding.path, rotation);
										}
										break;

									case "m_LocalScale.x":
										if (scales.TryGetValue(binding.path, out scale))
										{
											scale.x = val;
											scales[binding.path] = scale;
										}
										else
										{
											scale = new Vector3();
											scale.x = val;
											scales.Add(binding.path, scale);
										}
										break;
									case "m_LocalScale.y":
										if (scales.TryGetValue(binding.path, out scale))
										{
											scale.y = val;
											scales[binding.path] = scale;
										}
										else
										{
											scale = new Vector3();
											scale.y = val;
											scales.Add(binding.path, scale);
										}
										break;
									case "m_LocalScale.z":
										if (scales.TryGetValue(binding.path, out scale))
										{
											scale.z = val;
											scales[binding.path] = scale;
										}
										else
										{
											scale = new Vector3();
											scale.z = val;
											scales.Add(binding.path, scale);
										}
										break;

									default:
										Debug.LogError("Unexpected property:" + binding.propertyName);
										break;
								}
							}
						}

						UMABonePose bonePose = CreatePoseAsset(folderPath, pose.ID);

						foreach (Transform bone in sourceBones)
						{
							string path = AnimationUtility.CalculateTransformPath(bone, sourceSkeleton.parent);
							Vector3 position;
							Quaternion rotation;
							Vector3 scale;
							if (!positions.TryGetValue(path, out position))
							{
								position = bone.localPosition;
							}
							if (!rotations.TryGetValue(path, out rotation))
							{
								rotation = bone.localRotation;
							}
							if (!scales.TryGetValue(path, out scale))
							{
								scale = bone.localScale;
							}

							if ((bone.localPosition != position) ||
								(bone.localRotation != rotation) ||
								(bone.localScale != scale))
							{
								bonePose.AddBone(bone, position, rotation, scale,"");
							}
						}

						EditorUtility.SetDirty(bonePose);
					}

					AssetDatabase.SaveAssets();
				}
				GUI.enabled = true;
				EditorGUI.indentLevel--;
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

		[MenuItem("UMA/Pose Tools/Bone Pose Builder", priority = 1)]
		public static void OpenUMABonePoseBuildWindow()
		{
			EditorWindow win = EditorWindow.GetWindow(typeof(UMABonePoseBuildWindow));

			win.titleContent.text = "Pose Builder";
        }
	}
}
#endif