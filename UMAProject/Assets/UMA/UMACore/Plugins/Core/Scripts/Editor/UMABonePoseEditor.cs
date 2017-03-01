using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections;
using System.Collections.Generic;

namespace UMA.PoseTools
{
    [CustomEditor(typeof(UMABonePose),true)]
    public class UMABonePoseEditor : Editor
    {
		UMABonePose targetPose = null;
		public UMABonePoseEditorContext context = null;
		public bool haveValidContext
		{
			get { return ((context != null) && (context.activeUMA != null)); }
		}

		private int drawBoneIndex = -1;
		private int editBoneIndex = -1;
		private int contextBoneIndex = -1;
		private int mirrorBoneIndex = -1;
		private bool mirrorActive = true;

		// HACK for testing
		public UMAData sourceUMA;

//		private bool inspectorLocked = false;

		private bool doBoneAdd = false;
		private bool doBoneRemove = false;
		private int removeBoneIndex = -1;
		private int addBoneIndex = -1;
		const int minBoneNameLength = 4;
		private string addBoneName = "";
		private string[] removeBoneOptions;
		private string[] addBoneOptions;

		private static Texture warningIcon;
//		private static Texture trashIcon;

        private string bonePoseFilter = "";

		private static GUIContent positionGUIContent = new GUIContent(
			LocalizationDatabase.GetLocalizedString("Position"),
			LocalizationDatabase.GetLocalizedString("The change in this bone's local position when pose is applied."));
		private static GUIContent rotationGUIContent = new GUIContent(
			LocalizationDatabase.GetLocalizedString("Rotation"),
			LocalizationDatabase.GetLocalizedString("The change in this bone's local rotation when pose is applied."));
		private static GUIContent scaleGUIContent = new GUIContent(
			LocalizationDatabase.GetLocalizedString("Scale"),
			LocalizationDatabase.GetLocalizedString("The change in this bone's local scale when pose is applied."));
		private static GUIContent scaleWarningGUIContent = new GUIContent(
			LocalizationDatabase.GetLocalizedString("WARNING: Non-uniform scale."),
			LocalizationDatabase.GetLocalizedString("Non-uniform scaling can cause errors on bones that are animated. Use only with adjustment bones."));
		private static GUIContent removeBoneGUIContent = new GUIContent(
			LocalizationDatabase.GetLocalizedString("Remove Bone"),
			LocalizationDatabase.GetLocalizedString("Remove the selected bone from the pose."));
		private static GUIContent addBoneGUIContent = new GUIContent(
			LocalizationDatabase.GetLocalizedString("Add Bone"),
			LocalizationDatabase.GetLocalizedString("Add the selected bone into the pose."));
		
		public void OnEnable()
		{
			targetPose = target as UMABonePose;
//			inspectorLocked = ActiveEditorTracker.sharedTracker.isLocked;
//			ActiveEditorTracker.sharedTracker.isLocked = true;
			SceneView.onSceneGUIDelegate += this.OnSceneGUI;

			if (warningIcon == null)
			{
				warningIcon = EditorGUIUtility.FindTexture("console.warnicon.sml");
			}
//			if (trashIcon == null)
//			{
//				trashIcon = EditorGUIUtility.FindTexture("TreeEditor.Trash");
//			}
		}

		public void OnDisable()
		{
			SceneView.onSceneGUIDelegate -= this.OnSceneGUI;
//			ActiveEditorTracker.sharedTracker.isLocked = inspectorLocked;
		}

