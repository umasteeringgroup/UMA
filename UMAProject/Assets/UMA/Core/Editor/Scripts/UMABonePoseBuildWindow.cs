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
using System;

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

		[Serializable]
		public class AnimationPose
		{
			[XmlAttribute("ID")]
			public string ID = "";
			public int frame = 0;
		}

		private List<AnimationPose> poses;
		private bool animOpen;
		private Vector2 scrollPosition;
		public string debugBone = "Neck";
		public string debugPose = "NeckDown";
		public bool debugMode = false;


        // Persistence
        private const string PrefsKey = "UMA_UMABonePoseBuildWindow_State_v4";
		[System.Serializable]
		private class PersistedState
		{
			public string sourceSkeletonPath;
			public string poseFolderPath;
			public string poseSkeletonPath;
			public string poseAnimationPath;
			public string skelPoseID;
			public bool skelOpen;
			public bool animOpen;
			public float scrollX;
			public float scrollY;
			public List<AnimationPose> poses;
		}

		private void OnEnable()
		{
			LoadState();
			if (poses == null || poses.Count == 0)
			{
				poses = new List<AnimationPose> { new AnimationPose() };
			}
		}

		private void OnDisable()
		{
			SaveState();
		}

		public void SavePoseSet()
		{
			string folderPath = "";
			if (poseFolder != null)
				folderPath = AssetDatabase.GetAssetPath(poseFolder);
			else if (poseAnimation != null)
			{
				folderPath = AssetDatabase.GetAssetPath(poseAnimation);
				if (!string.IsNullOrEmpty(folderPath))
					folderPath = folderPath.Substring(0, folderPath.LastIndexOf('/'));
			}

			string defaultName = (poseAnimation != null ? poseAnimation.name : "PoseSet") + "_Poses.xml";
			string filePath = EditorUtility.SaveFilePanel("Save pose set", folderPath, defaultName, "xml");

			if (!string.IsNullOrEmpty(filePath))
			{
				XmlSerializer serializer = new XmlSerializer(typeof(List<AnimationPose>));
				using (var stream = new FileStream(filePath, FileMode.Create))
				{
					serializer.Serialize(stream, poses);
				}
				SaveState();
			}
		}

		public void LoadPoseSet()
		{
			string folderPath = "";
			if (poseFolder != null)
				folderPath = AssetDatabase.GetAssetPath(poseFolder);
			else if (poseAnimation != null)
			{
				folderPath = AssetDatabase.GetAssetPath(poseAnimation);
				if (!string.IsNullOrEmpty(folderPath))
					folderPath = folderPath.Substring(0, folderPath.LastIndexOf('/'));
			}

			string filePath = EditorUtility.OpenFilePanel("Load pose set", folderPath, "xml");
			if (!string.IsNullOrEmpty(filePath))
			{
				XmlSerializer serializer = new XmlSerializer(typeof(List<AnimationPose>));
				using (var stream = new FileStream(filePath, FileMode.Open))
				{
					poses = serializer.Deserialize(stream) as List<AnimationPose>;
				}
				if (poses == null || poses.Count == 0)
					poses = new List<AnimationPose> { new AnimationPose() };
				SaveState();
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
				else if (!Directory.Exists(destpath))
				{
					destpath = destpath.Substring(0, destpath.LastIndexOf('/'));
					folderObject = AssetDatabase.LoadMainAssetAtPath(destpath);
				}
			}
		}

		private bool debug = false;

		void OnGUI()
		{
			bool stateChanged = false;

			EditorGUI.BeginChangeCheck();
			sourceSkeleton = EditorGUILayout.ObjectField("Base Prefab", sourceSkeleton, typeof(Transform), true) as Transform;
			if (EditorGUI.EndChangeCheck()) stateChanged = true;

			EditorGUI.BeginChangeCheck();
			poseFolder = EditorGUILayout.ObjectField("Pose Folder", poseFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;
			if (EditorGUI.EndChangeCheck()) { EnforceFolder(ref poseFolder); stateChanged = true; }

			EditorGUILayout.Space();

			debug = EditorGUILayout.Foldout(debug, "Debug Options", true);
			if (debug)
			{
				debugMode = EditorGUILayout.Toggle("Enable Debug Mode", debugMode);
				debugBone = EditorGUILayout.TextField("Debug Bone", debugBone);
				debugPose = EditorGUILayout.TextField("Debug Pose", debugPose);
			}

			// Single pose from skeleton
			if (skelOpen = EditorGUILayout.Foldout(skelOpen, "Pose Source"))
			{
				EditorGUI.indentLevel++;
				EditorGUI.BeginChangeCheck();
				poseSkeleton = EditorGUILayout.ObjectField("Pose Rig", poseSkeleton, typeof(Transform), false) as Transform;
				if (EditorGUI.EndChangeCheck()) stateChanged = true;

				EditorGUI.BeginChangeCheck();
				skelPoseID = EditorGUILayout.TextField("ID", skelPoseID);
				if (EditorGUI.EndChangeCheck()) stateChanged = true;

				if (sourceSkeleton == null || poseSkeleton == null || string.IsNullOrEmpty(skelPoseID))
					GUI.enabled = false;

				if (GUILayout.Button("Build Pose"))
				{
					string folderPath;
					if (poseFolder != null)
						folderPath = AssetDatabase.GetAssetPath(poseFolder);
					else
					{
						folderPath = AssetDatabase.GetAssetPath(poseAnimation);
						if (!string.IsNullOrEmpty(folderPath))
							folderPath = folderPath.Substring(0, folderPath.LastIndexOf('/'));
					}

					UMABonePose bonePose = CreatePoseAsset(folderPath, skelPoseID);

					Transform[] sourceBones = UMABonePose.GetTransformsInPrefab(sourceSkeleton);
					Transform[] poseBones = UMABonePose.GetTransformsInPrefab(poseSkeleton);

					List<UMABonePose.PoseBone> poseList = new List<UMABonePose.PoseBone>();

					foreach (Transform bone in poseBones)
					{
						Transform source = System.Array.Find(sourceBones, entry => entry.name == bone.name);
						if (source)
						{
							if (bone.localPosition != source.localPosition ||
								bone.localRotation != source.localRotation ||
								bone.localScale != source.localScale)
							{
								var poseB = new UMABonePose.PoseBone
								{
									bone = bone.name,
									position = bone.localPosition - source.localPosition,
									rotation = bone.localRotation * Quaternion.Inverse(source.localRotation),
									scale = new Vector3(
										source.localScale.x != 0 ? bone.localScale.x / source.localScale.x : bone.localScale.x,
										source.localScale.y != 0 ? bone.localScale.y / source.localScale.y : bone.localScale.y,
										source.localScale.z != 0 ? bone.localScale.z / source.localScale.z : bone.localScale.z)
								};
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
					stateChanged = true; // Ensure persistence after build
				}
				GUI.enabled = true;
				EditorGUI.indentLevel--;
			}

			EditorGUILayout.Space();

			// Multiple poses from animation frames
			if (animOpen = EditorGUILayout.Foldout(animOpen, "Animation Source"))
			{
				EditorGUI.indentLevel++;
				EditorGUI.BeginChangeCheck();
				poseAnimation = EditorGUILayout.ObjectField("Pose Animation", poseAnimation, typeof(AnimationClip), false) as AnimationClip;
				if (EditorGUI.EndChangeCheck()) stateChanged = true;

				if (poses == null)
				{
					poses = new List<AnimationPose> { new AnimationPose() };
					stateChanged = true;
				}

				bool validPose = false;
				AnimationPose deletedPose = null;
				scrollPosition = GUILayout.BeginScrollView(scrollPosition);
				for (int i = 0; i < poses.Count; i++)
				{
					var pose = poses[i];
					GUILayout.BeginHorizontal();
					EditorGUI.BeginChangeCheck();
					EditorGUILayout.LabelField("ID", GUILayout.Width(50f));
					string newID = EditorGUILayout.TextField(pose.ID);
					EditorGUILayout.LabelField("Frame", GUILayout.Width(60f));
					int newFrame = EditorGUILayout.IntField(pose.frame, GUILayout.Width(50f));
					if (EditorGUI.EndChangeCheck())
					{
						pose.ID = newID;
						pose.frame = newFrame;
						stateChanged = true;
					}
					if (!string.IsNullOrEmpty(pose.ID)) validPose = true;

					if (GUILayout.Button("-", GUILayout.Width(20f)))
					{
						deletedPose = pose;
					}
					GUILayout.EndHorizontal();
				}
				if (deletedPose != null)
				{
					poses.Remove(deletedPose);
					stateChanged = true;
				}
				GUILayout.EndScrollView();

				GUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("+", GUILayout.Width(30f)))
				{
					poses.Add(new AnimationPose());
					stateChanged = true;
				}
				GUILayout.EndHorizontal();

				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Load Pose Set"))
				{
					LoadPoseSet();
					stateChanged = true;
				}
				if (!validPose) GUI.enabled = false;
				if (GUILayout.Button("Save Pose Set"))
				{
					SavePoseSet();
					stateChanged = true;
				}
				GUI.enabled = true;
				GUILayout.EndHorizontal();

				if (poseAnimation == null || !validPose)
					GUI.enabled = false;

				if (GUILayout.Button("Build Poses"))
				{
					string folderPath;
					if (poseFolder != null)
						folderPath = AssetDatabase.GetAssetPath(poseFolder);
					else
					{
						folderPath = AssetDatabase.GetAssetPath(poseAnimation);
						if (!string.IsNullOrEmpty(folderPath))
							folderPath = folderPath.Substring(0, folderPath.LastIndexOf('/'));
					}

					EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(poseAnimation);
					Transform[] sourceBones = sourceSkeleton != null ? UMABonePose.GetTransformsInPrefab(sourceSkeleton) : null;

					var basePositions = new Dictionary<string, Vector3>();
					var baseRotations = new Dictionary<string, Quaternion>();
					var baseScales = new Dictionary<string, Vector3>();
					CollectCurvesAtTime(poseAnimation, bindings, 0f, basePositions, baseRotations, baseScales);

					foreach (AnimationPose pose in poses)
					{
						if (string.IsNullOrEmpty(pose.ID))
						{
							Debug.LogWarning("Bad pose identifier, not building for frame: " + pose.frame);
							continue;
						}
						float time = pose.frame / poseAnimation.frameRate;
						if (time < 0f || time > poseAnimation.length)
						{
							Debug.LogWarning("Bad frame number, not building for pose: " + pose.ID);
							continue;
						}


						var positions = new Dictionary<string, Vector3>();
						var rotations = new Dictionary<string, Quaternion>();
						var scales = new Dictionary<string, Vector3>();
						CollectCurvesAtTime(poseAnimation, bindings, time, positions, rotations, scales);

						UMABonePose bonePose = CreatePoseAsset(folderPath, pose.ID);

						if (sourceBones != null)
						{
							foreach (Transform bone in sourceBones)
							{
								if (debugMode)
								{
									if (bone.name == debugBone && pose.ID == debugPose)
									{
										Debug.Log("Processing bone: " + bone.name);
										string dbgPath = AnimationUtility.CalculateTransformPath(bone, sourceSkeleton);

										Vector3 dbgBasePos;
										if (!basePositions.TryGetValue(dbgPath, out dbgBasePos))
										{
											dbgBasePos = bone.localPosition;
										}
										Quaternion dbgBaseRot;
										if (!baseRotations.TryGetValue(dbgPath, out dbgBaseRot))
										{
											dbgBaseRot = bone.localRotation;
										}
										Vector3 dbgBaseScale;
										if (!baseScales.TryGetValue(dbgPath, out dbgBaseScale))
										{
											dbgBaseScale = bone.localScale;
										}

										Vector3 dbgCurPos;
										if (!positions.TryGetValue(dbgPath, out dbgCurPos))
										{
											dbgCurPos = bone.localPosition;
										}
										Quaternion dbgCurRot;
										if (!rotations.TryGetValue(dbgPath, out dbgCurRot))
										{
											dbgCurRot = bone.localRotation;
										}
										Vector3 dbgCurScale;
										if (!scales.TryGetValue(dbgPath, out dbgCurScale))
										{
											dbgCurScale = bone.localScale;
										}

										Debug.Log("Base Pos: " + dbgBasePos + ", Cur Pos: " + dbgCurPos);
										Debug.Log("Base Rot: " + dbgBaseRot + ", Cur Rot: " + dbgCurRot);
										Debug.Log("Base Scale: " + dbgBaseScale + ", Cur Scale: " + dbgCurScale);
									}

								}

								string path = AnimationUtility.CalculateTransformPath(bone, sourceSkeleton);
								Vector3 basePos = basePositions.TryGetValue(path, out var bp) ? bp : bone.localPosition;
								Quaternion baseRot;
								if (baseRotations.TryGetValue(path, out var br))
								{
									baseRot = br;
								}
								else
								{
									baseRot = bone.localRotation;
								}
								Vector3 baseScale;
								if (baseScales.TryGetValue(path, out var bs))
								{
									baseScale = bs;
								}
								else
								{
									baseScale = bone.localScale;
								}

								Vector3 curPos;
								if (positions.TryGetValue(path, out var cp))
								{
									curPos = cp;
								}
								else
								{
									curPos = bone.localPosition;
								}
								Quaternion curRot;
								if (rotations.TryGetValue(path, out var cr))
								{
									curRot = cr;
								}
								else
								{
									curRot = bone.localRotation;
								}
								Vector3 curScale;
								if (scales.TryGetValue(path, out var cs))
								{
									curScale = cs;
								}
								else
								{
									curScale = bone.localScale;
								}

								bool posDif = false;

								if (Mathf.Abs(curPos.x - basePos.x) > 0.00001f) posDif = true;
								if (Mathf.Abs(curPos.y - basePos.y) > 0.00001f) posDif = true;
								if (Mathf.Abs(curPos.z - basePos.z) > 0.00001f) posDif = true;

								bool rotDif = false;
								if (Mathf.Abs(curRot.x - curRot.x) > 0.00001f) rotDif = true;
                                if (Mathf.Abs(curRot.y - curRot.y) > 0.00001f) rotDif = true;
                                if (Mathf.Abs(curRot.z - curRot.z) > 0.00001f) rotDif = true;
                                if (Mathf.Abs(curRot.w - curRot.w) > 0.00001f) rotDif = true;

                                //bool posDif = curPos != basePos;
                                //bool rotDif = curRot != baseRot;
                                bool scaleDif = curScale != baseScale;

								if (posDif || rotDif || scaleDif)
								{
									Vector3 deltaPos = curPos - basePos;
									Quaternion deltaRot = curRot * Quaternion.Inverse(baseRot);
									float deltaScaleX;
									if (baseScale.x != 0)
									{
										deltaScaleX = curScale.x / baseScale.x;
									}
									else
									{
										deltaScaleX = curScale.x;
									}
									float deltaScaleY;
									if (baseScale.y != 0)
									{
										deltaScaleY = curScale.y / baseScale.y;
									}
									else
									{
										deltaScaleY = curScale.y;
									}
									float deltaScaleZ;
									if (baseScale.z != 0)
									{
										deltaScaleZ = curScale.z / baseScale.z;
									}
									else
									{
										deltaScaleZ = curScale.z;
									}
									Vector3 deltaScale = new Vector3(deltaScaleX, deltaScaleY, deltaScaleZ);

									bonePose.AddBone(bone, deltaPos, deltaRot, deltaScale, "");
								}
							}
						}
						else
						{
							foreach (var kv in positions)
							{
								string path = kv.Key;
								int slash = path.LastIndexOf('/');
								string boneName = (slash >= 0) ? path.Substring(slash + 1) : path;

								Vector3 basePos = basePositions.TryGetValue(path, out var bp) ? bp : Vector3.zero;
								Quaternion baseRot = baseRotations.TryGetValue(path, out var br) ? br : Quaternion.identity;
								Vector3 baseScale = baseScales.TryGetValue(path, out var bs) ? bs : Vector3.one;

								Vector3 curPos = positions[path];
								Quaternion curRot = rotations.TryGetValue(path, out var cr) ? cr : baseRot;
								Vector3 curScale = scales.TryGetValue(path, out var cs) ? cs : baseScale;

								if (curPos != basePos || curRot != baseRot || curScale != baseScale)
								{
									var tempGO = new GameObject(boneName);
									try
									{
										Vector3 deltaPos = curPos - basePos;
										Quaternion deltaRot = curRot * Quaternion.Inverse(baseRot);
										Vector3 deltaScale = new Vector3(
											baseScale.x != 0 ? curScale.x / baseScale.x : curScale.x,
											baseScale.y != 0 ? curScale.y / baseScale.y : curScale.y,
											baseScale.z != 0 ? curScale.z / baseScale.z : curScale.z);

										bonePose.AddBone(tempGO.transform, deltaPos, deltaRot, deltaScale, "");
									}
									finally
									{
										DestroyImmediate(tempGO);
									}
								}
							}
						}

						EditorUtility.SetDirty(bonePose);
						AssetDatabase.SaveAssetIfDirty(bonePose);
						AssetDatabase.LoadAssetAtPath<UMABonePose>(AssetDatabase.GetAssetPath(bonePose));
                    }

					AssetDatabase.SaveAssets();
					stateChanged = true; // Persist after building
				}
				GUI.enabled = true;
				EditorGUI.indentLevel--;
			}

			// Persist lightweight UI state frequently
			if (Event.current.type == EventType.Repaint)
				SaveState();
		}

		private void CollectCurvesAtTime(AnimationClip clip, EditorCurveBinding[] bindings, float time,
			Dictionary<string, Vector3> positions, Dictionary<string, Quaternion> rotations, Dictionary<string, Vector3> scales)
		{
			foreach (EditorCurveBinding binding in bindings)
			{
				if (binding.type != typeof(Transform)) continue;
				AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
				if (curve == null) continue;
				float val = curve.Evaluate(time);

				switch (binding.propertyName)
				{
					case "m_LocalPosition.x":
						var px = positions.TryGetValue(binding.path, out var pvecx) ? pvecx : Vector3.zero;
						px.x = val; positions[binding.path] = px; break;
					case "m_LocalPosition.y":
						var py = positions.TryGetValue(binding.path, out var pvecy) ? pvecy : Vector3.zero;
						py.y = val; positions[binding.path] = py; break;
					case "m_LocalPosition.z":
						var pz = positions.TryGetValue(binding.path, out var pvecz) ? pvecz : Vector3.zero;
						pz.z = val; positions[binding.path] = pz; break;

					case "m_LocalRotation.x":
						var rx = rotations.TryGetValue(binding.path, out var rqx) ? rqx : new Quaternion();
						rx.x = val; rotations[binding.path] = rx; break;
					case "m_LocalRotation.y":
						var ry = rotations.TryGetValue(binding.path, out var rqy) ? rqy : new Quaternion();
						ry.y = val; rotations[binding.path] = ry; break;
					case "m_LocalRotation.z":
						var rz = rotations.TryGetValue(binding.path, out var rqz) ? rqz : new Quaternion();
						rz.z = val; rotations[binding.path] = rz; break;
					case "m_LocalRotation.w":
						var rw = rotations.TryGetValue(binding.path, out var rqw) ? rqw : new Quaternion();
						rw.w = val; rotations[binding.path] = rw; break;

					case "m_LocalScale.x":
						var sx = scales.TryGetValue(binding.path, out var svx) ? svx : Vector3.one;
						sx.x = val; scales[binding.path] = sx; break;
					case "m_LocalScale.y":
						var sy = scales.TryGetValue(binding.path, out var svy) ? svy : Vector3.one;
						sy.y = val; scales[binding.path] = sy; break;
					case "m_LocalScale.z":
						var sz = scales.TryGetValue(binding.path, out var svz) ? svz : Vector3.one;
						sz.z = val; scales[binding.path] = sz; break;
				}
			}
		}

		private void SaveState()
		{
			var state = new PersistedState
			{
				sourceSkeletonPath = GetAssetPath(sourceSkeleton),
				poseFolderPath = GetAssetPath(poseFolder),
				poseSkeletonPath = GetAssetPath(poseSkeleton),
				poseAnimationPath = GetAssetPath(poseAnimation),
				skelPoseID = skelPoseID,
				skelOpen = skelOpen,
				animOpen = animOpen,
				scrollX = scrollPosition.x,
				scrollY = scrollPosition.y,
				poses = poses
			};
			EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(state));
		}

		private void LoadState()
		{
			if (!EditorPrefs.HasKey(PrefsKey)) return;
			string json = EditorPrefs.GetString(PrefsKey, "");
			if (string.IsNullOrEmpty(json)) return;
			try
			{
				var state = JsonUtility.FromJson<PersistedState>(json);
				if (state == null) return;
				sourceSkeleton = LoadTransform(state.sourceSkeletonPath);
				poseFolder = LoadObject(state.poseFolderPath);
				poseSkeleton = LoadTransform(state.poseSkeletonPath);
				poseAnimation = LoadObject(state.poseAnimationPath) as AnimationClip;
				skelPoseID = state.skelPoseID;
				skelOpen = state.skelOpen;
				animOpen = state.animOpen;
				scrollPosition = new Vector2(state.scrollX, state.scrollY);
				if (state.poses != null) poses = state.poses;
			}
			catch { }
		}

		private static string GetAssetPath(UnityEngine.Object obj)
		{
			return obj == null ? null : AssetDatabase.GetAssetPath(obj);
		}

		private static Transform LoadTransform(string path)
		{
			if (string.IsNullOrEmpty(path)) return null;
			var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			return go ? go.transform : null;
		}

		private static UnityEngine.Object LoadObject(string path)
		{
			if (string.IsNullOrEmpty(path)) return null;
			return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
		}

		public static UMABonePose CreatePoseAsset(string assetFolder, string assetName)
		{
			if (!Directory.Exists(assetFolder))
				Directory.CreateDirectory(assetFolder);

			UMABonePose asset = ScriptableObject.CreateInstance<UMABonePose>();
			AssetDatabase.CreateAsset(asset, assetFolder + "/" + assetName + ".asset");
			AssetDatabase.SaveAssets();
			return asset;
		}

		[MenuItem("UMA/Pose Tools/Bone Pose Builder", priority = 1)]
		public static void OpenUMABonePoseBuildWindow()
		{
			EditorWindow win = GetWindow(typeof(UMABonePoseBuildWindow));
			win.titleContent.text = "Pose Builder";
		}
	}
}
#endif