		void OnSceneGUI(SceneView scene)
		{
			if (haveValidContext)
			{
				if (contextBoneIndex != editBoneIndex)
				{
					contextBoneIndex = -1;
					mirrorBoneIndex = -1;
					if ((editBoneIndex >= 0) && (editBoneIndex < targetPose.poses.Length))
					{
						int boneHash = targetPose.poses[editBoneIndex].hash;
						context.activeTransform = context.activeUMA.skeleton.GetBoneTransform(boneHash);
						if (context.activeTransform != null)
						{
							contextBoneIndex = editBoneIndex;
						}

						if (context.mirrorTransform != null)
						{
							int mirrorHash = UMASkeleton.StringToHash(context.mirrorTransform.name);
							for (int i = 0; i < targetPose.poses.Length; i++)
							{
								if (targetPose.poses[i].hash == mirrorHash)
								{
									mirrorBoneIndex = i;
									break;
								}
							}
						}
					}
					else
					{
						context.activeTransform = null;
					}

				}

//				EditorGUI.BeginChangeCheck( );
				Transform activeTrans = context.activeTransform;
				Transform mirrorTrans = context.mirrorTransform;
				if (activeTrans != null)
				{
					if (context.activeTransChanged)
					{
						scene.pivot = activeTrans.position;
						context.activeTransChanged = false;
					}

					if (context.activeTool == UMABonePoseEditorContext.EditorTool.Tool_Position)
					{
						Vector3 newPos = Handles.PositionHandle(activeTrans.position, activeTrans.rotation);
						if (newPos != activeTrans.position)
						{
							Vector3 newLocalPos = activeTrans.parent.InverseTransformPoint(newPos);
							Vector3 deltaPos = newLocalPos - activeTrans.localPosition;
	//						Debug.Log("Moved active bone by: " + localDelta);
							activeTrans.localPosition += deltaPos;

							if (mirrorTrans != null)
							{
								switch(context.mirrorPlane)
								{
									case UMABonePoseEditorContext.MirrorPlane.Mirror_X:
										deltaPos.x = -deltaPos.x;
										break;
									case UMABonePoseEditorContext.MirrorPlane.Mirror_Y:
										deltaPos.y = -deltaPos.y;
										break;
									case UMABonePoseEditorContext.MirrorPlane.Mirror_Z:
										deltaPos.z = -deltaPos.z;
										break;
								}

								mirrorTrans.localPosition += deltaPos;
							}
						}
					}

					if (context.activeTool == UMABonePoseEditorContext.EditorTool.Tool_Rotation)
					{
						Quaternion newRotation = Handles.RotationHandle(activeTrans.rotation, activeTrans.position);
						if (newRotation != activeTrans.rotation)
						{
							Quaternion deltaRot = Quaternion.Inverse(activeTrans.rotation) * newRotation;
							activeTrans.localRotation *= deltaRot;

							if (mirrorTrans != null)
							{
								switch(context.mirrorPlane)
								{
									case UMABonePoseEditorContext.MirrorPlane.Mirror_X:
										deltaRot.y = -deltaRot.y;
										deltaRot.z = -deltaRot.z;
										break;
									case UMABonePoseEditorContext.MirrorPlane.Mirror_Y:
										deltaRot.x = -deltaRot.x;
										deltaRot.z = -deltaRot.z;
										break;
									case UMABonePoseEditorContext.MirrorPlane.Mirror_Z:
										deltaRot.x = -deltaRot.x;
										deltaRot.y = -deltaRot.y;
										break;
								}

								mirrorTrans.localRotation *= deltaRot;
							}
						}
					}

					if (context.activeTool == UMABonePoseEditorContext.EditorTool.Tool_Scale)
					{
						Vector3 newScale = Handles.ScaleHandle(activeTrans.localScale, activeTrans.position, activeTrans.rotation, HandleUtility.GetHandleSize(activeTrans.position));
						if (newScale != activeTrans.localScale)
						{
							activeTrans.localScale = newScale;

							if (mirrorTrans != null)
							{
								mirrorTrans.localScale = activeTrans.localScale;
							}
						}
					}
				}

//				if( EditorGUI.EndChangeCheck( ) )
//				{
//					Undo.RecordObject( target, "Changed Look Target" );
//					t.lookTarget = lookTarget;
//					t.Update( );
//				}
			}


		}

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
			SerializedProperty poses = serializedObject.FindProperty("poses");

			if (doBoneAdd)
			{
				int addedIndex = poses.arraySize;
				poses.InsertArrayElementAtIndex(addedIndex);
				var pose = poses.GetArrayElementAtIndex(addedIndex);
				SerializedProperty bone = pose.FindPropertyRelative("bone");
				bone.stringValue = addBoneName;
				SerializedProperty position = pose.FindPropertyRelative("position");
				position.vector3Value = Vector3.zero;
				SerializedProperty rotation = pose.FindPropertyRelative("rotation");
				rotation.quaternionValue = Quaternion.identity;
				SerializedProperty scale = pose.FindPropertyRelative("scale");
				scale.vector3Value = Vector3.one;

				if (haveValidContext)
				{
					ArrayUtility.Remove(ref addBoneOptions, addBoneName);
				}
				ArrayUtility.Add(ref removeBoneOptions, addBoneName);

				addBoneIndex = 0;
				addBoneName = "";
				doBoneAdd = false;
			}
			if (doBoneRemove)
			{
				removeBoneIndex = 0;
				doBoneRemove = false;
			}

			// HACK
			sourceUMA = EditorGUILayout.ObjectField("Source UMA", sourceUMA, typeof(UMAData), true) as UMAData;
			if (sourceUMA != null)
			{
				if (context == null) {
					context = new UMABonePoseEditorContext();
				}
				if (context.activeUMA != sourceUMA)
				{
					context.activeUMA = sourceUMA;
				}
			}

//			string controlName = GUI.GetNameOfFocusedControl();
//			if ((controlName != null) && (controlName.Length > 0))
//				Debug.Log(controlName);

			if (removeBoneOptions == null)
			{
				List<string> removeList = new List<string>(targetPose.poses.Length + 1);
				removeList.Add(" ");
				for (int i = 0; i < targetPose.poses.Length; i++)
				{
					removeList.Add(targetPose.poses[i].bone);
				}

				removeBoneOptions = removeList.ToArray();
			}
			if (haveValidContext && (addBoneOptions == null))
			{
				List<string> addList = new List<string>(context.boneList);
				addList.Insert(0, " ");
				for (int i = 0; i < targetPose.poses.Length; i++)
				{
					addList.Remove(targetPose.poses[i].bone);
				}

				addBoneOptions = addList.ToArray();
			}

			// List of existing bones
			poses.isExpanded = EditorGUILayout.Foldout(poses.isExpanded, "Pose Bones ("+poses.arraySize+")");
			if (poses.isExpanded)
			{
				for (int i = 0; i < poses.arraySize; i++)
				{
					var pose = poses.GetArrayElementAtIndex(i);
					drawBoneIndex = i;
					PoseBoneDrawer(pose);
				}
			}

			GUILayout.Space(EditorGUIUtility.singleLineHeight);
			const float addRemovePadding = 20f;
			const float buttonVerticalOffset = 4f; // Can't be calculated because button layout is weird.

			// Controls for adding a new bone
			EditorGUILayout.BeginHorizontal();
			GUILayout.Space(addRemovePadding);
			if (haveValidContext)
			{
				EditorGUI.BeginDisabledGroup(addBoneIndex <= 0);
				if (GUILayout.Button(addBoneGUIContent, GUILayout.Width(90f)))
				{
					addBoneName = addBoneOptions[addBoneIndex];
					doBoneAdd = true;
				}
				EditorGUI.EndDisabledGroup();

				EditorGUILayout.BeginVertical();
				GUILayout.Space(buttonVerticalOffset);
				addBoneIndex = EditorGUILayout.Popup(addBoneIndex, addBoneOptions);
				EditorGUILayout.EndVertical();
			}
			else
			{
				EditorGUI.BeginDisabledGroup(addBoneName.Length < minBoneNameLength);
				if (GUILayout.Button(addBoneGUIContent, GUILayout.Width(90f)))
				{
					doBoneAdd = true;
				}
				EditorGUI.EndDisabledGroup();

				EditorGUILayout.BeginVertical();
				GUILayout.Space(buttonVerticalOffset);
				addBoneName = EditorGUILayout.TextField(addBoneName);
				EditorGUILayout.EndVertical();
			}
			GUILayout.Space(addRemovePadding);
			EditorGUILayout.EndHorizontal();

			// Controls for removing existing bone
			EditorGUILayout.BeginHorizontal();
			GUILayout.Space(addRemovePadding);
			EditorGUI.BeginDisabledGroup(removeBoneIndex <= 0);
			if (GUILayout.Button(removeBoneGUIContent, GUILayout.Width(90f)))
			{
				doBoneRemove = true;
			}
			EditorGUI.EndDisabledGroup();
			EditorGUILayout.BeginVertical();
			GUILayout.Space(buttonVerticalOffset);
			removeBoneIndex = EditorGUILayout.Popup(removeBoneIndex, removeBoneOptions);
			EditorGUILayout.EndVertical();
			GUILayout.Space(addRemovePadding);
			EditorGUILayout.EndHorizontal();

            serializedObject.ApplyModifiedProperties();
        }

        private void PoseBoneDrawer(SerializedProperty property)
        {
			EditorGUI.indentLevel++;

			SerializedProperty bone = property.FindPropertyRelative("bone");
			GUIContent boneGUIContent = new GUIContent(
				bone.stringValue,
				LocalizationDatabase.GetLocalizedString("The name of the bone being modified by pose."));
			EditorGUILayout.BeginHorizontal();
			bone.isExpanded = EditorGUILayout.Foldout(bone.isExpanded, boneGUIContent);
			Color currentColor = GUI.color;
			if (drawBoneIndex == editBoneIndex)
			{
				GUI.color = Color.green;
				if (GUILayout.Button(LocalizationDatabase.GetLocalizedString("Editing"), EditorStyles.miniButton, GUILayout.Width(60f)))
				{
					editBoneIndex = -1;
				}
			}
			else if (drawBoneIndex == mirrorBoneIndex)
			{
				Color lightBlue = Color.Lerp(Color.blue, Color.cyan, 0.66f);
				if (mirrorActive)
				{
					GUI.color = lightBlue;
					if (GUILayout.Button(LocalizationDatabase.GetLocalizedString("Mirroring"), EditorStyles.miniButton, GUILayout.Width(60f)))
					{
						mirrorActive = false;
					}
				}
				else
				{
					GUI.color = Color.Lerp(lightBlue, Color.white, 0.66f);
					if (GUILayout.Button(LocalizationDatabase.GetLocalizedString("Mirror"), EditorStyles.miniButton, GUILayout.Width(60f)))
					{
						mirrorActive = true;
					}
				}
			}
			else
			{
				if (GUILayout.Button(LocalizationDatabase.GetLocalizedString("Edit"), EditorStyles.miniButton, GUILayout.Width(60f)))
				{
					editBoneIndex = drawBoneIndex;
				}
			}
			GUI.color = currentColor;
			EditorGUILayout.EndHorizontal();

			if (bone.isExpanded)
			{
				EditorGUI.BeginDisabledGroup(drawBoneIndex != editBoneIndex);
				EditorGUI.indentLevel++;
//				GUI.SetNextControlName(bone.stringValue + "_position");
				int controlIDLow = GUIUtility.GetControlID(0, FocusType.Passive);
				EditorGUILayout.PropertyField(property.FindPropertyRelative("position"), positionGUIContent);
				int controlIDHigh = GUIUtility.GetControlID(0, FocusType.Passive);
				if ((GUIUtility.keyboardControl > controlIDLow) && (GUIUtility.keyboardControl < controlIDHigh))
				{
					if (context != null) context.activeTool = UMABonePoseEditorContext.EditorTool.Tool_Position;
				}

				// Show Euler angles for rotation
				SerializedProperty rotation = property.FindPropertyRelative("rotation");
				// Use BeginProperty() with fake rect to enable Undo but keep layout correct
				Rect rotationRect = new Rect(0, 0, 0, 0);
				EditorGUI.BeginProperty(rotationRect, GUIContent.none, rotation);

				Vector3 currentRotationEuler = ((Quaternion)rotation.quaternionValue).eulerAngles;
				Vector3 newRotationEuler = currentRotationEuler;
				EditorGUI.BeginChangeCheck();
//				GUI.SetNextControlName(bone.stringValue + "_rotation");
				controlIDLow = GUIUtility.GetControlID(0, FocusType.Passive);
				newRotationEuler = EditorGUILayout.Vector3Field(rotationGUIContent, newRotationEuler);
				controlIDHigh = GUIUtility.GetControlID(0, FocusType.Passive);
				if ((GUIUtility.keyboardControl > controlIDLow) && (GUIUtility.keyboardControl < controlIDHigh))
				{
					if (context != null) context.activeTool = UMABonePoseEditorContext.EditorTool.Tool_Rotation;
				}
				if (EditorGUI.EndChangeCheck())
				{
					if(newRotationEuler != currentRotationEuler)
					{
						rotation.quaternionValue = Quaternion.Euler(newRotationEuler);
					}

				}
				EditorGUI.EndProperty();

//				GUI.SetNextControlName(property.FindPropertyRelative("scale").displayName);
				SerializedProperty scaleProperty = property.FindPropertyRelative("scale");
				controlIDLow = GUIUtility.GetControlID(0, FocusType.Passive);
				EditorGUILayout.PropertyField(scaleProperty, scaleGUIContent);
				controlIDHigh = GUIUtility.GetControlID(0, FocusType.Passive);
				if ((GUIUtility.keyboardControl > controlIDLow) && (GUIUtility.keyboardControl < controlIDHigh))
				{
					if (context != null) context.activeTool = UMABonePoseEditorContext.EditorTool.Tool_Scale;
				}

				// Warn if there's a non-uniform scale
				Vector3 scaleValue = scaleProperty.vector3Value;
				if (!Mathf.Approximately(scaleValue.x, scaleValue.y) || !Mathf.Approximately(scaleValue.y, scaleValue.z))
				{
					EditorGUILayout.BeginHorizontal();
					GUILayout.Space(EditorGUIUtility.labelWidth / 2f);
					if (warningIcon != null)
					{
						scaleWarningGUIContent.image = warningIcon;
						EditorGUILayout.LabelField(scaleWarningGUIContent, GUILayout.MinHeight(warningIcon.height + 4f));
					}
					else
					{
						EditorGUILayout.LabelField(scaleWarningGUIContent);
					}
					EditorGUILayout.EndHorizontal();
				}
					
				EditorGUI.indentLevel--;
				EditorGUI.EndDisabledGroup();

			}

			EditorGUI.indentLevel--;
        }
    }
}
